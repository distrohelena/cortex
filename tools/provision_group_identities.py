"""Explicit, bounded canonical-identity provisioning; never writes facts or episodes."""


def missing_identities(audit):
    missing = []
    for item in audit['expected']:
        by_uuid, by_actor = item['uuid_matches'], item['stable_matches']
        if not by_uuid and not by_actor:
            missing.append({'uuid': item['uuid'], 'stable_id': item['stable_id']})
            continue
        if len(by_uuid) != 1 or len(by_actor) != 1 or by_uuid != by_actor:
            raise ValueError('Ambiguous existing identity; no mutation allowed')
        node = by_uuid[0]
        if (node['group_id'] != audit['namespace'] or 'Person' not in node['labels']
                or node['stable_id'] != item['stable_id'] or node['uuid'] != item['uuid']):
            raise ValueError('Invalid existing canonical identity')
    return missing


CREATE_MISSING = '''
OPTIONAL MATCH (existing)
WHERE existing.uuid IN $uuids OR existing.stable_id IN $variants OR existing.name IN $variants
    OR any(a IN coalesce(existing.aliases, []) WHERE a IN $variants)
WITH count(existing) AS conflicts
WHERE conflicts = 0
UNWIND $items AS item
CREATE (n:Entity:Person {uuid: item.uuid, name: item.stable_id,
    stable_id: item.stable_id, group_id: $namespace, summary: '',
    created_at: $created_at, aliases: []})
RETURN n.uuid
'''


async def main():
    import argparse
    import json
    from datetime import datetime, timezone
    from pathlib import Path
    from uuid import UUID, uuid5

    from falkordb.asyncio import FalkorDB

    parser = argparse.ArgumentParser()
    parser.add_argument('private_directory', type=Path)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--verify-intent', action='store_true')
    args = parser.parse_args()
    directory = args.private_directory
    audit = json.loads((directory / 'canonical-identity-audit.private.json').read_text())
    config = json.loads((directory / 'manual.private.json').read_text(encoding='utf-8-sig'))
    expected = {(p['conversationNamespace'], p['participantStableId']) for p in config['graphitiProbes']}
    if expected != {(audit['namespace'], e['stable_id']) for e in audit['expected']}:
        raise ValueError('Audit does not match reviewed host binding')
    for e in audit['expected']:
        if e['uuid'] != str(uuid5(UUID('cc5bf3bb-68cb-5e0e-a63e-cf8e76cf5f8b'), audit['namespace'] + '\0' + e['stable_id'])):
            raise ValueError('Canonical UUID mismatch')
    items = missing_identities(audit)
    if len(items) != 2:
        raise ValueError('This approved slice requires exactly two missing identities')
    client = FalkorDB(host='filomena-falkordb', port=6379)
    if audit['namespace'] not in await client.list_graphs():
        raise ValueError('Namespace unavailable')
    graph = client.select_graph(audit['namespace'])
    if args.verify_intent:
        intent = json.loads((directory / 'canonical-identity-provision.private.json').read_text())
        recorded = intent['parameters']
        if recorded['namespace'] != audit['namespace'] or recorded['items'] != items:
            raise ValueError('Intent differs from reviewed identity plan')
        verification = await graph.ro_query(
            'MATCH (n) WHERE n.uuid IN $uuids RETURN n.uuid, n.stable_id, n.group_id, labels(n)',
            {'uuids': recorded['uuids']},
        )
        if len(verification.result_set) != len(items):
            raise ValueError('Incomplete identity registration')
        for item in items:
            matches = [r for r in verification.result_set if r[0] == item['uuid']]
            if len(matches) != 1 or matches[0][1] != item['stable_id'] or matches[0][2] != audit['namespace'] or 'Person' not in matches[0][3]:
                raise ValueError('Registered identity mismatch')
        with (directory / 'canonical-identity-reconciliation.private.json').open('x', encoding='utf-8') as output:
            json.dump({'state': 'verified_read_only', 'rows': verification.result_set}, output, indent=2)
        print(json.dumps({'verified_read_only': len(items), 'writes': 0}))
        return
    params = {'items': items, 'uuids': [i['uuid'] for i in items],
              'actors': [i['stable_id'] for i in items], 'namespace': audit['namespace'],
              'created_at': datetime.now(timezone.utc).isoformat()}
    # Read-only recheck against trusted PN/LID aliases; never match a display name.
    aliases = json.loads((directory / 'participants.private.json').read_text(encoding='utf-8-sig'))
    variants = set(params['actors'])
    for alias, actor in aliases.items():
        if actor in params['actors']:
            variants.update([alias, 'whatsapp:' + alias])
    for actor in params['actors']:
        variants.add(actor.removeprefix('whatsapp:'))
    params['variants'] = sorted(variants)
    conflicts = await graph.ro_query(
        'MATCH (n) WHERE n.uuid IN $uuids OR n.stable_id IN $variants OR n.name IN $variants '
        'OR any(a IN coalesce(n.aliases, []) WHERE a IN $variants) RETURN n.uuid',
        {'uuids': params['uuids'], 'variants': sorted(variants)},
    )
    if conflicts.result_set:
        raise ValueError('Identity or alias already exists; reconciliation required')
    if not args.apply:
        print(json.dumps({'dry_run': True, 'missing': len(items), 'conflicts': 0}))
        return
    # Fail if a previous attempt exists. Intent is written before the atomic graph query.
    receipt = directory / 'canonical-identity-provision.private.json'
    with receipt.open('x', encoding='utf-8') as output:
        json.dump({'state': 'intent', 'parameters': params}, output, indent=2)
    result = await graph.query(CREATE_MISSING, params)
    created = sorted(row[0] for row in result.result_set)
    if created != sorted(params['uuids']):
        raise ValueError('Concurrent identity conflict; inspect intent receipt, do not retry blindly')
    with (directory / 'canonical-identity-provision-confirmed.private.json').open('x', encoding='utf-8') as output:
        json.dump({'state': 'created', 'uuids': created}, output, indent=2)
    print(json.dumps({'created': len(created), 'facts_written': 0, 'episodes_written': 0}))


if __name__ == '__main__':
    import asyncio
    asyncio.run(main())
