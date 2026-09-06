import {spawnSync} from 'node:child_process';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {copyFileSync} from 'node:fs';
const require=createRequire(import.meta.url);
const root=fileURLToPath(new URL('.',import.meta.url));
let compiler=process.argv[2];
if(!compiler){try{compiler=require.resolve('typescript/bin/tsc');}catch{throw new Error('Install package devDependencies or pass an explicit TypeScript compiler path to build.mjs.');}}
const result=spawnSync(process.execPath,[compiler,'-p',root],{stdio:'inherit'});
if(result.error)throw result.error;
if(result.status!==0)process.exit(result.status??1);
copyFileSync(new URL('src/editor.css',import.meta.url),new URL('dist/editor.css',import.meta.url));
