import test from 'node:test';
import assert from 'node:assert/strict';
import {validateManifest} from '../../scripts/repository-split-manifest.mjs';

const entry = {sourcePath:'src/a.cs',owner:'cortex',destinationPath:'src/a.cs',sha256:'a'.repeat(64),reason:'runtime'};
const manifest = files => ({schemaVersion:1,sourceCommit:'b'.repeat(40),files});
test('complete ownership is accepted',()=>assert.deepEqual(validateManifest(manifest([entry]),['src/a.cs']),[]));
for (const [name, files, tracked, code] of [
  ['missing owner',[{...entry,owner:null}],['src/a.cs'],'owner_invalid'],
  ['unclassified file',[entry],['src/a.cs','src/b.cs'],'file_unclassified'],
  ['duplicate destination',[entry,{...entry,sourcePath:'src/b.cs'}],['src/a.cs','src/b.cs'],'destination_duplicate'],
  ['absolute path',[{...entry,destinationPath:'C:/outside.cs'}],['src/a.cs'],'path_invalid'],
  ['traversal',[{...entry,destinationPath:'../outside.cs'}],['src/a.cs'],'path_invalid'],
  ['production duplication',[{...entry,owner:'both-metadata'}],['src/a.cs'],'shared_source_forbidden'],
]) test(name,()=>assert.ok(validateManifest(manifest(files),tracked).includes(code)));
