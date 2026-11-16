'use strict';

const $ = (id) => document.getElementById(id);
const appState = { data: { missions: [], activity: [], conversation: [] }, selectedMissionId: null, busy: false, configured: false };

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function humanDate(value) { return value ? new Date(value).toLocaleString() : '—'; }
function setBusy(value) {
  appState.busy = value;
  document.querySelectorAll('button').forEach(button => {
    if (button.closest('.nav') || button.id === 'cancelMission') return;
    button.disabled = value;
  });
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
  const [data, config] = await Promise.all([api('/api/state'), api('/api/config')]);
  appState.data = data;
  appState.configured = config.configured;
  $('statApi').textContent = config.configured ? 'KEY SET' : 'NO KEY';
  $('statApi').className = 'connection-state ' + (config.configured ? 'online' : 'offline');
  $('modelLabel').textContent = config.configured ? config.model : 'API key needed';
  $('connectionLabel').textContent = config.configured ? 'AI key present · unverified' : 'Local server online · AI not set up';
  const missions = data.missions;
  if (!appState.selectedMissionId && missions.length) appState.selectedMissionId = missions[0].id;
  if (appState.selectedMissionId && !missions.some(m => m.id === appState.selectedMissionId)) appState.selectedMissionId = missions[0]?.id ?? null;
  render();
}
function render() {
  const { missions, activity, conversation } = appState.data;
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
    if (pending) html += `<div class="run-actions"><button class="button primary" data-action="run" data-steps="1">▶ Run next step</button><button class="button secondary" data-action="run" data-steps="3">▶ Run up to 3 steps</button></div>`;
  }
  if (m.artifacts.length) {
    html += `<div class="section-label artifacts-heading">DELIVERABLES <span>${m.artifacts.length}</span></div>`;
    html += m.artifacts.slice().reverse().map(a => `<details class="artifact"><summary><span>▤</span> ${escapeHtml(a.title)} <small>${escapeHtml(humanDate(a.createdAtUtc))}</small></summary><div class="report-text" id="artifact-${escapeHtml(a.id)}"></div></details>`).join('');
  }
  detail.innerHTML = html;
  m.artifacts.forEach(a => { const el = $(`artifact-${a.id}`); if (el) el.textContent = a.content; });
}
function renderChat(conversation) {
  $('chatMessages').innerHTML = conversation.length ? conversation.map(e => `<div class="chat-message ${e.role === 'user' ? 'from-user' : 'from-archer'}"><div class="chat-speaker">${e.role === 'user' ? 'YOU' : 'ARCHER'}</div><div class="chat-content">${escapeHtml(e.content)}</div><small>${escapeHtml(humanDate(e.createdAtUtc))}</small></div>`).join('') : '<div class="chat-intro"><div class="big-icon">◇</div><h3>Archer is standing by.</h3><p>Ask a question, discuss a mission, or make a decision together.</p></div>';
}
async function action(run, success) {
  if (appState.busy) return;
  setBusy(true);
  try {
    const result = await run();
    const message = typeof success === 'function' ? success(result) : success;
    await refresh();
    notice(message);
  } catch (error) { notice(error.message, true); }
  finally { setBusy(false); }
}
function showView(view) {
  document.querySelectorAll('.view').forEach(section => section.classList.toggle('hidden', section.id !== `${view}View`));
  document.querySelectorAll('.nav-link').forEach(link => link.classList.toggle('active', link.dataset.view === view));
  $('breadcrumb').textContent = ({ missions: 'MISSION CONTROL', chat: 'TALK TO ARCHER', activity: 'ACTIVITY' })[view];
}
document.addEventListener('click', async event => {
  const link = event.target.closest('[data-view]');
  if (link) { showView(link.dataset.view); return; }
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
  await action(() => api('/api/chat', { method: 'POST', body: JSON.stringify({ message }) }), () => { $('chatText').value = ''; return 'Archer replied.'; });
  $('chatMessages').scrollTop = $('chatMessages').scrollHeight;
});
refresh().catch(error => {
  $('connectionLabel').textContent = 'Local server unavailable';
  notice(`Cannot connect to Archer: ${error.message}`, true);
});
