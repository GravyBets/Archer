'use strict';
(() => {
  const el = id => document.getElementById(id);
  const status = el('developmentBatchStatus');
  const list = el('developmentBatchList');
  const plan = el('developmentPlanButton');
  const objective = el('developmentObjective');
  const picker = el('managedProjectSelect');
  let busy = false, refreshing = false, projects = [], batches = [], selectedObjective = null;
  const current = () => projects.find(p => p.id === picker.value);
  function element(tag, value) {
    const node = document.createElement(tag);
    node.textContent = value;
    return node;
  }
  function option(value, label) { const o = element('option', label); o.value = value; return o; }
  function action(label, handler, disabled = false) {
    const b = element('button', label);
    b.type = 'button'; b.className = 'button secondary'; b.disabled = disabled;
    b.addEventListener('click', handler);
    return b;
  }
  function link(url, label, repository) {
    try {
      const u = new URL(url);
      if (u.origin !== 'https://github.com' || u.username || u.password ||
          !u.pathname.startsWith('/' + repository + '/pull/') ||
          !/^\d+$/.test(u.pathname.split('/').pop()) || u.search || u.hash) return null;
      const a = element('a', label); a.href = u.href; a.target = '_blank'; a.rel = 'noopener noreferrer';
      return a;
    } catch { return null; }
  }
  async function command(path, body, method = 'POST', onSuccess = () => {}) {
    if (busy) return;
    busy = true;
    try {
      const result = await api(path, { method, body: JSON.stringify(body) });
      notice(result.message || 'Saved.');
      onSuccess(result);
    } catch (error) { notice(error.message, true); }
    finally { busy = false; await refresh(); }
  }
  function render() {
    const p = current();
    plan.disabled = !p?.enabled || busy;
    el('pauseProjectButton').disabled = !p;
    el('pauseProjectButton').textContent = p?.paused ? 'Make project available' : 'Pause project';
    el('managedProjectStatus').textContent = p
      ? p.repository + ' · ' + p.technology + ' · priority ' + p.priority + ' · ' +
        p.attemptsToday + '/' + p.dailyAttemptLimit + ' attempts in last 24h · ' +
        (p.enabled ? 'Configured' : 'Not configured or paused') +
        ' · credential variables: ' + p.credentialPrefix + '_READ_TOKEN and _WRITE_TOKEN'
      : 'No project registered. Registration itself grants no GitHub or model access.';
    status.textContent = p?.enabled
      ? 'Review the plan, authorize one bounded batch, then Archer will run tasks and verify the required CI.'
      : 'Choose a configured project. Set ARCHER_PROJECT_AUTONOMY_ENABLED, the repository allowlists and scoped credentials.';
    const objectives = el('projectObjectives');
    const memories = el('projectMemories');
    const dependency = el('projectDependency');
    const previousDependency = dependency.value;
    objectives.replaceChildren(element('h3', 'Objectives'));
    memories.replaceChildren(element('h3', 'Project memory'));
    dependency.replaceChildren(option('', 'None'));
    if (!p) { list.replaceChildren(); return; }
    for (const o of p.objectives) {
      dependency.append(option(o.id, o.description.slice(0, 100)));
      const ready = !o.acceptedAtUtc && o.dependsOn.every(id => p.objectives.some(other => other.id === id && other.acceptedAtUtc));
      const related = batches.filter(b => b.projectId === p.id && b.objectiveId === o.id);
      const alreadyPlanned = related.some(b => b.armed || b.tasks.some(t => t.status !== 'Blocked'));
      const delegated = ['Delegated', 'Planning'].includes(o.planningState);
      const box = element('section', ''); box.className = 'coding-file-diff';
      box.append(element('p', (o.acceptedAtUtc ? 'Accepted' : ready ? 'Ready' : 'Waiting on dependencies') + ' · ' + o.description));
      box.append(element('p', o.planningState + (o.planningDetail ? ' · ' + o.planningDetail : '') + (o.delegatedUntilUtc ? ' · until ' + new Date(o.delegatedUntilUtc).toLocaleString() : '')));
      if (delegated) box.append(action('Cancel delegation', () => command('/api/projects/' + p.id + '/objectives/' + o.id + '/cancel-delegation', { confirm: true })));
      if (!o.acceptedAtUtc && !o.planningAttempted && !delegated && related.length === 0) {
        box.append(action('Delegate to Archer', () => {
          const resumeAfterRestart = el('developmentResumeAfterRestart').checked;
          if (window.confirm('Delegate this objective to Archer for 24 hours? Archer may select files within ' + p.sourceRoots.join(', ') + ', make one paid planning call and automatically publish draft PRs within the project coding allowance. No merge or deployment. Dependencies must be accepted first.'))
            command('/api/projects/' + p.id + '/objectives/' + o.id + '/delegate', { confirm: true, resumeAfterRestart });
        }, !p.enabled));
      }
      if (ready) {
        box.append(action('Use objective for next plan', () => {
          selectedObjective = o.id; objective.value = o.description; objective.readOnly = true; objective.focus();
          status.textContent = 'Saved objective selected. Create its development plan.';
        }, !p.enabled || alreadyPlanned || delegated));
        box.append(action('Accept objective', () => {
          if (window.confirm('Mark this objective accepted by you? This releases dependencies but does not certify a deployment.'))
            command('/api/projects/' + p.id + '/objectives/' + o.id + '/accept', { confirm: true });
        }));
      }
      objectives.append(box);
    }
    if ([...dependency.options].some(o => o.value === previousDependency)) dependency.value = previousDependency;
    for (const m of [...p.memory].reverse().slice(0, 50)) {
      const box = element('section', ''); box.className = 'coding-file-diff';
      box.append(element('p', m.kind + ' · ' + new Date(m.createdAtUtc).toLocaleString() +
        ' · ' + (m.shareWithAi ? 'Available to AI' : 'Local only')),
        element('p', m.content),
        action('Remove memory', () => {
          if (window.confirm('Remove this local memory entry?')) command('/api/projects/' + p.id + '/memory/' + m.id, {}, 'DELETE');
        }));
      memories.append(box);
    }
    list.replaceChildren();
    for (const batch of batches.filter(b => b.projectId === p.id).reverse()) {
      const section = element('section', ''); section.className = 'coding-file-diff';
      section.append(element('h3', batch.objective),
        element('p', (batch.armed ? 'Authorized / queued or running' : 'Not armed') + ' · ' +
          batch.tasks.filter(t => t.status === 'Verified').length + '/' + batch.tasks.length +
          ' CI-verified · ' + batch.attemptsRemaining + ' attempts left' +
          (batch.expiresAtUtc ? ' · expires ' + new Date(batch.expiresAtUtc).toLocaleString() : '')));
      for (const t of batch.tasks) {
        const detail = document.createElement('details');
        detail.append(element('summary', t.status + ' · ' + t.instruction),
          element('p', 'Approved files: ' + t.paths.join(', ')), element('p', 'Attempts: ' + t.attempts + ' / 2'));
        if (t.detail) detail.append(element('p', t.detail));
        const currentPr = link(t.pullRequestUrl, 'Current draft PR', batch.repository);
        if (currentPr) detail.append(currentPr);
        for (const prior of t.priorPullRequests || []) {
          const previous = link(prior, 'Previous failed draft (do not merge)', batch.repository);
          if (previous) detail.append(document.createElement('br'), previous);
        }
        section.append(detail);
      }
      section.append(action(batch.armed ? 'Pause batch' : 'Authorize / resume batch', () => {
        const path = '/api/development/batches/' + batch.id;
        if (batch.armed) { command(path + '/pause', { confirm: true }); return; }
        const resumeAfterRestart = el('developmentResumeAfterRestart').checked;
        if (window.confirm('Authorize these exact source tasks in ' + p.repository +
          ' for up to 24 hours and at most two coding attempts per task within your project and AI budget? Draft PRs only. No merge or deployment.'))
          command(path + '/arm', { confirm: true, resumeAfterRestart });
      }, !batch.armed && (!p.enabled || batch.tasks.some(t => t.status === 'Blocked') ||
          batch.tasks.every(t => t.status === 'Verified') ||
          (batch.expiresAtUtc && Date.parse(batch.expiresAtUtc) <= Date.now()))));
      list.append(section);
    }
    if (!list.children.length) list.append(element('p', 'No development batches for this project.'));
  }
  async function refresh() {
    if (busy || refreshing) return;
    refreshing = true;
    try {
      const selected = picker.value;
      [projects, { batches }] = await Promise.all([api('/api/projects'), api('/api/development/batches')]);
      picker.replaceChildren(...projects.map(p => option(p.id, p.name)));
      if (projects.some(p => p.id === selected)) picker.value = selected;
      render();
    } catch (error) { status.textContent = 'Could not load project state: ' + error.message; }
    finally { refreshing = false; }
  }
  picker.addEventListener('change', () => { selectedObjective = null; objective.value = ''; objective.readOnly = false; render(); });
  el('projectRegistryForm').addEventListener('submit', event => {
    event.preventDefault();
    command('/api/projects', {
      name: el('projectName').value, repository: el('projectRepo').value, technology: el('projectTechnology').value,
      sourceRoots: el('projectRoots').value.split(',').map(x => x.trim()).filter(Boolean),
      workflowFile: el('projectWorkflow').value, verificationJob: el('projectJob').value,
      requiredSteps: el('projectSteps').value.split('\n').map(x => x.trim()).filter(Boolean),
      priority: Number(el('projectPriority').value), dailyAttemptLimit: Number(el('projectAttemptLimit').value)
    }, 'POST', () => el('projectRegistryForm').reset());
  });
  el('pauseProjectButton').addEventListener('click', () => {
    const p = current(); if (p) command('/api/projects/' + p.id + '/pause', { paused: !p.paused });
  });
  el('projectObjectiveForm').addEventListener('submit', event => {
    event.preventDefault(); const p = current(); if (!p) return;
    const dep = el('projectDependency').value;
    command('/api/projects/' + p.id + '/objectives', { description: el('projectObjectiveText').value, dependsOn: dep ? [dep] : [] },
      'POST', () => el('projectObjectiveText').value = '');
  });
  el('projectMemoryForm').addEventListener('submit', event => {
    event.preventDefault(); const p = current(); if (!p) return;
    command('/api/projects/' + p.id + '/memory', { kind: el('projectMemoryKind').value,
      content: el('projectMemoryText').value, shareWithAi: el('projectMemoryShare').checked },
      'POST', () => el('projectMemoryText').value = '');
  });
  plan.addEventListener('click', () => {
    const p = current(); if (!p?.enabled || busy) return;
    const value = objective.value.trim();
    if (value.length < 15 || value.length > 1000) { notice('Provide a concrete 15–1000 character objective.', true); return; }
    if (!window.confirm('Generate a bounded development plan for ' + p.name + ' with one budget-checked AI request? No GitHub writes yet.')) return;
    command('/api/development/batches', { projectId: p.id, objectiveId: selectedObjective, objective: value, confirm: true },
      'POST', () => { selectedObjective = null; objective.value = ''; objective.readOnly = false; });
  });
  el('developmentRefreshButton').addEventListener('click', () => { selectedObjective = null; objective.readOnly = false; refresh(); });
  refresh();
  window.setInterval(() => {
    if (!document.hidden && !el('developerView').classList.contains('hidden')) refresh();
  }, 15000);
})();
