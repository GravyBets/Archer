import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {runInNewContext} from 'node:vm';

// Run only the pure status presenter from the real browser source. No network,
// microphone, repo credentials, paid model calls or fake authorizations.
const source = readFileSync(fileURLToPath(
  new URL('../Archer.MissionControl/wwwroot/js/app.js',import.meta.url)), 'utf8');
const start = source.indexOf('function trustedReviewUrl(');
const end = source.indexOf('function render() {',start);
assert.ok(start >= 0 && end > start, 'pure work-pulse functions are defined before render');

const elements = new Map();
const elt = id => {
  if (!elements.has(id))elements.set(id,{
    textContent:'',dataset:{},style:{},hidden:false,href:'',
    attributes:{},
    setAttribute(k,v){this.attributes[k]=String(v);},
    removeAttribute(k){delete this.attributes[k];if(k==='href')this.href='';}
  });
  return elements.get(id);
};
const sandbox = { $, Date, Array, String, Number, appState:{projectReadiness:[]} };
function $(id){return elt(id);}
runInNewContext(source.slice(start,end) +
  '\nthis.pulseApi = { latestDevelopmentPulse, developmentNeedsRefresh, renderDevelopmentPulse, renderMissionProgress, activeMissionProgress, trustedReviewUrl, paintWorkProgress };',sandbox);
const {latestDevelopmentPulse:latest,developmentNeedsRefresh:needs,renderDevelopmentPulse:render,
  renderMissionProgress:renderMission, trustedReviewUrl:reviewUrl} = sandbox.pulseApi;

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


// A real completion count is not an elapsed-time estimate; unknown plans
// must stay indeterminate rather than claim a made-up percentage.
const planningState={projects:[{
  id:'plan',name:'Archer',repository:'GravyBets/Archer',
  objectives:[{id:'one',description:'Improve inbox',planningState:'Planning',
    createdAtUtc:'2026-11-22T18:00:00Z'}]
}],developmentBatches:[]};
render(planningState);
assert.equal(elt('homeDevelopmentProgress').hidden,false);
assert.equal(elt('homeDevelopmentProgress').dataset.mode,'indeterminate');
assert.equal(elt('homeDevelopmentProgress').attributes['aria-valuenow'],undefined);
assert.equal(elt('homeDevelopmentCount').textContent,'Work in progress · tasks not yet known');
assert.equal(elt('homeDevelopmentReview').hidden,true);

const completeState={projects:[{
  id:'real',name:'GeoShift',repository:'GravyBets/GeoShift',
  objectives:[{id:'real-o',description:'Improve conversion errors',planningState:'Batch created',
    createdAtUtc:'2026-11-22T21:00:00Z'}]
}],developmentBatches:[{
  projectId:'real',objectiveId:'real-o',armed:true,
  tasks:[{status:'Verified'},{status:'Editing'},{status:'Queued'}]
}]};
render(completeState);
assert.equal(elt('homeDevelopmentProgress').dataset.mode,'determinate');
assert.equal(elt('homeDevelopmentProgress').attributes['aria-valuenow'],'1');
assert.equal(elt('homeDevelopmentProgress').attributes['aria-valuemax'],'3');
assert.equal(elt('homeDevelopmentBar').style.width,'33%');
assert.equal(elt('homeDevelopmentCount').textContent,'1 of 3 tasks verified');
assert.equal(elt('homeDevelopmentReview').hidden,true);

// A ready draft is not merged or deployed; only show a URL that matches
// the registered repository and the exact GitHub pull request path.
const task=completeState.developmentBatches[0].tasks;
task.forEach(t=>t.status='Verified');
task[2].proposal={pullRequestUrl:'https://github.com/GravyBets/GeoShift/pull/120'};
render(completeState);
assert.equal(elt('homeDevelopmentBar').style.width,'100%');
assert.equal(elt('homeDevelopmentStatus').textContent,'Draft ready for review');
assert.equal(elt('homeDevelopmentReview').hidden,false);
assert.equal(elt('homeDevelopmentReview').href,'https://github.com/GravyBets/GeoShift/pull/120');
assert.equal(reviewUrl('GravyBets/GeoShift',{tasks:[{proposal:{
  pullRequestUrl:'https://evil.example.net/GravyBets/GeoShift/pull/12'}}]}),null);
assert.equal(reviewUrl('GravyBets/GeoShift',{tasks:[{proposal:{
  pullRequestUrl:'https://github.com/GravyBets/Archer/pull/12'}}]}),null);
assert.equal(reviewUrl('GravyBets/GeoShift',{workspacePullRequestNumber:21,tasks:[]}),
  'https://github.com/GravyBets/GeoShift/pull/21');
task[2].status='Blocked';
render(completeState);
assert.equal(elt('homeDevelopmentReview').hidden,true);
assert.equal(elt('homeDevelopmentReview').attributes.href,undefined);

// Setup checklist is read-only and never treats a saved objective as running.
const saved={projects:[{
  id:'g',name:'GeoShift',repository:'GravyBets/GeoShift',
  objectives:[{id:'a',description:'Preserve skipped duplicates',
    planningState:'Not delegated',createdAtUtc:'2026-11-22T22:00:00Z'}]
}],developmentBatches:[]};
sandbox.appState.projectReadiness=[{
  repository:'GravyBets/GeoShift',enabled:false,
  setup:{ready:false,missing:['Configure scoped GitHub read and write access',
    'Add repository to GitHub write allowlist','Enable coding']}
}];
render(saved);
assert.equal(elt('homeDevelopmentProgress').hidden,true);
assert.equal(elt('homeDevelopmentSetup').hidden,false);
assert.match(elt('homeDevelopmentSetupText').textContent,/Not running/);
assert.match(elt('homeDevelopmentSetupText').textContent,/2 more checks|1 more checks/);
assert.equal(elt('homeDevelopmentStatus').textContent,'Saved, not started');
sandbox.appState.projectReadiness=[];
render(saved);
assert.equal(elt('homeDevelopmentSetup').hidden,true);

// Mission progress uses completed steps rather than development verification.
// Inactive/pending missions do not produce a fake running bar.
renderMission([{title:'Someday',status:'Ready',tasks:[{status:'Pending'}]}]);
assert.equal(elt('homeMissionPulse').hidden,true);
renderMission([{title:'GeoShift customer research',status:'Running',autoRunEnabled:true,
  updatedAtUtc:'2026-11-22T22:00:00Z',
  tasks:[{status:'Completed'},{status:'Running'},{status:'Pending'}]}]);
assert.equal(elt('homeMissionPulse').hidden,false);
assert.equal(elt('homeMissionBar').style.width,'33%');
assert.equal(elt('homeMissionCount').textContent,'1 of 3 mission steps complete');
assert.equal(elt('homeMissionProgress').attributes['aria-valuenow'],'1');
renderMission([{title:'Plan',status:'Running',tasks:[]}]);
assert.equal(elt('homeMissionProgress').dataset.mode,'indeterminate');
assert.equal(elt('homeMissionProgress').attributes['aria-valuenow'],undefined);
renderMission([]);
assert.equal(elt('homeMissionPulse').hidden,true);

console.log('PASS: live work meters reflect verified development/mission steps, show setup blockers and never expose an untrusted review link.');
