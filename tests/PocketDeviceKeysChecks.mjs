import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {createPublicKey, verify as verifyNode, webcrypto} from 'node:crypto';
import {runInNewContext} from 'node:vm';

const path = fileURLToPath(new URL('../web/archer-pocket/device-keys.js', import.meta.url));
const script = readFileSync(path, 'utf8');

function makeIndexedDB() {
  const entries = new Map();
  let initialized = false;
  const db = {
    objectStoreNames: {contains: () => initialized},
    createObjectStore: () => { initialized = true; },
    close() {},
    transaction: () => {
      const tx = {
        oncomplete: null,
        onabort: null,
        onerror: null,
        objectStore() {
          function reqFor(action) {
            const req = {result:undefined,onsuccess:null,onerror:null};
            queueMicrotask(() => {
              try {
                req.result = action();
                req.onsuccess?.();
                queueMicrotask(() => tx.oncomplete?.());
              } catch (e) {
                req.onerror?.(e);
                queueMicrotask(() => tx.onabort?.(e));
              }
            });
            return req;
          }
          return {
            get: key => reqFor(() => entries.get(key)),
            add: (item,key) => reqFor(() => {
              if(entries.has(key)) throw Error('ConstraintError');
              entries.set(key,item);
            }),
            delete: key => reqFor(() => entries.delete(key))
          };
        }
      };
      return tx;
    }
  };
  return {
    open() {
      const request = {result: db, onupgradeneeded:null,onerror:null,onblocked:null,onsuccess:null};
      queueMicrotask(() => {
        if (!initialized) request.onupgradeneeded?.();
        request.onsuccess?.();
      });
      return request;
    }
  };
}

const context = {
  crypto:webcrypto,
  indexedDB:makeIndexedDB(),
  isSecureContext:true,
  TextEncoder,
  Uint8Array,
  ArrayBuffer,
  Date,
  btoa,
  queueMicrotask
};
context.globalThis = context;
runInNewContext(script, context, {filename:'device-keys.js'});
const vault = context.ArcherPocketDeviceKeys;
assert.ok(vault, 'Device cryptography exported to browser only');

let checks = 0;
async function test(label, operation) {
  await operation();
  checks++;
}

await test('initially no local key', async () =>
  assert.equal(await vault.read(), null));

await test('intentional P-256 creation and durable read', async () => {
  const record = await vault.create();
  const stored = await vault.read();
  assert.equal(record.privateKey.extractable,false);
  assert.equal(stored.privateKey.type,'private');
  assert.equal(stored.privateKey.algorithm.namedCurve,'P-256');
  assert.equal(stored.publicSpki.byteLength > 80,true);
  assert.equal((await vault.fingerprint(stored)).length,64);
});

await test('creation never silently replaces original key', async () =>
  await assert.rejects(vault.create(),/already exists/));

await test('browser private key cannot be exported', async () => {
  const record = await vault.read();
  await assert.rejects(webcrypto.subtle.exportKey('pkcs8',record.privateKey));
});

await test('local random challenge sign/verify passes', async () => {
  assert.equal(await vault.selfTest(await vault.read()),true);
});

await test('public enrollment description has no private key', async () => {
  const value = vault.publicDescription(await vault.read());
  assert.equal(value.algorithm,'P-256');
  assert.equal(value.enrollmentApproved,false);
  assert.equal('privateKey' in value,false);
  assert.equal('ownerSubject' in value,false);
  assert.match(value.spkiBase64Url,/^[A-Za-z0-9_-]+$/);
});

await test('wire payload matches exact .NET pairing contract', async () => {
  const bytes = vault.pairingPayload('auth0|owner','TICKET','NONCE');
  const expected = 'ARCHER_DEVICE_PAIR_P256_V1\nauth0|owner\nTICKET\nNONCE';
  assert.equal(Buffer.from(bytes).toString('utf8'),expected);
});

await test('raw 64-byte WebCrypto ECDSA proof verifies with Node equivalent .NET P1363 format', async () => {
  const record = await vault.read();
  const payload = vault.pairingPayload('auth0|owner','TICKET','NONCE');
  const proofB64 = await vault.signServerChallenge(record,'auth0|owner','TICKET','NONCE');
  const signature = Buffer.from(proofB64,'base64url');
  assert.equal(signature.length,64);
  const publicKey = createPublicKey({
    key:Buffer.from(record.publicSpki),format:'der',type:'spki'
  });
  assert.equal(verifyNode('sha256',Buffer.from(payload),
    {key:publicKey,dsaEncoding:'ieee-p1363'},signature),true);
  const wrong = vault.pairingPayload('auth0|owner','TICKET','OTHER-NONCE');
  assert.equal(verifyNode('sha256',Buffer.from(wrong),
    {key:publicKey,dsaEncoding:'ieee-p1363'},signature),false);
});

await test('reject malformed challenge and newlines', async () => {
  for(const fields of [
    ['','TICKET','NONCE'],
    ['user\nimpersonated','TICKET','NONCE'],
    ['auth0|owner','','NONCE'],
    ['auth0|owner','TICKET','NONCE\r'],
    ['auth0|owner','TICKET','A'.repeat(300)]
  ]) assert.throws(()=>vault.pairingPayload(...fields));
});

await test('no hardcoded account subject in signed proof', async () => {
  const record = await vault.read();
  const proofA = await vault.signServerChallenge(record,'auth0|one','ticket','nonce');
  const proofB = await vault.signServerChallenge(record,'auth0|two','ticket','nonce');
  assert.notEqual(proofA,proofB);
});

await test('phone signs only one exact read-only GET challenge for its enrolled key', async () => {
  const record = await vault.read();
  const owner = 'auth0|owner';
  const deviceId = 'A'.repeat(48);
  const ticketId = 'B'.repeat(48);
  const nonce = 'C'.repeat(64);
  const resource = '/v1/conversation/messages';
  const bytes = vault.readProofPayload(owner, deviceId, resource, ticketId, nonce);
  assert.equal(Buffer.from(bytes).toString('utf8'),
    'ARCHER_DEVICE_READ_PROOF_V1\n' + owner + '\n' + deviceId +
    '\nGET\n' + resource + '\n' + ticketId + '\n' + nonce);
  const signature = Buffer.from(await vault.signReadChallenge(
    record, owner, deviceId, resource, ticketId, nonce), 'base64url');
  assert.equal(signature.byteLength, 64);
  const key = createPublicKey({key:Buffer.from(record.publicSpki),format:'der',type:'spki'});
  assert.equal(verifyNode('sha256',Buffer.from(bytes),
    {key,dsaEncoding:'ieee-p1363'},signature),true);
  const otherNonce = vault.readProofPayload(
    owner, deviceId, resource, ticketId, 'D'.repeat(64));
  assert.equal(verifyNode('sha256',Buffer.from(otherNonce),
    {key,dsaEncoding:'ieee-p1363'},signature),false);
  const otherOwner = vault.readProofPayload(
    'auth0|different', deviceId, resource, ticketId, nonce);
  assert.equal(verifyNode('sha256',Buffer.from(otherOwner),
    {key,dsaEncoding:'ieee-p1363'},signature),false);
  for(const illegal of ['/v1/missions/run','/v1/conversation/messages?admin=true',
                        '/api/chat']) {
    assert.throws(() => vault.readProofPayload(owner,deviceId,illegal,ticketId,nonce));
  }
  assert.throws(() => vault.readProofPayload(
    owner,deviceId,resource,ticketId,'bad nonce'));
  assert.throws(() => vault.readProofPayload(
    'owner\nimposter',deviceId,resource,ticketId,nonce));
  assert.throws(() => vault.readProofPayload(
    owner,'not-an-enrolled-id',resource,ticketId,nonce));
});

await test('explicit erase confirms no key remains', async () => {
  await vault.erase();
  assert.equal(await vault.read(),null);
});

await test('insecure context fails closed', async () => {
  context.isSecureContext=false;
  await assert.rejects(vault.create(),/unavailable/);
  await assert.rejects(vault.read(),/unavailable/);
  context.isSecureContext=true;
});

await test('storage unavailability fails closed', async () => {
  context.indexedDB=undefined;
  await assert.rejects(vault.create(),/unavailable/);
});

console.log('PASS:',checks,'P-256 local-only key, vault, proof-of-possession, replay context and failure checks');
