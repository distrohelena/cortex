"""Deterministic entity bindings for provenance-aware episode ingestion.

This module is provider-neutral. Callers translate provider identities into a
canonical UUID/name and Graphiti guarantees that the bound role is persisted
through that canonical entity rather than an LLM-selected identity.
"""

import json
from dataclasses import dataclass
from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field

from graphiti_core.edges import EntityEdge
from graphiti_core.nodes import EntityNode


class BoundEntityInvariantError(RuntimeError):
    """Raised before entity facts are persisted when a binding is ambiguous."""


class BoundEntity(BaseModel):
    uuid: str
    role: Literal['speaker', 'addressee']
    name: str
    entity_type: str = 'Entity'
    labels: list[str] = Field(default_factory=list)
    attributes: dict[str, Any] = Field(default_factory=dict)


@dataclass(frozen=True)
class BoundCandidateResult:
    nodes_for_resolution: list[EntityNode]
    nodes_for_edges: list[EntityNode]
    uuid_map: dict[str, str]
    bound_candidate_uuids: set[str]


def validate_bound_entity_bindings(bindings: list[BoundEntity]) -> None:
    """Reject structurally ambiguous actor definitions before extraction."""
    roles: set[str] = set()
    names: dict[str, str] = {}
    uuids: dict[str, str] = {}
    for binding in bindings:
        if binding.role in roles:
            raise BoundEntityInvariantError(f'bound actor role {binding.role} is duplicated')
        roles.add(binding.role)
        if binding.name in names and names[binding.name] != binding.uuid:
            raise BoundEntityInvariantError('bound actors share a canonical name')
        if binding.uuid in uuids and uuids[binding.uuid] != binding.name:
            raise BoundEntityInvariantError('bound actor UUID has multiple canonical names')
        names[binding.name] = binding.uuid
        uuids[binding.uuid] = binding.name


def _aliases(attributes: dict[str, Any]) -> list[str]:
    value = attributes.get('aliases', [])
    if not isinstance(value, list):
        raise BoundEntityInvariantError('bound entity aliases must be a list')
    return [alias.strip() for alias in value if isinstance(alias, str) and alias.strip()]


def _alias_union(existing: dict[str, Any], incoming: dict[str, Any]) -> list[str]:
    aliases = {*_aliases(existing), *_aliases(incoming)}
    return sorted(aliases, key=lambda value: (value.casefold(), value))


def build_bound_entity_node(
    binding: BoundEntity,
    group_id: str,
    now: datetime,
    existing: EntityNode | None = None,
) -> EntityNode:
    """Create or update a canonical entity without probabilistic resolution."""
    labels = sorted({'Entity', binding.entity_type, *binding.labels})
    attributes = dict(existing.attributes) if existing is not None else {}

    if existing is not None:
        if existing.uuid != binding.uuid:
            raise BoundEntityInvariantError('existing entity UUID differs from canonical UUID')
        if existing.group_id != group_id:
            raise BoundEntityInvariantError('canonical UUID already belongs to another group')
        if existing.name != binding.name:
            raise BoundEntityInvariantError('canonical UUID already has a different canonical name')
        if binding.entity_type not in existing.labels:
            raise BoundEntityInvariantError('canonical UUID already has a different entity type')

    stable_id = binding.attributes.get('stable_id')
    existing_stable_id = attributes.get('stable_id')
    if existing_stable_id and stable_id and existing_stable_id != stable_id:
        raise BoundEntityInvariantError('canonical UUID already has a different stable_id')

    aliases = _alias_union(attributes, binding.attributes)
    incoming_attributes = dict(binding.attributes)
    incoming_display = incoming_attributes.get('display_name_current')
    if not isinstance(incoming_display, str) or not incoming_display.strip():
        incoming_attributes.pop('display_name_current', None)
    attributes.update(incoming_attributes)
    attributes['aliases'] = aliases

    return EntityNode(
        uuid=binding.uuid,
        name=binding.name,
        group_id=group_id,
        labels=sorted(set(labels) | (set(existing.labels) if existing is not None else set())),
        summary=existing.summary if existing is not None else '',
        attributes=attributes,
        created_at=existing.created_at if existing is not None else now,
        name_embedding=existing.name_embedding if existing is not None else None,
    )


def bind_entity_candidates(
    extracted_nodes: list[EntityNode],
    bound_nodes: list[EntityNode],
) -> BoundCandidateResult:
    """Replace only explicit canonical actor markers with their bound UUIDs.

    Display names and aliases deliberately do not participate. A mentioned
    person named like a bound actor therefore remains an ordinary extracted node.
    """
    names_to_nodes: dict[str, EntityNode] = {}
    uuids_to_names: dict[str, str] = {}
    for bound_node in bound_nodes:
        existing_by_name = names_to_nodes.get(bound_node.name)
        if existing_by_name is not None and existing_by_name.uuid != bound_node.uuid:
            raise BoundEntityInvariantError('bound actors share a canonical name')
        existing_name = uuids_to_names.get(bound_node.uuid)
        if existing_name is not None and existing_name != bound_node.name:
            raise BoundEntityInvariantError('bound actor UUID has multiple canonical names')
        names_to_nodes[bound_node.name] = bound_node
        uuids_to_names[bound_node.uuid] = bound_node.name

    nodes_for_resolution: list[EntityNode] = []
    uuid_map = {bound_node.uuid: bound_node.uuid for bound_node in bound_nodes}
    bound_candidates: set[str] = set()

    for candidate in extracted_nodes:
        bound_node = names_to_nodes.get(candidate.name)
        if bound_node is not None:
            if candidate.group_id != bound_node.group_id:
                raise BoundEntityInvariantError('bound actor candidate belongs to another group')
            bound_candidates.add(candidate.uuid)
            uuid_map[candidate.uuid] = bound_node.uuid
        else:
            nodes_for_resolution.append(candidate)

    return BoundCandidateResult(
        nodes_for_resolution=nodes_for_resolution,
        nodes_for_edges=[*nodes_for_resolution, *bound_nodes],
        uuid_map=uuid_map,
        bound_candidate_uuids=bound_candidates,
    )


def normalize_bound_edge_pointers(
    edges: list[EntityEdge],
    speaker_uuid_map: dict[str, str],
) -> None:
    """Rewrite speaker endpoints before edge resolution or persistence."""
    for edge in edges:
        edge.source_node_uuid = speaker_uuid_map.get(edge.source_node_uuid, edge.source_node_uuid)
        edge.target_node_uuid = speaker_uuid_map.get(edge.target_node_uuid, edge.target_node_uuid)


def validate_bound_entity_persistence(
    nodes: list[EntityNode],
    edges: list[EntityEdge],
    bound_nodes: list[EntityNode],
    bound_candidate_uuids: set[str],
) -> None:
    """Fail closed if any canonical bound actor invariant is false pre-write."""
    for bound_node in bound_nodes:
        canonical = [node for node in nodes if node.uuid == bound_node.uuid]
        if len(canonical) != 1:
            raise BoundEntityInvariantError(
                'bound actor must resolve to exactly one canonical entity'
            )
        if canonical[0].name != bound_node.name or canonical[0].group_id != bound_node.group_id:
            raise BoundEntityInvariantError('bound actor canonical identity changed during extraction')

        conflicting_names = [
            node.uuid
            for node in nodes
            if node.name == bound_node.name and node.uuid != bound_node.uuid
        ]
        if conflicting_names:
            raise BoundEntityInvariantError('another entity retained a bound actor canonical name')

    for edge in edges:
        if (
            edge.source_node_uuid in bound_candidate_uuids
            or edge.target_node_uuid in bound_candidate_uuids
        ):
            raise BoundEntityInvariantError(
                'edge still points at an unnormalized speaker candidate or other bound actor candidate'
            )


def render_bound_extraction_content(content: str, bindings: list[BoundEntity]) -> str:
    """Render provider-neutral structural actor markers for the extractor."""
    return json.dumps(
        {
            'bound_entities': [
                {
                    'role': binding.role,
                    'canonical_name': binding.name,
                    'entity_type': binding.entity_type,
                }
                for binding in bindings
            ],
            'content': content,
        },
        ensure_ascii=False,
        separators=(',', ':'),
    )
