'use strict';

/*
 * Narrow, owner-reviewed local voice -> mission bridge.
 * No provider credentials, GitHub token, automatic planning or execution.
 * Web Speech uses explicit command grammar; Realtime may only call the two
 * allowlisted functions below. Creating a draft ALWAYS needs a screen
 * confirmation; spoken/model-supplied "yes" is never authorization.
 */
(() => {
  function parse(input) {
    const utterance = String(input ?? '').trim();
    if (utterance.length > 700) return null;
    const prefix = '(?:(?:hey|okay)\\s+)?(?:archer[,.:]?\\s+)?(?:please\\s+)?';
    const status = new RegExp('^' + prefix +
      '(?:list (?:my )?missions|show (?:me )?(?:my )?missions|' +
      "what(?:'s| is) (?:my |the )?mission status|" +
      'which missions are (?:active|running))[?.!]*$', 'i');
    if (status.test(utterance)) return {kind: 'list'};

    const creating = new RegExp('^' + prefix +
      '(?:create|make|add|set up)\\s+(?:a\\s+)?(?:new\\s+)?mission\\s+' +
      '(?:to|for|about)\\s+(.+)$', 'i');
    const match = creating.exec(utterance);
    if (!match) return null;
    const objective = match[1].replace(/[.!?]+$/, '').trim();
    if (objective.length < 10 || objective.length > 500) return null;
    const title = objective.slice(0, 90).trim();
    if (title.length < 3) return null;
    return {kind: 'propose', title, objective};
  }

  async function list() {
    // Owner-started local status call records the verified question/answer in
    // Archer's already-private conversation history. No AI model runs.
    const response = await fetch('/api/voice/mission-list', {
      method: 'POST', cache: 'no-store',
      headers: {'Content-Type': 'application/json'}
    });
    if (!response.ok) throw new Error('Mission status is unavailable.');
    const data = await response.json();
    if (data.status !== 'ok' || !Array.isArray(data.missions) ||
        typeof data.count !== 'number' || typeof data.reply !== 'string')
      throw new Error('Invalid mission status response.');
    return data;
  }

  async function propose(title, objective, options = {}) {
    if (options.signal?.aborted)
      return {status: 'declined', message: 'Voice session ended; no mission was created.'};
    if (typeof title !== 'string' || typeof objective !== 'string' ||
        title.trim().length < 3 || title.length > 100 ||
        objective.trim().length < 10 || objective.length > 500) {
      return {status: 'rejected', message: 'Mission title or objective is invalid.'};
    }
    // Every mutation needs deliberate physical on-screen owner approval.
    // Never infer consent from the model, a voice transcript or a tool call.
    if (!window.confirm(
      'Create this local mission draft?\n\n' +
      'Title: ' + title + '\n\n' +
      'Objective: ' + objective + '\n\n' +
      'This creates a draft ONLY. No planning, tasks, code changes or charges.' +
      '\n\nOnly approve when it is safe to use your screen.'
    )) {
      return {status: 'declined', message: 'Owner declined. No mission was created.'};
    }

    if (options.signal?.aborted)
      return {status: 'declined', message: 'Voice session ended; no mission was created.'};
    const response = await fetch('/api/missions', {
      method: 'POST',
      signal: options.signal,
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({title: title.trim(), objective: objective.trim()})
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok || typeof result.id !== 'string')
      throw new Error(result.error || 'Could not create the mission draft.');
    // UI refresh is passive and cannot plan, run or approve work.
    if (typeof window.ArcherOnMissionDraftCreated === 'function') {
      try { await window.ArcherOnMissionDraftCreated(result.id); }
      catch { /* mission was created; do not falsely report a failed mutation */ }
    }
    return {
      status: 'created',
      title: title.trim(),
      message: 'Mission draft created. Plan and task execution remain unapproved.'
    };
  }

  function describe(result) {
    if (result?.status === 'declined') return 'No mission created. Nothing was changed.';
    if (result?.status === 'rejected') return 'I could not create that mission draft. Please give a clearer objective.';
    if (result?.status === 'created')
      return 'Mission draft created. I have not planned or run any tasks.';
    if (result?.status === 'ok')
      return typeof result.reply === 'string' ? result.reply :
        'I checked your missions, but could not read the response.';
    return 'I could not complete that mission request.';
  }

  window.ArcherVoiceMissions = Object.freeze({parse, list, propose, describe});
})();