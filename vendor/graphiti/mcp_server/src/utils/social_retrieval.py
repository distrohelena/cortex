"""Deterministic, group-bound helpers for active social-memory retrieval."""

from datetime import datetime, timezone

from graphiti_core.driver.driver import GraphDriver, GraphProvider
from graphiti_core.edges import EntityEdge
from graphiti_core.nodes import EntityNode
from graphiti_core.search.search_filters import ComparisonOperator, DateFilter, SearchFilters
from graphiti_core.utils.maintenance.graph_data_operations import clear_data

from utils.formatting import format_fact_result


async def clear_group_namespaces(driver: GraphDriver, group_ids: list[str]) -> None:
    """Clear the requested logical groups without crossing FalkorDB graph boundaries."""
    if driver.provider == GraphProvider.FALKORDB:
        for group_id in group_ids:
            await clear_data(driver.clone(database=group_id), group_ids=None)
        return
    await clear_data(driver, group_ids=group_ids)


async def load_endpoint_nodes_by_group(
    driver: GraphDriver,
    edges: list[EntityEdge],
) -> dict[tuple[str, str], EntityNode]:
    """Load semantic-result endpoints from the physical graph that owns each edge."""
    endpoint_uuids: dict[str, set[str]] = {}
    for edge in edges:
        endpoint_uuids.setdefault(edge.group_id, set()).update(
            (edge.source_node_uuid, edge.target_node_uuid)
        )

    nodes_by_group: dict[tuple[str, str], EntityNode] = {}
    for group_id, uuids in endpoint_uuids.items():
        group_driver = (
            driver.clone(database=group_id)
            if driver.provider == GraphProvider.FALKORDB
            else driver
        )
        for node in await EntityNode.get_by_uuids(group_driver, list(uuids)):
            if node.group_id == group_id:
                nodes_by_group[(group_id, node.uuid)] = node
    return nodes_by_group


def format_fact_with_group_nodes(
    edge: EntityEdge,
    nodes_by_group: dict[tuple[str, str], EntityNode],
) -> dict[str, object]:
    local_nodes = {
        uuid: node
        for (group_id, uuid), node in nodes_by_group.items()
        if group_id == edge.group_id
    }
    return format_fact_with_nodes(edge, local_nodes)


def active_edge_filter(now: datetime) -> SearchFilters:
    """Build the explicit temporal filter used by semantic active-memory search."""
    return SearchFilters(
        valid_at=[
            [DateFilter(comparison_operator=ComparisonOperator.is_null)],
            [DateFilter(date=now, comparison_operator=ComparisonOperator.less_than_equal)],
        ],
        invalid_at=[
            [DateFilter(comparison_operator=ComparisonOperator.is_null)],
            [DateFilter(date=now, comparison_operator=ComparisonOperator.greater_than)],
        ],
        expired_at=[[DateFilter(comparison_operator=ComparisonOperator.is_null)]],
    )


def _is_active(edge: EntityEdge, now: datetime) -> bool:
    if edge.expired_at is not None:
        return False
    if edge.invalid_at is not None and edge.invalid_at <= now:
        return False
    return edge.valid_at is None or edge.valid_at <= now


def normalize_persisted_edge_direction(edge: EntityEdge) -> EntityEdge:
    """Restore source/target direction after an undirected neighborhood match."""
    source_uuid = edge.attributes.get('source_uuid')
    target_uuid = edge.attributes.get('target_uuid')
    if not isinstance(source_uuid, str) or not isinstance(target_uuid, str):
        return edge
    if {source_uuid, target_uuid} != {edge.source_node_uuid, edge.target_node_uuid}:
        raise ValueError(f'edge {edge.uuid} has inconsistent persisted endpoints')
    edge.source_node_uuid = source_uuid
    edge.target_node_uuid = target_uuid
    return edge


def filter_neighborhood_edges(
    edges: list[EntityEdge],
    *,
    group_id: str,
    active_only: bool,
    now: datetime,
    limit: int,
) -> list[EntityEdge]:
    """Apply partition, temporal, stable ordering, and limit constraints."""
    if limit <= 0:
        return []
    filtered = [
        edge
        for edge in edges
        if edge.group_id == group_id and (not active_only or _is_active(edge, now))
    ]
    filtered.sort(key=lambda edge: edge.uuid)
    filtered.sort(
        key=lambda edge: edge.created_at or datetime.min.replace(tzinfo=timezone.utc),
        reverse=True,
    )
    return filtered[:limit]


def format_fact_with_nodes(
    edge: EntityEdge,
    nodes_by_uuid: dict[str, EntityNode],
) -> dict[str, object]:
    """Serialize a fact with enough endpoint provenance to quarantine legacy Persons."""

    result: dict[str, object] = format_fact_result(edge)
    for side, node_uuid in (
        ('source_node', edge.source_node_uuid),
        ('target_node', edge.target_node_uuid),
    ):
        node = nodes_by_uuid.get(node_uuid)
        if node is None:
            result[side] = {
                'uuid': node_uuid,
                'canonical': False,
            }
            continue
        stable_id = node.attributes.get('stable_id') if node.attributes else None
        result[side] = {
            'uuid': node.uuid,
            'name': node.name,
            'labels': node.labels,
            'stable_id': stable_id,
            'canonical': isinstance(stable_id, str) and bool(stable_id),
        }
    return result
