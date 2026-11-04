"""Add only the counter activity choice to the existing signed-activity table.

Uses the authenticated Dataverse CLI, existing environment and solution.
Default is read-only. No business rows, tables, permissions or flows are created.
InsertOptionValue: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/insertoptionvalue
"""
import argparse
import base64
import json
from pathlib import Path
from provision_copiers_activity_v2 import api, rows, SOLUTION, ENV

TABLE = "dtc_copiersactivityv2"
FIELD = "dtc_maintenancetype"
VALUE = 827270012
LABEL = "Toma de contador"
PATH = (f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{FIELD}')/"
        "Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet")


def read_options():
    metadata = api("GET", PATH)
    options = metadata.get("OptionSet", {})
    if metadata.get("LogicalName") != FIELD or options.get("IsGlobal") is not False:
        raise RuntimeError("Expected the existing local activity choice")
    return options["Options"]


def validate(options):
    values = {option["Value"]: option for option in options}
    if len(values) != len(options) or set(values) - {827270010, 827270011, VALUE}:
        raise RuntimeError("Unexpected activity options; reconcile before changing metadata")
    expected = {827270010: "Movimiento de equipo", 827270011: "Entrega de toner", VALUE: LABEL}
    for value, label in expected.items():
        if value == VALUE and value not in values:
            continue
        labels = values.get(value, {}).get("Label", {}).get("LocalizedLabels", [])
        if not any(item["LanguageCode"] == 3082 and item["Label"] == label for item in labels):
            raise RuntimeError(f"Conflicting activity choice {value}")
    return VALUE in values


def run(apply=False):
    solution = rows("solutions?$select=solutionid,_publisherid_value&$filter=uniquename eq '" + SOLUTION + "'")
    if len(solution) != 1:
        raise RuntimeError("Existing Copiers solution is not unique")
    publisher = api("GET", f"publishers({solution[0]['_publisherid_value']})?$select=uniquename,customizationprefix")
    if publisher.get("uniquename") != "DigitalTechCopiers" or publisher.get("customizationprefix") != "dtc":
        raise RuntimeError("Unexpected solution publisher")
    before = read_options()
    present = validate(before)
    if apply:
        if not present:
            result = api("POST", "InsertOptionValue", {
                "EntityLogicalName": TABLE, "AttributeLogicalName": FIELD, "Value": VALUE,
                "Label": {"LocalizedLabels": [{"Label": LABEL, "LanguageCode": 3082}]},
                "SolutionUniqueName": SOLUTION
            })
            if result.get("NewOptionValue") != VALUE:
                raise RuntimeError("Uncertain insert result; inspect metadata before retrying")
        api("POST", "PublishXml", {"ParameterXml": f"<importexportxml><entities><entity>{TABLE}</entity></entities></importexportxml>"})
        after = read_options()
        if not validate(after):
            raise RuntimeError("Counter choice is absent after publication")
        for option in before:
            if option not in after:
                raise RuntimeError("An existing option changed unexpectedly")
    return {"environment": ENV, "solution": SOLUTION, "table": TABLE, "field": FIELD,
            "value": VALUE, "label": LABEL, "alreadyPresent": present, "applied": apply}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--export", type=Path, help="Export the existing solution to this new ZIP path (no schema writes)")
    args = parser.parse_args()
    if args.export:
        if args.apply or args.export.exists():
            raise RuntimeError("Export requires a new path and cannot be combined with --apply")
        result = api("POST", "ExportSolution", {"SolutionName": SOLUTION, "Managed": False}, skill="dv-solution")
        content = base64.b64decode(result["ExportSolutionFile"], validate=True)
        if not content.startswith(b"PK"):
            raise RuntimeError("Export did not return a solution ZIP")
        args.export.write_bytes(content)
        print(json.dumps({"solution": SOLUTION, "export": str(args.export), "bytes": len(content)}))
    else:
        print(json.dumps(run(args.apply), ensure_ascii=True, indent=2))
