import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {runInNewContext} from 'node:vm';

// Run only the pure status presenter from the real browser source. No network,
// microphone, repo credentials, paid model calls or fake authorizations.
const source = readFileSync(fileURLToPath(
  new URL('../Archer.MissionControl/wwwroot/js/app.js',import.meta.url)), 'utf8');
const start = source.indexOf('function latestDevelopmentPulse(');
const end = source.indexOf('function render() {',start);
assert.ok(start >= 0 && end > start, 'pure work-pulse functions are defined before render');

const elements = new Map();
const elt = id => {
  if (!elements.has(id))elements.set(id,{textContent:'',dataset:{}});
  return elements.get(id);
};
const sandbox = { $, Date, Array, String, Number };
function $(id){return elt(id);}
runInNewContext(source.slice(start,end) +
  '\nthis.pulseApi = { latestDevelopmentPulse, developmentNeedsRefresh, renderDevelopmentPulse };',sandbox);
const {latestDevelopmentPulse:latest,developmentNeedsRefresh:needs,renderDevelopmentPulse:render} = sandbox.pulseApi;

const empty={projects:[],developmentBatches:[]};
assert.equal(latest(empty),null);
assert.equal(needs(empty),false);
render(empty);
assert.equal(elt('homeDevelopmentStatus').textContent,'IDLE');
assert.match(elt('homeDevelopmentDetail').textContent,/registered project/);

const project={id:'pr1',name:'GeoShift',paused:false,objectives:[{
  id:'o1',description:'Improve KML dedupe review without losing skipped decisions',
  planningState:'Delegated',createdAtUtc:'2026-11-22T10:00:00Z'
}]};
const state={projects:[project],developmentBatches:[]};
assert.equal(latest(state).status,'Queued for planning');
assert.equal(needs(state),true);
render(state);
assert.equal(elt('homeDevelopmentLabel').textContent,'GeoShift');
assert.equal(elt('homeDevelopmentStatus').textContent,'Queued for planning');

project.objectives[0].planningState='Batch created';
const batch={projectId:'pr1',objectiveId:'o1',armed:false,tasks:[{status:'Queued'}]};
state.developmentBatches=[batch];
assert.equal(latest(state).status,'Awaiting authorization');
assert.equal(needs(state),false, 'unarmed draft does not claim background coding');
batch.armed=true;
assert.equal(latest(state).status,'Queued for coding');
assert.equal(needs(state),true);
batch.tasks[0].status='Editing';
assert.equal(latest(state).status,'Writing code');
assert.equal(needs(state),true);
batch.tasks[0].status='Testing';
assert.equal(latest(state).status,'Checking code');
assert.equal(needs(state),true);
batch.armed=false;
batch.tasks[0].status='Verified';
assert.equal(latest(state).status,'Draft ready for review');
assert.equal(needs(state),false);
render(state);
assert.equal(elt('homeDevelopmentPulse').dataset.tone,'ready');

batch.tasks[0].status='Blocked';
assert.equal(latest(state).status,'Needs attention');
assert.equal(needs(state),false, 'blocked work does not loop on a fake active status');
render(state);
assert.equal(elt('homeDevelopmentPulse').dataset.tone,'alert');
project.paused=true;
assert.equal(latest(state).status,'Project paused');
project.paused=false;
batch.tasks[0].status='Verified';

// A newer open task takes precedence over an accepted task; keep original
// objective descriptions intact, and render them only via textContent.
project.objectives.push({
  id:'o2',description:'<img src=x onerror=alert(1)>',
  planningState:'Delegated',createdAtUtc:'2026-11-23T10:00:00Z'
});
render(state);
assert.equal(elt('homeDevelopmentDetail').textContent,'<img src=x onerror=alert(1)>');
assert.equal(elt('homeDevelopmentStatus').textContent,'Queued for planning');
project.objectives[1].acceptedAtUtc='2026-11-23T11:00:00Z';
assert.equal(latest(state).description,project.objectives[0].description);
project.objectives[0].acceptedAtUtc='2026-11-24T11:00:00Z';
assert.equal(latest(state).status,'Accepted');
assert.equal(needs(state),false);

console.log('PASS: Archer home shows real queued, writing, testing, blocked and review statuses without claiming unarmed code ran.');
