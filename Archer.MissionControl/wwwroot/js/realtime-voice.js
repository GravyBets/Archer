'use strict';

/*
 * Explicitly opt-in WebRTC realtime speech. The standard OPENAI_API_KEY or
 * dedicated provider API key never reaches the browser. The localhost broker
 * uses a separately budgeted model key and conservatively counts initiations.
 * Browser 4 minute auto-stop is NOT a guaranteed provider-side cost limit.
 * Real-time voice is a separate, no-tools conversation; no audio is recorded
 * or saved in local Archer's conversation JSON by this module.
 */
(() => {
  const $ = id => document.getElementById(id);
  const start = $('realtimeVoiceStart');
  const end = $('realtimeVoiceEnd');
  const status = $('realtimeVoiceStatus');
  const manualMic = $('voiceInputButton');
  let connection = null;
  let microphone = null;
  let channel = null;
  let speaker = null;
  let stopTimer = null;
  let connecting = false;
  let manualWasDisabled = false;
  const SESSION_LIMIT_MS = 4 * 60 * 1000;

  function announce(message) { status.textContent = message; }

  function release() {
    if (stopTimer !== null) window.clearTimeout(stopTimer);
    stopTimer = null;
    try { channel?.close(); } catch { /* cleanup */ }
    channel = null;
    if (microphone) microphone.getTracks().forEach(track => track.stop());
    microphone = null;
    if (speaker) {
      speaker.pause();
      speaker.srcObject = null;
      speaker.remove();
    }
    speaker = null;
    const oldConnection = connection;
    connection = null;
    if (oldConnection) {
      try { oldConnection.close(); } catch { /* cleanup */ }
    }
    manualMic.disabled = manualWasDisabled;
    connecting = false;
    end.disabled = true;
    start.disabled = false;
  }

  function stopSession(reason = 'Live conversation ended. No transcript was saved to Archer.') {
    // Important: page hide, user Stop and connection failure must all release
    // mic hardware AND close the model connection.
    release();
    announce(reason);
  }

  async function initialize() {
    if (!window.RTCPeerConnection || !navigator.mediaDevices?.getUserMedia) {
      announce('Realtime WebRTC audio is not supported in this browser.');
      start.disabled = true;
      return;
    }
    try {
      const result = await fetch('/api/realtime-voice/status', {cache: 'no-store'});
      const config = await result.json();
      start.disabled = !config.enabled;
      announce(config.note || 'Realtime voice is unavailable.');
      if (config.enabled) start.title =
        'Separate billable voice session; recommended 4-minute maximum per call.';
    } catch {
      announce('Could not check realtime voice configuration. Normal Archer chat remains available.');
      start.disabled = true;
    }
  }

  async function startSession() {
    if (connecting || connection || start.disabled) return;
    if (document.hidden) return;
    // Paid audio is independent of the usual Responses usage ledger. The
    // owner must approve every individual session until independent real
    // cost accounting, termination and a hard provider budget are verified.
    if (!window.confirm(
      'Start a billable realtime voice session?\n\n' +
      'Uses your separately budgeted Realtime API key (NOT covered by the ' +
      'normal Archer chat cost ledger). Recommended maximum 4 minutes, ' +
      'with an automatic browser stop that is NOT a guaranteed billing cap.\n\n' +
      'Voice goes directly to the AI provider. Archer cannot run missions ' +
      'or change code in this live session. Continue?')) return;

    connecting = true;
    start.disabled = true;
    end.disabled = false;
    announce('Opening microphone and establishing encrypted WebRTC audio…');

    // Avoid two microphone recognition systems or two voices operating at
    // the same time. The original browser-handsfree mode is independent.
    window.ArcherPresence?.stopListening();
    window.ArcherPresence?.stopSpeech();
    manualWasDisabled = manualMic.disabled;
    manualMic.disabled = true;

    try {
      microphone = await navigator.mediaDevices.getUserMedia({
        audio: {echoCancellation: true, noiseSuppression: true, autoGainControl: true}
      });
      if (!connecting) {
        microphone.getTracks().forEach(track => track.stop());
        microphone = null;
        return; // user cancelled during microphone permission prompt
      }
      connection = new RTCPeerConnection();
      const activeConnection = connection;
      speaker = document.createElement('audio');
      speaker.autoplay = true;
      speaker.style.display = 'none';
      document.body.appendChild(speaker);
      connection.ontrack = event => {
        if (connection !== activeConnection || !speaker) return;
        speaker.srcObject = event.streams[0];
        speaker.play().catch(() => {
          announce('Audio autoplay was blocked. Check speaker permission or try again.');
        });
      };
      for (const track of microphone.getAudioTracks())
        connection.addTrack(track, microphone);
      channel = connection.createDataChannel('oai-events');
      channel.onmessage = event => {
        try {
          const data = JSON.parse(event.data);
          if (data.type === 'input_audio_buffer.speech_started')
            announce('Listening. You can interrupt Archer while he speaks.');
          if (data.type === 'response.created')
            announce('Archer is responding… speak to interrupt.');
          if (data.type === 'response.done')
            announce('Your turn. Take your time before speaking.');
          if (data.type === 'error')
            announce('Realtime audio reported an error. End the call and review your API configuration.');
        } catch { /* data channel messages are not trusted HTML */ }
      };
      connection.onconnectionstatechange = () => {
        if (connection === activeConnection &&
            ['failed', 'closed'].includes(connection.connectionState))
          stopSession('Realtime voice connection closed. Start again if needed.');
      };

      const offer = await connection.createOffer();
      await connection.setLocalDescription(offer);
      if (!connecting) return;
      const response = await fetch('/api/realtime-voice/call', {
        method: 'POST',
        headers: {'Content-Type': 'application/sdp'},
        body: connection.localDescription.sdp,
        cache: 'no-store'
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        throw new Error(problem.error || 'Realtime session request failed.');
      }
      const answer = await response.text();
      if (!answer.startsWith('v=0')) throw new Error('Invalid realtime connection answer.');
      if (!connecting) return;
      await connection.setRemoteDescription({type:'answer',sdp:answer});
      if (!connecting) return;
      announce('Realtime conversation connected. Speak normally; semantic turn detection waits through short pauses.');
      stopTimer = window.setTimeout(() => stopSession(
        'The four-minute local trial limit elapsed. You can deliberately start another call.'
      ), SESSION_LIMIT_MS);
    } catch (error) {
      stopSession(error?.name === 'NotAllowedError'
        ? 'Microphone permission denied. Realtime voice was not started.'
        : 'Realtime voice failed: ' + (error?.message || 'Unknown error.'));
    }
  }

  start.addEventListener('click', startSession);
  end.addEventListener('click', () => stopSession());
  document.addEventListener('visibilitychange', () => {
    if (document.hidden && (connection || connecting))
      stopSession('The page was hidden; realtime audio and microphone were stopped.');
  });
  window.addEventListener('pagehide', () => release());
  // No automatic microphone access or provider call during page load.
  void initialize();
})();
