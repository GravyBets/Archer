'use strict';

// Offline UI shell only. Never cache OAuth redirects, token requests, user
// data, Auth0 SDK responses, runtime configuration or Archer gateway traffic.
const CACHE = 'archer-pocket-shell-v2';
const FILES = ['./', './index.html', './styles.css', './app.js',
               './manifest.webmanifest', './assets/archer-icon.svg'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE)
    .then(cache => cache.addAll(FILES))
    .then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(key => key !== CACHE &&
      key.startsWith('archer-pocket-shell-')).map(key => caches.delete(key))))
    .then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  const scope = new URL(self.registration.scope);
  if (url.origin !== scope.origin || !url.pathname.startsWith(scope.pathname)) return;

  // OAuth callbacks use ?code= and ?state=; NEVER cache or intercept those.
  if (url.search || url.hash) return;

  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request)
      .catch(() => caches.match('./index.html')));
    return;
  }

  const safePaths = new Set(
    FILES.slice(2).map(path => new URL(path, self.registration.scope).pathname)
  );
  if (!safePaths.has(url.pathname)) return;
  // Prefer freshly deployed JS and CSS. A cache-first app.js could keep
  // pointing an installed iPhone PWA at a removed/misspelled Auth0 hostname.
  // No OAuth callback or credential response reaches this handler.
  event.respondWith(fetch(event.request)
    .then(response => {
      if (response.ok && response.type === 'basic') {
        const copy = response.clone();
        event.waitUntil(caches.open(CACHE)
          .then(cache => cache.put(event.request, copy)));
      }
      return response;
    })
    .catch(() => caches.match(event.request)));
});
