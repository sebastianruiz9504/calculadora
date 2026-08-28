"""Provision and verify the Cuenta de Cobro expense fields in Dataverse.

The script is fail-closed and dry-run by default. Use --apply only after the
target PAC profile, environment, solution, and publisher have been verified.
"""

import argparse
import json
import os
import time
import urllib.error
import urllib.request

from auth import get_client, get_plugin_headers, get_token, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
SOLUTION = "CotizadorInternoCRM"
TABLE = "cr07a_gastodelaempresa"
LANGUAGE_CODE = 3082

CONTRACTS = (
    {
        "schema_suffix": "ExcelKey",
        "logical_suffix": "excelkey",
        "label": "ExcelKey DIAN",
        "types": ("String",),
        "max_length": 200,
    },
    {
        "schema_suffix": "CuentaContableCodigo",
        "logical_suffix": "cuentacontablecodigo",
        "label": "Cuenta contable codigo",
        "types": ("String",),
        "max_length": 50,
    },
    {
        "schema_suffix": "CuentaContableNombre",
        "logical_suffix": "cuentacontablenombre",
        "label": "Cuenta contable nombre",
        "types": ("String",),
        "max_length": 250,
    },
    {
        "schema_suffix": "EstadoAutomatizacion",
        "logical_suffix": "estadoautomatizacion",
        "label": "Estado automatizacion",
        "types": ("String",),
        "max_length": 100,
    },
    {
        "schema_suffix": "MotivoRevision",
        "logical_suffix": "motivorevision",
        "label": "Motivo revision",
        "types": ("Memo",),
        "max_length": 4000,
    },
    {
        "schema_suffix": "RetencionesJson",
        "logical_suffix": "retencionesjson",
        "label": "Detalle de retenciones",
        "types": ("Memo",),
        "max_length": 100000,
    },
    {
        "schema_suffix": "IVA",
        "logical_suffix": "iva",
        "label": "IVA",
        "types": ("Decimal", "Money"),
        "precision": 2,
    },
    {
        "schema_suffix": "SiigoDocumentId",
        "logical_suffix": "siigodocumentid",
        "label": "Siigo document id",
        "types": ("String",),
        "max_length": 150,
    },
    {
        "schema_suffix": "SiigoDocumentName",
        "logical_suffix": "siigodocumentname",
        "label": "Siigo document name",
        "types": ("String",),
        "max_length": 150,
    },
    {
        "schema_suffix": "SiigoPaymentId",
        "logical_suffix": "siigopaymentid",
        "label": "Siigo payment id",
        "types": ("String",),
        "max_length": 150,
    },
    {
        "schema_suffix": "SiigoPaymentName",
        "logical_suffix": "siigopaymentname",
        "label": "Siigo payment name",
        "types": ("String",),
        "max_length": 150,
    },
    {
        "schema_suffix": "SiigoRespuesta",
        "logical_suffix": "siigorespuesta",
        "label": "Respuesta documento Siigo",
        "types": ("Memo",),
        "max_length": 100000,
    },
    {
        "schema_suffix": "SiigoPaymentResponse",
        "logical_suffix": "siigopaymentresponse",
        "label": "Respuesta pago Siigo",
        "types": ("Memo",),
        "max_length": 100000,
    },
)

REQUIRED_ACTIVE_KEYS = {
    "cr07a_gastoempresadianexcelkey": ("cr07a_excelkey",),
}

TYPE_CASTS = {
    "String": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
    "Memo": "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
    "Decimal": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
    "Money": "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
}


def flatten(pages):
    return [item for page in pages for item in page]


def label(text):
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [{
            "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
            "Label": text,
            "LanguageCode": LANGUAGE_CODE,
        }],
    }


class DataverseMetadataApi:
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
        retry_delays = (0, 3, 5, 10, 15)
        for attempt, delay in enumerate(retry_delays, start=1):
            if delay:
                time.sleep(delay)
            try:
                with urllib.request.urlopen(request) as response:
                    payload = response.read()
                    return json.loads(payload) if payload else None
            except urllib.error.HTTPError as error:
                response_body = error.read().decode("utf-8", errors="replace")
                transient = (
                    error.code in (408, 429, 500, 502, 503, 504)
                    or "0x80040216" in response_body
                    or "customization operation is running" in response_body.lower()
                    or "metadatacache" in response_body.lower()
                )
                if transient and attempt < len(retry_delays):
                    continue
                raise RuntimeError(
                    f"Dataverse metadata request failed: {method} {relative_url} "
                    f"HTTP {error.code}: {response_body}"
                ) from error

    def get(self, relative_url):
        return self.request("GET", relative_url)

    def create_attribute(self, payload):
        return self.request(
            "POST",
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes",
            payload,
            solution=True,
        )

    def update_attribute(self, logical_name, attribute):
        headers_attribute = dict(attribute)
        headers_attribute.pop("@odata.context", None)
        return self.request(
            "PUT",
            f"EntityDefinitions(LogicalName='{TABLE}')"
            f"/Attributes(LogicalName='{logical_name}')",
            headers_attribute,
            solution=True,
        )

    def publish_all(self):
        return self.request("POST", "PublishAllXml", {}, solution=False)


def get_solution_context(client):
    solutions = flatten(client.records.get(
        "solution",
        filter=f"uniquename eq '{SOLUTION}'",
        select=[
            "solutionid",
            "uniquename",
            "friendlyname",
            "version",
            "_publisherid_value",
        ],
        top=1,
    ))
    if len(solutions) != 1:
        raise RuntimeError(f"Expected one solution named {SOLUTION}")
    solution = solutions[0]
    publisher_id = solution.get("_publisherid_value")
    publisher = client.records.get("publisher", publisher_id)
    prefix = (publisher.get("customizationprefix") or "").strip().lower()
    if not prefix:
        raise RuntimeError(f"Solution {SOLUTION} has no publisher prefix")
    return solution, publisher, prefix


def get_attribute_summaries(api):
    payload = api.get(
        f"EntityDefinitions(LogicalName='{TABLE}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType,AttributeTypeName"
    )
    return {
        item["LogicalName"]: item
        for item in payload.get("value", [])
        if item.get("LogicalName")
    }


def get_attribute_detail(api, logical_name, attribute_type, full=False):
    cast = TYPE_CASTS[attribute_type]
    suffix = "" if full else {
        "String": "?$select=MetadataId,LogicalName,SchemaName,MaxLength",
        "Memo": "?$select=MetadataId,LogicalName,SchemaName,MaxLength",
        "Decimal": "?$select=MetadataId,LogicalName,SchemaName,Precision",
        "Money": "?$select=MetadataId,LogicalName,SchemaName,Precision,PrecisionSource",
    }[attribute_type]
    return api.get(
        f"EntityDefinitions(LogicalName='{TABLE}')"
        f"/Attributes(LogicalName='{logical_name}')/{cast}{suffix}"
    )


def get_keys(api):
    payload = api.get(
        f"EntityDefinitions(LogicalName='{TABLE}')/Keys"
        "?$select=LogicalName,SchemaName,EntityKeyIndexStatus,KeyAttributes"
    )
    return {
        item["LogicalName"]: item
        for item in payload.get("value", [])
        if item.get("LogicalName")
    }


def create_payload(contract, prefix):
    schema_name = f"{prefix}_{contract['schema_suffix']}"
    common = {
        "SchemaName": schema_name,
        "DisplayName": label(contract["label"]),
        "Description": label(contract["label"]),
        "RequiredLevel": {
            "Value": "None",
            "CanBeChanged": True,
            "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings",
        },
    }
    expected_type = contract["types"][0]
    if expected_type == "String":
        common.update({
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": "Text"},
            "MaxLength": contract["max_length"],
        })
        return common
    if expected_type == "Memo":
        common.update({
            "@odata.type": "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
            "AttributeType": "Memo",
            "AttributeTypeName": {"Value": "MemoType"},
            "Format": "TextArea",
            "ImeMode": "Disabled",
            "IsLocalizable": False,
            "MaxLength": contract["max_length"],
        })
        return common
    raise RuntimeError(
        f"Creation is not allowed for unsupported contract type: {expected_type}"
    )


def build_plan(api, prefix):
    attributes = get_attribute_summaries(api)
    plan = []
    for contract in CONTRACTS:
        logical_name = f"{prefix}_{contract['logical_suffix']}".lower()
        existing = attributes.get(logical_name)
        if existing is None:
            if contract["types"][0] not in ("String", "Memo"):
                raise RuntimeError(
                    f"Required numeric column is missing and will not be inferred: "
                    f"{logical_name}"
                )
            plan.append({
                "action": "create",
                "logical_name": logical_name,
                "contract": contract,
            })
            continue

        attribute_type = existing.get("AttributeType")
        if attribute_type not in contract["types"]:
            raise RuntimeError(
                f"Incompatible type for {logical_name}: {attribute_type}; "
                f"expected one of {contract['types']}"
            )
        detail = get_attribute_detail(api, logical_name, attribute_type)
        minimum_length = contract.get("max_length")
        minimum_precision = contract.get("precision")
        if minimum_length is not None:
            actual_length = int(detail.get("MaxLength") or 0)
            if actual_length < minimum_length:
                plan.append({
                    "action": "increase_length",
                    "logical_name": logical_name,
                    "attribute_type": attribute_type,
                    "current": actual_length,
                    "required": minimum_length,
                    "contract": contract,
                })
                continue
        if minimum_precision is not None:
            actual_precision = int(detail.get("Precision") or 0)
            if actual_precision < minimum_precision:
                raise RuntimeError(
                    f"Incompatible precision for {logical_name}: {actual_precision}; "
                    f"required at least {minimum_precision}"
                )
        plan.append({
            "action": "reuse",
            "logical_name": logical_name,
            "attribute_type": attribute_type,
            "contract": contract,
        })

    keys = get_keys(api)
    for key_name, expected_attributes in REQUIRED_ACTIVE_KEYS.items():
        key = keys.get(key_name)
        if key is None:
            raise RuntimeError(f"Required alternate key is missing: {key_name}")
        actual_attributes = tuple(key.get("KeyAttributes") or ())
        if actual_attributes != expected_attributes:
            raise RuntimeError(
                f"Alternate key {key_name} uses {actual_attributes}; "
                f"expected {expected_attributes}"
            )
        if key.get("EntityKeyIndexStatus") != "Active":
            raise RuntimeError(
                f"Alternate key {key_name} is not Active: "
                f"{key.get('EntityKeyIndexStatus')}"
            )
    return plan


def apply_plan(api, prefix, plan):
    changed = []
    for item in plan:
        action = item["action"]
        if action == "reuse":
            continue
        logical_name = item["logical_name"]
        if action == "increase_length":
            full_attribute = get_attribute_detail(
                api,
                logical_name,
                item["attribute_type"],
                full=True,
            )
            full_attribute["@odata.type"] = TYPE_CASTS[item["attribute_type"]]
            full_attribute["MaxLength"] = item["required"]
            api.update_attribute(logical_name, full_attribute)
        elif action == "create":
            api.create_attribute(create_payload(item["contract"], prefix))
        else:
            raise RuntimeError(f"Unsupported plan action: {action}")
        changed.append({
            "action": action,
            "logical_name": logical_name,
        })
        time.sleep(3)

    if changed:
        api.publish_all()
        time.sleep(5)
    return changed


def summarize_plan(plan):
    return [
        {
            "action": item["action"],
            "logical_name": item["logical_name"],
            **({
                "current": item["current"],
                "required": item["required"],
            } if item["action"] == "increase_length" else {}),
            **({
                "type": item["contract"]["types"][0],
                "max_length": item["contract"].get("max_length"),
            } if item["action"] == "create" else {}),
        }
        for item in plan
    ]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Apply safe additive metadata changes; otherwise only report the plan.",
    )
    args = parser.parse_args()

    load_env()
    configured_environment = os.environ["DATAVERSE_URL"].rstrip("/") + "/"
    if configured_environment.lower() != EXPECTED_ENVIRONMENT.lower():
        raise RuntimeError(
            f"Unexpected Dataverse environment: {configured_environment}"
        )
    if os.environ.get("SOLUTION_NAME", "").strip() != SOLUTION:
        raise RuntimeError(
            f"Unexpected solution: {os.environ.get('SOLUTION_NAME', '')}"
        )

    client = get_client("dv-metadata")
    table = client.tables.get(TABLE)
    if not table:
        raise RuntimeError(f"Required table does not exist: {TABLE}")

    solution, publisher, prefix = get_solution_context(client)
    configured_prefix = os.environ.get("PUBLISHER_PREFIX", "").strip().lower()
    if configured_prefix and configured_prefix != prefix:
        raise RuntimeError(
            f"Publisher prefix mismatch: solution={prefix}, env={configured_prefix}"
        )
    if TABLE != f"{prefix}_gastodelaempresa":
        raise RuntimeError(
            f"Table {TABLE} does not belong to publisher prefix {prefix}"
        )

    api = DataverseMetadataApi()
    plan = build_plan(api, prefix)
    result = {
        "mode": "apply" if args.apply else "dry-run",
        "environment": configured_environment,
        "solution": solution.get("uniquename"),
        "publisher": publisher.get("uniquename"),
        "publisher_prefix": prefix,
        "table": TABLE,
        "plan": summarize_plan(plan),
    }
    if args.apply:
        result["changed"] = apply_plan(api, prefix, plan)
        verification = build_plan(api, prefix)
        remaining = [
            item for item in verification
            if item["action"] != "reuse"
        ]
        if remaining:
            raise RuntimeError(
                "Schema verification still reports pending changes: "
                f"{summarize_plan(remaining)}"
            )
        result["verified_columns"] = [
            item["logical_name"] for item in verification
        ]
        result["alternate_keys"] = list(REQUIRED_ACTIVE_KEYS)

    print(json.dumps(result, indent=2, ensure_ascii=True, default=str))


if __name__ == "__main__":
    main()
