'use strict';

// Public OAuth identifiers only. Do not add an API key or client secret.
// An Archer Pocket Dev application of type "Single Page Application" is
// required. The existing Archer iPhone Dev Native Client ID is NOT reusable.
// The public SPA client ID below was provided in owner setup. This remains a
// review-only build; the GitHub Pages URL and Auth0 allowed origins still
// require dashboard configuration and explicit manual deployment.
window.ArcherPocketSettings = Object.freeze({
  domain: 'dev-113ic41kxj7zpeb8.us.auth0.com',
  audience: 'https://archer.example/api/dev',
  clientId: 'Q25ebj5IT14zwo63SP7920S25m21Rjgm'
});
