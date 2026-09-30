"""Read payloads from stdin; validate only, never call the tool or echo content."""
import json
import sys

sys.path.insert(0, '/app/mcp/src')
import graphiti_mcp_server as server
from mcp.server.fastmcp.utilities.func_metadata import func_metadata
from pydantic import ValidationError

metadata = func_metadata(server.add_memory)
results = []
for item in json.load(sys.stdin):
    args = {'name': 'validation-only', 'episode_body': '{}', 'extraction_body': item['body']}
    try:
        parsed = metadata.arg_model.model_validate(metadata.pre_parse_json(args))
        results.append({'ref': item['ref'], 'valid': True,
                        'textPreserved': parsed.extraction_body == item['body']})
    except ValidationError as error:
        results.append({'ref': item['ref'], 'valid': False,
                        'errors': [{'field': list(e['loc']), 'type': e['type']}
                                   for e in error.errors(include_input=False, include_url=False)]})
print(json.dumps(results))
