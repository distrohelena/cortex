// Experimental artifact transform only; no activation, host or provider imports.
export function createSimpleTurnVariant(source, clause) {
  const heading='VOZ DE CONVERSA ENTRE ADULTOS',tail='CONTINUIDADE SEM RECICLAR A PRÓPRIA FALA';
  if(typeof clause!=='string'||!clause.trim().startsWith(heading+'\n')||clause.split(heading).length!==2||clause.includes(tail))
    throw new Error('Invalid replacement');
  const result=structuredClone(source);
  const states=result?.project?.states;
  const expected=new Set(['participation-inactive-v1','participation-attentive-v1','participation-engaged-v1']);
  if(!Array.isArray(states)||states.length!==3||new Set(states.map(s=>s.id)).size!==3||states.some(s=>!expected.has(s.id)))throw new Error('Unreviewed states');
  for(const state of states)for(const id of ['group-generation','group-regeneration']){
    const nodes=state.graph.nodes.filter(n=>n.id===id);
    if(nodes.length!==1||nodes[0].kind!=='prompt'||typeof nodes[0].prompt!=='string')throw new Error('Unreviewed target');
    const node=nodes[0],prompt=node.prompt;
    if(prompt.split(heading).length!==2||prompt.split(tail).length!==2)throw new Error('Unreviewed voice boundaries');
    const start=prompt.indexOf(heading),end=prompt.indexOf(tail,start);
    if(start<0||end<=start)throw new Error('Unreviewed voice order');
    if(prompt.slice(start,end).trim()===clause.trim())throw new Error('Already replaced');
    node.prompt=prompt.slice(0,start)+clause.trim()+'\n\n'+prompt.slice(end);
  }
  return result;
}
