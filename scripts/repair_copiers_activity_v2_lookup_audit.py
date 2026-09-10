"""Normalize only three verified new activity lookup audit flags.

Dataverse column metadata uses full-definition PUT. The only semantic delta is
IsAuditEnabled.Value; labels and all other definition values are preserved.
https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-update-column-definitions-using-web-api#update-a-column
"""
import argparse
import copy
import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path
import provision_copiers_activity_v2 as provision

IDS = {
    ("dtc_copiersactivityv2", "dtc_client"): "98353e5b-2c1a-4546-b10f-a7bbff7d569c",
    ("dtc_copiersactivityv2", "dtc_equipment"): "96a4566d-9941-46c1-9b5f-151b26cc8e57",
    ("dtc_copiersactivityevidencev2", "dtc_signedactivity"): "5e3c8042-4965-4148-90f5-b56e61e466d3",
}

def normalized(value):
    if isinstance(value, dict):
        return {key: normalized(item) for key,item in value.items()
            if not key.startswith("@odata.") and key not in ("ModifiedOn", "HasChanged")}
    if isinstance(value, list): return [normalized(item) for item in value]
    return value

def payload_for(spec, current):
    expected_id = IDS[(spec.referencing_table, spec.lookup_logical_name)]
    if current.get("MetadataId", "").lower() != expected_id or current.get("EntityLogicalName") != spec.referencing_table:
        raise RuntimeError("Lookup identity changed; refusing repair: " + spec.lookup_logical_name)
    differences = provision.base.relationship_lookup_contract_differences(spec, current, verify_audit=False)
    if current.get("SchemaName") != spec.lookup_schema_name: differences.append("SchemaName casing differs")
    if differences: raise RuntimeError("Non-audit lookup drift: " + "; ".join(differences))
    audit = current.get("IsAuditEnabled") or {}
    if audit.get("Value") not in (True, False) or audit.get("CanBeChanged") is not True:
        raise RuntimeError("Audit setting is not safely editable: " + spec.lookup_logical_name)
    payload = provision.base.lookup_attribute_update_payload(current)
    expected = copy.deepcopy(current)
    expected["IsAuditEnabled"]["Value"] = True
    if normalized(payload) != normalized(expected): raise RuntimeError("Repair would change more than the audit value")
    return payload

def put_lookup(spec, payload):
    if payload.get("MetadataId", "").lower() != IDS[(spec.referencing_table, spec.lookup_logical_name)]:
        raise RuntimeError("Unapproved metadata target")
    executable = shutil.which("dataverse")
    if not executable: raise RuntimeError("Authenticated Dataverse CLI is required")
    launcher = Path(executable).parent / "node_modules" / "@microsoft" / "dataverse" / "bin" / "dataverse.js"
    command = [shutil.which("node"), str(launcher)] if Path(executable).suffix.lower() == ".cmd" and launcher.is_file() else [executable]
    with tempfile.TemporaryDirectory(prefix="copiers-activity-audit-") as temp:
        body_path = Path(temp) / "lookup.json"
        body_path.write_text(json.dumps(payload, ensure_ascii=True), encoding="utf-8")
        args = command + ["api", "request", "--target", "dataverse", "--method", "PUT",
            "--path", "/api/data/v9.2/" + provision.base.lookup_attribute_metadata_path(spec, typed=False),
            "--environment", provision.ENV, "--body-file", str(body_path),
            "--header", "MSCRM.SolutionUniqueName:" + provision.SOLUTION, "--header", "MSCRM.MergeLabels:true",
            "--context", "app=dataverse-skills/1.11.3;skill=dv-metadata;agent=codex"]
        result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", result.stdout + result.stderr)
    start,end = output.find("{"),output.rfind("}")
    response = json.loads(output[start:end+1]) if start >= 0 and end >= start else {}
    if result.returncode or "error" in response: raise RuntimeError("Lookup PUT failed; reconcile without blind retry: " + output[-1200:])

def repair(apply=False):
    plan = []
    # Validate all three immutable identities/contracts before the first PUT.
    for spec in provision.RELATIONSHIPS:
        current = provision.api("GET", provision.base.lookup_attribute_metadata_path(spec, typed=True))
        payload = payload_for(spec, current)
        plan.append((spec,current,payload))
    changes = [item for item in plan if item[1]["IsAuditEnabled"]["Value"] is False]
    if apply:
        for spec,current,payload in changes:
            latest = provision.api("GET", provision.base.lookup_attribute_metadata_path(spec, typed=True))
            if normalized(latest) != normalized(current): raise RuntimeError("Lookup changed since preflight; abort before PUT")
            put_lookup(spec,payload)
        if changes:
            provision.api("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities><entity>dtc_copiersactivityv2</entity><entity>dtc_copiersactivityevidencev2</entity></entities></importexportxml>"})
        for spec,current,payload in plan:
            actual = provision.api("GET", provision.base.lookup_attribute_metadata_path(spec, typed=True))
            payload_for(spec,actual)
            if normalized(actual) != normalized(payload): raise RuntimeError("Audit read-back differs from the single-property repair")
    return {"mode":"apply" if apply else "plan","environment":provision.ENV,"solution":provision.SOLUTION,
        "changes":[{"table":spec.referencing_table,"column":spec.lookup_logical_name,"metadataId":current["MetadataId"],"property":"IsAuditEnabled.Value","before":False,"after":True} for spec,current,_ in changes],
        "ready":apply or not changes,"legacyUntouched":True}

def self_test():
    from test_provision_copiers_activity_v2 import MetadataFixture
    fixture = MetadataFixture()
    for spec in provision.RELATIONSHIPS:
        current = fixture.attributes[spec.referencing_table][spec.lookup_logical_name]
        current.update(MetadataId=IDS[(spec.referencing_table,spec.lookup_logical_name)],EntityLogicalName=spec.referencing_table)
        current["IsAuditEnabled"] = {"Value":False,"CanBeChanged":True,"ManagedPropertyLogicalName":"canmodifyauditsettings"}
        original = copy.deepcopy(current)
        proposed = payload_for(spec,current)
        assert current == original and proposed["IsAuditEnabled"]["Value"] is True
        current["Targets"] = ["unexpected"]
        try: payload_for(spec,current)
        except RuntimeError: pass
        else: raise AssertionError("Non-audit drift was accepted")
    return {"passed":6,"failed":0,"networkCalls":0}

if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--apply",action="store_true")
    parser.add_argument("--self-test",action="store_true")
    args=parser.parse_args()
    if args.apply and args.self_test: parser.error("Self-test cannot apply changes")
    print(json.dumps(self_test() if args.self_test else repair(args.apply),ensure_ascii=True,indent=2))
