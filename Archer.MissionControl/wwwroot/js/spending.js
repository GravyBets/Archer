'use strict';

/* Archer spend guard — local preflight limits, not an OpenAI billing control. */
(() => {
  const el = id => document.getElementById(id);
  let initialized = false;
  let saving = false;
  const money = n => '$' + Number(n || 0).toFixed(4);

  function render(data) {
    el('spendCounted').textContent = money(data.budgetCountedUsd);
    el('spendRemaining').textContent = money(data.remainingEstimatedUsd);
    el('spendUncertain').textContent =
      money(Number(data.uncertainReservedUsd || 0) + Number(data.inFlightReservedUsd || 0));
    el('spendPauseIndicator').textContent = data.settings.paused ? 'PAUSED' : 'ACTIVE';
    el('spendPauseIndicator').classList.toggle('spend-paused', data.settings.paused);
    const known = Number(data.knownEstimatedUsd || 0);
    el('spendStatus').textContent = data.settings.paused
      ? 'Paid Archer AI requests are currently paused.'
      : 'Known usage: ' + money(known) + '. In-flight and uncertain costs count toward your budget.';
    if (!initialized) {
      el('spendMonthly').value = data.settings.monthlyBudgetUsd;
      el('spendPerRequest').value = data.settings.perRequestCapUsd;
      el('spendPaused').checked = !!data.settings.paused;
      initialized = true;
    }
  }

  async function refresh() {
    if (saving) return;
    try { render(await api('/api/spending')); }
    catch (error) { el('spendStatus').textContent = 'Could not load budget: ' + error.message; }
  }

  el('spendSettingsForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (saving || appState.busy) return;
    saving = true;
    el('spendSaveButton').disabled = true;
    try {
      const settings = {
        monthlyBudgetUsd: Number(el('spendMonthly').value),
        perRequestCapUsd: Number(el('spendPerRequest').value),
        paused: el('spendPaused').checked
      };
      const result = await api('/api/spending/settings', {
        method: 'POST', body: JSON.stringify(settings)
      });
      notice(result.message);
      render(await api('/api/spending'));
    } catch (error) {
      notice('Spending settings could not be saved: ' + error.message, true);
    } finally {
      saving = false;
      el('spendSaveButton').disabled = false;
    }
  });

  window.SpendingDashboard = { refresh };
  refresh();
})();
