from datetime import UTC, datetime

import pytest

from graphiti_core.edges import EntityEdge
from graphiti_core.nodes import EntityNode
from graphiti_core.utils.maintenance.bound_entities import (
    BoundEntity,
    BoundEntityInvariantError,
    bind_entity_candidates,
    build_bound_entity_node,
    normalize_bound_edge_pointers,
    validate_bound_entity_persistence,
)

NOW = datetime(2026, 9, 4, tzinfo=UTC)
GROUP = 'whatsapp-group-a-g-us'
HELENA_UUID = '11111111-1111-5111-8111-111111111111'
GABRIEL_UUID = '22222222-2222-5222-8222-222222222222'


def speaker(display_name: str, aliases: list[str]) -> BoundEntity:
    return BoundEntity(
        uuid=HELENA_UUID,
        role='speaker',
        name='whatsapp:5511981042361@s.whatsapp.net',
        entity_type='Person',
        labels=['Person'],
        attributes={
            'stable_id': 'whatsapp:5511981042361@s.whatsapp.net',
            'display_name_current': display_name,
            'aliases': aliases,
        },
    )


def addressee() -> BoundEntity:
    return BoundEntity(
        uuid=GABRIEL_UUID,
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


def node(uuid: str, name: str, labels: list[str] | None = None) -> EntityNode:
    return EntityNode(
        uuid=uuid,
        name=name,
        group_id=GROUP,
        labels=labels or ['Entity', 'Person'],
        created_at=NOW,
        summary='',
    )


def test_display_name_changes_merge_aliases_as_an_idempotent_set() -> None:
    first = build_bound_entity_node(speaker('Gabriel', ['Gabriel']), GROUP, NOW)
    second = build_bound_entity_node(speaker('Gabs', ['Gabs']), GROUP, NOW, existing=first)
    third = build_bound_entity_node(speaker('Gabriel', ['Gabriel']), GROUP, NOW, existing=second)

    assert first.uuid == second.uuid == third.uuid == HELENA_UUID
    assert third.name == 'whatsapp:5511981042361@s.whatsapp.net'
    assert third.attributes['display_name_current'] == 'Gabriel'
    assert third.attributes['aliases'] == ['Gabriel', 'Gabs']


def test_binding_uses_structural_canonical_marker_not_display_name() -> None:
    bound = build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW)
    marker_candidate = node('candidate-speaker', bound.name)
    mentioned_gabriel = node('mentioned-gabriel', 'Gabriel')

    result = bind_entity_candidates([marker_candidate, mentioned_gabriel], [bound])

    assert result.uuid_map == {'candidate-speaker': HELENA_UUID, HELENA_UUID: HELENA_UUID}
    assert [item.uuid for item in result.nodes_for_resolution] == ['mentioned-gabriel']
    assert [item.uuid for item in result.nodes_for_edges] == ['mentioned-gabriel', HELENA_UUID]


def test_edges_are_repointed_to_canonical_speaker_before_validation() -> None:
    bound = build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW)
    extracted = EntityEdge(
        uuid='edge-1',
        source_node_uuid='candidate-speaker',
        target_node_uuid='game',
        group_id=GROUP,
        name='LIKES',
        fact='Helena likes the game',
        created_at=NOW,
    )

    normalize_bound_edge_pointers([extracted], {'candidate-speaker': HELENA_UUID})

    assert extracted.source_node_uuid == HELENA_UUID
    validate_bound_entity_persistence(
        [bound, node('game', 'Shadow of Mordor', ['Entity'])],
        [extracted],
        [bound],
        {'candidate-speaker'},
    )


def test_artificial_speaker_binding_violation_fails_closed() -> None:
    bound = build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW)
    wrong_person = node('wrong-person', 'Other Person')
    bad_edge = EntityEdge(
        uuid='edge-bad',
        source_node_uuid='candidate-speaker',
        target_node_uuid='wrong-person',
        group_id=GROUP,
        name='IS',
        fact='ambiguous speaker fact',
        created_at=NOW,
    )

    with pytest.raises(BoundEntityInvariantError, match='speaker candidate'):
        validate_bound_entity_persistence(
            [bound, wrong_person],
            [bad_edge],
            [bound],
            {'candidate-speaker'},
        )


def test_existing_uuid_with_different_identity_fails_closed() -> None:
    existing = node(HELENA_UUID, 'whatsapp:someone-else@s.whatsapp.net')

    with pytest.raises(BoundEntityInvariantError, match='canonical name'):
        build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW, existing=existing)


def test_multiple_bound_roles_normalize_speaker_and_addressee_before_edges() -> None:
    bound_speaker = build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW)
    bound_addressee = build_bound_entity_node(addressee(), GROUP, NOW)
    extracted_speaker = node('candidate-speaker', bound_speaker.name)
    extracted_addressee = node('candidate-addressee', bound_addressee.name)
    mentioned_person = node('mentioned-third-party', 'Marina')

    result = bind_entity_candidates(
        [extracted_speaker, extracted_addressee, mentioned_person],
        [bound_speaker, bound_addressee],
    )
    edge = EntityEdge(
        uuid='edge-multi',
        source_node_uuid='candidate-speaker',
        target_node_uuid='candidate-addressee',
        group_id=GROUP,
        name='TEASED',
        fact='Filomena teased Gabriel',
        created_at=NOW,
    )

    normalize_bound_edge_pointers([edge], result.uuid_map)

    assert edge.source_node_uuid == HELENA_UUID
    assert edge.target_node_uuid == GABRIEL_UUID
    assert [item.uuid for item in result.nodes_for_resolution] == ['mentioned-third-party']
    validate_bound_entity_persistence(
        [bound_speaker, bound_addressee, mentioned_person],
        [edge],
        [bound_speaker, bound_addressee],
        result.bound_candidate_uuids,
    )


def test_same_display_name_third_party_is_not_bound_to_addressee() -> None:
    bound_addressee = build_bound_entity_node(addressee(), GROUP, NOW)
    marker = node('candidate-addressee', bound_addressee.name)
    mentioned_same_display_name = node('mentioned-gabriel', 'Gabriel')

    result = bind_entity_candidates([marker, mentioned_same_display_name], [bound_addressee])

    assert result.uuid_map['candidate-addressee'] == GABRIEL_UUID
    assert 'mentioned-gabriel' not in result.uuid_map
    assert [item.uuid for item in result.nodes_for_resolution] == ['mentioned-gabriel']


def test_ambiguous_bound_actor_definitions_fail_closed() -> None:
    first = build_bound_entity_node(speaker('Helena', ['Helena']), GROUP, NOW)
    conflicting = node(GABRIEL_UUID, first.name)

    with pytest.raises(BoundEntityInvariantError, match='canonical name'):
        bind_entity_candidates([], [first, conflicting])
