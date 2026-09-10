"""Add only existing, approved Activity V2 components to the existing solution.

Read-only plan by default. Never creates schema, publishes, exports, removes
components, or retries an uncertain write. Rerun the plan to reconcile instead.
"""
from __future__ import annotations
import argparse
import json
import re
import shutil
import subprocess
import tempfile
import uuid
from pathlib import Path

ENV = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION = "CopiersMtoFirmadoV2"
SOLUTION_ID = "71b7fcd0-77a2-f111-aaad-70a8a5a95cf5"
PUBLISHER_ID = "2d2cb8bc-77a2-f111-aaad-70a8a5a95cf5"
TABLES = {
    "dtc_copiersactivityv2": "dtc_CopiersActivityV2",
    "dtc_copiersactivityevidencev2": "dtc_CopiersActivityEvidenceV2",
}
FIELDS = {
    "cr07a_movimientosequipos": (
        "dtc_signedreportkey", "dtc_signedfingerprint", "dtc_reference",
        "dtc_originclientkey", "dtc_originclientname"),
    "cr07a_entrega": ("dtc_signedreportkey", "dtc_signedfingerprint", "dtc_reference"),
}


def api(method, path, body=None):
    if method != "GET" and not (method == "POST" and path == "AddSolutionComponent"):
        raise RuntimeError("Only reads and AddSolutionComponent are permitted")
    executable = shutil.which("dataverse")
    if not executable:
        raise RuntimeError("Existing authenticated Dataverse CLI is required")
    launcher = Path(executable).parent / "node_modules/@microsoft/dataverse/bin/dataverse.js"
    command = [shutil.which("node"), str(launcher)] if Path(executable).suffix.lower() == ".cmd" and launcher.is_file() else [executable]
    args = command + ["api", "request", "--target", "dataverse", "--method", method,
        "--path", "/api/data/v9.2/" + path.replace("$", "%24"), "--environment", ENV,
        "--context", "app=dataverse-skills/1.11.3;skill=dv-solution;agent=codex"]
    with tempfile.TemporaryDirectory(prefix="copiers-activity-solution-") as temp:
        if body is not None:
            payload = Path(temp) / "payload.json"
            payload.write_text(json.dumps(body), encoding="utf-8")
            args += ["--body-file", str(payload), "--header", "MSCRM.SolutionUniqueName:" + SOLUTION]
        result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", result.stdout + result.stderr)
    start, end = output.find("{"), output.rfind("}")
    value = json.loads(output[start:end + 1]) if start >= 0 and end >= start else {}
    if result.returncode or "error" in value or (method == "GET" and not value):
        raise RuntimeError(f"Solution {method} {path}: " + output[-1600:])
    return value


def guid(value):
    try:
        return str(uuid.UUID(str(value)))
    except (ValueError, TypeError, AttributeError):
        raise RuntimeError("Missing or invalid component identity") from None


def rows(path):
    result = api("GET", path)
    if not isinstance(result.get("value"), list) or result.get("@odata.nextLink") or result.get("nextLink"):
        raise RuntimeError("Incomplete/paged solution response: " + path)
    return result["value"]


def preflight():
    solution = api("GET", f"solutions({SOLUTION_ID})?$select=solutionid,uniquename,ismanaged,_publisherid_value")
    if (guid(solution.get("solutionid")) != SOLUTION_ID or solution.get("uniquename") != SOLUTION
            or solution.get("ismanaged") is not False or guid(solution.get("_publisherid_value")) != PUBLISHER_ID):
        raise RuntimeError("Approved unmanaged solution identity does not match")
    publisher = api("GET", f"publishers({PUBLISHER_ID})?$select=publisherid,uniquename,customizationprefix")
    if (guid(publisher.get("publisherid")) != PUBLISHER_ID or publisher.get("uniquename") != "DigitalTechCopiers"
            or publisher.get("customizationprefix") != "dtc"):
        raise RuntimeError("Approved solution publisher identity does not match")
    desired, legacy_tables = [], set()
    for logical in (*TABLES, *FIELDS):
        entity = api("GET", f"EntityDefinitions(LogicalName='{logical}')?$select=MetadataId,LogicalName,SchemaName")
        if entity.get("LogicalName") != logical or (logical in TABLES and entity.get("SchemaName") != TABLES[logical]):
            raise RuntimeError("Existing table identity does not match: " + logical)
        identity = guid(entity.get("MetadataId"))
        if logical in TABLES:
            desired.append({"name": logical, "componentType": 1, "componentId": identity})
            continue
        legacy_tables.add(identity)
        names = FIELDS[logical]
        query = " or ".join(f"LogicalName eq '{name}'" for name in names)
        attributes = rows(f"EntityDefinitions(LogicalName='{logical}')/Attributes?$select=MetadataId,LogicalName,SchemaName,AttributeType&$filter={query}")
        by_name = {x.get("LogicalName"): x for x in attributes}
        if len(by_name) != len(attributes) or set(by_name) != set(names):
            raise RuntimeError("All approved columns must already exist: " + logical)
        for name in names:
            attribute = by_name[name]
            if str(attribute.get("SchemaName", "")).lower() != name or attribute.get("AttributeType") != "String":
                raise RuntimeError("Existing column identity/type does not match: " + logical + "." + name)
            desired.append({"name": logical + "." + name, "componentType": 2, "componentId": guid(attribute.get("MetadataId"))})
    if len({x["componentId"] for x in desired}) != 10:
        raise RuntimeError("Approved ten component identities must be distinct")
    return desired, legacy_tables


def membership():
    result = rows("solutioncomponents?$select=solutioncomponentid,componenttype,objectid,rootcomponentbehavior"
                  f"&$filter=_solutionid_value eq {SOLUTION_ID}")
    indexed = {}
    for row in result:
        key = (int(row["componenttype"]), guid(row["objectid"]))
        if key in indexed:
            raise RuntimeError("Duplicate solution component membership")
        indexed[key] = {"id": guid(row["solutioncomponentid"]), "behavior": row.get("rootcomponentbehavior")}
    return indexed


def missing_components(desired, current):
    missing = []
    for item in desired:
        found = current.get((item["componentType"], item["componentId"]))
        if found is None:
            missing.append(item)
        elif item["componentType"] == 1 and found["behavior"] != 0:
            if found["behavior"] not in (1, 2):
                raise RuntimeError("Unknown new-table root inclusion: " + item["name"])
            # Relationships can introduce a shell before its explicit root is
            # added. The approved full root is still missing in that case.
            missing.append(item)
    return missing


def payload(item):
    result = {"ComponentId": item["componentId"], "ComponentType": item["componentType"],
              "SolutionUniqueName": SOLUTION, "AddRequiredComponents": False}
    if item["componentType"] == 1:
        result["DoNotIncludeSubcomponents"] = False
    return result


def assert_preserved(before, after, desired, legacy_tables):
    approved_roots = {x["componentId"] for x in desired if x["componentType"] == 1}
    for key, original in before.items():
        observed = after.get(key)
        expected_upgrade = (key[0] == 1 and key[1] in approved_roots and original["behavior"] in (1, 2)
                            and observed == {"id": original["id"], "behavior": 0})
        if observed != original and not expected_upgrade:
            raise RuntimeError("Existing solution membership was not preserved")
    for identity in legacy_tables:
        key = (1, identity)
        if key not in before and key in after and after[key]["behavior"] != 2:
            raise RuntimeError("Legacy table was included beyond a component-only shell")


def run(apply=False):
    desired, legacy_tables = preflight()
    before = membership()
    missing = missing_components(desired, before)
    added = []
    if apply:
        for item in missing:
            # Fail closed if another actor changes membership after preflight.
            live = membership()
            assert_preserved(before, live, desired, legacy_tables)
            if item not in missing_components([item], live):
                continue
            api("POST", "AddSolutionComponent", payload(item))
            # No automatic retry: an uncertain result must be reconciled by GET.
            readback = membership()
            assert_preserved(before, readback, desired, legacy_tables)
            if missing_components([item], readback):
                raise RuntimeError("AddSolutionComponent did not persist: " + item["name"])
            added.append(item["name"])
        after = membership()
        assert_preserved(before, after, desired, legacy_tables)
        if missing_components(desired, after):
            raise RuntimeError("Solution component registration is incomplete")
    return {"mode": "Apply" if apply else "Plan", "solution": SOLUTION, "solutionId": SOLUTION_ID,
            "publisherId": PUBLISHER_ID, "ready": apply or not missing, "desiredCount": len(desired),
            "missing": missing, "added": added, "preservedBaselineComponents": len(before)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Register only missing approved existing components")
    args = parser.parse_args()
    print(json.dumps(run(args.apply), indent=2))
