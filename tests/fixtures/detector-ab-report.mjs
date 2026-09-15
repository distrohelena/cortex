import {readFileSync} from 'node:fs';
import {pathToFileURL} from 'node:url';

export function summarize(rows){
 const groups=new Map();
 for(const r of rows){const key=r.Phase+'/'+r.Strategy;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(r);}
 return [...groups].map(([group,rs])=>{
  const calls=rs.flatMap(r=>r.Calls),cases=new Map();
  for(const r of rs){if(!cases.has(r.Id))cases.set(r.Id,new Set());cases.get(r.Id).add(r.Allow);}
  const times=rs.map(r=>r.WallMilliseconds).sort((a,b)=>a-b),n=times.length;
  const usd=calls.reduce((sum,c)=>{const u=c.Usage??{},d=u.input_tokens_details??{};return sum+((u.input_tokens??0)-(d.cached_tokens??0)-(d.cache_write_tokens??0))*.2/1e6+(d.cached_tokens??0)*.02/1e6+(d.cache_write_tokens??0)*.25/1e6+(u.output_tokens??0)*1.2/1e6;},0);
  return {group,runs:n,fn:rs.filter(r=>r.FN).length,fp:rs.filter(r=>r.FP).length,instabilityCases:[...cases.values()].filter(v=>v.size>1).length,
   invalidCalls:calls.filter(c=>c.Invalid).length,
   invalidOutputs:calls.filter(c=>c.Invalid&&c.Diagnostic!=='provider_failure').length,
   providerFailures:calls.filter(c=>c.Diagnostic==='provider_failure').length,
   ambiguousCalls:calls.filter(c=>c.Ambiguous).length,calls:calls.length,
   meanWallMs:times.reduce((a,b)=>a+b,0)/n,p50WallMs:(times[Math.floor((n-1)/2)]+times[Math.floor(n/2)])/2,p95WallMs:times[Math.ceil(n*.95)-1],
   meanSumMs:rs.reduce((a,r)=>a+r.SumMilliseconds,0)/n,usd,
   fnCases:rs.filter(r=>r.FN).map(r=>r.Id+'/'+r.Repetition),fpCases:rs.filter(r=>r.FP).map(r=>r.Id+'/'+r.Repetition)};
 });
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)console.log(JSON.stringify(summarize(JSON.parse(readFileSync(process.argv[2],'utf8')).rows),null,2));
