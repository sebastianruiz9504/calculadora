import copy
import unittest
from unittest.mock import patch
import provision_copiers_counter_activity as provision


def option(value):
    return {"Value": value, "Label": {"LocalizedLabels": [{"LanguageCode": 3082, "Label": provision.CHOICES[value]}]}}


class OtherActivityTests(unittest.TestCase):
    def test_existing_counter_is_required_and_unknown_choices_are_rejected(self):
        with self.assertRaises(RuntimeError):
            provision.validate([option(827270010), option(827270011)], 827270013)
        options = [option(v) for v in provision.CHOICES]
        options.append({"Value": 999})
        with self.assertRaises(RuntimeError):
            provision.validate(options, 827270013)

    def test_old_counter_migration_accepts_new_other_choice(self):
        self.assertTrue(provision.validate([option(v) for v in provision.CHOICES]))

    def test_apply_is_additive_and_a_second_run_does_not_insert_again(self):
        options = [option(v) for v in provision.CHOICES if v != 827270013]
        original = copy.deepcopy(options)
        calls = []

        def api(method, path, body=None):
            calls.append((method, path, body))
            if path.startswith("publishers("):
                return {"uniquename": "DigitalTechCopiers", "customizationprefix": "dtc"}
            if path == "InsertOptionValue":
                self.assertEqual(827270013, body["Value"])
                self.assertEqual("Otras actividades", body["Label"]["LocalizedLabels"][0]["Label"])
                options.append(option(body["Value"]))
                return {"NewOptionValue": body["Value"]}
            if path == "PublishXml":
                return {}
            raise AssertionError(path)

        with patch.object(provision, "api", api), patch.object(provision, "rows", return_value=[{"_publisherid_value": "publisher"}]), patch.object(provision, "read_options", side_effect=lambda: copy.deepcopy(options)):
            plan = provision.run(value=827270013)
            self.assertFalse(plan["alreadyPresent"])
            self.assertTrue(all(c[0] == "GET" for c in calls))
            self.assertTrue(provision.run(True, 827270013)["applied"])
            self.assertTrue(provision.run(True, 827270013)["alreadyPresent"])
        self.assertEqual(1, sum(path == "InsertOptionValue" for _, path, _ in calls))
        self.assertEqual(original, options[:-1])


if __name__ == "__main__":
    unittest.main()
