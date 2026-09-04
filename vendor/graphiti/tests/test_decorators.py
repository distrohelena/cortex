import asyncio
from types import SimpleNamespace
from unittest.mock import Mock

from graphiti_core.decorators import handle_multiple_group_ids
from graphiti_core.driver.driver import GraphProvider


class FakeFalkorDriver:
    provider = GraphProvider.FALKORDB

    def __init__(self, database: str):
        self.database = database
        self.clone = Mock(side_effect=lambda database: FakeFalkorDriver(database))


class FakeGraphiti:
    def __init__(self):
        self.clients = SimpleNamespace(driver=FakeFalkorDriver('default_db'))
        self.max_coroutines = 1

    @handle_multiple_group_ids
    async def search(self, group_ids=None, driver=None):
        return [driver.database if driver is not None else self.clients.driver.database]


def test_single_falkordb_group_uses_that_group_as_database():
    async def run():
        graphiti = FakeGraphiti()

        result = await graphiti.search(group_ids=['whatsapp-group-a'])

        assert result == ['whatsapp-group-a']
        graphiti.clients.driver.clone.assert_called_once_with(database='whatsapp-group-a')

    asyncio.run(run())


def test_no_group_keeps_the_configured_database():
    async def run():
        graphiti = FakeGraphiti()

        result = await graphiti.search(group_ids=None)

        assert result == ['default_db']
        graphiti.clients.driver.clone.assert_not_called()

    asyncio.run(run())
