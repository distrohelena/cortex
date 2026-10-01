export function validateManifest(manifest, tracked) {
  const errors = new Set();
  if (manifest?.schemaVersion !== 1 || !/^[a-f0-9]{40}$/.test(manifest.sourceCommit ?? '') ||
      !Array.isArray(manifest.files)) return ['manifest_invalid'];
  const sources = new Set(), destinations = new Set(), expected = new Set(tracked);
  const safe = path => typeof path === 'string' && path.length > 0 &&
    !path.includes('\\') && !path.includes(':') && !path.startsWith('/') &&
    !path.split('/').some(part => !part || part === '.' || part === '..');
  for (const file of manifest.files) {
    if (!safe(file.sourcePath) || !safe(file.destinationPath)) errors.add('path_invalid');
    if (!['cortex','filo','both-metadata'].includes(file.owner)) errors.add('owner_invalid');
    if (file.owner === 'both-metadata' && !['.gitignore','.gitattributes','global.json'].includes(file.sourcePath))
      errors.add('shared_source_forbidden');
    if (!/^[a-f0-9]{64}$/.test(file.sha256 ?? '') || !file.reason) errors.add('evidence_invalid');
    if (sources.has(file.sourcePath)) errors.add('source_duplicate');
    sources.add(file.sourcePath);
    if (!expected.has(file.sourcePath)) errors.add('source_unexpected');
    for (const owner of file.owner === 'both-metadata' ? ['cortex','filo'] : [file.owner]) {
      const key = owner + ':' + String(file.destinationPath).toLowerCase();
      if (destinations.has(key)) errors.add('destination_duplicate');
      destinations.add(key);
    }
  }
  for (const path of expected) if (!sources.has(path)) errors.add('file_unclassified');
  return [...errors].sort();
}
