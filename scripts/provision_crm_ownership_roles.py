"""Provision CRM ownership roles and the manual opportunity description.

The script is idempotent and pinned to the Digital Tech default environment
and the CotizadorInternoCRM unmanaged solution. Without --apply it is read-only.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Any

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from auth import get_client
from provision_crm_deal_lifecycle import (
    DataverseApi,
    DataverseMetadataError,
    LANGUAGE_CODE,
    SOLUTION_NAME,
    get_solution,
    label,
)


DEAL_TABLE = "cr07a_crmnegocio"
DESCRIPTION_SCHEMA = "cr07a_DescripcionBreve"
DESCRIPTION_LOGICAL = "cr07a_descripcionbreve"
EMPLOYEE_TABLE = "cr07a_empleado"
EMPLOYEE_ID = "cr07a_empleadoid"
EMPLOYEE_EMAIL = "cr07a_correo"
EMPLOYEE_MODULES = "cr07a_modulos"
CRM_USER_OPTION = 645250025
CRM_ADMIN_OPTION = 645250026
INITIAL_ADMIN_EMAIL = "sruiz@digitaltechcolombia.com"


def get_attributes(api: DataverseApi, table: str) -> dict[str, dict[str, Any]]:
    result = api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{table}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType",
    )
    return {
        row["LogicalName"]: row
        for row in result.get("value", [])
        if row.get("LogicalName")
    }


def ensure_description(api: DataverseApi, apply: bool) -> bool:
    if DESCRIPTION_LOGICAL in get_attributes(api, DEAL_TABLE):
        return False
    if not apply:
        return False

    definition = {
        "@odata.type": "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
        "SchemaName": DESCRIPTION_SCHEMA,
        "DisplayName": label("Descripcion breve"),
        "Description": label(
            "Resumen comercial de una oportunidad estimada creada sin calculadora."
        ),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 1000,
        "Format": "TextArea",
        "ImeMode": "Auto",
    }
    api.request(
        "POST",
        f"EntityDefinitions(LogicalName='{DEAL_TABLE}')/Attributes",
        definition,
        solution=True,
    )
    return True


def get_module_options(api: DataverseApi) -> dict[int, str]:
    path = (
        f"EntityDefinitions(LogicalName='{EMPLOYEE_TABLE}')"
        f"/Attributes(LogicalName='{EMPLOYEE_MODULES}')"
        "/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata"
        "?$select=LogicalName&$expand=OptionSet($select=Options)"
    )
    metadata = api.request("GET", path)
    options: dict[int, str] = {}
    for option in metadata.get("OptionSet", {}).get("Options", []):
        value = option.get("Value")
        labels = option.get("Label", {}).get("LocalizedLabels", [])
        text = next(
            (
                item.get("Label", "")
                for item in labels
                if item.get("LanguageCode") == LANGUAGE_CODE
            ),
            "",
        )
        if isinstance(value, int):
            options[value] = text
    return options


def ensure_role_options(api: DataverseApi, apply: bool) -> list[str]:
    options = get_module_options(api)
    changes: list[str] = []

    if options.get(CRM_USER_OPTION) != "CRM Usuario":
        changes.append("rename-user-role")
        if apply:
            api.request(
                "POST",
                "UpdateOptionValue",
                {
                    "EntityLogicalName": EMPLOYEE_TABLE,
                    "AttributeLogicalName": EMPLOYEE_MODULES,
                    "Value": CRM_USER_OPTION,
                    "Label": label("CRM Usuario"),
                    "MergeLabels": False,
                },
            )

    if CRM_ADMIN_OPTION not in options:
        changes.append("insert-admin-role")
        if apply:
            api.request(
                "POST",
                "InsertOptionValue",
                {
                    "EntityLogicalName": EMPLOYEE_TABLE,
                    "AttributeLogicalName": EMPLOYEE_MODULES,
                    "Value": CRM_ADMIN_OPTION,
                    "Label": label("CRM Administrador"),
                },
            )
    elif options.get(CRM_ADMIN_OPTION) != "CRM Administrador":
        changes.append("rename-admin-role")
        if apply:
            api.request(
                "POST",
                "UpdateOptionValue",
                {
                    "EntityLogicalName": EMPLOYEE_TABLE,
                    "AttributeLogicalName": EMPLOYEE_MODULES,
                    "Value": CRM_ADMIN_OPTION,
                    "Label": label("CRM Administrador"),
                    "MergeLabels": False,
                },
            )
    return changes


def parse_multi_select(value: Any) -> set[int]:
    if value is None:
        return set()
    if isinstance(value, int):
        return {value}
    if isinstance(value, list):
        return {
            int(item)
            for item in value
            if str(item).strip().lstrip("-").isdigit()
        }
    return {
        int(item)
        for item in str(value).split(",")
        if item.strip().lstrip("-").isdigit()
    }


def read_admin_employee(data_client: Any) -> dict[str, Any]:
    escaped_email = INITIAL_ADMIN_EMAIL.replace("'", "''")
    pages = data_client.records.get(
        EMPLOYEE_TABLE,
        select=[EMPLOYEE_ID, EMPLOYEE_EMAIL, EMPLOYEE_MODULES],
        filter=f"statecode eq 0 and {EMPLOYEE_EMAIL} eq '{escaped_email}'",
        top=2,
    )
    rows = [dict(row) for page in pages for row in page]
    if len(rows) != 1:
        raise DataverseMetadataError(
            f"Se esperaba un empleado activo para {INITIAL_ADMIN_EMAIL} y se encontraron {len(rows)}."
        )
    return rows[0]


def ensure_admin_assignment(data_client: Any, apply: bool) -> dict[str, Any]:
    employee = read_admin_employee(data_client)
    before = parse_multi_select(employee.get(EMPLOYEE_MODULES))
    after = (before - {CRM_USER_OPTION}) | {CRM_ADMIN_OPTION}
    pending = before != after
    if pending and apply:
        data_client.records.update(
            EMPLOYEE_TABLE,
            employee[EMPLOYEE_ID],
            {EMPLOYEE_MODULES: ",".join(str(value) for value in sorted(after))},
        )
        verified = read_admin_employee(data_client)
        verified_values = parse_multi_select(verified.get(EMPLOYEE_MODULES))
        if verified_values != after:
            raise DataverseMetadataError(
                "Dataverse recibio el rol administrador, pero la lectura posterior no coincide."
            )
    else:
        verified_values = before

    return {
        "employeeId": employee[EMPLOYEE_ID],
        "email": employee[EMPLOYEE_EMAIL],
        "before": sorted(before),
        "expected": sorted(after),
        "verified": sorted(verified_values),
        "pending": pending,
    }


def publish(api: DataverseApi) -> None:
    api.request(
        "POST",
        "PublishXml",
        {
            "ParameterXml": (
                "<importexportxml><entities>"
                f"<entity>{DEAL_TABLE}</entity>"
                f"<entity>{EMPLOYEE_TABLE}</entity>"
                "</entities></importexportxml>"
            )
        },
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Crea metadatos y asigna el rol. Sin esta opcion solo audita.",
    )
    args = parser.parse_args()

    api = DataverseApi()
    solution = get_solution(api)

    description_missing = DESCRIPTION_LOGICAL not in get_attributes(api, DEAL_TABLE)
    description_created = ensure_description(api, args.apply)
    role_changes = ensure_role_options(api, args.apply)
    data_client = get_client("dv-data")
    assignment = ensure_admin_assignment(data_client, args.apply)

    if args.apply and (description_created or role_changes):
        publish(api)

    verified_description = DESCRIPTION_LOGICAL in get_attributes(api, DEAL_TABLE)
    verified_options = get_module_options(api)
    ready = (
        verified_description
        and verified_options.get(CRM_USER_OPTION) == "CRM Usuario"
        and verified_options.get(CRM_ADMIN_OPTION) == "CRM Administrador"
        and CRM_ADMIN_OPTION in assignment["verified"]
        and CRM_USER_OPTION not in assignment["verified"]
    )
    result = {
        "mode": "apply" if args.apply else "check",
        "solution": SOLUTION_NAME,
        "description": {
            "logicalName": DESCRIPTION_LOGICAL,
            "wasMissing": description_missing,
            "created": description_created,
            "verified": verified_description,
        },
        "roles": {
            "changes": role_changes,
            "user": verified_options.get(CRM_USER_OPTION),
            "administrator": verified_options.get(CRM_ADMIN_OPTION),
        },
        "assignment": assignment,
        "ready": ready,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if ready else 2


if __name__ == "__main__":
    raise SystemExit(main())
