// Creates a NEW host document for --manual-shadow only. No active file is overwritten.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname, join } from 'node:path';

const [baseFile, profileFile, outputDirectory, ...extra] = process.argv.slice(2);
if (!baseFile || !profileFile || !outputDirectory || extra.length) throw new Error('Expected base host config, participation profile, NEW private output directory');
const base = resolve(baseFile);
const host = JSON.parse(await readFile(base, 'utf8'));
const profile = JSON.parse(await readFile(resolve(profileFile), 'utf8'));
if (profile.version !== 'systemone-participation:v1' || profile.model !== 'jev-1.13.0') throw new Error('Unsupported profile');
const project = resolve(dirname(base), host.projectFile);
const bytes = await readFile(project);
if (createHash('sha256').update(bytes).digest('hex').toUpperCase() !== host.expectedProjectSha256) throw new Error('Project pin mismatch');
const directory = resolve(outputDirectory);
await mkdir(directory); // Existing destination fails; caller supplies a private parent with reviewed ACL.
await mkdir(join(directory, 'journal'));
host.projectFile = project;
host.journalDirectory = join(directory, 'journal');
host.systemOneParticipation = profile;
await writeFile(join(directory, 'manual-shadow.private.json'), JSON.stringify(host, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
console.log(JSON.stringify({ status: 'prepared', command: '--manual-shadow', projectPinUnchanged: true, transportAvailable: false }));
