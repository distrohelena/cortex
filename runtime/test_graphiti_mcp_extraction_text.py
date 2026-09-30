"""Offline regression using the installed FastMCP argument validation path."""
import sys
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

sys.path.insert(0, '/app/mcp/src')
import graphiti_mcp_server as server
from mcp.server.fastmcp.utilities.func_metadata import func_metadata
from pydantic import ValidationError


class ExtractionTextContract(unittest.IsolatedAsyncioTestCase):
    async def call(self, body):
        queued = AsyncMock(return_value=1)
        metadata = func_metadata(server.add_memory)
        args = dict(name='synthetic', episode_body='{"raw":"unchanged"}',
                    group_id='offline-test', extraction_body=body)
        with patch.object(server, 'graphiti_service', SimpleNamespace(entity_types={})), \
                patch.object(server, 'queue_service', SimpleNamespace(add_episode=queued)):
            result = await metadata.call_fn_with_arg_validation(server.add_memory, True, args, {})
        return queued, result

    async def test_structured_extraction_text_reaches_queue_byte_for_byte(self):
        body = '{ "schema": "conversation-evidence:v2", "current_message": {"text":"café"}, "quoted_context": null }'
        queued, _ = await self.call(body)
        self.assertEqual(queued.call_args.kwargs['extraction_body'], body)
        self.assertEqual(queued.call_args.kwargs['content'], '{"raw":"unchanged"}')

    async def test_nullable_plain_and_json_looking_text_preserve_existing_contract(self):
        for body in [None, '', 'plain text', '[1,2]', 'null', 'true', '123', '"text"']:
            with self.subTest(body=body):
                queued, _ = await self.call(body)
                self.assertEqual(queued.call_args.kwargs['extraction_body'], body)

    async def test_objects_and_arrays_are_rejected_not_silently_reserialized(self):
        for body in [{}, [], 123, True]:
            with self.subTest(body=body), self.assertRaises(ValidationError):
                await self.call(body)

    async def test_wire_schema_stays_nullable_text_and_omission_remains_valid(self):
        metadata = func_metadata(server.add_memory)
        schema = metadata.arg_model.model_json_schema()['properties']['extraction_body']
        self.assertEqual(schema['anyOf'], [{'type': 'string'}, {'type': 'null'}])
        self.assertIsNone(metadata.arg_model.model_validate({'name': 'test', 'episode_body': '{}'}).extraction_body)


if __name__ == '__main__':
    unittest.main()
