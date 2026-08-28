"""Provision and verify the monthly bank opening-balance table.

The script is fail-closed and dry-run by default. Run with --apply only after
PAC has been verified against Digital Tech Copiers (default).
"""

import argparse
import json
import os
import time
import urllib.error
import urllib.request

from auth import get_client, get_plugin_headers, get_token, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
EXPECTED_PAC_PROFILE = "digital-tech-default"
SOLUTION = "CotizadorInternoCRM"
PREFIX = "cr07a"
LANGUAGE_CODE = 3082
TABLE_SCHEMA = "cr07a_CierreFlujoCajaBanco"
TABLE = "cr07a_cierreflujocajabanco"
KEY_SCHEMA = "cr07a_CierreFlujoCajaBancoClaveExternaKey"

COLUMNS = (
    ("cr07a_ClaveExterna", "Clave externa", "String", 200),
    ("cr07a_PeriodoKey", "Periodo", "String", 7),
    ("cr07a_OrigenFlujo", "Origen flujo", "String", 50),
    ("cr07a_BancoCuentaCodigo", "Codigo cuenta bancaria", "String", 50),
    ("cr07a_BancoCuentaNombre", "Nombre cuenta bancaria", "String", 250),
    ("cr07a_SaldoInicial", "Saldo inicial", "Decimal", 2),
)


def label(text):
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [{
            "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
            "Label": text,
            "LanguageCode": LANGUAGE_CODE,
        }],
    }


def required_none():
    return {
        "Value": "None",
        "CanBeChanged": True,
        "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings",
    }


class MetadataApi:
    def __init__(self):
        load_env()
        self.base_url = os.environ["DATAVERSE_URL"].rstrip("/")
        self.token = get_token()

    def request(self, method, relative_url, body=None, solution=False):
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

        request = urllib.request.Request(
            f"{self.base_url}/api/data/v9.2/{relative_url}",
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(request) as response:
                payload = response.read()
                return json.loads(payload) if payload else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"Dataverse metadata request failed: {method} {relative_url} "
                f"HTTP {error.code}: {detail}"
            ) from error

    def get(self, relative_url):
        return self.request("GET", relative_url)

    def entity(self):
        payload = self.get(
            f"EntityDefinitions(LogicalName='{TABLE}')"
            "?$select=MetadataId,LogicalName,SchemaName,EntitySetName,OwnershipType"
        )
        return payload

    def entity_or_none(self):
        try:
            return self.entity()
        except RuntimeError as error:
            if "HTTP 404" in str(error):
                return None
            raise

    def attributes(self):
        payload = self.get(
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes"
            "?$select=LogicalName,SchemaName,AttributeType"
        )
        return {
            item["LogicalName"].lower(): item
            for item in payload.get("value", [])
            if item.get("LogicalName")
        }

    def keys(self):
        payload = self.get(
            f"EntityDefinitions(LogicalName='{TABLE}')/Keys"
            "?$select=LogicalName,SchemaName,EntityKeyIndexStatus,KeyAttributes"
        )
        return payload.get("value", [])

    def publish(self):
        self.request("POST", "PublishAllXml", {})


def flatten(query_result):
    return list(query_result.records)


def verify_context():
    load_env()
    environment = os.environ.get("DATAVERSE_URL", "")
    if environment.rstrip("/").lower() != EXPECTED_ENVIRONMENT.rstrip("/").lower():
        raise RuntimeError(f"Unexpected Dataverse environment: {environment}")
    if os.environ.get("PAC_AUTH_PROFILE", "") != EXPECTED_PAC_PROFILE:
        raise RuntimeError(
            f"Unexpected PAC profile: {os.environ.get('PAC_AUTH_PROFILE', '')}"
        )
    if os.environ.get("SOLUTION_NAME", "") != SOLUTION:
        raise RuntimeError(f"Unexpected solution: {os.environ.get('SOLUTION_NAME', '')}")
    if os.environ.get("PUBLISHER_PREFIX", "").lower() != PREFIX:
        raise RuntimeError(
            f"Unexpected publisher prefix: {os.environ.get('PUBLISHER_PREFIX', '')}"
        )

    client = get_client("dv-solution")
    solutions = flatten(client.records.list(
        "solution",
        filter=f"uniquename eq '{SOLUTION}'",
        select=[
            "solutionid",
            "uniquename",
            "friendlyname",
            "version",
            "ismanaged",
            "_publisherid_value",
        ],
        top=2,
    ))
    if len(solutions) != 1:
        raise RuntimeError(f"Expected one solution named {SOLUTION}")
    solution = solutions[0]
    if bool(solution.get("ismanaged")):
        raise RuntimeError(f"Solution {SOLUTION} is managed")

    publisher = client.records.retrieve(
        "publisher",
        solution["_publisherid_value"],
        select=["publisherid", "uniquename", "customizationprefix"],
    )
    if not publisher or str(publisher.get("customizationprefix", "")).lower() != PREFIX:
        raise RuntimeError("The solution publisher prefix is not cr07a")

    print(
        "Context verified: "
        f"{environment} | {solution['uniquename']} {solution.get('version', '')} "
        f"| prefix={publisher.get('customizationprefix')}",
        flush=True,
    )


def primary_name_payload():
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "AttributeType": "String",
        "AttributeTypeName": {"Value": "StringType"},
        "SchemaName": "cr07a_Name",
        "DisplayName": label("Nombre"),
        "Description": label("Nombre del saldo bancario mensual"),
        "IsPrimaryName": True,
        "RequiredLevel": required_none(),
        "MaxLength": 200,
        "FormatName": {"Value": "Text"},
    }


def table_payload():
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "Attributes": [primary_name_payload()],
        "SchemaName": TABLE_SCHEMA,
        "DisplayName": label("Cierre flujo caja banco"),
        "DisplayCollectionName": label("Cierres flujo caja banco"),
        "Description": label("Saldo inicial mensual por cuenta bancaria"),
        "OwnershipType": "UserOwned",
        "IsActivity": False,
        "HasActivities": False,
        "HasNotes": True,
        "EntitySetName": "cr07a_cierreflujocajabancos",
    }


def column_payload(schema_name, display_name, kind, size):
    common = {
        "SchemaName": schema_name,
        "DisplayName": label(display_name),
        "Description": label(display_name),
        "RequiredLevel": required_none(),
    }
    if kind == "String":
        common.update({
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": "Text"},
            "MaxLength": size,
        })
        return common
    if kind == "Decimal":
        common.update({
            "@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            "AttributeType": "Decimal",
            "AttributeTypeName": {"Value": "DecimalType"},
            "MinValue": -100_000_000_000,
            "MaxValue": 100_000_000_000,
            "Precision": size,
        })
        return common
    raise RuntimeError(f"Unsupported column kind: {kind}")


def key_payload():
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityKeyMetadata",
        "SchemaName": KEY_SCHEMA,
        "DisplayName": label("Cierre banco por clave externa"),
        "KeyAttributes": ["cr07a_claveexterna"],
    }


def inspect_plan(api):
    table = api.entity_or_none()
    if not table:
        return {
            "table": "create",
            "columns": [item[0].lower() for item in COLUMNS],
            "key": KEY_SCHEMA,
        }

    attributes = api.attributes()
    keys = api.keys()
    missing_columns = [
        schema_name.lower()
        for schema_name, _, _, _ in COLUMNS
        if schema_name.lower() not in attributes
    ]
    matching_key = next(
        (item for item in keys if item.get("SchemaName") == KEY_SCHEMA),
        None,
    )
    return {
        "table": "reuse",
        "columns": missing_columns,
        "key": (
            f"reuse:{matching_key.get('EntityKeyIndexStatus')}"
            if matching_key
            else KEY_SCHEMA
        ),
    }


def wait_for_entity(api, attempts=24):
    for _ in range(attempts):
        entity = api.entity_or_none()
        if entity:
            return entity
        time.sleep(5)
    raise RuntimeError(f"Table {TABLE} was not readable after creation")


def wait_for_key(api, attempts=60):
    for attempt in range(attempts):
        key = next(
            (item for item in api.keys() if item.get("SchemaName") == KEY_SCHEMA),
            None,
        )
        if key:
            status = str(key.get("EntityKeyIndexStatus", ""))
            print(f"Key status: {status} (check {attempt + 1})", flush=True)
            if status.lower() == "active":
                return key
            if status.lower() == "failed":
                raise RuntimeError(f"Alternate key {KEY_SCHEMA} failed")
        time.sleep(5)
    raise RuntimeError(f"Alternate key {KEY_SCHEMA} did not become active")


def apply_schema(api):
    if not api.entity_or_none():
        api.request("POST", "EntityDefinitions", table_payload(), solution=True)
        print(f"Created table: {TABLE}", flush=True)
        wait_for_entity(api)
    else:
        print(f"Reusing table: {TABLE}", flush=True)

    attributes = api.attributes()
    for schema_name, display_name, kind, size in COLUMNS:
        logical_name = schema_name.lower()
        existing = attributes.get(logical_name)
        if existing:
            actual_type = str(existing.get("AttributeType", ""))
            if actual_type.lower() != kind.lower():
                raise RuntimeError(
                    f"{TABLE}.{logical_name} is {actual_type}; expected {kind}"
                )
            print(f"Reusing column: {logical_name}", flush=True)
            continue

        api.request(
            "POST",
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes",
            column_payload(schema_name, display_name, kind, size),
            solution=True,
        )
        print(f"Created column: {logical_name}", flush=True)
        time.sleep(2)

    api.publish()
    existing_key = next(
        (item for item in api.keys() if item.get("SchemaName") == KEY_SCHEMA),
        None,
    )
    if existing_key:
        expected = [str(value).lower() for value in existing_key.get("KeyAttributes", [])]
        if expected != ["cr07a_claveexterna"]:
            raise RuntimeError(f"Alternate key {KEY_SCHEMA} has unexpected columns")
        print(f"Reusing alternate key: {KEY_SCHEMA}", flush=True)
    else:
        api.request(
            "POST",
            f"EntityDefinitions(LogicalName='{TABLE}')/Keys",
            key_payload(),
            solution=True,
        )
        print(f"Created alternate key: {KEY_SCHEMA}", flush=True)
    wait_for_key(api)
    api.publish()


def verify_schema(api):
    entity = api.entity()
    if entity.get("EntitySetName") != "cr07a_cierreflujocajabancos":
        raise RuntimeError(f"Unexpected entity set: {entity.get('EntitySetName')}")
    attributes = api.attributes()
    missing = [
        schema_name.lower()
        for schema_name, _, _, _ in COLUMNS
        if schema_name.lower() not in attributes
    ]
    if missing:
        raise RuntimeError(f"Missing columns after verification: {missing}")
    key = wait_for_key(api, attempts=1)
    report = {
        "environment": EXPECTED_ENVIRONMENT,
        "solution": SOLUTION,
        "table": TABLE,
        "entity_set": entity.get("EntitySetName"),
        "columns": sorted(schema_name.lower() for schema_name, _, _, _ in COLUMNS),
        "alternate_key": key.get("SchemaName"),
        "key_status": key.get("EntityKeyIndexStatus"),
    }
    print(json.dumps(report, indent=2, ensure_ascii=True), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    verify_context()
    api = MetadataApi()
    plan = inspect_plan(api)
    print(json.dumps({"apply": args.apply, "plan": plan}, indent=2), flush=True)
    if not args.apply:
        return

    apply_schema(api)
    verify_schema(api)


if __name__ == "__main__":
    main()
