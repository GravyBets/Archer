'use strict';

/*
 * Passive display bridge for the Windows SAPI tray listener. This script
 * never requests browser microphone permission, starts recognition, or
 * sends speech to AI. Only owner-launched Archer-Voice.ps1 handles audio.
 */
(() => {
  let updating = false;
  let visibleLastTurn = '';
  const snapshotKey = data => {
    const entries = Array.isArray(data?.conversation) ? data.conversation : [];
    const last = entries[entries.length - 1];
    return String(entries.length) + ':' + (last?.role || '') + ':' + (last?.content || '').slice(-250);
  };
  const poll = async () => {
    if (updating) return;
    updating = true;
    try {
      const response = await fetch('/api/desktop-voice/status', {cache:'no-store'});
      if (!response.ok) throw Error('Local desktop voice status unavailable');
      const status = await response.json();
      window.ArcherPresence?.setDesktopVoiceStatus?.(status);
      if (status.running && !document.hidden && !appState.busy) {
        const stateResponse = await fetch('/api/state', {cache:'no-store'});
        if (stateResponse.ok) {
          const state = await stateResponse.json();
          const key = snapshotKey(state);
          if (!visibleLastTurn) visibleLastTurn = snapshotKey(appState.data);
          if (key !== visibleLastTurn) {
            visibleLastTurn = key;
            appState.data = state;
            render();
          }
        }
      }
    } catch {
      window.ArcherPresence?.setDesktopVoiceStatus?.({
        running:false, status:'offline',
        detail:'Desktop voice could not be detected.'
      });
    } finally {
      updating = false;
    }
  };
  poll();
  window.setInterval(poll, 3000);
})();