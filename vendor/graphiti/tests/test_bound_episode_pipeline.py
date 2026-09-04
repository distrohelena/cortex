import asyncio
from datetime import UTC, datetime
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock

import pytest

import graphiti_core.graphiti as graphiti_module
from graphiti_core.edges import EntityEdge
from graphiti_core.errors import NodeNotFoundError
from graphiti_core.graphiti import Graphiti
from graphiti_core.nodes import EntityNode, EpisodeType, EpisodicNode
from graphiti_core.utils.maintenance.bound_entities import BoundEntity, BoundEntityInvariantError

GROUP = 'bound-pipeline-test'
EPISODE_UUID = '22222222-2222-5222-8222-222222222222'
PARTICIPANT_UUID = '11111111-1111-5111-8111-111111111111'
ADDRESSEE_UUID = '33333333-3333-5333-8333-333333333333'
NOW = datetime(2026, 9, 4, tzinfo=UTC)


class FakeSpan:
    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def add_attributes(self, _attributes):
        pass

    def set_status(self, *_args):
        pass

    def record_exception(self, *_args):
        pass


class FakeTracer:
    def start_span(self, _name):
        return FakeSpan()


class FakeDriver:
    _database = GROUP
    provider = SimpleNamespace(value='fake')

    def clone(self, database):
        self._database = database
        return self


def binding() -> BoundEntity:
    return BoundEntity(
        uuid=PARTICIPANT_UUID,
        role='speaker',
        name='whatsapp:5511981042361@s.whatsapp.net',
        entity_type='Person',
        labels=['Person'],
        attributes={
            'stable_id': 'whatsapp:5511981042361@s.whatsapp.net',
            'display_name_current': 'Helena',
            'aliases': ['Helena'],
        },
    )


def addressee_binding() -> BoundEntity:
    return BoundEntity(
        uuid=ADDRESSEE_UUID,
        role='addressee',
        name='whatsapp:5511999999999@s.whatsapp.net',
        entity_type='Person',
        labels=['Person'],
        attributes={
            'stable_id': 'whatsapp:5511999999999@s.whatsapp.net',
            'display_name_current': 'Gabriel',
            'aliases': ['Gabriel'],
        },
    )


def graphiti() -> Graphiti:
    instance = object.__new__(Graphiti)
    instance.driver = FakeDriver()
    instance.clients = SimpleNamespace(driver=instance.driver)
    instance.tracer = FakeTracer()
    instance.store_raw_episode_content = True
    instance.max_coroutines = 1
    instance.embedder = SimpleNamespace()
    instance.llm_client = SimpleNamespace()
    instance.retrieve_episodes = AsyncMock(return_value=[])
    return instance


def patch_missing_nodes(monkeypatch) -> None:
    async def missing_episode(_driver, uuid):
        raise NodeNotFoundError(uuid)

    async def missing_entity(_driver, uuid):
        raise NodeNotFoundError(uuid)

    monkeypatch.setattr(EpisodicNode, 'get_by_uuid', missing_episode)
    monkeypatch.setattr(EntityNode, 'get_by_uuid', missing_entity)


def test_pipeline_binds_only_structural_speaker_and_persists_raw_episode(monkeypatch) -> None:
    instance = graphiti()
    patch_missing_nodes(monkeypatch)
    marker = EntityNode(
        uuid='candidate-speaker',
        name=binding().name,
        group_id=GROUP,
        labels=['Entity', 'Person'],
        created_at=NOW,
    )
    gabriel = EntityNode(
        uuid='mentioned-gabriel',
        name='Gabriel',
        group_id=GROUP,
        labels=['Entity', 'Person'],
        created_at=NOW,
    )
    extract_nodes = AsyncMock(return_value=[marker, gabriel])
    monkeypatch.setattr(graphiti_module, 'extract_nodes', extract_nodes)
    monkeypatch.setattr(
        graphiti_module,
        'resolve_extracted_nodes',
        AsyncMock(return_value=([gabriel], {gabriel.uuid: gabriel.uuid}, [])),
    )

    captured = {}

    async def extract_edges(self, episode, extracted_nodes, *_args, **_kwargs):
        captured['extraction_content'] = episode.content
        captured['edge_nodes'] = extracted_nodes
        return (
            [
                EntityEdge(
                    uuid='fact-1',
                    source_node_uuid=PARTICIPANT_UUID,
                    target_node_uuid=gabriel.uuid,
                    group_id=GROUP,
                    name='QUOTED',
                    fact='Helena said that Gabriel finished the game',
                    created_at=NOW,
                )
            ],
            [],
            [],
        )

    instance._extract_and_resolve_edges = MethodType(extract_edges, instance)
    monkeypatch.setattr(
        graphiti_module,
        'extract_attributes_from_nodes',
        AsyncMock(side_effect=lambda _clients, nodes, *_args, **_kwargs: nodes),
    )

    async def process(self, episode, nodes, edges, *_args, **_kwargs):
        captured['raw_content'] = episode.content
        captured['nodes'] = nodes
        captured['edges'] = edges
        return [], episode

    instance._process_episode_data = MethodType(process, instance)

    result = asyncio.run(
        instance.add_episode(
            name='inbound',
            episode_body='{"text":"Gabriel disse que terminou o jogo."}',
            source_description='test',
            reference_time=NOW,
            source=EpisodeType.json,
            group_id=GROUP,
            episode_uuid=EPISODE_UUID,
            bound_entity=binding(),
            extraction_body='Gabriel disse que terminou o jogo.',
        )
    )

    assert result.episode.uuid == EPISODE_UUID
    assert captured['raw_content'] == '{"text":"Gabriel disse que terminou o jogo."}'
    assert '"role":"speaker"' in captured['extraction_content']
    assert [node.uuid for node in captured['edge_nodes']] == ['mentioned-gabriel', PARTICIPANT_UUID]
    assert {node.uuid for node in captured['nodes']} == {'mentioned-gabriel', PARTICIPANT_UUID}
    assert captured['edges'][0].source_node_uuid == PARTICIPANT_UUID
    assert captured['edges'][0].target_node_uuid == 'mentioned-gabriel'


def test_pipeline_binds_speaker_and_addressee_without_binding_mentioned_person(monkeypatch) -> None:
    instance = graphiti()
    patch_missing_nodes(monkeypatch)
    speaker_marker = EntityNode(
        uuid='candidate-speaker',
        name=binding().name,
        group_id=GROUP,
        labels=['Entity', 'Agent'],
        created_at=NOW,
    )
    addressee_marker = EntityNode(
        uuid='candidate-addressee',
        name=addressee_binding().name,
        group_id=GROUP,
        labels=['Entity', 'Person'],
        created_at=NOW,
    )
    mentioned_person = EntityNode(
        uuid='mentioned-marina',
        name='Marina',
        group_id=GROUP,
        labels=['Entity', 'Person'],
        created_at=NOW,
    )
    monkeypatch.setattr(
        graphiti_module,
        'extract_nodes',
        AsyncMock(return_value=[speaker_marker, addressee_marker, mentioned_person]),
    )
    monkeypatch.setattr(
        graphiti_module,
        'resolve_extracted_nodes',
        AsyncMock(return_value=([mentioned_person], {mentioned_person.uuid: mentioned_person.uuid}, [])),
    )

    captured = {}

    async def extract_edges(self, episode, extracted_nodes, *_args, **_kwargs):
        captured['extraction_content'] = episode.content
        captured['edge_nodes'] = extracted_nodes
        return (
            [
                EntityEdge(
                    uuid='relationship-edge',
                    # _extract_and_resolve_edges normally applies the bound UUID map
                    # before returning. This test double returns that normalized shape.
                    source_node_uuid=PARTICIPANT_UUID,
                    target_node_uuid=ADDRESSEE_UUID,
                    group_id=GROUP,
                    name='TEASED',
                    fact='Filomena teased Gabriel while mentioning Marina',
                    created_at=NOW,
                )
            ],
            [],
            [],
        )

    instance._extract_and_resolve_edges = MethodType(extract_edges, instance)
    monkeypatch.setattr(
        graphiti_module,
        'extract_attributes_from_nodes',
        AsyncMock(side_effect=lambda _clients, nodes, *_args, **_kwargs: nodes),
    )

    async def process(self, episode, nodes, edges, *_args, **_kwargs):
        captured['raw_content'] = episode.content
        captured['nodes'] = nodes
        captured['edges'] = edges
        return [], episode

    instance._process_episode_data = MethodType(process, instance)

    result = asyncio.run(
        instance.add_episode(
            name='outbound',
            episode_body='{"text":"Gabriel, a Marina comentou isso."}',
            source_description='test',
            reference_time=NOW,
            source=EpisodeType.json,
            group_id=GROUP,
            episode_uuid=EPISODE_UUID,
            bound_entities=[binding(), addressee_binding()],
            extraction_body='Gabriel, a Marina comentou isso.',
        )
    )

    assert result.episode.uuid == EPISODE_UUID
    assert captured['raw_content'] == '{"text":"Gabriel, a Marina comentou isso."}'
    assert '"role":"speaker"' in captured['extraction_content']
    assert '"role":"addressee"' in captured['extraction_content']
    assert [node.uuid for node in captured['edge_nodes']] == [
        'mentioned-marina',
        PARTICIPANT_UUID,
        ADDRESSEE_UUID,
    ]
    assert captured['edges'][0].source_node_uuid == PARTICIPANT_UUID
    assert captured['edges'][0].target_node_uuid == ADDRESSEE_UUID
    assert {node.uuid for node in captured['nodes']} == {
        'mentioned-marina',
        PARTICIPANT_UUID,
        ADDRESSEE_UUID,
    }


def test_existing_episode_uuid_short_circuits_all_extraction(monkeypatch) -> None:
    instance = graphiti()
    existing = EpisodicNode(
        uuid=EPISODE_UUID,
        name='existing',
        group_id=GROUP,
        labels=[],
        source=EpisodeType.json,
        source_description='test',
        content='raw',
        created_at=NOW,
        valid_at=NOW,
    )

    async def get_episode(_driver, _uuid):
        return existing

    monkeypatch.setattr(EpisodicNode, 'get_by_uuid', get_episode)
    extract_nodes = AsyncMock()
    monkeypatch.setattr(graphiti_module, 'extract_nodes', extract_nodes)

    result = asyncio.run(
        instance.add_episode(
            name='retry',
            episode_body='raw retry',
            source_description='test',
            reference_time=NOW,
            group_id=GROUP,
            episode_uuid=EPISODE_UUID,
            bound_entity=binding(),
        )
    )

    assert result.episode is existing
    extract_nodes.assert_not_awaited()


def test_binding_violation_preserves_raw_episode_without_entity_edges(monkeypatch) -> None:
    instance = graphiti()
    patch_missing_nodes(monkeypatch)
    marker = EntityNode(
        uuid='candidate-speaker',
        name=binding().name,
        group_id=GROUP,
        labels=['Entity', 'Person'],
        created_at=NOW,
    )
    monkeypatch.setattr(graphiti_module, 'extract_nodes', AsyncMock(return_value=[marker]))

    async def bad_edges(self, *_args, **_kwargs):
        return (
            [
                EntityEdge(
                    uuid='bad-fact',
                    source_node_uuid='candidate-speaker',
                    target_node_uuid='other',
                    group_id=GROUP,
                    name='BAD',
                    fact='bad binding',
                    created_at=NOW,
                )
            ],
            [],
            [],
        )

    instance._extract_and_resolve_edges = MethodType(bad_edges, instance)
    preserved = []

    async def save(episode, _driver):
        preserved.append(episode.model_copy(deep=True))

    monkeypatch.setattr(EpisodicNode, 'save', save)

    with pytest.raises(BoundEntityInvariantError, match='speaker candidate'):
        asyncio.run(
            instance.add_episode(
                name='bad',
                episode_body='raw must survive',
                source_description='test',
                reference_time=NOW,
                group_id=GROUP,
                episode_uuid=EPISODE_UUID,
                bound_entity=binding(),
            )
        )

    assert len(preserved) == 1
    assert preserved[0].content == 'raw must survive'
    assert preserved[0].entity_edges == []
