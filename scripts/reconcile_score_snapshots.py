#!/usr/bin/env python3
"""Reconcile verified score scalars with immutable calculator headers."""

from __future__ import annotations

import hashlib
import json
import os
import re
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path

from azure.identity import ClientSecretCredential
from PowerPlatform.Dataverse.client import DataverseClient
from PowerPlatform.Dataverse.core.config import OperationContext


TABLE = "cr07a_contractrecord1"
ID = "cr07a_contractrecord1id"
FIELDS = [
    ID,
    "cr07a_aprovisionamientodetallelargo",
    "cr07a_description",
    "cr07a_score",
    "cr07a_commission",
    "cr07a_contractvalue",
    "cr07a_adicionales",
    "cr07a_verificado",
    "createdon",
    "modifiedon",
]
HEADER_PATTERN = re.compile(
    r"(?im)^(Puntaje|Comisi(?:o|\u00f3)n|Venta mensual total|Venta total anual|Venta total)\s*:\s*([^\r\n]+)"
)


def make_client(skill: str) -> DataverseClient:
    credential = ClientSecretCredential(
        tenant_id=os.environ["TENANT_ID"],
        client_id=os.environ["CLIENT_ID"],
        client_secret=os.environ["CLIENT_SECRET"],
    )
    return DataverseClient(
        base_url=os.environ["DATAVERSE_URL"],
        credential=credential,
        context=OperationContext(
            user_agent_context=f"app=dataverse-skills/1.6.0;skill={skill};agent=codex"
        ),
    )


def decimal_text(value: object) -> Decimal | None:
    if value is None:
        return None
    cleaned = re.sub(r"[^0-9+\-.]", "", str(value).strip())
    if not cleaned:
        return None
    try:
        return Decimal(cleaned)
    except InvalidOperation:
        return None


def parse_header(description: str) -> dict[str, Decimal]:
    result: dict[str, Decimal] = {}
    for match in HEADER_PATTERN.finditer(description):
        key = match.group(1).lower().replace("\u00f3", "o")
        value = decimal_text(match.group(2))
        if value is None:
            continue
        if key == "puntaje":
            result["score"] = value
        elif key == "comision":
            result["commission"] = value
        elif key == "venta mensual total":
            result["monthly"] = value
        else:
            result["total"] = value
    return result


def is_true(value: object) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return int(value) == 1
    return str(value or "").strip().lower() in {"1", "true", "si", "yes"}


def equal_decimal(left: object, right: Decimal) -> bool:
    parsed = decimal_text(left)
    return parsed is not None and abs(parsed - right) <= Decimal("0.005")


def score_scalar_matches(left: object, right: Decimal) -> bool:
    """Accept the Dataverse score column's one-decimal storage precision."""
    parsed = decimal_text(left)
    if parsed is None:
        return False
    return equal_decimal(parsed, right) or equal_decimal(
        parsed,
        right.quantize(Decimal("0.1"), rounding=ROUND_HALF_UP),
    )


def header_is_complete_and_consistent(header: dict[str, Decimal]) -> bool:
    if not {"score", "commission", "monthly", "total"}.issubset(header):
        return False
    # Negative score/commission values are valid calculator outcomes for deals
    # below the profitability threshold. Sales totals must remain nonnegative.
    return header["monthly"] >= 0 and header["total"] >= 0


def result_matches(additional: dict, header: dict[str, Decimal]) -> bool:
    result = additional.get("LastResult")
    if not isinstance(result, dict):
        return False
    return (
        equal_decimal(result.get("Points"), header["score"])
        and equal_decimal(result.get("Commission"), header["commission"])
        and equal_decimal(result.get("TotalMonthlySale"), header["monthly"])
        and equal_decimal(result.get("TotalSale"), header["total"])
    )


def build_last_result(additional: dict, header: dict[str, Decimal]) -> dict:
    previous = additional.get("LastResult")
    result = dict(previous) if isinstance(previous, dict) else {}
    result["Points"] = float(header["score"])
    result["Commission"] = float(header["commission"])
    result["TotalMonthlySale"] = float(header["monthly"])
    result["TotalSale"] = float(header["total"])
    result.setdefault("ProrationDays", 0)
    result.setdefault("ProrationFactor", 1)
    result.setdefault("ProrationText", "No")
    return result


def backup_row(record, data: dict) -> dict:
    return {
        "id": str(data.get(ID) or record.id),
        "etag": record.etag,
        "modifiedon": data.get("modifiedon"),
        "cr07a_score": data.get("cr07a_score"),
        "cr07a_commission": data.get("cr07a_commission"),
        "cr07a_contractvalue": data.get("cr07a_contractvalue"),
        "cr07a_adicionales": data.get("cr07a_adicionales"),
        "cr07a_verificado": data.get("cr07a_verificado"),
    }


def main() -> None:
    apply_changes = os.environ.get("APPLY_SCORE_REPAIR") == "1"
    skill = "dv-data" if apply_changes else "dv-query"
    client = make_client(skill)
    records = list(
        client.records.list(
            TABLE,
            select=FIELDS,
            orderby=["createdon asc"],
            page_size=500,
        )
    )

    candidates = []
    inconsistent_headers = []
    skipped = {
        "pending": 0,
        "incompleteHeader": 0,
        "inconsistentHeader": 0,
        "invalidAdditional": 0,
        "alreadyCorrect": 0,
    }
    for record in records:
        data = record.data
        if not is_true(data.get("cr07a_verificado")):
            skipped["pending"] += 1
            continue

        description = str(
            data.get("cr07a_aprovisionamientodetallelargo")
            or data.get("cr07a_description")
            or ""
        )
        header = parse_header(description)
        if not {"score", "commission", "monthly", "total"}.issubset(header):
            skipped["incompleteHeader"] += 1
            continue
        if not header_is_complete_and_consistent(header):
            skipped["inconsistentHeader"] += 1
            inconsistent_headers.append(
                {
                    "id": str(data.get(ID) or record.id),
                    "createdon": data.get("createdon"),
                    **{key: str(value) for key, value in header.items()},
                }
            )
            continue

        raw_additional = data.get("cr07a_adicionales")
        try:
            additional = json.loads(str(raw_additional or "{}"))
        except json.JSONDecodeError:
            skipped["invalidAdditional"] += 1
            continue
        if not isinstance(additional, dict):
            skipped["invalidAdditional"] += 1
            continue

        scalar_matches = (
            score_scalar_matches(data.get("cr07a_score"), header["score"])
            and equal_decimal(data.get("cr07a_commission"), header["commission"])
            and equal_decimal(data.get("cr07a_contractvalue"), header["total"])
        )
        has_last_result = isinstance(additional.get("LastResult"), dict)
        if scalar_matches and (result_matches(additional, header) or not has_last_result):
            skipped["alreadyCorrect"] += 1
            continue

        candidates.append((record, header, additional))

    if not apply_changes:
        sample = [
            {
                "id": str(record.data.get(ID) or record.id),
                "createdon": record.data.get("createdon"),
                "score": str(header["score"]),
                "commission": str(header["commission"]),
                "monthly": str(header["monthly"]),
                "total": str(header["total"]),
                "storedScore": str(record.data.get("cr07a_score")),
                "storedCommission": str(record.data.get("cr07a_commission")),
                "storedTotal": str(record.data.get("cr07a_contractvalue")),
                "additionalLength": len(str(record.data.get("cr07a_adicionales") or "")),
                "lastResult": additional.get("LastResult"),
            }
            for record, header, additional in candidates[-8:]
        ]
        print(
            "PLAN "
            + json.dumps(
                {
                    "environment": "DigitalTech",
                    "recordsRead": len(records),
                    "candidates": len(candidates),
                    "skipped": skipped,
                    "recentCandidates": sample,
                    "inconsistentHeaders": inconsistent_headers[-12:],
                },
                ensure_ascii=True,
                separators=(",", ":"),
            )
        )
        return

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    backup_path = Path.home() / f"score-snapshot-backup-{timestamp}.jsonl"
    backup_lines = [
        json.dumps(backup_row(record, record.data), ensure_ascii=True, separators=(",", ":"))
        for record, _, _ in candidates
    ]
    backup_bytes = ("\n".join(backup_lines) + ("\n" if backup_lines else "")).encode("utf-8")
    backup_path.write_bytes(backup_bytes)
    backup_sha256 = hashlib.sha256(backup_bytes).hexdigest()

    changed = []
    scalar_only = []
    concurrent = []
    failed = []
    for original, header, additional in candidates:
        record_id = str(original.data.get(ID) or original.id)
        current = client.records.retrieve(TABLE, record_id, select=FIELDS)
        if current is None:
            failed.append({"id": record_id, "reason": "missing-before-update"})
            continue
        if (
            current.etag != original.etag
            or current.data.get("modifiedon") != original.data.get("modifiedon")
        ):
            concurrent.append(record_id)
            continue
        if not is_true(current.data.get("cr07a_verificado")):
            concurrent.append(record_id)
            continue

        repaired_additional = dict(additional)
        repaired_additional["Version"] = max(int(repaired_additional.get("Version") or 0), 1)
        repaired_additional["LastResult"] = build_last_result(repaired_additional, header)
        additional_json = json.dumps(
            repaired_additional,
            ensure_ascii=True,
            separators=(",", ":"),
        )
        can_update_additional = len(additional_json) <= 4000
        if not can_update_additional and isinstance(additional.get("LastResult"), dict):
            failed.append({"id": record_id, "reason": "additional-over-4000"})
            continue

        changes = {
            "cr07a_score": float(header["score"]),
            "cr07a_commission": float(header["commission"]),
            "cr07a_contractvalue": float(header["total"]),
        }
        if can_update_additional:
            changes["cr07a_adicionales"] = additional_json
        client.records.update(TABLE, record_id, changes)

        after = client.records.retrieve(TABLE, record_id, select=FIELDS)
        if after is None:
            failed.append({"id": record_id, "reason": "missing-after-update"})
            continue
        after_additional = json.loads(str(after.data.get("cr07a_adicionales") or "{}"))
        verified_unchanged = is_true(after.data.get("cr07a_verificado"))
        scalar_matches = (
            score_scalar_matches(after.data.get("cr07a_score"), header["score"])
            and equal_decimal(after.data.get("cr07a_commission"), header["commission"])
            and equal_decimal(after.data.get("cr07a_contractvalue"), header["total"])
        )
        result_verified = result_matches(after_additional, header) if can_update_additional else not isinstance(after_additional.get("LastResult"), dict)
        if not verified_unchanged or not scalar_matches or not result_verified:
            failed.append({"id": record_id, "reason": "readback-mismatch"})
            continue
        changed.append(record_id)
        if not can_update_additional:
            scalar_only.append(record_id)

    print(
        "APPLY "
        + json.dumps(
            {
                "environment": "DigitalTech",
                "backup": str(backup_path),
                "backupSha256": backup_sha256,
                "candidates": len(candidates),
                "changed": len(changed),
                "concurrent": concurrent,
                "failed": failed,
                "scalarOnly": scalar_only,
                "changedIds": changed,
            },
            ensure_ascii=True,
            separators=(",", ":"),
        )
    )


if __name__ == "__main__":
    main()
