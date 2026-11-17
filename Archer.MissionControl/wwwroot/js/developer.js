'use strict';

/*
 * Archer Developer Workspace:
 * read-only GitHub repository inventory + code previews.
 * No AI calls or repository writes are made from this module.
 */
(() => {
  const el = id => document.getElementById(id);
  const repoInput = el('developerRepository');
  const loadButton = el('developerLoad');
  const inspectArcherButton = el('developerArcherButton');
  const filter = el('developerFilter');
  const list = el('developerFiles');
  const previewButton = el('developerPreviewButton');
  const reviewButton = el('developerReviewButton');
  const previewArea = el('developerPreview');
  const status = el('developerStatus');

  let repositoryUrl = '';
  let inventory = null;
  let busy = false;
  const selected = new Set();

  const safe = value => String(value ?? '').replace(/[&<>"']/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  function statusMessage(message, error = false) {
    status.textContent = message;
    status.classList.toggle('developer-status-error', error);
  }

  function updateButtons() {
    const count = selected.size;
    el('developerSelectedCount').textContent = count + ' / 6';
    const enabled = inventory && count > 0 && !busy && !appState.busy;
    previewButton.disabled = !enabled;
    reviewButton.disabled = !enabled;
  }

  function clearRepository() {
    inventory = null;
    repositoryUrl = '';
    selected.clear();
    list.innerHTML = '<div class="developer-empty">Load a repository to browse its source files.</div>';
    previewArea.innerHTML = '<div class="developer-empty">Choose files and preview actual code here.</div>';
    el('developerBranch').textContent = '—';
    el('developerTree').textContent = '—';
    el('developerPreviewMeta').textContent = 'Actual source from GitHub, not an AI-generated summary';
    updateButtons();
  }

  function renderFiles() {
    if (!inventory) return;
    const term = filter.value.trim().toLowerCase();
    const files = inventory.files.filter(file => file.path.toLowerCase().includes(term));
    list.innerHTML = files.length
      ? files.map(file => '<label class="developer-file-choice"><input type="checkbox" ' +
        'data-source-path="' + safe(file.path) + '" ' +
        (selected.has(file.path) ? 'checked ' : '') +
        'aria-label="Select ' + safe(file.path) + '">' +
        '<span class="developer-file-name">' + safe(file.path) + '</span>' +
        '<span class="developer-file-size">' + Math.ceil(file.size / 1024) + ' KB</span></label>').join('')
      : '<div class="developer-empty">No matching source files.</div>';
    updateButtons();
  }

  function selectedPaths() {
    if (!inventory) return [];
    // Preserve repository path order for consistent previews and mission objectives.
    return inventory.files.filter(x => selected.has(x.path)).map(x => x.path);
  }

  repoInput.addEventListener('input', () => {
    if (repositoryUrl && repoInput.value.trim() !== repositoryUrl) {
      clearRepository();
      statusMessage('Repository URL changed. Click Load files to reconnect.');
    }
  });

  loadButton.addEventListener('click', async () => {
    if (busy || appState.busy) return;
    busy = true;
    loadButton.disabled = true;
    selected.clear();
    updateButtons();
    statusMessage('Reading the allowlisted GitHub repository…');
    try {
      const url = repoInput.value.trim();
      const data = await api('/api/development/repository?url=' + encodeURIComponent(url));
      inventory = data;
      repositoryUrl = url;
      el('developerBranch').textContent = data.branch;
      el('developerTree').textContent = (data.treeSha || '').slice(0, 12) || '—';
      el('developerPreviewMeta').textContent = 'Select files to inspect actual content';
      previewArea.innerHTML = '<div class="developer-empty">Select source files to preview.</div>';
      filter.value = '';
      statusMessage('Connected to ' + data.repository + '. ' + data.totalReviewableFiles +
        ' reviewable source files.' +
        (data.truncated ? ' Only the first 300 paths are shown.' : ''));
      renderFiles();
    } catch (error) {
      clearRepository();
      statusMessage(error.message, true);
      notice('Developer Workspace could not read GitHub: ' + error.message, true);
    } finally {
      busy = false;
      loadButton.disabled = false;
      updateButtons();
    }
  });

  // Safe self-review shortcut: identical read-only allowlist validation as
  // manually entered URLs. No AI call, file modifications or auto-publishing.
  inspectArcherButton.addEventListener('click', () => {
    if (busy || appState.busy) return;
    repoInput.value = 'https://github.com/GravyBets/Archer';
    repoInput.dispatchEvent(new Event('input', { bubbles: true }));
    loadButton.click();
  });

  filter.addEventListener('input', renderFiles);

  list.addEventListener('change', event => {
    const box = event.target.closest('input[data-source-path]');
    if (!box || !inventory) return;
    const path = box.dataset.sourcePath;
    if (box.checked && selected.size >= 6) {
      box.checked = false;
      notice('Choose at most six files per focused source review.', true);
      return;
    }
    if (box.checked) selected.add(path);
    else selected.delete(path);
    updateButtons();
  });

  previewButton.addEventListener('click', async () => {
    if (!inventory || busy || appState.busy) return;
    const paths = selectedPaths();
    if (paths.length < 1 || paths.length > 6) return;
    busy = true;
    updateButtons();
    statusMessage('Loading actual source files from GitHub…');
    try {
      const result = await api('/api/development/preview', {
        method: 'POST',
        body: JSON.stringify({ repository: repositoryUrl, paths })
      });
      previewArea.replaceChildren();
      for (const file of result.files) {
        const section = document.createElement('section');
        section.className = 'developer-code-file';

        const heading = document.createElement('div');
        heading.className = 'developer-code-heading';
        const name = document.createElement('strong');
        name.textContent = file.path;
        heading.appendChild(name);

        if (file.url) {
          const link = document.createElement('a');
          link.href = file.url;
          link.target = '_blank';
          link.rel = 'noopener noreferrer';
          link.textContent = 'View on GitHub';
          heading.appendChild(link);
        }
        section.appendChild(heading);

        if (file.truncated) {
          const warning = document.createElement('p');
          warning.className = 'developer-truncation';
          warning.textContent = 'Partial file preview. Inspect GitHub for the remaining lines.';
          section.appendChild(warning);
        }

        const pre = document.createElement('pre');
        pre.className = 'developer-code-text';
        pre.textContent = file.content.split(/\r?\n/).map((line, i) =>
          String(i + 1).padStart(4, ' ') + ' │ ' + line).join('\n');
        section.appendChild(pre);
        previewArea.appendChild(section);
      }
      el('developerPreviewMeta').textContent = result.files.length + ' files · ' +
        result.branch + ' · tree ' + (result.treeSha || '').slice(0, 12) +
        (result.partial ? ' · partial preview' : '');
      statusMessage('Loaded ' + result.files.length + ' source file previews. No AI credits used.');
    } catch (error) {
      statusMessage(error.message, true);
      notice('Source preview failed: ' + error.message, true);
    } finally {
      busy = false;
      updateButtons();
    }
  });

  reviewButton.addEventListener('click', () => {
    if (!inventory || appState.busy || busy) return;
    const paths = selectedPaths();
    if (paths.length < 1 || paths.length > 6) return;
    const name = inventory.repository.split('/').at(-1);
    el('missionTitle').value = (name + ': Focused code review').slice(0, 100);
    el('missionObjective').value = [
      'Review actual source code from https://github.com/' + inventory.repository + '.',
      'SOURCE FILES: ' + paths.join(', '),
      'Inspect these exact files and identify at most three prioritized, testable improvements.',
      'Check whether each safeguard already exists before claiming a defect.',
      'Cite the inspected file and observed code. Classify findings as verified, possible, or already handled.',
      'Do not claim code was changed, compiled, or tested. Do not make any GitHub modifications.'
    ].join('\n').slice(0, 3000);
    el('missionDialog').showModal();
    statusMessage('Review mission prepared. You decide whether to create and execute it.');
  });

  window.ArcherDeveloper = {
    getCodeSelection: () => ({
      repository: inventory ? 'https://github.com/' + inventory.repository : '',
      paths: selectedPaths()
    })
  };

  updateButtons();
})();