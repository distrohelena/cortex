import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root = new URL('../../', import.meta.url);
const baseline = JSON.parse(readFileSync(new URL('cortex-compatibility-baseline.json', import.meta.url)));
test('approved artifacts retain exact bytes', () => {
  assert.ok(baseline.files.length > 0);
  for (const {path, sha256} of baseline.files) {
    const currentPath = path.replace(/^src\/Filomena\.Evaluation\//, 'src/Cortex.Evaluation/');
    assert.equal(createHash('sha256').update(readFileSync(new URL(currentPath, root))).digest('hex'), sha256, path);
  }
});

test('all solution projects and their direct project references resolve', () => {
  const solution = readFileSync(new URL('Filomena.slnx', root), 'utf8');
  const projects = [...solution.matchAll(/<Project Path="([^"]+)"/g)].map(m => m[1]);
  assert.ok(projects.includes('src/Cortex.Inference/Cortex.Inference.csproj'));
  for (const path of projects) {
    const project = new URL(path, root);
    assert.ok(existsSync(project), path);
    for (const [, reference] of readFileSync(project, 'utf8').matchAll(/<ProjectReference Include="([^"]+)"/g)) {
      assert.ok(existsSync(new URL(reference.replaceAll('\\', '/'), project)), `${path}: ${reference}`);
    }
  }
});
test('Cortex Core has no application or adapter dependency', () => {
  const project = new URL('src/Cortex.Core/Cortex.Core.csproj', root);
  assert.ok(existsSync(project), 'Cortex.Core project must exist');
  assert.doesNotMatch(readFileSync(project, 'utf8'), /<ProjectReference\b/);
});
