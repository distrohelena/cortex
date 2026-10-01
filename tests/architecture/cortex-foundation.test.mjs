import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=new URL('../../',import.meta.url);
const baseline=JSON.parse(readFileSync(new URL('cortex-compatibility-baseline.json',import.meta.url)));
test('compatibility ownership covers all 53 approved artifacts without losing hashes',()=>{
 assert.equal(baseline.files.length,53);
 assert.equal(new Set(baseline.files.map(f=>f.path)).size,53);
 assert.ok(baseline.files.every(f=>['cortex','filo'].includes(f.owner)));
});
test('foundation artifacts preserve approved bytes independently of Filo',()=>{
 for(const f of baseline.files.filter(f=>f.owner==='cortex'))
  assert.equal(createHash('sha256').update(readFileSync(new URL(f.currentPath,root))).digest('hex'),f.sha256,f.path);
});
test('Cortex solution project references resolve without an application',()=>{
 const solution=readFileSync(new URL('Cortex.slnx',root),'utf8');
 for(const [,path] of solution.matchAll(/<Project Path="([^"]+)"/g)) {
  assert.ok(path.startsWith('src/Cortex.')||path.startsWith('tests/Cortex.'));
  const project=new URL(path,root);
  for(const [,reference] of readFileSync(project,'utf8').matchAll(/<ProjectReference Include="([^"]+)"/g)) {
   assert.doesNotMatch(reference,/Filomena\.(App|Llm)/);
   assert.ok(existsSync(new URL(reference.replaceAll('\\','/'),project)),reference);
  }
 }
 assert.doesNotMatch(readFileSync(new URL('src/Cortex.Core/Cortex.Core.csproj',root),'utf8'),/<ProjectReference\b/);
});
