"""Add the approved MTO V2 email/reference and widen internal GPS accuracy.

Check-only by default. Uses the authenticated first-party Dataverse CLI with an
explicit pinned environment; never reads credentials or changes auth profiles.
Metadata formats require the managed Web API escape hatch (Email/AutoNumber).
No customer records, existing references, roles, or legacy MTO data are edited.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any

ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION = "CopiersMtoFirmadoV2"
SOLUTION_ID = "71b7fcd0-77a2-f111-aaad-70a8a5a95cf5"
CONTEXT = "app=dataverse-skills/1.11.3;skill=dv-metadata;agent=codex"
ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")


def api(method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
    executable = shutil.which("dataverse")
    if not executable:
        raise RuntimeError("Dataverse CLI is required; reuse the existing profile.")
    # npm installs a .cmd shim on Windows. Python's CreateProcess argument
    # quoting does not quote '&' without spaces, allowing cmd.exe to split an
    # OData URL. Invoke the observed first-party launcher directly, not a shell.
    command = [executable]
    launcher = Path(executable).parent / "node_modules" / "@microsoft" / "dataverse" / "bin" / "dataverse.js"
    if Path(executable).suffix.lower() == ".cmd" and launcher.is_file():
        node = shutil.which("node")
        if not node:
            raise RuntimeError("The installed Dataverse CLI requires its existing Node launcher.")
        command = [node, str(launcher)]
    args = command + ["api", "request", "--target", "dataverse", "--method", method,
            "--path", "/api/data/v9.2/" + path, "--environment", ENVIRONMENT,
            "--context", CONTEXT]
    if body is not None:
        args += ["--body", json.dumps(body, separators=(",", ":")),
                 "--header", "MSCRM.SolutionName:" + SOLUTION]
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    output = ANSI.sub("", result.stdout + result.stderr)
    start, end = output.find("{"), output.rfind("}")
    payload = json.loads(output[start:end + 1]) if start >= 0 and end >= start else {}
    if result.returncode or payload.get("error"):
        raise RuntimeError(f"{method} {path}: {output}")
    return payload


def label(value: str) -> dict[str, Any]:
    return {"LocalizedLabels": [{"Label": value, "LanguageCode": 3082}]}


SPECS = (
    ("cr07a_cliente", "dtc_personaencargadacopiers", {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "dtc_PersonaEncargadaCopiers",
        "DisplayName": label("Persona encargada de copiers"),
        "Description": label("Correo de la persona encargada de Copiers para recibir reportes de mantenimiento."),
        "RequiredLevel": {"Value": "None"},
        "FormatName": {"Value": "Email"}, "MaxLength": 320,
        "IsSecured": False, "IsAuditEnabled": {"Value": True},
    }),
    ("dtc_copiersmtov2", "dtc_reference", {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "dtc_Reference",
        "DisplayName": label("Consecutivo MTO"),
        "Description": label("Referencia automatica asignada al crear un MTO Firmado V2. No editar manualmente."),
        "RequiredLevel": {"Value": "None"},
        "FormatName": {"Value": "Text"}, "MaxLength": 100,
        "AutoNumberFormat": "MTO-{SEQNUM:6}",
        "IsSecured": False, "IsAuditEnabled": {"Value": True},
    }),
)


def column(table: str, name: str) -> dict[str, Any] | None:
    rows = api("GET", f"EntityDefinitions(LogicalName='{table}')/Attributes/"
               "Microsoft.Dynamics.CRM.StringAttributeMetadata?"
               "%24select=LogicalName,SchemaName,MetadataId,MaxLength,FormatName,AutoNumberFormat,IsSecured,IsAuditEnabled,RequiredLevel"
               f"&%24filter=LogicalName eq '{name}'").get("value", [])
    if len(rows) > 1:
        raise RuntimeError(f"Ambiguous metadata: {table}.{name}")
    return rows[0] if rows else None


def validate(actual: dict[str, Any], expected: dict[str, Any], table: str, name: str) -> None:
    fields = ("SchemaName", "MaxLength", "FormatName", "AutoNumberFormat", "IsSecured")
    mismatched = [key for key in fields if actual.get(key) != expected.get(key)]
    if actual.get("RequiredLevel", {}).get("Value") != "None":
        mismatched.append("RequiredLevel")
    if actual.get("IsAuditEnabled", {}).get("Value") is not True:
        mismatched.append("IsAuditEnabled")
    if mismatched:
        raise RuntimeError(f"Incompatible {table}.{name}: {', '.join(mismatched)}; no destructive repair performed.")


def solution_contains(table: str, attribute_id: str) -> bool:
    table_id = api("GET", f"EntityDefinitions(LogicalName='{table}')?%24select=MetadataId")["MetadataId"]
    components = api("GET", "solutioncomponents?%24select=componenttype,objectid,rootcomponentbehavior&"
                     f"%24filter=_solutionid_value eq {SOLUTION_ID} and "
                     f"(objectid eq {attribute_id} or objectid eq {table_id})").get("value", [])
    return any((item["componenttype"] == 2 and item["objectid"] == attribute_id) or
               (item["componenttype"] == 1 and item["objectid"] == table_id and
                item.get("rootcomponentbehavior") == 0) for item in components)


def ensure_accuracy(apply: bool) -> None:
    path = "EntityDefinitions(LogicalName='dtc_copiersmtov2')/Attributes(LogicalName='dtc_accuracymeters')"
    current = api("GET", path + "/Microsoft.Dynamics.CRM.DecimalAttributeMetadata")
    if current.get("MinValue") != 0 or current.get("Precision") != 7 or current.get("IsSecured") is not True:
        raise RuntimeError("Unexpected internal GPS accuracy metadata; refusing an incompatible change.")
    if current.get("MaxValue") not in (250, 20000000):
        raise RuntimeError("Unexpected GPS accuracy maximum; refusing an unreviewed change.")
    if current["MaxValue"] == 20000000:
        print("REUSED dtc_copiersmtov2.dtc_accuracymeters max=20000000 min=0 precision=7 secured=true")
    elif not apply:
        print("PLANNED dtc_copiersmtov2.dtc_accuracymeters max=250 -> 20000000")
    else:
        payload = {key: value for key, value in current.items() if key != "@odata.context"}
        payload["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        payload["MaxValue"] = 20000000
        api("PUT", path, payload)
        after = api("GET", path + "/Microsoft.Dynamics.CRM.DecimalAttributeMetadata")
        preserved = ("SchemaName", "MinValue", "Precision", "IsSecured", "RequiredLevel", "IsAuditEnabled", "DisplayName", "Description")
        if after.get("MaxValue") != 20000000 or any(after.get(key) != current.get(key) for key in preserved):
            raise RuntimeError("GPS accuracy update did not match read-back; inspect before retrying.")
        print("VERIFIED dtc_copiersmtov2.dtc_accuracymeters max=20000000; other contract properties preserved")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    solution = api("GET", "solutions?%24select=solutionid,uniquename&"
                   f"%24filter=uniquename eq '{SOLUTION}'&"
                   "%24expand=publisherid(%24select=customizationprefix)").get("value", [])
    if len(solution) != 1 or solution[0]["solutionid"] != SOLUTION_ID or solution[0]["publisherid"]["customizationprefix"] != "dtc":
        raise RuntimeError("Approved solution/publisher did not match the pinned environment.")
    print(json.dumps({"environment": ENVIRONMENT, "solution": SOLUTION, "apply": args.apply}))
    changes = []
    for table, name, spec in SPECS:
        current = column(table, name)
        if current:
            validate(current, spec, table, name)
            print(f"REUSED {table}.{name}")
        elif not args.apply:
            print(f"PLANNED {table}.{name}")
        else:
            api("POST", f"EntityDefinitions(LogicalName='{table}')/Attributes", spec)
            current = column(table, name)
            if current is None:
                raise RuntimeError(f"Creation not visible for {table}.{name}; verify before retry.")
            validate(current, spec, table, name)
            changes.append(table)
            print(f"CREATED {table}.{name}")
        if current and args.apply:
            if not solution_contains(table, current["MetadataId"]):
                api("POST", "AddSolutionComponent", {
                    "ComponentId": current["MetadataId"], "ComponentType": 2,
                    "SolutionUniqueName": SOLUTION, "AddRequiredComponents": False,
                })
            if not solution_contains(table, current["MetadataId"]):
                raise RuntimeError(f"Solution membership not verified: {table}.{name}")
            print(f"VERIFIED {table}.{name} metadataId={current['MetadataId']} solution={SOLUTION}")
    ensure_accuracy(args.apply)
    if args.apply:
        entities = "".join(f"<entity>{table}</entity>" for table in sorted({spec[0] for spec in SPECS}))
        api("POST", "PublishXml", {"ParameterXml": f"<importexportxml><entities>{entities}</entities></importexportxml>"})
        print("PUBLISHED approved entities only. Existing values and autonumber seed preserved.")
    print("Export/unpack the approved solution after any applied metadata change.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
