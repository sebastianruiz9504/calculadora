"""Pure contract tests. No authentication or Dataverse requests are performed."""
import copy
import unittest
from unittest.mock import patch
import provision_copiers_activity_v2 as provision


class MetadataFixture:
    def __init__(self):
        self.calls = []
        self.tables = {}
        self.attributes = {}
        self.relationships = {}
        self.keys = {}
        for logical in provision.TABLES:
            spec = provision.table_spec(logical)
            self.tables[logical] = {
                "LogicalName": logical, "SchemaName": spec.schema_name, "MetadataId": logical + "-metadata",
                "EntitySetName": logical + "s", "PrimaryIdAttribute": logical + "id", "PrimaryNameAttribute": "dtc_name",
                "OwnershipType": "UserOwned", "IsAuditEnabled": {"Value": True}, "IsOptimisticConcurrencyEnabled": True, "HasNotes": False,
            }
            specs = (provision.base.primary_name_spec(spec),) + spec.columns
            self.attributes[logical] = {item.logical_name: self.attribute(item) for item in specs}
            field = "dtc_operationkey" if logical == "dtc_copiersactivityv2" else "dtc_evidencekey"
            self.keys[logical] = [{"SchemaName": spec.schema_name + "Key", "KeyAttributes": [field], "EntityKeyIndexStatus": "Active"}]
        for spec in provision.RELATIONSHIPS:
            self.relationships[spec.schema_name] = {
                **provision.base.relationship_payload(spec), "ReferencedAttribute": spec.referenced_table + "id",
                "ReferencingAttribute": spec.lookup_logical_name, "ReferencedEntityNavigationPropertyName": spec.schema_name,
                "ReferencingEntityNavigationPropertyName": spec.lookup_schema_name,
            }
            self.attributes[spec.referencing_table][spec.lookup_logical_name] = {
                "LogicalName": spec.lookup_logical_name, "SchemaName": spec.lookup_schema_name, "AttributeType": "Lookup",
                "AttributeTypeName": {"Value": "LookupType"}, "RequiredLevel": {"Value": "ApplicationRequired" if spec.required else "None"},
                "IsSecured": False, "IsAuditEnabled": {"Value": True}, "Targets": [spec.referenced_table],
            }
        for logical in provision.BUSINESS_FIELDS:
            self.attributes[logical] = {item.logical_name: self.attribute(item) for item in provision.business_specs(logical)}

    @staticmethod
    def attribute(spec):
        result = copy.deepcopy(spec.payload)
        result.update({"LogicalName": spec.logical_name, "SchemaName": spec.schema_name,
            "AttributeType": "Virtual" if spec.attribute_type == "File" else spec.attribute_type,
            "AttributeTypeName": {"Value": spec.attribute_type + "Type"}, "IsSecured": spec.secured})
        return result

    def api(self, method, path, body=None):
        self.calls.append((method, path, body))
        if method != "GET": raise AssertionError("Tests must never reach a mutation: " + path)
        if path.startswith("solutions?"): return {"value": [{"_publisherid_value": "publisher"}]}
        if path.startswith("publishers("): return {"uniquename": "DigitalTechCopiers", "customizationprefix": "dtc"}
        if path.startswith("EntityDefinitions?"):
            logical = path.rsplit("'", 2)[1]
            return {"value": [self.tables[logical]] if logical in self.tables else []}
        if path.startswith("RelationshipDefinitions/"):
            name = path.rsplit("'", 2)[1]
            return {"value": [self.relationships[name]] if name in self.relationships else []}
        logical = path.split("'", 2)[1]
        if "/Keys?" in path: return {"value": self.keys[logical]}
        if "/Attributes(LogicalName=" in path:
            return self.attributes[logical][path.split("'")[3]]
        if "/Attributes/Microsoft.Dynamics.CRM." in path:
            kind = path.split("Microsoft.Dynamics.CRM.")[1].split("AttributeMetadata")[0]
            return {"value": [item for item in self.attributes[logical].values() if item["AttributeTypeName"]["Value"] == kind + "Type"]}
        if "/Attributes?" in path: return {"value": list(self.attributes[logical].values())}
        raise AssertionError("Unexpected read: " + path)


class ActivityMetadataTests(unittest.TestCase):
    def setUp(self):
        self.fixture = MetadataFixture()
        self.mock = patch.object(provision, "api", self.fixture.api)
        self.mock.start()
        self.addCleanup(self.mock.stop)

    def assert_invalid(self, message):
        with self.assertRaisesRegex(RuntimeError, message): provision.ensure_schema(False)
        self.assertTrue(all(method == "GET" for method, _, _ in self.fixture.calls))

    def test_complete_contract_is_ready_and_idempotent_without_writes(self):
        for _ in range(2):
            result = provision.ensure_schema(False)
            self.assertTrue(result["ready"])
            self.assertEqual([], result["actions"])
        self.assertTrue(all(method == "GET" for method, _, _ in self.fixture.calls))

    def test_already_ready_apply_does_not_publish_or_mutate(self):
        self.assertTrue(provision.ensure_schema(True)["ready"])
        self.assertTrue(all(method == "GET" for method, _, _ in self.fixture.calls))

    def test_choices_are_distinct_and_copy_does_not_modify_maintenance_template(self):
        originals = copy.deepcopy(provision.base.MAIN_COLUMNS)
        choices = {p["SchemaName"].lower(): p for p in provision.columns("dtc_copiersactivityv2")}
        self.assertEqual([827270010, 827270011], [o["Value"] for o in choices["dtc_maintenancetype"]["OptionSet"]["Options"]])
        self.assertEqual([827270000, 827270001, 827270002, 827270003], [o["Value"] for o in choices["dtc_workflowstate"]["OptionSet"]["Options"]])
        self.assertEqual(originals, provision.base.MAIN_COLUMNS)

    def test_role_field_policy_is_exactly_the_approved_secured_template(self):
        policy = provision.security_field_policy()
        self.assertEqual(23, len(policy["dtc_copiersactivityv2"]))
        self.assertEqual(4, len(policy["dtc_copiersactivityevidencev2"]))
        self.assertIn("dtc_filecontent", policy["dtc_copiersactivityevidencev2"])
        self.assertIn("dtc_latitude", policy["dtc_copiersactivityv2"])
        self.assertEqual([], self.fixture.calls)

    def test_wrong_existing_type_is_rejected_before_any_write(self):
        item = self.fixture.attributes["dtc_copiersactivityv2"]["dtc_title"]
        item["AttributeTypeName"] = {"Value": "IntegerType"}
        item["AttributeType"] = "Integer"
        self.assert_invalid("dtc_title")

    def test_wrong_choice_value_is_rejected(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_emailstate"]["OptionSet"]["Options"][3]["Value"] = 1
        self.assert_invalid("choice values")

    def test_wrong_choice_label_is_rejected(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_maintenancetype"]["OptionSet"]["Options"][0]["Label"]["LocalizedLabels"][0]["Label"] = "Preventivo"
        self.assert_invalid("choice values")

    def test_wrong_file_limit_is_rejected(self):
        self.fixture.attributes["dtc_copiersactivityevidencev2"]["dtc_filecontent"]["MaxSizeInKB"] = 1024
        self.assert_invalid("MaxSizeInKB")

    def test_wrong_memo_limit_is_rejected(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_answersjson"]["MaxLength"] = 1000
        self.assert_invalid("MaxLength")

    def test_wrong_date_behavior_is_rejected(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_servicedate"]["DateTimeBehavior"] = {"Value": "UserLocal"}
        self.assert_invalid("DateTimeBehavior")

    def test_autonumber_format_is_verified(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_reference"]["AutoNumberFormat"] = "MTO-{SEQNUM:6}"
        self.assert_invalid("AutoNumberFormat")

    def test_existing_column_security_is_verified(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_latitude"]["IsSecured"] = False
        self.assert_invalid("IsSecured")

    def test_unexpected_secured_field_is_not_granted_by_dynamic_discovery(self):
        self.fixture.attributes["dtc_copiersactivityv2"]["dtc_secret"] = {"LogicalName": "dtc_secret", "IsSecured": True, "AttributeTypeName": {"Value": "StringType"}}
        self.assert_invalid("Unexpected secured fields")

    def test_ownership_and_concurrency_are_required(self):
        for field, value in (("OwnershipType", "OrganizationOwned"), ("IsOptimisticConcurrencyEnabled", False)):
            with self.subTest(field=field):
                self.fixture = MetadataFixture()
                with patch.object(provision, "api", self.fixture.api):
                    self.fixture.tables["dtc_copiersactivityv2"][field] = value
                    self.assert_invalid(field)

    def test_pending_or_failed_key_is_not_ready_or_recreated(self):
        for status in ("Pending", "InProgress", "Failed", None):
            with self.subTest(status=status):
                self.fixture.keys["dtc_copiersactivityv2"][0]["EntityKeyIndexStatus"] = status
                self.assert_invalid("not Active")

    def test_conflicting_key_name_or_attributes_is_rejected(self):
        self.fixture.keys["dtc_copiersactivityv2"][0]["KeyAttributes"] = ["dtc_title"]
        self.assert_invalid("Incompatible alternate key")

    def test_navigation_property_casing_is_exact(self):
        self.fixture.relationships["dtc_CopiersActivityV2_Evidence"]["ReferencingEntityNavigationPropertyName"] = "dtc_signedactivity"
        self.assert_invalid("exact casing")

    def test_lookup_parent_target_and_required_level_are_verified(self):
        self.fixture.attributes["dtc_copiersactivityevidencev2"]["dtc_signedactivity"]["Targets"] = ["dtc_copiersmtov2"]
        self.assert_invalid("Targets")

    def test_relationship_cannot_point_to_another_parent_table(self):
        self.fixture.relationships["dtc_CopiersActivityV2_Evidence"]["ReferencedEntity"] = "dtc_copiersmtov2"
        self.assert_invalid("ReferencedEntity")

    def test_cascade_delete_is_rejected(self):
        self.fixture.relationships["dtc_CopiersActivityV2_Evidence"]["CascadeConfiguration"]["Delete"] = "Cascade"
        self.assert_invalid("CascadeConfiguration.Delete")

    def test_existing_lookup_without_expected_relationship_is_not_overwritten(self):
        del self.fixture.relationships["dtc_CopiersActivityV2_Evidence"]
        self.assert_invalid("Lookup exists")

    def test_legacy_trace_field_limits_are_verified(self):
        self.fixture.attributes["cr07a_movimientosequipos"]["dtc_originclientname"]["MaxLength"] = 100
        self.assert_invalid("MaxLength")

    def test_missing_field_is_planned_without_a_write(self):
        del self.fixture.attributes["cr07a_entrega"]["dtc_reference"]
        result = provision.ensure_schema(False)
        self.assertFalse(result["ready"])
        self.assertEqual(["Create column cr07a_entrega.dtc_reference"], result["actions"])

    def test_preflight_failure_anywhere_prevents_all_mutations(self):
        del self.fixture.attributes["dtc_copiersactivityv2"]["dtc_title"]
        self.fixture.attributes["cr07a_entrega"]["dtc_reference"]["MaxLength"] = 1
        with self.assertRaisesRegex(RuntimeError, "cr07a_entrega"):
            provision.ensure_schema(True)
        self.assertTrue(all(method == "GET" for method, _, _ in self.fixture.calls))

    def test_missing_tables_plan_contains_all_fields_keys_relationships(self):
        self.fixture.tables = {}
        self.fixture.relationships = {}
        result = provision.ensure_schema(False)
        self.assertFalse(result["ready"])
        expected = 2 + sum(len(provision.columns(name)) for name in provision.TABLES) + 2 + 3
        self.assertEqual(expected, len(result["actions"]))

    def test_paged_metadata_is_not_silently_accepted(self):
        with patch.object(provision, "api", return_value={"value": [], "@odata.nextLink": "unread-page"}):
            self.assert_invalid("Paged metadata")


if __name__ == "__main__":
    unittest.main()
