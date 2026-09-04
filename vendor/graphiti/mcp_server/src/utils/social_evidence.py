"""Small deterministic social-evidence primitives independent of LLM extraction."""

from datetime import datetime
from uuid import UUID, uuid5

from graphiti_core.edges import EntityEdge

ADDRESS_TERM_NAMESPACE_V1 = UUID('7696cbf9-940c-56a1-a39e-47e7ad3dde51')


class AddressTermInvariantError(ValueError):
    """Raised when an existing deterministic edge conflicts with its canonical key."""


def address_term_edge_uuid(
    group_id: str,
    source_uuid: str,
    target_uuid: str,
    normalized_term: str,
) -> str:
    key = '\0'.join((group_id, source_uuid, target_uuid, normalized_term))
    return str(uuid5(ADDRESS_TERM_NAMESPACE_V1, key))


def merge_address_term_observation(
    *,
    existing: EntityEdge | None,
    group_id: str,
    source_uuid: str,
    target_uuid: str,
    source_name: str,
    target_name: str,
    term: str,
    normalized_term: str,
    run_id: str,
    episode_uuid: str,
    reference_time: datetime,
) -> EntityEdge:
    """Create/update one idempotent edge while counting distinct runs, not chunks."""
    edge_uuid = address_term_edge_uuid(group_id, source_uuid, target_uuid, normalized_term)
    if existing is not None:
        expected = (
            existing.uuid == edge_uuid
            and existing.group_id == group_id
            and existing.source_node_uuid == source_uuid
            and existing.target_node_uuid == target_uuid
            and existing.name == 'USED_ADDRESS_TERM'
            and existing.attributes.get('normalized_term') == normalized_term
        )
        if not expected:
            raise AddressTermInvariantError(
                f'address-term edge {edge_uuid} conflicts with existing canonical endpoints'
            )

    run_ids = set(existing.attributes.get('evidence_run_ids', []) if existing else [])
    episode_uuids = set(
        existing.attributes.get('evidence_episode_uuids', []) if existing else []
    )
    run_ids.add(run_id)
    episode_uuids.add(episode_uuid)
    display_term = existing.attributes.get('display_term', term) if existing else term
    created_at = existing.created_at if existing else reference_time
    valid_at = existing.valid_at if existing else reference_time

    return EntityEdge(
        uuid=edge_uuid,
        source_node_uuid=source_uuid,
        target_node_uuid=target_uuid,
        group_id=group_id,
        name='USED_ADDRESS_TERM',
        fact=f'{source_name} used address term "{display_term}" for {target_name}.',
        fact_embedding=existing.fact_embedding if existing else None,
        episodes=sorted(set(existing.episodes if existing else []) | {episode_uuid}),
        created_at=created_at,
        valid_at=valid_at,
        invalid_at=existing.invalid_at if existing else None,
        expired_at=existing.expired_at if existing else None,
        attributes={
            **(existing.attributes if existing else {}),
            'evidence_kind': 'address_term',
            'display_term': display_term,
            'normalized_term': normalized_term,
            'evidence_run_ids': sorted(run_ids),
            'evidence_episode_uuids': sorted(episode_uuids),
        },
    )
