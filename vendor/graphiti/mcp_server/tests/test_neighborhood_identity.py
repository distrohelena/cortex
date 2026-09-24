"""Exact identity must remain observable when a participant has no facts."""

import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

from graphiti_core.nodes import EntityNode
from graphiti_core.errors import NodeNotFoundError

import graphiti_mcp_server as server


class NeighborhoodIdentityTests(unittest.IsolatedAsyncioTestCase):
    async def test_absent_participant_is_explicit_read_result_not_backend_outage(self):
        service = SimpleNamespace(get_client=AsyncMock(return_value=SimpleNamespace(
            driver=SimpleNamespace(clone=Mock(return_value=object())),
        )))
        edges = AsyncMock()
        with (
            patch.object(server, 'graphiti_service', service),
            patch.object(server.EntityNode, 'get_by_uuid', AsyncMock(side_effect=NodeNotFoundError('new-person'))),
            patch.object(server.EntityEdge, 'get_by_node_uuid', edges),
        ):
            result = await server.get_entity_neighborhood('group-a', 'new-person')
        self.assertEqual(result, {
            'status': 'entity_not_found', 'version': 'entity-neighborhood-absence:v1',
            'group_id': 'group-a', 'entity_uuid': 'new-person', 'facts': [],
        })
        edges.assert_not_awaited()

    async def test_outage_is_not_an_absent_participant(self):
        service = SimpleNamespace(get_client=AsyncMock(return_value=SimpleNamespace(
            driver=SimpleNamespace(clone=Mock(return_value=object())),
        )))
        with (
            patch.object(server, 'graphiti_service', service),
            patch.object(server.EntityNode, 'get_by_uuid', AsyncMock(side_effect=ConnectionError('offline'))),
        ):
            result = await server.get_entity_neighborhood('group-a', 'new-person')
        self.assertIn('error', result)
        self.assertNotIn('facts', result)

    async def test_empty_neighborhood_preserves_loaded_identity(self):
        entity = EntityNode(
            uuid='canonical-person', name='person', group_id='group-a',
            labels=['Entity', 'Person'], attributes={'stable_id': 'trusted-actor', 'display_name_current': 'Helena Assis'},
        )
        service = SimpleNamespace(get_client=AsyncMock(return_value=SimpleNamespace(
            driver=SimpleNamespace(clone=Mock(return_value=object())),
        )))
        with (
            patch.object(server, 'graphiti_service', service),
            patch.object(server.EntityNode, 'get_by_uuid', AsyncMock(return_value=entity)),
            patch.object(server.EntityEdge, 'get_by_node_uuid', AsyncMock(return_value=[])),
        ):
            result = await server.get_entity_neighborhood('group-a', 'canonical-person')
        self.assertEqual(result['facts'], [])
        self.assertEqual(result.get('entity'), {
            'uuid': 'canonical-person', 'group_id': 'group-a',
            'labels': ['Entity', 'Person'], 'stable_id': 'trusted-actor',
            'display_name_current': 'Helena Assis',
        })

    async def test_foreign_entity_is_rejected_even_without_facts(self):
        entity = EntityNode(uuid='person', name='person', group_id='foreign')
        service = SimpleNamespace(get_client=AsyncMock(return_value=SimpleNamespace(
            driver=SimpleNamespace(clone=Mock(return_value=object())),
        )))
        with (
            patch.object(server, 'graphiti_service', service),
            patch.object(server.EntityNode, 'get_by_uuid', AsyncMock(return_value=entity)),
        ):
            result = await server.get_entity_neighborhood('group-a', 'person')
        self.assertEqual(result, {'error': 'entity is outside the requested group'})


if __name__ == '__main__':
    unittest.main()
