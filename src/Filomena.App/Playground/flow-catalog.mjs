// The user's catalog is the only selectable source; orphaned graph values are read-only.
export function catalogOptions(catalog,node,defaultModel){
 const providers=catalog.providers,providerId=node.provider||'lmstudio';
 const provider=providers.find(p=>p.id===providerId),local=providers.find(p=>p.id==='lmstudio');
 const providerOptions=[{value:'',label:'Herdar padrão (LM Studio)',disabled:!local},...providers.map(p=>({value:p.id,label:p.name,disabled:false}))];
 if(node.provider&&!providers.some(p=>p.id===node.provider))providerOptions.push({value:node.provider,label:`${node.provider} — fora do catálogo`,disabled:true});
 const inherited=providerId==='lmstudio';
 const models=[{value:'',label:inherited?`Herdar padrão (${defaultModel||'servidor'})`:'Selecione um modelo',disabled:inherited?!provider?.models.some(m=>m.id===defaultModel):true},
  ...(provider?.models??[]).map(m=>({value:m.id,label:m.name,disabled:false}))];
 if(node.model&&!provider?.models.some(m=>m.id===node.model))models.push({value:node.model,label:`${node.model} — fora do catálogo`,disabled:true});
 return {providers:providerOptions,models};
}

export function chooseProvider(node,value,catalog){
 if(!catalog.providers.some(p=>p.id===(value||'lmstudio')))throw new Error('Escolha um provedor do catálogo.');
 if((node.provider??'')===value)return;
 node.provider=value;node.model='';
}

export function chooseModel(node,value,catalog){
 const provider=catalog.providers.find(p=>p.id===(node.provider||'lmstudio'));
 if(!provider||(value!==''&&!provider.models.some(m=>m.id===value)))throw new Error('Escolha um modelo do catálogo deste provedor.');
 node.model=value;
}
