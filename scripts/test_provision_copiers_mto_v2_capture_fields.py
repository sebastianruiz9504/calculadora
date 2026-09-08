"""Offline safety checks for the narrow metadata migration (no network)."""

import copy
import io
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch

import provision_copiers_mto_v2_capture_fields as migration


class CaptureMetadataTests(unittest.TestCase):
    def test_check_only_never_writes(self):
        calls = []

        def fake_api(method, path, body=None):
            calls.append(method)
            if path.startswith("solutions?"):
                return {"value": [{"solutionid": migration.SOLUTION_ID,
                                    "publisherid": {"customizationprefix": "dtc"}}]}
            if "dtc_accuracymeters" in path:
                return {"MinValue": 0, "MaxValue": 250, "Precision": 7, "IsSecured": True}
            return {"value": []}

        with patch.object(migration, "api", side_effect=fake_api), patch("sys.argv", ["migration"]), redirect_stdout(io.StringIO()):
            self.assertEqual(0, migration.main())
        self.assertEqual(["GET", "GET", "GET", "GET"], calls)

    def test_metadata_contract_rejects_wrong_autonumber(self):
        table, name, expected = migration.SPECS[1]
        actual = copy.deepcopy(expected)
        actual["AutoNumberFormat"] = "OTHER-{SEQNUM:6}"
        with self.assertRaisesRegex(RuntimeError, "AutoNumberFormat"):
            migration.validate(actual, expected, table, name)

    def test_email_optional_in_dataverse_no_customer_backfill(self):
        table, name, expected = migration.SPECS[0]
        self.assertEqual("cr07a_cliente", table)
        self.assertEqual("dtc_personaencargadacopiers", name)
        self.assertEqual("Email", expected["FormatName"]["Value"])
        self.assertEqual("None", expected["RequiredLevel"]["Value"])
        migration.validate(copy.deepcopy(expected), expected, table, name)

    def test_whole_table_component_includes_new_column(self):
        with patch.object(migration, "api", side_effect=[
            {"MetadataId": "table-id"},
            {"value": [{"componenttype": 1, "objectid": "table-id", "rootcomponentbehavior": 0}]},
        ]):
            self.assertTrue(migration.solution_contains("cr07a_cliente", "attribute-id"))

    def test_table_shell_does_not_prove_column_membership(self):
        with patch.object(migration, "api", side_effect=[
            {"MetadataId": "table-id"},
            {"value": [{"componenttype": 1, "objectid": "table-id", "rootcomponentbehavior": 2}]},
        ]):
            self.assertFalse(migration.solution_contains("cr07a_cliente", "attribute-id"))


    def test_accuracy_does_not_rewrite_already_compatible_column(self):
        with patch.object(migration, "api", return_value={"MinValue": 0, "MaxValue": 20000000, "Precision": 7, "IsSecured": True}) as api, redirect_stdout(io.StringIO()):
            migration.ensure_accuracy(True)
        self.assertEqual(1, api.call_count)
        self.assertEqual("GET", api.call_args.args[0])

    def test_accuracy_rejects_unexpected_precision(self):
        with patch.object(migration, "api", return_value={"MinValue": 0, "MaxValue": 250, "Precision": 4, "IsSecured": True}):
            with self.assertRaisesRegex(RuntimeError, "Unexpected internal GPS accuracy"):
                migration.ensure_accuracy(True)


if __name__ == "__main__":
    unittest.main()
