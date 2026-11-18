'use strict';

/*
 * Explicitly opt-in WebRTC realtime speech. The standard OPENAI_API_KEY or
 * dedicated provider API key never reaches the browser. The localhost broker
 * uses a separately budgeted model key and conservatively counts initiations.
 * Browser 4 minute auto-stop is NOT a guaranteed provider-side cost limit.
 * Real-time voice may use two narrowly scoped local mission tools. Only
 * draft creation is permitted and only after real on-screen owner confirmation.
 * No planning, executing, code changes, payments or telephony.
 * Audio/transcripts are not saved to Archer conversation JSON by this module.
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
  let approvedSession = null;
  let handledCalls = new Set();
  let toolCalls = 0;
  const SESSION_LIMIT_MS = 4 * 60 * 1000;

  function announce(message) { status.textContent = message; }

  function release() {
    if (approvedSession) approvedSession.abort();
    approvedSession = null;
    handledCalls.clear();
    toolCalls = 0;
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

  // Function call results are sent only to the active Realtime session.
  // Model instructions cannot authorize a mutation: the local tool performs
  // its own physical on-screen confirmation before a draft can be created.
  async function finishFunctionCalls(items, activeConnection) {
    if (!Array.isArray(items) || connection !== activeConnection ||
        channel?.readyState !== 'open') return;
    const outputs = [];
    for (const call of items) {
      if (call?.type !== 'function_call') continue;
      const callId = call.call_id;
      if (typeof callId !== 'string' || !/^[a-zA-Z0-9_-]{1,32}$/.test(callId) ||
          handledCalls.has(callId)) continue;
      handledCalls.add(callId);
      toolCalls += 1;
      let result = {status: 'rejected', message: 'Unsupported mission request.'};
      if (toolCalls <= 3 && typeof call.arguments === 'string' &&
          call.arguments.length <= 2500) {
        try {
          const args = JSON.parse(call.arguments);
          if (args && typeof args === 'object' && !Array.isArray(args)) {
            if (call.name === 'list_local_missions' && Object.keys(args).length === 0)
              result = await window.ArcherVoiceMissions.list();
            else if (call.name === 'propose_local_mission' &&
                     Object.keys(args).length === 2 &&
                     typeof args.title === 'string' &&
                     typeof args.objective === 'string') {
              result = await window.ArcherVoiceMissions.propose(
                args.title, args.objective, {signal: approvedSession?.signal});
            }
          }
        } catch {
          result = {status: 'error', message: 'Mission operation unavailable; no unapproved action was taken.'};
        }
      }
      if (connection !== activeConnection || approvedSession?.signal.aborted ||
          channel?.readyState !== 'open') return;
      outputs.push({
        type: 'conversation.item.create',
        item: {type: 'function_call_output', call_id: callId, output: JSON.stringify(result)}
      });
    }
    if (outputs.length === 0) return;
    for (const output of outputs) channel.send(JSON.stringify(output));
    channel.send(JSON.stringify({type: 'response.create'}));
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
      'Voice goes to the AI provider. Mission titles/status may be shared ' +
      'if you request a status check. A mission draft needs on-screen approval. ' +
      'No mission execution, code changes or payments are permitted. Continue?')) return;

    connecting = true;
    approvedSession = new AbortController();
    handledCalls.clear();
    toolCalls = 0;
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
          if (data.type === 'response.done') {
            announce('Your turn. Take your time before speaking.');
            // The completed response contains the authoritative function-call
            // items. Do not process partial arguments or arbitrary event data.
            void finishFunctionCalls(data.response?.output, activeConnection);
          }
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
