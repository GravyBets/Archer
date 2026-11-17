'use strict';

// Public OAuth identifiers only. Do not add an API key or client secret.
// An Archer Pocket Dev application of type "Single Page Application" is
// required. The existing Archer iPhone Dev Native Client ID is NOT reusable.
// Leave the clientId empty until the owner configures the SPA, its HTTPS URL,
// its exact Callback/Logout/Web Origins and User-Delegated API access.
window.ArcherPocketSettings = Object.freeze({
  domain: 'dev-113ic41kxj7zpeb8.us.auth0.com',
  audience: 'https://archer.example/api/dev',
  clientId: 'Q25ebj5IT14zwo63SP7920S25m21Rjgm'
});
