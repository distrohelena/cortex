import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
import {resolve} from 'node:path';

test('video plugin is optional and not statically referenced by its hosts',()=>{
 for(const path of ['src/Cortex.Core/Cortex.Core.csproj','src/Cortex.Editor/Cortex.Editor.csproj','../depois-do-slogan/src/DepoisDoSlogan/DepoisDoSlogan.csproj']){
  if(!existsSync(path))continue;
  assert.doesNotMatch(readFileSync(path,'utf8'),/ProjectReference[^>]*Cortex\.Video|Reference[^>]*Cortex\.Video/);
 }
 const manifest=JSON.parse(readFileSync(resolve('src/Cortex.Video/cortex-video.plugin.json'),'utf8'));
 assert.equal(manifest.entryType,'Cortex.Video.VideoNodePlugin');
 assert.equal(manifest.contractVersion,1);
 assert.equal(manifest.assemblyPath,'Cortex.Video.dll');
});
