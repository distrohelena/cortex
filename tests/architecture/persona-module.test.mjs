import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync, readFileSync, readdirSync} from 'node:fs';
import {resolve, dirname, basename} from 'node:path';
import {fileURLToPath} from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const libraryPath = 'src/Cortex.Personas/Cortex.Personas.csproj';
const testPath = 'tests/Cortex.Personas.Tests/Cortex.Personas.Tests.csproj';
const approvedProjects = [libraryPath, testPath].sort();
const read = path => readFileSync(resolve(root, path), 'utf8');
const includes = (xml, element, attribute) => [...xml.matchAll(
  new RegExp(`<${element}\\b[^>]*\\b${attribute}\\s*=\\s*["']([^"']+)["']`, 'g'),
)].map(([, value]) => value.replaceAll('\\', '/'));

test('persona projects are explicitly registered', () => {
  const projects = includes(read('Cortex.slnx'), 'Project', 'Path');
  for (const path of approvedProjects) {
    assert.equal(projects.filter(project => project === path).length, 1, path);
  }
});

test('offline solution contains only persona projects', () => {
  assert.deepEqual(includes(read('Personas.slnx'), 'Project', 'Path').sort(), approvedProjects);
});

test('persona library has no project or package dependencies', () => {
  assert.doesNotMatch(read(libraryPath), /<(?:ProjectReference|PackageReference)\b/);
});

test('core and Filo production projects do not depend on personas', () => {
  const filoSource = resolve(root, '../filo/src');
  assert.ok(existsSync(filoSource), 'Required sibling Filo checkout is unavailable');
  const projects = findProjects(filoSource);
  assert.ok(projects.length > 0, 'Filo has no production project files to verify');
  projects.push(resolve(root, 'src/Cortex.Core/Cortex.Core.csproj'));
  for (const project of projects) {
    const references = includes(readFileSync(project, 'utf8'), 'ProjectReference', 'Include');
    for (const reference of references) {
      assert.notEqual(basename(reference).toLowerCase(), 'cortex.personas.csproj', project);
    }
  }
});

test('persona tests use only the approved local project references', () => {
  const projectDirectory = dirname(resolve(root, testPath));
  const references = includes(read(testPath), 'ProjectReference', 'Include');
  const resolved = references.map(reference => resolve(projectDirectory, reference)).sort();
  assert.deepEqual(resolved, [
    resolve(root, libraryPath),
    resolve(root, 'src/Cortex.Core/Cortex.Core.csproj'),
  ].sort());
  for (const reference of resolved) assert.ok(existsSync(reference), reference);
});

function findProjects(directory) {
  const projects = [];
  for (const entry of readdirSync(directory, {withFileTypes: true})) {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory() && !['bin', 'obj', 'node_modules'].includes(entry.name)) {
      projects.push(...findProjects(path));
    } else if (entry.isFile() && entry.name.endsWith('.csproj')) {
      projects.push(path);
    }
  }
  return projects;
}
