"""Audit and repair the durable DIAN/Siigo Dataverse schema.

The script is intentionally narrow:
- targets cr07a_gastodelaempresa in the environment from .env;
- creates only cr07a_siigobusinesskey when it is missing;
- backfills only received electronic invoices with a CUFE/CUDE;
- verifies uniqueness before creating or repairing alternate keys;
- never calls Siigo or any application endpoint that sends documents.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.request
import warnings
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent
sys.path.insert(0, str(SCRIPT_DIR))

from auth import get_client, get_plugin_headers, get_token, load_env


warnings.filterwarnings(
    "ignore",
    category=DeprecationWarning,
)


TABLE = "cr07a_gastodelaempresa"
PRIMARY_ID = "cr07a_gastodelaempresaid"
COLUMN_LOGICAL = "cr07a_siigobusinesskey"
COLUMN_SCHEMA = "cr07a_SiigoBusinessKey"
COLUMN_MAX_LENGTH = 150
REQUIRED_FIELDS = (
    "cr07a_fecharecepcion",
    "cr07a_estadoautomatizacion",
    "cr07a_motivorevision",
    "cr07a_siigodocumentid",
    "cr07a_excelkey",
    COLUMN_LOGICAL,
    "cr07a_cufecude",
    "cr07a_fuenteautomatizacion",
    "cr07a_siigoproveedorid",
)
BACKFILL_FIELDS = (
    PRIMARY_ID,
    "cr07a_tipodocumento",
    "cr07a_grupodian",
    "cr07a_cufecude",
    "cr07a_prefijo",
    "cr07a_folio",
    "cr07a_nitemisor",
)
KEY_DEFINITIONS = (
    (
        "cr07a_GastoEmpresaDianExcelKey",
        "Documento DIAN por CUFE",
        "cr07a_excelkey",
    ),
    (
        "cr07a_GastoEmpresaSiigoBusinessKey",
        "Factura DIAN por identidad Siigo",
        COLUMN_LOGICAL,
    ),
    (
        "cr07a_GastoEmpresaSiigoDocumentIdKey",
        "Documento DIAN por compra Siigo",
        "cr07a_siigodocumentid",
    ),
)
ACTIVE_KEY_STATUSES = {2, "2", "Active"}
FAILED_KEY_STATUSES = {3, "3", "Failed"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Apply the audited repair. Without this flag the script is read-only.",
    )
    parser.add_argument(
        "--backup-dir",
        default=str(PROJECT_DIR / "a" / "dataverse-backups"),
        help="Directory for the pre-write backup and JSON report.",
    )
    parser.add_argument(
        "--key-timeout-seconds",
        type=int,
        default=300,
        help="Maximum wait for each alternate-key index to become active.",
    )
    return parser.parse_args()


def label(text: str) -> dict[str, Any]:
    return {
        "@odata.type": "Microsoft.Dynamics.CRM.Label",
        "LocalizedLabels": [
            {
                "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel",
                "Label": text,
                "LanguageCode": 3082,
            }
        ],
    }


def value(text: str) -> dict[str, str]:
    return {"Value": text}


def required_none() -> dict[str, Any]:
    return {
        "Value": "None",
        "CanBeChanged": True,
        "ManagedPropertyLogicalName": "canmodifyrequirementlevelsettings",
    }


class DataverseWebApi:
    def __init__(self, solution_name: str):
        load_env()
        self.base_url = os.environ["DATAVERSE_URL"].rstrip("/")
        self.solution_name = solution_name
        self.token = get_token()

    def request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        solution_aware: bool = False,
    ) -> dict[str, Any]:
        url = path if path.startswith("https://") else f"{self.base_url}{path}"
        headers = get_plugin_headers("dv-metadata", self.token)
        headers.update(
            {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            }
        )
        if solution_aware:
            headers["MSCRM.SolutionName"] = self.solution_name
        data = None
        if body is not None:
            data = json.dumps(body, ensure_ascii=True).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"
        request = urllib.request.Request(
            url,
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                raw = response.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"Dataverse {method} {url} failed with HTTP {exc.code}: {detail}"
            ) from exc

    def attributes(self) -> dict[str, dict[str, Any]]:
        path = (
            f"/api/data/v9.2/EntityDefinitions(LogicalName='{TABLE}')/Attributes"
            "?$select=MetadataId,LogicalName,SchemaName,AttributeType"
        )
        payload = self.request("GET", path)
        return {
            str(row.get("LogicalName", "")).lower(): row
            for row in payload.get("value", [])
            if row.get("LogicalName")
        }

    def keys(self) -> list[dict[str, Any]]:
        path = (
            f"/api/data/v9.2/EntityDefinitions(LogicalName='{TABLE}')/Keys"
            "?$select=MetadataId,SchemaName,DisplayName,KeyAttributes,"
            "EntityKeyIndexStatus"
        )
        return list(self.request("GET", path).get("value", []))

    def create_column(self) -> None:
        payload = {
            "@odata.type": "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "AttributeType": "String",
            "AttributeTypeName": value("StringType"),
            "SchemaName": COLUMN_SCHEMA,
            "DisplayName": label("Identidad unica factura Siigo"),
            "Description": label(
                "Hash durable de proveedor, prefijo y folio para idempotencia DIAN/Siigo"
            ),
            "RequiredLevel": required_none(),
            "MaxLength": COLUMN_MAX_LENGTH,
            "FormatName": value("Text"),
        }
        path = (
            f"/api/data/v9.2/EntityDefinitions(LogicalName='{TABLE}')/Attributes"
        )
        self.request("POST", path, payload, solution_aware=True)

    def create_key(
        self,
        schema_name: str,
        display_name: str,
        attribute_name: str,
    ) -> None:
        payload = {
            "@odata.type": "Microsoft.Dynamics.CRM.EntityKeyMetadata",
            "SchemaName": schema_name,
            "DisplayName": label(display_name),
            "KeyAttributes": [attribute_name],
        }
        path = f"/api/data/v9.2/EntityDefinitions(LogicalName='{TABLE}')/Keys"
        self.request("POST", path, payload, solution_aware=True)

    def delete_key(self, metadata_id: str) -> None:
        path = (
            f"/api/data/v9.2/EntityDefinitions(LogicalName='{TABLE}')"
            f"/Keys({metadata_id})"
        )
        self.request("DELETE", path, solution_aware=True)

    def publish_table(self) -> None:
        parameter_xml = (
            "<importexportxml><entities><entity>"
            f"{TABLE}"
            "</entity></entities></importexportxml>"
        )
        self.request(
            "POST",
            "/api/data/v9.2/PublishXml",
            {"ParameterXml": parameter_xml},
        )

    def solution_component_exists(
        self,
        solution_id: str,
        component_id: str,
        component_type: int,
    ) -> bool:
        path = (
            "/api/data/v9.2/solutioncomponents"
            "?$select=solutioncomponentid"
            f"&$filter=_solutionid_value eq {solution_id}"
            f" and objectid eq {component_id}"
            f" and componenttype eq {component_type}"
            "&$top=1"
        ).replace(" ", "%20")
        return bool(self.request("GET", path).get("value", []))

    def ensure_solution_component(
        self,
        solution_id: str,
        component_id: str,
        component_type: int,
    ) -> bool:
        if self.solution_component_exists(
            solution_id,
            component_id,
            component_type,
        ):
            return False
        self.request(
            "POST",
            "/api/data/v9.2/AddSolutionComponent",
            {
                "ComponentId": component_id,
                "ComponentType": component_type,
                "SolutionUniqueName": self.solution_name,
                "AddRequiredComponents": False,
            },
        )
        if not self.solution_component_exists(
            solution_id,
            component_id,
            component_type,
        ):
            raise RuntimeError(
                "Dataverse did not add the expected component to solution "
                f"{self.solution_name}: type={component_type}, id={component_id}"
            )
        return True


def flatten(pages: Any) -> list[dict[str, Any]]:
    if isinstance(pages, dict):
        value_rows = pages.get("value")
        if isinstance(value_rows, list):
            return [row for row in value_rows if isinstance(row, dict)]
        return [pages]
    materialized = list(pages)
    if not materialized:
        return []
    if all(isinstance(row, dict) for row in materialized):
        return materialized
    return [row for page in materialized for row in page]


def query_records(
    client: Any,
    fields: list[str],
    filter_text: str | None = None,
) -> list[dict[str, Any]]:
    pages = client.records.get(
        TABLE,
        select=fields,
        filter=filter_text,
    )
    return flatten(pages)


def normalize_search_text(text: Any) -> str:
    value_text = str(text or "").strip()
    if not value_text:
        return ""
    decomposed = unicodedata.normalize("NFD", value_text)
    without_marks = "".join(
        character
        for character in decomposed
        if unicodedata.category(character) != "Mn"
    )
    normalized = re.sub(r"[^A-Z0-9]+", " ", without_marks.upper()).strip()
    return re.sub(r"\s+", " ", normalized)


def normalize_identity_part(text: Any) -> str:
    return "".join(
        character.upper()
        for character in str(text or "")
        if character.isalnum()
    )


def normalize_folio(text: Any) -> str:
    digits = re.sub(r"\D", "", str(text or ""))
    if not digits:
        return normalize_identity_part(text)
    normalized = digits.lstrip("0")
    return normalized or "0"


def colombian_nit_check_digit(identification: str) -> int:
    digits = re.sub(r"\D", "", identification)
    weights = [71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3]
    offset = max(0, len(weights) - len(digits))
    total = sum(
        int(character) * weights[index + offset]
        for index, character in enumerate(digits)
        if index + offset < len(weights)
    )
    remainder = total % 11
    return 11 - remainder if remainder > 1 else remainder


def canonical_supplier_tax_id(text: Any) -> str:
    digits = re.sub(r"\D", "", str(text or ""))
    if len(digits) != 10:
        return digits
    base_nit = digits[:9]
    return base_nit if colombian_nit_check_digit(base_nit) == int(digits[9]) else digits


def business_key(supplier_nit: Any, prefix: Any, folio: Any) -> str:
    supplier = canonical_supplier_tax_id(supplier_nit)
    normalized_prefix = normalize_identity_part(prefix)
    normalized_folio = normalize_folio(folio)
    if not supplier or not normalized_prefix or not normalized_folio:
        return ""
    canonical = f"{supplier}|{normalized_prefix}|{normalized_folio}"
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return f"dian-siigo:{digest}"


def is_received_electronic_invoice(row: dict[str, Any]) -> bool:
    document_type = normalize_search_text(row.get("cr07a_tipodocumento"))
    group = normalize_search_text(row.get("cr07a_grupodian"))
    return (
        "FACTURA ELECTRONICA" in document_type
        and "NOTA" not in document_type
        and "APPLICATION RESPONSE" not in document_type
        and "RECIBID" in group
        and "EMITID" not in group
    )


def build_candidates(
    rows: list[dict[str, Any]],
    column_exists: bool,
) -> tuple[list[dict[str, Any]], list[dict[str, str]]]:
    candidates: list[dict[str, Any]] = []
    skipped: list[dict[str, str]] = []
    for row in rows:
        if not is_received_electronic_invoice(row):
            continue
        record_id = str(row.get(PRIMARY_ID, "")).strip()
        key = business_key(
            row.get("cr07a_nitemisor"),
            row.get("cr07a_prefijo"),
            row.get("cr07a_folio"),
        )
        if not record_id or not key:
            skipped.append(
                {
                    "id": record_id or "<missing-id>",
                    "cufe": str(row.get("cr07a_cufecude", "") or ""),
                    "document_type": str(
                        row.get("cr07a_tipodocumento", "") or ""
                    ),
                    "group": str(row.get("cr07a_grupodian", "") or ""),
                    "supplier_nit": str(
                        row.get("cr07a_nitemisor", "") or ""
                    ),
                    "prefix": str(row.get("cr07a_prefijo", "") or ""),
                    "folio": str(row.get("cr07a_folio", "") or ""),
                }
            )
            continue
        candidates.append(
            {
                "id": record_id,
                "cufe": str(row.get("cr07a_cufecude", "") or ""),
                "old_key": (
                    str(row.get(COLUMN_LOGICAL, "") or "")
                    if column_exists
                    else ""
                ),
                "new_key": key,
            }
        )
    return candidates, skipped


def duplicate_values(rows: list[dict[str, Any]], field: str) -> list[str]:
    values = [
        str(row.get(field, "") or "").strip()
        for row in rows
        if str(row.get(field, "") or "").strip()
    ]
    counter = Counter(value.casefold() for value in values)
    representatives: dict[str, str] = {}
    for value in values:
        representatives.setdefault(value.casefold(), value)
    return [
        representatives[key]
        for key, count in counter.items()
        if count > 1
    ]


def assert_no_candidate_collisions(candidates: list[dict[str, Any]]) -> None:
    by_key: dict[str, list[dict[str, Any]]] = {}
    for candidate in candidates:
        by_key.setdefault(candidate["new_key"].casefold(), []).append(candidate)
    collisions = [rows for rows in by_key.values() if len(rows) > 1]
    if collisions:
        samples = [
            {
                "key": rows[0]["new_key"],
                "records": [
                    {"id": row["id"], "cufe": row["cufe"]}
                    for row in rows
                ],
            }
            for rows in collisions[:5]
        ]
        raise RuntimeError(
            "SiigoBusinessKey collisions detected before any write: "
            + json.dumps(samples, ensure_ascii=True)
        )


def key_status_label(status: Any) -> str:
    if status in ACTIVE_KEY_STATUSES:
        return "Active"
    if status in FAILED_KEY_STATUSES:
        return "Failed"
    return str(status)


def key_by_schema(
    keys: list[dict[str, Any]],
    schema_name: str,
) -> dict[str, Any] | None:
    return next(
        (
            key
            for key in keys
            if str(key.get("SchemaName", "")).casefold()
            == schema_name.casefold()
        ),
        None,
    )


def wait_for_column(api: DataverseWebApi, timeout_seconds: int = 120) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        attribute = api.attributes().get(COLUMN_LOGICAL)
        if attribute:
            return
        time.sleep(5)
    raise RuntimeError(f"Column {COLUMN_LOGICAL} did not propagate in time.")


def wait_for_key(
    api: DataverseWebApi,
    schema_name: str,
    timeout_seconds: int,
) -> dict[str, Any]:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        key = key_by_schema(api.keys(), schema_name)
        if key:
            status = key.get("EntityKeyIndexStatus")
            if status in ACTIVE_KEY_STATUSES:
                return key
            if status in FAILED_KEY_STATUSES:
                raise RuntimeError(
                    f"Alternate key {schema_name} failed to build its index."
                )
        time.sleep(5)
    raise RuntimeError(f"Alternate key {schema_name} did not become active in time.")


def verify_solution(client: Any, solution_name: str) -> dict[str, Any]:
    raw_rows = client.records.get(
        "solution",
        filter=f"uniquename eq '{solution_name}'",
        select=[
            "solutionid",
            "uniquename",
            "friendlyname",
            "_publisherid_value",
        ],
        top=1,
    )
    rows = flatten(raw_rows)
    if len(rows) != 1:
        shape = (
            f"dict keys={list(raw_rows.keys())}"
            if isinstance(raw_rows, dict)
            else type(raw_rows).__name__
        )
        raise RuntimeError(
            f"Expected exactly one solution named {solution_name}; "
            f"found {len(rows)} ({shape})."
        )
    publisher_id = str(rows[0].get("_publisherid_value", "")).strip()
    if not publisher_id:
        raise RuntimeError(f"Solution {solution_name} has no publisher id.")
    publisher = client.records.get(
        "publisher",
        publisher_id,
        select=["publisherid", "uniquename", "friendlyname", "customizationprefix"],
    )
    prefix = str(publisher.get("customizationprefix", "")).strip()
    if prefix.casefold() != "cr07a":
        raise RuntimeError(
            f"Solution {solution_name} publisher prefix is {prefix!r}, expected 'cr07a'."
        )
    return {
        "solution_id": str(rows[0].get("solutionid", "")).strip(),
        "unique_name": solution_name,
        "friendly_name": rows[0].get("friendlyname", ""),
        "publisher_prefix": prefix,
    }


def write_report(
    backup_dir: Path,
    report: dict[str, Any],
    prefix: str,
) -> Path:
    backup_dir.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    path = backup_dir / f"{prefix}-{timestamp}.json"
    path.write_text(
        json.dumps(report, indent=2, ensure_ascii=True),
        encoding="utf-8",
    )
    return path


def main() -> int:
    args = parse_args()
    load_env()
    environment_url = os.environ["DATAVERSE_URL"].rstrip("/") + "/"
    solution_name = os.environ.get("SOLUTION_NAME", "").strip()
    if not solution_name:
        raise RuntimeError("SOLUTION_NAME is missing from .env.")
    if environment_url.casefold() != "https://orgc79ca19c.crm2.dynamics.com/".casefold():
        raise RuntimeError(f"Unexpected Dataverse target: {environment_url}")

    client = get_client("dv-data")
    api = DataverseWebApi(solution_name)
    solution = verify_solution(client, solution_name)
    attributes = api.attributes()
    missing_required = sorted(
        field for field in REQUIRED_FIELDS if field not in attributes
    )
    column_exists = COLUMN_LOGICAL in attributes

    select_fields = list(BACKFILL_FIELDS)
    if column_exists:
        select_fields.append(COLUMN_LOGICAL)
    rows = query_records(
        client,
        select_fields,
        filter_text="cr07a_cufecude ne null",
    )
    candidates, skipped = build_candidates(rows, column_exists)
    assert_no_candidate_collisions(candidates)

    uniqueness_fields = [
        field
        for field in ("cr07a_excelkey", COLUMN_LOGICAL, "cr07a_siigodocumentid")
        if field in attributes
    ]
    uniqueness_rows = (
        query_records(client, [PRIMARY_ID, *uniqueness_fields])
        if uniqueness_fields
        else []
    )
    duplicates = {
        field: duplicate_values(uniqueness_rows, field)
        for field in uniqueness_fields
    }
    duplicate_failures = {
        field: values for field, values in duplicates.items() if values
    }
    if duplicate_failures:
        raise RuntimeError(
            "Duplicate alternate-key values detected before any metadata write: "
            + json.dumps(duplicate_failures, ensure_ascii=True)
        )

    keys_before = api.keys()
    audit_report: dict[str, Any] = {
        "mode": "apply" if args.apply else "audit",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "environment_url": environment_url,
        "solution": solution,
        "table": TABLE,
        "missing_required_fields_before": missing_required,
        "column_exists_before": column_exists,
        "candidate_count": len(candidates),
        "pending_backfill_count": sum(
            1
            for candidate in candidates
            if candidate["old_key"] != candidate["new_key"]
        ),
        "skipped_unkeyable_ids": skipped,
        "duplicates_before": duplicates,
        "keys_before": [
            {
                "schema_name": key.get("SchemaName"),
                "attributes": key.get("KeyAttributes"),
                "status": key_status_label(key.get("EntityKeyIndexStatus")),
                "metadata_id": key.get("MetadataId"),
            }
            for key in keys_before
        ],
    }

    if not args.apply:
        print(json.dumps(audit_report, indent=2, ensure_ascii=True))
        return 0

    backup_dir = Path(args.backup_dir).resolve()
    backup_report = {
        **audit_report,
        "candidate_rows_before": candidates,
    }
    backup_path = write_report(
        backup_dir,
        backup_report,
        "dian-siigo-durable-schema-before",
    )
    print(f"Backup written: {backup_path}", flush=True)

    if not column_exists:
        api.create_column()
        wait_for_column(api)
        api.publish_table()
        print(f"Created and published column: {TABLE}.{COLUMN_LOGICAL}", flush=True)

    attributes = api.attributes()
    missing_after_column = sorted(
        field for field in REQUIRED_FIELDS if field not in attributes
    )
    if missing_after_column:
        raise RuntimeError(
            "Required fields still missing after column repair: "
            + ", ".join(missing_after_column)
        )

    rows = query_records(
        client,
        [*BACKFILL_FIELDS, COLUMN_LOGICAL],
        filter_text="cr07a_cufecude ne null",
    )
    candidates, skipped = build_candidates(rows, True)
    assert_no_candidate_collisions(candidates)
    pending = [
        candidate
        for candidate in candidates
        if candidate["old_key"] != candidate["new_key"]
    ]
    for index, candidate in enumerate(pending, start=1):
        client.records.update(
            TABLE,
            candidate["id"],
            {COLUMN_LOGICAL: candidate["new_key"]},
        )
        if index % 25 == 0 or index == len(pending):
            print(f"Backfill progress: {index}/{len(pending)}", flush=True)

    readback_rows = query_records(
        client,
        [*BACKFILL_FIELDS, COLUMN_LOGICAL],
        filter_text="cr07a_cufecude ne null",
    )
    readback_candidates, readback_skipped = build_candidates(readback_rows, True)
    mismatches = [
        candidate
        for candidate in readback_candidates
        if candidate["old_key"] != candidate["new_key"]
    ]
    if mismatches:
        raise RuntimeError(
            f"Backfill read-back failed for {len(mismatches)} record(s)."
        )

    all_key_fields = [
        PRIMARY_ID,
        "cr07a_excelkey",
        COLUMN_LOGICAL,
        "cr07a_siigodocumentid",
    ]
    uniqueness_rows = query_records(client, all_key_fields)
    duplicates_after = {
        field: duplicate_values(uniqueness_rows, field)
        for field in all_key_fields[1:]
    }
    duplicate_failures_after = {
        field: values for field, values in duplicates_after.items() if values
    }
    if duplicate_failures_after:
        raise RuntimeError(
            "Duplicate alternate-key values detected after backfill: "
            + json.dumps(duplicate_failures_after, ensure_ascii=True)
        )

    for schema_name, display_name, attribute_name in KEY_DEFINITIONS:
        key = key_by_schema(api.keys(), schema_name)
        if key and key.get("EntityKeyIndexStatus") in FAILED_KEY_STATUSES:
            metadata_id = str(key.get("MetadataId", "")).strip()
            if not metadata_id:
                raise RuntimeError(
                    f"Failed key {schema_name} has no MetadataId for repair."
                )
            api.delete_key(metadata_id)
            time.sleep(10)
            key = None
            print(f"Removed failed alternate key: {schema_name}", flush=True)
        if not key:
            api.create_key(schema_name, display_name, attribute_name)
            print(f"Creating alternate key: {schema_name}", flush=True)
        wait_for_key(api, schema_name, args.key_timeout_seconds)
        print(f"Alternate key active: {schema_name}", flush=True)

    solution_id = str(solution.get("solution_id", "")).strip()
    if not solution_id:
        raise RuntimeError(f"Solution {solution_name} has no solution id.")
    column_metadata_id = str(
        (api.attributes().get(COLUMN_LOGICAL) or {}).get("MetadataId", "")
    ).strip()
    if not column_metadata_id:
        raise RuntimeError(f"Column {COLUMN_LOGICAL} has no MetadataId.")
    api.ensure_solution_component(solution_id, column_metadata_id, 2)
    print(f"Solution component present: column {COLUMN_SCHEMA}", flush=True)
    for schema_name, _, _ in KEY_DEFINITIONS:
        key = key_by_schema(api.keys(), schema_name) or {}
        metadata_id = str(key.get("MetadataId", "")).strip()
        if not metadata_id:
            raise RuntimeError(f"Alternate key {schema_name} has no MetadataId.")
        api.ensure_solution_component(solution_id, metadata_id, 14)
        print(f"Solution component present: alternate key {schema_name}", flush=True)

    api.publish_table()
    final_attributes = api.attributes()
    final_keys = api.keys()
    final_missing = sorted(
        field for field in REQUIRED_FIELDS if field not in final_attributes
    )
    final_key_state = {
        schema_name: key_status_label(
            (key_by_schema(final_keys, schema_name) or {}).get(
                "EntityKeyIndexStatus"
            )
        )
        for schema_name, _, _ in KEY_DEFINITIONS
    }
    if final_missing:
        raise RuntimeError(
            "Final schema verification failed; missing: "
            + ", ".join(final_missing)
        )
    inactive = [
        name for name, status in final_key_state.items() if status != "Active"
    ]
    if inactive:
        raise RuntimeError(
            "Final key verification failed; inactive: " + ", ".join(inactive)
        )

    final_report = {
        **audit_report,
        "mode": "applied",
        "backup_path": str(backup_path),
        "created_column": not column_exists,
        "backfilled_count": len(pending),
        "readback_candidate_count": len(readback_candidates),
        "readback_mismatch_count": len(mismatches),
        "skipped_unkeyable_ids_after": readback_skipped,
        "duplicates_after": duplicates_after,
        "missing_required_fields_after": final_missing,
        "key_status_after": final_key_state,
    }
    report_path = write_report(
        backup_dir,
        final_report,
        "dian-siigo-durable-schema-result",
    )
    print(json.dumps(final_report, indent=2, ensure_ascii=True))
    print(f"Result written: {report_path}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
