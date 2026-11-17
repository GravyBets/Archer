'use strict';

/*
 * Archer Presence — browser-only optional voice and visual state.
 * No cloud wake word, outbound calls, recordings archive, or silent microphone access.
 * Speech recognition may be handled by the browser/vendor; don't dictate secrets.
 */
(() => {
  const el = id => document.getElementById(id);
  const stage = el('archerPresence');
  const mic = el('voiceInputButton');
  const orb = el('orbButton');
  const readToggle = el('voiceReadToggle');
  const stopButton = el('voiceStopButton');
  const replayButton = el('voiceReplayButton');
  const note = el('voiceSupportNote');
  const voicePicker = el('voiceChoiceSelect');
  const rateSlider = el('voiceRateSlider');
  const rateValue = el('voiceRateValue');
  const accentStatus = el('voiceAccentStatus');
  const input = el('chatText');
  const handsFreeToggle = el('voiceHandsFreeToggle');
  const pauseSlider = el('voicePauseSlider');
  const pauseValue = el('voicePauseValue');
  const endConversationButton = el('voiceEndConversation');

  const SpeechRecognitionCtor = window.SpeechRecognition || window.webkitSpeechRecognition;
  const hasSpeechOut = 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window;

  let recognition = null;
  let listening = false;
  let speaking = false;
  let hasFailures = false;
  let stoppedByUser = false;
  let initialTranscript = '';
  let carriedTranscript = '';
  let currentTranscript = '';
  let voiceSessionActive = false; // explicit Speak action only, never auto-start on page load
  let sendingVoiceTurn = false;
  let sendAfterRecognitionEnd = false;
  let waitingForVoiceReply = false;
  let silenceTimer = null;
  let restartTimer = null;
  let recognitionRestarts = 0;
  let recentVoiceError = false;
  let transientError = false;
  let lastReply = '';
  let availableVoices = [];
  let selectedVoiceUri = '';

  const getPreference = key => { try { return window.localStorage.getItem(key); } catch { return null; } };
  const setPreference = (key, value) => { try { window.localStorage.setItem(key, value); } catch { /* privacy mode */ } };
  readToggle.checked = getPreference('archer.voice.readAloud') === 'yes';
  handsFreeToggle.checked = getPreference('archer.voice.handsFree') === 'yes';
  const savedPause = Number(getPreference('archer.voice.pauseSeconds'));
  const pauseSeconds = Number.isInteger(savedPause) && savedPause >= 4 && savedPause <= 10
    ? savedPause : 7;
  pauseSlider.value = String(pauseSeconds);
  pauseValue.textContent = pauseSeconds + ' sec';
  pauseSlider.addEventListener('input', () => {
    const value = Math.min(10, Math.max(4, Number(pauseSlider.value) || 7));
    pauseValue.textContent = value + ' sec';
    setPreference('archer.voice.pauseSeconds', String(value));
    if (voiceSessionActive && recognizedVoiceText()) armSilenceTimer();
  });
  handsFreeToggle.addEventListener('change', () => {
    setPreference('archer.voice.handsFree', handsFreeToggle.checked ? 'yes' : 'no');
    if (!handsFreeToggle.checked) endVoiceConversation(false);
  });

  // Default to a conversational pace; keep the preference locally per browser.
  // Invalid/old preferences cannot push Web Speech rate beyond the offered range.
  const savedRate = Number(getPreference('archer.voice.rate'));
  const speakingRate = Number.isFinite(savedRate) && savedRate >= 0.80 && savedRate <= 1.65
    ? Math.round(savedRate * 20) / 20 : 1.25;
  rateSlider.value = String(speakingRate);
  rateValue.textContent = speakingRate.toFixed(2) + '×';
  rateSlider.addEventListener('input', () => {
    const value = Math.min(1.65, Math.max(0.80, Number(rateSlider.value) || 1.25));
    rateValue.textContent = value.toFixed(2) + '×';
    setPreference('archer.voice.rate', value.toFixed(2));
  });

  function statusText(value, hint = '') {
    el('presenceStatus').textContent = value;
    if (hint) el('presenceHint').textContent = hint;
  }

  function setState(value, hint = '') {
    stage.dataset.state = value;
    const descriptions = {
      idle: 'Standing by',
      listening: 'Listening',
      thinking: 'Thinking',
      responding: 'Responding',
      speaking: 'Speaking',
      alert: 'Attention needed',
      error: 'Something went wrong'
    };
    statusText(descriptions[value] || 'Standing by', hint);
    el('conversationMode').textContent = ({
      idle: 'TEXT READY', listening: 'MIC ACTIVE', thinking: 'PROCESSING',
      responding: 'REPLY READY', speaking: 'VOICE OUTPUT', alert: 'TASK ALERT', error: 'CHECK STATUS'
    })[value] || 'TEXT READY';
    mic.setAttribute('aria-pressed', listening || voiceSessionActive ? 'true' : 'false');
    mic.textContent = (listening || voiceSessionActive) ? '■ Stop listening' : '🎙 Speak';
    endConversationButton.disabled = !(voiceSessionActive || waitingForVoiceReply || listening);
  }

  function settle() {
    if (listening || speaking || appState.busy || transientError) return;
    setState(hasFailures ? 'alert' : 'idle',
      hasFailures ? 'A mission task failed. Open Attention for details.' :
        'Select the orb or type below to talk with Archer.');
  }

  function softError(message) {
    transientError = true;
    setState('error', message);
    window.setTimeout(() => {
      transientError = false;
      settle();
    }, 3800);
  }

  function voiceScore(voice) {
    const label = voice.name || '';
    const locale = (voice.lang || '').toLowerCase();
    if (locale !== 'en-gb') return -9999;
    let score = 100;
    if (/natural|neural|premium|online/i.test(label)) score += 45;
    if (/ryan|george|oliver|daniel|male/i.test(label)) score += 70;
    if (/hazel|sonia|libby|susan|female/i.test(label)) score -= 60;
    return score;
  }

  function chooseVoices() {
    if (!hasSpeechOut) return;
    const voices = window.speechSynthesis.getVoices();
    if (!voices.length) {
      accentStatus.textContent = 'Searching for installed British voices…';
      return;
    }
    availableVoices = voices;
    const best = voices.filter(v => (v.lang || '').toLowerCase() === 'en-gb')
      .sort((a, b) => voiceScore(b) - voiceScore(a));
    const saved = getPreference('archer.voice.uri');
    let chosen = voices.find(v => v.voiceURI === saved) || best[0];
    if (!chosen) chosen = voices.find(v => (v.lang || '').toLowerCase().startsWith('en')) || voices[0];
    selectedVoiceUri = chosen?.voiceURI || '';

    voicePicker.replaceChildren();
    for (const voice of [
      ...best, ...voices.filter(v => !best.includes(v) && (v.lang || '').toLowerCase().startsWith('en'))
    ]) {
      const option = document.createElement('option');
      option.value = voice.voiceURI;
      option.textContent = voice.name + ' (' + voice.lang + ')';
      voicePicker.appendChild(option);
    }
    if (!voicePicker.options.length) {
      const option = document.createElement('option');
      option.value = selectedVoiceUri;
      option.textContent = chosen.name + ' (' + chosen.lang + ')';
      voicePicker.appendChild(option);
    }
    voicePicker.value = selectedVoiceUri;
    const british = (chosen.lang || '').toLowerCase() === 'en-gb';
    const maleLikely = /ryan|george|oliver|daniel|male/i.test(chosen.name || '');
    accentStatus.textContent = british
      ? (maleLikely
        ? 'Selected a British voice that may sound masculine. Preview with Read last reply.'
        : 'British voice selected. Its gender and naturalness depend on your browser.')
      : 'No British English voice is installed. The selected fallback may sound different.';
  }

  voicePicker.addEventListener('change', () => {
    selectedVoiceUri = voicePicker.value;
    setPreference('archer.voice.uri', selectedVoiceUri);
    const selected = availableVoices.find(v => v.voiceURI === selectedVoiceUri);
    accentStatus.textContent = selected
      ? ((selected.lang || '').toLowerCase() === 'en-gb'
        ? 'Using British English: ' + selected.name
        : 'Not a British English voice: ' + selected.name)
      : 'Selected voice unavailable; system default will be used.';
  });

  function stopSpeech() {
    if (hasSpeechOut) window.speechSynthesis.cancel();
    speaking = false;
    stopButton.disabled = true;
    settle();
  }

  function say(text, forcePlayback = false) {
    if (text) lastReply = text;
    replayButton.disabled = !lastReply || !hasSpeechOut;
    if ((!readToggle.checked && !forcePlayback) || !hasSpeechOut || !text) {
      setState('responding', 'Archer replied. Read the conversation below.');
      window.setTimeout(settle, 1600);
      return;
    }
    stopSpeech();
    const clean = text.replace(/https?:\/\/\S+/g, 'a link').replace(/[*#_]+/g, '').trim();
    if (!clean) { settle(); return; }
    // Browser TTS engines can truncate very long utterances.
    const clipped = clean.length > 1800;
    const utterance = new SpeechSynthesisUtterance(clipped ? clean.slice(0, 1800) : clean);
    const chosen = availableVoices.find(v => v.voiceURI === selectedVoiceUri);
    if (chosen) {
      utterance.voice = chosen;
      utterance.lang = chosen.lang;
    } else {
      utterance.lang = 'en-GB';
    }
    utterance.rate = Math.min(1.65, Math.max(0.80, Number(rateSlider.value) || 1.25));
    utterance.pitch = 0.96;
    utterance.volume = 1;
    utterance.onstart = () => { speaking = true; stopButton.disabled = false; setState('speaking', 'Archer is reading the reply aloud.'); };
    utterance.onend = () => {
      speaking = false;
      stopButton.disabled = true;
      if (clipped) note.textContent = 'Only the first portion of a long reply was read; full text is shown above.';
      settle();
      if (waitingForVoiceReply && voiceSessionActive && handsFreeToggle.checked) {
        waitingForVoiceReply = false;
        window.setTimeout(() => {
          if (voiceSessionActive && !appState.busy && !document.hidden)
            beginVoiceTurn();
        }, 300);
      }
    };
    utterance.onerror = event => {
      speaking = false;
      stopButton.disabled = true;
      if (event.error === 'canceled' || event.error === 'interrupted') {
        settle();
        return;
      }
      if (voiceSessionActive) endVoiceConversation(false);
      softError('Browser voice playback failed. You can still read the reply.');
    };
    window.speechSynthesis.speak(utterance);
  }

  function clearSilenceTimer() {
    if (silenceTimer !== null) window.clearTimeout(silenceTimer);
    silenceTimer = null;
  }

  function clearRestartTimer() {
    if (restartTimer !== null) window.clearTimeout(restartTimer);
    restartTimer = null;
  }

  function recognizedVoiceText() {
    return [initialTranscript, carriedTranscript, currentTranscript]
      .filter(Boolean).join(' ').trim().slice(0, 3000);
  }

  function displayVoiceText() {
    input.value = recognizedVoiceText();
  }

  function commitCurrentTranscript() {
    if (currentTranscript.trim()) {
      carriedTranscript = [carriedTranscript, currentTranscript.trim()]
        .filter(Boolean).join(' ').trim().slice(0, 3000);
      currentTranscript = '';
    }
    displayVoiceText();
  }

  function armSilenceTimer() {
    clearSilenceTimer();
    if (!voiceSessionActive || !handsFreeToggle.checked || !recognizedVoiceText()) return;
    silenceTimer = window.setTimeout(() => {
      silenceTimer = null;
      if (!voiceSessionActive || appState.busy || !handsFreeToggle.checked ||
          !recognizedVoiceText()) return;
      // Wait for browser's last finalized recognition event, then auto-send
      // exactly one owner-spoken turn. No mission is executed by the mic.
      sendAfterRecognitionEnd = true;
      clearRestartTimer();
      if (listening) {
        try { recognition.stop(); }
        catch { finishRecognizedTurn(); }
      } else {
        finishRecognizedTurn();
      }
    }, Math.min(10, Math.max(4, Number(pauseSlider.value) || 7)) * 1000);
  }

  function stopRecognition() {
    stoppedByUser = true;
    clearRestartTimer();
    if (recognition && listening) {
      try { recognition.abort(); }
      catch { /* already ended */ }
    }
    listening = false;
  }

  function endVoiceConversation(stopAudio = true) {
    voiceSessionActive = false;
    waitingForVoiceReply = false;
    sendingVoiceTurn = false;
    sendAfterRecognitionEnd = false;
    clearSilenceTimer();
    stopRecognition();
    if (stopAudio) stopSpeech();
    settle();
    endConversationButton.disabled = true;
    mic.setAttribute('aria-pressed', 'false');
    mic.textContent = '🎙 Speak';
  }

  function finishRecognizedTurn() {
    if (!sendAfterRecognitionEnd) return;
    sendAfterRecognitionEnd = false;
    clearSilenceTimer();
    clearRestartTimer();
    commitCurrentTranscript();
    if (!voiceSessionActive || !handsFreeToggle.checked ||
        appState.busy || !input.value.trim()) return;
    sendingVoiceTurn = true;
    // This calls the same local /api/chat handler as pressing Send.
    // All existing model spending limits still apply.
    el('chatForm').requestSubmit();
  }

  function resumeRecognition() {
    if (!voiceSessionActive || listening || appState.busy || document.hidden ||
        sendAfterRecognitionEnd) return;
    if (recognitionRestarts >= 12) {
      endVoiceConversation(false);
      softError('Browser speech recognition kept stopping. Start again manually.');
      return;
    }
    recognitionRestarts += 1;
    currentTranscript = '';
    try { recognition.start(); }
    catch {
      endVoiceConversation(false);
      softError('Browser would not restart the microphone. Tap Speak to retry.');
    }
  }

  function beginVoiceTurn() {
    if (!recognition || appState.busy || document.hidden) return;
    initialTranscript = input.value.trim();
    carriedTranscript = '';
    currentTranscript = '';
    sendAfterRecognitionEnd = false;
    sendingVoiceTurn = false;
    recentVoiceError = false;
    recognitionRestarts = 0;
    stoppedByUser = false;
    clearSilenceTimer();
    clearRestartTimer();
    recognition.continuous = voiceSessionActive;
    recognition.interimResults = true;
    try { recognition.start(); }
    catch {
      endVoiceConversation(false);
      softError('The microphone could not start. Check browser microphone permissions.');
    }
  }

  function startListening() {
    if (!recognition) {
      input.focus();
      softError('Speech recognition is not available in this browser. Type your message instead.');
      return;
    }
    if (appState.busy) return;
    if (listening || voiceSessionActive) {
      endVoiceConversation();
      return;
    }
    stopSpeech();
    // Hands-free is opt-in and always starts with an explicit Speak/orb tap.
    voiceSessionActive = handsFreeToggle.checked;
    waitingForVoiceReply = false;
    if (voiceSessionActive && hasSpeechOut && !readToggle.checked) {
      readToggle.checked = true;
      setPreference('archer.voice.readAloud', 'yes');
    }
    beginVoiceTurn();
  }

  function prepareSend() {
    const wasVoiceTurn = voiceSessionActive && sendingVoiceTurn;
    clearSilenceTimer();
    clearRestartTimer();
    stopRecognition();
    sendAfterRecognitionEnd = false;
    sendingVoiceTurn = false;
    if (wasVoiceTurn) {
      waitingForVoiceReply = true;
    } else if (voiceSessionActive) {
      // Typing and manually sending does not silently resume the mic.
      endVoiceConversation(false);
    }
    return wasVoiceTurn;
  }

  if (SpeechRecognitionCtor) {
    recognition = new SpeechRecognitionCtor();
    recognition.lang = navigator.language || 'en-US';
    recognition.continuous = false;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;

    recognition.onstart = () => {
      listening = true;
      setState('listening', voiceSessionActive
        ? 'Hands-free: speak naturally; Archer waits for your selected pause.'
        : 'Your microphone is active. Review the transcript before sending.');
    };
    recognition.onresult = event => {
      // Rebuild from the complete results list to avoid duplicating text when
      // browsers revise a partial result or return multiple final phrases.
      let transcript = '';
      for (let i = 0; i < event.results.length; i++)
        transcript += (event.results[i][0]?.transcript || '') + ' ';
      const next = transcript.trim();
      if (next !== currentTranscript) {
        currentTranscript = next;
        displayVoiceText();
        if (voiceSessionActive && !sendAfterRecognitionEnd) {
          recognitionRestarts = 0;
          armSilenceTimer();
        }
      }
      input.focus({ preventScroll: true });
    };
    recognition.onerror = event => {
      if (stoppedByUser) return;
      const code = event.error || 'unknown';
      if (code === 'no-speech') return; // Browser sometimes ends after a pause.
      recentVoiceError = true;
      if (['not-allowed', 'service-not-allowed', 'audio-capture',
           'network', 'language-not-supported'].includes(code)) {
        endVoiceConversation(false);
        softError(code === 'not-allowed' || code === 'service-not-allowed'
          ? 'Microphone access was denied. Allow it in browser settings.'
          : 'Microphone service failed (' + code + '). Tap Speak when it is ready.');
      }
    };
    recognition.onend = () => {
      listening = false;
      commitCurrentTranscript();
      if (sendAfterRecognitionEnd) {
        finishRecognizedTurn();
        return;
      }
      if (stoppedByUser || !voiceSessionActive) {
        settle();
        return;
      }
      if (recentVoiceError) {
        endVoiceConversation(false);
        softError('Speech recognition ended unexpectedly. Start it again manually.');
        return;
      }
      // Web Speech engines may end on brief silence regardless of continuous.
      // Retry a bounded number of times while awaiting the longer pause.
      clearRestartTimer();
      restartTimer = window.setTimeout(() => {
        restartTimer = null;
        resumeRecognition();
      }, 300);
      setState('listening', 'Waiting through your pause… speak when ready.');
    };
  } else {
    mic.disabled = true;
    handsFreeToggle.disabled = true;
    pauseSlider.disabled = true;
    orb.title = 'Focus the Archer chat input';
    note.textContent = 'Speech recognition is unavailable in this browser. You can type to Archer.';
  }

  if (!hasSpeechOut) {
    readToggle.disabled = true;
    voicePicker.disabled = true;
    rateSlider.disabled = true;
    accentStatus.textContent = 'Spoken playback is unavailable in this browser.';
    readToggle.checked = false;
    if (recognition) note.textContent = 'Speech-to-text available. Speech playback is unavailable in this browser.';
  }

  if (hasSpeechOut) {
    chooseVoices();
    window.speechSynthesis.addEventListener?.('voiceschanged', chooseVoices);
  }
  endConversationButton.addEventListener('click', () => endVoiceConversation());
  mic.addEventListener('click', startListening);
  orb.addEventListener('click', () => {
    if (recognition) startListening();
    else input.focus();
  });
  stopButton.addEventListener('click', () => {
    if (voiceSessionActive) endVoiceConversation();
    else stopSpeech();
  });
  replayButton.addEventListener('click', () => { if (lastReply) say(lastReply, true); });
  readToggle.addEventListener('change', () => {
    setPreference('archer.voice.readAloud', readToggle.checked ? 'yes' : 'no');
    if (!readToggle.checked) stopSpeech();
  });

  input.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      el('chatForm').requestSubmit();
    }
  });

  function update(data) {
    const missions = data.missions || [];
    const active = missions.filter(m => m.status === 'Running' || m.status === 'Ready').length;
    const today = new Date().toDateString();
    const completedToday = missions.flatMap(m => m.tasks || []).filter(t =>
      t.status === 'Completed' && t.completedAtUtc &&
      new Date(t.completedAtUtc).toDateString() === today).length;
    const failed = missions.flatMap(m => (m.tasks || []).filter(t => t.status === 'Failed')
      .map(t => ({ missionId: m.id, mission: m.title, title: t.title, error: t.lastError || '' })));
    hasFailures = failed.length > 0;

    el('presenceActiveMissions').textContent = active;
    el('presenceTasksToday').textContent = completedToday;
    el('presenceAttentionCount').textContent = failed.length;
    el('attentionIssueCount').textContent = failed.length;
    el('attentionNavCount').textContent = failed.length;
    el('attentionNavCount').classList.toggle('hidden', failed.length === 0);

    const escape = s => String(s ?? '').replace(/[&<>"']/g,
      c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

    el('attentionFailures').innerHTML = failed.length
      ? failed.map(x => '<button type="button" class="attention-item" data-open-mission="' +
        escape(x.missionId) + '"><strong>' + escape(x.mission) + ': ' + escape(x.title) +
        '</strong><small>' + escape(x.error || 'Task failed. Open Mission Control to review or retry.') +
        '</small></button>').join('')
      : '<div class="attention-empty"><strong>No failed mission tasks recorded.</strong>' +
        'This only reflects Archer’s local mission history, not external service health or business incidents.</div>';
    settle();
  }

  window.ArcherPresence = {
    setState,
    update,
    reply: say,
    settle,
    stopSpeech,
    stopListening: endVoiceConversation,
    prepareSend,
    failure: message => {
      endVoiceConversation(false);
      softError(message);
    }
  };
  update(appState.data);
  settle();

  // Do not leave an opted-in microphone running in a background tab.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) endVoiceConversation();
  });
  window.addEventListener('beforeunload', () => {
    endVoiceConversation(false);
    if (hasSpeechOut) window.speechSynthesis.cancel();
  });
})();