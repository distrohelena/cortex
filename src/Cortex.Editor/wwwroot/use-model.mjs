export class UseModel {
 constructor(state,api){this.api=api;this.graph=structuredClone(state.graph);this.entryId=state.entryId;this.catalog=state.catalog??[];this.descriptions=state.descriptions??{};this.fields=state.usageFields??{};this.models=state.modelChoices??{};this.parameters=Object.create(null);this.runState=state.runState??{state:'idle',nextNodeId:this.entryId};}
 get busy(){return this.runState.state==='running';}
 get editable(){return this.runState.state==='idle'&&!this.busy;}
 edit(id,field,value){if(!this.editable)throw new Error('Start over to change the inputs.');if(!this.fields[id]?.includes(field.name))throw new Error('This field is locked.');const parameter=field.schema.type==='number'?Number(value):value;const fields=this.parameters[id]??=Object.create(null);fields[field.name]=!field.required&&typeof value==='string'&&!value.trim()?null:parameter;}
 value(id,name){if(Object.hasOwn(this.parameters[id]??{},name))return this.parameters[id][name];return this.graph.nodes.find(n=>n.id===id)?.settings?.[name];}
 accept(run){this.runState=run;}
 async advance(){if(this.busy)throw new Error('A step is already running.');const parameters=this.editable?structuredClone(this.parameters):{};this.runState={...this.runState,state:'running'};try{this.accept(await this.api('/api/use/step','POST',{parameters}));}catch(error){this.accept(await this.api('/api/run'));throw error;}}
 async poll(){this.accept(await this.api('/api/run'));}
 async cancel(){this.accept(await this.api('/api/run/cancel','POST'));}
 async reset(){if(this.busy)throw new Error('Cancel the running step first.');this.accept(await this.api('/api/use/reset','POST'));this.parameters=Object.create(null);}
 definition(node){return this.catalog.find(d=>d.kind===node.kind)??this.descriptions[node.id]??{title:node.kind,ports:[],executionPorts:[]};}
}
