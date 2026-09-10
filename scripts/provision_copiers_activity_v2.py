"""Approved, additive activity journal schema in the existing Copiers solution.

Uses the managed Dataverse CLI metadata escape hatch: UserOwned, field security,
exact choices, audit, file sizes and memo limits require advanced metadata control.
Never creates a publisher/solution, edits legacy metadata, or writes business rows.
"""
from __future__ import annotations
import argparse
import copy
import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path
import provision_copiers_mto_v2_dataverse as base

ENV = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION = "CopiersMtoFirmadoV2"
TABLES = {
    "dtc_copiersactivityv2": ("dtc_CopiersActivityV2", "Acta Copiers V2", base.MAIN_COLUMNS),
    "dtc_copiersactivityevidencev2": ("dtc_CopiersActivityEvidenceV2", "Evidencia acta Copiers V2", base.EVIDENCE_COLUMNS),
}
RELATIONSHIPS = (
    base.RelationshipSpec("dtc_Cliente_CopiersActivityV2", "cr07a_cliente", "dtc_copiersactivityv2", "dtc_client", "dtc_Client", "Cliente", True),
    base.RelationshipSpec("dtc_Equipo_CopiersActivityV2", "cr07a_equipo", "dtc_copiersactivityv2", "dtc_equipment", "dtc_Equipment", "Equipo", False),
    base.RelationshipSpec("dtc_CopiersActivityV2_Evidence", "dtc_copiersactivityv2", "dtc_copiersactivityevidencev2", "dtc_signedactivity", "dtc_SignedActivity", "Acta firmada", True),
)
BUSINESS_FIELDS = {
    "cr07a_movimientosequipos": (("dtc_signedreportkey",36),("dtc_signedfingerprint",64),("dtc_reference",100),("dtc_originclientkey",36),("dtc_originclientname",850)),
    "cr07a_entrega": (("dtc_signedreportkey",36),("dtc_signedfingerprint",64),("dtc_reference",100)),
}

def api(method, path, body=None):
    if method not in ("GET", "POST"):
        raise RuntimeError("Only read and additive create/publish operations are permitted")
    executable = shutil.which("dataverse")
    if not executable: raise RuntimeError("Authenticated Dataverse CLI is required")
    launcher = Path(executable).parent / "node_modules" / "@microsoft" / "dataverse" / "bin" / "dataverse.js"
    command = [shutil.which("node"), str(launcher)] if Path(executable).suffix.lower() == ".cmd" and launcher.is_file() else [executable]
    args = command + ["api", "request", "--target", "dataverse", "--method", method,
            "--path", "/api/data/v9.2/" + path.replace("$", "%24"), "--environment", ENV,
            "--context", "app=dataverse-skills/1.11.3;skill=dv-metadata;agent=codex"]
    with tempfile.TemporaryDirectory(prefix="copiers-activity-metadata-") as temp:
        if body is not None:
            body_path = Path(temp) / "payload.json"
            body_path.write_text(json.dumps(body, ensure_ascii=True), encoding="utf-8")
            args += ["--body-file", str(body_path), "--header", "MSCRM.SolutionUniqueName:" + SOLUTION]
        result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    output = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", result.stdout + result.stderr)
    start, end = output.find("{"), output.rfind("}")
    value = json.loads(output[start:end+1]) if start >= 0 and end >= start else {}
    if result.returncode or "error" in value:
        raise RuntimeError(f"Metadata {method} {path}: " + output[-1600:])
    return value

def rows(path):
    result = api("GET", path)
    if not isinstance(result.get("value"), list):
        raise RuntimeError("Incomplete metadata response: " + path)
    # Never silently treat a first page as the complete contract. Metadata sets
    # here are small; a paged response requires explicit reconciliation.
    if result.get("@odata.nextLink") or result.get("nextLink"):
        raise RuntimeError("Paged metadata requires reconciliation: " + path)
    return result["value"]

def columns(logical):
    result = []
    for spec in TABLES[logical][2]:
        p = copy.deepcopy(spec.payload)
        if p.get("OptionSet", {}).get("Options"):
            for index, option in enumerate(p["OptionSet"]["Options"]):
                option["Value"] = 827270000 + index
            if spec.logical_name == "dtc_maintenancetype":
                for option, value, name in zip(p["OptionSet"]["Options"], (827270010,827270011), ("Movimiento de equipo", "Entrega de toner")):
                    option["Value"] = value
                    option["Label"] = base.label(name)
                p["DisplayName"] = base.label("Tipo de atencion")
        result.append(p)
    if logical == "dtc_copiersactivityv2":
        reference = copy.deepcopy(base.string_column("dtc_reference", "dtc_Reference", "Consecutivo del acta", 100, "Consecutivo automatico para actas firmadas.").payload)
        reference["AutoNumberFormat"] = "ACT-{SEQNUM:6}"
        result.append(reference)
    return result

def table_payload(logical):
    schema, display, specs = TABLES[logical]
    table = base.TableSpec(logical, schema, logical + "s", display, display + " - registros",
        "Diario firmado separado de los mantenimientos; movimientos y entregas se registran en sus tablas reales.",
        "dtc_name", "dtc_Name", specs)
    p = base.table_payload(table)
    # File/security metadata is installed after the table exists; Dataverse does
    # not support every advanced attribute in an inline CreateEntity payload.
    return p

def column_specs(logical):
    return tuple(base.ColumnSpec(p["SchemaName"].lower(), p["SchemaName"], "Acta V2",
        p["@odata.type"].split(".")[-1].removesuffix("AttributeMetadata"), p, bool(p.get("IsSecured")))
        for p in columns(logical))

def table_spec(logical):
    schema, display, _ = TABLES[logical]
    return base.TableSpec(logical, schema, logical + "s", display, display, "Acta V2",
        "dtc_name", "dtc_Name", column_specs(logical), optimistic_concurrency_required=True)

def business_specs(logical):
    return tuple(base.string_column(name,name,"Acta V2 - " + name[4:],length,
        "Traza inmutable de la atencion firmada V2.") for name,length in BUSINESS_FIELDS[logical])

def security_field_policy():
    """Local contract only: never infer grants from arbitrary live fields."""
    return {logical: sorted(spec.logical_name for spec in column_specs(logical) if spec.secured) for logical in TABLES}

def read_attributes(logical, specs):
    current = rows(f"EntityDefinitions(LogicalName='{logical}')/Attributes?$select=LogicalName,SchemaName,AttributeType,IsSecured")
    known = {x["LogicalName"]:x for x in current}
    if len(known) != len(current): raise RuntimeError("Duplicate attribute metadata: " + logical)
    typed = {}
    for kind in sorted({spec.attribute_type for spec in specs}):
        cast = base.ATTRIBUTE_METADATA_CASTS[kind]
        suffix = "?$expand=OptionSet" if kind == "Picklist" else ""
        for item in rows(f"EntityDefinitions(LogicalName='{logical}')/Attributes/Microsoft.Dynamics.CRM.{cast}{suffix}"):
            typed[item["LogicalName"]] = item
    return known, typed

def verify_column(logical, spec, actual):
    table = table_spec(logical) if logical in TABLES else base.TableSpec(logical, logical, "", "", "", "", "", "", ())
    differences = base.column_contract_differences(table, spec, actual)
    if actual is not None:
        # Navigation and schema casing is contractual in @odata.bind payloads.
        if actual.get("SchemaName") != spec.schema_name:
            differences.append("SchemaName casing differs")
        if "AutoNumberFormat" in spec.payload and actual.get("AutoNumberFormat") != spec.payload["AutoNumberFormat"]:
            differences.append("AutoNumberFormat differs")
        if spec.attribute_type == "Picklist":
            expected_options = spec.payload["OptionSet"]["Options"]
            actual_set = actual.get("OptionSet") or {}
            actual_options = actual_set.get("Options") or []
            def choices(options):
                return [(item.get("Value"), sorted((label.get("LanguageCode"), label.get("Label"))
                    for label in (item.get("Label") or {}).get("LocalizedLabels", []))) for item in options]
            if actual_set.get("IsGlobal") is not False or sorted(choices(actual_options)) != sorted(choices(expected_options)):
                differences.append("Local choice values/labels differ")
    if differences:
        raise RuntimeError("Incompatible column " + logical + "." + spec.logical_name + ": " + "; ".join(differences))

def key_contract(logical, keys):
    key_field = "dtc_operationkey" if logical == "dtc_copiersactivityv2" else "dtc_evidencekey"
    key_name = TABLES[logical][0] + "Key"
    matching = [key for key in keys if key.get("SchemaName") == key_name or key.get("KeyAttributes") == [key_field]]
    if not matching: return key_name, key_field, False
    if len(matching) != 1 or matching[0].get("SchemaName") != key_name or matching[0].get("KeyAttributes") != [key_field]:
        raise RuntimeError("Incompatible alternate key " + logical)
    if matching[0].get("EntityKeyIndexStatus") != "Active":
        raise RuntimeError("Alternate key is not Active: " + logical + "; revalidate without recreating it")
    return key_name, key_field, True

def build_plan():
    solutions = rows("solutions?$select=solutionid,uniquename,_publisherid_value&$filter=uniquename eq '" + SOLUTION + "'")
    if len(solutions) != 1: raise RuntimeError("Approved existing solution not found uniquely")
    publisher = api("GET", f"publishers({solutions[0]['_publisherid_value']})?$select=uniquename,customizationprefix")
    if publisher.get("uniquename") != "DigitalTechCopiers" or publisher.get("customizationprefix") != "dtc":
        raise RuntimeError("Existing solution publisher does not match approved dtc publisher")
    plan = []
    known_tables = {}
    for logical in TABLES:
        table = table_spec(logical)
        existing = rows(f"EntityDefinitions?$select=LogicalName,SchemaName,MetadataId,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute,OwnershipType,IsAuditEnabled,IsOptimisticConcurrencyEnabled,HasNotes&$filter=LogicalName eq '{logical}'")
        if len(existing) > 1: raise RuntimeError("Duplicate table metadata " + logical)
        if not existing:
            plan.append(("Create " + logical, "EntityDefinitions", table_payload(logical)))
            known, typed, keys = {}, {}, []
        else:
            differences = base.table_contract_differences(table, existing[0])
            if existing[0].get("SchemaName") != table.schema_name: differences.append("SchemaName casing differs")
            if differences: raise RuntimeError("Incompatible table " + logical + ": " + "; ".join(differences))
            specs = (base.primary_name_spec(table),) + table.columns
            known, typed = read_attributes(logical, specs)
            verify_column(logical, specs[0], typed.get(specs[0].logical_name))
            secured = {name for name,item in known.items() if item.get("IsSecured") is True}
            expected_secured = {spec.logical_name for spec in table.columns if spec.secured}
            if secured - expected_secured: raise RuntimeError("Unexpected secured fields in " + logical + ": " + ",".join(sorted(secured-expected_secured)))
            keys = rows(f"EntityDefinitions(LogicalName='{logical}')/Keys?$select=SchemaName,KeyAttributes,EntityKeyIndexStatus")
        known_tables[logical] = known
        for spec in table.columns:
            if spec.logical_name in known: verify_column(logical, spec, typed.get(spec.logical_name))
            else: plan.append(("Create column " + logical + "." + spec.logical_name, f"EntityDefinitions(LogicalName='{logical}')/Attributes", spec.payload))
        key_name, key_field, present = key_contract(logical, keys)
        if not present: plan.append(("Create key " + logical, f"EntityDefinitions(LogicalName='{logical}')/Keys",
            {"SchemaName":key_name,"DisplayName":base.label("Clave unica"),"KeyAttributes":[key_field]}))
    for spec in RELATIONSHIPS:
        found = rows(f"RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$filter=SchemaName eq '{spec.schema_name}'")
        if not found:
            if spec.lookup_logical_name in known_tables[spec.referencing_table]:
                raise RuntimeError("Lookup exists without its expected relationship: " + spec.schema_name)
            plan.append(("Create relationship " + spec.schema_name,"RelationshipDefinitions",base.relationship_payload(spec)))
        else:
            if len(found) != 1: raise RuntimeError("Duplicate relationship " + spec.schema_name)
            differences = base.relationship_contract_differences(spec, found[0])
            for prop, value in (("SchemaName",spec.schema_name),("ReferencingEntityNavigationPropertyName",spec.lookup_schema_name),("ReferencedEntityNavigationPropertyName",spec.schema_name)):
                if found[0].get(prop) != value: differences.append(prop + " exact casing differs")
            if found[0].get("ReferencedAttribute") != spec.referenced_table + "id": differences.append("Referenced primary ID differs")
            lookup = api("GET", base.lookup_attribute_metadata_path(spec, typed=True))
            differences += base.relationship_lookup_contract_differences(spec, lookup)
            if lookup.get("SchemaName") != spec.lookup_schema_name: differences.append("Lookup SchemaName casing differs")
            if differences: raise RuntimeError("Incompatible relationship " + spec.schema_name + ": " + "; ".join(differences))
    for logical in BUSINESS_FIELDS:
        specs = business_specs(logical)
        known, typed = read_attributes(logical, specs)
        for spec in specs:
            if spec.logical_name in known: verify_column(logical, spec, typed.get(spec.logical_name))
            else: plan.append(("Create column " + logical + "." + spec.logical_name,f"EntityDefinitions(LogicalName='{logical}')/Attributes",spec.payload))
    return plan

def ensure_schema(apply):
    # Complete preflight before the first write. A partial/uncertain create is
    # reconciled on the next run, never blindly retried or deleted.
    plan = build_plan()
    if apply:
        for description,path,payload in plan:
            api("POST",path,payload)
            print(description,flush=True)
        if plan:
            entities = "".join("<entity>" + n + "</entity>" for n in list(TABLES)+list(BUSINESS_FIELDS))
            api("POST", "PublishXml", {"ParameterXml":"<importexportxml><entities>"+entities+"</entities></importexportxml>"})
        remaining = build_plan()
        if remaining: raise RuntimeError("Read-back is incomplete; reconcile without duplicating: " + ", ".join(action[0] for action in remaining))
    return {"environment":ENV,"solution":SOLUTION,"mode":"apply" if apply else "plan", "ready":apply or not plan,"actions":[action[0] for action in plan]}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--security-policy", action="store_true")
    args = parser.parse_args()
    if args.security_policy and args.apply: parser.error("--security-policy is local read-only")
    print(json.dumps(security_field_policy() if args.security_policy else ensure_schema(args.apply), ensure_ascii=True, indent=2))
