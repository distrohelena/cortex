import asyncio
from datetime import UTC, datetime, timedelta
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock, Mock, call, patch

import pytest
from graphiti_core.driver.driver import GraphProvider
from graphiti_core.edges import EntityEdge
from graphiti_core.nodes import EntityNode
from graphiti_core.search.search_filters import ComparisonOperator

from services.queue_service import QueueService
from utils.social_evidence import AddressTermInvariantError, merge_address_term_observation
from utils.social_retrieval import (
    active_edge_filter,
    clear_group_namespaces,
    filter_neighborhood_edges,
    format_fact_with_group_nodes,
    format_fact_with_nodes,
    load_endpoint_nodes_by_group,
    normalize_persisted_edge_direction,
)

NOW = datetime(2026, 9, 4, 18, 30, tzinfo=UTC)
GROUP = 'whatsapp-group-a-g-us'
PARTICIPANT = 'participant-uuid'
FILOMENA = 'filomena-uuid'


def test_falkordb_clear_targets_each_physical_group_graph() -> None:
    async def run() -> tuple[list[str], list[tuple[object, object]]]:
        clones: list[str] = []
        cloned_drivers: dict[str, object] = {}

        def clone(database: str) -> object:
            clones.append(database)
            cloned = object()
            cloned_drivers[database] = cloned
            return cloned

        driver = SimpleNamespace(provider=GraphProvider.FALKORDB, clone=clone)
        calls: list[tuple[object, object]] = []

        async def fake_clear(target, group_ids=None):
            calls.append((target, group_ids))

        with patch('utils.social_retrieval.clear_data', side_effect=fake_clear):
            await clear_group_namespaces(driver, ['group-a', 'group-b'])
        return clones, calls

    clones, calls = asyncio.run(run())
    assert clones == ['group-a', 'group-b']
    assert [group_ids for _, group_ids in calls] == [None, None]
    assert calls[0][0] is not calls[1][0]


def test_non_falkordb_clear_uses_group_property_filter() -> None:
    async def run() -> list[tuple[object, object]]:
        driver = SimpleNamespace(provider=GraphProvider.NEO4J)
        calls: list[tuple[object, object]] = []

        async def fake_clear(target, group_ids=None):
            calls.append((target, group_ids))

        with patch('utils.social_retrieval.clear_data', side_effect=fake_clear):
            await clear_group_namespaces(driver, ['group-a', 'group-b'])
        return calls

    calls = asyncio.run(run())
    assert calls == [(calls[0][0], ['group-a', 'group-b'])]


def edge(
    uuid: str,
    *,
    group_id: str = GROUP,
    invalid_at: datetime | None = None,
    expired_at: datetime | None = None,
    valid_at: datetime | None = NOW,
    created_at: datetime = NOW,
) -> EntityEdge:
    return EntityEdge(
        uuid=uuid,
        source_node_uuid=PARTICIPANT,
        target_node_uuid=FILOMENA,
        group_id=group_id,
        name='RELATION',
        fact=uuid,
        created_at=created_at,
        valid_at=valid_at,
        invalid_at=invalid_at,
        expired_at=expired_at,
    )


def test_active_edge_filter_excludes_invalid_expired_and_future_facts() -> None:
    filters = active_edge_filter(NOW)

    assert filters.invalid_at is not None
    assert filters.invalid_at[0][0].comparison_operator is ComparisonOperator.is_null
    assert filters.invalid_at[1][0].comparison_operator is ComparisonOperator.greater_than
    assert filters.expired_at is not None
    assert filters.expired_at[0][0].comparison_operator is ComparisonOperator.is_null
    assert filters.valid_at is not None

    results = filter_neighborhood_edges(
        [
            edge('active'),
            edge('invalid', invalid_at=NOW - timedelta(seconds=1)),
            edge('expired', expired_at=NOW - timedelta(seconds=1)),
            edge('future', valid_at=NOW + timedelta(seconds=1)),
        ],
        group_id=GROUP,
        active_only=True,
        now=NOW,
        limit=10,
    )

    assert [item.uuid for item in results] == ['active']


def test_neighborhood_is_group_bound_and_deterministically_limited() -> None:
    results = filter_neighborhood_edges(
        [
            edge('older', created_at=NOW - timedelta(days=1)),
            edge('newer-b', created_at=NOW),
            edge('newer-a', created_at=NOW),
            edge('other-group', group_id='other-group'),
        ],
        group_id=GROUP,
        active_only=False,
        now=NOW,
        limit=2,
    )

    assert [item.uuid for item in results] == ['newer-a', 'newer-b']


def test_neighborhood_restores_persisted_edge_direction() -> None:
    reversed_edge = edge('direction')
    reversed_edge.source_node_uuid = FILOMENA
    reversed_edge.target_node_uuid = PARTICIPANT
    reversed_edge.attributes = {
        'source_uuid': PARTICIPANT,
        'target_uuid': FILOMENA,
    }

    normalized = normalize_persisted_edge_direction(reversed_edge)

    assert normalized.source_node_uuid == PARTICIPANT
    assert normalized.target_node_uuid == FILOMENA


def test_neighborhood_fails_closed_on_inconsistent_persisted_endpoints() -> None:
    inconsistent = edge('direction-conflict')
    inconsistent.attributes = {
        'source_uuid': 'unexpected-source',
        'target_uuid': FILOMENA,
    }

    with pytest.raises(ValueError, match='inconsistent persisted endpoints'):
        normalize_persisted_edge_direction(inconsistent)


def test_fact_format_marks_only_stable_id_nodes_as_canonical() -> None:
    participant = EntityNode(
        uuid=PARTICIPANT,
        name='whatsapp:5511999999999@s.whatsapp.net',
        group_id=GROUP,
        labels=['Person'],
        attributes={'stable_id': 'whatsapp:5511999999999@s.whatsapp.net'},
    )
    legacy = EntityNode(
        uuid=FILOMENA,
        name='Gabriel',
        group_id=GROUP,
        labels=['Person'],
        attributes={},
    )

    result = format_fact_with_nodes(edge('fact-1'), {PARTICIPANT: participant, FILOMENA: legacy})

    assert result['source_node']['uuid'] == PARTICIPANT
    assert result['source_node']['canonical'] is True
    assert result['target_node']['uuid'] == FILOMENA
    assert result['target_node']['canonical'] is False
    assert 'name_embedding' not in result['source_node']


def test_semantic_endpoint_nodes_are_loaded_from_each_falkordb_group() -> None:
    async def run():
        group_a_edge = edge('a', group_id='group-a')
        group_b_edge = edge('b', group_id='group-b')
        drivers = {
            'group-a': object(),
            'group-b': object(),
        }
        driver = SimpleNamespace(
            provider=GraphProvider.FALKORDB,
            clone=Mock(side_effect=lambda database: drivers[database]),
        )

        async def load_nodes(group_driver, uuids):
            group_id = next(key for key, value in drivers.items() if value is group_driver)
            return [
                EntityNode(
                    uuid=uuid,
                    name=f'{group_id}:{uuid}',
                    group_id=group_id,
                    labels=['Person'],
                    attributes={'stable_id': f'{group_id}:{uuid}'},
                )
                for uuid in uuids
            ]

        with patch.object(EntityNode, 'get_by_uuids', side_effect=load_nodes):
            loaded = await load_endpoint_nodes_by_group(driver, [group_a_edge, group_b_edge])
        return group_a_edge, group_b_edge, loaded, driver

    group_a_edge, group_b_edge, loaded, driver = asyncio.run(run())
    assert driver.clone.call_args_list == [
        call(database='group-a'),
        call(database='group-b'),
    ]
    assert format_fact_with_group_nodes(group_a_edge, loaded)['source_node']['name'].startswith(
        'group-a:'
    )
    assert format_fact_with_group_nodes(group_b_edge, loaded)['source_node']['name'].startswith(
        'group-b:'
    )


def test_queue_forwards_bound_entities_and_real_custom_instructions() -> None:
    asyncio.run(_assert_queue_forwards_bound_entities_and_real_custom_instructions())


async def _assert_queue_forwards_bound_entities_and_real_custom_instructions() -> None:
    queue = QueueService()
    client = SimpleNamespace(add_episode=AsyncMock())
    await queue.initialize(client)
    queue.get_group_client = lambda _group_id: client

    async def run_immediately(self, _group_id, process_func):
        await process_func()
        return 1

    queue.add_episode_task = MethodType(run_immediately, queue)
    bindings = [
        {
            'uuid': 'speaker',
            'role': 'speaker',
            'name': 'agent:filomena',
            'entity_type': 'Agent',
            'labels': ['Agent'],
            'attributes': {'stable_id': 'agent:filomena'},
        }
    ]

    await queue.add_episode(
        group_id=GROUP,
        name='outbound',
        content='raw outbound',
        source_description='test',
        episode_type='json',
        entity_types={},
        uuid=None,
        episode_uuid='episode-1',
        bound_entities=bindings,
        extraction_body='Filomena: oi',
        custom_extraction_instructions='Only concrete observations.',
    )

    client.add_episode.assert_awaited_once()
    kwargs = client.add_episode.await_args.kwargs
    assert kwargs['episode_body'] == 'raw outbound'
    assert kwargs['bound_entities'] == bindings
    assert kwargs['custom_extraction_instructions'] == 'Only concrete observations.'


def test_queue_uses_one_dedicated_graphiti_client_per_group() -> None:
    base_driver = SimpleNamespace(clone=Mock(side_effect=lambda database: f'driver:{database}'))
    base = SimpleNamespace(
        driver=base_driver,
        llm_client=object(),
        embedder=object(),
        cross_encoder=object(),
        store_raw_episode_content=True,
        max_coroutines=1,
    )
    created: list[SimpleNamespace] = []

    def make_client(**kwargs):
        client = SimpleNamespace(**kwargs)
        created.append(client)
        return client

    queue = QueueService()
    asyncio.run(queue.initialize(base))
    with patch('services.queue_service.Graphiti', side_effect=make_client):
        group_a = queue.get_group_client('group-a')
        same_group_a = queue.get_group_client('group-a')
        group_b = queue.get_group_client('group-b')

    assert group_a is same_group_a
    assert group_a is not group_b
    assert [client.graph_driver for client in created] == ['driver:group-a', 'driver:group-b']
    assert base.driver is base_driver


def test_same_group_tasks_cannot_start_multiple_workers_during_enqueue_race() -> None:
    async def run() -> int:
        queue = QueueService()
        active = 0
        maximum_active = 0

        async def task() -> None:
            nonlocal active, maximum_active
            active += 1
            maximum_active = max(maximum_active, active)
            await asyncio.sleep(0.02)
            active -= 1

        await asyncio.gather(
            queue.add_episode_task(GROUP, task),
            queue.add_episode_task(GROUP, task),
            queue.add_episode_task(GROUP, task),
        )
        await queue._episode_queues[GROUP].join()
        return maximum_active

    assert asyncio.run(run()) == 1


def test_address_term_evidence_is_idempotent_and_set_like_per_run() -> None:
    first = merge_address_term_observation(
        existing=None,
        group_id=GROUP,
        source_uuid=FILOMENA,
        target_uuid=PARTICIPANT,
        source_name='agent:filomena',
        target_name='whatsapp:5511999999999@s.whatsapp.net',
        term='Professor',
        normalized_term='professor',
        run_id='run-1',
        episode_uuid='episode-1',
        reference_time=NOW,
    )
    repeated_chunk = merge_address_term_observation(
        existing=first,
        group_id=GROUP,
        source_uuid=FILOMENA,
        target_uuid=PARTICIPANT,
        source_name='agent:filomena',
        target_name='whatsapp:5511999999999@s.whatsapp.net',
        term='Professor',
        normalized_term='professor',
        run_id='run-1',
        episode_uuid='episode-2',
        reference_time=NOW,
    )
    second_turn = merge_address_term_observation(
        existing=repeated_chunk,
        group_id=GROUP,
        source_uuid=FILOMENA,
        target_uuid=PARTICIPANT,
        source_name='agent:filomena',
        target_name='whatsapp:5511999999999@s.whatsapp.net',
        term='Professor',
        normalized_term='professor',
        run_id='run-2',
        episode_uuid='episode-3',
        reference_time=NOW,
    )

    assert second_turn.uuid == first.uuid
    assert second_turn.attributes['evidence_run_ids'] == ['run-1', 'run-2']
    assert second_turn.attributes['evidence_episode_uuids'] == [
        'episode-1',
        'episode-2',
        'episode-3',
    ]


def test_address_term_evidence_fails_closed_on_endpoint_mismatch() -> None:
    existing = merge_address_term_observation(
        existing=None,
        group_id=GROUP,
        source_uuid=FILOMENA,
        target_uuid=PARTICIPANT,
        source_name='agent:filomena',
        target_name='whatsapp:5511999999999@s.whatsapp.net',
        term='Professor',
        normalized_term='professor',
        run_id='run-1',
        episode_uuid='episode-1',
        reference_time=NOW,
    )

    with pytest.raises(AddressTermInvariantError):
        merge_address_term_observation(
            existing=existing,
            group_id=GROUP,
            source_uuid=FILOMENA,
            target_uuid='wrong-participant',
            source_name='agent:filomena',
            target_name='whatsapp:other@s.whatsapp.net',
            term='Professor',
            normalized_term='professor',
            run_id='run-2',
            episode_uuid='episode-2',
            reference_time=NOW,
        )
