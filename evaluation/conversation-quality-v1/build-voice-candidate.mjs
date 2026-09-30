// Generates an offline artifact; no runtime, provider or host imports.
import {readFileSync, writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {createVariant} from './build-variants.mjs';
const source = new URL('../../docs/manual-group-live-project-v3-conversation-repair-v1.json', import.meta.url);
const output = new URL('./group-unforced-voice-v3.project.json', import.meta.url);
const candidate = createVariant(JSON.parse(readFileSync(source, 'utf8')), 'group-unforced-voice-v3');
const bytes = JSON.stringify(candidate, null, 2) + '\n';
writeFileSync(output, bytes);
console.log(JSON.stringify({sha256:createHash('sha256').update(bytes).digest('hex'), deployed:false}));
