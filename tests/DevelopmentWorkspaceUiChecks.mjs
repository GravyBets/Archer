import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const root = new URL('../Archer.MissionControl/wwwroot/', import.meta.url);
const html = fs.readFileSync(new URL('index.html',root),'utf8');
const source = fs.readFileSync(new URL('js/development-batches.js',root),'utf8');
class Node {
  constructor(tag) { this.tagName=tag; this.children=[]; this.events={}; this.dataset={}; this._text=''; this.value=''; this.checked=false; this.disabled=false; this.classList={contains:()=>false}; }
  set textContent(s) { this._text=String(s); this.children=[]; }
  get textContent() { return this._text+this.children.map(n=>n.textContent).join(' '); }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children=nodes; this._text=''; if(this.tagName==='select')this.value=nodes[0]?.value||''; }
  addEventListener(name,fn){this.events[name]=fn;}
  setAttribute(){}
  focus(){}
  reportValidity(){return true;}
  reset(){}
  get options(){return this.children;}
}
const ids=new Map([...html.matchAll(/id="([^"]+)"/g)].map(m=>[m[1],new Node('div')]));
for(const id of ['managedProjectSelect','projectDependency'])ids.get(id).tagName='select';
const el=id=>ids.get(id);
const goal='Improve the inbox empty-state experience and retain all existing behavior.';
const p={id:'project',name:'Archer',repository:'GravyBets/Archer',technology:'.NET',enabled:true,paused:false,selfProject:true,cumulativeWorkspaces:true,dailyAttemptLimit:6,attemptsToday:1,credentialPrefix:'TEST',objectives:[
  {id:'old',description:goal,dependsOn:[],planningState:'Blocked',planningAttempted:true},
  {id:'current',description:goal,dependsOn:[],planningState:'Batch created',planningAttempted:true}
],memory:[]};
const head='a'.repeat(40),base='b'.repeat(40);
const batch={id:'done',projectId:p.id,objectiveId:'current',objective:goal,repository:p.repository,workspaceHeadSha:head,armed:false,tasks:[{status:'Verified',paths:['inbox.js'],instruction:'Fix empty state',attempts:1,pullRequestUrl:'https://github.com/GravyBets/Archer/pull/78'}]};
let batches=[batch], calls=[], failDelegation=false, intervals=[];
const candidate={repository:p.repository,status:'Draft CI rechecked',headSha:head,baselineSha:base,nextAction:'Owner review',pullRequestUrl:batch.tasks[0].pullRequestUrl,baselineUrl:'https://github.com/'+p.repository+'/commit/'+base,compareUrl:'https://github.com/'+p.repository+'/compare/'+base+'...'+head};
const api=async(path,opts)=>{calls.push({path,opts});if(path==='/api/projects')return [p];if(path==='/api/development/batches')return {batches};if(path==='/api/self-development')return {registered:true,enabled:true,findings:[],approvedPaths:['inbox.js']};
  if(path==='/api/self-development/candidates/done')return candidate;
  if(path==='/api/projects/project/objectives'){const body=JSON.parse(opts.body);p.objectives.push({id:'new',description:body.description,dependsOn:[],planningState:'Not delegated',planningAttempted:false});return {id:'new'};}
  if(path.endsWith('/new/delegate')){if(failDelegation)throw new Error('Access refused');p.objectives.at(-1).planningState='Delegated';return {};}
  throw new Error('Unexpected API '+path);
};
const context={document:{getElementById:el,createElement:t=>new Node(t),hidden:false},window:{confirm:()=>true,setInterval:f=>intervals.push(f)},notice(){},api,URL,console};
vm.runInNewContext(source,context);
const flush=()=>new Promise(r=>setImmediate(r));
await flush();
function find(node,text){if(node.tagName==='button'&&node.textContent===text)return node;for(const child of node.children){const match=find(child,text);if(match)return match;}}
assert.match(el('developmentBatchList').textContent,/Ready for review/);
assert.doesNotMatch(el('developmentBatchList').textContent,/Needs attention/);
assert.match(el('developmentHistory').textContent,/Needs attention/);
await find(el('developmentBatchList'),'Review completed work').events.click();
assert.match(el('developmentBatchList').textContent,/Independent checks passed/);
assert.match(el('developmentBatchList').textContent,/Review changes on GitHub/);
await intervals[0]();await flush();
assert.match(el('developmentBatchList').textContent,/Independent checks passed/);
batch.workspaceHeadSha='c'.repeat(40);
await intervals[0]();await flush();
assert.doesNotMatch(el('developmentBatchList').textContent,/Independent checks passed/);
el('projectObjectiveText').value='Make another small improvement to feedback.';
el('projectObjectiveForm').events.submit({preventDefault(){}});
await flush();await flush();
assert.equal(calls.filter(c=>c.path.endsWith('/new/delegate')).length,1);
assert.equal(JSON.parse(calls.find(c=>c.path.endsWith('/new/delegate')).opts.body).confirm,true);
assert.match(el('objectiveSubmitStatus').textContent,/Assigned/);
assert.equal(el('projectObjectiveText').value,'');
failDelegation=true;
el('projectObjectiveText').value='Save this assignment even if delegation fails.';
el('projectObjectiveForm').events.submit({preventDefault(){}});
await flush();await flush();
assert.match(el('objectiveSubmitStatus').textContent,/Saved, but not authorized/);
console.log('PASS: owner workspace shows current result, archives earlier failure, keeps review local and head-bound, and assigns with explicit consent.');
