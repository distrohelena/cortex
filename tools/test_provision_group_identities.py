import unittest

from provision_group_identities import missing_identities


class IdentityPlanTests(unittest.TestCase):
    def test_only_missing_nodes_are_planned(self):
        existing = {'uuid': 'one', 'stable_id': 'actor-one', 'group_id': 'group',
                    'labels': ['Person', 'Entity']}
        audit = {'namespace': 'group', 'expected': [
            {'uuid': 'one', 'stable_id': 'actor-one', 'uuid_matches': [existing], 'stable_matches': [existing]},
            {'uuid': 'two', 'stable_id': 'actor-two', 'uuid_matches': [], 'stable_matches': []},
        ]}
        self.assertEqual(missing_identities(audit), [{'uuid': 'two', 'stable_id': 'actor-two'}])

    def test_conflicting_legacy_identity_is_not_duplicated(self):
        audit = {'namespace': 'group', 'expected': [
            {'uuid': 'one', 'stable_id': 'actor-one', 'uuid_matches': [], 'stable_matches': [{'uuid': 'legacy'}]},
        ]}
        with self.assertRaises(ValueError):
            missing_identities(audit)

    def test_wrong_namespace_or_labels_is_not_accepted(self):
        for group, labels in [('foreign', ['Person']), ('group', ['Entity'])]:
            node = {'uuid': 'one', 'stable_id': 'actor-one', 'group_id': group, 'labels': labels}
            with self.assertRaises(ValueError):
                missing_identities({'namespace': 'group', 'expected': [
                    {'uuid': 'one', 'stable_id': 'actor-one', 'uuid_matches': [node], 'stable_matches': [node]},
                ]})


if __name__ == '__main__':
    unittest.main()
