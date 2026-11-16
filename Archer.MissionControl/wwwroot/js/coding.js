'use strict';

/* Explicit owner-reviewed code proposals. Nothing is published automatically. */
(() => {
  const el = id => document.getElementById(id);
  const generateButton = el('codingGenerateButton');
  const publishButton = el('codingPublishButton');
  const status = el('codingStatus');
  const publishStatus = el('codingPublishStatus');
  const preview = el('codingProposalPreview');
  let latest = null;
  let publishingEnabled = false;
  let busy = false;

  function renderProposal(proposal) {
    latest = proposal;
    preview.replaceChildren();
    if (!proposal) {
      const empty = document.createElement('p');
      empty.className = 'developer-empty';
      empty.textContent = 'No code proposal yet. A model-generated draft is not a tested fix.';
      preview.appendChild(empty);
      publishButton.disabled = true;
      return;
    }

    const title = document.createElement('h3');
    title.textContent = proposal.title + ' · ' + proposal.status;
    preview.appendChild(title);
    const summary = document.createElement('p');
    summary.textContent = proposal.summary;
    preview.appendChild(summary);
    const tests = document.createElement('p');
    tests.className = 'coding-tests';
    tests.textContent = 'Recommended tests (not run): ' + proposal.testRecommendations;
    preview.appendChild(tests);
    const repo = document.createElement('p');
    repo.className = 'coding-source-meta';
    repo.textContent = proposal.repository + ' · ' + proposal.baseBranch +
      ' · ' + proposal.files.length + ' changed file(s).';
    preview.appendChild(repo);

    for (const file of proposal.files) {
      const section = document.createElement('details');
      section.className = 'coding-file-diff';
      const heading = document.createElement('summary');
      heading.textContent = file.path + ' · inspect original and proposed source';
      section.appendChild(heading);

      const beforeTitle = document.createElement('h4');
      beforeTitle.textContent = 'Original source';
      section.appendChild(beforeTitle);
      const beforeCode = document.createElement('pre');
      beforeCode.textContent = file.originalContent;
      section.appendChild(beforeCode);

      const afterTitle = document.createElement('h4');
      afterTitle.textContent = 'Proposed source — not built or tested';
      section.appendChild(afterTitle);
      const afterCode = document.createElement('pre');
      afterCode.textContent = file.proposedContent;
      section.appendChild(afterCode);
      preview.appendChild(section);
    }

    if (proposal.pullRequestUrl) {
      const link = document.createElement('a');
      link.href = proposal.pullRequestUrl;
      link.rel = 'noopener noreferrer';
      link.target = '_blank';
      link.textContent = 'Open draft pull request on GitHub';
      preview.appendChild(link);
    }

    publishButton.disabled = busy || !publishingEnabled || proposal.status !== 'Draft';
    publishStatus.textContent = proposal.status === 'Draft PR'
      ? 'Draft PR exists. Review GitHub checks; only you may approve any merge.'
      : publishingEnabled
        ? 'Ready for your explicit confirmation. This will create a branch and draft PR, never a merge.'
        : 'Publishing is disabled. Configure ARCHER_CODING_ENABLED=true, ARCHER_GITHUB_WRITE_REPOS and a fine-grained GitHub write token on your PC.';
  }

  async function refresh() {
    try {
      const result = await api('/api/development/code-proposals');
      publishingEnabled = !!result.publishingEnabled;
      const proposed = result.proposals || [];
      renderProposal(proposed[0] || null);
    } catch (error) {
      status.textContent = 'Could not load code drafts: ' + error.message;
    }
  }

  generateButton.addEventListener('click', async () => {
    if (busy || appState.busy) return;
    const selection = window.ArcherDeveloper?.getCodeSelection?.();
    const paths = selection?.paths || [];
    const instruction = el('codingInstruction').value.trim();
    if (!selection?.repository || paths.length < 1 || paths.length > 2) {
      notice('Load GeoShift and select one or two .cs or .razor files first.', true);
      return;
    }
    if (instruction.length < 15 || instruction.length > 1200) {
      notice('Give Archer a clear change request between 15 and 1,200 characters.', true);
      return;
    }
    if (!window.confirm(
      'Generate an AI code proposal using ' + paths.length + ' actual source file(s)? ' +
      'This sends selected source to OpenAI and uses your local spending allowance. No GitHub changes will be made.'))
      return;

    busy = true;
    generateButton.disabled = true;
    status.textContent = 'Generating with the code model. AI tokens are billable…';
    try {
      const result = await api('/api/development/code-proposals', {
        method: 'POST',
        body: JSON.stringify({ repository: selection.repository, paths, instruction })
      });
      renderProposal(result.proposal);
      status.textContent = 'Local draft created. Inspect every changed file before publishing.';
      window.SpendingDashboard?.refresh();
      notice(result.message);
    } catch (error) {
      status.textContent = 'Code proposal failed: ' + error.message;
      notice(error.message, true);
    } finally {
      busy = false;
      generateButton.disabled = false;
      publishButton.disabled = !latest || latest.status !== 'Draft' || !publishingEnabled;
    }
  });

  publishButton.addEventListener('click', async () => {
    if (busy || appState.busy || !latest || latest.status !== 'Draft' || !publishingEnabled) return;
    const repo = latest.repository;
    const files = latest.files.map(x => x.path).join('\n');
    if (!window.confirm(
      'APPROVE GITHUB WRITE?\n\nRepository: ' + repo +
      '\nFiles:\n' + files +
      '\n\nCreate an isolated branch and a DRAFT pull request. No merge or deployment will occur.'))
      return;
    busy = true;
    publishButton.disabled = true;
    publishStatus.textContent = 'Creating branch, committing proposed files and opening draft PR…';
    try {
      const result = await api(
        '/api/development/code-proposals/' + encodeURIComponent(latest.id) + '/publish',
        { method: 'POST', body: JSON.stringify({ confirm: true }) }
      );
      publishStatus.textContent = 'Draft PR opened. Check its diff and CI before considering a merge.';
      notice(result.message);
      await refresh();
    } catch (error) {
      publishStatus.textContent = 'Publish failed. Inspect GitHub manually before trying again.';
      notice(error.message, true);
      await refresh();
    } finally { busy = false; }
  });

  window.ArcherCoding = { refresh };
  refresh();
})();