"""Opt-in evaluation of the deployed edge extractor; no database client or writes.

Input is JSON on stdin: policy plus cases. Entity candidates are controlled;
this measures edge extraction, not entity discovery/deduplication or ingestion.
"""
import asyncio
import json
import sys
import time

sys.path.insert(0, '/app/mcp/src')
from config.schema import GraphitiConfig
from services.factories import LLMClientFactory
from graphiti_core.prompts.extract_edges import edge, ExtractedEdges


async def main():
    payload = json.load(sys.stdin)
    config = GraphitiConfig()
    client = None if payload.get('renderOnly') else LLMClientFactory.create(config.llm)
    for case in payload['cases']:
        body = json.dumps({
            'schema': 'conversation-evidence:v2',
            'current_message': {'text': case['text']},
            'quoted_context': case.get('quote'),
        }, ensure_ascii=False)
        content = json.dumps({
            'bound_entities': [{'role': 'speaker', 'canonical_name': 'test-speaker',
                                'entity_type': 'Person'}],
            'content': body,
        }, ensure_ascii=False)
        context = {
            'episode_content': content,
            'nodes': [{'name': 'test-speaker', 'entity_types': ['Person']}] + [
                {'name': name, 'entity_types': ['Entity']} for name in case['entities']],
            'previous_episodes': [], 'reference_time': '2026-09-28T00:00:00Z',
            'edge_types': [], 'custom_extraction_instructions': payload['policy'],
        }
        if client is None:
            print(json.dumps({'id': case['id'], 'messages': [m.model_dump() for m in edge(context)],
                'schema': ExtractedEdges.model_json_schema()}, ensure_ascii=False), flush=True)
            continue
        start = time.monotonic()
        response = await client.generate_response(edge(context), response_model=ExtractedEdges,
            max_tokens=4096, group_id='offline-evaluation', prompt_name='extract_edges.edge')
        validated = ExtractedEdges(**response)
        print(json.dumps({'id': case['id'], 'model': config.llm.model,
            'seconds': time.monotonic() - start, 'result': validated.model_dump(),
            'databaseWrites': 0}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    asyncio.run(main())
