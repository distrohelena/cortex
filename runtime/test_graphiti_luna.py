"""Offline contract tests. Execute with the candidate Graphiti image's Python."""
import sys
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
from datetime import datetime, timezone

sys.path.insert(0, '/app/mcp/src')
from pydantic import BaseModel
from config.schema import LLMConfig, LLMProvidersConfig, OpenAIProviderConfig
from services.factories import LLMClientFactory
from graphiti_core.llm_client.openai_client import OpenAIClient
from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.nodes import EntityNode, EpisodicNode, EpisodeType
from graphiti_core.utils.maintenance.node_operations import extract_attributes_from_nodes


class Answer(BaseModel):
    value: str


class LunaContracts(unittest.IsolatedAsyncioTestCase):
    async def test_luna_structured_request_uses_reasoning_and_does_not_store(self):
        parse = AsyncMock(return_value=object())
        client = OpenAIClient(client=SimpleNamespace(responses=SimpleNamespace(parse=parse)))
        await client._create_structured_completion('gpt-6-luna', [], 0.7, 4096,
                                                   Answer, reasoning='medium')
        request = parse.call_args.kwargs
        self.assertEqual(request.get('reasoning'), {'effort': 'medium'})
        self.assertNotIn('temperature', request)
        self.assertIs(request.get('store'), False)

    async def test_explicit_profile_reaches_both_model_sizes(self):
        cfg = LLMConfig(provider='openai', model='gpt-6-luna', reasoning_effort='medium',
            providers=LLMProvidersConfig(openai=OpenAIProviderConfig(api_key='synthetic')))
        client = LLMClientFactory.create(cfg)
        self.assertEqual(client.reasoning, 'medium')
        self.assertEqual(client.model, 'gpt-6-luna')
        self.assertEqual(client.small_model, 'gpt-6-luna')
        await client.client.close()

    async def test_generic_local_provider_is_not_replaced(self):
        cfg = LLMConfig(provider='openai_generic', model='local-test',
            providers=LLMProvidersConfig(openai=OpenAIProviderConfig(api_key='synthetic',
                api_url='http://127.0.0.1:1/v1')))
        client = LLMClientFactory.create(cfg)
        self.assertIsInstance(client, OpenAIGenericClient)
        self.assertEqual(client.model, 'local-test')
        await client.client.close()

    async def test_remote_failure_propagates_without_local_fallback(self):
        parse = AsyncMock(side_effect=RuntimeError('synthetic failure'))
        client = OpenAIClient(client=SimpleNamespace(responses=SimpleNamespace(parse=parse)))
        with self.assertRaisesRegex(RuntimeError, 'synthetic failure'):
            await client._create_structured_completion('gpt-6-luna', [], None, 4096,
                                                       Answer, reasoning='medium')

    async def test_service_rejects_configured_provider_failure_before_database(self):
        from graphiti_mcp_server import GraphitiService
        from config.schema import GraphitiConfig
        service = GraphitiService(GraphitiConfig())
        with patch('graphiti_mcp_server.LLMClientFactory.create',
                   side_effect=ValueError('synthetic invalid provider')), \
             patch('graphiti_mcp_server.DatabaseDriverFactory.create_config') as database:
            with self.assertRaisesRegex(ValueError, 'synthetic invalid provider'):
                await service.initialize()
            database.assert_not_called()
            self.assertIsNone(service.client)

    async def test_policy_reaches_summary_request_through_attribute_pipeline(self):
        now = datetime.now(timezone.utc)
        node = EntityNode(name='test-speaker', group_id='test-only', labels=['Entity','Person'])
        episode = EpisodicNode(name='test', group_id='test-only', source=EpisodeType.json,
            source_description='synthetic', content='{}', valid_at=now)
        calls = []
        async def generate(messages, **kwargs):
            calls.append((messages, kwargs))
            return {'summaries': [{'name': 'test-speaker', 'summary': 'Asked a question.'}]}
        clients = SimpleNamespace(llm_client=SimpleNamespace(generate_response=generate),
                                  embedder=object())
        with patch('graphiti_core.utils.maintenance.node_operations.create_entity_node_embeddings',
                   new=AsyncMock()):
            result = await extract_attributes_from_nodes(clients, [node], episode, [],
                custom_extraction_instructions='TEST_POLICY: preserve question attribution')
        self.assertEqual(result[0].summary, 'Asked a question.')
        self.assertEqual(len(calls), 1)
        self.assertIn('TEST_POLICY: preserve question attribution',
                      '\n'.join(message.content for message in calls[0][0]))


if __name__ == '__main__':
    unittest.main()
