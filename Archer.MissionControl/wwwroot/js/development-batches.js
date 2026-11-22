'use strict';
(() => {
  const el = id => document.getElementById(id);
  const status = el('developmentBatchStatus');
  const list = el('developmentBatchList');
  const plan = el('developmentPlanButton');
  const objective = el('developmentObjective');
  let busy = false, enabled = false;
  const pilot = 'Bring KmlCsvConverter.cs in line with KmlAnalyzer.cs for legacy KML exports that reference xsi:schemaLocation without declaring the standard xsi namespace. Change only KmlCsvConverter.cs; preserve DTD prohibition, XML validation and conversion limits.';
  el('developmentPilotButton').addEventListener('click', () => { objective.value = pilot; objective.focus(); });
  function element(tag, value) {
    const node = document.createElement(tag);
    node.textContent = value;
    return node;
  }
  function link(url, label) {
    if (!/^https:\/\/github\.com\/GravyBets\/GeoShift\/pull\/\d+$/.test(url || '')) return null;
    const a = element('a', label);
    a.href = url; a.target = '_blank'; a.rel = 'noopener noreferrer';
    return a;
  }
  async function command(id, action, body) {
    if (busy) return;
    busy = true;
    try {
      const result = await api('/api/development/batches/' + encodeURIComponent(id) + '/' + action,
        { method: 'POST', body: JSON.stringify(body) });
      notice(result.message);
    } catch (error) { notice(error.message, true); }
    finally { busy = false; await refresh(); }
  }
  async function refresh() {
    if (busy) return;
    try {
      const result = await api('/api/development/batches');
      enabled = result.enabled;
      plan.disabled = !enabled;
      status.textContent = enabled
        ? 'Configured. Review the proposed files and authorize one bounded batch. GitHub CI is required.'
        : 'Disabled. Configure dedicated GeoShift autonomy, API key and repository-scoped publishing.';
      list.replaceChildren();
      for (const batch of [...result.batches].reverse()) {
        const section = element('section', '');
        section.className = 'coding-file-diff';
        section.append(element('h3', batch.objective),
          element('p', (batch.armed ? 'Authorized / running' : 'Not armed') + ' · ' +
            batch.tasks.filter(t => t.status === 'Verified').length + '/' + batch.tasks.length +
            ' CI-verified · ' + batch.attemptsRemaining + ' coding attempts left' +
            (batch.expiresAtUtc ? ' · expires ' + new Date(batch.expiresAtUtc).toLocaleString() : '')));
        for (const t of batch.tasks) {
          const detail = document.createElement('details');
          detail.append(element('summary', t.status + ' · ' + t.instruction),
            element('p', 'Approved files: ' + t.paths.join(', ')),
            element('p', 'Attempts: ' + t.attempts + ' / 2'));
          if (t.detail) detail.append(element('p', t.detail));
          const current = link(t.pullRequestUrl, 'Current draft PR');
          if (current) detail.append(current);
          for (const prior of t.priorPullRequests || []) {
            const previous = link(prior, 'Previous failed draft (do not merge)');
            if (previous) detail.append(document.createElement('br'), previous);
          }
          section.append(detail);
        }
        const button = element('button', batch.armed ? 'Pause mission' : 'Authorize / resume batch');
        button.type = 'button'; button.className = 'button secondary';
        button.disabled = !batch.armed && (!enabled || batch.tasks.some(t => t.status === 'Blocked') ||
          batch.tasks.every(t => t.status === 'Verified') ||
          (batch.expiresAtUtc && Date.parse(batch.expiresAtUtc) <= Date.now()));
        button.addEventListener('click', () => {
          if (batch.armed) { command(batch.id, 'pause', { confirm: true }); return; }
          const resumeAfterRestart = el('developmentResumeAfterRestart').checked;
          if (!window.confirm('Authorize these exact source tasks for up to 24 hours and up to two coding attempts per task, using your existing AI budget? Archer may publish draft PRs automatically. No merge or deployment.' +
            (resumeAfterRestart ? ' Safe queued work may resume after restart.' : ' Restart will disarm the mission.'))) return;
          command(batch.id, 'arm', { confirm: true, resumeAfterRestart });
        });
        section.append(button); list.append(section);
      }
      if (!result.batches.length) list.append(element('p', 'No development plans yet.'));
    } catch (error) { status.textContent = 'Could not read development missions: ' + error.message; }
  }
  plan.addEventListener('click', async () => {
    if (busy || !enabled) return;
    const value = objective.value.trim();
    if (value.length < 15 || value.length > 1000) { notice('Provide a concrete 15–1000 character objective.', true); return; }
    if (!window.confirm('Generate a bounded development plan using one paid AI request? Nothing will be edited or published yet.')) return;
    busy = true; plan.disabled = true;
    status.textContent = 'Generating plan…';
    try {
      const result = await api('/api/development/batches', { method: 'POST', body: JSON.stringify({ objective: value, confirm: true }) });
      notice(result.message);
    } catch (error) { notice(error.message, true); }
    finally { busy = false; await refresh(); }
  });
  el('developmentRefreshButton').addEventListener('click', refresh);
  refresh();
  window.setInterval(() => {
    if (!document.hidden && !el('developerView').classList.contains('hidden')) refresh();
  }, 15000);
})();
