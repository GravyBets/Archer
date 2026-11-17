'use strict';

// Public OAuth identifiers only. Do not add an API key or client secret.
// An Archer Pocket Dev application of type "Single Page Application" is
// required. The existing Archer iPhone Dev Native Client ID is NOT reusable.
// The public SPA client ID below was provided in owner setup. This remains a
// development-only public browser client. GitHub Pages is already deployed,
// but changes require review, merge and another manual deploy.
window.ArcherPocketSettings = Object.freeze({
  domain: 'dev-1l3ic41kxj7zpeb8.us.auth0.com',
  audience: 'https://archer.example/api/dev',
  clientId: 'Q25ebj5IT14zwo63SP792OS25m21Rjgm'
});
