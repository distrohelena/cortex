import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
test('CoreHasNoTemporalReference', () => {
  assert.doesNotMatch(read('src/Cortex.Core/Cortex.Core.csproj'), /Temporalio|Cortex\.Temporal/);
});
test('Temporal module references Core and pins the approved SDK', () => {
  const project = read('src/Cortex.Temporal/Cortex.Temporal.csproj');
  assert.match(project, /ProjectReference Include="\.\.\/Cortex\.Core\/Cortex\.Core\.csproj"/);
  assert.match(project, /PackageReference Include="Temporalio" Version="1\.20\.0"/);
  assert.doesNotMatch(project, /ProjectReference[^>]+Cortex\.(Inference|Memory|OpenClaw)/);
});
test('Temporal projects are registered exactly once', () => {
  const solution = read('Cortex.slnx');
  for (const path of ['src/Cortex.Temporal/Cortex.Temporal.csproj', 'tests/Cortex.Temporal.Tests/Cortex.Temporal.Tests.csproj']) {
    assert.equal(solution.split(`Path="${path}"`).length - 1, 1);
  }
});
