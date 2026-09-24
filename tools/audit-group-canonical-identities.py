"""Read-only identity audit; raw metadata stays in the private operator directory."""

import argparse
import asyncio
import json
from pathlib import Path
from uuid import UUID, uuid5

from falkordb.asyncio import FalkorDB


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('private_directory', type=Path)
    args = parser.parse_args()
    directory = args.private_directory
    config = json.loads((directory / 'manual.private.json').read_text(encoding='utf-8-sig'))
    probes = config['graphitiProbes']
    namespaces = {p['conversationNamespace'] for p in probes}
    if len(namespaces) != 1:
        raise ValueError('Expected one reviewed namespace')
    namespace = namespaces.pop()
    actors = sorted({p['participantStableId'] for p in probes})
    client = FalkorDB(host='filomena-falkordb', port=6379)
    if namespace not in await client.list_graphs():
        raise ValueError('Namespace does not exist')
    graph = client.select_graph(namespace)
    result = await graph.ro_query(
        'MATCH (n) RETURN n.uuid, n.name, n.stable_id, n.group_id, labels(n), n.aliases'
    )
    rows = [dict(zip(['uuid', 'name', 'stable_id', 'group_id', 'labels', 'aliases'], row))
            for row in result.result_set]
    expected = []
    for actor in actors:
        uuid = str(uuid5(UUID('cc5bf3bb-68cb-5e0e-a63e-cf8e76cf5f8b'), namespace + '\0' + actor))
        expected.append({'uuid': uuid, 'stable_id': actor,
                         'uuid_matches': [r for r in rows if r['uuid'] == uuid],
                         'stable_matches': [r for r in rows if r['stable_id'] == actor]})
    with (directory / 'canonical-identity-audit.private.json').open('x', encoding='utf-8') as output:
        json.dump({'namespace': namespace, 'expected': expected, 'nodes': rows}, output, indent=2)
    print(json.dumps({'read_only': True, 'nodes': len(rows), 'participants': [
        {'ordinal': i, 'uuid_matches': len(e['uuid_matches']), 'stable_matches': len(e['stable_matches'])}
        for i, e in enumerate(expected)]}))


if __name__ == '__main__':
    asyncio.run(main())
