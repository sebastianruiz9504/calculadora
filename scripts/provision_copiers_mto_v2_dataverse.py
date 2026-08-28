"""Provision the isolated Copiers MTO Firmado V2 Dataverse schema.

The command is check-only by default. ``--apply`` creates only the approved
publisher, unmanaged solution and V2 components in the pinned Digital Tech
Copiers environment. It never reads or modifies the legacy maintenance table.

The schema is intentionally explicit. Existing compatible metadata is reused;
an incompatible component stops the run instead of being deleted or replaced.
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import sys
import time
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from typing import Any, Callable
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from auth import get_plugin_headers, get_token, load_env


ENVIRONMENT_URL = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION_NAME = "CopiersMtoFirmadoV2"
SOLUTION_DISPLAY_NAME = "Copiers MTO Firmado V2"
SOLUTION_VERSION = "1.0.0.0"
PUBLISHER_UNIQUE_NAME = "DigitalTechCopiers"
PUBLISHER_DISPLAY_NAME = "Digital Tech Copiers"
PUBLISHER_PREFIX = "dtc"
PUBLISHER_OPTION_PREFIX = 82727
LANGUAGE_CODE = 3082

MAIN_TABLE = "dtc_copiersmtov2"
MAIN_TABLE_SCHEMA = "dtc_CopiersMtoV2"
MAIN_ENTITY_SET = "dtc_copiersmtov2s"
MAIN_PRIMARY_NAME = "dtc_name"
MAIN_PRIMARY_NAME_SCHEMA = "dtc_Name"

EVIDENCE_TABLE = "dtc_copiersmtoevidenciav2"
EVIDENCE_TABLE_SCHEMA = "dtc_CopiersMtoEvidenciaV2"
EVIDENCE_ENTITY_SET = "dtc_copiersmtoevidenciav2s"
EVIDENCE_PRIMARY_NAME = "dtc_name"
EVIDENCE_PRIMARY_NAME_SCHEMA = "dtc_Name"

CLIENT_TABLE = "cr07a_cliente"
CLIENT_ENTITY_SET = "cr07a_clientes"
EQUIPMENT_TABLE = "cr07a_equipo"
EQUIPMENT_ENTITY_SET = "cr07a_equipos"


class ProvisioningError(RuntimeError):
    """Raised when live metadata conflicts with the approved schema."""


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


def required_level(required: bool) -> dict[str, Any]:
    return {
        "Value": "ApplicationRequired" if required else "None",
        "CanBeChanged": True,
        "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings",
    }


def audit_enabled() -> dict[str, Any]:
    return {"Value": True}


def option(text: str) -> dict[str, Any]:
    # Dataverse assigns a publisher-scoped value because Value is null.
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.OptionMetadata",
        "Value": None,
        "Label": label(text),
    }


@dataclass(frozen=True)
class ColumnSpec:
    logical_name: str
    schema_name: str
    display_name: str
    attribute_type: str
    payload: dict[str, Any]
    secured: bool = False


@dataclass(frozen=True)
class TableSpec:
    logical_name: str
    schema_name: str
    entity_set_name: str
    display_name: str
    display_collection_name: str
    description: str
    primary_name_logical: str
    primary_name_schema: str
    columns: tuple[ColumnSpec, ...]
    optimistic_concurrency_required: bool = False


@dataclass(frozen=True)
class RelationshipSpec:
    schema_name: str
    referenced_table: str
    referencing_table: str
    lookup_logical_name: str
    lookup_schema_name: str
    display_name: str
    required: bool


@dataclass(frozen=True)
class KeySpec:
    table: str
    schema_name: str
    display_name: str
    attributes: tuple[str, ...]


ATTRIBUTE_METADATA_CASTS: dict[str, str] = {
    "String": "StringAttributeMetadata",
    "Memo": "MemoAttributeMetadata",
    "Picklist": "PicklistAttributeMetadata",
    "Boolean": "BooleanAttributeMetadata",
    "Integer": "IntegerAttributeMetadata",
    "BigInt": "BigIntAttributeMetadata",
    "Decimal": "DecimalAttributeMetadata",
    "DateTime": "DateTimeAttributeMetadata",
    "File": "FileAttributeMetadata",
    "Lookup": "LookupAttributeMetadata",
}

ATTRIBUTE_TYPE_PROPERTIES: dict[str, tuple[str, ...]] = {
    "String": ("MaxLength", "FormatName"),
    "Memo": ("MaxLength", "Format"),
    "Picklist": (),
    "Boolean": ("DefaultValue",),
    "Integer": ("MinValue", "MaxValue", "Format"),
    "BigInt": (),
    "Decimal": ("MinValue", "MaxValue", "Precision"),
    "DateTime": ("Format", "DateTimeBehavior"),
    "File": ("MaxSizeInKB",),
    "Lookup": ("Targets",),
}

ATTRIBUTE_BASE_PROPERTIES: tuple[str, ...] = (
    "MetadataId",
    "LogicalName",
    "SchemaName",
    "AttributeType",
    "AttributeTypeName",
    "IsPrimaryName",
    "IsSecured",
    "RequiredLevel",
    "IsAuditEnabled",
)


def managed_value(value: Any) -> Any:
    if isinstance(value, dict) and "Value" in value:
        return value.get("Value")
    return value


def metadata_values_equal(actual: Any, expected: Any) -> bool:
    actual = managed_value(actual)
    expected = managed_value(expected)
    if isinstance(actual, bool) or isinstance(expected, bool):
        return actual is expected
    if isinstance(actual, (int, float, Decimal)) and isinstance(
        expected, (int, float, Decimal)
    ):
        try:
            return Decimal(str(actual)) == Decimal(str(expected))
        except InvalidOperation:
            return False
    if isinstance(actual, str) and isinstance(expected, str):
        return actual.casefold() == expected.casefold()
    return actual == expected


def column_base(
    schema_name: str,
    display_name: str,
    description: str,
    required: bool,
    *,
    secured: bool,
) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "SchemaName": schema_name,
        "DisplayName": label(display_name),
        "Description": label(description),
        "RequiredLevel": required_level(required),
        "IsAuditEnabled": audit_enabled(),
    }
    if secured:
        payload["IsSecured"] = True
    return payload


def string_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    max_length: int,
    description: str,
    *,
    required: bool = False,
    secured: bool = False,
    format_name: str = "Text",
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": format_name},
            "MaxLength": max_length,
        }
    )
    return ColumnSpec(
        logical_name, schema_name, display_name, "String", payload, secured
    )


def memo_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    max_length: int,
    description: str,
    *,
    required: bool = False,
    secured: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
            "AttributeType": "Memo",
            "AttributeTypeName": {"Value": "MemoType"},
            "Format": "TextArea",
            "MaxLength": max_length,
        }
    )
    return ColumnSpec(logical_name, schema_name, display_name, "Memo", payload, secured)


def choice_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    choices: tuple[str, ...],
    description: str,
    *,
    required: bool = False,
    default_label: str | None = None,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=False
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
            "AttributeType": "Picklist",
            "AttributeTypeName": {"Value": "PicklistType"},
            "OptionSet": {
                "@odata.type": "Microsoft.Dynamics.CRM.OptionSetMetadata",
                "IsGlobal": False,
                "OptionSetType": "Picklist",
                "Options": [option(text) for text in choices],
            },
        }
    )
    # A null value is resolved after creation. The runtime always writes the
    # explicit state, so a form default is not required for data integrity.
    if default_label is not None:
        payload["Description"] = label(
            f"{description} Valor inicial contractual: {default_label}."
        )
    return ColumnSpec(logical_name, schema_name, display_name, "Picklist", payload)


def boolean_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    description: str,
    *,
    required: bool = False,
    secured: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
            "AttributeType": "Boolean",
            "AttributeTypeName": {"Value": "BooleanType"},
            "DefaultValue": False,
            "OptionSet": {
                "@odata.type": "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata",
                "IsGlobal": False,
                "TrueOption": {
                    "@odata.type": "Microsoft.Dynamics.CRM.OptionMetadata",
                    "Value": 1,
                    "Label": label("Sí"),
                },
                "FalseOption": {
                    "@odata.type": "Microsoft.Dynamics.CRM.OptionMetadata",
                    "Value": 0,
                    "Label": label("No"),
                },
            },
        }
    )
    return ColumnSpec(
        logical_name, schema_name, display_name, "Boolean", payload, secured
    )


def integer_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    min_value: int,
    max_value: int,
    description: str,
    *,
    required: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=False
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
            "AttributeType": "Integer",
            "AttributeTypeName": {"Value": "IntegerType"},
            "Format": "None",
            "MinValue": min_value,
            "MaxValue": max_value,
        }
    )
    return ColumnSpec(logical_name, schema_name, display_name, "Integer", payload)


def bigint_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    description: str,
    *,
    required: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=False
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.BigIntAttributeMetadata",
            "AttributeType": "BigInt",
            "AttributeTypeName": {"Value": "BigIntType"},
        }
    )
    return ColumnSpec(logical_name, schema_name, display_name, "BigInt", payload)


def decimal_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    min_value: float,
    max_value: float,
    description: str,
    *,
    secured: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, False, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            "AttributeType": "Decimal",
            "AttributeTypeName": {"Value": "DecimalType"},
            "MinValue": min_value,
            "MaxValue": max_value,
            "Precision": 7,
        }
    )
    return ColumnSpec(
        logical_name, schema_name, display_name, "Decimal", payload, secured
    )


def datetime_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    description: str,
    *,
    required: bool = False,
    date_only: bool = False,
    secured: bool = False,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, required, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
            "AttributeType": "DateTime",
            "AttributeTypeName": {"Value": "DateTimeType"},
            "Format": "DateOnly" if date_only else "DateAndTime",
            "DateTimeBehavior": {
                "Value": "TimeZoneIndependent" if date_only else "UserLocal"
            },
            "ImeMode": "Inactive",
        }
    )
    return ColumnSpec(
        logical_name, schema_name, display_name, "DateTime", payload, secured
    )


def file_column(
    logical_name: str,
    schema_name: str,
    display_name: str,
    max_size_kb: int,
    description: str,
    *,
    secured: bool = True,
) -> ColumnSpec:
    payload = column_base(
        schema_name, display_name, description, False, secured=secured
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.FileAttributeMetadata",
            "MaxSizeInKB": max_size_kb,
        }
    )
    return ColumnSpec(logical_name, schema_name, display_name, "File", payload, secured)


WORKFLOW_CHOICES = ("Draft", "Finalizing", "ReadyToSend", "Failed")
EMAIL_CHOICES = ("NotReady", "Pending", "Processing", "Sent", "Failed")
MAINTENANCE_CHOICES = ("Correctivo", "Preventivo")
EVIDENCE_PURPOSE_CHOICES = (
    "Signature",
    "SignedReport",
    "OriginalAttachment",
    "CustomerAttachment",
)
EVIDENCE_SECURITY_CHOICES = (
    "NotApplicable",
    "Pending",
    "ScanPassed",
    "Rejected",
)


MAIN_COLUMNS: tuple[ColumnSpec, ...] = (
    string_column("dtc_operationkey", "dtc_OperationKey", "Clave de operación", 128, "Clave idempotente del MTO V2.", required=True),
    choice_column("dtc_workflowstate", "dtc_WorkflowState", "Estado del proceso", WORKFLOW_CHOICES, "Estado de finalización del reporte.", required=True, default_label="Draft"),
    choice_column("dtc_emailstate", "dtc_EmailState", "Estado del correo", EMAIL_CHOICES, "Estado idempotente de envío del correo.", required=True, default_label="NotReady"),
    string_column("dtc_technicianuserkey", "dtc_TechnicianUserKey", "Identificador del técnico", 36, "Identificador autoritativo del técnico autenticado.", required=True),
    string_column("dtc_techniciannamesnapshot", "dtc_TechnicianNameSnapshot", "Técnico", 200, "Nombre congelado del técnico.", required=True),
    string_column("dtc_technicianemailsnapshot", "dtc_TechnicianEmailSnapshot", "Correo interno del técnico", 320, "Correo congelado del técnico; uso interno.", required=True, secured=True, format_name="Email"),
    string_column("dtc_clientnamesnapshot", "dtc_ClientNameSnapshot", "Cliente", 250, "Nombre congelado del cliente.", required=True),
    string_column("dtc_clientcontactnamesnapshot", "dtc_ClientContactNameSnapshot", "Persona que atiende", 200, "Persona del cliente que recibe el servicio.", required=True),
    string_column("dtc_clientemailsnapshot", "dtc_ClientEmailSnapshot", "Correo del cliente", 320, "Destinatario congelado del reporte.", required=True, secured=True, format_name="Email"),
    string_column("dtc_equipmentserialsnapshot", "dtc_EquipmentSerialSnapshot", "Serial del equipo", 200, "Serial congelado, incluso cuando no existe lookup de equipo.", required=True),
    string_column("dtc_title", "dtc_Title", "Título del reporte", 250, "Título visible del MTO V2.", required=True),
    datetime_column("dtc_servicedate", "dtc_ServiceDate", "Fecha del servicio", "Fecha civil del servicio.", required=True, date_only=True),
    choice_column("dtc_maintenancetype", "dtc_MaintenanceType", "Tipo de mantenimiento", MAINTENANCE_CHOICES, "Tipo de mantenimiento realizado.", required=True),
    string_column("dtc_formversion", "dtc_FormVersion", "Versión del formulario", 80, "Versión allowlisted del formulario y PDF."),
    memo_column("dtc_answersjson", "dtc_AnswersJson", "Respuestas del formulario", 262144, "Respuestas canónicas del formulario.", secured=True),
    memo_column("dtc_workperformed", "dtc_WorkPerformed", "Trabajo realizado", 12000, "Descripción visible del trabajo realizado."),
    memo_column("dtc_customerobservations", "dtc_CustomerObservations", "Observaciones del cliente", 6000, "Observaciones visibles del cliente."),
    string_column("dtc_serviceaddressinternal", "dtc_ServiceAddressInternal", "Dirección interna del servicio", 300, "Dato interno; nunca se envía al cliente.", secured=True),
    memo_column("dtc_internalnotes", "dtc_InternalNotes", "Notas internas", 4000, "Notas internas; nunca se envían al cliente.", secured=True),
    string_column("dtc_signername", "dtc_SignerName", "Nombre del firmante", 200, "Nombre declarado por quien firma.", secured=True),
    string_column("dtc_signerrole", "dtc_SignerRole", "Cargo del firmante", 150, "Cargo o relación con el cliente.", secured=True),
    boolean_column("dtc_customeraccepted", "dtc_CustomerAccepted", "Cliente acepta", "Aceptación expresa antes de firmar."),
    integer_column("dtc_signaturepointcount", "dtc_SignaturePointCount", "Puntos de firma", 0, 100000, "Cantidad de puntos capturados por el pad de firma."),
    datetime_column("dtc_devicesignedatutc", "dtc_DeviceSignedAtUtc", "Firma registrada por dispositivo", "Hora reportada por el dispositivo al firmar."),
    datetime_column("dtc_serverfinalizedatutc", "dtc_ServerFinalizedAtUtc", "Finalización del servidor", "Hora autoritativa de finalización."),
    decimal_column("dtc_latitude", "dtc_Latitude", "Latitud interna", -90.0, 90.0, "Coordenada interna con siete decimales.", secured=True),
    decimal_column("dtc_longitude", "dtc_Longitude", "Longitud interna", -180.0, 180.0, "Coordenada interna con siete decimales.", secured=True),
    decimal_column("dtc_accuracymeters", "dtc_AccuracyMeters", "Precisión de ubicación", 0.0, 250.0, "Precisión interna del GPS en metros.", secured=True),
    datetime_column("dtc_locationcapturedatutc", "dtc_LocationCapturedAtUtc", "Captura de ubicación", "Hora interna de captura de ubicación.", secured=True),
    string_column("dtc_locationsource", "dtc_LocationSource", "Fuente de ubicación", 80, "Fuente interna de la ubicación.", secured=True),
    string_column("dtc_signaturesha256", "dtc_SignatureSha256", "SHA-256 de firma", 64, "Huella de la evidencia de firma.", secured=True),
    string_column("dtc_signatureevidencekey", "dtc_SignatureEvidenceKey", "Clave de evidencia de firma", 64, "Selector interno de la firma.", secured=True),
    string_column("dtc_reportevidencekey", "dtc_ReportEvidenceKey", "Clave del reporte firmado", 64, "Selector del PDF firmado para entrega."),
    string_column("dtc_reportfilename", "dtc_ReportFileName", "Nombre del PDF", 260, "Nombre seguro del reporte PDF."),
    string_column("dtc_reportsha256", "dtc_ReportSha256", "SHA-256 del reporte", 64, "Huella del PDF descargado y verificado."),
    integer_column("dtc_attachmentcount", "dtc_AttachmentCount", "Cantidad de adjuntos", 0, 8, "Cantidad de adjuntos customer-safe publicados."),
    memo_column("dtc_attachmentmanifestjson", "dtc_AttachmentManifestJson", "Manifiesto de adjuntos", 1048576, "Manifiesto canónico de adjuntos customer-safe."),
    string_column("dtc_finalizationfingerprint", "dtc_FinalizationFingerprint", "Huella de finalización", 64, "Huella canónica de la solicitud final.", secured=True),
    string_column("dtc_finalizationleasekey", "dtc_FinalizationLeaseKey", "Lease de finalización", 64, "Lease idempotente durante el staging.", secured=True),
    datetime_column("dtc_readyatutc", "dtc_ReadyAtUtc", "Listo para enviar", "Hora autoritativa de publicación."),
    string_column("dtc_emailoutboxkey", "dtc_EmailOutboxKey", "Clave de correlación del correo", 200, "Correlación estable con el borrador Graph.", secured=True),
    memo_column("dtc_emailtosnapshot", "dtc_EmailToSnapshot", "Destinatarios congelados", 1000, "Destinatarios validados del correo.", secured=True),
    string_column("dtc_emailsubjectsnapshot", "dtc_EmailSubjectSnapshot", "Asunto congelado", 500, "Asunto final del correo.", secured=True),
    memo_column("dtc_emailhtmlbodysnapshot", "dtc_EmailHtmlBodySnapshot", "Cuerpo HTML congelado", 1048576, "HTML final codificado y sin datos internos.", secured=True),
    string_column("dtc_providerdraftid", "dtc_ProviderDraftId", "Identificador del borrador Graph", 512, "Identificador inmutable persistido por el flujo.", secured=True),
    string_column("dtc_internetmessageid", "dtc_InternetMessageId", "Internet Message ID", 998, "Identificador de mensaje reconciliado por el flujo.", secured=True),
    string_column("dtc_lasterrorcode", "dtc_LastErrorCode", "Código del último error", 80, "Código seguro de error de app o flujo."),
    memo_column("dtc_lasterrorsafemessage", "dtc_LastErrorSafeMessage", "Último error seguro", 1500, "Mensaje seguro sin secretos ni datos internos.", secured=True),
)


EVIDENCE_COLUMNS: tuple[ColumnSpec, ...] = (
    string_column("dtc_evidencekey", "dtc_EvidenceKey", "Clave de evidencia", 64, "Clave content-addressed de evidencia.", required=True),
    choice_column("dtc_purpose", "dtc_Purpose", "Propósito", EVIDENCE_PURPOSE_CHOICES, "Propósito inmutable de la evidencia.", required=True),
    integer_column("dtc_sequence", "dtc_Sequence", "Secuencia", 0, 8, "Secuencia estable de evidencia.", required=True),
    file_column("dtc_filecontent", "dtc_FileContent", "Archivo", 12288, "Binario privado verificado por read-back."),
    string_column("dtc_originalfilename", "dtc_OriginalFileName", "Nombre de archivo", 260, "Nombre interno o genérico según el propósito.", required=True, secured=True),
    string_column("dtc_contenttype", "dtc_ContentType", "Tipo de contenido", 160, "MIME validado por contenido.", required=True),
    bigint_column("dtc_bytelength", "dtc_ByteLength", "Tamaño en bytes", "Longitud exacta del binario.", required=True),
    string_column("dtc_sha256", "dtc_Sha256", "SHA-256", 64, "Huella del binario descargado.", required=True, secured=True),
    string_column("dtc_derivedfromevidencekey", "dtc_DerivedFromEvidenceKey", "Derivada de evidencia", 64, "Clave del original saneado del cual deriva."),
    choice_column("dtc_securitystate", "dtc_SecurityState", "Estado de seguridad", EVIDENCE_SECURITY_CHOICES, "Resultado del control AV/CDR."),
    datetime_column("dtc_securitycheckedatutc", "dtc_SecurityCheckedAtUtc", "Control de seguridad", "Hora autoritativa del control de seguridad."),
    string_column("dtc_securityprovider", "dtc_SecurityProvider", "Proveedor de seguridad", 200, "Motor, política y versión del control.", secured=True),
)


TABLES: tuple[TableSpec, ...] = (
    TableSpec(
        MAIN_TABLE,
        MAIN_TABLE_SCHEMA,
        MAIN_ENTITY_SET,
        "MTO Firmado V2",
        "MTO Firmados V2",
        "Ticket y reporte de mantenimiento firmado creado por la experiencia Copiers V2.",
        MAIN_PRIMARY_NAME,
        MAIN_PRIMARY_NAME_SCHEMA,
        MAIN_COLUMNS,
        optimistic_concurrency_required=True,
    ),
    TableSpec(
        EVIDENCE_TABLE,
        EVIDENCE_TABLE_SCHEMA,
        EVIDENCE_ENTITY_SET,
        "Evidencia MTO V2",
        "Evidencias MTO V2",
        "Evidencia privada e idempotente del MTO Firmado V2.",
        EVIDENCE_PRIMARY_NAME,
        EVIDENCE_PRIMARY_NAME_SCHEMA,
        EVIDENCE_COLUMNS,
    ),
)


KEYS: tuple[KeySpec, ...] = (
    KeySpec(MAIN_TABLE, "dtc_CopiersMtoV2OperationKey", "Clave de operación única", ("dtc_operationkey",)),
    KeySpec(EVIDENCE_TABLE, "dtc_CopiersMtoEvidenciaV2EvidenceKey", "Clave de evidencia única", ("dtc_evidencekey",)),
)


RELATIONSHIPS: tuple[RelationshipSpec, ...] = (
    RelationshipSpec("dtc_Cliente_CopiersMtoV2", CLIENT_TABLE, MAIN_TABLE, "dtc_client", "dtc_Client", "Cliente", True),
    RelationshipSpec("dtc_Equipo_CopiersMtoV2", EQUIPMENT_TABLE, MAIN_TABLE, "dtc_equipment", "dtc_Equipment", "Equipo", False),
    RelationshipSpec("dtc_CopiersMtoV2_Evidencia", MAIN_TABLE, EVIDENCE_TABLE, "dtc_signedmto", "dtc_SignedMto", "MTO firmado", True),
)


class DataverseApi:
    def __init__(self, *, allow_writes: bool = False) -> None:
        load_env()
        configured = os.environ.get("DATAVERSE_URL", "").rstrip("/")
        if configured.casefold() != ENVIRONMENT_URL.casefold():
            raise ProvisioningError(
                f"Ambiente rechazado: {configured or '(vacío)'}. "
                f"Este provisioner solo admite {ENVIRONMENT_URL}."
            )
        self.base = f"{ENVIRONMENT_URL}/api/data/v9.2"
        self.token = get_token()
        self.allow_writes = allow_writes

    def request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        *,
        solution: bool = False,
        merge_labels: bool = False,
        allow_404: bool = False,
    ) -> dict[str, Any]:
        if method.upper() not in {"GET", "HEAD"} and not self.allow_writes:
            raise ProvisioningError(
                f"El modo check-only bloqueo la solicitud {method.upper()} {path}."
            )
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
        if merge_labels:
            headers["MSCRM.MergeLabels"] = "true"
        data = (
            json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            if body is not None
            else None
        )
        request = Request(
            f"{self.base}/{path.lstrip('/')}",
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urlopen(request, timeout=120) as response:
                raw = response.read()
        except HTTPError as error:
            raw = error.read().decode("utf-8", errors="replace")
            if allow_404 and error.code == 404:
                return {}
            try:
                detail = json.loads(raw).get("error", {}).get("message", raw)
            except json.JSONDecodeError:
                detail = raw
            raise ProvisioningError(
                f"Dataverse rechazó {method} {path} con HTTP {error.code}: {detail}"
            ) from error
        return json.loads(raw) if raw else {}


def retry(action: Callable[[], Any], label_text: str, attempts: int = 4) -> Any:
    last: Exception | None = None
    for attempt in range(attempts):
        try:
            return action()
        except ProvisioningError as error:
            last = error
            if attempt == attempts - 1:
                break
            time.sleep(2 * (attempt + 1))
    raise ProvisioningError(f"Falló {label_text}: {last}") from last


def query_rows(api: DataverseApi, collection: str, query: str) -> list[dict[str, Any]]:
    return api.request("GET", f"{collection}?{query}").get("value", [])


def find_publisher(api: DataverseApi) -> dict[str, Any] | None:
    query = quote(
        "$select=publisherid,uniquename,friendlyname,customizationprefix,customizationoptionvalueprefix"
        f"&$filter=uniquename eq '{PUBLISHER_UNIQUE_NAME}'",
        safe="$=,&'()",
    )
    rows = query_rows(api, "publishers", query)
    if len(rows) > 1:
        raise ProvisioningError("Hay más de un publisher con el unique name aprobado.")
    return rows[0] if rows else None


def ensure_publisher(api: DataverseApi, apply: bool) -> dict[str, Any] | None:
    publisher = find_publisher(api)
    if publisher is None and apply:
        api.request(
            "POST",
            "publishers",
            {
                "uniquename": PUBLISHER_UNIQUE_NAME,
                "friendlyname": PUBLISHER_DISPLAY_NAME,
                "description": "Publisher aislado para soluciones propias de Digital Tech Copiers.",
                "customizationprefix": PUBLISHER_PREFIX,
                "customizationoptionvalueprefix": PUBLISHER_OPTION_PREFIX,
            },
        )
        publisher = find_publisher(api)
    if publisher is None:
        return None
    if str(publisher.get("customizationprefix", "")).casefold() != PUBLISHER_PREFIX:
        raise ProvisioningError("El publisher existe con un prefijo de personalización diferente.")
    if int(publisher.get("customizationoptionvalueprefix", -1)) != PUBLISHER_OPTION_PREFIX:
        raise ProvisioningError("El publisher existe con un prefijo numérico diferente.")
    return publisher


def find_solution(api: DataverseApi) -> dict[str, Any] | None:
    query = quote(
        "$select=solutionid,uniquename,friendlyname,version,ismanaged,_publisherid_value"
        f"&$filter=uniquename eq '{SOLUTION_NAME}'",
        safe="$=,&'()",
    )
    rows = query_rows(api, "solutions", query)
    if len(rows) > 1:
        raise ProvisioningError("Hay más de una solución con el unique name aprobado.")
    return rows[0] if rows else None


def ensure_solution(api: DataverseApi, apply: bool) -> dict[str, Any] | None:
    publisher = ensure_publisher(api, apply)
    if publisher is None:
        return None
    solution = find_solution(api)
    if solution is None and apply:
        api.request(
            "POST",
            "solutions",
            {
                "uniquename": SOLUTION_NAME,
                "friendlyname": SOLUTION_DISPLAY_NAME,
                "description": "Nueva experiencia paralela para captura, firma, PDF y envío de mantenimientos Copiers.",
                "version": SOLUTION_VERSION,
                "publisherid@odata.bind": f"/publishers({publisher['publisherid']})",
            },
        )
        solution = find_solution(api)
    if solution is None:
        return None
    if bool(solution.get("ismanaged")):
        raise ProvisioningError("La solución V2 existe como administrada.")
    if str(solution.get("_publisherid_value", "")).casefold() != str(
        publisher["publisherid"]
    ).casefold():
        raise ProvisioningError("La solución V2 pertenece a otro publisher.")
    return solution


def get_table(api: DataverseApi, logical_name: str) -> dict[str, Any] | None:
    result = api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{logical_name}')"
        "?$select=MetadataId,LogicalName,SchemaName,EntitySetName,PrimaryIdAttribute,"
        "PrimaryNameAttribute,OwnershipType,IsAuditEnabled,IsOptimisticConcurrencyEnabled,HasNotes",
        allow_404=True,
    )
    return result or None


def table_contract_differences(
    table: TableSpec, current: dict[str, Any] | None
) -> list[str]:
    if current is None:
        return [f"Falta la tabla contractual {table.logical_name}."]

    differences: list[str] = []
    expected = {
        "LogicalName": table.logical_name,
        "SchemaName": table.schema_name,
        "EntitySetName": table.entity_set_name,
        "PrimaryIdAttribute": f"{table.logical_name}id",
        "PrimaryNameAttribute": table.primary_name_logical,
        "OwnershipType": "UserOwned",
        "HasNotes": False,
        "IsAuditEnabled": True,
    }
    for property_name, expected_value in expected.items():
        current_value = current.get(property_name)
        if not metadata_values_equal(current_value, expected_value):
            differences.append(
                f"{table.logical_name}.{property_name}={managed_value(current_value)!r}; "
                f"se esperaba {expected_value!r}."
            )
    if table.optimistic_concurrency_required and not bool(
        current.get("IsOptimisticConcurrencyEnabled")
    ):
        differences.append(
            f"{table.logical_name}.IsOptimisticConcurrencyEnabled=False; "
            "se esperaba True."
        )
    return differences


def primary_name_payload(table: TableSpec) -> dict[str, Any]:
    payload = column_base(
        table.primary_name_schema,
        "Nombre",
        "Nombre legible interno del registro.",
        True,
        secured=False,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": "Text"},
            "MaxLength": 160,
            "IsPrimaryName": True,
        }
    )
    return payload


def table_payload(table: TableSpec) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "SchemaName": table.schema_name,
        "EntitySetName": table.entity_set_name,
        "DisplayName": label(table.display_name),
        "DisplayCollectionName": label(table.display_collection_name),
        "Description": label(table.description),
        "OwnershipType": "UserOwned",
        "IsActivity": False,
        "HasActivities": False,
        "HasNotes": False,
        "PrimaryNameAttribute": table.primary_name_logical,
        "IsAuditEnabled": audit_enabled(),
        "Attributes": [primary_name_payload(table)],
    }


def wait_for_table(api: DataverseApi, logical_name: str) -> dict[str, Any]:
    for attempt in range(20):
        current = get_table(api, logical_name)
        if current is not None:
            return current
        if attempt < 19:
            time.sleep(2)
    raise ProvisioningError(f"Dataverse no propagó la tabla {logical_name}.")


def ensure_table(api: DataverseApi, table: TableSpec, apply: bool) -> dict[str, Any] | None:
    current = get_table(api, table.logical_name)
    if current is None and apply:
        retry(
            lambda: api.request("POST", "EntityDefinitions", table_payload(table), solution=True),
            f"crear tabla {table.logical_name}",
        )
        current = wait_for_table(api, table.logical_name)
    if current is None:
        return None
    differences = table_contract_differences(table, current)
    if differences:
        raise ProvisioningError(
            "La tabla existente no coincide con el contrato:\n- "
            + "\n- ".join(differences)
        )
    return current


def get_attributes(api: DataverseApi, table: str) -> dict[str, dict[str, Any]]:
    path = (
        f"EntityDefinitions(LogicalName='{table}')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType,AttributeTypeName,"
        "IsPrimaryName,IsSecured,RequiredLevel,IsAuditEnabled"
    )
    return {
        str(row["LogicalName"]).casefold(): row
        for row in api.request("GET", path).get("value", [])
        if row.get("LogicalName")
    }


def get_typed_attributes(
    api: DataverseApi, table: str, attribute_type: str
) -> dict[str, dict[str, Any]]:
    metadata_cast = ATTRIBUTE_METADATA_CASTS.get(attribute_type)
    if metadata_cast is None:
        raise ProvisioningError(
            f"No existe un cast de metadata para el tipo {attribute_type}."
        )
    selected = ATTRIBUTE_BASE_PROPERTIES + ATTRIBUTE_TYPE_PROPERTIES[attribute_type]
    path = (
        f"EntityDefinitions(LogicalName='{table}')/Attributes/"
        f"Microsoft.Dynamics.CRM.{metadata_cast}?$select={','.join(selected)}"
    )
    return {
        str(row["LogicalName"]).casefold(): row
        for row in api.request("GET", path).get("value", [])
        if row.get("LogicalName")
    }


def primary_name_spec(table: TableSpec) -> ColumnSpec:
    return ColumnSpec(
        table.primary_name_logical,
        table.primary_name_schema,
        "Nombre",
        "String",
        primary_name_payload(table),
        False,
    )


def get_column_contract_metadata(
    api: DataverseApi, table: TableSpec
) -> dict[str, dict[str, Any]]:
    columns = (primary_name_spec(table),) + table.columns
    metadata: dict[str, dict[str, Any]] = {}
    for attribute_type in sorted({column.attribute_type for column in columns}):
        rows = get_typed_attributes(api, table.logical_name, attribute_type)
        for column in columns:
            if column.attribute_type != attribute_type:
                continue
            current = rows.get(column.logical_name.casefold())
            if current is not None:
                metadata[column.logical_name.casefold()] = current
    return metadata


def expected_attribute_type_name(column: ColumnSpec) -> str:
    configured = managed_value(column.payload.get("AttributeTypeName"))
    return str(configured or f"{column.attribute_type}Type")


def column_contract_differences(
    table: TableSpec,
    column: ColumnSpec,
    actual: dict[str, Any] | None,
) -> list[str]:
    path = f"{table.logical_name}.{column.logical_name}"
    if actual is None:
        return [f"Falta la columna contractual {path}."]

    differences: list[str] = []
    expected_base_type = "Virtual" if column.attribute_type == "File" else column.attribute_type
    comparisons = {
        "SchemaName": column.schema_name,
        "AttributeType": expected_base_type,
        "AttributeTypeName": expected_attribute_type_name(column),
        "IsSecured": column.secured,
        "RequiredLevel": managed_value(column.payload.get("RequiredLevel")),
        "IsAuditEnabled": managed_value(column.payload.get("IsAuditEnabled")),
    }
    if "IsPrimaryName" in column.payload:
        comparisons["IsPrimaryName"] = bool(column.payload["IsPrimaryName"])
    for property_name in ATTRIBUTE_TYPE_PROPERTIES[column.attribute_type]:
        if property_name in column.payload:
            comparisons[property_name] = column.payload[property_name]

    for property_name, expected in comparisons.items():
        current = actual.get(property_name)
        if not metadata_values_equal(current, expected):
            differences.append(
                f"{path}.{property_name}={managed_value(current)!r}; "
                f"se esperaba {managed_value(expected)!r}."
            )
    return differences


def verify_column_contract(
    api: DataverseApi, table: TableSpec
) -> tuple[int, list[str]]:
    columns = (primary_name_spec(table),) + table.columns
    current = get_column_contract_metadata(api, table)
    differences: list[str] = []
    for column in columns:
        differences.extend(
            column_contract_differences(
                table, column, current.get(column.logical_name.casefold())
            )
        )
    return len(columns), differences


def wait_for_attribute(
    api: DataverseApi, table: str, logical_name: str
) -> dict[str, Any]:
    for attempt in range(20):
        current = get_attributes(api, table).get(logical_name.casefold())
        if current is not None:
            return current
        if attempt < 19:
            time.sleep(2)
    raise ProvisioningError(f"Dataverse no propagó {table}.{logical_name}.")


def ensure_columns(
    api: DataverseApi, table: TableSpec, metadata_id: str, apply: bool
) -> list[str]:
    current = get_attributes(api, table.logical_name)
    contract_metadata = get_column_contract_metadata(api, table)
    created: list[str] = []
    for column in table.columns:
        existing = current.get(column.logical_name.casefold())
        if existing is not None:
            differences = column_contract_differences(
                table,
                column,
                contract_metadata.get(column.logical_name.casefold()),
            )
            if differences:
                raise ProvisioningError(
                    "La columna existente no coincide con el contrato:\n- "
                    + "\n- ".join(differences)
                )
            continue
        if not apply:
            continue
        retry(
            lambda column=column: api.request(
                "POST",
                f"EntityDefinitions({metadata_id})/Attributes",
                column.payload,
                solution=True,
            ),
            f"crear columna {table.logical_name}.{column.logical_name}",
        )
        wait_for_attribute(api, table.logical_name, column.logical_name)
        created.append(column.logical_name)
        print(f"  created {table.logical_name}.{column.logical_name}", flush=True)
    return created


def get_keys(api: DataverseApi, table: str) -> list[dict[str, Any]]:
    return api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{table}')/Keys"
        "?$select=MetadataId,SchemaName,KeyAttributes,EntityKeyIndexStatus",
    ).get("value", [])


def ensure_key(api: DataverseApi, key: KeySpec, apply: bool) -> str:
    current = next(
        (
            row
            for row in get_keys(api, key.table)
            if str(row.get("SchemaName", "")).casefold() == key.schema_name.casefold()
        ),
        None,
    )
    if current is None and apply:
        api.request(
            "POST",
            f"EntityDefinitions(LogicalName='{key.table}')/Keys",
            {
                "SchemaName": key.schema_name,
                "DisplayName": label(key.display_name),
                "KeyAttributes": list(key.attributes),
            },
            solution=True,
        )
    if current is None and not apply:
        return "Missing"
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        current = next(
            (
                row
                for row in get_keys(api, key.table)
                if str(row.get("SchemaName", "")).casefold()
                == key.schema_name.casefold()
            ),
            None,
        )
        if current is not None:
            attributes = tuple(str(value).casefold() for value in current.get("KeyAttributes", []))
            if attributes != tuple(value.casefold() for value in key.attributes):
                raise ProvisioningError(f"La clave {key.schema_name} tiene otras columnas.")
            status = str(current.get("EntityKeyIndexStatus", ""))
            if status in {"Active", "Failed"}:
                if status == "Failed":
                    raise ProvisioningError(f"La clave {key.schema_name} quedó Failed.")
                return status
        if not apply:
            return str(current.get("EntityKeyIndexStatus", "Missing")) if current else "Missing"
        time.sleep(3)
    raise ProvisioningError(f"La clave {key.schema_name} no llegó a Active en 300 segundos.")


def get_relationships(api: DataverseApi) -> list[dict[str, Any]]:
    query = quote(
        "$select=MetadataId,SchemaName,ReferencedEntity,ReferencingEntity,"
        "ReferencingAttribute,ReferencedEntityNavigationPropertyName,"
        "ReferencingEntityNavigationPropertyName,CascadeConfiguration",
        safe="$=,&'()",
    )
    return query_rows(api, "RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata", query)


def relationship_payload(spec: RelationshipSpec) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
        "SchemaName": spec.schema_name,
        "ReferencedEntity": spec.referenced_table,
        "ReferencingEntity": spec.referencing_table,
        "Lookup": {
            "@odata.type": "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
            "SchemaName": spec.lookup_schema_name,
            "DisplayName": label(spec.display_name),
            "Description": label(f"Relación V2 con {spec.display_name.lower()}."),
            "RequiredLevel": required_level(spec.required),
            "IsAuditEnabled": audit_enabled(),
        },
        "CascadeConfiguration": {
            "Assign": "NoCascade",
            "Delete": "Restrict",
            "Merge": "NoCascade",
            "Reparent": "NoCascade",
            "Share": "NoCascade",
            "Unshare": "NoCascade",
        },
    }


RELATIONSHIP_CASCADE_CONTRACT: dict[str, str] = {
    "Assign": "NoCascade",
    "Delete": "Restrict",
    "Merge": "NoCascade",
    "Reparent": "NoCascade",
    "Share": "NoCascade",
    "Unshare": "NoCascade",
}


def relationship_contract_differences(
    spec: RelationshipSpec, current: dict[str, Any] | None
) -> list[str]:
    if current is None:
        return [f"Falta la relacion contractual {spec.schema_name}."]

    differences: list[str] = []
    expected = {
        "SchemaName": spec.schema_name,
        "ReferencedEntity": spec.referenced_table,
        "ReferencingEntity": spec.referencing_table,
        "ReferencingAttribute": spec.lookup_logical_name,
        "ReferencedEntityNavigationPropertyName": spec.schema_name,
        "ReferencingEntityNavigationPropertyName": spec.lookup_schema_name,
    }
    for property_name, expected_value in expected.items():
        current_value = current.get(property_name)
        if not metadata_values_equal(current_value, expected_value):
            differences.append(
                f"{spec.schema_name}.{property_name}={current_value!r}; "
                f"se esperaba {expected_value!r}."
            )

    cascade = current.get("CascadeConfiguration") or {}
    for property_name, expected_value in RELATIONSHIP_CASCADE_CONTRACT.items():
        current_value = cascade.get(property_name)
        if not metadata_values_equal(current_value, expected_value):
            differences.append(
                f"{spec.schema_name}.CascadeConfiguration.{property_name}="
                f"{current_value!r}; se esperaba {expected_value!r}."
            )
    return differences


def relationship_lookup_contract_differences(
    spec: RelationshipSpec,
    current: dict[str, Any] | None,
    *,
    verify_audit: bool = True,
) -> list[str]:
    path = f"{spec.referencing_table}.{spec.lookup_logical_name}"
    if current is None:
        return [f"Falta el lookup contractual {path}."]

    differences: list[str] = []
    expected = {
        "SchemaName": spec.lookup_schema_name,
        "AttributeType": "Lookup",
        "AttributeTypeName": "LookupType",
        "IsSecured": False,
        "RequiredLevel": "ApplicationRequired" if spec.required else "None",
    }
    if verify_audit:
        expected["IsAuditEnabled"] = True
    for property_name, expected_value in expected.items():
        current_value = current.get(property_name)
        if not metadata_values_equal(current_value, expected_value):
            differences.append(
                f"{path}.{property_name}={managed_value(current_value)!r}; "
                f"se esperaba {expected_value!r}."
            )
    actual_targets = {
        str(value).casefold() for value in (current.get("Targets") or [])
    }
    expected_targets = {spec.referenced_table.casefold()}
    if actual_targets != expected_targets:
        differences.append(
            f"{path}.Targets={sorted(actual_targets)!r}; "
            f"se esperaba {sorted(expected_targets)!r}."
        )
    return differences


def lookup_attribute_metadata_path(
    spec: RelationshipSpec, *, typed: bool
) -> str:
    path = (
        f"EntityDefinitions(LogicalName='{spec.referencing_table}')/"
        f"Attributes(LogicalName='{spec.lookup_logical_name}')"
    )
    if typed:
        return path + "/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    return path


def get_lookup_attribute_definition(
    api: DataverseApi, spec: RelationshipSpec
) -> dict[str, Any]:
    current = api.request(
        "GET",
        lookup_attribute_metadata_path(spec, typed=True),
        allow_404=True,
    )
    if not current:
        raise ProvisioningError(
            f"Falta el lookup {spec.referencing_table}.{spec.lookup_logical_name}."
        )
    return current


def lookup_attribute_update_payload(current: dict[str, Any]) -> dict[str, Any]:
    # Dataverse metadata entities do not support PATCH. The supported update is
    # a PUT of the full definition. Preserve every returned property and change
    # only IsAuditEnabled.Value; drop response-only OData annotations.
    payload = copy.deepcopy(current)
    for property_name in tuple(payload):
        if property_name.startswith("@odata."):
            payload.pop(property_name, None)
    payload["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    audit = dict(payload.get("IsAuditEnabled") or {})
    audit["Value"] = True
    payload["IsAuditEnabled"] = audit
    return payload


def ensure_relationship_lookup_audit(
    api: DataverseApi, spec: RelationshipSpec, apply: bool
) -> str:
    current = get_lookup_attribute_definition(api, spec)
    non_audit_differences = relationship_lookup_contract_differences(
        spec, current, verify_audit=False
    )
    if non_audit_differences:
        raise ProvisioningError(
            "El lookup no admite la normalizacion acotada porque tiene otras "
            "diferencias:\n- "
            + "\n- ".join(non_audit_differences)
        )

    audit = current.get("IsAuditEnabled") or {}
    if bool(managed_value(audit)):
        return "AlreadyEnabled"
    if not bool(audit.get("CanBeChanged")):
        raise ProvisioningError(
            f"{spec.referencing_table}.{spec.lookup_logical_name}.IsAuditEnabled "
            "no puede modificarse."
        )
    if not apply:
        return "Disabled"

    api.request(
        "PUT",
        lookup_attribute_metadata_path(spec, typed=False),
        lookup_attribute_update_payload(current),
        solution=True,
        merge_labels=True,
    )

    for attempt in range(20):
        refreshed = get_lookup_attribute_definition(api, spec)
        differences = relationship_lookup_contract_differences(spec, refreshed)
        if not differences:
            return "Enabled"
        remaining = relationship_lookup_contract_differences(
            spec, refreshed, verify_audit=False
        )
        if remaining:
            raise ProvisioningError(
                "El read-back del lookup cambio fuera de IsAuditEnabled:\n- "
                + "\n- ".join(remaining)
            )
        if attempt < 19:
            time.sleep(2)
    raise ProvisioningError(
        f"El read-back no confirmo auditoria en "
        f"{spec.referencing_table}.{spec.lookup_logical_name}."
    )


def ensure_relationship(
    api: DataverseApi, spec: RelationshipSpec, apply: bool
) -> dict[str, Any] | None:
    current = next(
        (
            row
            for row in get_relationships(api)
            if str(row.get("SchemaName", "")).casefold() == spec.schema_name.casefold()
        ),
        None,
    )
    if current is None and apply:
        retry(
            lambda: api.request(
                "POST", "RelationshipDefinitions", relationship_payload(spec), solution=True
            ),
            f"crear relación {spec.schema_name}",
        )
        for attempt in range(20):
            current = next(
                (
                    row
                    for row in get_relationships(api)
                    if str(row.get("SchemaName", "")).casefold()
                    == spec.schema_name.casefold()
                ),
                None,
            )
            if current is not None:
                break
            if attempt < 19:
                time.sleep(2)
    if current is None:
        return None
    differences = relationship_contract_differences(spec, current)
    if differences:
        raise ProvisioningError(
            "La relacion existente no coincide con el contrato:\n- "
            + "\n- ".join(differences)
        )
    return current


def get_choice_options(api: DataverseApi, table: str, attribute: str) -> list[dict[str, Any]]:
    return api.request(
        "GET",
        f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{attribute}')/"
        "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "?$select=LogicalName&$expand=OptionSet($select=Options)",
    ).get("OptionSet", {}).get("Options", [])


def option_label(row: dict[str, Any]) -> str:
    labels = (row.get("Label") or {}).get("LocalizedLabels") or []
    preferred = next(
        (item for item in labels if item.get("LanguageCode") == LANGUAGE_CODE), None
    )
    return str((preferred or (labels[0] if labels else {})).get("Label", ""))


def verify_choice(
    api: DataverseApi, table: str, attribute: str, expected_labels: tuple[str, ...]
) -> dict[str, int]:
    actual = {
        option_label(row): int(row["Value"])
        for row in get_choice_options(api, table, attribute)
        if row.get("Value") is not None
    }
    if set(actual) != set(expected_labels):
        raise ProvisioningError(
            f"{table}.{attribute} tiene opciones {sorted(actual)}; "
            f"se esperaban {sorted(expected_labels)}."
        )
    if len(set(actual.values())) != len(actual):
        raise ProvisioningError(f"{table}.{attribute} repite valores numéricos.")
    for value in actual.values():
        if value // 10000 != PUBLISHER_OPTION_PREFIX:
            raise ProvisioningError(
                f"{table}.{attribute} contiene {value}, fuera del publisher {PUBLISHER_OPTION_PREFIX}."
            )
    return {label_text: actual[label_text] for label_text in expected_labels}


def publish(api: DataverseApi) -> None:
    entities = "".join(
        f"<entity>{table.logical_name}</entity>" for table in TABLES
    )
    api.request(
        "POST",
        "PublishXml",
        {"ParameterXml": f"<importexportxml><entities>{entities}</entities></importexportxml>"},
    )


def verify_all(api: DataverseApi) -> dict[str, Any]:
    publisher = ensure_publisher(api, False)
    solution = ensure_solution(api, False)
    if publisher is None or solution is None:
        return {
            "ready": False,
            "environment": ENVIRONMENT_URL,
            "publisherExists": publisher is not None,
            "solutionExists": solution is not None,
        }

    differences: list[str] = []
    tables_result: dict[str, Any] = {}
    table_metadata: dict[str, dict[str, Any]] = {}
    for table in TABLES:
        metadata = get_table(api, table.logical_name)
        table_differences = table_contract_differences(table, metadata)
        differences.extend(table_differences)
        if metadata is None:
            tables_result[table.logical_name] = {
                "exists": False,
                "contractVerified": False,
            }
            continue
        table_metadata[table.logical_name] = metadata
        attributes = get_attributes(api, table.logical_name)
        expected_names = {
            table.primary_name_logical,
            *(column.logical_name for column in table.columns),
        }
        missing = sorted(expected_names - set(attributes))
        verified_columns, column_differences = verify_column_contract(api, table)
        differences.extend(column_differences)
        optimistic = bool(metadata.get("IsOptimisticConcurrencyEnabled"))
        tables_result[table.logical_name] = {
            "exists": True,
            "contractVerified": not table_differences and not column_differences,
            "metadataId": metadata.get("MetadataId"),
            "entitySetName": metadata.get("EntitySetName"),
            "primaryIdAttribute": metadata.get("PrimaryIdAttribute"),
            "primaryNameAttribute": metadata.get("PrimaryNameAttribute"),
            "auditEnabled": bool(managed_value(metadata.get("IsAuditEnabled"))),
            "optimisticConcurrencyEnabled": optimistic,
            "optimisticConcurrencyRequired": table.optimistic_concurrency_required,
            "contractColumns": verified_columns,
            "missingColumns": missing,
        }

    base_result: dict[str, Any] = {
        "ready": False,
        "environment": ENVIRONMENT_URL,
        "publisher": {
            "id": publisher.get("publisherid"),
            "uniqueName": publisher.get("uniquename"),
            "prefix": publisher.get("customizationprefix"),
            "optionValuePrefix": publisher.get("customizationoptionvalueprefix"),
        },
        "solution": {
            "id": solution.get("solutionid"),
            "uniqueName": solution.get("uniquename"),
            "version": solution.get("version"),
            "managed": solution.get("ismanaged"),
        },
        "externalBindings": {
            "client": {"logicalName": CLIENT_TABLE, "entitySetName": CLIENT_ENTITY_SET},
            "equipment": {"logicalName": EQUIPMENT_TABLE, "entitySetName": EQUIPMENT_ENTITY_SET},
        },
        "tables": tables_result,
        "differences": differences,
    }
    if len(table_metadata) != len(TABLES):
        return base_result

    key_status: dict[str, str] = {}
    for key in KEYS:
        try:
            status = ensure_key(api, key, False)
        except ProvisioningError as error:
            status = "Invalid"
            differences.append(str(error))
        key_status[key.schema_name] = status
        if status != "Active":
            differences.append(
                f"La clave {key.schema_name} esta {status}; se esperaba Active."
            )

    relationship_rows = get_relationships(api)
    relationship_by_name = {
        str(row.get("SchemaName", "")).casefold(): row
        for row in relationship_rows
        if row.get("SchemaName")
    }
    lookup_metadata = {
        table_name: get_typed_attributes(api, table_name, "Lookup")
        for table_name in {spec.referencing_table for spec in RELATIONSHIPS}
    }
    relationship_result: dict[str, Any] = {}
    for spec in RELATIONSHIPS:
        row = relationship_by_name.get(spec.schema_name.casefold())
        relationship_differences = relationship_contract_differences(spec, row)
        lookup = lookup_metadata[spec.referencing_table].get(
            spec.lookup_logical_name.casefold()
        )
        lookup_differences = relationship_lookup_contract_differences(spec, lookup)
        differences.extend(relationship_differences)
        differences.extend(lookup_differences)
        relationship_result[spec.schema_name] = {
            "exists": row is not None,
            "contractVerified": not relationship_differences and not lookup_differences,
            "referencingAttribute": row.get("ReferencingAttribute") if row else None,
            "referencingNavigationProperty": (
                row.get("ReferencingEntityNavigationPropertyName") if row else None
            ),
            "referencedNavigationProperty": (
                row.get("ReferencedEntityNavigationPropertyName") if row else None
            ),
            "cascade": row.get("CascadeConfiguration") if row else None,
            "lookupAuditEnabled": (
                bool(managed_value(lookup.get("IsAuditEnabled"))) if lookup else None
            ),
            "lookupRequiredLevel": (
                managed_value(lookup.get("RequiredLevel")) if lookup else None
            ),
        }

    choice_contracts = (
        ("workflowState", MAIN_TABLE, "dtc_workflowstate", WORKFLOW_CHOICES),
        ("emailState", MAIN_TABLE, "dtc_emailstate", EMAIL_CHOICES),
        (
            "maintenanceType",
            MAIN_TABLE,
            "dtc_maintenancetype",
            MAINTENANCE_CHOICES,
        ),
        (
            "evidencePurpose",
            EVIDENCE_TABLE,
            "dtc_purpose",
            EVIDENCE_PURPOSE_CHOICES,
        ),
        (
            "evidenceSecurityState",
            EVIDENCE_TABLE,
            "dtc_securitystate",
            EVIDENCE_SECURITY_CHOICES,
        ),
    )
    choices: dict[str, Any] = {}
    for name, table_name, attribute, labels in choice_contracts:
        try:
            choices[name] = verify_choice(api, table_name, attribute, labels)
        except ProvisioningError as error:
            choices[name] = {"verified": False, "error": str(error)}
            differences.append(str(error))

    ready = (
        not differences
        and all(item.get("contractVerified") for item in tables_result.values())
        and all(status == "Active" for status in key_status.values())
        and all(item.get("contractVerified") for item in relationship_result.values())
    )
    return {
        **base_result,
        "ready": ready,
        "alternateKeys": key_status,
        "relationships": relationship_result,
        "choices": choices,
        "differences": differences,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Crea componentes V2 faltantes. Sin esta opción solo verifica.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    api = DataverseApi(allow_writes=args.apply)
    print(
        f"environment={ENVIRONMENT_URL} solution={SOLUTION_NAME} "
        f"publisher={PUBLISHER_UNIQUE_NAME} prefix={PUBLISHER_PREFIX}_ apply={args.apply}",
        flush=True,
    )
    if not args.apply:
        result = verify_all(api)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if result.get("ready") else 2

    solution = ensure_solution(api, args.apply)
    if solution is None:
        result = verify_all(api)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 2

    table_metadata: dict[str, dict[str, Any]] = {}
    for table in TABLES:
        metadata = ensure_table(api, table, args.apply)
        if metadata is not None:
            table_metadata[table.logical_name] = metadata
    if args.apply:
        for table in TABLES:
            metadata = table_metadata[table.logical_name]
            ensure_columns(api, table, str(metadata["MetadataId"]), True)
        # Keys are created only after every scalar column is readable.
        for key in KEYS:
            status = ensure_key(api, key, True)
            print(f"  key {key.schema_name}: {status}", flush=True)
        # Relationships are created only after table/key propagation.
        for relationship in RELATIONSHIPS:
            current = ensure_relationship(api, relationship, True)
            if current is None:
                raise ProvisioningError(
                    f"No se pudo crear la relación {relationship.schema_name}."
                )
            audit_status = ensure_relationship_lookup_audit(
                api, relationship, True
            )
            print(
                f"  relationship {relationship.schema_name}: ready; "
                f"lookupAudit={audit_status}",
                flush=True,
            )
        publish(api)

    result = verify_all(api)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get("ready") else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Provisioning cancelled.", file=sys.stderr)
        raise SystemExit(130)
    except Exception as error:
        print(f"Provisioning failed: {error}", file=sys.stderr)
        raise SystemExit(1)
