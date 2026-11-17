'use strict';

/*
 * Archer Pocket — *owner sign-in only*.
 * Client-side success is NOT Archer device authorization. No token is
 * displayed, persisted by application code, posted to GitHub or passed to an
 * Archer gateway. There is no chat/mission/fetch endpoint here.
 */
(() => {
  const byId = id => document.getElementById(id);
  const signIn = byId('signInButton');
  const signOut = byId('signOutButton');
  const status = byId('identityMessage');
  const capability = byId('authCapability');
  const tag = byId('authTag');
  const badge = byId('connectionBadge');
  const settings = window.ArcherPocketSettings ?? {};
  const EXPECTED_DOMAIN = 'dev-1l3ic41kxj7zpeb8.us.auth0.com';
  const EXPECTED_AUDIENCE = 'https://archer.example/api/dev';
  const SCOPES = 'openid profile email conversation:read';

  let client = null;
  let busy = false;

  function setMessage(message, isError = false) {
    status.textContent = message;
    status.classList.toggle('problem', isError);
  }

  function setBusy(value) {
    busy = value;
    signIn.disabled = value || !client;
    signOut.disabled = value || !client;
  }

  function setSignedIn(value) {
    signIn.hidden = value;
    signOut.hidden = !value;
    signIn.disabled = busy || value || !client;
    signOut.disabled = busy || !value || !client;
    tag.textContent = value ? 'AUTH0 ONLY' : 'PENDING';
    tag.classList.toggle('ready', value);
    capability.textContent = value
      ? 'Auth0 session found. Archer device is not approved.'
      : 'Not yet verified on this device';
    badge.textContent = value ? 'AUTH0 SIGNED IN' : 'LOCAL PREVIEW';
    if (value) {
      setMessage('Signed in to Auth0. Your iPhone is NOT yet enrolled with Archer and remote access remains disabled.');
    }
  }

  function validSettings() {
    if (!window.isSecureContext) return false;
    if (!settings || settings.domain !== EXPECTED_DOMAIN ||
        settings.audience !== EXPECTED_AUDIENCE ||
        typeof settings.clientId !== 'string' ||
        !/^[A-Za-z0-9_-]{12,128}$/.test(settings.clientId)) return false;
    // The page must be served over HTTPS, except browser localhost developer
    // previews. Do not point to your Windows PC's unauthenticated desktop API.
    return window.location.protocol === 'https:' ||
      (window.location.protocol === 'http:' &&
       ['localhost', '127.0.0.1'].includes(window.location.hostname));
  }

  function redirectUri() {
    // Exact, canonical callback; do not accept URL query parameters,
    // fragments, or arbitrary redirect destinations as an OAuth returnTo.
    return window.location.origin + window.location.pathname;
  }

  async function init() {
    if ('serviceWorker' in navigator &&
        window.location.protocol === 'https:') {
      // The worker only caches public static shell files and never intercepts
      // OAuth code/state callback requests, tokens or API responses.
      navigator.serviceWorker.register('./sw.js').catch(() => {});
    }

    if (!validSettings()) {
      setMessage('Preview prepared. Create a separate Auth0 Single Page Application, configure its exact HTTPS callback, then set its public Client ID in config.js. No server connection is active.', true);
      capability.textContent = 'Waiting for approved SPA setup and HTTPS hosting';
      return;
    }
    if (!window.auth0 || typeof window.auth0.createAuth0Client !== 'function') {
      setMessage('Auth0 login library could not load. Check your connection and try again.', true);
      return;
    }

    try {
      setMessage('Opening secure Auth0 sign-in readiness check…');
      client = await window.auth0.createAuth0Client({
        domain: settings.domain,
        clientId: settings.clientId,
        cacheLocation: 'memory',
        useRefreshTokens: false,
        authorizationParams: {
          audience: settings.audience,
          scope: SCOPES,
          redirect_uri: redirectUri()
        }
      });

      const query = new URLSearchParams(window.location.search);
      if (query.has('error')) {
        // Never display OAuth callback details or a raw server error.
        window.history.replaceState(null, '', window.location.pathname);
        setMessage('Auth0 sign-in did not complete. You can retry without changing any security settings.', true);
        setSignedIn(false);
      } else if (query.has('code') || query.has('state')) {
        if (!query.has('code') || !query.has('state')) {
          throw new Error('Incomplete OAuth callback');
        }
        try {
          await client.handleRedirectCallback();
        } finally {
          // Remove the one-time authorization code and state from browser URL.
          window.history.replaceState(null, '', window.location.pathname);
        }
        setSignedIn(await client.isAuthenticated());
      } else {
        // A new Home Screen app may use a different cookie/storage partition
        // than Safari. Do not infer remote authentication from local cookies.
        setSignedIn(await client.isAuthenticated());
        if (!await client.isAuthenticated()) {
          setMessage('Ready. Sign in using your Archer Auth0 owner account. Passkeys will be offered by Auth0 if configured.');
        }
      }
    } catch {
      // Do not leak callback codes, URL query strings or tokens in logs/UI.
      client = null;
      setSignedIn(false);
      setMessage('Sign-in could not be completed. Check SPA callback, web origin and app access settings; no device access was granted.', true);
    } finally {
      signIn.disabled = !client;
    }
  }

  signIn.addEventListener('click', async () => {
    if (busy || !client) return;
    setBusy(true);
    try {
      await client.loginWithRedirect({
        authorizationParams: {
          audience: EXPECTED_AUDIENCE,
          scope: SCOPES,
          redirect_uri: redirectUri()
        }
      });
    } catch {
      setMessage('Unable to open Auth0 sign-in. Try again using a secure connection.', true);
      setBusy(false);
    }
  });

  signOut.addEventListener('click', async () => {
    if (busy || !client) return;
    setBusy(true);
    try {
      // SDK clears its in-memory session; Auth0 then handles browser logout.
      await client.logout({logoutParams: {returnTo: redirectUri()}});
    } catch {
      setMessage('Auth0 sign-out did not complete. Close the app and try again.', true);
      setBusy(false);
    }
  });

  init();
})();
