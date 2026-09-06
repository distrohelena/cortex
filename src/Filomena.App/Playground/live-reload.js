(() => {
 const initial=new URL(document.currentScript.src,location.href).searchParams.get('revision');
 const storageKey='filomena-live-reload:'+location.pathname;
 try{
  const saved=sessionStorage.getItem(storageKey);
  if(saved){
   const state=JSON.parse(saved);
   for(const [id,value] of Object.entries(state.fields??{})){
    const field=document.getElementById(id);if(field?.tagName==='TEXTAREA'&&typeof value==='string')field.value=value;
   }
   if(typeof state.layout==='string')document.documentElement.style.cssText=state.layout;
   sessionStorage.removeItem(storageKey);
  }
 }catch{console.warn('Live reload: não foi possível restaurar os campos.');}
 async function poll(){
  try{
   const response=await fetch('/__playground/revision',{cache:'no-store'});
   if(response.ok){
    const {revision}=await response.json();
    if(typeof revision==='string'&&revision!==initial){
     // Do not interrupt an active simple-prompt request or a flow that vetoes reload.
     if(!document.getElementById('test')?.disabled&&window.dispatchEvent(new Event('filomena:before-reload',{cancelable:true}))){
      const fields={};for(const field of document.querySelectorAll('textarea[id]'))fields[field.id]=field.value;
      sessionStorage.setItem(storageKey,JSON.stringify({fields,layout:document.documentElement.style.cssText}));
      location.reload();return;
     }
    }
   }
  }catch{ /* Server restarting, file being saved, or storage unavailable: retry without destroying the page. */ }
  setTimeout(poll,1000);
 }
 setTimeout(poll,1000);
})();
