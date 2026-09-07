import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {openProject,saveProject} from '../../src/Filomena.App/Playground/flow-model.mjs';

for(const name of ['typed-flow-slice1.json','typed-flow-slice2.json','typed-flow-slice3.json']){
 test(`${name} imports and exports the complete design without changes`,()=>{
  const text=readFileSync(new URL('../fixtures/'+name,import.meta.url),'utf8');
  assert.deepEqual(JSON.parse(saveProject(openProject(text))),JSON.parse(text));
 });
}
