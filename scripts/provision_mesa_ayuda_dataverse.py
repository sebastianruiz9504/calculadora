"""Provision the Mesa de ayuda Dataverse schema.

This script is intentionally environment-specific and idempotent. It refuses
to run unless ``.env`` points to the user-confirmed Digital Tech environment.

Examples:

    python -u scripts/provision_mesa_ayuda_dataverse.py --phase solution
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase schema
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase keys
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase relationships
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase seed
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase verify
    python -u scripts/provision_mesa_ayuda_dataverse.py --phase all

The official PowerPlatform-Dataverse-Client SDK is used for solution records,
relationships, alternate keys, and seed records. The Web API is restricted to
advanced metadata that the Python SDK does not expose completely: ownership,
notes, exact column metadata, autonumber, auditing, and unbound solution actions.
No secret or access token is persisted.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any, Callable, Iterable, Sequence


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEB_ROOT = os.path.dirname(SCRIPT_DIR)
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from auth import get_client, get_plugin_headers, get_token, load_env


TARGET_URL = "https://orgc79ca19c.crm2.dynamics.com"
SOLUTION_NAME = "MesaAyudaDigitalTech"
SOLUTION_DISPLAY_NAME = "Mesa de Ayuda Digital Tech"
SOLUTION_VERSION = "1.0.0.0"
PUBLISHER_UNIQUE_NAME = "DigitalTech"
PUBLISHER_PREFIX = "hd"
BASE_LANGUAGE = 3082
TICKET_TABLE = "cr07a_ticket"
TICKET_SCHEMA = "cr07a_Ticket"
CLIENT_TABLE = "cr07a_cliente"
SYSTEM_USER_TABLE = "systemuser"
CASE_NUMBER_FORMAT = "TKT-{DATETIMEUTC:yyyy}-{SEQNUM:6}"
PHASE_ORDER = ("solution", "schema", "keys", "relationships", "seed", "verify")

MAILBOXES = (
    "sruiz@digitaltechcolombia.com",
    "abarriga@digitaltechcolombia.com",
    "dmarentes@digitaltechcolombia.com",
)


class ProvisioningError(RuntimeError):
    """Raised when the requested schema cannot be provisioned safely."""


class WebApiError(ProvisioningError):
    """A Dataverse Web API response with useful status and body details."""

    def __init__(
        self,
        method: str,
        url: str,
        status: int,
        body: str,
        retry_after: str | None = None,
    ) -> None:
        self.method = method
        self.url = url
        self.status = status
        self.body = body
        self.retry_after = retry_after
        super().__init__(f"{method} {url} failed with HTTP {status}: {body}")


@dataclass(frozen=True)
class ColumnSpec:
    logical_name: str
    schema_name: str
    display_name: str
    attribute_type: str
    payload: dict[str, Any]


@dataclass(frozen=True)
class TableSpec:
    logical_name: str
    schema_name: str
    display_name: str
    display_collection_name: str
    description: str
    ownership_type: str
    has_notes: bool
    primary_name_schema: str
    primary_name_logical: str
    primary_name_display: str
    primary_name_max_length: int
    columns: tuple[ColumnSpec, ...]


@dataclass(frozen=True)
class RelationshipSpec:
    schema_name: str
    lookup_schema_name: str
    lookup_logical_name: str
    display_name: str
    referenced_table: str
    referenced_attribute: str
    referencing_table: str


@dataclass(frozen=True)
class KeySpec:
    table_schema_name: str
    table_logical_name: str
    schema_name: str
    display_name: str
    columns: tuple[str, ...]


def label(text: str) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [
            {
                "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
                "Label": text,
                "LanguageCode": BASE_LANGUAGE,
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


def default_schema_name(logical_name: str) -> str:
    prefix, separator, suffix = logical_name.partition("_")
    if not separator or not suffix:
        raise ValueError(f"Custom logical name must contain a prefix: {logical_name}")
    return f"{prefix}_{suffix[0].upper()}{suffix[1:]}"


def column_base(
    logical_name: str,
    display_name: str,
    description: str,
    required: bool,
    schema_name: str | None = None,
) -> dict[str, Any]:
    return {
        "SchemaName": schema_name or default_schema_name(logical_name),
        "DisplayName": label(display_name),
        "Description": label(description),
        "RequiredLevel": required_level(required),
        "IsAuditEnabled": audit_enabled(),
    }


def string_column(
    logical_name: str,
    display_name: str,
    max_length: int,
    description: str,
    *,
    required: bool = False,
    schema_name: str | None = None,
    auto_number_format: str | None = None,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": "Text"},
            "MaxLength": max_length,
        }
    )
    if auto_number_format:
        payload["AutoNumberFormat"] = auto_number_format
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "String",
        payload,
    )


def memo_column(
    logical_name: str,
    display_name: str,
    description: str,
    *,
    required: bool = False,
    schema_name: str | None = None,
    max_length: int = 1_048_576,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
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
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "Memo",
        payload,
    )


def datetime_column(
    logical_name: str,
    display_name: str,
    description: str,
    *,
    required: bool = False,
    schema_name: str | None = None,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
            "AttributeType": "DateTime",
            "AttributeTypeName": {"Value": "DateTimeType"},
            "Format": "DateAndTime",
            "DateTimeBehavior": {"Value": "UserLocal"},
        }
    )
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "DateTime",
        payload,
    )


def bool_column(
    logical_name: str,
    display_name: str,
    description: str,
    *,
    default: bool = False,
    required: bool = False,
    schema_name: str | None = None,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
            "AttributeType": "Boolean",
            "AttributeTypeName": {"Value": "BooleanType"},
            "DefaultValue": default,
            "OptionSet": {
                "TrueOption": {"Value": 1, "Label": label("Si")},
                "FalseOption": {"Value": 0, "Label": label("No")},
            },
        }
    )
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "Boolean",
        payload,
    )


def decimal_column(
    logical_name: str,
    display_name: str,
    description: str,
    *,
    minimum: float = 0.0,
    maximum: float = 1.0,
    precision: int = 4,
    required: bool = False,
    schema_name: str | None = None,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            "AttributeType": "Decimal",
            "AttributeTypeName": {"Value": "DecimalType"},
            "MinValue": minimum,
            "MaxValue": maximum,
            "Precision": precision,
        }
    )
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "Decimal",
        payload,
    )


def integer_column(
    logical_name: str,
    display_name: str,
    description: str,
    *,
    minimum: int = 0,
    maximum: int = 2_147_483_647,
    required: bool = False,
    schema_name: str | None = None,
) -> ColumnSpec:
    resolved_schema = schema_name or default_schema_name(logical_name)
    payload = column_base(
        logical_name,
        display_name,
        description,
        required,
        resolved_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
            "AttributeType": "Integer",
            "AttributeTypeName": {"Value": "IntegerType"},
            "Format": "None",
            "MinValue": minimum,
            "MaxValue": maximum,
        }
    )
    return ColumnSpec(
        logical_name,
        resolved_schema,
        display_name,
        "Integer",
        payload,
    )


TICKET_COLUMNS = (
    string_column(
        "hd_casenumber",
        "Consecutivo",
        100,
        "Consecutivo inmutable del caso de soporte.",
        schema_name="hd_CaseNumber",
        auto_number_format=CASE_NUMBER_FORMAT,
    ),
    string_column(
        "hd_sourcechannel",
        "Canal de origen",
        40,
        "Canal normalizado que origino el caso.",
        schema_name="hd_SourceChannel",
    ),
    string_column(
        "hd_receivemailbox",
        "Buzon receptor",
        320,
        "Buzon de Digital Tech que recibio el mensaje.",
        schema_name="hd_ReceiveMailbox",
    ),
    string_column(
        "hd_externalconversation",
        "Conversacion externa",
        512,
        "Identificador de la conversacion en el canal de origen.",
        schema_name="hd_ExternalConversation",
    ),
    string_column(
        "hd_externalcasekey",
        "Clave externa del caso",
        64,
        (
            "SHA-256 estable del canal, buzon receptor y conversacion externa; "
            "evita tickets duplicados bajo reintentos concurrentes."
        ),
        schema_name="hd_ExternalCaseKey",
    ),
    string_column(
        "hd_requestername",
        "Nombre solicitante",
        200,
        "Nombre informado por quien solicita soporte.",
        schema_name="hd_RequesterName",
    ),
    string_column(
        "hd_requesteremail",
        "Correo solicitante",
        320,
        "Correo normalizado de quien solicita soporte.",
        schema_name="hd_RequesterEmail",
    ),
    string_column(
        "hd_aiclassification",
        "Clasificacion IA",
        40,
        "Clasificacion vigente: support, no_support o doubtful.",
        schema_name="hd_AiClassification",
    ),
    decimal_column(
        "hd_aiconfidence",
        "Confianza IA",
        "Confianza entre cero y uno de la clasificacion vigente.",
        schema_name="hd_AiConfidence",
    ),
    string_column(
        "hd_aiseverity",
        "Severidad IA",
        40,
        "Severidad tecnica vigente calculada por IA.",
        schema_name="hd_AiSeverity",
    ),
    memo_column(
        "hd_aisummary",
        "Resumen IA",
        "Resumen operativo vigente producido por la investigacion.",
        schema_name="hd_AiSummary",
    ),
    string_column(
        "hd_automationstatus",
        "Estado de automatizacion",
        80,
        "Estado de ingestion, investigacion o cierre automatico.",
        schema_name="hd_AutomationStatus",
    ),
    datetime_column(
        "hd_lastactivityat",
        "Ultima actividad",
        "Fecha y hora de la ultima interaccion del caso.",
        schema_name="hd_LastActivityAt",
    ),
    datetime_column(
        "hd_tenantconfirmedat",
        "Tenant confirmado el",
        "Fecha y hora en que un agente confirmo el tenant objetivo.",
        schema_name="hd_TenantConfirmedAt",
    ),
)


TABLE_SPECS = (
    TableSpec(
        logical_name="hd_supportchannel",
        schema_name="hd_SupportChannel",
        display_name="Canal de soporte",
        display_collection_name="Canales de soporte",
        description="Canales autorizados para recibir y responder casos.",
        ownership_type="OrganizationOwned",
        has_notes=False,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            string_column(
                "hd_channelkey",
                "Clave del canal",
                120,
                "Clave estable e idempotente del canal.",
                required=True,
                schema_name="hd_ChannelKey",
            ),
            string_column(
                "hd_address",
                "Direccion",
                320,
                "Direccion exacta del buzon o canal.",
                required=True,
                schema_name="hd_Address",
            ),
            string_column(
                "hd_channeltype",
                "Tipo de canal",
                40,
                "Tipo extensible del canal, inicialmente email.",
                required=True,
                schema_name="hd_ChannelType",
            ),
            bool_column(
                "hd_active",
                "Activo",
                "Indica si el canal puede ingerir nuevos mensajes.",
                default=True,
                required=True,
                schema_name="hd_Active",
            ),
            string_column(
                "hd_subscriptionkey",
                "Clave de suscripcion",
                200,
                "Identificador no secreto de la suscripcion del proveedor.",
                schema_name="hd_SubscriptionKey",
            ),
            string_column(
                "hd_subscriptionresource",
                "Recurso de suscripcion",
                1000,
                "Recurso remoto cubierto por la suscripcion.",
                schema_name="hd_SubscriptionResource",
            ),
            datetime_column(
                "hd_subscriptionexpiresat",
                "Expiracion de suscripcion",
                "Fecha y hora de expiracion de la suscripcion.",
                schema_name="hd_SubscriptionExpiresAt",
            ),
            memo_column(
                "hd_deltalink",
                "Delta link",
                "Punto de continuacion para sincronizacion incremental.",
                schema_name="hd_DeltaLink",
            ),
            datetime_column(
                "hd_lastsyncat",
                "Ultima sincronizacion",
                "Fecha y hora de la ultima sincronizacion.",
                schema_name="hd_LastSyncAt",
            ),
            string_column(
                "hd_lastsyncstatus",
                "Estado de sincronizacion",
                80,
                "Resultado normalizado de la ultima sincronizacion.",
                schema_name="hd_LastSyncStatus",
            ),
            memo_column(
                "hd_lastsyncerror",
                "Error de sincronizacion",
                "Detalle sanitizado del ultimo error de sincronizacion.",
                schema_name="hd_LastSyncError",
            ),
            datetime_column(
                "hd_lastmessageat",
                "Ultimo mensaje",
                "Fecha y hora del ultimo mensaje observado.",
                schema_name="hd_LastMessageAt",
            ),
        ),
    ),
    TableSpec(
        logical_name="hd_customertenant",
        schema_name="hd_CustomerTenant",
        display_name="Tenant de cliente",
        display_collection_name="Tenants de cliente",
        description="Identidad canonica y relacion GDAP de un tenant cliente.",
        ownership_type="OrganizationOwned",
        has_notes=True,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            string_column(
                "hd_tenantguid",
                "Tenant GUID",
                36,
                "GUID canonico del tenant confirmado.",
                required=True,
                schema_name="hd_TenantGuid",
            ),
            string_column(
                "hd_primarydomain",
                "Dominio principal",
                253,
                "Dominio principal verificado del tenant.",
                schema_name="hd_PrimaryDomain",
            ),
            string_column(
                "hd_partnersourcekey",
                "Clave de cliente partner",
                100,
                "Identificador del cliente en Partner Center.",
                schema_name="hd_PartnerSourceKey",
            ),
            string_column(
                "hd_gdaprelationshipkey",
                "Clave de relacion GDAP",
                100,
                "Identificador de la relacion GDAP confirmada.",
                schema_name="hd_GdapRelationshipKey",
            ),
            string_column(
                "hd_gdaprelationshipstatus",
                "Estado de relacion GDAP",
                80,
                "Estado observado de la relacion GDAP.",
                schema_name="hd_GdapRelationshipStatus",
            ),
            datetime_column(
                "hd_lastvalidatedat",
                "Ultima validacion",
                "Fecha y hora de la ultima validacion del tenant.",
                schema_name="hd_LastValidatedAt",
            ),
            memo_column(
                "hd_capabilitiesjson",
                "Capacidades JSON",
                "Capacidades delegadas observadas, sin credenciales.",
                schema_name="hd_CapabilitiesJson",
            ),
            bool_column(
                "hd_active",
                "Activo",
                "Indica si el tenant puede seleccionarse para nuevos casos.",
                default=True,
                required=True,
                schema_name="hd_Active",
            ),
        ),
    ),
    TableSpec(
        logical_name="hd_supportinteraction",
        schema_name="hd_SupportInteraction",
        display_name="Interaccion de soporte",
        display_collection_name="Interacciones de soporte",
        description="Bitacora cronologica e inmutable del chat y los canales.",
        ownership_type="UserOwned",
        has_notes=True,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            string_column(
                "hd_interactionkey",
                "Clave de interaccion",
                200,
                "Clave estable de la interaccion.",
                required=True,
                schema_name="hd_InteractionKey",
            ),
            datetime_column(
                "hd_eventat",
                "Fecha del evento",
                "Fecha y hora canonica del evento.",
                required=True,
                schema_name="hd_EventAt",
            ),
            string_column(
                "hd_interactiontype",
                "Tipo de interaccion",
                80,
                "Tipo extensible: email, chat, audit, approval o system.",
                required=True,
                schema_name="hd_InteractionType",
            ),
            string_column(
                "hd_direction",
                "Direccion",
                40,
                "Direccion del mensaje: inbound, outbound o internal.",
                required=True,
                schema_name="hd_Direction",
            ),
            string_column(
                "hd_actortype",
                "Tipo de actor",
                40,
                "Tipo de actor: customer, agent, ai o system.",
                required=True,
                schema_name="hd_ActorType",
            ),
            string_column(
                "hd_actorname",
                "Nombre del actor",
                200,
                "Nombre visible del actor.",
                schema_name="hd_ActorName",
            ),
            string_column(
                "hd_actoraddress",
                "Direccion del actor",
                320,
                "Correo u otra direccion normalizada del actor.",
                schema_name="hd_ActorAddress",
            ),
            string_column(
                "hd_subject",
                "Asunto",
                500,
                "Asunto del mensaje o evento.",
                schema_name="hd_Subject",
            ),
            memo_column(
                "hd_content",
                "Contenido",
                "Contenido textual sanitizado de la interaccion.",
                schema_name="hd_Content",
            ),
            memo_column(
                "hd_structuredjson",
                "Contenido estructurado JSON",
                "Datos estructurados de la interaccion.",
                schema_name="hd_StructuredJson",
            ),
            string_column(
                "hd_immutablemessagekey",
                "Clave inmutable del mensaje",
                512,
                "Identificador inmutable del mensaje del proveedor.",
                schema_name="hd_ImmutableMessageKey",
            ),
            string_column(
                "hd_internetmessagekey",
                "Clave Internet Message",
                512,
                "Internet Message ID cuando el canal es correo.",
                schema_name="hd_InternetMessageKey",
            ),
            string_column(
                "hd_conversationkey",
                "Clave de conversacion",
                512,
                "Identificador externo de la conversacion.",
                schema_name="hd_ConversationKey",
            ),
            string_column(
                "hd_modelresponsekey",
                "Clave de respuesta del modelo",
                256,
                "Identificador de la respuesta del modelo.",
                schema_name="hd_ModelResponseKey",
            ),
            string_column(
                "hd_classification",
                "Clasificacion",
                40,
                "Clasificacion de soporte asociada a la interaccion.",
                schema_name="hd_Classification",
            ),
            decimal_column(
                "hd_confidence",
                "Confianza",
                "Confianza entre cero y uno.",
                schema_name="hd_Confidence",
            ),
            string_column(
                "hd_triagestatus",
                "Estado de triage",
                80,
                "Estado del analisis y triage.",
                schema_name="hd_TriageStatus",
            ),
            string_column(
                "hd_deliverystatus",
                "Estado de entrega",
                80,
                "Estado de entrega al canal externo.",
                schema_name="hd_DeliveryStatus",
            ),
            bool_column(
                "hd_visiblecustomer",
                "Visible para cliente",
                "Indica si el contenido puede mostrarse o enviarse al cliente.",
                default=False,
                required=True,
                schema_name="hd_VisibleCustomer",
            ),
            string_column(
                "hd_deduplicationkey",
                "Clave de deduplicacion",
                200,
                "Clave estable para impedir eventos repetidos.",
                schema_name="hd_DeduplicationKey",
            ),
            string_column(
                "hd_idempotencykey",
                "Clave de idempotencia",
                200,
                "Clave para repetir operaciones sin duplicar efectos.",
                schema_name="hd_IdempotencyKey",
            ),
            integer_column(
                "hd_sequence",
                "Secuencia",
                "Orden monotono de la interaccion dentro del ticket.",
                required=True,
                schema_name="hd_Sequence",
            ),
        ),
    ),
    TableSpec(
        logical_name="hd_investigation",
        schema_name="hd_Investigation",
        display_name="Investigacion",
        display_collection_name="Investigaciones",
        description="Ejecucion auditable de investigacion asistida por IA.",
        ownership_type="UserOwned",
        has_notes=True,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            string_column(
                "hd_runkey",
                "Clave de ejecucion",
                200,
                "Clave estable de la ejecucion de investigacion.",
                required=True,
                schema_name="hd_RunKey",
            ),
            string_column(
                "hd_status",
                "Estado",
                80,
                "Estado vigente de la investigacion.",
                required=True,
                schema_name="hd_Status",
            ),
            datetime_column(
                "hd_startedat",
                "Inicio",
                "Fecha y hora de inicio.",
                required=True,
                schema_name="hd_StartedAt",
            ),
            datetime_column(
                "hd_completedat",
                "Finalizacion",
                "Fecha y hora de finalizacion.",
                schema_name="hd_CompletedAt",
            ),
            string_column(
                "hd_model",
                "Modelo",
                120,
                "Modelo exacto utilizado.",
                schema_name="hd_Model",
            ),
            string_column(
                "hd_modelresponsekey",
                "Clave de respuesta del modelo",
                256,
                "Identificador de la respuesta del modelo.",
                schema_name="hd_ModelResponseKey",
            ),
            string_column(
                "hd_inputhash",
                "Hash de entrada",
                128,
                "SHA-256 del contexto canonico de entrada.",
                schema_name="hd_InputHash",
            ),
            memo_column(
                "hd_resultjson",
                "Resultado JSON",
                "Resultado estructurado completo de la investigacion.",
                schema_name="hd_ResultJson",
            ),
            memo_column(
                "hd_summary",
                "Resumen",
                "Resumen operativo de la investigacion.",
                schema_name="hd_Summary",
            ),
            string_column(
                "hd_initiatedbyoid",
                "OID del iniciador",
                36,
                "OID de Entra del agente que inicio la investigacion.",
                schema_name="hd_InitiatedByOid",
            ),
            memo_column(
                "hd_error",
                "Error",
                "Detalle sanitizado de un error de investigacion.",
                schema_name="hd_Error",
            ),
        ),
    ),
    TableSpec(
        logical_name="hd_evidence",
        schema_name="hd_Evidence",
        display_name="Evidencia",
        display_collection_name="Evidencias",
        description="Evidencia tecnica reproducible obtenida durante la auditoria.",
        ownership_type="UserOwned",
        has_notes=True,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            string_column(
                "hd_evidencekey",
                "Clave de evidencia",
                200,
                "Clave estable de la evidencia.",
                required=True,
                schema_name="hd_EvidenceKey",
            ),
            datetime_column(
                "hd_capturedat",
                "Capturada el",
                "Fecha y hora de captura.",
                required=True,
                schema_name="hd_CapturedAt",
            ),
            string_column(
                "hd_toolname",
                "Herramienta",
                150,
                "Nombre de la herramienta que genero la evidencia.",
                required=True,
                schema_name="hd_ToolName",
            ),
            string_column(
                "hd_toolversion",
                "Version de herramienta",
                80,
                "Version exacta de la herramienta.",
                schema_name="hd_ToolVersion",
            ),
            string_column(
                "hd_resourcekey",
                "Clave del recurso",
                850,
                "Identidad canonica del recurso auditado.",
                schema_name="hd_ResourceKey",
            ),
            string_column(
                "hd_operation",
                "Operacion",
                160,
                "Consulta u operacion ejecutada.",
                required=True,
                schema_name="hd_Operation",
            ),
            memo_column(
                "hd_summary",
                "Resumen",
                "Resumen legible de la evidencia.",
                schema_name="hd_Summary",
            ),
            memo_column(
                "hd_payloadjson",
                "Carga util JSON",
                "Carga util estructurada y sanitizada.",
                schema_name="hd_PayloadJson",
            ),
            string_column(
                "hd_correlationkey",
                "Clave de correlacion",
                200,
                "Identificador de correlacion de la herramienta.",
                schema_name="hd_CorrelationKey",
            ),
            string_column(
                "hd_evidencehash",
                "Hash de evidencia",
                128,
                "SHA-256 de la evidencia canonica.",
                schema_name="hd_EvidenceHash",
            ),
            string_column(
                "hd_artifacturl",
                "URL de artefacto",
                1000,
                "URL protegida del artefacto asociado.",
                schema_name="hd_ArtifactUrl",
            ),
            memo_column(
                "hd_tenantsnapshotjson",
                "Snapshot del tenant JSON",
                "Identidad y alcance del tenant observados al capturar.",
                schema_name="hd_TenantSnapshotJson",
            ),
        ),
    ),
    TableSpec(
        logical_name="hd_changeplan",
        schema_name="hd_ChangePlan",
        display_name="Plan de cambio",
        display_collection_name="Planes de cambio",
        description="Plan versionado, aprobacion y resultado de una escritura supervisada.",
        ownership_type="UserOwned",
        has_notes=True,
        primary_name_schema="hd_Name",
        primary_name_logical="hd_name",
        primary_name_display="Nombre",
        primary_name_max_length=200,
        columns=(
            integer_column(
                "hd_version",
                "Version",
                "Version monotona del plan.",
                minimum=1,
                required=True,
                schema_name="hd_Version",
            ),
            string_column(
                "hd_environmentkey",
                "Clave del ambiente",
                200,
                "Identidad canonica del ambiente objetivo.",
                required=True,
                schema_name="hd_EnvironmentKey",
            ),
            string_column(
                "hd_resourcekey",
                "Clave del recurso",
                850,
                "Identidad canonica del recurso objetivo.",
                required=True,
                schema_name="hd_ResourceKey",
            ),
            string_column(
                "hd_toolname",
                "Herramienta",
                150,
                "Herramienta exacta propuesta para ejecutar.",
                required=True,
                schema_name="hd_ToolName",
            ),
            string_column(
                "hd_toolversion",
                "Version de herramienta",
                80,
                "Version exacta de la herramienta.",
                required=True,
                schema_name="hd_ToolVersion",
            ),
            memo_column(
                "hd_argumentsjson",
                "Argumentos JSON",
                "Argumentos canonicos de la herramienta.",
                required=True,
                schema_name="hd_ArgumentsJson",
            ),
            memo_column(
                "hd_beforestatejson",
                "Estado anterior JSON",
                "Estado observado antes del cambio.",
                required=True,
                schema_name="hd_BeforeStateJson",
            ),
            memo_column(
                "hd_proposedstatejson",
                "Estado propuesto JSON",
                "Estado exacto que quedaria despues del cambio.",
                required=True,
                schema_name="hd_ProposedStateJson",
            ),
            string_column(
                "hd_statefingerprint",
                "Huella del estado",
                128,
                "Huella del recurso que debe coincidir al ejecutar.",
                required=True,
                schema_name="hd_StateFingerprint",
            ),
            memo_column(
                "hd_impact",
                "Impacto",
                "Impacto esperado del cambio.",
                required=True,
                schema_name="hd_Impact",
            ),
            string_column(
                "hd_risk",
                "Riesgo",
                40,
                "Nivel de riesgo normalizado.",
                required=True,
                schema_name="hd_Risk",
            ),
            memo_column(
                "hd_verificationstrategy",
                "Estrategia de verificacion",
                "Comprobacion posterior exacta.",
                required=True,
                schema_name="hd_VerificationStrategy",
            ),
            memo_column(
                "hd_rollbackstrategy",
                "Estrategia de rollback",
                "Procedimiento de reversion previsto.",
                required=True,
                schema_name="hd_RollbackStrategy",
            ),
            datetime_column(
                "hd_expiresat",
                "Expira el",
                "Fecha y hora de expiracion del plan.",
                required=True,
                schema_name="hd_ExpiresAt",
            ),
            string_column(
                "hd_idempotencykey",
                "Clave de idempotencia",
                200,
                "Clave estable de ejecucion unica.",
                required=True,
                schema_name="hd_IdempotencyKey",
            ),
            memo_column(
                "hd_canonicalplanjson",
                "Plan canonico JSON",
                "Representacion canonica congelada del plan.",
                required=True,
                schema_name="hd_CanonicalPlanJson",
            ),
            string_column(
                "hd_plansha256",
                "SHA-256 del plan",
                64,
                "Hash SHA-256 del plan canonico.",
                required=True,
                schema_name="hd_PlanSha256",
            ),
            string_column(
                "hd_planstatus",
                "Estado del plan",
                80,
                "Estado vigente del ciclo de aprobacion.",
                required=True,
                schema_name="hd_PlanStatus",
            ),
            string_column(
                "hd_approvedbyoid",
                "OID del aprobador",
                36,
                "OID autenticado que aprobo esta version.",
                schema_name="hd_ApprovedByOid",
            ),
            string_column(
                "hd_approvedbyemail",
                "Correo del aprobador",
                320,
                "Correo autenticado del aprobador.",
                schema_name="hd_ApprovedByEmail",
            ),
            datetime_column(
                "hd_approvedat",
                "Aprobado el",
                "Fecha y hora de la aprobacion.",
                schema_name="hd_ApprovedAt",
            ),
            datetime_column(
                "hd_executionstartedat",
                "Ejecucion iniciada",
                "Fecha y hora de inicio de la ejecucion.",
                schema_name="hd_ExecutionStartedAt",
            ),
            datetime_column(
                "hd_executioncompletedat",
                "Ejecucion finalizada",
                "Fecha y hora de finalizacion de la ejecucion.",
                schema_name="hd_ExecutionCompletedAt",
            ),
            string_column(
                "hd_executionstatus",
                "Estado de ejecucion",
                80,
                "Estado vigente de la ejecucion.",
                schema_name="hd_ExecutionStatus",
            ),
            memo_column(
                "hd_executionresultjson",
                "Resultado de ejecucion JSON",
                "Resultado estructurado de la herramienta.",
                schema_name="hd_ExecutionResultJson",
            ),
            string_column(
                "hd_observedstatefingerprint",
                "Huella observada",
                128,
                "Huella del recurso observada justo antes de ejecutar.",
                schema_name="hd_ObservedStateFingerprint",
            ),
            memo_column(
                "hd_verificationresultjson",
                "Verificacion JSON",
                "Resultado estructurado de la verificacion posterior.",
                schema_name="hd_VerificationResultJson",
            ),
            memo_column(
                "hd_rollbackresultjson",
                "Rollback JSON",
                "Resultado estructurado de una reversion, si aplica.",
                schema_name="hd_RollbackResultJson",
            ),
        ),
    ),
)


RELATIONSHIP_SPECS = (
    RelationshipSpec(
        "hd_cliente_customertenant",
        "hd_ClientId",
        "hd_clientid",
        "Cliente",
        CLIENT_TABLE,
        "cr07a_clienteid",
        "hd_customertenant",
    ),
    RelationshipSpec(
        "hd_customertenant_ticket",
        "hd_CustomerTenantId",
        "hd_customertenantid",
        "Tenant del cliente",
        "hd_customertenant",
        "hd_customertenantid",
        TICKET_TABLE,
    ),
    RelationshipSpec(
        "hd_systemuser_ticket_tenantconfirmedby",
        "hd_TenantConfirmedById",
        "hd_tenantconfirmedbyid",
        "Tenant confirmado por",
        SYSTEM_USER_TABLE,
        "systemuserid",
        TICKET_TABLE,
    ),
    RelationshipSpec(
        "hd_ticket_supportinteraction",
        "hd_TicketId",
        "hd_ticketid",
        "Ticket",
        TICKET_TABLE,
        "cr07a_ticketid",
        "hd_supportinteraction",
    ),
    RelationshipSpec(
        "hd_supportchannel_supportinteraction",
        "hd_ChannelId",
        "hd_channelid",
        "Canal",
        "hd_supportchannel",
        "hd_supportchannelid",
        "hd_supportinteraction",
    ),
    RelationshipSpec(
        "hd_ticket_investigation",
        "hd_TicketId",
        "hd_ticketid",
        "Ticket",
        TICKET_TABLE,
        "cr07a_ticketid",
        "hd_investigation",
    ),
    RelationshipSpec(
        "hd_customertenant_investigation",
        "hd_TenantId",
        "hd_tenantid",
        "Tenant",
        "hd_customertenant",
        "hd_customertenantid",
        "hd_investigation",
    ),
    RelationshipSpec(
        "hd_investigation_evidence",
        "hd_InvestigationId",
        "hd_investigationid",
        "Investigacion",
        "hd_investigation",
        "hd_investigationid",
        "hd_evidence",
    ),
    RelationshipSpec(
        "hd_ticket_evidence",
        "hd_TicketId",
        "hd_ticketid",
        "Ticket",
        TICKET_TABLE,
        "cr07a_ticketid",
        "hd_evidence",
    ),
    RelationshipSpec(
        "hd_ticket_changeplan",
        "hd_TicketId",
        "hd_ticketid",
        "Ticket",
        TICKET_TABLE,
        "cr07a_ticketid",
        "hd_changeplan",
    ),
    RelationshipSpec(
        "hd_customertenant_changeplan",
        "hd_TenantId",
        "hd_tenantid",
        "Tenant",
        "hd_customertenant",
        "hd_customertenantid",
        "hd_changeplan",
    ),
    RelationshipSpec(
        "hd_systemuser_changeplan_approvedby",
        "hd_ApprovedById",
        "hd_approvedbyid",
        "Aprobado por",
        SYSTEM_USER_TABLE,
        "systemuserid",
        "hd_changeplan",
    ),
)


KEY_SPECS = (
    KeySpec(
        TICKET_SCHEMA,
        TICKET_TABLE,
        "hd_Ticket_CaseNumberKey",
        "Consecutivo unico del ticket",
        ("hd_casenumber",),
    ),
    KeySpec(
        TICKET_SCHEMA,
        TICKET_TABLE,
        "hd_Ticket_ExternalCaseKey",
        "Clave externa unica del ticket",
        ("hd_externalcasekey",),
    ),
    KeySpec(
        "hd_SupportChannel",
        "hd_supportchannel",
        "hd_SupportChannel_ChannelKey",
        "Clave unica del canal",
        ("hd_channelkey",),
    ),
    KeySpec(
        "hd_SupportChannel",
        "hd_supportchannel",
        "hd_SupportChannel_AddressKey",
        "Direccion unica del canal",
        ("hd_address",),
    ),
    KeySpec(
        "hd_CustomerTenant",
        "hd_customertenant",
        "hd_CustomerTenant_TenantGuidKey",
        "Tenant GUID unico",
        ("hd_tenantguid",),
    ),
    KeySpec(
        "hd_SupportInteraction",
        "hd_supportinteraction",
        "hd_SupportInteraction_InteractionKey",
        "Clave unica de interaccion",
        ("hd_interactionkey",),
    ),
    KeySpec(
        "hd_SupportInteraction",
        "hd_supportinteraction",
        "hd_SupportInteraction_DeduplicationKey",
        "Clave unica de deduplicacion",
        ("hd_deduplicationkey",),
    ),
    KeySpec(
        "hd_SupportInteraction",
        "hd_supportinteraction",
        "hd_SupportInteraction_IdempotencyKey",
        "Clave unica de idempotencia",
        ("hd_idempotencykey",),
    ),
    KeySpec(
        "hd_Investigation",
        "hd_investigation",
        "hd_Investigation_RunKey",
        "Clave unica de investigacion",
        ("hd_runkey",),
    ),
    KeySpec(
        "hd_Evidence",
        "hd_evidence",
        "hd_Evidence_EvidenceKey",
        "Clave unica de evidencia",
        ("hd_evidencekey",),
    ),
    KeySpec(
        "hd_ChangePlan",
        "hd_changeplan",
        "hd_ChangePlan_IdempotencyKey",
        "Clave unica de idempotencia",
        ("hd_idempotencykey",),
    ),
    KeySpec(
        "hd_ChangePlan",
        "hd_changeplan",
        "hd_ChangePlan_PlanSha256Key",
        "SHA-256 unico del plan",
        ("hd_plansha256",),
    ),
)


class DataverseWebApi:
    """Small Web API adapter limited to advanced metadata and actions."""

    def __init__(self, skill: str = "dv-metadata") -> None:
        self.skill = skill
        self.base_url = TARGET_URL
        self.token = get_token()

    @staticmethod
    def query_path(path: str, params: dict[str, str]) -> str:
        query = urllib.parse.urlencode(params, safe="'(),$")
        return f"{path}?{query}"

    def request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
        *,
        solution: bool = False,
        extra_headers: dict[str, str] | None = None,
        allow_not_found: bool = False,
        retry_auth: bool = True,
    ) -> Any:
        url = f"{self.base_url}/api/data/v9.2/{path.lstrip('/')}"
        headers = get_plugin_headers(self.skill, self.token)
        headers.update(
            {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            }
        )
        if payload is not None:
            headers["Content-Type"] = "application/json; charset=utf-8"
        if solution:
            headers["MSCRM.SolutionUniqueName"] = SOLUTION_NAME
        if extra_headers:
            headers.update(extra_headers)

        body = (
            json.dumps(payload, ensure_ascii=True, separators=(",", ":")).encode(
                "utf-8"
            )
            if payload is not None
            else None
        )
        request = urllib.request.Request(
            url,
            data=body,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                raw = response.read()
                if not raw:
                    return None
                return json.loads(raw.decode("utf-8"))
        except urllib.error.HTTPError as error:
            error_body = error.read().decode("utf-8", errors="replace")
            if error.code == 404 and allow_not_found:
                return None
            if error.code == 401 and retry_auth:
                self.token = get_token()
                return self.request(
                    method,
                    path,
                    payload,
                    solution=solution,
                    extra_headers=extra_headers,
                    allow_not_found=allow_not_found,
                    retry_auth=False,
                )
            raise WebApiError(
                method,
                url,
                error.code,
                error_body,
                error.headers.get("Retry-After"),
            ) from error
        except urllib.error.URLError as error:
            raise ProvisioningError(f"{method} {url} failed: {error}") from error

    def entity(self, logical_name: str, *, full: bool = False) -> dict[str, Any] | None:
        path = f"EntityDefinitions(LogicalName='{logical_name}')"
        if not full:
            path = self.query_path(
                path,
                {
                    "$select": (
                        "MetadataId,LogicalName,SchemaName,EntitySetName,"
                        "OwnershipType,HasNotes,PrimaryIdAttribute,"
                        "PrimaryNameAttribute,IsAuditEnabled"
                    )
                },
            )
        return self.request("GET", path, allow_not_found=True)

    def attributes(self, logical_name: str) -> dict[str, dict[str, Any]]:
        path = self.query_path(
            f"EntityDefinitions(LogicalName='{logical_name}')/Attributes",
            {
                "$select": (
                    "MetadataId,LogicalName,SchemaName,AttributeType,"
                    "IsAuditEnabled,AttributeOf"
                )
            },
        )
        response = self.request("GET", path)
        values = (response or {}).get("value", [])
        return {
            item["LogicalName"].lower(): item
            for item in values
            if item.get("LogicalName")
        }

    def string_attribute(
        self,
        table_logical_name: str,
        attribute_logical_name: str,
    ) -> dict[str, Any] | None:
        path = self.query_path(
            (
                f"EntityDefinitions(LogicalName='{table_logical_name}')/"
                f"Attributes(LogicalName='{attribute_logical_name}')/"
                "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            ),
            {
                "$select": (
                    "MetadataId,LogicalName,SchemaName,AttributeType,"
                    "AutoNumberFormat,MaxLength,IsAuditEnabled"
                )
            },
        )
        return self.request("GET", path, allow_not_found=True)

    def relationship(self, schema_name: str) -> dict[str, Any] | None:
        escaped = odata_escape(schema_name)
        path = self.query_path(
            (
                f"RelationshipDefinitions(SchemaName='{escaped}')/"
                "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
            ),
            {
                "$select": (
                    "MetadataId,SchemaName,ReferencedEntity,ReferencingEntity,"
                    "ReferencingAttribute"
                ),
            },
        )
        return self.request(
            "GET",
            path,
            allow_not_found=True,
            extra_headers={"Consistency": "Strong"},
        )


def odata_escape(value: str) -> str:
    return value.replace("'", "''")


def flatten_pages(pages: Iterable[Iterable[dict[str, Any]]]) -> list[dict[str, Any]]:
    return [record for page in pages for record in page]


def managed_value(value: Any) -> Any:
    if isinstance(value, dict) and "Value" in value:
        return value["Value"]
    return value


def object_value(item: Any, *names: str) -> Any:
    for name in names:
        if isinstance(item, dict) and name in item:
            return item[name]
        if hasattr(item, name):
            return getattr(item, name)
    return None


def retry_metadata(
    operation: Callable[[], Any],
    description: str,
    *,
    max_attempts: int,
) -> Any:
    transient_fragments = (
        "another customization operation",
        "another operation is running",
        "currently being published",
        "metadata cache",
        "0x80040216",
        "0x80060891",
        "temporarily unavailable",
        "timeout",
        "timed out",
    )
    for attempt in range(1, max_attempts + 1):
        try:
            return operation()
        except Exception as error:
            text = str(error).lower()
            transient = any(fragment in text for fragment in transient_fragments)
            if isinstance(error, WebApiError) and error.status in {
                408,
                409,
                423,
                429,
                500,
                502,
                503,
                504,
            }:
                transient = True
            if not transient or attempt >= max_attempts:
                raise
            retry_after = (
                int(error.retry_after)
                if isinstance(error, WebApiError)
                and error.retry_after
                and error.retry_after.isdigit()
                else 0
            )
            wait_seconds = max(retry_after, min(30, 5 * attempt))
            print(
                f"  {description}: transient metadata lock; "
                f"waiting {wait_seconds}s ({attempt}/{max_attempts})",
                flush=True,
            )
            time.sleep(wait_seconds)
    raise ProvisioningError(f"Metadata retry exhausted: {description}")


def validate_environment() -> None:
    load_env()
    configured_url = os.environ.get("DATAVERSE_URL", "").rstrip("/")
    if configured_url.lower() != TARGET_URL.lower():
        raise ProvisioningError(
            "Safety stop: DATAVERSE_URL does not match the confirmed target. "
            f"Expected {TARGET_URL}, got {configured_url or '<missing>'}."
        )

    expected_values = {
        "SOLUTION_NAME": SOLUTION_NAME,
        "PUBLISHER_UNIQUE_NAME": PUBLISHER_UNIQUE_NAME,
        "PUBLISHER_PREFIX": PUBLISHER_PREFIX,
    }
    for variable, expected in expected_values.items():
        actual = os.environ.get(variable, "").strip()
        if actual and actual.lower() != expected.lower():
            raise ProvisioningError(
                f"Safety stop: {variable} must be {expected}, got {actual}."
            )

    print(f"Target confirmed in .env: {TARGET_URL}", flush=True)
    print(
        f"Solution: {SOLUTION_NAME}; publisher: {PUBLISHER_UNIQUE_NAME}; "
        f"prefix: {PUBLISHER_PREFIX}_; language: {BASE_LANGUAGE}",
        flush=True,
    )


def find_publisher(client: Any) -> dict[str, Any]:
    escaped = odata_escape(PUBLISHER_UNIQUE_NAME)
    publishers = flatten_pages(
        client.records.get(
            "publisher",
            filter=f"uniquename eq '{escaped}'",
            select=[
                "publisherid",
                "uniquename",
                "friendlyname",
                "customizationprefix",
            ],
            top=10,
        )
    )
    exact = [
        item
        for item in publishers
        if str(item.get("uniquename", "")).lower()
        == PUBLISHER_UNIQUE_NAME.lower()
    ]
    if len(exact) != 1:
        raise ProvisioningError(
            f"Expected one existing publisher named {PUBLISHER_UNIQUE_NAME}; "
            f"found {len(exact)}."
        )
    publisher = exact[0]
    actual_prefix = str(publisher.get("customizationprefix", ""))
    if actual_prefix.lower() != PUBLISHER_PREFIX:
        raise ProvisioningError(
            f"Publisher {PUBLISHER_UNIQUE_NAME} has prefix "
            f"{actual_prefix!r}, expected {PUBLISHER_PREFIX!r}."
        )
    return publisher


def find_solution(client: Any) -> dict[str, Any] | None:
    escaped = odata_escape(SOLUTION_NAME)
    solutions = flatten_pages(
        client.records.get(
            "solution",
            filter=f"uniquename eq '{escaped}'",
            select=[
                "solutionid",
                "uniquename",
                "friendlyname",
                "version",
                "_publisherid_value",
                "ismanaged",
            ],
            top=10,
        )
    )
    exact = [
        item
        for item in solutions
        if str(item.get("uniquename", "")).lower() == SOLUTION_NAME.lower()
    ]
    if len(exact) > 1:
        raise ProvisioningError(
            f"More than one solution matched unique name {SOLUTION_NAME}."
        )
    return exact[0] if exact else None


def ensure_ticket_solution_component(
    client: Any,
    web: DataverseWebApi,
    solution: dict[str, Any],
) -> None:
    ticket = web.entity(TICKET_TABLE)
    if not ticket:
        raise ProvisioningError(f"Required table {TICKET_TABLE} does not exist.")
    solution_id = str(solution["solutionid"])
    metadata_id = str(ticket["MetadataId"])
    components = flatten_pages(
        client.records.get(
            "solutioncomponent",
            filter=(
                f"_solutionid_value eq {solution_id} and componenttype eq 1 "
                f"and objectid eq {metadata_id}"
            ),
            select=["solutioncomponentid", "objectid", "componenttype"],
            top=5,
        )
    )
    if components:
        print(f"  Reusing solution component: {TICKET_TABLE}", flush=True)
        return

    solution_web = DataverseWebApi("dv-solution")
    retry_metadata(
        lambda: solution_web.request(
            "POST",
            "AddSolutionComponent",
            {
                "ComponentId": metadata_id,
                "ComponentType": 1,
                "SolutionUniqueName": SOLUTION_NAME,
                "AddRequiredComponents": True,
                "DoNotIncludeSubcomponents": False,
            },
        ),
        f"add {TICKET_TABLE} to solution",
        max_attempts=6,
    )
    print(f"  Added solution component: {TICKET_TABLE}", flush=True)


def ensure_solution(max_attempts: int) -> dict[str, Any]:
    print("[solution] Ensuring publisher and unmanaged solution", flush=True)
    client = get_client("dv-solution")
    publisher = find_publisher(client)
    publisher_id = str(publisher["publisherid"])
    solution = find_solution(client)
    if solution:
        if bool(solution.get("ismanaged", False)):
            raise ProvisioningError(f"Solution {SOLUTION_NAME} is managed.")
        actual_publisher_id = str(solution.get("_publisherid_value", "")).lower()
        if actual_publisher_id and actual_publisher_id != publisher_id.lower():
            raise ProvisioningError(
                f"Solution {SOLUTION_NAME} belongs to a different publisher."
            )
        print(
            f"  Reusing solution {SOLUTION_NAME} ({solution['solutionid']})",
            flush=True,
        )
    else:
        solution_id = retry_metadata(
            lambda: client.records.create(
                "solution",
                {
                    "uniquename": SOLUTION_NAME,
                    "friendlyname": SOLUTION_DISPLAY_NAME,
                    "version": SOLUTION_VERSION,
                    "publisherid@odata.bind": f"/publishers({publisher_id})",
                },
            ),
            f"create solution {SOLUTION_NAME}",
            max_attempts=max_attempts,
        )
        print(f"  Created solution {SOLUTION_NAME} ({solution_id})", flush=True)
        solution = find_solution(client)
        if not solution:
            raise ProvisioningError(
                f"Solution {SOLUTION_NAME} was not readable after creation."
            )

    web = DataverseWebApi("dv-metadata")
    ensure_ticket_solution_component(client, web, solution)
    return solution


def primary_name_payload(table: TableSpec) -> dict[str, Any]:
    payload = column_base(
        table.primary_name_logical,
        table.primary_name_display,
        f"Nombre principal de {table.display_name.lower()}.",
        True,
        table.primary_name_schema,
    )
    payload.update(
        {
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": {"Value": "StringType"},
            "FormatName": {"Value": "Text"},
            "MaxLength": table.primary_name_max_length,
            "IsPrimaryName": True,
        }
    )
    return payload


def table_create_payload(table: TableSpec) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.EntityMetadata",
        "SchemaName": table.schema_name,
        "DisplayName": label(table.display_name),
        "DisplayCollectionName": label(table.display_collection_name),
        "Description": label(table.description),
        "OwnershipType": table.ownership_type,
        "IsActivity": False,
        "HasActivities": False,
        "HasNotes": table.has_notes,
        "EntitySetName": f"{table.logical_name}s",
        "PrimaryNameAttribute": table.primary_name_logical,
        "IsAuditEnabled": audit_enabled(),
        "Attributes": [primary_name_payload(table)],
    }


def ensure_table(
    web: DataverseWebApi,
    table: TableSpec,
    *,
    max_attempts: int,
) -> bool:
    existing = web.entity(table.logical_name)
    if existing:
        ownership = str(existing.get("OwnershipType", ""))
        has_notes = bool(existing.get("HasNotes", False))
        entity_set_name = str(existing.get("EntitySetName", ""))
        expected_entity_set_name = f"{table.logical_name}s"
        if ownership.lower() != table.ownership_type.lower():
            raise ProvisioningError(
                f"Table {table.logical_name} ownership is {ownership}; "
                f"expected {table.ownership_type}."
            )
        if has_notes != table.has_notes:
            raise ProvisioningError(
                f"Table {table.logical_name} HasNotes is {has_notes}; "
                f"expected {table.has_notes}."
            )
        if entity_set_name.lower() != expected_entity_set_name.lower():
            raise ProvisioningError(
                f"Table {table.logical_name} entity set is {entity_set_name}; "
                f"expected {expected_entity_set_name}."
            )
        print(f"  Reusing table: {table.logical_name}", flush=True)
        return False

    retry_metadata(
        lambda: web.request(
            "POST",
            "EntityDefinitions",
            table_create_payload(table),
            solution=True,
        ),
        f"create table {table.logical_name}",
        max_attempts=max_attempts,
    )
    created = web.entity(table.logical_name)
    if not created:
        raise ProvisioningError(
            f"Table {table.logical_name} was not readable after creation."
        )
    print(f"  Created table: {table.logical_name}", flush=True)
    return True


def enable_table_audit(
    web: DataverseWebApi,
    logical_name: str,
    *,
    max_attempts: int,
) -> None:
    summary = web.entity(logical_name)
    if not summary:
        raise ProvisioningError(f"Cannot audit missing table {logical_name}.")
    if bool(managed_value(summary.get("IsAuditEnabled"))):
        print(f"  Audit already enabled: {logical_name}", flush=True)
        return

    def update() -> None:
        full = web.entity(logical_name, full=True)
        if not full:
            raise ProvisioningError(f"Cannot retrieve full metadata for {logical_name}.")
        full = {
            key: value
            for key, value in full.items()
            if not key.startswith("@odata.")
        }
        current = full.get("IsAuditEnabled")
        if isinstance(current, dict):
            current["Value"] = True
        else:
            full["IsAuditEnabled"] = audit_enabled()
        metadata_id = full.get("MetadataId") or summary.get("MetadataId")
        if not metadata_id:
            raise ProvisioningError(f"Missing MetadataId for {logical_name}.")
        web.request(
            "PUT",
            f"EntityDefinitions({metadata_id})",
            full,
            solution=True,
            extra_headers={"MSCRM.MergeLabels": "true"},
        )

    retry_metadata(
        update,
        f"enable audit on {logical_name}",
        max_attempts=max_attempts,
    )
    refreshed = web.entity(logical_name)
    if not refreshed or not bool(
        managed_value(refreshed.get("IsAuditEnabled"))
    ):
        raise ProvisioningError(f"Audit did not enable on {logical_name}.")
    print(f"  Enabled audit: {logical_name}", flush=True)


def ensure_column(
    web: DataverseWebApi,
    table_logical_name: str,
    column: ColumnSpec,
    existing_attributes: dict[str, dict[str, Any]],
    *,
    max_attempts: int,
) -> bool:
    existing = existing_attributes.get(column.logical_name.lower())
    if existing:
        actual_type = str(existing.get("AttributeType", ""))
        if actual_type.lower() != column.attribute_type.lower():
            raise ProvisioningError(
                f"Column {table_logical_name}.{column.logical_name} has type "
                f"{actual_type}; expected {column.attribute_type}."
            )
        if column.payload.get("AutoNumberFormat"):
            auto = web.string_attribute(table_logical_name, column.logical_name)
            actual_format = (auto or {}).get("AutoNumberFormat")
            if actual_format != column.payload["AutoNumberFormat"]:
                raise ProvisioningError(
                    f"Column {table_logical_name}.{column.logical_name} has "
                    f"AutoNumberFormat {actual_format!r}; expected "
                    f"{column.payload['AutoNumberFormat']!r}."
                )
        print(
            f"  Reusing column: {table_logical_name}.{column.logical_name}",
            flush=True,
        )
        return False

    retry_metadata(
        lambda: web.request(
            "POST",
            (
                f"EntityDefinitions(LogicalName='{table_logical_name}')/"
                "Attributes"
            ),
            column.payload,
            solution=True,
        ),
        f"create column {table_logical_name}.{column.logical_name}",
        max_attempts=max_attempts,
    )
    print(
        f"  Created column: {table_logical_name}.{column.logical_name}",
        flush=True,
    )
    return True


def ensure_schema(args: argparse.Namespace) -> None:
    print("[schema] Creating tables, columns, autonumber, and audit", flush=True)
    web = DataverseWebApi("dv-metadata")
    if not web.entity(TICKET_TABLE):
        raise ProvisioningError(f"Required table {TICKET_TABLE} does not exist.")

    created_any_table = False
    for index, table in enumerate(TABLE_SPECS):
        created = ensure_table(web, table, max_attempts=args.max_attempts)
        created_any_table = created_any_table or created
        if created and index < len(TABLE_SPECS) - 1:
            time.sleep(args.table_delay_seconds)

    if created_any_table and args.propagation_wait_seconds:
        print(
            f"  Waiting {args.propagation_wait_seconds}s for table propagation",
            flush=True,
        )
        time.sleep(args.propagation_wait_seconds)

    enable_table_audit(
        web,
        TICKET_TABLE,
        max_attempts=args.max_attempts,
    )
    for table in TABLE_SPECS:
        enable_table_audit(
            web,
            table.logical_name,
            max_attempts=args.max_attempts,
        )

    ticket_attributes = web.attributes(TICKET_TABLE)
    for column in TICKET_COLUMNS:
        created = ensure_column(
            web,
            TICKET_TABLE,
            column,
            ticket_attributes,
            max_attempts=args.max_attempts,
        )
        if created:
            ticket_attributes[column.logical_name] = {
                "LogicalName": column.logical_name,
                "SchemaName": column.schema_name,
                "AttributeType": column.attribute_type,
            }
            time.sleep(args.operation_delay_seconds)

    for table in TABLE_SPECS:
        attributes = web.attributes(table.logical_name)
        for column in table.columns:
            created = ensure_column(
                web,
                table.logical_name,
                column,
                attributes,
                max_attempts=args.max_attempts,
            )
            if created:
                attributes[column.logical_name] = {
                    "LogicalName": column.logical_name,
                    "SchemaName": column.schema_name,
                    "AttributeType": column.attribute_type,
                }
                time.sleep(args.operation_delay_seconds)


def ensure_relationship(
    client: Any,
    web: DataverseWebApi,
    relationship: RelationshipSpec,
    *,
    max_attempts: int,
) -> bool:
    existing = web.relationship(relationship.schema_name)
    if existing:
        if (
            str(existing.get("ReferencedEntity", "")).lower()
            != relationship.referenced_table.lower()
            or str(existing.get("ReferencingEntity", "")).lower()
            != relationship.referencing_table.lower()
            or str(existing.get("ReferencingAttribute", "")).lower()
            != relationship.lookup_logical_name.lower()
        ):
            raise ProvisioningError(
                f"Relationship {relationship.schema_name} exists with a "
                "different definition."
            )
        print(
            f"  Reusing relationship: {relationship.schema_name}",
            flush=True,
        )
        return False

    referencing_attributes = web.attributes(relationship.referencing_table)
    if relationship.lookup_logical_name in referencing_attributes:
        raise ProvisioningError(
            f"Lookup {relationship.referencing_table}."
            f"{relationship.lookup_logical_name} exists but relationship "
            f"{relationship.schema_name} does not. Manual review is required."
        )

    from PowerPlatform.Dataverse.common.constants import (
        CASCADE_BEHAVIOR_REMOVE_LINK,
    )
    from PowerPlatform.Dataverse.models.labels import Label, LocalizedLabel
    from PowerPlatform.Dataverse.models.relationship import (
        CascadeConfiguration,
        LookupAttributeMetadata,
        OneToManyRelationshipMetadata,
    )

    lookup = LookupAttributeMetadata(
        schema_name=relationship.lookup_schema_name,
        display_name=Label(
            localized_labels=[
                LocalizedLabel(
                    label=relationship.display_name,
                    language_code=BASE_LANGUAGE,
                )
            ]
        ),
    )
    metadata = OneToManyRelationshipMetadata(
        schema_name=relationship.schema_name,
        referenced_entity=relationship.referenced_table,
        referencing_entity=relationship.referencing_table,
        referenced_attribute=relationship.referenced_attribute,
        cascade_configuration=CascadeConfiguration(
            delete=CASCADE_BEHAVIOR_REMOVE_LINK
        ),
    )
    retry_metadata(
        lambda: client.tables.create_one_to_many_relationship(
            lookup,
            metadata,
            solution=SOLUTION_NAME,
        ),
        f"create relationship {relationship.schema_name}",
        max_attempts=max_attempts,
    )
    print(f"  Created relationship: {relationship.schema_name}", flush=True)
    return True


def ensure_relationships(args: argparse.Namespace) -> None:
    print("[relationships] Creating lookup relationships", flush=True)
    client = get_client("dv-metadata")
    web = DataverseWebApi("dv-metadata")
    for index, relationship in enumerate(RELATIONSHIP_SPECS):
        created = ensure_relationship(
            client,
            web,
            relationship,
            max_attempts=args.max_attempts,
        )
        if created and index < len(RELATIONSHIP_SPECS) - 1:
            time.sleep(args.relationship_delay_seconds)


def key_schema_name(key: Any) -> str:
    return str(
        object_value(
            key,
            "schema_name",
            "SchemaName",
            "logical_name",
            "LogicalName",
        )
        or ""
    )


def key_status(key: Any) -> str:
    value = object_value(
        key,
        "status",
        "Status",
        "entity_key_index_status",
        "EntityKeyIndexStatus",
    )
    return str(value if value is not None else "Unknown")


def find_key(client: Any, spec: KeySpec) -> Any | None:
    keys = client.tables.get_alternate_keys(spec.table_schema_name)
    return next(
        (
            key
            for key in keys
            if key_schema_name(key).lower() == spec.schema_name.lower()
        ),
        None,
    )


def is_key_active(status: str) -> bool:
    normalized = status.strip().lower()
    return normalized == "2" or normalized.endswith("active")


def is_key_failed(status: str) -> bool:
    normalized = status.strip().lower()
    return normalized == "3" or normalized.endswith("failed")


def ensure_alternate_key(
    client: Any,
    spec: KeySpec,
    *,
    max_attempts: int,
) -> bool:
    existing = find_key(client, spec)
    if existing:
        status = key_status(existing)
        if is_key_failed(status):
            raise ProvisioningError(
                f"Alternate key {spec.schema_name} is Failed. Resolve duplicate "
                "data and reactivate it before continuing."
            )
        print(
            f"  Reusing key: {spec.schema_name} (status: {status})",
            flush=True,
        )
        return False

    retry_metadata(
        lambda: client.tables.create_alternate_key(
            spec.table_schema_name,
            spec.schema_name,
            list(spec.columns),
            display_name=spec.display_name,
            language_code=BASE_LANGUAGE,
        ),
        f"create key {spec.schema_name}",
        max_attempts=max_attempts,
    )
    print(f"  Created key: {spec.schema_name}", flush=True)
    return True


def wait_for_keys(
    client: Any,
    specs: Sequence[KeySpec],
    timeout_seconds: int,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    pending = {spec.schema_name: spec for spec in specs}
    while pending:
        for name, spec in list(pending.items()):
            key = find_key(client, spec)
            if key is None:
                raise ProvisioningError(f"Alternate key disappeared: {name}")
            status = key_status(key)
            if is_key_failed(status):
                raise ProvisioningError(
                    f"Alternate key {name} entered Failed status."
                )
            if is_key_active(status):
                print(f"  Key active: {name}", flush=True)
                del pending[name]
        if not pending:
            return
        if time.monotonic() >= deadline:
            states = ", ".join(
                f"{name}={key_status(find_key(client, spec))}"
                for name, spec in pending.items()
            )
            raise ProvisioningError(
                f"Timed out waiting for alternate keys: {states}"
            )
        time.sleep(5)


def ensure_keys(args: argparse.Namespace) -> None:
    print("[keys] Creating alternate keys", flush=True)
    client = get_client("dv-metadata")
    created_any = False
    for index, spec in enumerate(KEY_SPECS):
        created = ensure_alternate_key(
            client,
            spec,
            max_attempts=args.max_attempts,
        )
        created_any = created_any or created
        if created and index < len(KEY_SPECS) - 1:
            time.sleep(args.key_delay_seconds)
    if created_any:
        wait_for_keys(client, KEY_SPECS, args.key_timeout_seconds)


def seed_channels(args: argparse.Namespace) -> None:
    print("[seed] Upserting the three authorized mailboxes", flush=True)
    metadata_client = get_client("dv-metadata")
    channel_key_spec = next(
        spec
        for spec in KEY_SPECS
        if spec.schema_name == "hd_SupportChannel_ChannelKey"
    )
    key = find_key(metadata_client, channel_key_spec)
    if key is None or not is_key_active(key_status(key)):
        raise ProvisioningError(
            "The channel alternate key is not Active. Run --phase keys first."
        )

    from PowerPlatform.Dataverse.models.upsert import UpsertItem

    client = get_client("dv-data")
    items = []
    for mailbox in MAILBOXES:
        items.append(
            UpsertItem(
                alternate_key={"hd_channelkey": f"email:{mailbox}"},
                record={
                    "hd_name": mailbox,
                    "hd_address": mailbox,
                    "hd_channeltype": "email",
                    "hd_active": True,
                    "hd_lastsyncstatus": "not_configured",
                },
            )
        )
    result = client.records.upsert("hd_supportchannel", items)
    print(
        f"  Upserted {len(MAILBOXES)} support channel records: {result}",
        flush=True,
    )


def expected_columns_for(table_logical_name: str) -> tuple[ColumnSpec, ...]:
    if table_logical_name == TICKET_TABLE:
        return TICKET_COLUMNS
    table = next(
        item for item in TABLE_SPECS if item.logical_name == table_logical_name
    )
    return table.columns


def verify_solution() -> dict[str, Any]:
    client = get_client("dv-solution")
    publisher = find_publisher(client)
    solution = find_solution(client)
    if not solution:
        raise ProvisioningError(f"Solution {SOLUTION_NAME} is missing.")
    if bool(solution.get("ismanaged", False)):
        raise ProvisioningError(f"Solution {SOLUTION_NAME} must be unmanaged.")
    return {
        "solutionid": str(solution["solutionid"]),
        "publisherid": str(publisher["publisherid"]),
        "version": str(solution.get("version", "")),
    }


def verify_tables_and_columns(web: DataverseWebApi) -> dict[str, Any]:
    verified: dict[str, Any] = {}
    expected_tables = [(TICKET_TABLE, "UserOwned", None)] + [
        (table.logical_name, table.ownership_type, table.has_notes)
        for table in TABLE_SPECS
    ]
    for logical_name, ownership, has_notes in expected_tables:
        metadata = web.entity(logical_name)
        if not metadata:
            raise ProvisioningError(f"Missing table: {logical_name}")
        actual_ownership = str(metadata.get("OwnershipType", ""))
        if actual_ownership.lower() != ownership.lower():
            raise ProvisioningError(
                f"{logical_name} ownership is {actual_ownership}, "
                f"expected {ownership}."
            )
        if has_notes is not None and bool(metadata.get("HasNotes")) != has_notes:
            raise ProvisioningError(
                f"{logical_name} HasNotes does not match the schema contract."
            )
        if logical_name != TICKET_TABLE:
            expected_entity_set_name = f"{logical_name}s"
            actual_entity_set_name = str(metadata.get("EntitySetName", ""))
            if actual_entity_set_name.lower() != expected_entity_set_name.lower():
                raise ProvisioningError(
                    f"{logical_name} entity set is {actual_entity_set_name}; "
                    f"expected {expected_entity_set_name}."
                )
        if not bool(managed_value(metadata.get("IsAuditEnabled"))):
            raise ProvisioningError(f"Audit is disabled on {logical_name}.")

        attributes = web.attributes(logical_name)
        missing = [
            column.logical_name
            for column in expected_columns_for(logical_name)
            if column.logical_name not in attributes
        ]
        if missing:
            raise ProvisioningError(
                f"Missing columns on {logical_name}: {', '.join(missing)}"
            )
        verified[logical_name] = {
            "entitySetName": metadata.get("EntitySetName"),
            "ownershipType": actual_ownership,
            "hasNotes": bool(metadata.get("HasNotes")),
            "auditEnabled": True,
            "customColumnCount": len(expected_columns_for(logical_name)),
        }

    case_number = web.string_attribute(TICKET_TABLE, "hd_casenumber")
    if not case_number or case_number.get("AutoNumberFormat") != CASE_NUMBER_FORMAT:
        raise ProvisioningError(
            f"{TICKET_TABLE}.hd_casenumber does not use {CASE_NUMBER_FORMAT}."
        )
    return verified


def verify_relationships(web: DataverseWebApi) -> list[str]:
    verified = []
    for spec in RELATIONSHIP_SPECS:
        relationship = web.relationship(spec.schema_name)
        if not relationship:
            raise ProvisioningError(f"Missing relationship: {spec.schema_name}")
        if (
            str(relationship.get("ReferencedEntity", "")).lower()
            != spec.referenced_table.lower()
            or str(relationship.get("ReferencingEntity", "")).lower()
            != spec.referencing_table.lower()
            or str(relationship.get("ReferencingAttribute", "")).lower()
            != spec.lookup_logical_name.lower()
        ):
            raise ProvisioningError(
                f"Relationship mismatch: {spec.schema_name}"
            )
        verified.append(spec.schema_name)
    return verified


def verify_keys() -> dict[str, str]:
    client = get_client("dv-metadata")
    verified = {}
    for spec in KEY_SPECS:
        key = find_key(client, spec)
        if key is None:
            raise ProvisioningError(f"Missing alternate key: {spec.schema_name}")
        status = key_status(key)
        if not is_key_active(status):
            raise ProvisioningError(
                f"Alternate key {spec.schema_name} is not Active: {status}"
            )
        verified[spec.schema_name] = status
    return verified


def verify_seed() -> list[str]:
    client = get_client("dv-data")
    records = flatten_pages(
        client.records.get(
            "hd_supportchannel",
            filter="hd_channeltype eq 'email' and hd_active eq true",
            select=["hd_channelkey", "hd_address", "hd_channeltype", "hd_active"],
            top=50,
        )
    )
    by_address = {
        str(record.get("hd_address", "")).lower(): record for record in records
    }
    missing = [mailbox for mailbox in MAILBOXES if mailbox not in by_address]
    if missing:
        raise ProvisioningError(
            f"Missing seeded support channels: {', '.join(missing)}"
        )
    for mailbox in MAILBOXES:
        expected_key = f"email:{mailbox}"
        actual_key = str(by_address[mailbox].get("hd_channelkey", ""))
        if actual_key != expected_key:
            raise ProvisioningError(
                f"Channel {mailbox} has key {actual_key!r}; "
                f"expected {expected_key!r}."
            )
    return list(MAILBOXES)


def verify_all() -> None:
    print("[verify] Reading back the complete schema and seed", flush=True)
    web = DataverseWebApi("dv-metadata")
    summary = {
        "target": TARGET_URL,
        "solution": verify_solution(),
        "tables": verify_tables_and_columns(web),
        "relationships": verify_relationships(web),
        "alternateKeys": verify_keys(),
        "seedMailboxes": verify_seed(),
        "caseNumberFormat": CASE_NUMBER_FORMAT,
    }
    print(json.dumps(summary, indent=2, ensure_ascii=True), flush=True)
    print("[verify] PASS", flush=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Idempotently provision Mesa de ayuda in Dataverse."
    )
    parser.add_argument(
        "--phase",
        action="append",
        choices=("all",) + PHASE_ORDER,
        help=(
            "Phase to run. Repeat for multiple phases. Default: all phases in "
            "dependency order."
        ),
    )
    parser.add_argument(
        "--max-attempts",
        type=int,
        default=6,
        help="Maximum attempts for transient metadata operations.",
    )
    parser.add_argument(
        "--table-delay-seconds",
        type=float,
        default=5.0,
        help="Delay after each newly created table.",
    )
    parser.add_argument(
        "--operation-delay-seconds",
        type=float,
        default=1.0,
        help="Delay after each newly created column.",
    )
    parser.add_argument(
        "--relationship-delay-seconds",
        type=float,
        default=3.0,
        help="Delay after each newly created lookup relationship.",
    )
    parser.add_argument(
        "--key-delay-seconds",
        type=float,
        default=3.0,
        help="Delay after each newly created alternate key.",
    )
    parser.add_argument(
        "--propagation-wait-seconds",
        type=int,
        default=20,
        help="Wait after creating tables before adding columns.",
    )
    parser.add_argument(
        "--key-timeout-seconds",
        type=int,
        default=240,
        help="Maximum wait for alternate key indexes to become Active.",
    )
    args = parser.parse_args()
    if args.max_attempts < 1:
        parser.error("--max-attempts must be at least 1")
    for name in (
        "table_delay_seconds",
        "operation_delay_seconds",
        "relationship_delay_seconds",
        "key_delay_seconds",
        "propagation_wait_seconds",
        "key_timeout_seconds",
    ):
        if getattr(args, name) < 0:
            parser.error(f"--{name.replace('_', '-')} cannot be negative")
    return args


def selected_phases(args: argparse.Namespace) -> tuple[str, ...]:
    requested = args.phase or ["all"]
    if "all" in requested:
        return PHASE_ORDER
    requested_set = set(requested)
    return tuple(phase for phase in PHASE_ORDER if phase in requested_set)


def main() -> int:
    args = parse_args()
    validate_environment()
    phases = selected_phases(args)
    print(f"Phases: {', '.join(phases)}", flush=True)

    handlers: dict[str, Callable[[], Any]] = {
        "solution": lambda: ensure_solution(args.max_attempts),
        "schema": lambda: ensure_schema(args),
        "keys": lambda: ensure_keys(args),
        "relationships": lambda: ensure_relationships(args),
        "seed": lambda: seed_channels(args),
        "verify": verify_all,
    }
    for index, phase in enumerate(phases):
        handlers[phase]()
        if (
            phase in {"schema", "relationships", "keys"}
            and index < len(phases) - 1
            and args.propagation_wait_seconds
        ):
            print(
                f"Waiting {args.propagation_wait_seconds}s before next phase",
                flush=True,
            )
            time.sleep(args.propagation_wait_seconds)
    print("Mesa de ayuda provisioning completed.", flush=True)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Provisioning cancelled by operator.", file=sys.stderr, flush=True)
        raise SystemExit(130)
    except Exception as error:
        print(f"Provisioning failed: {error}", file=sys.stderr, flush=True)
        raise SystemExit(1)
