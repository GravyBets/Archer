import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {runInNewContext} from 'node:vm';

const root = fileURLToPath(new URL('../Archer.MissionControl/wwwroot/js/', import.meta.url));
const source = readFileSync(root + '/mission-voice.js', 'utf8');
let approved = false;
const network = [];
const created = [];
let callbackId = null;
const fakeWindow = {
  confirm: () => approved,
  ArcherOnMissionDraftCreated: async id => { callbackId = id; }
};
const fakeFetch = async (path, options = {}) => {
  network.push({path, options});
  if (path === '/api/voice/mission-list') {
    assert.equal(options.method, 'POST');
    assert.equal(options.headers['Content-Type'], 'application/json');
    return {ok: true, json: async () => ({
      status: 'ok', count: 1,
      reply: "You've got one mission: GeoShift audit. Its status is Ready.",
      missions: [{title: 'GeoShift audit', status: 'Ready',
        tasksCompleted: 1, tasksTotal: 2}]
    })};
  }
  if (path === '/api/missions' && options.method === 'POST') {
    const mission = JSON.parse(options.body);
    created.push(mission);
    return {ok: true, json: async () => ({id: 'generated-guid'})};
  }
  throw Error('Unexpected remote request: ' + path);
};
runInNewContext(source, {window: fakeWindow, fetch: fakeFetch});
const bridge = fakeWindow.ArcherVoiceMissions;
assert.ok(bridge);

assert.equal(bridge.parse('Hey Archer, what is my mission status?')?.kind, 'list');
assert.equal(bridge.parse('Archer, list my missions')?.kind, 'list');
assert.equal(bridge.parse('Archer, create a mission to research utility SaaS customers')?.kind, 'propose');
for (const dangerous of [
  'Archer run all missions now',
  'Archer deploy code to production',
  'Archer charge my card for server hosting',
  'Archer enable automatic execution',
  'Archer merge the latest pull request',
  'Archer create a mission'
]) assert.equal(bridge.parse(dangerous), null, dangerous);

const result = await bridge.list();
assert.equal(result.status, 'ok');
assert.equal(result.count, 1);
assert.equal(result.missions[0].tasksCompleted, 1);
assert.equal(result.missions[0].tasksTotal, 2);
assert.equal(JSON.stringify(result).includes('DO_NOT_RETURN_ME'), false);
assert.deepEqual(network.map(x=>x.path), ['/api/voice/mission-list']);
assert.equal(bridge.describe(result), "You've got one mission: GeoShift audit. Its status is Ready.");

assert.equal((await bridge.propose('Utility SaaS research',
  'Research customer pain points for utility software')).status, 'declined');
assert.equal(created.length, 0);
assert.equal(network.length, 1);

const ctrl = new AbortController();
ctrl.abort();
assert.equal((await bridge.propose('Utility SaaS research',
  'Research customer pain points for utility software', {signal: ctrl.signal})).status, 'declined');
assert.equal(created.length, 0);

approved = true;
const made = await bridge.propose('Utility SaaS research',
  'Research customer pain points for utility software');
assert.equal(made.status, 'created');
assert.equal(created.length, 1);
assert.equal(callbackId, 'generated-guid');
assert.equal(created[0].title, 'Utility SaaS research');
assert.equal(network.length, 2);
assert.equal(network[1].path, '/api/missions');
assert.equal(network[1].options.method, 'POST');
assert.equal(String(network[1].options.body).includes('maxSteps'), false);
assert.equal(bridge.describe(made).includes('not planned'), true);

const realtime = readFileSync(root + '/realtime-voice.js', 'utf8');
assert.equal(realtime.includes("data.response?.output"), true);
assert.equal(realtime.includes("type: 'function_call_output'"), true);
assert.equal(realtime.includes("approvedSession?.signal"), true);
assert.equal(realtime.includes("call.name === 'propose_local_mission'"), true);
assert.equal(realtime.includes('toolCalls <= 3'), true);
assert.equal(realtime.includes("channel.send(JSON.stringify({type: 'response.create'}))"), true);
assert.equal(realtime.includes("fetch('/api/missions"), false);
assert.equal(realtime.includes("fetch('/api/chat"), false);

console.log('PASS: mission voice grammar, bounded status, physical approval, cancellation, no autonomous execution, realtime tool allowlist');