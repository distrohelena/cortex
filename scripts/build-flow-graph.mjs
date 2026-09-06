// Host integration: the reusable package itself never knows about Filomena.App.
import {spawnSync} from 'node:child_process';
import {existsSync,mkdirSync,copyFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
const root=new URL('../',import.meta.url);
const local=new URL('packages/flow-graph/node_modules/typescript/bin/tsc',root);
const existing=new URL('plugins/openclaw-memory-graphiti/node_modules/typescript/bin/tsc',root);
const compiler=existsSync(local)?local:existing;
const result=spawnSync(process.execPath,[fileURLToPath(new URL('packages/flow-graph/build.mjs',root)),fileURLToPath(compiler)],{stdio:'inherit'});
if(result.error)throw result.error;
if(result.status!==0)process.exit(result.status??1);
const target=new URL('src/Filomena.App/Playground/flow-graph/',root);
mkdirSync(target,{recursive:true});
for(const file of ['index.js','camera.js','editor.css'])copyFileSync(new URL('packages/flow-graph/dist/'+file,root),new URL(file,target));
