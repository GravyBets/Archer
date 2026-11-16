'use strict';

/* Archer communications: opt-in, one configured recipient, no hidden calls. */
(() => {
  const el = id => document.getElementById(id);
  const sms = el('communicationsSmsTest');
  const call = el('communicationsCallTest');
  const daily = el('communicationsDailySms');
  const urgent = el('communicationsUrgentCalls');
  const saveButton = el('communicationsSaveButton');
  let busy = false;
  let loadedPrefs = false;

  function render(data) {
    const available = !!data.providerConfigured;
    el('communicationsProviderStatus').textContent = available ? 'CONFIGURED · OPT-IN' : 'NOT CONFIGURED';
    el('communicationsDestination').textContent = 'Recipient: ' + (data.recipient || 'not configured');
    sms.disabled = busy || !available;
    call.disabled = busy || !available;
    daily.disabled = busy;
    urgent.disabled = busy;
    saveButton.disabled = busy;
    if (!loadedPrefs) {
      daily.checked = !!data.preferences?.dailySmsEnabled;
      urgent.checked = !!data.preferences?.urgentCallsEnabled;
      loadedPrefs = true;
    }
    el('communicationsAttempts').replaceChildren();
    const attempts = data.attempts || [];
    if (!attempts.length) {
      const empty = document.createElement('p');
      empty.className = 'developer-empty';
      empty.textContent = 'No texts or calls attempted from this Archer instance.';
      el('communicationsAttempts').appendChild(empty);
    }
    for (const attempt of attempts) {
      const item = document.createElement('div');
      item.className = 'communications-attempt';
      const main = document.createElement('strong');
      main.textContent = attempt.channel + ' · ' + attempt.kind + ' · ' + attempt.status;
      const time = document.createElement('small');
      time.textContent = new Date(attempt.createdAtUtc).toLocaleString();
      item.appendChild(main);
      item.appendChild(time);
      el('communicationsAttempts').appendChild(item);
    }
  }

  async function refresh() {
    if (busy) return;
    try {
      const [communications, mobile] = await Promise.all([
        api('/api/communications'), api('/api/mobile/capabilities')
      ]);
      render(communications);
      el('mobileIngestStatus').textContent = mobile.enabled ? 'TOKEN GATE ENABLED' : 'DISABLED';
      el('mobileIngestDescription').textContent = mobile.enabled
        ? 'A local token-guarded endpoint is enabled. Archer is still localhost-only; do not expose its dashboard to the Internet.'
        : 'Mobile access is disabled. Archer cannot determine your iPhone location or activity automatically.';
    } catch (error) {
      el('communicationsTestStatus').textContent = 'Communications status unavailable: ' + error.message;
    }
  }

  async function test(type) {
    if (busy || appState.busy) return;
    const message = type === 'call'
      ? 'This places one real, potentially billable Twilio phone call to your configured number. Maximum one call per UTC day. Proceed?'
      : 'This sends one real, potentially billable SMS to your configured number. Maximum three SMS attempts per UTC day. Proceed?';
    if (!window.confirm(message)) return;
    busy = true;
    sms.disabled = true;
    call.disabled = true;
    el('communicationsTestStatus').textContent = 'Submitting a single ' + type + ' request…';
    try {
      const result = await api('/api/communications/test-' + type, {
        method: 'POST', body: JSON.stringify({ confirm: true })
      });
      el('communicationsTestStatus').textContent = result.message;
      notice(result.message);
    } catch (error) {
      el('communicationsTestStatus').textContent = 'Provider submission failed. No automatic retry. ' + error.message;
      notice(error.message, true);
    } finally {
      busy = false;
      await refresh();
    }
  }

  sms.addEventListener('click', () => test('sms'));
  call.addEventListener('click', () => test('call'));

  el('communicationsPreferencesForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || appState.busy) return;
    const enabledDaily = daily.checked;
    const enabledUrgent = urgent.checked;
    if (!window.confirm(
      'Confirm notification consent?\n\n' +
      'Daily SMS: ' + (enabledDaily ? 'ON' : 'OFF') +
      '\nUrgent availability calls: ' + (enabledUrgent ? 'ON' : 'OFF') +
      '\n\nIf enabled, Twilio may bill you. Archer sends to your single configured number only, while the PC app runs. No automatic retries.'))
      return;
    busy = true;
    saveButton.disabled = true;
    try {
      const response = await api('/api/communications/preferences', {
        method: 'POST',
        body: JSON.stringify({
          dailySmsEnabled: enabledDaily,
          urgentCallsEnabled: enabledUrgent,
          confirmCharges: true
        })
      });
      el('communicationsSaveStatus').textContent = response.message;
      notice(response.message);
    } catch (error) {
      el('communicationsSaveStatus').textContent = 'Settings not saved: ' + error.message;
      notice(error.message, true);
    } finally {
      busy = false;
      loadedPrefs = false;
      await refresh();
    }
  });

  window.ArcherCommunications = { refresh };
  refresh();
  window.setInterval(() => {
    if (document.hidden || appState.busy || el('operationsView').classList.contains('hidden')) return;
    refresh();
  }, 30000);
})();