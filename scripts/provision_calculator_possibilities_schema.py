"""Provision the durable calculator/proposal hierarchy in Dataverse.

The script is read-only unless --apply is supplied. It is intentionally pinned
to the Digital Tech default environment and the CotizadorInternoCRM solution.
"""

from __future__ import annotations

import argparse
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request

from auth import get_client, get_plugin_headers, get_token, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
EXPECTED_PROFILE = "digital-tech-default"
SOLUTION = "CotizadorInternoCRM"
PREFIX = "cr07a"
TABLE = "cr07a_negocioscomerciales"
SET = "cr07a_negocioscomercialeses"
LANGUAGE = 3082
RELATIONSHIP_SCHEMA = "cr07a_NegociosComerciales_ParentRecord"

RECORD_TYPES = (
    (645250000, "Grupo"),
    (645250001, "Posibilidad"),
    (645250002, "Linea"),
    (645250003, "Exportacion de propuesta"),
)
EXPORT_STATUSES = (
    (645250000, "Cargando"),
    (645250001, "Completada"),
    (645250002, "Fallida"),
)

# schema, label, kind, configuration
COLUMNS = (
    ("cr07a_RecordType", "Tipo de registro", "Choice", RECORD_TYPES),
    ("cr07a_RecordKey", "Llave de registro", "String", 180),
    ("cr07a_GroupId", "Id del escenario", "String", 100),
    ("cr07a_GroupName", "Nombre del escenario", "String", 200),
    ("cr07a_PossibilityName", "Nombre de la posibilidad", "String", 200),
    ("cr07a_PossibilityOrder", "Orden de la posibilidad", "Integer", (1, 3)),
    ("cr07a_IncludeInProposal", "Incluir en propuesta", "Boolean", True),
    ("cr07a_IsRecommended", "Posibilidad recomendada", "Boolean", False),
    ("cr07a_InputHash", "Huella de insumos", "String", 64),
    ("cr07a_LinesHash", "Huella de lineas confirmadas", "String", 64),
    ("cr07a_LineId", "Id de la linea", "String", 100),
    ("cr07a_LineOrder", "Orden de la linea", "Integer", (1, 1000)),
    ("cr07a_PossibilityId", "Id de posibilidad de la linea", "String", 100),
    ("cr07a_LineBusinessType", "Tipo de negocio de la linea", "Integer", (0, 20)),
    ("cr07a_LineProductId", "Id del producto de la linea", "String", 100),
    ("cr07a_LineProductDescription", "Producto de la linea", "String", 500),
    ("cr07a_LineCostUnit", "Costo unitario de la linea", "Decimal", 4),
    ("cr07a_LineMarginPercent", "Margen de la linea", "Decimal", 6),
    ("cr07a_LineContractMonths", "Meses de contrato de la linea", "Integer", (1, 1200)),
    ("cr07a_LineQuantity", "Cantidad de la linea", "Integer", (1, 1000000)),
    ("cr07a_LineSuggestedPrice", "Precio sugerido de la linea", "Decimal", 4),
    ("cr07a_LineAccelerator", "Acelerador de la linea", "Decimal", 6),
    ("cr07a_LineHasVat", "Linea con IVA", "Boolean", False),
    ("cr07a_ResultPoints", "Puntaje calculado", "Decimal", 6),
    ("cr07a_ResultCommission", "Comision calculada", "Decimal", 4),
    ("cr07a_ResultProrationDays", "Dias de prorrateo", "Integer", (0, 10000)),
    ("cr07a_ResultProrationFactor", "Factor de prorrateo", "Decimal", 10),
    ("cr07a_ResultProrationText", "Detalle de prorrateo", "String", 300),
    ("cr07a_ResultMonthlySale", "Venta mensual calculada", "Decimal", 4),
    ("cr07a_ResultTotalSale", "Venta contractual calculada", "Decimal", 4),
    ("cr07a_ExportId", "Id de exportacion", "String", 100),
    ("cr07a_ExportVersion", "Version de exportacion", "Integer", (1, 1000000)),
    ("cr07a_ExportStatus", "Estado de exportacion", "Choice", EXPORT_STATUSES),
    ("cr07a_ExportIdempotency", "Llave de idempotencia", "String", 100),
    ("cr07a_ExportEconomicHash", "Huella economica exportada", "String", 64),
    ("cr07a_ExportConfigurationHash", "Huella de configuracion", "String", 64),
    ("cr07a_ExportPdfHash", "Huella del PDF exportado", "String", 64),
    ("cr07a_ExportFileName", "Nombre del PDF", "String", 180),
    ("cr07a_ExportedByName", "Exportado por", "String", 200),
    ("cr07a_ExportedByEmail", "Correo del exportador", "String", 320),
    ("cr07a_ExportPossibilityCount", "Cantidad de posibilidades", "Integer", (1, 3)),
    ("cr07a_ExportConfigurationFile", "Configuracion de propuesta", "File", 1024),
    ("cr07a_ExportPdfFile", "PDF de propuesta", "File", 10240),
)

KEYS = (
    ("cr07a_NegociosComerciales_RecordKey_Key", ("cr07a_recordkey",)),
    ("cr07a_NegociosComerciales_ScenarioId_Key", ("cr07a_scenarioid",)),
    ("cr07a_NegociosComerciales_GroupSlot_Key", ("cr07a_groupid", "cr07a_possibilityorder")),
    ("cr07a_NegociosComerciales_Line_Key", ("cr07a_possibilityid", "cr07a_lineid")),
    ("cr07a_NegociosComerciales_ExportVersion_Key", ("cr07a_groupid", "cr07a_exportversion")),
    ("cr07a_NegociosComerciales_ExportIdempotency_Key", ("cr07a_groupid", "cr07a_exportidempotency")),
)


def label(text: str) -> dict:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [{
            "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
            "Label": text,
            "LanguageCode": LANGUAGE,
        }],
    }


def required_none() -> dict:
    return {
        "Value": "None",
        "CanBeChanged": True,
        "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings",
    }


def option(value: int, text: str) -> dict:
    return {
        "Value": value,
        "Label": label(text),
    }


def column_payload(schema: str, display: str, kind: str, config) -> dict:
    payload = {
        "SchemaName": schema,
        "DisplayName": label(display),
        "Description": label(display),
        "RequiredLevel": required_none(),
    }
    if kind == "String":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "FormatName": {"Value": "Text"},
            "MaxLength": config,
        })
    elif kind == "Integer":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
            "AttributeType": "Integer",
            "Format": "None",
            "MinValue": config[0],
            "MaxValue": config[1],
        })
    elif kind == "Decimal":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            "AttributeType": "Decimal",
            "MinValue": -100000000000.0,
            "MaxValue": 100000000000.0,
            "Precision": config,
        })
    elif kind == "Boolean":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
            "AttributeType": "Boolean",
            "DefaultValue": bool(config),
            "OptionSet": {
                "TrueOption": option(1, "Si"),
                "FalseOption": option(0, "No"),
            },
        })
    elif kind == "Choice":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
            "AttributeType": "Picklist",
            "OptionSet": {
                "@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata",
                "IsGlobal": False,
                "OptionSetType": "Picklist",
                "Options": [option(value, text) for value, text in config],
            },
        })
    elif kind == "File":
        payload.update({
            "@odata.type": "Microsoft.Dynamics.CRM.FileAttributeMetadata",
            "MaxSizeInKB": config,
        })
    else:
        raise ValueError(f"Unsupported column kind: {kind}")
    return payload


class Api:
    def __init__(self) -> None:
        load_env()
        self.base = os.environ["DATAVERSE_URL"].rstrip("/")
        self.token = get_token()

    def request(self, method: str, path: str, body=None, solution=False, allow_404=False):
        headers = get_plugin_headers("dv-metadata", self.token)
        headers.update({
            "Accept": "application/json",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
        })
        if solution:
            headers["MSCRM.SolutionUniqueName"] = SOLUTION
        data = None
        if body is not None:
            data = json.dumps(body, ensure_ascii=True).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"
        url = path if path.startswith("http") else f"{self.base}/api/data/v9.2/{path}"
        req = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if allow_404 and error.code == 404:
                return None
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"{method} {path} failed HTTP {error.code}: {detail}") from error

    def collection(self, path: str) -> list[dict]:
        rows: list[dict] = []
        next_path = path
        while next_path:
            result = self.request("GET", next_path)
            rows.extend(result.get("value", []))
            next_path = result.get("@odata.nextLink", "")
        return rows

    def attributes(self) -> dict[str, dict]:
        rows = self.collection(
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes"
            "?$select=LogicalName,SchemaName,AttributeType,AttributeTypeName,RequiredLevel",
        )
        return {row["LogicalName"].lower(): row for row in rows}

    def attribute_detail(self, logical_name: str, kind: str) -> dict:
        metadata_type = {
            "String": "StringAttributeMetadata",
            "Integer": "IntegerAttributeMetadata",
            "Decimal": "DecimalAttributeMetadata",
            "Boolean": "BooleanAttributeMetadata",
            "Choice": "PicklistAttributeMetadata",
            "File": "FileAttributeMetadata",
        }[kind]
        suffix = "?$expand=OptionSet($select=Options)" if kind == "Choice" else ""
        return self.request(
            "GET",
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{logical_name}')/"
            f"Microsoft.Dynamics.CRM.{metadata_type}{suffix}",
        )

    def keys(self) -> list[dict]:
        return self.collection(
            f"EntityDefinitions(LogicalName='{TABLE}')/Keys"
            "?$select=SchemaName,KeyAttributes,EntityKeyIndexStatus",
        )

    def relationship(self):
        escaped = urllib.parse.quote(f"SchemaName='{RELATIONSHIP_SCHEMA}'", safe="='_")
        return self.request(
            "GET",
            f"RelationshipDefinitions({escaped})/"
            "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?"
            "$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencedAttribute,ReferencingAttribute",
            allow_404=True,
        )


def verify_context() -> None:
    load_env()
    actual = os.environ.get("DATAVERSE_URL", "")
    if actual.rstrip("/").lower() != EXPECTED_ENVIRONMENT.rstrip("/").lower():
        raise RuntimeError(f"Unexpected Dataverse environment: {actual}")
    if os.environ.get("PAC_AUTH_PROFILE", "") != EXPECTED_PROFILE:
        raise RuntimeError(f"Unexpected PAC profile: {os.environ.get('PAC_AUTH_PROFILE', '')}")
    if os.environ.get("SOLUTION_NAME", "") != SOLUTION:
        raise RuntimeError(f"Unexpected solution: {os.environ.get('SOLUTION_NAME', '')}")
    if os.environ.get("PUBLISHER_PREFIX", "").lower() != PREFIX:
        raise RuntimeError("Unexpected publisher prefix")

    client = get_client("dv-solution")
    solutions = list(client.records.list(
        "solution",
        filter=f"uniquename eq '{SOLUTION}'",
        select=["solutionid", "uniquename", "version", "ismanaged", "_publisherid_value"],
        top=2,
    ).records)
    if len(solutions) != 1 or bool(solutions[0].get("ismanaged")):
        raise RuntimeError(f"Expected one unmanaged solution named {SOLUTION}")
    publisher = client.records.retrieve(
        "publisher",
        solutions[0]["_publisherid_value"],
        select=["customizationprefix"],
    )
    if str(publisher.get("customizationprefix", "")).lower() != PREFIX:
        raise RuntimeError("The selected solution does not use prefix cr07a")
    print(f"Context verified: {actual} | {SOLUTION} | prefix={PREFIX}", flush=True)


def relationship_payload() -> dict:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
        "SchemaName": RELATIONSHIP_SCHEMA,
        "ReferencedEntity": TABLE,
        "ReferencingEntity": TABLE,
        "ReferencedEntityNavigationPropertyName": "cr07a_ParentRecord_Children",
        "ReferencingEntityNavigationPropertyName": "cr07a_ParentRecord",
        "Lookup": {
            "@odata.type": "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
            "SchemaName": "cr07a_ParentRecord",
            "DisplayName": label("Registro padre"),
            "Description": label("Grupo o posibilidad que contiene este registro"),
            "RequiredLevel": required_none(),
        },
        "AssociatedMenuConfiguration": {
            "Behavior": "UseLabel",
            "Group": "Details",
            "Label": label("Registros hijos de calculadora"),
            "Order": 10000,
        },
        "CascadeConfiguration": {
            "Assign": "NoCascade",
            "Delete": "RemoveLink",
            "Merge": "NoCascade",
            "Reparent": "NoCascade",
            "Share": "NoCascade",
            "Unshare": "NoCascade",
        },
    }


def audit_scenario_duplicates(api: Api) -> dict:
    query = urllib.parse.urlencode({
        "$select": "cr07a_scenarioid",
        "$filter": "cr07a_scenarioid ne null",
        "$orderby": "cr07a_scenarioid asc",
    }, safe="$(),= '")
    rows = api.collection(f"{SET}?{query}")
    counts: dict[str, int] = {}
    for row in rows:
        value = str(row.get("cr07a_scenarioid", "")).strip().lower()
        if value:
            counts[value] = counts.get(value, 0) + 1
    duplicates = [value for value, count in counts.items() if count > 1]
    if duplicates:
        raise RuntimeError(f"Duplicate scenario ids block the alternate key: {duplicates[:10]}")
    print(f"Scenario id audit: {len(rows)} rows, no duplicates", flush=True)
    return {"rows": len(rows), "duplicates": []}


def normalize_metadata_decimal(value) -> str:
    if value is None:
        return ""
    text = format(float(value), ".12f").rstrip("0").rstrip(".")
    return "0" if text in ("", "-0") else text


def column_contract(api: Api, logical_name: str, kind: str, config) -> tuple[dict, dict]:
    detail = api.attribute_detail(logical_name, kind)
    if kind == "String":
        return {"kind": kind, "maxLength": config}, {
            "kind": kind,
            "maxLength": int(detail.get("MaxLength") or 0),
        }
    if kind == "Integer":
        return {"kind": kind, "min": config[0], "max": config[1]}, {
            "kind": kind,
            "min": int(detail.get("MinValue") or 0),
            "max": int(detail.get("MaxValue") or 0),
        }
    if kind == "Decimal":
        return {
            "kind": kind,
            "min": "-100000000000",
            "max": "100000000000",
            "precision": config,
        }, {
            "kind": kind,
            "min": normalize_metadata_decimal(detail.get("MinValue")),
            "max": normalize_metadata_decimal(detail.get("MaxValue")),
            "precision": int(detail.get("Precision") or 0),
        }
    if kind == "Boolean":
        return {"kind": kind, "default": bool(config)}, {
            "kind": kind,
            "default": detail.get("DefaultValue"),
        }
    if kind == "Choice":
        expected_values = sorted(value for value, _ in config)
        option_set = detail.get("OptionSet") or {}
        actual_values = sorted(
            int(option["Value"])
            for option in option_set.get("Options", [])
            if option.get("Value") is not None
        )
        return {"kind": kind, "values": expected_values}, {
            "kind": kind,
            "values": actual_values,
        }
    if kind == "File":
        return {"kind": kind, "maxSizeInKB": config}, {
            "kind": kind,
            "maxSizeInKB": int(detail.get("MaxSizeInKB") or 0),
        }
    raise ValueError(f"Unsupported column kind: {kind}")


def column_contract_mismatches(api: Api, attributes: dict[str, dict]) -> list[dict]:
    expected_attribute_types = {
        "String": "String",
        "Integer": "Integer",
        "Decimal": "Decimal",
        "Boolean": "Boolean",
        "Choice": "Picklist",
        # Dataverse exposes file columns as Virtual in AttributeType and as
        # FileAttributeMetadata in the derived metadata endpoint.
        "File": "Virtual",
    }
    mismatches: list[dict] = []
    for schema, _, kind, config in COLUMNS:
        logical = schema.lower()
        attribute = attributes.get(logical)
        if attribute is None:
            continue
        actual_type = str(attribute.get("AttributeType") or "")
        expected_type = expected_attribute_types[kind]
        if actual_type.lower() != expected_type.lower():
            mismatches.append({
                "column": logical,
                "expected": {"attributeType": expected_type},
                "actual": {"attributeType": actual_type},
            })
            continue
        expected, actual = column_contract(api, logical, kind, config)
        expected["requiredLevel"] = "None"
        actual["requiredLevel"] = str(
            (attribute.get("RequiredLevel") or {}).get("Value")
            or ""
        )
        if actual != expected:
            mismatches.append({"column": logical, "expected": expected, "actual": actual})
    return mismatches


def key_contract_mismatches(keys: dict[str, dict]) -> list[dict]:
    mismatches: list[dict] = []
    for schema, expected_attributes in KEYS:
        existing = keys.get(schema)
        if existing is None:
            continue
        actual_attributes = tuple(
            str(value).strip().lower() for value in existing.get("KeyAttributes", [])
        )
        # Dataverse may return KeyAttributes in a different order after the
        # asynchronous index build. Composite-key identity is the attribute
        # set, so order is not a schema mismatch.
        if sorted(actual_attributes) != sorted(expected_attributes):
            mismatches.append({
                "key": schema,
                "expectedAttributes": expected_attributes,
                "actualAttributes": actual_attributes,
            })
    return mismatches


def relationship_contract_mismatch(relationship: dict | None) -> dict:
    if relationship is None:
        return {}
    expected = {
        "ReferencedEntity": TABLE,
        "ReferencingEntity": TABLE,
        "ReferencedAttribute": "cr07a_negocioscomercialesid",
        "ReferencingAttribute": "cr07a_parentrecord",
    }
    actual = {field: str(relationship.get(field) or "") for field in expected}
    differences = {
        field: {"expected": value, "actual": actual[field]}
        for field, value in expected.items()
        if actual[field].lower() != value.lower()
    }
    return differences


def memo_max_length(api: Api, logical_name: str) -> int:
    row = api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{logical_name}')/"
        "Microsoft.Dynamics.CRM.MemoAttributeMetadata?$select=LogicalName,MaxLength",
    )
    return int(row.get("MaxLength", 0))


def describe(api: Api) -> dict:
    attributes = api.attributes()
    keys = {row.get("SchemaName"): row for row in api.keys()}
    relationship = api.relationship()
    return {
        "environment": EXPECTED_ENVIRONMENT,
        "solution": SOLUTION,
        "table": TABLE,
        "missingColumns": [schema.lower() for schema, *_ in COLUMNS if schema.lower() not in attributes],
        "columnMismatches": column_contract_mismatches(api, attributes),
        "relationshipExists": relationship is not None,
        "relationshipMismatch": relationship_contract_mismatch(relationship),
        "missingKeys": [schema for schema, _ in KEYS if schema not in keys],
        "keyMismatches": key_contract_mismatches(keys),
        "legacyMemoMaxLength": {
            name: memo_max_length(api, name)
            for name in ("cr07a_linesjson", "cr07a_lastresultjson")
        },
        "keyStatuses": {
            name: keys[name].get("EntityKeyIndexStatus")
            for name, _ in KEYS if name in keys
        },
    }


def apply_schema(api: Api) -> None:
    attributes = api.attributes()
    for schema, display, kind, config in COLUMNS:
        logical = schema.lower()
        if logical in attributes:
            print(f"Reusing column: {logical}", flush=True)
            continue
        api.request(
            "POST",
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes",
            column_payload(schema, display, kind, config),
            solution=True,
        )
        print(f"Created column: {logical}", flush=True)
        time.sleep(1)

    if api.relationship() is None:
        api.request("POST", "RelationshipDefinitions", relationship_payload(), solution=True)
        print(f"Created relationship: {RELATIONSHIP_SCHEMA}", flush=True)
    else:
        print(f"Reusing relationship: {RELATIONSHIP_SCHEMA}", flush=True)

    for memo in ("cr07a_linesjson", "cr07a_lastresultjson"):
        current = memo_max_length(api, memo)
        if current < 1048576:
            api.request(
                "PATCH",
                f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{memo}')/"
                "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
                {"MaxLength": 1048576},
                solution=True,
            )
            print(f"Expanded memo: {memo} {current} -> 1048576", flush=True)
        else:
            print(f"Reusing memo length: {memo}={current}", flush=True)

    existing_keys = {row.get("SchemaName"): row for row in api.keys()}
    for schema, attributes_for_key in KEYS:
        if schema in existing_keys:
            actual = tuple(str(value).lower() for value in existing_keys[schema].get("KeyAttributes", []))
            if actual != attributes_for_key:
                raise RuntimeError(f"Key {schema} has columns {actual}, expected {attributes_for_key}")
            print(f"Reusing key: {schema}", flush=True)
            continue
        api.request(
            "POST",
            f"EntityDefinitions(LogicalName='{TABLE}')/Keys",
            {
                "SchemaName": schema,
                "DisplayName": label(schema),
                "KeyAttributes": list(attributes_for_key),
            },
            solution=True,
        )
        print(f"Created key: {schema}", flush=True)

    api.request("POST", "PublishAllXml", {})
    for attempt in range(60):
        statuses = {
            row.get("SchemaName"): str(row.get("EntityKeyIndexStatus", ""))
            for row in api.keys()
            if row.get("SchemaName") in {name for name, _ in KEYS}
        }
        failed = [name for name, status in statuses.items() if status.lower() == "failed"]
        if failed:
            raise RuntimeError(f"Alternate keys failed: {failed}")
        if len(statuses) == len(KEYS) and all(status.lower() == "active" for status in statuses.values()):
            print("All calculator alternate keys are active", flush=True)
            break
        print(f"Waiting for alternate keys ({attempt + 1}/60): {statuses}", flush=True)
        time.sleep(5)
    else:
        raise RuntimeError("Calculator alternate keys did not become active")

    api.request("POST", "PublishAllXml", {})


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="Create/update schema. Default is read-only.")
    args = parser.parse_args()
    verify_context()
    api = Api()
    before = describe(api)
    before["scenarioIdAudit"] = audit_scenario_duplicates(api)
    print(json.dumps({"mode": "apply" if args.apply else "dry-run", "before": before}, indent=2))
    if not args.apply:
        return
    if before["columnMismatches"]:
        raise RuntimeError(f"Existing calculator columns do not match the contract: {before['columnMismatches']}")
    if before["keyMismatches"]:
        raise RuntimeError(f"Existing calculator keys do not match the contract: {before['keyMismatches']}")
    if before["relationshipMismatch"]:
        raise RuntimeError(
            f"Existing calculator relationship does not match the contract: {before['relationshipMismatch']}"
        )
    failed_keys = [
        name for name, status in before["keyStatuses"].items()
        if str(status).lower() == "failed"
    ]
    if failed_keys:
        raise RuntimeError(f"Existing calculator alternate keys are failed: {failed_keys}")
    apply_schema(api)
    after = describe(api)
    if (after["missingColumns"] or after["columnMismatches"]
            or after["missingKeys"] or after["keyMismatches"]
            or not after["relationshipExists"] or after["relationshipMismatch"]):
        raise RuntimeError(f"Schema read-back failed: {after}")
    inactive_keys = [
        name for name, status in after["keyStatuses"].items()
        if str(status).lower() != "active"
    ]
    if len(after["keyStatuses"]) != len(KEYS) or inactive_keys:
        raise RuntimeError(f"Alternate-key read-back failed: {after['keyStatuses']}")
    if any(value < 1048576 for value in after["legacyMemoMaxLength"].values()):
        raise RuntimeError(f"Legacy memo read-back failed: {after}")
    print(json.dumps({"mode": "applied", "after": after}, indent=2))


if __name__ == "__main__":
    main()
