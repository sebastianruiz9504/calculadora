#!/usr/bin/env python3
"""Read-only audit of score records against their immutable submission headers."""

from __future__ import annotations

import json
import os
import re
from decimal import Decimal, InvalidOperation

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
    "cr07a_contractstartdate",
    "createdon",
    "modifiedon",
]

HEADER_PATTERN = re.compile(
    r"(?im)^(Puntaje|Comisi(?:o|\u00f3)n|Venta mensual total|Venta total anual|Venta total)\s*:\s*([^\r\n]+)"
)


def decimal_text(value: str) -> Decimal | None:
    cleaned = re.sub(r"[^0-9+\-.]", "", value.strip())
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


def to_decimal(value: object) -> Decimal | None:
    if value is None:
        return None
    try:
        return Decimal(str(value))
    except InvalidOperation:
        return None


def different(left: Decimal | None, right: Decimal | None) -> bool:
    if left is None or right is None:
        return left != right
    return abs(left - right) > Decimal("0.005")


def main() -> None:
    credential = ClientSecretCredential(
        tenant_id=os.environ["TENANT_ID"],
        client_id=os.environ["CLIENT_ID"],
        client_secret=os.environ["CLIENT_SECRET"],
    )
    client = DataverseClient(
        base_url=os.environ["DATAVERSE_URL"],
        credential=credential,
        context=OperationContext(
            user_agent_context="app=dataverse-skills/1.6.0;skill=dv-query;agent=codex"
        ),
    )
    records = client.records.list(
        TABLE,
        select=FIELDS,
        orderby=["createdon asc"],
        page_size=500,
    )

    rows = []
    malformed = []
    for record in records:
        data = record.data
        record_id = str(data.get(ID) or record.id)
        description = str(
            data.get("cr07a_aprovisionamientodetallelargo")
            or data.get("cr07a_description")
            or ""
        )
        header = parse_header(description)
        if not {"score", "commission", "monthly", "total"}.issubset(header):
            malformed.append(
                {
                    "id": record_id,
                    "verified": bool(data.get("cr07a_verificado")),
                    "createdon": data.get("createdon"),
                    "headerKeys": sorted(header),
                    "descriptionLength": len(description),
                }
            )
            continue

        stored = {
            "score": to_decimal(data.get("cr07a_score")),
            "commission": to_decimal(data.get("cr07a_commission")),
            "total": to_decimal(data.get("cr07a_contractvalue")),
        }
        mismatch = {
            key: different(stored.get(key), header.get(key))
            for key in ("score", "commission", "total")
        }
        additional_raw = data.get("cr07a_adicionales")
        additional = None
        additional_error = False
        if additional_raw:
            try:
                additional = json.loads(str(additional_raw))
            except json.JSONDecodeError:
                additional_error = True

        rows.append(
            {
                "id": record_id,
                "verified": bool(data.get("cr07a_verificado")),
                "createdon": data.get("createdon"),
                "modifiedon": data.get("modifiedon"),
                "etag": record.etag,
                "header": {key: str(value) for key, value in header.items()},
                "stored": {key: None if value is None else str(value) for key, value in stored.items()},
                "mismatch": mismatch,
                "additionalError": additional_error,
                "additionalVersion": additional.get("Version") if isinstance(additional, dict) else None,
                "additionalLines": len(additional.get("Lines") or []) if isinstance(additional, dict) else 0,
                "hasLastResult": bool(additional.get("LastResult")) if isinstance(additional, dict) else False,
                "hasClosures": bool(additional.get("MonthlyClosures") or additional.get("UploadedLines")) if isinstance(additional, dict) else False,
            }
        )

    mismatches = [row for row in rows if any(row["mismatch"].values())]
    mismatch_shapes: dict[str, int] = {}
    for row in mismatches:
        shape = "+".join(key for key, value in row["mismatch"].items() if value)
        mismatch_shapes[shape] = mismatch_shapes.get(shape, 0) + 1
    summary = {
        "environment": "DigitalTech",
        "total": len(rows) + len(malformed),
        "parsed": len(rows),
        "mismatchCount": len(mismatches),
        "verifiedMismatchCount": sum(1 for row in mismatches if row["verified"]),
        "pendingMismatchCount": sum(1 for row in mismatches if not row["verified"]),
        "mismatchShapes": mismatch_shapes,
        "malformedCount": len(malformed),
        "malformedVerified": sum(1 for row in malformed if row["verified"]),
        "malformedPending": sum(1 for row in malformed if not row["verified"]),
    }
    print("SUMMARY " + json.dumps(summary, ensure_ascii=True, separators=(",", ":")))
    for row in mismatches[-4:]:
        compact = {
            "id": row["id"],
            "verified": row["verified"],
            "createdon": row["createdon"],
            "header": row["header"],
            "stored": row["stored"],
            "mismatch": row["mismatch"],
            "additionalLines": row["additionalLines"],
            "hasLastResult": row["hasLastResult"],
            "hasClosures": row["hasClosures"],
        }
        print("REC " + json.dumps(compact, ensure_ascii=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
