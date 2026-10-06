const clone=value=>structuredClone(value);
export class EditorModel {
 constructor(state,api){this.api=api;this.graph=clone(state.graph);this.entryId=state.entryId??null;this.catalog=state.catalog??[];this.descriptions=state.descriptions??{};this.busy=state.busy??false;this.pendingRestart=state.pendingRestart??false;this.filePath=state.filePath??null;this.issues=[];this.dirty=false;this.runState={state:'idle'};this.error=null;}
 guard(){if(this.busy)throw new Error('Wait for the run to finish or cancel it before changing the graph or settings.');}
 editSettings(id,settings){this.guard();const node=this.graph.nodes.find(node=>node.id===id);if(!node)throw new Error('Node not found.');node.settings=clone(settings);this.dirty=true;}
 editField(id,field,value){const node=this.graph.nodes.find(node=>node.id===id);if(!node)throw new Error('Node not found.');const settings=clone(node.settings??{});if(!field.required&&typeof value==='string'&&!value.trim())delete settings[field.name];else settings[field.name]=field.schema.type==='number'?Number(value):value;this.editSettings(id,settings);}
 addNode(definition){this.guard();if(this.graph.nodes.length>=64)throw new Error('The limit is 64 nodes.');const node={id:crypto.randomUUID(),kind:definition.kind,x:70+(this.graph.nodes.length%3)*280,y:100+Math.floor(this.graph.nodes.length/3)*220,settings:Object.fromEntries((definition.fields??[]).filter(field=>field.required).map(field=>[field.name,field.schema.type==='string'?'':field.schema.type==='boolean'?false:0]))};this.graph.nodes.push(node);if(!this.entryId&&definition.isEntry)this.entryId=node.id;this.dirty=true;return node;}
 removeNode(id){this.guard();this.graph.nodes=this.graph.nodes.filter(node=>node.id!==id);for(const key of ['edges','controlEdges'])if(this.graph[key])this.graph[key]=this.graph[key].filter(edge=>edge.source!==id&&edge.target!==id);if(this.entryId===id)this.entryId=null;this.dirty=true;}
 async flush(){await this.api('/api/graph','PUT',{graph:clone(this.graph),entryId:this.entryId});}
 async save(){this.guard();await this.flush();const result=await this.api('/api/graph/save','POST');this.dirty=false;return result;}
 async validate(){this.guard();await this.flush();const result=await this.api('/api/validate','POST');this.issues=result.issues??[];return result;}
 async run(){this.guard();this.busy=true;this.error=null;try{await this.flush();const validation=await this.api('/api/validate','POST');this.issues=validation.issues??[];if(!validation.success){this.busy=false;return false;}this.acceptRun(await this.api('/api/run','POST'));return true;}catch(error){try{this.acceptRun(await this.api('/api/run'));}catch{this.busy=false;}this.error=error.message;throw error;}}
 acceptRun(run){this.runState=run;this.busy=run.state==='running';}
 async poll(){this.acceptRun(await this.api('/api/run'));return this.runState;}
 async cancel(){this.acceptRun(await this.api('/api/run/cancel','POST'));return this.runState;}
 async saveSettings(settings){this.guard();const result=await this.api('/api/settings','PUT',settings);this.pendingRestart=result.pendingRestart;return result;}
 preview(id){const outputs=this.runState.result?.outputs?.[id];if(!outputs)return '';const value=Object.values(outputs)[0]?.value;if(typeof value==='string')return value;if(value&&typeof value==='object'){const text=Object.values(value).find(item=>typeof item==='string');if(text!==undefined)return text;}return JSON.stringify(value);}
}
