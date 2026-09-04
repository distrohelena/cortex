"""Queue service for managing episode processing."""

import asyncio
import logging
from collections.abc import Awaitable, Callable
from datetime import datetime, timezone
from typing import Any

from graphiti_core import Graphiti
from graphiti_core.utils.maintenance.bound_entities import BoundEntity

logger = logging.getLogger(__name__)


class QueueService:
    """Service for managing sequential episode processing queues by group_id."""

    def __init__(self):
        """Initialize the queue service."""
        # Dictionary to store queues for each group_id
        self._episode_queues: dict[str, asyncio.Queue] = {}
        # Dictionary to track if a worker is running for each group_id
        self._queue_workers: dict[str, bool] = {}
        # Store the graphiti client after initialization
        self._graphiti_client: Any = None
        # Graphiti mutates its driver when add_episode changes databases. Never
        # share that mutable instance across concurrently processed groups.
        self._group_clients: dict[str, Graphiti] = {}

    async def add_episode_task(
        self, group_id: str, process_func: Callable[[], Awaitable[None]]
    ) -> int:
        """Add an episode processing task to the queue.

        Args:
            group_id: The group ID for the episode
            process_func: The async function to process the episode

        Returns:
            The position in the queue
        """
        # Initialize queue for this group_id if it doesn't exist
        if group_id not in self._episode_queues:
            self._episode_queues[group_id] = asyncio.Queue()

        # Add the episode processing function to the queue
        await self._episode_queues[group_id].put(process_func)

        # Start a worker for this queue if one isn't already running
        if not self._queue_workers.get(group_id, False):
            # Claim the worker slot synchronously before yielding to the event
            # loop; concurrent enqueue calls must not each spawn a worker.
            self._queue_workers[group_id] = True
            asyncio.create_task(self._process_episode_queue(group_id))

        return self._episode_queues[group_id].qsize()

    async def _process_episode_queue(self, group_id: str) -> None:
        """Process episodes for a specific group_id sequentially.

        This function runs as a long-lived task that processes episodes
        from the queue one at a time.
        """
        logger.info(f'Starting episode queue worker for group_id: {group_id}')
        self._queue_workers[group_id] = True

        try:
            while True:
                # Get the next episode processing function from the queue
                # This will wait if the queue is empty
                process_func = await self._episode_queues[group_id].get()

                try:
                    # Process the episode
                    await process_func()
                except Exception as e:
                    logger.error(
                        f'Error processing queued episode for group_id {group_id}: {str(e)}'
                    )
                finally:
                    # Mark the task as done regardless of success/failure
                    self._episode_queues[group_id].task_done()
        except asyncio.CancelledError:
            logger.info(f'Episode queue worker for group_id {group_id} was cancelled')
        except Exception as e:
            logger.error(f'Unexpected error in queue worker for group_id {group_id}: {str(e)}')
        finally:
            self._queue_workers[group_id] = False
            logger.info(f'Stopped episode queue worker for group_id: {group_id}')

    def get_queue_size(self, group_id: str) -> int:
        """Get the current queue size for a group_id."""
        if group_id not in self._episode_queues:
            return 0
        return self._episode_queues[group_id].qsize()

    def is_worker_running(self, group_id: str) -> bool:
        """Check if a worker is running for a group_id."""
        return self._queue_workers.get(group_id, False)

    async def initialize(self, graphiti_client: Any) -> None:
        """Initialize the queue service with a graphiti client.

        Args:
            graphiti_client: The graphiti client instance to use for processing episodes
        """
        self._graphiti_client = graphiti_client
        self._group_clients.clear()
        logger.info('Queue service initialized with graphiti client')

    def get_group_client(self, group_id: str) -> Graphiti:
        """Return an isolated Graphiti instance whose driver is fixed to one group."""
        if self._graphiti_client is None:
            raise RuntimeError('Queue service not initialized. Call initialize() first.')
        existing = self._group_clients.get(group_id)
        if existing is not None:
            return existing
        base = self._graphiti_client
        client = Graphiti(
            graph_driver=base.driver.clone(database=group_id),
            llm_client=base.llm_client,
            embedder=base.embedder,
            cross_encoder=base.cross_encoder,
            store_raw_episode_content=base.store_raw_episode_content,
            max_coroutines=base.max_coroutines,
        )
        self._group_clients[group_id] = client
        return client

    async def add_episode(
        self,
        group_id: str,
        name: str,
        content: str,
        source_description: str,
        episode_type: Any,
        entity_types: Any,
        uuid: str | None,
        episode_uuid: str | None = None,
        reference_time: datetime | None = None,
        bound_entity: BoundEntity | None = None,
        bound_entities: list[BoundEntity] | None = None,
        extraction_body: str | None = None,
        custom_extraction_instructions: str | None = None,
    ) -> int:
        """Add an episode for processing.

        Args:
            group_id: The group ID for the episode
            name: Name of the episode
            content: Episode content
            source_description: Description of the episode source
            episode_type: Type of the episode
            entity_types: Entity types for extraction
            uuid: Episode UUID

        Returns:
            The position in the queue
        """
        if self._graphiti_client is None:
            raise RuntimeError('Queue service not initialized. Call initialize() first.')

        identity = episode_uuid or uuid or name
        group_client = self.get_group_client(group_id)

        async def process_episode():
            """Process the episode using the graphiti client."""
            try:
                logger.info(f'Processing episode {identity} for group {group_id}')

                # Process the episode using the graphiti client
                await group_client.add_episode(
                    name=name,
                    episode_body=content,
                    source_description=source_description,
                    source=episode_type,
                    group_id=group_id,
                    reference_time=reference_time or datetime.now(timezone.utc),
                    entity_types=entity_types,
                    uuid=uuid,
                    episode_uuid=episode_uuid,
                    bound_entity=bound_entity,
                    bound_entities=bound_entities,
                    extraction_body=extraction_body,
                    custom_extraction_instructions=custom_extraction_instructions,
                )

                logger.info(f'Successfully processed episode {identity} for group {group_id}')

            except Exception as e:
                logger.error(f'Failed to process episode {identity} for group {group_id}: {str(e)}')
                raise

        # Use the existing add_episode_task method to queue the processing
        return await self.add_episode_task(group_id, process_episode)
