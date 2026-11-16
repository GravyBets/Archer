'use strict';

/* Archer v0.6 — consented owner context and website operations.
 * Presence is one-time, coarse and expiring. No background GPS, no push,
 * no customer/payment connections, and no outbound SMS or phone calls.
 */
(() => {
  const el = id => document.getElementById(id);
  const safe = v => String(v ?? '').replace(/[&<>"']/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  let pendingCoordinates = null;
  let presenceLoaded = false;
  let busy = false;

  const activity = el('ownerPresenceActivity');
  const place = el('ownerPresencePlace');
  const duration = el('ownerPresenceDuration');
  const shareAi = el('ownerPresenceShareAi');
  const status = el('ownerPresenceStatus');
  const locationStatus = el('ownerPresenceLocationStatus');

  async function loadPresence() {
    const result = await api('/api/presence');
    if (result.active && result.presence) {
      const p = result.presence;
      const expiry = new Date(p.expiresAtUtc);
      const date = expiry.toLocaleString();
      status.textContent = p.activity +
        (p.placeLabel ? ' · ' + p.placeLabel : '') +
        ' · expires ' + date +
        (p.shareWithAi ? ' · included when you chat with Archer' : ' · private; not sent to the AI');
      if (!presenceLoaded) {
        activity.value = p.activity;
        place.value = p.placeLabel || '';
        shareAi.checked = !!p.shareWithAi;
        const minsLeft = Math.max(1, Math.round((expiry - Date.now()) / 60000));
        duration.value = [15, 60, 240, 480].reduce(
          (best, option) => Math.abs(option - minsLeft) < Math.abs(best - minsLeft) ? option : best, 60);
        if (p.approxLatitude != null && p.approxLongitude != null) {
          pendingCoordinates = { latitude: p.approxLatitude, longitude: p.approxLongitude };
        }
      }
      locationStatus.textContent = p.approxLatitude != null
        ? 'One-time approximate location stored: ' + p.approxLatitude.toFixed(2) +
          ', ' + p.approxLongitude.toFixed(2) + '. No movement tracking.'
        : 'No GPS location saved. Click Share location once if you want to attach an approximate location.';
    } else {
      status.textContent = 'Nothing shared right now. Archer does not know your location or activity.';
      if (!presenceLoaded) pendingCoordinates = null;
      locationStatus.textContent = 'No location shared. One-time browser permission only; coordinates are rounded before saving.';
    }
    presenceLoaded = true;
  }

  el('ownerPresenceSaveButton').addEventListener('click', async () => {
    if (busy || appState.busy) return;
    busy = true;
    try {
      const data = {
        activity: activity.value,
        placeLabel: place.value.trim(),
        durationMinutes: Number(duration.value),
        shareWithAi: shareAi.checked,
        latitude: pendingCoordinates?.latitude ?? null,
        longitude: pendingCoordinates?.longitude ?? null
      };
      await api('/api/presence', { method: 'POST', body: JSON.stringify(data) });
      await loadPresence();
      notice('Presence saved. It will expire automatically; you can clear it at any time.');
    } catch (error) { notice(error.message, true); }
    finally { busy = false; }
  });

  el('ownerPresenceClearButton').addEventListener('click', async () => {
    if (busy || appState.busy) return;
    busy = true;
    try {
      await api('/api/presence', { method: 'DELETE' });
      pendingCoordinates = null;
      activity.value = 'Available';
      place.value = '';
      duration.value = '60';
      shareAi.checked = false;
      await loadPresence();
      notice('Activity and approximate location cleared.');
    } catch (error) { notice(error.message, true); }
    finally { busy = false; }
  });

  el('ownerPresenceLocationButton').addEventListener('click', () => {
    if (busy || appState.busy) return;
    if (!navigator.geolocation) {
      notice('Location is not supported in this browser. You can still set your activity manually.', true);
      return;
    }
    locationStatus.textContent = 'Requesting one-time location permission…';
    navigator.geolocation.getCurrentPosition(position => {
      // Coarse coordinate and no repeated location observations.
      pendingCoordinates = {
        latitude: Number(position.coords.latitude.toFixed(2)),
        longitude: Number(position.coords.longitude.toFixed(2))
      };
      locationStatus.textContent = 'Approximate coordinates ready: ' +
        pendingCoordinates.latitude.toFixed(2) + ', ' +
        pendingCoordinates.longitude.toFixed(2) +
        '. Not saved until you click Save context. No tracking is active.';
    }, error => {
      const names = { 1: 'Location permission denied.', 2: 'Location unavailable.', 3: 'Location request timed out.' };
      locationStatus.textContent = (names[error.code] || 'Location could not be determined.') +
        ' Activity can still be set manually.';
    }, { enableHighAccuracy: false, timeout: 12000, maximumAge: 0 });
  });

  const opsList = el('operationsMonitorList');
  const incidentsList = el('operationsIncidentList');
  let opsBusy = false;

  function renderOperations(data) {
    const monitors = data.monitors || [];
    const incidents = data.incidents || [];
    el('operationsStatus').textContent =
      monitors.filter(m => m.enabled).length + ' monitoring / ' + monitors.length + ' registered';
    opsList.innerHTML = monitors.length ? monitors.map(m => {
      const checked = m.lastCheckedAtUtc ? new Date(m.lastCheckedAtUtc).toLocaleString() : 'Never';
      return '<div class="operations-site">' +
        '<div><strong>' + safe(m.name) + '</strong><small>' + safe(m.url) + '</small>' +
        '<small>Status: ' + safe(m.status) + ' · Last checked: ' + safe(checked) +
        (m.lastHttpStatus ? ' · HTTP ' + m.lastHttpStatus : '') + '</small>' +
        (m.lastError ? '<small class="operations-warning">' + safe(m.lastError) + '</small>' : '') +
        '</div><button type="button" class="button secondary" data-monitor-id="' + safe(m.id) +
        '" data-enable="' + (!m.enabled) + '">' + (m.enabled ? 'Stop monitoring' : 'Start monitoring') +
        '</button></div>';
    }).join('') : '<div class="developer-empty">No websites yet. Add an approved public HTTPS site above.</div>';

    el('operationsIncidentCount').textContent = incidents.filter(x => !x.acknowledgedAtUtc).length;
    incidentsList.innerHTML = incidents.length ? incidents.map(i => {
      return '<div class="operations-incident"><div><strong>' + safe(i.kind) + ': ' +
        safe(i.websiteName) + '</strong><small>' + safe(new Date(i.occurredAtUtc).toLocaleString()) +
        '</small><p>' + safe(i.message) + '</p></div>' +
        (i.acknowledgedAtUtc
          ? '<span class="operations-ack">Acknowledged</span>'
          : '<button type="button" class="button secondary" data-incident-id="' +
            safe(i.id) + '">Acknowledge</button>') + '</div>';
    }).join('') : '<div class="developer-empty">No website availability incidents recorded.</div>';
  }

  async function refreshOperations() {
    if (opsBusy || appState.busy) return;
    const data = await api('/api/operations');
    renderOperations(data);
  }

  el('operationsAddForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      const name = el('operationsSiteName').value.trim();
      const url = el('operationsSiteUrl').value.trim();
      const response = await api('/api/operations/monitors', {
        method: 'POST', body: JSON.stringify({ name, url })
      });
      el('operationsAddForm').reset();
      notice(response.message);
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  opsList.addEventListener('click', async event => {
    const button = event.target.closest('button[data-monitor-id]');
    if (!button || opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      const result = await api('/api/operations/monitors/' +
        encodeURIComponent(button.dataset.monitorId) + '/enabled', {
        method: 'POST', body: JSON.stringify({ enabled: button.dataset.enable === 'true' })
      });
      notice(result.message);
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  incidentsList.addEventListener('click', async event => {
    const button = event.target.closest('button[data-incident-id]');
    if (!button || opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      await api('/api/operations/incidents/' +
        encodeURIComponent(button.dataset.incidentId) + '/acknowledge', { method: 'POST' });
      notice('Incident acknowledged.');
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  // No-charge business briefing. These are suggestions only, not sent messages.
  let briefingText = '';
  let briefingPrefsLoaded = false;
  let briefingBusy = false;

  function renderBriefing(data) {
    briefingText = data.briefingText || '';
    el('briefingBody').textContent = briefingText ||
      'There is no recorded local activity to include yet.';
    el('briefingGeneratedAt').textContent = 'As of ' +
      (data.localDateTime || 'now') + ' ' + (data.timeZoneId || '');
    el('briefingCopyButton').disabled = !briefingText;
    el('briefingQuietStatus').textContent = data.quietHoursNow
      ? 'Quiet hours active' : 'Outside quiet hours';

    const alerts = data.alerts || [];
    el('briefingAlertList').innerHTML = alerts.length
      ? alerts.map(item => {
          const priority = ['Critical', 'Attention', 'Information'].includes(item.priority)
            ? item.priority : 'Information';
          return '<div class="briefing-alert briefing-' + priority.toLowerCase() +
            '"><div><strong>' + safe(priority) + ' · ' + safe(item.title) +
            '</strong><p>' + safe(item.detail) + '</p>' +
            '<small>Source: ' + safe(item.source) + '. ' + safe(item.reason) +
            '</small></div><div class="briefing-alert-route">' +
            safe(item.recommendedDelivery) +
            '<small>PREVIEW ONLY</small></div></div>';
        }).join('')
      : '<p class="briefing-no-alerts">No active alerts from connected local sources. ' +
        'This does not mean business revenue or payments were verified.</p>';

    const usage = data.metrics?.usage || {};
    const requests = usage.requestsLast24Hours ?? 0;
    const sites = data.metrics?.websites?.enabled ?? 0;
    el('briefingMetricNotes').textContent =
      requests + ' tracked AI requests in the last 24h; ' +
      sites + ' active HTTPS monitors. Revenue, customers and payment information are not connected.';
  }

  async function loadBriefing() {
    if (briefingBusy || appState.busy) return;
    const data = await api('/api/briefing');
    renderBriefing(data);
  }

  async function loadBriefingPreferences() {
    if (briefingPrefsLoaded) return;
    const result = await api('/api/briefing/preferences');
    const prefs = result.preferences || {};
    el('briefingTimezone').value = prefs.timeZoneId || 'America/Chicago';
    el('briefingHour').value = prefs.dailyHourLocal ?? 8;
    el('briefingQuietStart').value = prefs.quietStartHourLocal ?? 22;
    el('briefingQuietEnd').value = prefs.quietEndHourLocal ?? 7;
    el('briefingCriticalOverride').checked = !!prefs.criticalOverrideQuietHours;
    briefingPrefsLoaded = true;
  }

  el('briefingRefreshButton').addEventListener('click', async () => {
    if (briefingBusy || appState.busy) return;
    briefingBusy = true;
    el('briefingRefreshButton').disabled = true;
    try {
      const data = await api('/api/briefing');
      renderBriefing(data);
      notice('Briefing refreshed from locally recorded data. Nothing was sent.');
    } catch (error) {
      notice('Briefing preview failed: ' + error.message, true);
    } finally {
      briefingBusy = false;
      el('briefingRefreshButton').disabled = false;
    }
  });

  el('briefingCopyButton').addEventListener('click', async () => {
    if (!briefingText) return;
    try {
      await navigator.clipboard.writeText(briefingText);
      notice('Draft briefing copied. No text message or call was sent.');
    } catch (error) {
      notice('Could not copy the briefing in this browser: ' + error.message, true);
    }
  });

  el('briefingPreferencesForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (briefingBusy || appState.busy) return;
    briefingBusy = true;
    try {
      const data = {
        timeZoneId: el('briefingTimezone').value,
        dailyHourLocal: Number(el('briefingHour').value),
        quietStartHourLocal: Number(el('briefingQuietStart').value),
        quietEndHourLocal: Number(el('briefingQuietEnd').value),
        criticalOverrideQuietHours: el('briefingCriticalOverride').checked
      };
      const response = await api('/api/briefing/preferences', {
        method: 'POST', body: JSON.stringify(data)
      });
      el('briefingPreferenceStatus').textContent =
        'Briefing preferences saved. Separate communication delivery consent is unchanged.';
      notice(response.message);
      renderBriefing(await api('/api/briefing'));
    } catch (error) {
      notice('Briefing settings were not saved: ' + error.message, true);
    } finally { briefingBusy = false; }
  });

  window.OperatorContext = {
    refresh: async () => {
      await Promise.all([
        loadPresence(), refreshOperations(), loadBriefing(), loadBriefingPreferences()
      ]);
    }
  };
  window.OperatorContext.refresh().catch(error =>
    console.warn('Archer operator context could not load:', error.message));

  window.setInterval(() => {
    if (document.hidden || appState.busy) return;
    if (!el('archerView').classList.contains('hidden'))
      loadPresence().catch(console.warn);
    if (!el('operationsView').classList.contains('hidden')) {
      refreshOperations().catch(console.warn);
      loadBriefing().catch(console.warn);
    }
  }, 30000);
})();