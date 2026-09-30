"""Explicit live acceptance; synthetic namespaces only, no transport or production data."""
import asyncio
import json
import os
import sys
import time
from dataclasses import asdict
from datetime import datetime, timezone
from uuid import uuid4

sys.path.insert(0, '/app/mcp/src')
from pydantic import BaseModel
from config.schema import LLMConfig, LLMProvidersConfig, OpenAIProviderConfig
from services.factories import LLMClientFactory, NoopReranker
from graphiti_core import Graphiti
from graphiti_core.driver.falkordb_driver import FalkorDriver
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.nodes import EpisodeType, EpisodicNode
from graphiti_core.utils.maintenance.bound_entities import BoundEntity


async def main():
    with open('/evaluation/memory-input.json', encoding='utf-8') as file:
        data = json.load(file)
    run = 'eval-memory-luna-' + str(uuid4())
    config = LLMConfig(provider='openai', model='gpt-6-luna', reasoning_effort='medium',
        max_tokens=4096, providers=LLMProvidersConfig(
            openai=OpenAIProviderConfig(api_key=os.environ['OPENAI_API_KEY'])))
    llm = LLMClientFactory.create(config)
    embedder = OpenAIEmbedder(OpenAIEmbedderConfig(
        embedding_model='text-embedding-nomic-embed-text-v2-moe', embedding_dim=768,
        api_key='lm-studio', base_url='http://host.docker.internal:1234/v1'))
    types = {t['name']: type(t['name'], (BaseModel,), {'__doc__': t['description']})
             for t in data['entity_types']}
    for case in data['cases']:
        namespace = run + '-' + case['id']
        assert namespace.startswith('eval-memory-luna-')
        driver = FalkorDriver(host='falkordb', port=6379, database=namespace)
        graph = Graphiti(graph_driver=driver, llm_client=llm, embedder=embedder,
                         cross_encoder=NoopReranker(), max_coroutines=1)
        await graph.build_indices_and_constraints()
        speaker = BoundEntity(uuid=str(uuid4()), role='speaker', name='synthetic-speaker',
            entity_type='Person', labels=['Person'], attributes={
                'stable_id':'synthetic-speaker','display_name_current':'Test speaker',
                'aliases':[], 'identity_source':'controlled_fixture'})
        body = json.dumps({'schema':'conversation-evidence:v2',
            'current_message':{'text':case['text']}, 'quoted_context':case.get('quote')},
            ensure_ascii=False)
        llm.token_tracker.reset()
        start = time.monotonic()
        try:
            result = await graph.add_episode(name=case['id'], episode_body=body,
                extraction_body=body, source=EpisodeType.json,
                source_description='Controlled memory acceptance, not a real conversation',
                reference_time=datetime.now(timezone.utc), group_id=namespace,
                previous_episode_uuids=[], entity_types=types, bound_entities=[speaker],
                custom_extraction_instructions=data['policy'])
            saved = await EpisodicNode.get_by_uuid(driver, result.episode.uuid)
            assert saved.content == body and saved.group_id == namespace
            assert all(n.group_id == namespace for n in result.nodes)
            assert all(e.group_id == namespace for e in result.edges)
            record = {'id':case['id'], 'namespace':namespace, 'status':'ingested',
                'seconds':time.monotonic()-start, 'episode':saved.uuid,
                'speaker':speaker.uuid,
                'nodes':[{'uuid':n.uuid,'name':n.name,'summary':n.summary} for n in result.nodes],
                'edges':[{'source':e.source_node_uuid,'target':e.target_node_uuid,
                          'name':e.name,'fact':e.fact} for e in result.edges],
                'usage':{k:asdict(v) for k,v in llm.token_tracker.get_usage().items()},
                'cost':'unknown; no verified tariff applied'}
            print(json.dumps(record, ensure_ascii=False), flush=True)
        except Exception as exc:
            print(json.dumps({'id':case['id'],'namespace':namespace,'status':'failed',
                'error_type':type(exc).__name__, 'seconds':time.monotonic()-start}), flush=True)
            raise
        finally:
            await driver.close()
    await llm.client.close()
    await embedder.client.close()


asyncio.run(main())
