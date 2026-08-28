"""Provision and backfill the CRM company layer.

The operational ``cr07a_cliente`` table remains untouched. CRM companies are
stored in ``cr07a_crmempresa`` and optionally reference an operational client.
Re-running the script is safe: metadata and data are checked before writes.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from typing import Any

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from auth import get_client
from provision_crm_deal_lifecycle import (
    DataverseApi,
    DataverseMetadataError,
    LANGUAGE_CODE,
    SOLUTION_NAME,
    choice_option,
    label,
)


CRM_COMPANY_SCHEMA_NAME = "cr07a_CrmEmpresa"
CRM_COMPANY_LOGICAL_NAME = "cr07a_crmempresa"
CRM_COMPANY_SET_NAME = "cr07a_crmempresas"
CRM_COMPANY_ID_FIELD = "cr07a_crmempresaid"
OPERATIONAL_CLIENT_LOGICAL_NAME = "cr07a_cliente"
OPERATIONAL_CLIENT_SET_NAME = "cr07a_clientes"

LEAD = 645250000
ACTIVE_CUSTOMER = 645250001
INACTIVE = 645250002

CRM_COMPANY_ATTRIBUTE_DEFINITIONS: tuple[dict[str, Any], ...] = (
    {
        "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
        "SchemaName": "cr07a_TipoRelacion",
        "DisplayName": label("Tipo de relación"),
        "Description": label("Diferencia leads, clientes activos y clientes inactivos."),
        "RequiredLevel": {"Value": "ApplicationRequired"},
        "DefaultFormValue": LEAD,
        "OptionSet": {
            "@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata",
            "IsGlobal": False,
            "Options": [
                choice_option(LEAD, "Lead"),
                choice_option(ACTIVE_CUSTOMER, "Cliente activo"),
                choice_option(INACTIVE, "Cliente inactivo"),
            ],
        },
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_Nit",
        "DisplayName": label("NIT"),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 50,
        "FormatName": {"Value": "Text"},
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_Correo",
        "DisplayName": label("Correo"),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 254,
        "FormatName": {"Value": "Email"},
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_Telefono",
        "DisplayName": label("Teléfono"),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 50,
        "FormatName": {"Value": "Phone"},
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "SchemaName": "cr07a_Ciudad",
        "DisplayName": label("Ciudad"),
        "RequiredLevel": {"Value": "None"},
        "MaxLength": 100,
        "FormatName": {"Value": "Text"},
    },
    {
        "@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
        "SchemaName": "cr07a_FechaConversion",
        "DisplayName": label("Fecha de conversión"),
        "Description": label("Fecha en la que el lead se vinculó con un cliente operativo."),
        "RequiredLevel": {"Value": "None"},
        "Format": "DateAndTime",
        "DateTimeBehavior": {"Value": "UserLocal"},
        "ImeMode": "Inactive",
    },
)

EXPECTED_COMPANY_TYPES = {
    "cr07a_nombre": "String",
    "cr07a_tiporelacion": "Picklist",
    "cr07a_nit": "String",
    "cr07a_correo": "String",
    "cr07a_telefono": "String",
    "cr07a_ciudad": "String",
    "cr07a_fechaconversion": "DateTime",
    "cr07a_clienteoperativo": "Lookup",
}

LOOKUPS = (
    {
        "referencing_table": CRM_COMPANY_LOGICAL_NAME,
        "lookup_field_name": "cr07a_ClienteOperativo",
        "lookup_logical_name": "cr07a_clienteoperativo",
        "referenced_table": OPERATIONAL_CLIENT_LOGICAL_NAME,
        "display_name": "Cliente operativo",
        "description": "Cliente operativo vinculado cuando la empresa ya fue convertida.",
    },
    {
        "referencing_table": "cr07a_crmcontacto",
        "lookup_field_name": "cr07a_EmpresaCrm",
        "lookup_logical_name": "cr07a_empresacrm",
        "referenced_table": CRM_COMPANY_LOGICAL_NAME,
        "display_name": "Empresa CRM",
        "description": "Empresa comercial asociada con el contacto.",
    },
    {
        "referencing_table": "cr07a_crmnegocio",
        "lookup_field_name": "cr07a_EmpresaCrm",
        "lookup_logical_name": "cr07a_empresacrm",
        "referenced_table": CRM_COMPANY_LOGICAL_NAME,
        "display_name": "Empresa CRM",
        "description": "Empresa comercial asociada con el negocio.",
    },
    {
        "referencing_table": "cr07a_crmactividad",
        "lookup_field_name": "cr07a_EmpresaCrm",
        "lookup_logical_name": "cr07a_empresacrm",
        "referenced_table": CRM_COMPANY_LOGICAL_NAME,
        "display_name": "Empresa CRM",
        "description": "Empresa comercial asociada con la actividad.",
    },
)

COMPANY_OPERATIONAL_KEY = "cr07a_crmempresa_clienteoperativo_key"

CHILD_TABLES = (
    {
        "table": "cr07a_crmcontacto",
        "id_field": "cr07a_crmcontactoid",
        "old_lookup": "_cr07a_empresa_value",
        "new_lookup": "_cr07a_empresacrm_value",
        "new_navigation": "cr07a_EmpresaCrm",
    },
    {
        "table": "cr07a_crmnegocio",
        "id_field": "cr07a_crmnegocioid",
        "old_lookup": "_cr07a_empresa_value",
        "new_lookup": "_cr07a_empresacrm_value",
        "new_navigation": "cr07a_EmpresaCrm",
    },
    {
        "table": "cr07a_crmactividad",
        "id_field": "cr07a_crmactividadid",
        "old_lookup": "_cr07a_empresa_value",
        "new_lookup": "_cr07a_empresacrm_value",
        "new_navigation": "cr07a_EmpresaCrm",
    },
)


def list_records(
    data_client: Any,
    table: str,
    select: list[str],
) -> list[dict[str, Any]]:
    return [
        dict(row)
        for row in data_client.records.list(
            table,
            select=select,
        )
    ]


def get_entity(api: DataverseApi, logical_name: str) -> dict[str, Any] | None:
    try:
        return api.request(
            "GET",
            f"EntityDefinitions(LogicalName='{logical_name}')"
            "?$select=MetadataId,LogicalName,SchemaName,EntitySetName",
        )
    except DataverseMetadataError as error:
        if "HTTP 404" in str(error):
            return None
        raise


def create_company_table(api: DataverseApi) -> None:
    payload = {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "SchemaName": CRM_COMPANY_SCHEMA_NAME,
        "DisplayName": label("Empresa CRM"),
        "DisplayCollectionName": label("Empresas CRM"),
        "Description": label(
            "Identidad comercial para leads y clientes vinculados al maestro operativo."
        ),
        "OwnershipType": "UserOwned",
        "HasActivities": False,
        "HasNotes": False,
        "IsActivity": False,
        "PrimaryNameAttribute": "cr07a_nombre",
        "Attributes": [
            {
                "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
                "SchemaName": "cr07a_Nombre",
                "DisplayName": label("Nombre"),
                "RequiredLevel": {"Value": "ApplicationRequired"},
                "MaxLength": 200,
                "FormatName": {"Value": "Text"},
                "IsPrimaryName": True,
            }
        ],
    }
    api.request("POST", "EntityDefinitions", payload, solution=True)


def get_attributes(api: DataverseApi, logical_name: str) -> dict[str, dict[str, Any]]:
    path = (
        f"EntityDefinitions(LogicalName='{logical_name}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType"
    )
    return {
        row["LogicalName"]: row
        for row in api.request("GET", path).get("value", [])
        if row.get("LogicalName")
    }


def wait_for_entity(api: DataverseApi, logical_name: str) -> dict[str, Any]:
    for attempt in range(15):
        entity = get_entity(api, logical_name)
        if entity is not None:
            return entity
        if attempt < 14:
            time.sleep(2)
    raise DataverseMetadataError(f"Dataverse no propagó la tabla {logical_name}.")


def wait_for_attribute(
    api: DataverseApi,
    table: str,
    logical_name: str,
) -> dict[str, Any]:
    for attempt in range(15):
        attribute = get_attributes(api, table).get(logical_name)
        if attribute is not None:
            return attribute
        if attempt < 14:
            time.sleep(2)
    raise DataverseMetadataError(
        f"Dataverse no propagó {logical_name} en {table}."
    )


def ensure_company_columns(
    api: DataverseApi,
    entity: dict[str, Any],
    apply: bool,
) -> list[str]:
    current = get_attributes(api, CRM_COMPANY_LOGICAL_NAME)
    created: list[str] = []
    for definition in CRM_COMPANY_ATTRIBUTE_DEFINITIONS:
        logical_name = definition["SchemaName"].lower()
        if logical_name in current:
            continue
        if apply:
            api.request(
                "POST",
                f"EntityDefinitions({entity['MetadataId']})/Attributes",
                definition,
                solution=True,
            )
            created.append(logical_name)

    if apply and created:
        for logical_name in created:
            wait_for_attribute(api, CRM_COMPANY_LOGICAL_NAME, logical_name)
    return created


def ensure_lookups(
    api: DataverseApi,
    metadata_client: Any,
    apply: bool,
) -> list[str]:
    created: list[str] = []
    for lookup in LOOKUPS:
        attributes = get_attributes(api, lookup["referencing_table"])
        if lookup["lookup_logical_name"] in attributes:
            continue
        if not apply:
            continue
        result = metadata_client.tables.create_lookup_field(
            referencing_table=lookup["referencing_table"],
            lookup_field_name=lookup["lookup_field_name"],
            referenced_table=lookup["referenced_table"],
            display_name=lookup["display_name"],
            description=lookup["description"],
            required=False,
            cascade_delete="RemoveLink",
            solution=SOLUTION_NAME,
            language_code=LANGUAGE_CODE,
        )
        created.append(result.lookup_schema_name)
        wait_for_attribute(
            api,
            lookup["referencing_table"],
            lookup["lookup_logical_name"],
        )
    return created


def get_company_keys(api: DataverseApi) -> list[dict[str, Any]]:
    return api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{CRM_COMPANY_LOGICAL_NAME}')/Keys"
        "?$select=MetadataId,SchemaName,KeyAttributes,EntityKeyIndexStatus",
    ).get("value", [])


def ensure_company_key(api: DataverseApi, apply: bool) -> str:
    current = next(
        (
            item
            for item in get_company_keys(api)
            if item.get("SchemaName", "").casefold()
            == COMPANY_OPERATIONAL_KEY.casefold()
        ),
        None,
    )
    if current is not None:
        return current.get("EntityKeyIndexStatus") or "Existing"
    if not apply:
        return "Missing"

    api.request(
        "POST",
        f"EntityDefinitions(LogicalName='{CRM_COMPANY_LOGICAL_NAME}')/Keys",
        {
            "SchemaName": COMPANY_OPERATIONAL_KEY,
            "DisplayName": label("Cliente operativo único"),
            "KeyAttributes": ["cr07a_clienteoperativo"],
        },
        solution=True,
    )

    status = "Pending"
    for attempt in range(30):
        current = next(
            (
                item
                for item in get_company_keys(api)
                if item.get("SchemaName", "").casefold()
                == COMPANY_OPERATIONAL_KEY.casefold()
            ),
            None,
        )
        if current is not None:
            status = current.get("EntityKeyIndexStatus") or status
            if status in {"Active", "Failed"}:
                break
        if attempt < 29:
            time.sleep(3)
    if status == "Failed":
        raise DataverseMetadataError(
            f"La clave {COMPANY_OPERATIONAL_KEY} no pudo activar su índice."
        )
    return status


def publish_entities(api: DataverseApi) -> None:
    names = [
        CRM_COMPANY_LOGICAL_NAME,
        "cr07a_crmcontacto",
        "cr07a_crmnegocio",
        "cr07a_crmactividad",
    ]
    entities = "".join(f"<entity>{name}</entity>" for name in names)
    api.request(
        "POST",
        "PublishXml",
        {"ParameterXml": f"<importexportxml><entities>{entities}</entities></importexportxml>"},
    )


def optional_text(value: Any) -> str | None:
    if value is None:
        return None
    normalized = str(value).strip()
    return normalized or None


def read_operational_clients(data_client: Any) -> list[dict[str, Any]]:
    return list_records(
        data_client,
        OPERATIONAL_CLIENT_LOGICAL_NAME,
        [
            "cr07a_clienteid",
            "cr07a_nombre",
            "cr07a_nit",
            "cr07a_correoelectronico",
            "cr07a_telefono",
            "cr07a_ciudad",
            "statecode",
        ],
    )


def read_crm_companies(data_client: Any) -> list[dict[str, Any]]:
    return list_records(
        data_client,
        CRM_COMPANY_LOGICAL_NAME,
        [
            CRM_COMPANY_ID_FIELD,
            "cr07a_nombre",
            "cr07a_tiporelacion",
            "_cr07a_clienteoperativo_value",
        ],
    )


def build_company_payload(client: dict[str, Any]) -> dict[str, Any]:
    client_id = client["cr07a_clienteid"]
    payload: dict[str, Any] = {
        "cr07a_nombre": optional_text(client.get("cr07a_nombre"))
        or f"Cliente {client_id[:8]}",
        "cr07a_tiporelacion": (
            ACTIVE_CUSTOMER if client.get("statecode", 0) == 0 else INACTIVE
        ),
        "cr07a_ClienteOperativo@odata.bind": (
            f"/{OPERATIONAL_CLIENT_SET_NAME}({client_id})"
        ),
    }
    for source, target in (
        ("cr07a_nit", "cr07a_nit"),
        ("cr07a_correoelectronico", "cr07a_correo"),
        ("cr07a_telefono", "cr07a_telefono"),
        ("cr07a_ciudad", "cr07a_ciudad"),
    ):
        value = optional_text(client.get(source))
        if value is not None:
            payload[target] = value
    return payload


def ensure_company_records(
    data_client: Any,
    operational_clients: list[dict[str, Any]],
    apply: bool,
) -> tuple[list[dict[str, Any]], int]:
    crm_companies = read_crm_companies(data_client)
    existing_client_ids = {
        row.get("_cr07a_clienteoperativo_value")
        for row in crm_companies
        if row.get("_cr07a_clienteoperativo_value")
    }
    missing = [
        client
        for client in operational_clients
        if client["cr07a_clienteid"] not in existing_client_ids
    ]
    if apply and missing:
        payloads = [build_company_payload(client) for client in missing]
        for start in range(0, len(payloads), 500):
            data_client.records.create(
                CRM_COMPANY_LOGICAL_NAME,
                payloads[start : start + 500],
            )
        crm_companies = read_crm_companies(data_client)
    return crm_companies, 0 if apply else len(missing)


def read_child_rows(data_client: Any, config: dict[str, str]) -> list[dict[str, Any]]:
    return list_records(
        data_client,
        config["table"],
        [
            config["id_field"],
            config["old_lookup"],
            config["new_lookup"],
        ],
    )


def execute_updates(
    data_client: Any,
    updates: list[tuple[dict[str, str], str, str]],
) -> None:
    for start in range(0, len(updates), 900):
        batch = data_client.batch.new()
        for config, record_id, company_id in updates[start : start + 900]:
            batch.records.update(
                config["table"],
                record_id,
                {
                    f"{config['new_navigation']}@odata.bind": (
                        f"/{CRM_COMPANY_SET_NAME}({company_id})"
                    )
                },
            )
        result = batch.execute(continue_on_error=True)
        if result.failed:
            messages = [
                f"{item.status_code}: {item.error_message}"
                for item in result.failed[:5]
            ]
            raise DataverseMetadataError(
                "Falló la migración de relaciones CRM: " + " | ".join(messages)
            )


def migrate_child_relations(
    data_client: Any,
    company_by_operational_client: dict[str, str],
    apply: bool,
) -> dict[str, dict[str, int]]:
    summary: dict[str, dict[str, int]] = {}
    updates: list[tuple[dict[str, str], str, str]] = []
    for config in CHILD_TABLES:
        rows = read_child_rows(data_client, config)
        required = 0
        for row in rows:
            operational_client_id = row.get(config["old_lookup"])
            current_company_id = row.get(config["new_lookup"])
            target_company_id = company_by_operational_client.get(
                operational_client_id or ""
            )
            if not operational_client_id:
                continue
            if target_company_id is None:
                raise DataverseMetadataError(
                    f"{config['table']} referencia un cliente sin empresa CRM: "
                    f"{operational_client_id}."
                )
            if current_company_id == target_company_id:
                continue
            required += 1
            updates.append(
                (
                    config,
                    row[config["id_field"]],
                    target_company_id,
                )
            )
        summary[config["table"]] = {
            "records": len(rows),
            "pending": required,
        }

    if apply and updates:
        execute_updates(data_client, updates)
    return summary


def verify_data(
    data_client: Any,
    operational_clients: list[dict[str, Any]],
) -> tuple[dict[str, str], dict[str, dict[str, int]]]:
    crm_companies = read_crm_companies(data_client)
    company_by_operational_client = {
        row["_cr07a_clienteoperativo_value"]: row[CRM_COMPANY_ID_FIELD]
        for row in crm_companies
        if row.get("_cr07a_clienteoperativo_value")
    }
    missing_clients = sorted(
        {
            row["cr07a_clienteid"]
            for row in operational_clients
            if row["cr07a_clienteid"] not in company_by_operational_client
        }
    )
    if missing_clients:
        raise DataverseMetadataError(
            f"Quedaron {len(missing_clients)} clientes sin empresa CRM."
        )

    relation_summary: dict[str, dict[str, int]] = {}
    for config in CHILD_TABLES:
        rows = read_child_rows(data_client, config)
        mismatches = 0
        related = 0
        for row in rows:
            operational_client_id = row.get(config["old_lookup"])
            if not operational_client_id:
                continue
            related += 1
            expected = company_by_operational_client.get(operational_client_id)
            if row.get(config["new_lookup"]) != expected:
                mismatches += 1
        relation_summary[config["table"]] = {
            "recordsWithLegacyCompany": related,
            "mismatches": mismatches,
        }
        if mismatches:
            raise DataverseMetadataError(
                f"{config['table']} conserva {mismatches} relaciones sin migrar."
            )
    return company_by_operational_client, relation_summary


def validate_company_metadata(api: DataverseApi) -> dict[str, dict[str, Any]]:
    attributes = get_attributes(api, CRM_COMPANY_LOGICAL_NAME)
    for logical_name, expected_type in EXPECTED_COMPANY_TYPES.items():
        current = attributes.get(logical_name)
        if current is None:
            continue
        actual = current.get("AttributeType")
        if actual != expected_type:
            raise DataverseMetadataError(
                f"{logical_name} tiene tipo {actual}; se esperaba {expected_type}."
            )
    return attributes


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Crea metadatos y ejecuta el backfill. Sin esta opción solo audita.",
    )
    args = parser.parse_args()

    api = DataverseApi()
    metadata_client = get_client("dv-metadata")
    data_client = get_client("dv-data")

    entity = get_entity(api, CRM_COMPANY_LOGICAL_NAME)
    table_created = False
    if entity is None and args.apply:
        create_company_table(api)
        entity = wait_for_entity(api, CRM_COMPANY_LOGICAL_NAME)
        table_created = True

    if entity is None:
        print(
            json.dumps(
                {
                    "mode": "check",
                    "solution": SOLUTION_NAME,
                    "table": CRM_COMPANY_LOGICAL_NAME,
                    "tableExists": False,
                    "ready": False,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 2

    columns_created = ensure_company_columns(api, entity, args.apply)
    lookups_created = ensure_lookups(api, metadata_client, args.apply)
    attributes = validate_company_metadata(api)
    missing_metadata = sorted(set(EXPECTED_COMPANY_TYPES) - set(attributes))
    key_status = ensure_company_key(api, args.apply)

    data_summary: dict[str, Any] = {
        "operationalClients": 0,
        "crmCompanies": 0,
        "companiesPending": 0,
        "relations": {},
    }
    if not missing_metadata and key_status != "Missing":
        operational_clients = read_operational_clients(data_client)
        crm_companies, companies_pending = ensure_company_records(
            data_client,
            operational_clients,
            args.apply,
        )
        company_by_client = {
            row["_cr07a_clienteoperativo_value"]: row[CRM_COMPANY_ID_FIELD]
            for row in crm_companies
            if row.get("_cr07a_clienteoperativo_value")
        }
        pending_relations = migrate_child_relations(
            data_client,
            company_by_client,
            args.apply,
        )
        if args.apply:
            company_by_client, verified_relations = verify_data(
                data_client,
                operational_clients,
            )
        else:
            verified_relations = pending_relations
        data_summary = {
            "operationalClients": len(operational_clients),
            "crmCompanies": len(crm_companies),
            "companiesPending": companies_pending,
            "mappedOperationalClients": len(company_by_client),
            "relations": verified_relations,
        }

    if args.apply:
        publish_entities(api)

    ready = (
        not missing_metadata
        and key_status in {"Active", "Pending", "Existing"}
        and data_summary["companiesPending"] == 0
        and all(
            relation.get("mismatches", relation.get("pending", 0)) == 0
            for relation in data_summary["relations"].values()
        )
    )
    print(
        json.dumps(
            {
                "mode": "apply" if args.apply else "check",
                "solution": SOLUTION_NAME,
                "table": CRM_COMPANY_LOGICAL_NAME,
                "tableCreated": table_created,
                "columnsCreated": columns_created,
                "lookupsCreated": lookups_created,
                "missingMetadata": missing_metadata,
                "alternateKey": {
                    "schemaName": COMPANY_OPERATIONAL_KEY,
                    "status": key_status,
                },
                "data": data_summary,
                "ready": ready,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0 if ready else 2


if __name__ == "__main__":
    raise SystemExit(main())
