import test from 'node:test';
import assert from 'node:assert/strict';
import { catalogOptions, chooseProvider, chooseModel } from '../../src/Filomena.App/Playground/flow-catalog.mjs';
const catalog={providers:[{id:'lmstudio',name:'Local',models:[{id:'gemma',name:'Gemma'}]},{id:'custom',name:'Custom',models:[{id:'custom-1',name:'One'}]}]};

test('combobox models come exclusively from the selected provider catalog',()=>{
 const options=catalogOptions(catalog,{provider:'custom'},'gemma');
 assert.deepEqual(options.models.map(o=>o.value),['','custom-1']);
 assert.equal(options.models.some(o=>o.value==='gemma'),false);
 const node={provider:'lmstudio',model:'gemma'};
 chooseProvider(node,'custom',catalog);assert.equal(node.model,'');
 chooseModel(node,'custom-1',catalog);assert.equal(node.model,'custom-1');
 assert.throws(()=>chooseModel(node,'typed-model',catalog));
 assert.throws(()=>chooseProvider(node,'typed-provider',catalog));
});
test('removed selections stay visible as disabled values without rewriting a node',()=>{
 const node={provider:'gone',model:'old-model'},before=JSON.stringify(node);
 const options=catalogOptions(catalog,node,'gemma');
 assert.equal(options.providers.find(o=>o.value==='gone').disabled,true);
 assert.equal(options.models.find(o=>o.value==='old-model').disabled,true);
 assert.equal(JSON.stringify(node),before);
});
test('empty user catalog never repopulates seeded providers or models',()=>{
 const options=catalogOptions({providers:[]},{},'gemma');
 assert.deepEqual(options.providers.map(o=>o.value),['']);
 assert.deepEqual(options.models.map(o=>o.value),['']);
 assert.equal(options.providers[0].disabled,true);
 assert.equal(options.models[0].disabled,true);
});
