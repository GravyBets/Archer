'use strict';

/*
 * Archer Pocket device-key PREPARATION ONLY.
 * A non-extractable WebCrypto P-256 private key lives in this browser origin's
 * IndexedDB; there is NO network pairing, server approval, message API, or
 * guarantee that Safari/iOS backs WebCrypto keys with Secure Enclave hardware.
 *
 * Keys are per-origin browser storage, may be lost on Safari data clearing,
 * and cannot themselves prove Auth0 owner identity. Only a future backend
 * authenticated with Auth0 and explicit owner approval may enroll a key.
 */
(() => {
  const KEY_DB = 'archer-pocket-device-v1';
  const STORE = 'device-keys';
  const SLOT = 'primary';
  const encoder = new TextEncoder();
  const SUBTLE = () => globalThis.crypto?.subtle;
  const MAX_LABEL = 80;

  function assertSecure() {
    if (!globalThis.isSecureContext || !SUBTLE() || !globalThis.indexedDB)
      throw new Error('Secure browser storage is unavailable.');
  }

  function hex(bytes) {
    return Array.from(new Uint8Array(bytes), x => x.toString(16).padStart(2, '0')).join('');
  }

  function base64url(bytes) {
    const array = new Uint8Array(bytes);
    let raw = '';
    for (const byte of array) raw += String.fromCharCode(byte);
    return btoa(raw).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  }

  function openDb() {
    assertSecure();
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(KEY_DB, 1);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(STORE))
          db.createObjectStore(STORE);
      };
      request.onerror = () => reject(new Error('Device storage cannot be opened.'));
      request.onblocked = () => reject(new Error('Device storage upgrade was blocked.'));
      request.onsuccess = () => resolve(request.result);
    });
  }

  async function transaction(method, item) {
    const db = await openDb();
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(STORE, method === 'get' ? 'readonly' : 'readwrite');
        const store = tx.objectStore(STORE);
        let request;
        if (method === 'get') request = store.get(SLOT);
        if (method === 'add') request = store.add(item, SLOT);
        if (method === 'delete') request = store.delete(SLOT);
        if (!request) return reject(new Error('Unknown operation.'));
        let result;
        request.onsuccess = () => { result = request.result; };
        request.onerror = () => reject(new Error('Device storage operation failed.'));
        tx.oncomplete = () => resolve(result);
        tx.onabort = () => reject(new Error('Device storage transaction aborted.'));
        tx.onerror = () => reject(new Error('Device storage transaction failed.'));
      });
    } finally {
      db.close();
    }
  }

  function checkStored(record) {
    if (!record || record.version !== 1 ||
        !record.privateKey || !record.publicKey ||
        record.privateKey.type !== 'private' ||
        record.privateKey.extractable !== false ||
        record.publicKey.type !== 'public' ||
        record.privateKey.algorithm?.name !== 'ECDSA' ||
        record.privateKey.algorithm?.namedCurve !== 'P-256' ||
        record.publicKey.algorithm?.namedCurve !== 'P-256' ||
        !record.privateKey.usages.includes('sign') ||
        !record.publicKey.usages.includes('verify') ||
        !(record.publicSpki instanceof ArrayBuffer) ||
        record.publicSpki.byteLength < 80 ||
        record.publicSpki.byteLength > 256 ||
        typeof record.createdAt !== 'string')
      throw new Error('Saved device key does not meet Archer requirements.');
    return record;
  }

  async function read() {
    assertSecure();
    const existing = await transaction('get');
    return existing ? checkStored(existing) : null;
  }

  async function create() {
    assertSecure();
    // A second click must not silently replace the first key: an approved
    // device's identity must remain stable until the user explicitly erases.
    if (await read()) throw new Error('A device key already exists in this browser.');
    const pair = await SUBTLE().generateKey(
      { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign', 'verify']);
    if (pair.privateKey.extractable !== false)
      throw new Error('Private key was unexpectedly exportable.');
    const publicSpki = await SUBTLE().exportKey('spki', pair.publicKey);
    const record = {
      version: 1,
      createdAt: new Date().toISOString(),
      publicSpki,
      publicKey: pair.publicKey,
      privateKey: pair.privateKey
    };
    // Structured-clone persistence must succeed; never report success from
    // a volatile memory key. No private-key bytes are ever exported.
    await transaction('add', record);
    return checkStored(await read());
  }

  async function erase() {
    assertSecure();
    await transaction('delete');
    if (await read()) throw new Error('Local key erase could not be verified.');
  }

  async function fingerprint(record) {
    checkStored(record);
    const bytes = await SUBTLE().digest('SHA-256', record.publicSpki);
    return hex(bytes).toUpperCase();
  }

  function pairingPayload(ownerSubject, ticketId, nonce) {
    // Exact wire-domain matches the .NET offline pairing contract.
    // ownerSubject, ticketId, nonce must ultimately come from a
    // server-authenticated, freshly issued pairing challenge.
    const fields = [ownerSubject, ticketId, nonce];
    if (!fields.every(x => typeof x === 'string' &&
        x.length > 0 && x.length <= 256 &&
        !/[\r\n\0]/.test(x)))
      throw new Error('Invalid server pairing challenge.');
    return encoder.encode(
      'ARCHER_DEVICE_PAIR_P256_V1\n' +
      ownerSubject + '\n' + ticketId + '\n' + nonce);
  }

  // Future read-only remote gateway proof. This signs a fresh SERVER-issued
  // ticket for exactly one GET path, owner and enrolled device identifier.
  // The existing Pocket UI never calls it until authenticated remote
  // enrollment and trusted HTTPS ingress are independently enabled.
  function readProofPayload(ownerSubject, deviceId, requestPath, ticketId, nonce) {
    if (typeof ownerSubject !== 'string' ||
        !ownerSubject.trim() || ownerSubject.length > 128 ||
        /[\\r\\n\\0]/.test(ownerSubject) ||
        typeof deviceId !== 'string' || !/^[A-F0-9]{48}$/.test(deviceId) ||
        !['/v1/conversation/messages', '/v1/inbox'].includes(requestPath) ||
        typeof ticketId !== 'string' || !/^[A-F0-9]{48}$/.test(ticketId) ||
        typeof nonce !== 'string' || !/^[A-F0-9]{64}$/.test(nonce))
      throw new Error('Invalid authenticated read request challenge.');
    return encoder.encode(
      'ARCHER_DEVICE_READ_PROOF_V1\\n' +
      ownerSubject + '\\n' + deviceId + '\\nGET\\n' +
      requestPath + '\\n' + ticketId + '\\n' + nonce);
  }

  async function signReadChallenge(
    record, ownerSubject, deviceId, requestPath, ticketId, nonce) {
    checkStored(record);
    const proof = await SUBTLE().sign(
      {name:'ECDSA',hash:'SHA-256'}, record.privateKey,
      readProofPayload(ownerSubject, deviceId, requestPath, ticketId, nonce));
    if (proof.byteLength !== 64)
      throw new Error('Expected P-256 raw 64-byte read proof.');
    return base64url(proof);
  }

  async function signServerChallenge(record, ownerSubject, ticketId, nonce) {
    checkStored(record);
    const proof = await SUBTLE().sign(
      {name:'ECDSA',hash:'SHA-256'}, record.privateKey,
      pairingPayload(ownerSubject, ticketId, nonce));
    if (proof.byteLength !== 64)
      throw new Error('Expected P-256 raw 64-byte ECDSA proof.');
    return base64url(proof);
  }

  async function selfTest(record) {
    checkStored(record);
    const nonce = globalThis.crypto.getRandomValues(new Uint8Array(32));
    const payload = encoder.encode('ARCHER_LOCAL_KEY_SELFTEST_V1\n' + hex(nonce));
    const signature = await SUBTLE().sign(
      {name:'ECDSA',hash:'SHA-256'}, record.privateKey, payload);
    // This tests the *local keypair only*. It is NOT enrollment.
    return signature.byteLength === 64 &&
      await SUBTLE().verify(
        {name:'ECDSA',hash:'SHA-256'}, record.publicKey, signature, payload);
  }

  function publicDescription(record) {
    checkStored(record);
    // Public key only. Do not include a private key in pairing payloads.
    return {
      algorithm: 'P-256',
      spkiBase64Url: base64url(record.publicSpki),
      createdAt: record.createdAt,
      enrollmentApproved: false
    };
  }

  globalThis.ArcherPocketDeviceKeys = Object.freeze({
    create, read, erase, fingerprint, selfTest,
    pairingPayload, signServerChallenge, readProofPayload,
    signReadChallenge, publicDescription
  });
})();
