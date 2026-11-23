'use strict';

const $ = (id) => document.getElementById(id);
const appState = { data: { missions: [], activity: [], conversation: [] }, projectReadiness: [], selectedMissionId: null, busy: false, configured: false };

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function humanDate(value) { return value ? new Date(value).toLocaleString() : '—'; }
function setBusy(value) {
  appState.busy = value;
  document.querySelectorAll('button').forEach(button => {
    if (button.closest('.nav') || button.id === 'cancelMission' || button.dataset.allowBusy === 'true') return;
    if (value) {
      // Preserve browser-feature-disabled controls (e.g. unsupported microphones).
      button.dataset.wasDisabled = button.disabled ? 'true' : 'false';
      button.disabled = true;
    } else if (button.dataset.wasDisabled !== undefined) {
      button.disabled = button.dataset.wasDisabled === 'true';
      delete button.dataset.wasDisabled;
    }
  });
  // Some mission buttons are recreated during a refresh while busy. Rebuild
  // them after completion so they are not left artificially disabled.
  if (!value) render();
}
function notice(message, isError = false) {
  const target = $('notice');
  target.textContent = message;
  target.className = 'notice' + (isError ? ' error' : '');
  window.clearTimeout(notice.timer);
  notice.timer = window.setTimeout(() => target.classList.add('hidden'), 8500);
}
async function api(route, options = {}) {
  const res = await fetch(route, { ...options, headers: { 'Content-Type': 'application/json', ...options.headers } });
  let body;
  try { body = await res.json(); } catch { body = {}; }
  if (!res.ok) throw new Error(body.error || body.detail || `Request failed (HTTP ${res.status})`);
  return body;
}
async function refresh() {
  const [data, config, usage, projectReadiness] = await Promise.all([api('/api/state'), api('/api/config'), api('/api/usage'), api('/api/projects')]);
  appState.projectReadiness = Array.isArray(projectReadiness) ? projectReadiness : [];
  appState.data = data;
  appState.configured = config.configured;
  $('statApi').textContent = config.configured ? 'KEY SET' : 'NO KEY';
  $('statApi').className = 'connection-state ' + (config.configured ? 'online' : 'offline');
  $('modelLabel').textContent = config.configured ? (config.routing?.mode === 'adaptive' ? 'Adaptive model routing' : config.model) : 'API key needed';
  $('connectionLabel').textContent = config.configured ? 'AI key present · unverified' : 'Local server online · AI not set up';
  const missions = data.missions;
  if (!appState.selectedMissionId && missions.length) appState.selectedMissionId = missions[0].id;
  if (appState.selectedMissionId && !missions.some(m => m.id === appState.selectedMissionId)) appState.selectedMissionId = missions[0]?.id ?? null;
  render();
  renderUsage(usage, config.routing);
  window.SpendingDashboard?.refresh();
}

function formatUsd(value) {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return 'Not priced';
  const n = Number(value);
  return '$' + n.toFixed(n > 0 && n < 0.001 ? 6 : 4);
}

function renderUsage(usage, routing) {
  const month = usage.month || {};
  const lifetime = usage.lifetime || {};
  const fmt = new Intl.NumberFormat();
  $('usageMonthCost').textContent = formatUsd(month.estimatedCostUsd || 0);
  $('usageLifetimeCost').textContent = formatUsd(lifetime.estimatedCostUsd || 0);
  const tokens = (month.inputTokens || 0) + (month.outputTokens || 0);
  $('usageMonthTokens').textContent = fmt.format(tokens);
  $('usageTokenDetail').textContent = fmt.format(month.inputTokens || 0) + ' in · ' + fmt.format(month.outputTokens || 0) + ' out';
  $('usageRequests').textContent = fmt.format(month.requests || 0);
  $('usageRequestDetail').textContent = fmt.format(month.webSearchCalls || 0) + ' web searches this month';
  $('usageModelMode').textContent = routing?.mode === 'adaptive'
    ? 'Adaptive · ' + [routing.chat, routing.research, routing.codeReview].filter(Boolean).join(' / ')
    : 'Fixed · ' + (routing?.chat || 'unknown');

  const models = usage.models || [];
  $('usageModelBreakdown').innerHTML = models.length
    ? models.map(m => '<div class="usage-model-row"><div><strong>' + escapeHtml(m.model) +
        '</strong><small>' + fmt.format(m.requests) + ' requests · ' + fmt.format(m.tokens) +
        ' tokens</small></div><strong>' + escapeHtml(formatUsd(m.estimatedCostUsd)) +
        (m.unpricedRequests ? ' + unknown' : '') + '</strong></div>').join('')
    : '<p class="usage-empty">No API usage recorded this UTC month.</p>';

  const recent = usage.recent || [];
  $('usageRecentRows').innerHTML = recent.length
    ? recent.map(item => '<tr><td>' + escapeHtml(item.operation) +
        '<small>' + escapeHtml(humanDate(item.recordedAtUtc)) +
        (item.status !== 'completed' ? ' · ' + escapeHtml(item.status) : '') +
        '</small></td><td>' + escapeHtml(item.model) + '</td><td>' +
        fmt.format((item.inputTokens || 0) + (item.outputTokens || 0)) +
        '<small>' + fmt.format(item.webSearchCalls || 0) + ' search calls</small></td><td>' +
        escapeHtml(formatUsd(item.estimatedCostUsd)) + '</td></tr>').join('')
    : '<tr><td colspan="4">No API usage recorded yet.</td></tr>';

  const unpriced = lifetime.unpricedRequests || 0;
  $('usageDisclaimer').textContent =
    'Local estimate using API-reported tokens and observed web-search calls; not your OpenAI invoice. ' +
    'Pricing snapshot: ' + (usage.priceReferenceDate || 'unknown') +
    '. Previous requests are not included. ' +
    (unpriced ? unpriced + ' unpriced request(s) excluded from estimates. ' : '') +
    'Special pricing and charges without usage data may not appear.';
}
// Deterministic read-only owner summary; never mistake a queued draft for
// merged code, or claim an AI worker is running unless task state shows it.
// Repository links must come from an expected PR in the registered project.
// Never render a user-supplied URL without checking origin, path and PR number.
function trustedReviewUrl(repository, batch) {
  if (!/^[A-Za-z0-9-]{1,39}\/[A-Za-z0-9_.-]{1,100}$/.test(String(repository || '')))
    return null;
  const number = Number(batch?.workspacePullRequestNumber);
  const lastProposal = (batch?.tasks || []).map(t => t.proposal).filter(Boolean).at(-1);
  const raw = lastProposal?.pullRequestUrl;
  if (typeof raw === 'string') {
    const match = raw.match(/^https:\/\/github\.com\/([A-Za-z0-9-]{1,39})\/([A-Za-z0-9_.-]{1,100})\/pull\/([1-9][0-9]*)$/);
    if (match && (match[1] + '/' + match[2]).toLowerCase() === repository.toLowerCase())
      return raw;
  }
  if (Number.isSafeInteger(number) && number > 0)
    return 'https://github.com/' + repository + '/pull/' + number;
  return null;
}
function paintWorkProgress(prefix, completed, total, active, planning = false, unit = 'verified') {
  const root = $(prefix + 'Progress');
  const bar = $(prefix + 'Bar');
  const count = $(prefix + 'Count');
  const hasTasks = Number.isInteger(total) && total > 0;
  const progressVisible = Boolean(active || planning || hasTasks);
  root.hidden = !progressVisible;
  root.dataset.mode = hasTasks ? 'determinate' : (active || planning ? 'indeterminate' : 'idle');
  const done = hasTasks ? Math.max(0, Math.min(total, completed || 0)) : 0;
  bar.style.width = hasTasks ? Math.round(done * 100 / total) + '%' : '0%';
  count.textContent = hasTasks ? done + ' of ' + total + ' tasks ' + unit : (active || planning ? 'Work in progress · tasks not yet known' : '');
  // Only expose a numeric meter when actual task counts are available.
  if (hasTasks) {
    root.setAttribute('role', 'progressbar');
    root.setAttribute('aria-valuemin', '0');
    root.setAttribute('aria-valuemax', String(total));
    root.setAttribute('aria-valuenow', String(done));
    root.setAttribute('aria-valuetext', count.textContent);
  } else {
    root.setAttribute('role', 'status');
    root.removeAttribute('aria-valuenow');
    root.removeAttribute('aria-valuemax');
    root.removeAttribute('aria-valuemin');
    root.removeAttribute('aria-valuetext');
  }
}
function activeMissionProgress(missions) {
  return (Array.isArray(missions) ? missions : []).filter(m =>
    m.status === 'Running' || m.autoRunEnabled ||
    (m.tasks || []).some(t => t.status === 'Running')).sort((a,b) =>
      (Date.parse(b.updatedAtUtc || '') || 0) - (Date.parse(a.updatedAtUtc || '') || 0))[0] || null;
}
function renderMissionProgress(missions) {
  const mission = activeMissionProgress(missions);
  const root = $('homeMissionPulse');
  root.hidden = !mission;
  if (!mission) return;
  $('homeMissionLabel').textContent = mission.title || 'Active mission';
  $('homeMissionStatus').textContent = mission.status === 'Running' ? 'WORKING' : 'AUTO-RUN ON';
  const tasks = Array.isArray(mission.tasks) ? mission.tasks : [];
  const completed = tasks.filter(t => t.status === 'Completed').length;
  const total = tasks.length;
  paintWorkProgress('homeMission', completed, total, true, false, 'complete');
  $('homeMissionCount').textContent = total
    ? completed + ' of ' + total + ' mission steps complete'
    : 'Planning mission · steps not yet known';
}

function latestDevelopmentPulse(data) {
  const projects = Array.isArray(data?.projects) ? data.projects : [];
  const batches = Array.isArray(data?.developmentBatches) ? data.developmentBatches : [];
  const entries = projects.flatMap(project =>
    (Array.isArray(project.objectives) ? project.objectives : []).map(objective => {
      const related = batches.filter(b => b.projectId === project.id && b.objectiveId === objective.id);
      const batch = related.at(-1);
      const states = (Array.isArray(batch?.tasks) ? batch.tasks : []).map(t => t.status);
      let status = 'Saved, not started', tone = 'idle';
      if (objective.acceptedAtUtc) { status = 'Accepted'; tone = 'done'; }
      else if (states.includes('Blocked') || objective.planningState === 'Blocked') {
        status = 'Needs attention'; tone = 'alert';
      } else if (states.includes('Testing')) {
        status = 'Checking code'; tone = 'active';
      } else if (states.some(s => s === 'Editing' || s === 'Publishing')) {
        status = 'Writing code'; tone = 'active';
      } else if (states.length && states.every(s => s === 'Verified')) {
        status = 'Draft ready for review'; tone = 'ready';
      } else if (batch?.armed) {
        status = 'Queued for coding'; tone = 'queued';
      } else if (batch) {
        status = 'Awaiting authorization'; tone = 'queued';
      } else if (objective.planningState === 'Planning') {
        status = 'Planning'; tone = 'active';
      } else if (objective.planningState === 'Delegated') {
        status = 'Queued for planning'; tone = 'queued';
      }
      if (project.paused && tone !== 'done' && tone !== 'ready') {
        status = 'Project paused'; tone = 'idle';
      }
      return {
        project: String(project.name || 'Project'),
        description: String(objective.description || ''),
        status, tone, accepted: Boolean(objective.acceptedAtUtc),
        created: Date.parse(objective.createdAtUtc || '') || 0,
        batch, repository: String(project.repository || ''),
        completed: states.filter(s => s === 'Verified').length, total: states.length,
        planning: objective.planningState === 'Planning',
        reviewUrl: states.length && states.every(s => s === 'Verified')
          ? trustedReviewUrl(project.repository, batch) : null
      };
    }));
  // Show the latest open assignment. If everything is accepted, retain the
  // most recent completed item rather than falsely claiming there is work.
  // Actual in-flight development outranks newer but inactive requests.
  entries.sort((a,b) => Number(b.tone === 'active') - Number(a.tone === 'active') ||
    Number(a.accepted) - Number(b.accepted) || b.created - a.created);
  return entries[0] || null;
}
function developmentNeedsRefresh(data) {
  const projects = Array.isArray(data?.projects) ? data.projects : [];
  const batches = Array.isArray(data?.developmentBatches) ? data.developmentBatches : [];
  return projects.some(p => (p.objectives || []).some(o =>
    !o.acceptedAtUtc && ['Delegated','Planning','Reassessment queued'].includes(o.planningState))) ||
    batches.some(b => b.armed || (b.tasks || []).some(t =>
      ['Editing','Publishing','Testing','Repair queued'].includes(t.status)));
}
function renderDevelopmentPulse(data) {
  const pulse = latestDevelopmentPulse(data);
  const root = $('homeDevelopmentPulse');
  root.dataset.tone = pulse?.tone || 'idle';
  $('homeDevelopmentLabel').textContent = pulse?.project || 'No development assignments';
  $('homeDevelopmentDetail').textContent = pulse?.description ||
    'Tell Archer what to work on in a registered project.';
  $('homeDevelopmentStatus').textContent = pulse?.status || 'IDLE';
  const active = pulse?.tone === 'active';
  paintWorkProgress('homeDevelopment', pulse?.completed || 0, pulse?.total || 0,
    active, Boolean(pulse?.planning));
  const progress = $('homeDevelopmentProgress');
  progress.hidden = !pulse || (!active && pulse.tone !== 'ready' && !(pulse.total > 0));
  const review = $('homeDevelopmentReview');
  review.hidden = !(pulse?.tone === 'ready' && pulse.reviewUrl);
  if (!review.hidden) review.href = pulse.reviewUrl;
  else review.removeAttribute('href');
  const readiness = appState.projectReadiness.find(p => p.repository === pulse?.repository);
  const guidance = $('homeDevelopmentSetup');
  const missing = Array.isArray(readiness?.setup?.missing) ? readiness.setup.missing : [];
  const needsSetup = Boolean(readiness && !readiness.enabled && missing.length && pulse &&
    pulse.tone !== 'active' && pulse.tone !== 'ready' && pulse.tone !== 'done');
  guidance.hidden = !needsSetup;
  if (needsSetup) {
    $('homeDevelopmentSetupText').textContent = 'Not running: ' + missing.slice(0, 2).join(' · ') +
      (missing.length > 2 ? ' · ' + (missing.length - 2) + ' more checks' : '');
  }
}

function render() {
  const { missions, activity, conversation } = appState.data;
  // Server-persisted presentation choice works for browser and Windows
  // voice requests alike. No page reload or Developer interaction.
  $('archerView').dataset.layout = appState.data.homeLayout === 'split'
    ? 'split' : 'centered';
  renderDevelopmentPulse(appState.data);
  renderMissionProgress(missions);
  $('statMissions').textContent = missions.length;
  $('statCompleted').textContent = missions.reduce((n, m) => n + m.tasks.filter(t => t.status === 'Completed').length, 0);
  $('statActive').textContent = missions.filter(m => ['Ready','Paused','Running'].includes(m.status)).length;
  $('missionCount').textContent = missions.length;
  $('missionList').innerHTML = missions.length ? missions.map(m => {
    const completed = m.tasks.filter(t => t.status === 'Completed').length;
    const percent = m.tasks.length ? Math.round(completed / m.tasks.length * 100) : 0;
    return `<button type="button" class="mission-item ${m.id === appState.selectedMissionId ? 'selected' : ''}" data-mission="${escapeHtml(m.id)}">
      <div class="mission-item-head"><strong>${escapeHtml(m.title)}</strong><span class="small-status">${escapeHtml(m.status)}</span></div>
      <p>${escapeHtml(m.objective)}</p><div class="progress-track"><div class="progress-bar" style="width:${percent}%"></div></div>
      <div class="mission-meta"><span>${completed} / ${m.tasks.length} steps</span><span>${percent}%</span></div>
    </button>`;
  }).join('') : `<div class="empty-list"><div class="big-icon">⌘</div><h3>No missions yet</h3><p>Give Archer an objective to get started.</p><button class="button secondary" id="starterMission">Use starter mission</button></div>`;
  const mission = missions.find(m => m.id === appState.selectedMissionId);
  renderDetail(mission);
  renderChat(conversation);
  window.ArcherPresence?.update(appState.data);
  $('activityList').innerHTML = activity.length ? activity.map(e => `<div class="activity-entry"><span class="activity-dot ${e.level === 'Success' ? 'success' : e.level === 'Warning' ? 'warning' : ''}"></span><div><strong>${escapeHtml(e.message)}</strong><small>${escapeHtml(humanDate(e.createdAtUtc))}</small></div></div>`).join('') : `<div class="empty-list"><p>No activity yet. Your real mission actions will appear here.</p></div>`;
}
function renderDetail(m) {
  const detail = $('missionDetail');
  const badge = $('missionBadge');
  if (!m) {
    $('detailHeading').textContent = 'Mission overview';
    $('detailSubheading').textContent = 'Select a mission to view its progress';
    badge.className = 'status-badge hidden';
    detail.innerHTML = '<div class="empty-detail"><div class="big-icon">◎</div><h3>Mission awaiting command</h3><p>Create your first mission to begin.</p></div>';
    return;
  }
  $('detailHeading').textContent = m.title;
  $('detailSubheading').textContent = `Created ${humanDate(m.createdAtUtc)}`;
  badge.textContent = m.status;
  badge.className = 'status-badge ' + (m.status === 'Completed' ? 'done' : '');
  const complete = m.tasks.filter(t => t.status === 'Completed').length;
  const pending = m.tasks.filter(t => t.status === 'Pending').length;
  let html = `<div class="objective-card"><div class="eyebrow">MISSION OBJECTIVE</div><p>${escapeHtml(m.objective)}</p></div>`;
  if (!m.tasks.length) {
    html += `<div class="unplanned"><div class="big-icon">⌬</div><h3>Ready to develop a plan?</h3><p>Archer will break your objective into bounded, executable research and analysis tasks.</p><button class="button primary" data-action="plan" ${appState.busy ? 'disabled' : ''}>✧ Generate Mission Plan</button></div>`;
  } else {
    html += `<div class="section-label">MISSION PLAN <span>${complete}/${m.tasks.length} COMPLETE</span></div>`;
    if (m.summary) html += `<p class="mission-summary">${escapeHtml(m.summary)}</p>`;
    html += '<div class="task-list">' + m.tasks.map((t, idx) => `<div class="task"><div class="task-number ${t.status === 'Completed' ? 'complete' : ''}">${t.status === 'Completed' ? '✓' : idx + 1}</div><div class="task-content"><div class="task-line"><strong>${escapeHtml(t.title)}</strong><span class="task-tag">${escapeHtml(t.status)}</span></div><p>${escapeHtml(t.description)}</p>${t.lastError ? `<div class="task-error">${escapeHtml(t.lastError)} ${t.status === 'Failed' ? `<button class="text-button" data-action="retry" data-task="${escapeHtml(t.id)}">Retry</button>` : ''}</div>` : ''}</div></div>`).join('') + '</div>';
    if (m.autoRunEnabled) {
      html += `<div class="run-actions"><button class="button secondary" data-action="autorun" data-enabled="false">⏸ Pause automatic execution</button><span class="mission-summary">Automatic mode active · ${m.autoRunTasksRemaining ?? 0} task attempt(s) remaining. Current work may finish after pausing.</span></div>`;
    } else if (pending && m.status !== 'Running' && m.status !== 'Archived') {
      html += `<div class="run-actions"><button class="button primary" data-action="run" data-steps="1">▶ Run next step</button><button class="button secondary" data-action="run" data-steps="3">▶ Run up to 3 steps</button></div>`;
      if (!m.tasks.some(t => t.status === 'Failed')) {
        html += `<div class="run-actions"><button class="button secondary" data-action="autorun" data-enabled="true">✦ Auto-run up to 3 tasks</button><span class="mission-summary">Opt-in · local app stays open · no automatic retries.</span></div>`;
      }
    }
  }
  if (m.artifacts.length) {
    html += `<div class="section-label artifacts-heading">DELIVERABLES <span>${m.artifacts.length}</span></div>`;
    html += m.artifacts.slice().reverse().map(a => `<details class="artifact"><summary><span>▤</span> ${escapeHtml(a.title)} <small>${escapeHtml(humanDate(a.createdAtUtc))}</small></summary><div class="report-text" id="artifact-${escapeHtml(a.id)}"></div></details>`).join('');
  }
  const openArtifacts = Array.from(detail.querySelectorAll('details.artifact[open]')).map(el => {
    const report = el.querySelector('.report-text');
    return { id: report?.id, scrollTop: report?.scrollTop ?? 0 };
  }).filter(x => x.id);
  detail.innerHTML = html;
  m.artifacts.forEach(a => { const el = $(`artifact-${a.id}`); if (el) el.textContent = a.content; });
  for (const old of openArtifacts) {
    const report = $(old.id);
    if (!report) continue;
    const details = report.closest('details');
    if (details) details.open = true;
    report.scrollTop = old.scrollTop;
  }
}
function renderChat(conversation) {
  const container = $('chatMessages');
  const wasNearBottom = (container.scrollHeight - container.scrollTop - container.clientHeight) < 80;
  const oldScroll = container.scrollTop;
  container.innerHTML = conversation.length ? conversation.map(e => `<div class="chat-message ${e.role === 'user' ? 'from-user' : 'from-archer'}"><div class="chat-speaker">${e.role === 'user' ? 'YOU' : 'ARCHER'}</div><div class="chat-content">${escapeHtml(e.content)}</div><small>${escapeHtml(humanDate(e.createdAtUtc))}</small></div>`).join('') : '<div class="chat-intro"><h3>Archer is standing by.</h3><p>Ask a question, discuss a mission, or make a decision together.</p></div>';
  container.scrollTop = wasNearBottom ? container.scrollHeight : oldScroll;
}
async function action(run, success) {
  if (appState.busy) return;
  setBusy(true);
  try {
    const result = await run();
    const message = typeof success === 'function' ? success(result) : success;
    await refresh();
    notice(message);
    return result;
  } catch (error) {
    notice(error.message, true);
    return null;
  } finally {
    setBusy(false);
    window.ArcherPresence?.settle();
  }
}
function showView(view) {
  const allowed = ['archer', 'missions', 'developer', 'operations', 'inbox', 'attention', 'activity'];
  if (!allowed.includes(view)) view = 'archer';
  if (view !== 'archer') window.ArcherPresence?.stopListening();
  document.querySelectorAll('.view').forEach(section => section.classList.toggle('hidden', section.id !== `${view}View`));
  document.querySelectorAll('.nav-link').forEach(link => link.classList.toggle('active', link.dataset.view === view));
  $('breadcrumb').textContent = ({ archer: 'ARCHER', missions: 'MISSION CONTROL', developer: 'DEVELOPER', operations: 'OPERATIONS', inbox: 'INBOX', attention: 'ATTENTION', activity: 'ACTIVITY' })[view];
  if (view === 'operations' || view === 'archer')
    window.OperatorContext?.refresh().catch(error => console.warn('Context refresh failed:', error.message));
  if (view === 'inbox')
    window.ArcherInbox?.refresh().catch(error => console.warn('Inbox refresh failed:', error.message));
}
document.addEventListener('click', async event => {
  const link = event.target.closest('[data-view]');
  if (link) { showView(link.dataset.view); return; }
  const navLink = event.target.closest('[data-go-view]');
  if (navLink) { showView(navLink.dataset.goView); return; }
  const issue = event.target.closest('[data-open-mission]');
  if (issue) {
    appState.selectedMissionId = issue.dataset.openMission;
    showView('missions');
    render();
    return;
  }
  const card = event.target.closest('[data-mission]');
  if (card) { appState.selectedMissionId = card.dataset.mission; render(); return; }
  if (event.target.closest('#starterMission')) { await createStarterMission(); return; }
  const act = event.target.closest('[data-action]');
  if (!act || appState.busy) return;
  const id = appState.selectedMissionId;
  if (act.dataset.action === 'plan') {
    await action(() => api(`/api/missions/${id}/plan`, { method: 'POST' }), 'Mission plan ready. Review the tasks, then run a step.');
  } else if (act.dataset.action === 'run') {
    const steps = Number(act.dataset.steps);
    await action(() => api(`/api/missions/${id}/run`, { method: 'POST', body: JSON.stringify({ maxSteps: steps }) }),
      result => result.error ? `Completed ${result.stepsCompleted} task(s). Last task failed: ${result.error}` : `Completed ${result.stepsCompleted} task(s). Open Deliverables to inspect actual results.`);
  } else if (act.dataset.action === 'autorun') {
    const enabled = act.dataset.enabled === 'true';
    await action(() => api(`/api/missions/${id}/autorun`, { method: 'POST', body: JSON.stringify({ enabled }) }),
      result => result.message);
  } else if (act.dataset.action === 'retry') {
    await action(() => api(`/api/missions/${id}/tasks/${act.dataset.task}/retry`, { method: 'POST' }), 'Task queued for retry.');
  }
});
$('newMissionButton').addEventListener('click', () => $('missionDialog').showModal());
$('cancelMission').addEventListener('click', () => $('missionDialog').close());
$('missionForm').addEventListener('submit', async event => {
  event.preventDefault();
  if (appState.busy) return;
  const title = $('missionTitle').value;
  const objective = $('missionObjective').value;
  await action(() => api('/api/missions', { method: 'POST', body: JSON.stringify({ title, objective }) }), result => {
    appState.selectedMissionId = result.id;
    $('missionDialog').close();
    $('missionForm').reset();
    return 'Mission created. Generate a plan to begin.';
  });
});
async function createStarterMission() {
  await action(() => api('/api/missions', { method: 'POST', body: JSON.stringify({
    title: 'Build $1,000/month in recurring revenue',
    objective: 'Research real customer needs and identify a practical path toward $1,000/month in recurring revenue. Prioritize opportunities related to GeoShift and geographic data migration, and compare alternatives using evidence. No purchases, deployment, or customer outreach without permission.'
  }) }), result => { appState.selectedMissionId = result.id; return 'Starter mission created. Generate its plan to begin.'; });
}
$('chatForm').addEventListener('submit', async event => {
  event.preventDefault();
  const message = $('chatText').value.trim();
  if (!message || appState.busy) return;
  // Mission voice commands use a strict local parser. Never submit them
  // to the model or authorize execution via natural-language "yes".
  const missionCommand = window.ArcherVoiceMissions?.parse?.(message);
  // A deliberate hands-free voice turn may resume listening AFTER Archer
  // finishes speaking. Typed/manual sends end the microphone session.
  const voiceMode = window.ArcherPresence?.prepareSend?.() === true;
  window.ArcherPresence?.stopSpeech();
  if (missionCommand) {
    setBusy(true);
    let spoken = '';
    try {
      const result = missionCommand.kind === 'list'
        ? await window.ArcherVoiceMissions.list()
        : await window.ArcherVoiceMissions.propose(
            missionCommand.title, missionCommand.objective);
      spoken = window.ArcherVoiceMissions.describe(result);
      $('chatText').value = '';
      await refresh();
      notice(spoken);
    } catch (error) {
      spoken = 'I could not complete that mission request. ' +
        (error?.message || 'Please try again.');
      notice(spoken, true);
    } finally {
      setBusy(false);
    }
    // Reply only after releasing UI busy state so opt-in voice can resume.
    if (spoken) window.ArcherPresence?.reply(spoken);
    return;
  }
  // Only ordinary chat can share a manually selected phone draft.
  const mobileSource = window.ArcherMobileDrafts?.beginChat(message);
  window.ArcherPresence?.setState('thinking', 'Archer is considering your message.');
  const result = await action(
    () => api('/api/chat', { method: 'POST', body: JSON.stringify({ message, voiceMode, directOwnerInput: !mobileSource }) }),
    'Archer replied.');
  if (result && typeof result.answer === 'string') {
    $('chatText').value = '';
    $('chatMessages').scrollTop = $('chatMessages').scrollHeight;
    window.ArcherMobileDrafts?.chatCompleted(mobileSource);
    window.ArcherPresence?.reply(result.answer);
  } else {
    window.ArcherPresence?.failure('The response could not be completed. Check the error banner above.');
  }
});
// When an on-screen approved voice command creates a local mission draft,
 // select it in the existing dashboard without executing its plan.
window.ArcherOnMissionDraftCreated = async id => {
  if (typeof id !== 'string') return;
  appState.selectedMissionId = id;
  await refresh();
};
showView('archer');
refresh().catch(error => {
  $('connectionLabel').textContent = 'Local server unavailable';
  notice(`Cannot connect to Archer: ${error.message}`, true);
});

// Refresh while authorized background work is active, without interfering
// with user operations. Completed reports can still be read while updating.
window.setInterval(() => {
  if (appState.busy) return;
  if (document.hidden) return;
  const running = Boolean(activeMissionProgress(appState.data.missions));
  if (running) {
    refresh().catch(error => console.warn('Archer refresh failed:', error.message));
  } else if (developmentNeedsRefresh(appState.data)) {
    // Read-only status polling; no AI request, GitHub write, or approval.
    api('/api/state').then(data => { appState.data = data; render(); })
      .catch(error => console.warn('Development pulse refresh failed:', error.message));
  }
}, 5000);
