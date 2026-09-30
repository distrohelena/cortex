"""Offline characterization of the installed queue + UUID admission contract.

Run in the existing image with --network none and without host credentials.
Only external model/database boundaries are replaced; queue and add_episode are real.
"""
import asyncio
import sys
import unittest
from datetime import datetime, timezone
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

sys.path.insert(0, '/app/mcp')
from src.services.queue_service import QueueService
import graphiti_core.graphiti as core
from graphiti_core.errors import NodeNotFoundError
from graphiti_core.nodes import EpisodicNode, EpisodeType

GROUP = 'reconciliation-offline-only'
EPISODE = '00000000-0000-5000-8000-000000000001'


class QueueReconciliationTests(unittest.IsolatedAsyncioTestCase):
    async def exercise(self, fail_first=False, queued=True):
        saved = {}
        counts = {'extractions': 0, 'writes': 0}
        entered = asyncio.Event()
        release = asyncio.Event()
        instance = object.__new__(core.Graphiti)
        instance.driver = SimpleNamespace(_database=GROUP)
        instance.clients = SimpleNamespace(driver=instance.driver)
        instance.tracer = MagicMock()
        instance.store_raw_episode_content = True
        instance.retrieve_episodes = AsyncMock(return_value=[])
        instance._extract_and_resolve_edges = AsyncMock(return_value=([], [], []))

        async def lookup(_driver, uuid):
            if uuid not in saved:
                raise NodeNotFoundError(uuid)
            return saved[uuid]

        async def extract(*_args, **_kwargs):
            counts['extractions'] += 1
            ordinal = counts['extractions']
            entered.set()
            await release.wait()
            if fail_first and ordinal == 1:
                raise RuntimeError('synthetic pre-persistence failure')
            return []

        async def persist(episode, *_args, **_kwargs):
            counts['writes'] += 1
            saved[episode.uuid] = episode
            return [], episode

        instance._process_episode_data = persist
        queue = QueueService()
        await queue.initialize(instance)
        queue._group_clients[GROUP] = instance
        args = dict(group_id=GROUP, name='synthetic', source_description='test',
                    episode_uuid=EPISODE, reference_time=datetime.now(timezone.utc))

        async def submit():
            if queued:
                await queue.add_episode(**args, content='same payload',
                                        episode_type=EpisodeType.text, entity_types=None, uuid=None)
            else:
                await instance.add_episode(**args, episode_body='same payload')

        with patch.object(EpisodicNode, 'get_by_uuid', lookup), \
                patch.object(core, 'extract_nodes', extract), \
                patch.object(core, 'resolve_extracted_nodes', AsyncMock(return_value=([], {}, []))), \
                patch.object(core, 'extract_attributes_from_nodes', AsyncMock(return_value=[])):
            first = asyncio.create_task(submit())
            await asyncio.wait_for(entered.wait(), 3)
            second = asyncio.create_task(submit())
            await asyncio.sleep(0)  # Schedule the second admission while the first is blocked.
            release.set()
            await asyncio.wait_for(asyncio.gather(first, second), 3)
            if queued:
                await asyncio.wait_for(queue._episode_queues[GROUP].join(), 3)
        self.assertEqual(list(saved), [EPISODE])
        return counts

    async def test_same_uuid_in_flight_then_retry_extracts_and_persists_once(self):
        self.assertEqual(await self.exercise(), {'extractions': 1, 'writes': 1})

    async def test_failed_before_persistence_then_explicit_retry_can_persist(self):
        self.assertEqual(await self.exercise(fail_first=True), {'extractions': 2, 'writes': 1})

    async def test_counterexample_direct_concurrent_calls_are_not_queue_safe(self):
        self.assertEqual(await self.exercise(queued=False), {'extractions': 2, 'writes': 2})


if __name__ == '__main__':
    unittest.main()
