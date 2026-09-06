import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

test('reloads only for changed revision and defers while the page vetoes reload',async()=>{
 const code=readFileSync(new URL('../../src/Filomena.App/Playground/live-reload.js',import.meta.url),'utf8');
 const timers=[],saved=new Map();let revision='initial',allow=false,reloads=0,events=0;
 const document={currentScript:{src:'http://127.0.0.1/__playground/live-reload.js?revision=initial'},documentElement:{style:{cssText:''}},querySelectorAll:()=>[],getElementById:()=>null};
 const window={dispatchEvent:()=>{events++;return allow;}};
 vm.runInNewContext(code,{document,window,URL,Event,console,
  location:{href:'http://127.0.0.1/flow',pathname:'/flow',reload:()=>reloads++},
  sessionStorage:{getItem:key=>saved.get(key)??null,setItem:(key,value)=>saved.set(key,value),removeItem:key=>saved.delete(key)},
  fetch:async()=>({ok:true,json:async()=>({revision})}),setTimeout:callback=>timers.push(callback)
 });
 await timers.shift()();assert.equal(reloads,0);assert.equal(events,0);
 revision='changed';await timers.shift()();assert.equal(reloads,0);assert.equal(events,1);
 allow=true;await timers.shift()();assert.equal(reloads,1);assert.equal(saved.size,1);
});
