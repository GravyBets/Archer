'use strict';
(() => {
  const el = id => document.getElementById(id);
  const status = el('developmentBatchStatus');
  const list = el('developmentBatchList');
  const plan = el('developmentPlanButton');
  const objective = el('developmentObjective');
  const picker = el('managedProjectSelect');
  let busy = false, refreshing = false, projects = [], batches = [], selectedObjective = null, selfAudit = null, lastSnapshot = '';
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
      const path = '/' + repository + '/pull/';
      if (u.origin !== 'https://github.com' || u.username || u.password ||
          !u.pathname.startsWith(path) || !/^\d+$/.test(u.pathname.slice(path.length)) || u.search || u.hash) return null;
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
    finally { busy = false; lastSnapshot = ''; await refresh(); }
  }
  // Keep results across polling and bind them to the exact draft head.
  const reviews = new Map();
  function disclosure(label) {
    const d = document.createElement('details'); d.className = 'dev-disclosure';
    d.append(element('summary', label)); return d;
  }
  function phase(batch, objective = null) {
    if (batch.continuous && objective && ['Planning', 'Reassessment queued', 'Batch created'].includes(objective.planningState) && !batch.armed && batch.tasks.length && batch.tasks.every(t => t.status === 'Verified')) return 'Planning';
    if (batch.tasks.length && batch.tasks.every(t => t.status === 'Verified')) return 'Ready for review';
    if (batch.tasks.some(t => t.status === 'Blocked')) return 'Needs attention';
    if (batch.tasks.some(t => t.status === 'Testing')) return 'Checking the work';
    if (batch.tasks.some(t => ['Editing', 'Publishing'].includes(t.status))) return 'Working';
    if (batch.armed) return 'Queued';
    return batch.expiresAtUtc ? 'Paused' : 'Needs approval';
  }
  function objectivePhase(o) {
    if (o.acceptedAtUtc) return 'Accepted';
    if (o.planningState === 'Blocked') return 'Needs attention';
    if (o.planningState === 'Planning' || o.planningState === 'Reassessment queued') return 'Planning';
    if (o.planningState === 'Delegated') return 'Queued';
    return 'Saved';
  }
  function chip(label) {
    const node = element('span', label); node.className = 'dev-phase';
    node.dataset.phase = label; return node;
  }
  function primary(label, handler, disabled = false) {
    const b = action(label, handler, disabled); b.className = 'button primary'; return b;
  }
  function delegate(p, id, continuous) {
    const maxPlanningCycles = continuous ? 4 : 1;
    const maxCodingAttempts = Math.min(continuous ? 12 : 6, p.dailyAttemptLimit);
    return api('/api/projects/' + p.id + '/objectives/' + id + '/delegate', {
      method: 'POST', body: JSON.stringify({ confirm: true, resumeAfterRestart: el('developmentResumeAfterRestart').checked,
        maxPlanningCycles, maxCodingAttempts })
    });
  }
  function confirmAssignment(p, continuous) {
    return window.confirm('Assign this work to Archer for up to 24 hours? He may use up to ' +
      (continuous ? 'four planning calls' : 'one planning call') + ' and ' +
      Math.min(continuous ? 12 : 6, p.dailyAttemptLimit) + ' coding attempts within your spending limits. Changes will be prepared for your review, not installed automatically.');
  }
  function accept(p, o) {
    if (window.confirm('Mark this objective accepted? Do this only after reviewing the result. This does not install changes and may release dependent assignments.'))
      command('/api/projects/' + p.id + '/objectives/' + o.id + '/accept', { confirm: true });
  }
  function reviewKey(b) { return b.id + ':' + b.workspaceHeadSha; }
  function renderReview(panel, p, b) {
    const key = reviewKey(b), record = reviews.get(key);
    panel.replaceChildren();
    if (record?.loading) { panel.append(element('p', 'Checking the completed work…')); return; }
    if (record?.error) panel.append(element('p', 'Could not complete review: ' + record.error));
    if (record?.candidate) {
      const c = record.candidate;
      panel.append(element('strong', 'Independent checks passed. Ready for your decision.'));
      panel.append(element('p', 'Review the changes before installing. Keep a known-good version and backup.'));
      const pr = link(c.pullRequestUrl, 'Review changes on GitHub', c.repository);
      if (pr) panel.append(pr);
      const evidence = disclosure('Technical verification');
      evidence.append(element('p', c.status), element('p', 'Completed version: ' + c.headSha),
        element('p', 'Original source baseline: ' + c.baselineSha), element('p', c.nextAction));
      for (const [url, label, expected] of [
        [c.baselineUrl, 'Original source', '/' + c.repository + '/commit/' + c.baselineSha],
        [c.compareUrl, 'Compare versions', '/' + c.repository + '/compare/' + c.baselineSha + '...' + c.headSha]
      ]) {
        try {
          const u = new URL(url);
          if (u.origin === 'https://github.com' && !u.username && !u.password && !u.search && !u.hash && u.pathname === expected) {
            const a = element('a', label); a.href = u.href; a.target = '_blank'; a.rel = 'noopener noreferrer';
            evidence.append(a);
          }
        } catch {}
      }
      panel.append(evidence);
    }
    panel.append(action(record?.candidate ? 'Check again' : 'Review completed work', async () => {
      if (reviews.get(key)?.loading) return;
      reviews.set(key, { loading: true }); render();
      try { reviews.set(key, { candidate: await api('/api/self-development/candidates/' + b.id) }); }
      catch (error) { reviews.set(key, { error: error.message }); }
      render();
    }));
  }
  function batchCard(p, b, o, historical = false) {
    const label = phase(b, o);
    const card = element('article', ''); card.className = 'dev-assignment-card';
    card.append(chip(label), element('h3', b.objective));
    const done = b.tasks.filter(t => t.status === 'Verified').length;
    const progress = document.createElement('progress'); progress.max = Math.max(1, b.tasks.length); progress.value = done;
    progress.setAttribute('aria-label', 'Verified steps');
    card.append(progress, element('p', done + ' of ' + b.tasks.length + ' steps checked'));
    const task = b.tasks.find(t => t.status === 'Blocked') || b.tasks.find(t => t.status !== 'Verified');
    const messages = {
      'Ready for review': 'All required checks passed. Review the result before installing it.',
      'Needs attention': 'Archer stopped safely. See the reason below before retrying.',
      'Checking the work': 'Archer has prepared changes and is checking the result.',
      'Working': 'Archer is preparing the changes.',
      'Planning': 'Archer is deciding what to do next.',
      'Queued': 'Archer will pick up this assignment automatically.',
      'Paused': 'Work is paused or the authorization has expired.',
      'Needs approval': 'The plan is ready. Approve it to let Archer begin.'
    };
    card.append(element('p', messages[label]));
    if (label === 'Needs attention' && task?.detail) card.append(element('p', task.detail));
    const buttons = element('div', ''); buttons.className = 'dev-actions';
    const path = '/api/development/batches/' + b.id;
    if (!historical && b.armed) buttons.append(action('Pause work', () => command(path + '/pause', { confirm: true })));
    if (!historical && ['Needs approval', 'Paused'].includes(label) && !b.continuous &&
        p.enabled && (!b.expiresAtUtc || Date.parse(b.expiresAtUtc) > Date.now()))
      buttons.append(primary('Start work', () => {
        if (window.confirm('Authorize this plan for up to 24 hours and two coding attempts per step, within your project and spending limits? No automatic installation.'))
          command(path + '/arm', { confirm: true, resumeAfterRestart: el('developmentResumeAfterRestart').checked });
      }));
    if (label === 'Ready for review' && !b.armed && !historical) {
      if (p.selfProject) {
        const panel = element('div', ''); panel.className = 'dev-review'; panel.setAttribute('role', 'status');
        renderReview(panel, p, b); card.append(panel);
      } else {
        const urls = [...new Set(b.tasks.map(t => t.pullRequestUrl).filter(Boolean))];
        for (const url of urls) { const a = link(url, 'Review proposed changes', b.repository); if (a) buttons.append(a); }
      }
      if (o && !o.acceptedAtUtc) buttons.append(action('Mark accepted', () => accept(p, o)));
    }
    if (buttons.children.length) card.append(buttons);
    const detail = disclosure('Plan and technical details');
    detail.append(element('p', (b.continuous ? 'Planning cycle ' + b.planningCycles : 'Single planning cycle') +
      ' · ' + b.attemptsRemaining + ' attempts remaining' + (b.expiresAtUtc ? ' · Until ' + new Date(b.expiresAtUtc).toLocaleString() : '')));
    if (b.workspaceHeadSha) detail.append(element('p', 'Draft head: ' + b.workspaceHeadSha));
    for (const t of b.tasks) {
      const d = disclosure(t.status + ' — ' + t.instruction);
      d.append(element('p', 'Editable: ' + (t.paths.join(', ') || 'None')),
        element('p', 'New files: ' + ((t.newPaths || []).join(', ') || 'None')),
        element('p', 'Read-only context: ' + ((t.contextPaths || []).join(', ') || 'None')),
        element('p', 'Attempts: ' + t.attempts + ' / 2'));
      if (t.detail) d.append(element('p', t.detail));
      if (t.sandbox) d.append(element('p', 'Isolated verification: ' + t.sandbox.status + ' · ' + t.sandbox.detail));
      const pr = link(t.pullRequestUrl, 'Current draft', b.repository); if (pr) d.append(pr);
      for (const head of t.priorHeadShas || []) d.append(element('p', 'Earlier attempted head: ' + head));
      for (const url of t.priorPullRequests || []) { const a = link(url, 'Previous failed draft (do not merge)', b.repository); if (a) d.append(a); }
      detail.append(d);
    }
    card.append(detail); return card;
  }
  function render() {
    const p = current();
    const audit = el('selfDevelopmentAudit'); audit.replaceChildren();
    if (selfAudit) {
      audit.append(element('p', 'Protected pilot · ' + (selfAudit.enabled ? 'External opt-in enabled' : 'External opt-in required')));
      for (const finding of selfAudit.findings || []) audit.append(element('p', finding.status + ' — ' + finding.detail));
      audit.append(element('p', 'Approved files: ' + ((selfAudit.approvedPaths || []).join(', ') || 'None')));
    }
    el('registerSelfProjectButton').disabled = !!selfAudit?.registered || busy;
    plan.disabled = !p?.enabled || busy;
    el('assignObjectiveButton').disabled = !p?.enabled || busy;
    el('saveObjectiveOnly').disabled = !p || busy;
    el('developmentContinuous').disabled = !p?.cumulativeWorkspaces;
    if (!p?.cumulativeWorkspaces) el('developmentContinuous').checked = false;
    el('pauseProjectButton').disabled = !p || busy;
    el('pauseProjectButton').textContent = p?.paused ? 'Resume project' : 'Pause project';
    el('managedProjectStatus').textContent = p ? (p.paused ? 'Paused' : p.enabled ? 'Ready to work' : 'Setup needed') : 'Choose or register a project';
    el('developmentTechnicalStatus').textContent = p ? p.repository + ' · ' + p.technology + ' · ' + p.attemptsToday + '/' + p.dailyAttemptLimit +
      ' attempts in the last 24h · Credential variables: ' + p.credentialPrefix + '_READ_TOKEN and _WRITE_TOKEN' : 'No project registered.';
    status.textContent = p?.enabled ? 'Archer prepares and checks changes. You decide whether to release them.' : 'Open Project settings to configure access before assigning work.';
    const objectives = el('projectObjectives'), memories = el('projectMemories'), history = el('developmentHistory'), overview = el('developmentOverview');
    const dependency = el('projectDependency'); const oldDep = dependency.value;
    objectives.replaceChildren(); memories.replaceChildren(); history.replaceChildren(); overview.replaceChildren(); list.replaceChildren();
    dependency.replaceChildren(option('', 'None'));
    if (!p) { list.append(element('p', 'No project selected. Open Project settings to add one.')); return; }
    const projectBatches = batches.filter(b => b.projectId === p.id);
    const verifiedDescriptions = new Set(projectBatches.filter(b => phase(b) === 'Ready for review' && !b.armed).map(b => b.objective.trim()));
    const cards = [];
    let accepted = 0, pending = 0, working = 0, review = 0, attention = 0;
    function placeBatch(b, o, past) {
      const label = phase(b);
      if (past) { history.append(batchCard(p, b, o, true)); return; }
      if (label === 'Ready for review') review++;
      else if (label === 'Needs attention' || label === 'Needs approval' || label === 'Paused') attention++;
      else working++;
      cards.push(batchCard(p, b, o));
    }
    for (const o of [...p.objectives].reverse()) {
      dependency.append(option(o.id, o.description.slice(0,100)));
      const related = projectBatches.filter(b => b.objectiveId === o.id).reverse();
      const ready = !o.acceptedAtUtc && o.dependsOn.every(id => p.objectives.some(other => other.id === id && other.acceptedAtUtc));
      const alreadyPlanned = related.some(b => b.armed || b.tasks.some(t => t.status !== 'Blocked'));
      const delegated = ['Delegated','Planning','Reassessment queued'].includes(o.planningState) || (o.continuous && o.planningState === 'Batch created');
      if (o.acceptedAtUtc) accepted++;
      if (related.length) {
        const currentBatch = related.find(b => b.armed) || related[0];
        placeBatch(currentBatch, o, !!o.acceptedAtUtc || (phase(currentBatch, o) === 'Needs attention' && verifiedDescriptions.has(o.description.trim())));
        for (const b of related.filter(b => b !== currentBatch)) placeBatch(b, o, true);
      } else {
        const label = !ready && !o.acceptedAtUtc ? 'Waiting on dependency' : objectivePhase(o);
        const card = element('article',''); card.className = 'dev-assignment-card';
        card.append(chip(label), element('h3', o.description));
        if (label === 'Needs attention') { attention++; card.append(element('p', o.planningDetail || 'Planning stopped.')); }
        else if (['Planning','Queued'].includes(label)) { working++; card.append(element('p', 'Archer is preparing the assignment.')); }
        else if (!o.acceptedAtUtc) pending++;
        if (delegated) card.append(action('Cancel assignment', () => command('/api/projects/' + p.id + '/objectives/' + o.id + '/cancel-delegation', { confirm: true })));
        if (!o.acceptedAtUtc && !o.planningAttempted && !delegated && ready) card.append(primary('Assign to Archer', async () => {
          if (busy || !confirmAssignment(p, false)) return;
          busy = true; try { await delegate(p,o.id,false); notice('Assignment authorized.'); } catch (e) { notice(e.message,true); } finally { busy = false; await refresh(); }
        }, !p.enabled));
        if (o.acceptedAtUtc) history.append(card);
        else if (label === 'Saved' || label === 'Waiting on dependency') objectives.append(card);
        else if (label === 'Needs attention' && verifiedDescriptions.has(o.description.trim())) { attention--; history.append(card); }
        else cards.push(card);
      }
      if (ready && !alreadyPlanned && !delegated && !o.continuous) {
        const recovery = disclosure('Manual planning: ' + o.description.slice(0,80));
        recovery.append(action('Create reviewed plan', () => {
          selectedObjective = o.id; objective.value = o.description; objective.readOnly = true; objective.focus();
          status.textContent = 'Saved assignment selected. Create a plan for review.';
        }, !p.enabled));
        objectives.append(recovery);
      }
      if (related.length && delegated && o.continuous) {
        const c = element('div',''); c.className = 'dev-actions';
        c.append(element('p', 'Continuous assignment: ' + o.planningCyclesUsed + '/' + o.maxPlanningCycles + ' planning cycles used'),
          action('Cancel assignment', () => command('/api/projects/' + p.id + '/objectives/' + o.id + '/cancel-delegation', { confirm: true })));
        cards.push(c);
      }
    }
    for (const b of projectBatches.filter(b => !b.objectiveId || !p.objectives.some(o => o.id === b.objectiveId)).reverse()) placeBatch(b, null, false);
    for (const card of cards) list.append(card);
    if (!cards.length) list.append(element('p', 'No active assignments. Tell Archer what to accomplish above.'));
    if (!history.children.length) history.append(element('p', 'No past attempts.'));
    if ([...dependency.options].some(o => o.value === oldDep)) dependency.value = oldDep;
    for (const [label, value] of [['Working',working],['Ready for review',review],['Needs attention',attention],['Saved',pending],['Accepted',accepted]]) {
      const metric = element('div',''); metric.append(element('strong',String(value)),element('span',label)); overview.append(metric);
    }
    for (const m of [...p.memory].reverse().slice(0,50)) {
      const d = disclosure(m.kind + ' · ' + new Date(m.createdAtUtc).toLocaleString());
      d.append(element('p',m.content),element('p',m.shareWithAi ? 'Available to AI' : 'Local only'),action('Remove memory', () => {
        if (window.confirm('Remove this local memory entry?')) command('/api/projects/' + p.id + '/memory/' + m.id, {}, 'DELETE');
      })); memories.append(d);
    }
  }
  async function refresh() {
    if (busy || refreshing) return;
    refreshing = true;
    try {
      const selected = picker.value;
      const next = await Promise.all([api('/api/projects'), api('/api/development/batches'), api('/api/self-development')]);
      const snapshot = JSON.stringify(next);
      if (snapshot === lastSnapshot) return;
      lastSnapshot = snapshot;
      [projects, { batches }, selfAudit] = next;
      picker.replaceChildren(...projects.map(p => option(p.id, p.name)));
      if (projects.some(p => p.id === selected)) picker.value = selected;
      render();
    } catch (error) { status.textContent = 'Could not load project state: ' + error.message; }
    finally { refreshing = false; }
  }
  el('registerSelfProjectButton').addEventListener('click', () => {
    if (window.confirm('Register Archer as a protected project? This grants no credentials or automatic deployment.'))
      command('/api/self-development/register', { confirm: true });
  });
  picker.addEventListener('change', () => { selectedObjective = null; objective.value = ''; objective.readOnly = false; render(); });
  el('projectRegistryForm').addEventListener('submit', event => {
    event.preventDefault();
    command('/api/projects', {
      name: el('projectName').value, repository: el('projectRepo').value, technology: el('projectTechnology').value,
      sourceRoots: el('projectRoots').value.split(',').map(x => x.trim()).filter(Boolean),
      workflowFile: el('projectWorkflow').value, verificationJob: el('projectJob').value,
      requiredSteps: el('projectSteps').value.split('\n').map(x => x.trim()).filter(Boolean),
      priority: Number(el('projectPriority').value), dailyAttemptLimit: Number(el('projectAttemptLimit').value), allowNewSourceFiles: el('projectAllowNewSource').checked, cumulativeWorkspaces: el('projectCumulativeWorkspaces').checked, regressionTestRoots: el('projectRegressionRoots').value.split(',').map(x => x.trim()).filter(Boolean), requireSandbox: el('projectRequireSandbox').checked
    }, 'POST', () => el('projectRegistryForm').reset());
  });
  el('pauseProjectButton').addEventListener('click', () => {
    const p = current(); if (p) command('/api/projects/' + p.id + '/pause', { paused: !p.paused });
  });
  async function saveAssignment(assign) {
    const p = current(), form = el('projectObjectiveForm'), feedback = el('objectiveSubmitStatus');
    if (!p || busy || (assign && !p.enabled) || !form.reportValidity()) return;
    const description = el('projectObjectiveText').value.trim();
    if (description.length < 15 || description.length > 1000) { feedback.textContent = 'Describe the result in 15–1000 characters.'; return; }
    const dep = el('projectDependency').value, continuous = p.cumulativeWorkspaces && el('developmentContinuous').checked;
    if (assign && !confirmAssignment(p, continuous)) return;
    busy = true; el('assignObjectiveButton').disabled = true; el('saveObjectiveOnly').disabled = true;
    feedback.textContent = 'Saving assignment…';
    try {
      const saved = await api('/api/projects/' + p.id + '/objectives', { method: 'POST', body: JSON.stringify({description, dependsOn: dep ? [dep] : []}) });
      el('projectObjectiveText').value = '';
      feedback.textContent = 'Saved. ' + (assign ? 'Authorizing Archer…' : 'You can assign it later.');
      if (assign) {
        try { await delegate(p, saved.id, continuous); feedback.textContent = 'Assigned. Archer will start when ready.'; }
        catch (e) { feedback.textContent = 'Saved, but not authorized: ' + e.message + ' Use Saved assignments to retry.'; }
      }
    } catch (e) { feedback.textContent = 'Could not save: ' + e.message; }
    finally { busy = false; lastSnapshot = ''; await refresh(); render(); }
  }
  el('projectObjectiveForm').addEventListener('submit', event => { event.preventDefault(); saveAssignment(true); });
  el('saveObjectiveOnly').addEventListener('click', () => saveAssignment(false));
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
  el('developmentRefreshButton').addEventListener('click', () => { lastSnapshot = ''; refresh(); });
  refresh();
  window.setInterval(() => {
    if (!document.hidden && !el('developerView').classList.contains('hidden')) refresh();
  }, 15000);
})();
