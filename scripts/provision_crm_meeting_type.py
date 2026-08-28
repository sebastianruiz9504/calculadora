"""Audit or provision the CRM meeting type choice in Dataverse.

The script is intentionally idempotent. Without ``--apply`` it only audits
the current metadata. Writes are pinned to the Digital Tech default
environment and the ``CotizadorInternoCRM`` unmanaged solution.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from typing import Any

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from provision_crm_deal_lifecycle import (
    DataverseApi,
    DataverseMetadataError,
    EXPECTED_ENVIRONMENT,
    LANGUAGE_CODE,
    SOLUTION_NAME,
    choice_option,
    get_solution,
    label,
)


TABLE_LOGICAL_NAME = "cr07a_crmactividad"
FIELD_SCHEMA_NAME = "cr07a_TipoReunion"
FIELD_LOGICAL_NAME = "cr07a_tiporeunion"
EXPECTED_ATTRIBUTE_TYPE = "Picklist"
EXPECTED_OPTIONS = {
    645250000: "Portafolio",
    645250001: "Seguimiento",
}

ATTRIBUTE_DEFINITION: dict[str, Any] = {
    "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
    "SchemaName": FIELD_SCHEMA_NAME,
    "DisplayName": label("Tipo de reuni\u00f3n"),
    "Description": label(
        "Clasifica una reuni\u00f3n comercial como portafolio o seguimiento."
    ),
    "RequiredLevel": {"Value": "None"},
    "OptionSet": {
        "@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata",
        "IsGlobal": False,
        "Options": [
            choice_option(value, text)
            for value, text in EXPECTED_OPTIONS.items()
        ],
    },
}


def get_table(api: DataverseApi) -> dict[str, Any]:
    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')"
        "?$select=MetadataId,LogicalName,SchemaName"
    )
    table = api.request("GET", path)
    if table.get("LogicalName") != TABLE_LOGICAL_NAME:
        raise DataverseMetadataError(
            f"No existe la tabla esperada {TABLE_LOGICAL_NAME}."
        )
    return table


def get_attribute_summary(api: DataverseApi) -> dict[str, Any] | None:
    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType"
    )
    rows = api.request("GET", path).get("value", [])
    return next(
        (
            row
            for row in rows
            if row.get("LogicalName", "").casefold()
            == FIELD_LOGICAL_NAME.casefold()
        ),
        None,
    )


def get_picklist_metadata(api: DataverseApi) -> dict[str, Any] | None:
    summary = get_attribute_summary(api)
    if summary is None:
        return None

    actual_type = summary.get("AttributeType")
    if actual_type != EXPECTED_ATTRIBUTE_TYPE:
        raise DataverseMetadataError(
            f"{FIELD_LOGICAL_NAME} ya existe con tipo {actual_type}; "
            f"se esperaba {EXPECTED_ATTRIBUTE_TYPE}."
        )

    path = (
        f"EntityDefinitions(LogicalName='{TABLE_LOGICAL_NAME}')"
        f"/Attributes(LogicalName='{FIELD_LOGICAL_NAME}')"
        "/Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType"
        "&$expand=OptionSet($select=IsGlobal,Options)"
    )
    return api.request("GET", path)


def spanish_label(option: dict[str, Any]) -> str:
    labels = option.get("Label", {}).get("LocalizedLabels", [])
    return next(
        (
            item.get("Label", "")
            for item in labels
            if item.get("LanguageCode") == LANGUAGE_CODE
        ),
        "",
    )


def option_map(metadata: dict[str, Any]) -> dict[int, str]:
    result: dict[int, str] = {}
    for option in metadata.get("OptionSet", {}).get("Options", []):
        value = option.get("Value")
        if isinstance(value, int):
            result[value] = spanish_label(option)
    return result


def validate_picklist(metadata: dict[str, Any]) -> dict[int, str]:
    if metadata.get("LogicalName") != FIELD_LOGICAL_NAME:
        raise DataverseMetadataError(
            f"Nombre logico inesperado: {metadata.get('LogicalName')}."
        )
    if metadata.get("SchemaName") != FIELD_SCHEMA_NAME:
        raise DataverseMetadataError(
            f"SchemaName inesperado: {metadata.get('SchemaName')}."
        )
    if metadata.get("AttributeType") != EXPECTED_ATTRIBUTE_TYPE:
        raise DataverseMetadataError(
            f"Tipo inesperado: {metadata.get('AttributeType')}."
        )

    option_set = metadata.get("OptionSet") or {}
    if option_set.get("IsGlobal") is not False:
        raise DataverseMetadataError(
            f"{FIELD_LOGICAL_NAME} debe usar un choice local (IsGlobal=false)."
        )

    actual_options = option_map(metadata)
    if actual_options != EXPECTED_OPTIONS:
        raise DataverseMetadataError(
            "Las opciones no coinciden exactamente. "
            f"Actuales: {actual_options}. Esperadas: {EXPECTED_OPTIONS}."
        )
    return actual_options


def wait_for_picklist(api: DataverseApi) -> dict[str, Any]:
    for attempt in range(10):
        metadata = get_picklist_metadata(api)
        if metadata is not None:
            validate_picklist(metadata)
            return metadata
        if attempt < 9:
            time.sleep(2)
    raise DataverseMetadataError(
        f"Dataverse no propago el campo {FIELD_LOGICAL_NAME}."
    )


def publish_table(api: DataverseApi) -> None:
    parameter_xml = (
        "<importexportxml><entities>"
        f"<entity>{TABLE_LOGICAL_NAME}</entity>"
        "</entities></importexportxml>"
    )
    api.request(
        "POST",
        "PublishXml",
        {"ParameterXml": parameter_xml},
        solution=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Audita o crea el choice local de tipo de reunion del CRM. "
            "Sin --apply no realiza cambios."
        )
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Crea y publica el campo si falta. Sin esta opcion solo audita.",
    )
    args = parser.parse_args()

    api = DataverseApi()
    solution = get_solution(api)
    table = get_table(api)
    metadata = get_picklist_metadata(api)
    created = False

    if metadata is None and args.apply:
        api.request(
            "POST",
            f"EntityDefinitions({table['MetadataId']})/Attributes",
            ATTRIBUTE_DEFINITION,
            solution=True,
        )
        metadata = wait_for_picklist(api)
        created = True

    if metadata is None:
        result = {
            "environment": EXPECTED_ENVIRONMENT,
            "solution": solution.get("uniquename"),
            "table": TABLE_LOGICAL_NAME,
            "mode": "apply" if args.apply else "check",
            "field": FIELD_LOGICAL_NAME,
            "status": "missing",
            "created": False,
            "expectedType": EXPECTED_ATTRIBUTE_TYPE,
            "expectedLocalChoice": True,
            "expectedOptions": EXPECTED_OPTIONS,
        }
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 2

    actual_options = validate_picklist(metadata)
    if args.apply:
        publish_table(api)
        metadata = wait_for_picklist(api)
        actual_options = validate_picklist(metadata)

    result = {
        "environment": EXPECTED_ENVIRONMENT,
        "solution": solution.get("uniquename"),
        "table": TABLE_LOGICAL_NAME,
        "mode": "apply" if args.apply else "check",
        "field": metadata.get("LogicalName"),
        "schemaName": metadata.get("SchemaName"),
        "status": "exact",
        "created": created,
        "type": metadata.get("AttributeType"),
        "localChoice": metadata.get("OptionSet", {}).get("IsGlobal") is False,
        "options": actual_options,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
