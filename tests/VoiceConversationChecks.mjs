import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {runInNewContext} from 'node:vm';

// Browser Web Speech implementation is vendor-specific. This test verifies
// Archer's client state machine without microphone, cloud, keys or AI cost.
const source = readFileSync(fileURLToPath(
  new URL('../Archer.MissionControl/wwwroot/js/archer-home.js',import.meta.url)), 'utf8');

const elements = new Map();
const store = new Map();
function elt(id) {
  if (!elements.has(id)) {
    const handlers = new Map();
    const obj = {
      id, value:'', checked:false, disabled:false, textContent:'', title:'',
      dataset:{}, options:[], style:{},
      classList:{add(){},remove(){},toggle(){}},
      addEventListener(type,handler) { handlers.set(type,handler); },
      dispatch(type,event={}) {handlers.get(type)?.({target:obj,...event}); },
      setAttribute(){}, focus(){}, replaceChildren(){obj.options=[];},
      appendChild(child){obj.options.push(child);},
      requestSubmit() {throw Error('Unexpected form');}
    };
    elements.set(id,obj);
  }
  return elements.get(id);
}
elt('voicePauseSlider').value='7';
elt('voiceRateSlider').value='1.25';
let now=0, idCounter=0;
const queue=new Map();
function timeout(fn,ms){const id=++idCounter;queue.set(id,{at:now+ms,fn});return id;}
function clear(id){queue.delete(id);}
function advance(ms){
  const end=now+ms;
  let ticks=0;
  while(true){
    let next=null, key=null;
    for(const [k,item] of queue) {
      if(item.at<=end && (next===null||item.at<next.at)){next=item;key=k;}
    }
    if(next===null)break;
    if(++ticks>200)throw Error('Possible runaway voice timers');
    queue.delete(key);now=next.at;next.fn();
  }
  now=end;
}
class FakeRecognition {
  constructor(){this.starts=0;this.stops=0;this.aborts=0;this.active=false;}
  start(){this.starts++;this.active=true;this.onstart?.();}
  stop(){this.stops++;this.active=false;this.onend?.();}
  abort(){this.aborts++;this.active=false;this.onend?.();}
  push(words){this.onresult?.({resultIndex:0,
    results:[{isFinal:true,0:{transcript:words}}]});}
  spontaneousEnd(){this.active=false;this.onend?.();}
  denied(){this.onerror?.({error:'not-allowed'});}
}
class FakeUtterance{
  constructor(text){this.text=text;this.rate=1;}
}
const recs=[];
class Recognition extends FakeRecognition{
  constructor(){super();recs.push(this);}
}
const voice = {name:'Microsoft George',lang:'en-GB',voiceURI:'test-british'};
let pendingUtterance=null;
const speechSynthesis = {
  getVoices(){return [voice];},
  addEventListener(){},
  cancel(){pendingUtterance=null;},
  speak(utterance) {pendingUtterance=utterance;utterance.onstart?.();},
  finish(){const u=pendingUtterance;pendingUtterance=null;u?.onend?.();}
};
const browser={
  SpeechRecognition:Recognition,
  SpeechSynthesisUtterance:FakeUtterance,
  speechSynthesis,
  localStorage:{
    getItem:k=>store.has(k)?store.get(k):null,
    setItem:(k,v)=>store.set(k,String(v))
  },
  setTimeout:timeout,
  clearTimeout:clear,
  addEventListener(){},
};
const appState={data:{missions:[]},busy:false};
const doc={hidden:false,getElementById:elt,createElement:()=>({value:'',textContent:''}),
  addEventListener() {}};
const sandbox={window:browser,document:doc,navigator:{language:'en-US'},
  SpeechSynthesisUtterance:FakeUtterance,appState,Date,console};
runInNewContext(source,sandbox,{filename:'archer-home.js'});
const rec=recs[0], presence=browser.ArcherPresence;
const requests=[];
elt('chatForm').requestSubmit=()=>{
  requests.push({message:elt('chatText').value, voiceMode:presence.prepareSend()});
};
let cases=0;
function check(label, fn){fn();cases++;}

check('microphone is never started by merely opening page',()=>{
  assert.equal(rec.starts,0);
  assert.equal(store.get('archer.voice.handsFree'),undefined);
});
check('handsfree begins with explicit opt-in and user click',()=>{
  elt('voiceHandsFreeToggle').checked=true;
  elt('voiceHandsFreeToggle').dispatch('change');
  assert.equal(rec.starts,0);
  elt('voiceInputButton').dispatch('click');
  assert.equal(rec.starts,1);
  assert.equal(rec.continuous,true);
  assert.equal(elt('voiceReadToggle').checked,true);
});
check('initial phrase is not sent during a significant pause',()=>{
  rec.push('I want to speak');
  advance(3500);
  assert.equal(requests.length,0);
});
check('browser ending during a pause restarts recognition',()=>{
  rec.spontaneousEnd();
  assert.equal(rec.active,false);
  advance(300);
  assert.equal(rec.active,true);
  assert.equal(rec.starts,2);
  assert.equal(elt('chatText').value,'I want to speak');
});
check('additional words are joined to original phrase without dupes',()=>{
  rec.push('and take my time with a thought');
  assert.equal(elt('chatText').value,'I want to speak and take my time with a thought');
  advance(6999);
  assert.equal(requests.length,0);
});
check('after configured silence exactly one auto-submit happens',()=>{
  advance(1);
  assert.equal(rec.stops,1);
  assert.equal(requests.length,1);
  assert.equal(requests[0].voiceMode,true);
  assert.equal(requests[0].message,'I want to speak and take my time with a thought');
  advance(15000);
  assert.equal(requests.length,1);
});
check('after concise spoken reply Archer starts listening again',()=>{
  elt('chatText').value='';
  presence.reply('Okay, go on.');
  assert.ok(pendingUtterance);
  assert.equal(pendingUtterance.text,'Okay, go on.');
  assert.equal(pendingUtterance.rate,1.25);
  speechSynthesis.finish();
  advance(300);
  assert.equal(rec.starts,3);
  assert.equal(rec.active,true);
});
check('optional extended 20-second pause preserves deliberate hesitations',()=>{
  elt('voicePauseSlider').value='20';
  elt('voicePauseSlider').dispatch('input');
  rec.push('I need more time to complete a thought');
  advance(15000);
  assert.equal(requests.length,1);
  advance(4999);
  assert.equal(requests.length,1);
  advance(1);
  assert.equal(requests.length,2);
  assert.equal(requests[1].voiceMode,true);
  assert.equal(requests[1].message,'I need more time to complete a thought');
});
check('End conversation halts mic and prevents automatic restart',()=>{
  elt('voiceEndConversation').dispatch('click');
  const prior=rec.starts;
  advance(12000);
  assert.equal(rec.starts,prior);
  assert.equal(elt('voiceInputButton').textContent,'🎙 Speak');
});
check('without handsfree toggle it never silently submits voice',()=>{
  elt('voiceHandsFreeToggle').checked=false;
  elt('voiceHandsFreeToggle').dispatch('change');
  elt('chatText').value='';
  elt('voiceInputButton').dispatch('click');
  assert.equal(rec.continuous,false);
  rec.push('this is manual dictation');
  rec.spontaneousEnd();
  advance(12000);
  assert.equal(requests.length,2);
  assert.equal(elt('chatText').value,'this is manual dictation');
});
check('microphone denial fails closed',()=>{
  elt('voiceHandsFreeToggle').checked=true;
  elt('voiceHandsFreeToggle').dispatch('change');
  elt('chatText').value='';
  elt('voiceInputButton').dispatch('click');
  rec.denied();
  advance(12000);
  assert.equal(requests.length,2);
  assert.equal(elt('voiceInputButton').textContent,'🎙 Speak');
});
check('wake phrase mode is opt-in and changing it does not silently start microphone',()=>{
  elt('voiceWakeToggle').checked=true;
  elt('voiceWakeToggle').dispatch('change');
  assert.equal(rec.active,false);
  assert.equal(store.get('archer.voice.wake'),'yes');
});
check('two-second wait can be selected without changing the 1.25x speaking pace',()=>{
  elt('voicePauseSlider').value='2';
  elt('voicePauseSlider').dispatch('input');
  assert.equal(elt('voicePauseValue').textContent,'2 sec');
  assert.equal(store.get('archer.voice.pauseSeconds'),'2');
  assert.equal(elt('voiceRateSlider').value,'1.25');
});
check('non-wake background words never enter chat nor trigger sending',()=>{
  elt('voiceHandsFreeToggle').checked=true;
  elt('voiceHandsFreeToggle').dispatch('change');
  elt('voiceInputButton').dispatch('click');
  assert.equal(rec.active,true);
  rec.push('please create a mission to pay a bill');
  assert.equal(elt('chatText').value,'');
  advance(7000);
  assert.equal(requests.length,2);
});
check('saying only Hey Archer never submits a blank command',()=>{
  rec.push('Hey Archer');
  assert.equal(elt('chatText').value,'');
  advance(3500);
  assert.equal(requests.length,2);
});
check('wake phrase followed by request strips wake and waits exactly 2 seconds',()=>{
  rec.push('Hey Archer, list my missions');
  assert.equal(elt('chatText').value,'list my missions');
  advance(1999);
  assert.equal(requests.length,2);
  advance(1);
  assert.equal(requests.length,3);
  assert.equal(requests[2].message,'list my missions');
  assert.equal(requests[2].voiceMode,true);
  advance(9000);
  assert.equal(requests.length,3);
});
check('after reply the next turn does NOT require Hey Archer again',()=>{
  elt('chatText').value='';
  presence.reply('Your missions are ready.');
  assert.ok(pendingUtterance);
  assert.equal(pendingUtterance.rate,1.25);
  speechSynthesis.finish();
  advance(300);
  assert.equal(rec.active,true);
  rec.push('which one should I work on first');
  advance(1999);
  assert.equal(requests.length,3);
  advance(1);
  assert.equal(requests.length,4);
  assert.equal(requests[3].message,'which one should I work on first');
  assert.equal(requests[3].voiceMode,true);
});
check('spoken Markdown markers are sanitized before browser playback',()=>{
  elt('chatText').value='';
  presence.reply('Two options:\n- **$49 self-serve**\n- **$299 managed**');
  assert.ok(pendingUtterance);
  assert.ok(!pendingUtterance.text.includes('**'));
  assert.ok(!pendingUtterance.text.includes('\n'));
  assert.ok(pendingUtterance.text.includes('$49 self-serve'));
  assert.ok(pendingUtterance.text.includes('$299 managed'));
  assert.equal(pendingUtterance.rate,1.25);
  speechSynthesis.finish();
  advance(300);
  assert.equal(rec.active,true);
});
check('normal speech refreshes the 2-minute idle clock',()=>{
  // After a reply Archer is awake but silent; near the deadline a spoken
  // request should be allowed and reset the timeout.
  advance(119000);
  assert.equal(rec.active,true);
  rec.push('tell me more about the cheaper choice');
  advance(1999);
  assert.equal(requests.length,4);
  advance(1);
  assert.equal(requests.length,5);
  assert.equal(requests[4].message,'tell me more about the cheaper choice');
});
check('two minutes of silence after a reply requires Hey Archer again',()=>{
  elt('chatText').value='';
  presence.reply('We can test a smaller price first.');
  speechSynthesis.finish();
  advance(300);
  assert.equal(rec.active,true);
  advance(119999);
  assert.equal(rec.active,true);
  advance(1);
  // Flush recognizer so its previously detected wake cannot falsely
  // reactivate Archer after the standby transition.
  assert.equal(rec.active,false);
  advance(300);
  assert.equal(rec.active,true);
  rec.push('show me my missions without the wake word');
  advance(5000);
  assert.equal(requests.length,5);
  assert.equal(elt('chatText').value,'');
});
check('Hey Archer alone reawakens the session without creating a request',()=>{
  rec.push('Hey Archer');
  advance(3000);
  assert.equal(requests.length,5);
  assert.equal(elt('chatText').value,'');
});
check('once reawakened the next phrase may omit the wake phrase',()=>{
  rec.push('list my missions');
  assert.equal(elt('chatText').value,'list my missions');
  advance(1999);
  assert.equal(requests.length,5);
  advance(1);
  assert.equal(requests.length,6);
  assert.equal(requests[5].message,'list my missions');
});
check('manually ending prevents all idle-timer and browser auto-restarts',()=>{
  elt('voiceEndConversation').dispatch('click');
  const starts=rec.starts;
  advance(130000);
  assert.equal(rec.starts,starts);
  assert.equal(rec.active,false);
  assert.equal(elt('voiceInputButton').textContent,'🎙 Speak');
});

console.log('PASS:',cases,'one-time wake, normal followups, 2-minute idle reactivation, speech formatting, privacy and failure scenarios');
