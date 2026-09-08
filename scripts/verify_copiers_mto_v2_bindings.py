"""Read-only verification of all configured MTO V2 bindings against Dataverse."""

import json
from pathlib import Path

from provision_copiers_mto_v2_capture_fields import api


def main():
    cfg = json.loads((Path(__file__).resolve().parent.parent / "appsettings.json").read_text(encoding="utf-8-sig"))["CopiersMtoV2"]["Dataverse"]
    tables = {"main": "dtc_copiersmtov2", "evidence": "dtc_copiersmtoevidenciav2"}
    metadata = {}
    checked = 0
    for kind, table in tables.items():
        entity = api("GET", f"EntityDefinitions(LogicalName='{table}')?"
                     "%24select=LogicalName,EntitySetName,PrimaryIdAttribute,IsOptimisticConcurrencyEnabled&"
                     "%24expand=Attributes(%24select=LogicalName),"
                     "ManyToOneRelationships(%24select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity),"
                     "Keys(%24select=SchemaName,KeyAttributes,EntityKeyIndexStatus)")
        metadata[kind] = entity
        prefix = "Evidence" if kind == "evidence" else "Main"
        assert entity["EntitySetName"] == cfg[prefix + "EntitySetName"]
        assert entity["PrimaryIdAttribute"] == cfg[prefix + "IdField"]
        names = {item["LogicalName"] for item in entity["Attributes"]}
        for prop, value in cfg.items():
            if not prop.endswith("Field"):
                continue
            if prop.startswith("Evidence") != (kind == "evidence"):
                continue
            assert value in names, f"Missing configured column {table}.{value} ({prop})"
            checked += 1
        assert entity["Keys"] and all(key["EntityKeyIndexStatus"] == "Active" for key in entity["Keys"])
    assert metadata["main"]["IsOptimisticConcurrencyEnabled"]
    for kind, nav_config, lookup_config, target in (
        ("main", "ClientNavigationProperty", "ClientLookupLogicalName", "cr07a_cliente"),
        ("main", "EquipmentNavigationProperty", "EquipmentLookupLogicalName", "cr07a_equipo"),
        ("evidence", "EvidenceParentNavigationProperty", "EvidenceParentLookupLogicalName", "dtc_copiersmtov2"),
    ):
        assert any(rel["ReferencingAttribute"] == cfg[lookup_config] and
                   rel["ReferencingEntityNavigationPropertyName"] == cfg[nav_config] and
                   rel["ReferencedEntity"] == target
                   for rel in metadata[kind]["ManyToOneRelationships"]), f"Wrong navigation {nav_config}"

    choices = (
        ("main", "WorkflowStateField", {"DraftStateValue": "Draft", "FinalizingStateValue": "Finalizing", "ReadyToSendStateValue": "ReadyToSend", "FailedStateValue": "Failed"}),
        ("main", "EmailStateField", {"EmailNotReadyStateValue": "NotReady", "EmailPendingStateValue": "Pending", "EmailProcessingStateValue": "Processing", "EmailSentStateValue": "Sent", "EmailFailedStateValue": "Failed"}),
        ("main", "MaintenanceTypeField", {"MaintenanceTypeCorrectiveValue": "Correctivo", "MaintenanceTypePreventiveValue": "Preventivo"}),
        ("evidence", "EvidencePurposeField", {"EvidenceSignaturePurposeValue": "Signature", "EvidenceSignedReportPurposeValue": "SignedReport", "EvidenceOriginalAttachmentPurposeValue": "OriginalAttachment", "EvidenceCustomerAttachmentPurposeValue": "CustomerAttachment"}),
        ("evidence", "EvidenceSecurityStateField", {"EvidenceSecurityNotApplicableValue": "NotApplicable", "EvidenceSecurityPendingValue": "Pending", "EvidenceSecurityScanPassedValue": "ScanPassed", "EvidenceSecurityRejectedValue": "Rejected"}),
    )
    checked_choices = 0
    for kind, field, mapping in choices:
        choice = api("GET", f"EntityDefinitions(LogicalName='{tables[kind]}')/Attributes(LogicalName='{cfg[field]}')/"
                     "Microsoft.Dynamics.CRM.PicklistAttributeMetadata?%24select=LogicalName&%24expand=OptionSet")
        actual = {item["Value"]: {label["Label"] for label in item["Label"]["LocalizedLabels"]}
                  for item in choice["OptionSet"]["Options"]}
        for prop, label in mapping.items():
            assert label in actual.get(cfg[prop], set()), f"Wrong choice {prop}={cfg[prop]} (expected {label})"
            checked_choices += 1
    file = api("GET", "EntityDefinitions(LogicalName='dtc_copiersmtoevidenciav2')/Attributes(LogicalName='dtc_filecontent')/"
               "Microsoft.Dynamics.CRM.FileAttributeMetadata?%24select=LogicalName,MaxSizeInKB,IsSecured")
    assert file["MaxSizeInKB"] == 12288 and file["IsSecured"] is True
    accuracy = api("GET", "EntityDefinitions(LogicalName='dtc_copiersmtov2')/Attributes(LogicalName='dtc_accuracymeters')/"
                   "Microsoft.Dynamics.CRM.DecimalAttributeMetadata?%24select=MaxValue,MinValue,Precision,IsSecured")
    assert (accuracy["MinValue"], accuracy["MaxValue"], accuracy["Precision"], accuracy["IsSecured"]) == (0, 20000000, 7, True)
    print(json.dumps({"result": "PASS", "checkedColumns": checked, "checkedChoiceValues": checked_choices,
                      "checkedNavigations": 3, "activeAlternateKeys": 2, "mainOptimisticConcurrency": True,
                      "evidenceMaxSizeInKB": file["MaxSizeInKB"], "evidenceFileSecured": file["IsSecured"],
                      "accuracyMaxMeters": accuracy["MaxValue"], "accuracyPrecision": accuracy["Precision"]}))


if __name__ == "__main__":
    main()
