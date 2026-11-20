#Requires -Version 5.1
<#
Archer Desktop Voice — Windows-only, opt-in by launching Archer.
No browser microphone gesture is needed. The microphone runs in the owner's
Windows session and uses installed Microsoft System.Speech recognizers.
Before "Hey Archer" only a two-word LOCAL wake grammar is enabled; no
ambient words are sent to Archer/OpenAI or saved. After wake, offline Windows
dictation supplies a user-requested transcript to the already-budgeted
localhost chat endpoint. The tray menu can Pause or Exit at any time.
No login autostart, service installation, elevated rights or cloud wake API.
#>
[CmdletBinding()]
param([string]$DataDir = '', [string]$SessionToken = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') { throw 'Desktop wake recognition requires Windows.' }
if (-not $DataDir) {
  if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA missing.' }
  $DataDir = Join-Path $env:LOCALAPPDATA 'Archer\Data'
}
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
$script:statusFile = Join-Path $DataDir 'desktop-voice-status.json'
$script:sessionFile = Join-Path $DataDir 'desktop-voice-session.txt'
$script:expectedSession = $SessionToken
$script:state = 'starting'
$script:detail = 'Preparing local Windows speech recognizer.'
$script:stopVoice = $false
$script:paused = $false
$script:mode = 'wake'
$script:pending = ''
$script:lastSpeech = [DateTime]::UtcNow
$script:lastActivity = [DateTime]::UtcNow
$script:lastHeartbeat = [DateTime]::MinValue
$script:lastHealth = [DateTime]::MinValue
$script:unhealthy = 0
$script:recognizer = $null
$script:synth = $null
$script:wakeGrammar = $null
$script:dictation = $null
$script:icon = $null
$script:mutex = $null
$script:mutexHeld = $false
$script:pauseItem = $null
$script:engineRunning = $false
$script:eventRegistration = $null
$script:quietSeconds = 2.0
$script:idleSeconds = 120.0
$script:base = 'http://127.0.0.1:5087'

function Write-VoiceStatus([string]$state,[string]$detail) {
  $script:state = $state
  $script:detail = $detail
  $data = [ordered]@{
    status = $state
    detail = $detail.Substring(0,[Math]::Min(200,$detail.Length))
    updatedUtc = [DateTime]::UtcNow.ToString('o')
    pid = $PID
  }
  $json = ConvertTo-Json -InputObject $data -Compress
  # Do not persist ambient microphone audio or transcribed speech.
  $temp = $script:statusFile + '.' + $PID + '.tmp'
  try {
    [IO.File]::WriteAllText($temp,$json,[Text.Encoding]::UTF8)
    Move-Item -LiteralPath $temp -Destination $script:statusFile -Force
  } catch {
    try { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue } catch {}
    # Transient file contention cannot be allowed to stop listening.
  }
  $script:lastHeartbeat = [DateTime]::UtcNow
}

function Refresh-Heartbeat {
  if (([DateTime]::UtcNow - $script:lastHeartbeat).TotalSeconds -ge 2) {
    Write-VoiceStatus $script:state $script:detail
  }
}

function Listen-For-Wake {
  $script:mode = 'wake'
  $script:pending = ''
  $script:dictation.Enabled = $false
  $script:wakeGrammar.Enabled = $true
  Write-VoiceStatus 'standby' 'Say Hey Archer. Windows is listening locally for the wake phrase.'
}

function Listen-For-Conversation {
  $script:mode = 'conversation'
  $script:pending = ''
  $script:wakeGrammar.Enabled = $false
  $script:dictation.Enabled = $true
  $script:lastActivity = [DateTime]::UtcNow
  Write-VoiceStatus 'awake' 'Awake. Talk normally for up to two minutes of inactivity.'
}

function Speak-Reply([string]$message) {
  $message = [string]$message
  if ([string]::IsNullOrWhiteSpace($message)) { return }
  if ($message.Length -gt 1700) { $message = $message.Substring(0,1700) }
  # Silence the recognizer's grammars during synthesis; this avoids having
  # Archer interpret his own voice as another command or wake phrase.
  $script:wakeGrammar.Enabled = $false
  $script:dictation.Enabled = $false
  Write-VoiceStatus 'speaking' 'Archer is replying through Windows audio.'
  try {
    $script:synth.Speak($message)
  } finally {
    $script:lastActivity = [DateTime]::UtcNow
    if (-not $script:paused -and -not $script:stopVoice) {
      if ($script:mode -eq 'conversation') {
        $script:dictation.Enabled = $true
        Write-VoiceStatus 'awake' 'Awake. Talk normally; two-minute idle timer has restarted.'
      } else {
        $script:wakeGrammar.Enabled = $true
        Write-VoiceStatus 'standby' 'Say Hey Archer to begin.'
      }
    }
  }
}

function Ask-Archer([string]$words) {
  $text = $words.Trim()
  if (-not $text) { return }
  if ($text.Length -gt 3000) { $text = $text.Substring(0,3000) }
  Write-VoiceStatus 'thinking' 'Archer is processing the latest spoken request.'
  try {
    # No mission creation/execution is enabled from unattended microphone
    # input. Approval and edits continue to require physical UI controls.
    if ($text -match '^(?:please\s+)?(?:create|add|delete|run|start|approve|execute|cancel)\s+(?:a\s+|my\s+|the\s+)?mission\b') {
      Speak-Reply 'That needs your approval in Mission Control. I can discuss it, but I will not execute it from the microphone.'
      return
    }
    if ($text -match '^(?:list|show)\s+(?:me\s+)?(?:my\s+)?missions[.?!]*$') {
      $result = Invoke-RestMethod -Method Post -Uri ($script:base + '/api/voice/mission-list') -ContentType 'application/json' -Body '' -TimeoutSec 15
      $answer = [string]$result.reply
    } else {
      $payload = @{message=$text;voiceMode=$true} | ConvertTo-Json -Compress
      $result = Invoke-RestMethod -Method Post -Uri ($script:base + '/api/chat') -ContentType 'application/json' -Body $payload -TimeoutSec 250
      $answer = [string]$result.answer
    }
    if ([string]::IsNullOrWhiteSpace($answer)) { $answer = 'I did not get a response.' }
    # Guard against written formatting in the local speech engine.
    $answer = $answer -replace 'https?://\S+', 'a link'
    $answer = $answer -replace '(?m)^\s{0,3}#{1,6}\s+', ''
    $answer = $answer -replace '(?m)^\s*(?:[-*•]|\d+[.)])\s+', ''
    $formatting = '[*_' + [char]96 + ']+'
    $answer = $answer -replace $formatting, ''
    $answer = $answer -replace '\s*[\r\n]+\s*', '; '
    Speak-Reply $answer
  } catch {
    Speak-Reply 'I cannot reach Archer right now. Check that the local app and its API are running.'
  }
}

function Check-Local-Server {
  try {
    $response = Invoke-WebRequest -Uri ($script:base+'/api/health') -TimeoutSec 3 -UseBasicParsing
    $script:unhealthy = 0
  } catch {
    $script:unhealthy++
    if ($script:unhealthy -ge 3) {
      Write-VoiceStatus 'stopped' 'The local Archer server closed. Desktop voice is stopping too.'
      $script:stopVoice = $true
    }
  }
}

try {
  Add-Type -AssemblyName System.Speech
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing

  $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $script:mutex = [System.Threading.Mutex]::new($false,('Local\ArcherDesktopVoice-' + $userSid))
  try { $script:mutexHeld = $script:mutex.WaitOne(0) }
  catch [System.Threading.AbandonedMutexException] { $script:mutexHeld = $true }
  if (-not $script:mutexHeld) { return } # desktop listener already active

  $script:icon = [System.Windows.Forms.NotifyIcon]::new()
  $script:icon.Icon = [System.Drawing.SystemIcons]::Application
  $script:icon.Text = 'Archer desktop voice'
  $menu = [System.Windows.Forms.ContextMenuStrip]::new()
  $script:pauseItem = $menu.Items.Add('Pause microphone')
  $exitItem = $menu.Items.Add('Exit Archer voice')
  $script:pauseItem.add_Click({ $script:paused = -not $script:paused })
  $exitItem.add_Click({ $script:stopVoice = $true })
  $script:icon.ContextMenuStrip = $menu
  $script:icon.Visible = $true
  $script:icon.BalloonTipTitle = 'Archer desktop voice'
  $script:icon.BalloonTipText = 'Local wake phrase listening starts with Archer. Right-click this tray icon to pause or exit.'
  $script:icon.ShowBalloonTip(3500)

  $available = @([System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() |
    Where-Object { $_.Culture.Name -like 'en-*' })
  if ($available.Count -eq 0) {
    Write-VoiceStatus 'error' 'No installed Windows English Speech Recognition engine. Install a Windows English speech language pack.'
    # Keep the error visible long enough to read the state in the UI.
    Start-Sleep -Seconds 8
    return
  }
  $info = $available | Where-Object { $_.Culture.Name -eq 'en-US' } | Select-Object -First 1
  if ($null -eq $info) { $info = $available[0] }
  $script:recognizer = [System.Speech.Recognition.SpeechRecognitionEngine]::new($info)

  $builder = [System.Speech.Recognition.GrammarBuilder]::new('hey archer')
  $builder.Culture = $info.Culture
  $script:wakeGrammar = [System.Speech.Recognition.Grammar]::new($builder)
  $script:wakeGrammar.Name = 'Archer wake phrase'
  $script:dictation = [System.Speech.Recognition.DictationGrammar]::new()
  $script:dictation.Name = 'Archer conversation'
  $script:recognizer.LoadGrammar($script:wakeGrammar)
  $script:recognizer.LoadGrammar($script:dictation)
  $script:dictation.Enabled = $false
  $script:recognizer.SetInputToDefaultAudioDevice()

  $script:synth = [System.Speech.Synthesis.SpeechSynthesizer]::new()
  $script:synth.Rate = 2 # Native SAPI rate scale approximates prior 1.25× browser pace.
  try {
    $british = @($script:synth.GetInstalledVoices() |
      Where-Object { $_.VoiceInfo.Culture.Name -eq 'en-GB' })
    if ($british.Count) {
      $male = $british | Where-Object { $_.VoiceInfo.Gender -eq [System.Speech.Synthesis.VoiceGender]::Male } | Select-Object -First 1
      if ($null -ne $male) { $script:synth.SelectVoice($male.VoiceInfo.Name) }
      else { $script:synth.SelectVoice($british[0].VoiceInfo.Name) }
    }
  } catch { } # Always fall back to installed local Windows speech voice.

  $script:eventRegistration = Register-ObjectEvent -InputObject $script:recognizer -EventName SpeechRecognized -SourceIdentifier 'ArcherDesktopVoiceRecognized'
  $script:recognizer.RecognizeAsync([System.Speech.Recognition.RecognizeMode]::Multiple)
  $script:engineRunning = $true
  Listen-For-Wake
  $script:lastHealth = [DateTime]::UtcNow

  while (-not $script:stopVoice) {
    if ($script:expectedSession -and (Test-Path -LiteralPath $script:sessionFile) -and
        (Get-Content -LiteralPath $script:sessionFile -Raw).Trim() -ne $script:expectedSession) {
      # A new Archer Windows binary has been launched. Give up the microphone
      # and named mutex so that release can start its own tray listener.
      $script:stopVoice = $true
      break
    }
    [System.Windows.Forms.Application]::DoEvents()
    $event = Wait-Event -SourceIdentifier 'ArcherDesktopVoiceRecognized' -Timeout 1
    if ($null -ne $event) {
      Remove-Event -EventIdentifier $event.EventIdentifier -ErrorAction SilentlyContinue
      if (-not $script:paused -and $null -ne $event.SourceEventArgs -and
          $null -ne $event.SourceEventArgs.Result) {
        $recognized = [string]$event.SourceEventArgs.Result.Text
        $confidence = [double]$event.SourceEventArgs.Result.Confidence
        if ($script:mode -eq 'wake') {
          if ($recognized -match '(?i)^hey\s+archer[.!?]*$' -and $confidence -ge 0.48) {
            Listen-For-Conversation
            [System.Media.SystemSounds]::Asterisk.Play()
          }
        } elseif ($script:mode -eq 'conversation' -and $confidence -ge 0.28 -and
                  -not [string]::IsNullOrWhiteSpace($recognized)) {
          $script:pending = ($script:pending + ' ' + $recognized).Trim()
          if ($script:pending.Length -gt 3000) {
            $script:pending = $script:pending.Substring(0,3000)
          }
          $script:lastSpeech = [DateTime]::UtcNow
          $script:lastActivity = $script:lastSpeech
          Write-VoiceStatus 'awake' 'Heard your speech. Waiting two seconds for a possible continuation.'
        }
      }
    }
    if ($script:paused) {
      if ($script:state -ne 'paused') {
        $script:wakeGrammar.Enabled = $false
        $script:dictation.Enabled = $false
        $script:pending = ''
        $script:pauseItem.Text = 'Resume microphone'
        Write-VoiceStatus 'paused' 'Microphone paused from the Archer tray icon.'
      }
    } elseif ($script:state -eq 'paused') {
      $script:pauseItem.Text = 'Pause microphone'
      Listen-For-Wake
    }
    if (-not $script:paused -and $script:mode -eq 'conversation') {
      if ($script:pending.Length -gt 0 -and
          ([DateTime]::UtcNow - $script:lastSpeech).TotalSeconds -ge $script:quietSeconds) {
        $command = $script:pending
        $script:pending = ''
        Ask-Archer $command
      } elseif ($script:pending.Length -eq 0 -and
                ([DateTime]::UtcNow - $script:lastActivity).TotalSeconds -ge $script:idleSeconds) {
        Listen-For-Wake
      }
    }
    if (([DateTime]::UtcNow - $script:lastHealth).TotalSeconds -ge 10) {
      $script:lastHealth = [DateTime]::UtcNow
      Check-Local-Server
    }
    Refresh-Heartbeat
  }
} catch {
  Write-VoiceStatus 'error' ('Windows microphone listener failed: ' + $_.Exception.Message)
  # Status remains visible in Mission Control even after listener exits.
} finally {
  if ($script:recognizer) {
    try {
      if ($script:engineRunning) { $script:recognizer.RecognizeAsyncCancel() }
      $script:recognizer.Dispose()
    } catch {}
  }
  if ($script:synth) { try { $script:synth.Dispose() } catch {} }
  if ($script:eventRegistration) {
    try { Unregister-Event -SourceIdentifier 'ArcherDesktopVoiceRecognized' -ErrorAction SilentlyContinue } catch {}
  }
  if ($script:icon) { try { $script:icon.Visible=$false; $script:icon.Dispose() } catch {} }
  if ($script:mutex) {
    if ($script:mutexHeld) { try { $script:mutex.ReleaseMutex() } catch {} }
    $script:mutex.Dispose()
  }
}
