"""Provision the calculator-to-CRM lifecycle metadata in Dataverse.

The script is intentionally idempotent. Without ``--apply`` it only reports
the current state. Metadata writes are pinned to the Digital Tech production
environment and the ``CotizadorInternoCRM`` unmanaged solution.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from typing import Any
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from auth import get_plugin_headers, get_token, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION_NAME = "CotizadorInternoCRM"
TABLE_LOGICAL_NAME = "cr07a_crmnegocio"
LANGUAGE_CODE = 3082


def label(text: str) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [
            {
                "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
                "Label": text,
                "LanguageCode": LANGUAGE_CODE,
            }
        ],
    }


def choice_option(value: int, text: str) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.OptionMetadata",
        "Value": value,
        "Label": label(text),
    }


ATTRIBUTE_DEFINITIONS: tuple[dict[str, Any], ...] = (
    {
        "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
        "SchemaName": "cr07a_TipoRegistro",
        "DisplayName": label("Tipo de registro"),
        "Description": label("Diferencia una oportunidad estimada de un negocio cotizado."),
        "RequiredLevel": {"Value": "None"},
        "DefaultFormValue": 645250000,
        "OptionSet": {
            "@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata",
            "IsGlobal": False,
            "Options": [
                choice_option(645250000, "Oportunidad estimada"),
                choice_option(645250001, "Negocio cotizado"),
            ],
        },
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_EscenarioOrigen",
        "DisplayName": label("Escenario de origen"),
        "Description": label("Identificador del escenario editable en la calculadora."),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 100,
        "FormatName": {"Value": "Text"},
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
        "SchemaName": "cr07a_Puntaje",
        "DisplayName": label("Puntaje"),
        "Description": label("Puntaje congelado al sincronizar el negocio desde la calculadora."),
        "RequiredLevel": {"Value": "None"},
        "MinValue": -100000000000.0,
        "MaxValue": 100000000000.0,
        "Precision": 2,
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
        "SchemaName": "cr07a_ValorContrato",
        "DisplayName": label("Valor del contrato"),
        "Description": label("Valor total congelado al sincronizar el negocio cotizado."),
        "RequiredLevel": {"Value": "None"},
        "MinValue": 0.0,
        "MaxValue": 100000000000.0,
        "Precision": 2,
        "PrecisionSource": 2,
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
        "SchemaName": "cr07a_AprovisionamientoSolicitado",
        "DisplayName": label("Aprovisionamiento solicitado"),
        "Description": label("Indica si el flujo de aprovisionamiento aceptó la solicitud."),
        "RequiredLevel": {"Value": "None"},
        "DefaultValue": False,
        "OptionSet": {
            "@odata.type": "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata",
            "IsGlobal": False,
            "TrueOption": {"Value": 1, "Label": label("Sí")},
            "FalseOption": {"Value": 0, "Label": label("No")},
        },
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
        "SchemaName": "cr07a_FechaAprovisionamientoSolicitado",
        "DisplayName": label("Fecha de solicitud de aprovisionamiento"),
        "Description": label("Fecha y hora en que el flujo aceptó la solicitud."),
        "RequiredLevel": {"Value": "None"},
        "Format": "DateAndTime",
        "DateTimeBehavior": {"Value": "UserLocal"},
        "ImeMode": "Inactive",
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_SolicitudAprovisionamiento",
        "DisplayName": label("Solicitud de aprovisionamiento"),
        "Description": label("Identificador de correlación devuelto por el flujo de aprovisionamiento."),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 100,
        "FormatName": {"Value": "Text"},
    },
)

EXPECTED_TYPES = {
    "cr07a_tiporegistro": "Picklist",
    "cr07a_escenarioorigen": "String",
    "cr07a_puntaje": "Decimal",
    "cr07a_valorcontrato": "Money",
    "cr07a_aprovisionamientosolicitado": "Boolean",
    "cr07a_fechaaprovisionamientosolicitado": "DateTime",
    "cr07a_solicitudaprovisionamiento": "String",
}

ALTERNATE_KEY_SCHEMA_NAME = "cr07a_crmnegocio_escenarioorigen_key"


class DataverseMetadataError(RuntimeError):
    pass


class DataverseApi:
    def __init__(self) -> None:
        load_env()
        configured_environment = os.environ["DATAVERSE_URL"].rstrip("/")
        configured_solution = os.environ.get("SOLUTION_NAME", "").strip()
        if configured_environment.casefold() != EXPECTED_ENVIRONMENT.casefold():
            raise DataverseMetadataError(
                f"Entorno rechazado: {configured_environment}. "
                f"Este script solo admite {EXPECTED_ENVIRONMENT}."
            )
        if configured_solution != SOLUTION_NAME:
            raise DataverseMetadataError(
                f"Solución rechazada: {configured_solution or '(vacía)'}. "
                f"Este script solo admite {SOLUTION_NAME}."
            )

        self.api_url = f"{configured_environment}/api/data/v9.2"
        self.token = get_token()

    def request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        *,
        solution: bool = False,
    ) -> dict[str, Any]:
        headers = get_plugin_headers("dv-metadata", self.token)
        headers.update(
            {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            }
        )
        if body is not None:
            headers["Content-Type"] = "application/json; charset=utf-8"
        if solution:
            headers["MSCRM.SolutionUniqueName"] = SOLUTION_NAME

        data = (
            json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            if body is not None
            else None
        )
        request = Request(
            f"{self.api_url}/{path.lstrip('/')}",
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urlopen(request, timeout=90) as response:
                raw = response.read()
        except HTTPError as error:
            raw = error.read().decode("utf-8", errors="replace")
            try:
                detail = json.loads(raw).get("error", {}).get("message", raw)
            except json.JSONDecodeError:
                detail = raw
            raise DataverseMetadataError(
                f"Dataverse rechazó {method} {path} con HTTP {error.code}: {detail}"
            ) from error

        return json.loads(raw) if raw else {}


def get_solution(api: DataverseApi) -> dict[str, Any]:
    escaped_name = quote(
        f"$select=solutionid,uniquename,friendlyname,ismanaged"
        f"&$filter=uniquename eq '{SOLUTION_NAME}'",
        safe="$=,&'()",
    )
    rows = api.request("GET", f"solutions?{escaped_name}").get("value", [])
    if len(rows) != 1:
        raise DataverseMetadataError(
            f"Se esperaba una solución {SOLUTION_NAME} y se encontraron {len(rows)}."
        )
    if rows[0].get("ismanaged"):
        raise DataverseMetadataError(
            f"La solución {SOLUTION_NAME} es administrada y no admite estos cambios."
        )
    return rows[0]


def get_table(api: DataverseApi) -> dict[str, Any]:
    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')"
        "?$select=MetadataId,LogicalName,SchemaName"
    )
    row = api.request("GET", path)
    if row.get("LogicalName") != TABLE_LOGICAL_NAME:
        raise DataverseMetadataError(f"No existe la tabla {TABLE_LOGICAL_NAME}.")
    return row


def get_attributes(api: DataverseApi) -> dict[str, dict[str, Any]]:
    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType"
    )
    return {
        row["LogicalName"]: row
        for row in api.request("GET", path).get("value", [])
        if row.get("LogicalName")
    }


def get_keys(api: DataverseApi) -> list[dict[str, Any]]:
    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')/Keys"
        "?$select=MetadataId,SchemaName,KeyAttributes,EntityKeyIndexStatus"
    )
    return api.request("GET", path).get("value", [])


def validate_existing_attributes(attributes: dict[str, dict[str, Any]]) -> None:
    for logical_name, expected_type in EXPECTED_TYPES.items():
        current = attributes.get(logical_name)
        if current is None:
            continue
        actual_type = current.get("AttributeType")
        if actual_type != expected_type:
            raise DataverseMetadataError(
                f"{logical_name} ya existe con tipo {actual_type}; se esperaba {expected_type}."
            )


def wait_for_attributes(api: DataverseApi) -> dict[str, dict[str, Any]]:
    for attempt in range(10):
        attributes = get_attributes(api)
        if all(name in attributes for name in EXPECTED_TYPES):
            validate_existing_attributes(attributes)
            return attributes
        if attempt < 9:
            time.sleep(2)
    missing = sorted(set(EXPECTED_TYPES) - set(attributes))
    raise DataverseMetadataError(
        "Dataverse no propagó todos los campos creados: " + ", ".join(missing)
    )


def ensure_alternate_key(api: DataverseApi, apply: bool) -> str:
    keys = get_keys(api)
    current = next(
        (
            item
            for item in keys
            if item.get("SchemaName", "").casefold()
            == ALTERNATE_KEY_SCHEMA_NAME.casefold()
        ),
        None,
    )
    if current is not None:
        return current.get("EntityKeyIndexStatus") or "Existing"
    if not apply:
        return "Missing"

    payload = {
        "SchemaName": ALTERNATE_KEY_SCHEMA_NAME,
        "DisplayName": label("Escenario de origen único"),
        "KeyAttributes": ["cr07a_escenarioorigen"],
    }
    api.request(
        "POST",
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')/Keys",
        payload,
        solution=True,
    )

    status = "Pending"
    for attempt in range(20):
        current = next(
            (
                item
                for item in get_keys(api)
                if item.get("SchemaName", "").casefold()
                == ALTERNATE_KEY_SCHEMA_NAME.casefold()
            ),
            None,
        )
        if current is not None:
            status = current.get("EntityKeyIndexStatus") or status
            if status in {"Active", "Failed"}:
                break
        if attempt < 19:
            time.sleep(3)
    if status == "Failed":
        raise DataverseMetadataError(
            f"La clave {ALTERNATE_KEY_SCHEMA_NAME} no pudo activar su índice."
        )
    return status


def publish_table(api: DataverseApi) -> None:
    parameter_xml = (
        "<importexportxml><entities>"
        f"<entity>{TABLE_LOGICAL_NAME}</entity>"
        "</entities></importexportxml>"
    )
    api.request("POST", "PublishXml", {"ParameterXml": parameter_xml})


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Crea los campos y la clave faltantes. Sin esta opción solo audita.",
    )
    args = parser.parse_args()

    api = DataverseApi()
    solution = get_solution(api)
    table = get_table(api)
    before = get_attributes(api)
    validate_existing_attributes(before)

    missing = [
        definition
        for definition in ATTRIBUTE_DEFINITIONS
        if definition["SchemaName"].lower() not in before
    ]
    if args.apply:
        for definition in missing:
            api.request(
                "POST",
                f"EntityDefinitions({table['MetadataId']})/Attributes",
                definition,
                solution=True,
            )
        after = wait_for_attributes(api)
        key_status = ensure_alternate_key(api, apply=True)
        publish_table(api)
    else:
        after = before
        key_status = ensure_alternate_key(api, apply=False)

    result = {
        "environment": EXPECTED_ENVIRONMENT,
        "solution": solution.get("uniquename"),
        "table": table.get("LogicalName"),
        "mode": "apply" if args.apply else "check",
        "created": [
            definition["SchemaName"].lower()
            for definition in missing
            if args.apply
        ],
        "missing": sorted(set(EXPECTED_TYPES) - set(after)),
        "columns": [
            {
                "logicalName": name,
                "type": after[name]["AttributeType"],
            }
            for name in EXPECTED_TYPES
            if name in after
        ],
        "alternateKey": {
            "schemaName": ALTERNATE_KEY_SCHEMA_NAME,
            "status": key_status,
        },
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if not result["missing"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
