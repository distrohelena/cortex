#!/usr/bin/env python3
"""Read-only audit of legacy Person nodes across FalkorDB graph namespaces."""

import asyncio
import json
import os

from falkordb.asyncio import FalkorDB


async def main() -> None:
    client = FalkorDB(
        host=os.getenv('FALKORDB_HOST', 'filomena-falkordb'),
        port=int(os.getenv('FALKORDB_PORT', '6379')),
    )
    graph_names = sorted(await client.list_graphs())
    report = []

    for graph_name in graph_names:
        graph = client.select_graph(graph_name)
        result = await graph.query(
            """
            MATCH (p:Person)
            OPTIONAL MATCH (p)-[r]-()
            WITH p, count(r) AS edge_count
            RETURN p.uuid AS uuid, p.name AS name, p.stable_id AS stable_id,
                   p.display_name_current AS display_name_current,
                   p.aliases AS aliases, edge_count
            ORDER BY p.name, p.uuid
            """
        )
        headers = [
            entry[1] if isinstance(entry, (list, tuple)) and len(entry) > 1 else entry
            for entry in result.header
        ]
        rows = [dict(zip(headers, row, strict=True)) for row in result.result_set]
        canonical = [row for row in rows if str(row.get('stable_id') or '').startswith('whatsapp:')]
        legacy = [row for row in rows if not str(row.get('stable_id') or '').startswith('whatsapp:')]
        canonical_names = {
            str(name).casefold(): row['uuid']
            for row in canonical
            for name in [row.get('display_name_current'), *(row.get('aliases') or [])]
            if name
        }
        possible_matches = [
            {
                'legacy_uuid': row['uuid'],
                'legacy_name': row['name'],
                'canonical_uuid': canonical_names[str(row['name']).casefold()],
                'edge_count': row['edge_count'],
            }
            for row in legacy
            if row.get('name') and str(row['name']).casefold() in canonical_names
        ]
        report.append({
            'graph': graph_name,
            'legacy_person_count': len(legacy),
            'canonical_person_count': len(canonical),
            'legacy_with_edges': sum(1 for row in legacy if row['edge_count']),
            'possible_name_matches': possible_matches,
        })

    print(json.dumps({'read_only': True, 'graphs': report}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    asyncio.run(main())
