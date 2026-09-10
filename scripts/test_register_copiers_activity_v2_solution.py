"""Pure local solution-registration contract tests; never invokes the CLI."""
import copy
import unittest
from unittest.mock import patch
import register_copiers_activity_v2_solution as subject


def identity(index):
    return f"00000000-0000-0000-0000-{index:012d}"


class RegistrationTests(unittest.TestCase):
    def setUp(self):
        self.desired = [{"name": "component" + str(i), "componentType": 1 if i < 2 else 2,
                         "componentId": identity(i + 1)} for i in range(10)]
        self.current = {(20, identity(90)): {"id": identity(91), "behavior": None}}
        self.posts = []

    def fake_api(self, method, path, body):
        self.assertEqual((method, path), ("POST", "AddSolutionComponent"))
        self.assertFalse(body["AddRequiredComponents"])
        self.posts.append(copy.deepcopy(body))
        self.current[(body["ComponentType"], body["ComponentId"])] = {
            "id": identity(100 + len(self.posts)), "behavior": 0 if body["ComponentType"] == 1 else None}
        return {}

    def run_local(self, apply):
        with patch.object(subject, "preflight", return_value=(self.desired, {identity(80)})), \
             patch.object(subject, "membership", side_effect=lambda: copy.deepcopy(self.current)), \
             patch.object(subject, "api", side_effect=self.fake_api):
            return subject.run(apply)

    def test_plan_is_read_only_and_exactly_ten(self):
        result = self.run_local(False)
        self.assertEqual(10, len(result["missing"]))
        self.assertEqual([], self.posts)
        self.assertFalse(result["ready"])

    def test_apply_exact_components_and_replay_zero_writes(self):
        result = self.run_local(True)
        self.assertTrue(result["ready"])
        self.assertEqual([1, 1] + [2] * 8, [x["ComponentType"] for x in self.posts])
        self.assertTrue(all(x.get("DoNotIncludeSubcomponents") is False for x in self.posts[:2]))
        self.assertTrue(all("DoNotIncludeSubcomponents" not in x for x in self.posts[2:]))
        self.assertEqual([], self.run_local(True)["added"])
        self.assertEqual(10, len(self.posts))

    def test_missing_full_root_includes_existing_shell(self):
        item = self.desired[0]
        current = {(1, item["componentId"]): {"id": identity(30), "behavior": 2}}
        self.assertEqual([item], subject.missing_components([item], current))
        full = {(1, item["componentId"]): {"id": identity(30), "behavior": 0}}
        subject.assert_preserved(current, full, self.desired, set())

    def test_unknown_root_behavior_fails(self):
        item = self.desired[0]
        with self.assertRaises(RuntimeError):
            subject.missing_components([item], {(1, item["componentId"]): {"id": identity(30), "behavior": None}})

    def test_existing_unrelated_membership_must_be_preserved(self):
        with self.assertRaises(RuntimeError):
            subject.assert_preserved(self.current, {}, self.desired, set())

    def test_legacy_table_only_shell_allowed(self):
        shell = {(1, identity(80)): {"id": identity(81), "behavior": 2}}
        subject.assert_preserved({}, shell, self.desired, {identity(80)})
        shell[(1, identity(80))]["behavior"] = 0
        with self.assertRaises(RuntimeError):
            subject.assert_preserved({}, shell, self.desired, {identity(80)})

    def test_paged_membership_fails_closed(self):
        with patch.object(subject, "api", return_value={"value": [], "@odata.nextLink": "next"}):
            with self.assertRaises(RuntimeError):
                subject.membership()

    def test_wrong_solution_aborts_preflight(self):
        with patch.object(subject, "api", return_value={"solutionid": subject.SOLUTION_ID, "uniquename": "Other",
                                                       "ismanaged": False, "_publisherid_value": subject.PUBLISHER_ID}):
            with self.assertRaises(RuntimeError):
                subject.preflight()

    def test_failed_write_is_never_retried(self):
        with patch.object(subject, "preflight", return_value=(self.desired, set())), \
             patch.object(subject, "membership", return_value=copy.deepcopy(self.current)), \
             patch.object(subject, "api", side_effect=RuntimeError("uncertain")) as api:
            with self.assertRaises(RuntimeError):
                subject.run(True)
            self.assertEqual(1, api.call_count)

    def test_concurrent_loss_aborts_before_write(self):
        with patch.object(subject, "preflight", return_value=(self.desired, set())), \
             patch.object(subject, "membership", side_effect=[copy.deepcopy(self.current), {}]), \
             patch.object(subject, "api") as api:
            with self.assertRaises(RuntimeError):
                subject.run(True)
            api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
