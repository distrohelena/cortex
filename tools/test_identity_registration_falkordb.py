"""Run only against the disposable network-isolated FalkorDB test container."""
import unittest

from falkordb.asyncio import FalkorDB
from provision_group_identities import CREATE_MISSING


class RegistrationIntegrationTests(unittest.IsolatedAsyncioTestCase):
    async def test_create_only_and_atomic_conflict_guard(self):
        client = FalkorDB(host='127.0.0.1', port=6379)
        graph = client.select_graph('isolated-identity-registration-test')
        params = {
            'items': [{'uuid': 'one', 'stable_id': 'actor-one'}, {'uuid': 'two', 'stable_id': 'actor-two'}],
            'uuids': ['one', 'two'], 'actors': ['actor-one', 'actor-two'],
            'variants': ['actor-one', 'actor-two'], 'namespace': 'test',
            'created_at': '2026-09-24T00:00:00+00:00',
        }
        first = await graph.query(CREATE_MISSING, params)
        self.assertEqual(sorted(row[0] for row in first.result_set), ['one', 'two'])
        second = await graph.query(CREATE_MISSING, params)
        self.assertEqual(second.result_set, [])
        params['items'][1] = {'uuid': 'three', 'stable_id': 'actor-three'}
        params['uuids'][1] = 'three'
        self.assertEqual((await graph.query(CREATE_MISSING, params)).result_set, [])
        nodes = await graph.ro_query('MATCH (n) RETURN n.uuid ORDER BY n.uuid')
        self.assertEqual(nodes.result_set, [['one'], ['two']])
        edges = await graph.ro_query('MATCH ()-[r]->() RETURN count(r)')
        self.assertEqual(edges.result_set, [[0]])


if __name__ == '__main__':
    unittest.main()
