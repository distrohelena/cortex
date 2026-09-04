#!/usr/bin/env python3
"""Deterministic bound-entity integration test against a real FalkorDB."""

import asyncio
import json
import os
import sys
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import MethodType
from typing import Any

from pydantic import BaseModel

MCP_SRC = Path(__file__).parents[1] / 'mcp_server' / 'src'
sys.path.insert(0, str(MCP_SRC))

from services.queue_service import QueueService  # noqa: E402

import graphiti_core.graphiti as graphiti_module  # noqa: E402
from graphiti_core.cross_encoder.client import CrossEncoderClient  # noqa: E402
from graphiti_core.driver.falkordb_driver import FalkorDriver  # noqa: E402
from graphiti_core.edges import EntityEdge  # noqa: E402
from graphiti_core.embedder.client import EmbedderClient  # noqa: E402
from graphiti_core.graphiti import Graphiti  # noqa: E402
from graphiti_core.llm_client.client import LLMClient  # noqa: E402
from graphiti_core.llm_client.config import LLMConfig, ModelSize  # noqa: E402
from graphiti_core.nodes import EntityNode, EpisodeType  # noqa: E402
from graphiti_core.prompts.models import Message  # noqa: E402
from graphiti_core.utils.maintenance.bound_entities import (  # noqa: E402
    BoundEntity,
    BoundEntityInvariantError,
)

GROUP = 'filomena-bound-integration-v1'
SENDER_X = 'whatsapp:5511999999999@s.whatsapp.net'
SENDER_Y = 'whatsapp:5511888888888@s.whatsapp.net'
HELENA = 'whatsapp:5511981042361@s.whatsapp.net'
UUID_X = '10000000-0000-5000-8000-000000000001'
UUID_Y = '10000000-0000-5000-8000-000000000002'
UUID_HELENA = '10000000-0000-5000-8000-000000000003'


class Person(BaseModel):
    """A person participating in or mentioned by the conversation."""


class StableEmbedder(EmbedderClient):
    async def create(self, input_data) -> list[float]:
        text = str(input_data)
        seed = sum(text.encode('utf-8')) % 997
        return [((seed + index) % 101) / 100 for index in range(64)]

    async def create_batch(self, input_data_list: list[str]) -> list[list[float]]:
        return [await self.create(value) for value in input_data_list]


class StableCrossEncoder(CrossEncoderClient):
    async def rank(self, _query: str, passages: list[str]) -> list[tuple[str, float]]:
        return [(passage, 1.0) for passage in passages]


class StableLLM(LLMClient):
    def __init__(self):
        super().__init__(LLMConfig(model='deterministic-test', small_model='deterministic-test'))

    async def _generate_response(
        self,
        messages: list[Message],
        response_model: type[BaseModel] | None = None,
        max_tokens: int = 4096,
        model_size: ModelSize = ModelSize.medium,
    ) -> dict[str, Any]:
        del max_tokens, model_size
        model_name = response_model.__name__ if response_model is not None else ''
        prompt = '\n'.join(message.content for message in messages)
        case_tokens = [
            'CASE_X_SHADOW',
            'CASE_X_ARKHAM',
            'CASE_X_RETURN',
            'CASE_Y_SAME_NAME',
            'CASE_HELENA_MENTIONS_GABRIEL',
        ]
        current_case = max(case_tokens, key=prompt.rfind) if any(
            token in prompt for token in case_tokens
        ) else ''

        if model_name == 'ExtractedEntities':
            entities: list[dict[str, Any]] = []
            if current_case == 'CASE_X_SHADOW':
                entities = [
                    {'name': SENDER_X, 'entity_type_id': 1},
                    {'name': 'Shadow of Mordor', 'entity_type_id': 0},
                ]
            elif current_case == 'CASE_X_ARKHAM':
                entities = [
                    {'name': SENDER_X, 'entity_type_id': 1},
                    {'name': 'Arkham Knight', 'entity_type_id': 0},
                ]
            elif current_case == 'CASE_X_RETURN':
                entities = [{'name': SENDER_X, 'entity_type_id': 1}]
            elif current_case == 'CASE_Y_SAME_NAME':
                entities = [
                    {'name': SENDER_Y, 'entity_type_id': 1},
                    {'name': 'Shadow of Mordor', 'entity_type_id': 0},
                ]
            elif current_case == 'CASE_HELENA_MENTIONS_GABRIEL':
                entities = [
                    {'name': HELENA, 'entity_type_id': 1},
                    {'name': 'Gabriel', 'entity_type_id': 1},
                    {'name': 'Shadow of Mordor', 'entity_type_id': 0},
                ]
            return {'extracted_entities': entities}

        if model_name == 'ExtractedEdges':
            if current_case == 'CASE_X_SHADOW':
                return {'edges': [{
                    'source_entity_name': SENDER_X,
                    'target_entity_name': 'Shadow of Mordor',
                    'relation_type': 'LIKES',
                    'fact': 'Gabriel likes Shadow of Mordor',
                    'valid_at': None,
                    'invalid_at': None,
                }]}
            if current_case == 'CASE_X_ARKHAM':
                return {'edges': [{
                    'source_entity_name': SENDER_X,
                    'target_entity_name': 'Arkham Knight',
                    'relation_type': 'STARTED',
                    'fact': 'Gabs started Arkham Knight',
                    'valid_at': None,
                    'invalid_at': None,
                }]}
            if current_case == 'CASE_Y_SAME_NAME':
                return {'edges': [{
                    'source_entity_name': SENDER_Y,
                    'target_entity_name': 'Shadow of Mordor',
                    'relation_type': 'LIKES',
                    'fact': 'The other Gabriel likes Shadow of Mordor',
                    'valid_at': None,
                    'invalid_at': None,
                }]}
            if current_case == 'CASE_HELENA_MENTIONS_GABRIEL':
                return {'edges': [{
                    'source_entity_name': 'Gabriel',
                    'target_entity_name': 'Shadow of Mordor',
                    'relation_type': 'FINISHED',
                    'fact': 'Gabriel finished Shadow of Mordor',
                    'valid_at': None,
                    'invalid_at': None,
                }]}
            return {'edges': []}

        if model_name == 'NodeResolutions':
            return {'entity_resolutions': []}
        if model_name == 'EdgeDuplicate':
            return {'duplicate_facts': [], 'contradicted_facts': []}
        if model_name == 'SummarizedEntities':
            return {'summaries': []}
        if model_name == 'EntitySummary':
            return {'summary': ''}
        return {}


def bound(uuid: str, stable_id: str, display_name: str) -> BoundEntity:
    return BoundEntity(
        uuid=uuid,
        role='speaker',
        name=stable_id,
        entity_type='Person',
        labels=['Person'],
        attributes={
            'stable_id': stable_id,
            'display_name_current': display_name,
            'aliases': [display_name],
        },
    )


async def records(driver: FalkorDriver, query: str, **params) -> list[dict[str, Any]]:
    result, _, _ = await driver.execute_query(query, **params)
    return result


async def main() -> None:
    host = os.getenv('FALKORDB_HOST', 'filomena-falkordb')
    port = int(os.getenv('FALKORDB_PORT', '6379'))
    driver = FalkorDriver(host=host, port=port, database=GROUP)
    await driver.execute_query('MATCH (n) DETACH DELETE n')
    graphiti = Graphiti(
        graph_driver=driver,
        llm_client=StableLLM(),
        embedder=StableEmbedder(),
        cross_encoder=StableCrossEncoder(),
        max_coroutines=1,
    )
    await graphiti.build_indices_and_constraints()
    queue = QueueService()
    await queue.initialize(graphiti)
    start = datetime(2026, 9, 4, 12, 0, tzinfo=UTC)

    cases = [
        ('20000000-0000-5000-8000-000000000001', 'CASE_X_SHADOW eu gosto de Shadow of Mordor', bound(UUID_X, SENDER_X, 'Gabriel')),
        ('20000000-0000-5000-8000-000000000002', 'CASE_X_ARKHAM agora comecei Arkham Knight', bound(UUID_X, SENDER_X, 'Gabs')),
        ('20000000-0000-5000-8000-000000000003', 'CASE_X_RETURN voltei', bound(UUID_X, SENDER_X, 'Gabriel')),
        ('20000000-0000-5000-8000-000000000004', 'CASE_Y_SAME_NAME eu tambem gosto de Shadow of Mordor', bound(UUID_Y, SENDER_Y, 'Gabriel')),
        ('20000000-0000-5000-8000-000000000005', 'CASE_HELENA_MENTIONS_GABRIEL Gabriel disse que terminou Shadow of Mordor', bound(UUID_HELENA, HELENA, 'Helena')),
    ]

    # Submit the first two simultaneously. QueueService must serialize the group.
    async def submit(index: int) -> None:
        episode_uuid, content, entity = cases[index]
        await queue.add_episode(
            group_id=GROUP,
            name=f'inbound_{episode_uuid}',
            content=json.dumps({'text': content}),
            source_description='bound integration test',
            episode_type=EpisodeType.json,
            entity_types={'Person': Person},
            uuid=None,
            episode_uuid=episode_uuid,
            reference_time=start + timedelta(seconds=index),
            bound_entity=entity,
            extraction_body=content,
        )

    await asyncio.gather(submit(0), submit(1))
    for index in range(2, len(cases)):
        await submit(index)
    await queue._episode_queues[GROUP].join()

    # Retry the same provider message/episode UUID; it must short-circuit.
    await submit(0)
    await queue._episode_queues[GROUP].join()

    # Force an unnormalized speaker edge after extraction. The raw episode must
    # survive, while the ambiguous entity edge must never be persisted.
    bad_episode_uuid = '20000000-0000-5000-8000-000000000006'
    bad_candidate_uuid = '10000000-0000-5000-8000-000000000099'
    original_extract_nodes = graphiti_module.extract_nodes
    original_extract_edges = graphiti._extract_and_resolve_edges

    async def extract_bad_speaker(*_args, **_kwargs):
        return [
            EntityNode(
                uuid=bad_candidate_uuid,
                name=SENDER_X,
                group_id=GROUP,
                labels=['Entity', 'Person'],
                created_at=start + timedelta(seconds=10),
            )
        ]

    async def return_unnormalized_edge(self, *_args, **_kwargs):
        del self
        return (
            [
                EntityEdge(
                    uuid='10000000-0000-5000-8000-000000000098',
                    source_node_uuid=bad_candidate_uuid,
                    target_node_uuid=UUID_X,
                    group_id=GROUP,
                    name='AMBIGUOUS',
                    fact='artificial binding violation',
                    created_at=start + timedelta(seconds=10),
                )
            ],
            [],
            [],
        )

    graphiti_module.extract_nodes = extract_bad_speaker
    graphiti._extract_and_resolve_edges = MethodType(return_unnormalized_edge, graphiti)
    try:
        try:
            await graphiti.add_episode(
                name=f'inbound_{bad_episode_uuid}',
                episode_body=json.dumps({'text': 'raw fail-closed probe'}),
                source_description='bound integration fail-closed test',
                reference_time=start + timedelta(seconds=10),
                source=EpisodeType.json,
                group_id=GROUP,
                episode_uuid=bad_episode_uuid,
                bound_entity=bound(UUID_X, SENDER_X, 'Gabriel'),
                extraction_body='raw fail-closed probe',
                entity_types={'Person': Person},
            )
        except BoundEntityInvariantError:
            pass
        else:
            raise AssertionError('artificial speaker binding violation did not fail closed')
    finally:
        graphiti_module.extract_nodes = original_extract_nodes
        graphiti._extract_and_resolve_edges = original_extract_edges

    canonical_people = await records(
        graphiti.driver,
        """
        MATCH (p:Person)
        WHERE p.stable_id STARTS WITH 'whatsapp:'
        RETURN p.uuid AS uuid, p.stable_id AS stable_id,
               p.display_name_current AS display_name_current, p.aliases AS aliases
        ORDER BY p.uuid
        """,
    )
    episodes = await records(
        graphiti.driver,
        "MATCH (e:Episodic) RETURN e.uuid AS uuid, e.valid_at AS valid_at ORDER BY e.uuid",
    )
    facts = await records(
        graphiti.driver,
        """
        MATCH (s:Entity)-[r:RELATES_TO]->(t:Entity)
        WHERE r.fact IS NOT NULL
        RETURN s.uuid AS source_uuid, s.name AS source_name,
               t.uuid AS target_uuid, t.name AS target_name, r.fact AS fact
        ORDER BY r.fact
        """,
    )
    failed_episode = await records(
        graphiti.driver,
        """
        MATCH (e:Episodic {uuid: $episode_uuid})
        OPTIONAL MATCH (e)-[m:MENTIONS]->(:Entity)
        RETURN e.content AS content, count(m) AS mention_count
        """,
        episode_uuid=bad_episode_uuid,
    )
    bad_facts = await records(
        graphiti.driver,
        "MATCH (:Entity)-[r:RELATES_TO]->(:Entity) WHERE r.fact = $fact RETURN count(r) AS count",
        fact='artificial binding violation',
    )

    by_uuid = {person['uuid']: person for person in canonical_people}
    assert set(by_uuid) == {UUID_X, UUID_Y, UUID_HELENA}, canonical_people
    assert by_uuid[UUID_X]['aliases'] == ['Gabriel', 'Gabs'], by_uuid[UUID_X]
    assert by_uuid[UUID_X]['display_name_current'] == 'Gabriel', by_uuid[UUID_X]
    assert len([person for person in canonical_people if person['display_name_current'] == 'Gabriel']) == 2
    assert len(episodes) == len(cases) + 1, episodes
    assert episodes[0]['valid_at'] == start.isoformat(), episodes[0]
    assert failed_episode == [
        {'content': json.dumps({'text': 'raw fail-closed probe'}), 'mention_count': 0}
    ], failed_episode
    assert bad_facts == [{'count': 0}], bad_facts

    x_facts = [fact for fact in facts if fact['source_uuid'] == UUID_X]
    assert {fact['target_name'] for fact in x_facts} >= {'Shadow of Mordor', 'Arkham Knight'}, x_facts
    mention_fact = next(fact for fact in facts if fact['fact'] == 'Gabriel finished Shadow of Mordor')
    assert mention_fact['source_name'] == 'Gabriel', mention_fact
    assert mention_fact['source_uuid'] != UUID_HELENA, mention_fact

    print(json.dumps({
        'group_id': GROUP,
        'canonical_people': canonical_people,
        'normal_episode_count': len(cases),
        'fail_closed_raw_episode_count': len(failed_episode),
        'fail_closed_bad_fact_count': bad_facts[0]['count'],
        'facts': facts,
        'concurrent_same_jid_person_count': len([p for p in canonical_people if p['uuid'] == UUID_X]),
        'retry_normal_episode_count': len(cases),
    }, ensure_ascii=False, indent=2, default=str))

    await driver.execute_query('MATCH (n) DETACH DELETE n')
    await driver.close()


if __name__ == '__main__':
    asyncio.run(main())
