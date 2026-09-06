import test from 'node:test';
import assert from 'node:assert/strict';
import {renderTypedPanel} from '../../src/Filomena.App/Playground/flow-typed-panel.mjs';
import {migrateTypedGraph,typedPorts} from '../../src/Filomena.App/Playground/flow-contract.mjs';
import {initialGraph,connect} from '../../src/Filomena.App/Playground/flow-model.mjs';

class Element {
 constructor(tag){this.tag=tag;this.children=[];}
 append(...children){this.children.push(...children);}
 replaceChildren(...children){this.children=children;}
 all(){return [this,...this.children.flatMap(n=>n.all())];}
}
const documentFake={createElement:tag=>new Element(tag),createTextNode:text=>Object.assign(new Element('#text'),{textContent:text})};
const input=(root,label)=>root.all().find(n=>n.tag==='label'&&n.children[0]?.textContent===label)?.children[1];
const button=(root,label)=>root.all().find(n=>n.tag==='button'&&n.textContent===label);

test('input panel creates opaque ports, renames and changes types without deleting connections',()=>{
 const previous=globalThis.document;globalThis.document=documentFake;
 try{
  const graph=migrateTypedGraph(initialGraph()),node=graph.nodes.find(n=>n.kind==='prompt');node.inputs=[];
  const root=new Element('root');let changed=0;
  const render=()=>renderTypedPanel(root,graph,node,false,()=>changed++);
  render();button(root,'Adicionar entrada').onclick();render();
  const id=node.inputs[0].id;assert.match(id,/^[a-f0-9-]{36}$/);
  input(root,'Nome').value='texto';input(root,'Nome').onchange();
  graph.nodes.push({id:'source',kind:'prompt',inputs:[],responseFields:[],x:0,y:0});
  connect(graph,'source',node.id,'message',id);render();
  assert.equal(input(root,'Valor configurado').disabled,true);
  input(root,'Nome').value='renomeado';input(root,'Nome').onchange();
  input(root,'Tipo').value='number';input(root,'Tipo').onchange();
  assert.equal(node.inputs[0].id,id);assert.equal(graph.edges[0].targetPort,id);
  render();input(root,'Tipo').value='text';input(root,'Tipo').onchange();
  assert.equal(typedPorts(graph,node,'in')[0].id,id);assert.ok(changed>=4);
 }finally{globalThis.document=previous;}
});

test('property selector contains only known paths and never provider metadata',()=>{
 const previous=globalThis.document;globalThis.document=documentFake;
 try{
  const graph=migrateTypedGraph(initialGraph());const node={id:'p',kind:'property',settings:{path:[]}};
  graph.nodes.push(node);graph.edges.push({source:'input',sourcePort:'message',target:'p',targetPort:'object'});
  const root=new Element('root');renderTypedPanel(root,graph,node,false,()=>{});
  const select=input(root,'Propriedade'),values=select.children.map(o=>o.value);
  assert.ok(values.includes('texto'));assert.ok(values.includes('remetente'));assert.ok(values.includes('reply.id'));
  assert.equal(values.includes('raw.token'),false);
  select.value='remetente';select.onchange();
  assert.equal(typedPorts(graph,node,'out')[0].type,'actor?');
 }finally{globalThis.document=previous;}
});
