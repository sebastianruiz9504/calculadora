"""Audit and remove pending August cash-flow copies duplicated across verticals."""

import argparse
import json
import os
import re
from collections import defaultdict

from auth import get_client, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
MOVEMENT_TABLE = "cr07a_movimientobancario"
MOVEMENT_ID = "cr07a_movimientobancarioid"
MATCH_TABLE = "cr07a_cruceflujocaja"
COMPLETED_STATUSES = {"conciliado", "enviadosiigo"}
SAFE_PENDING_MATCH_STATUSES = {"sinfacturadescripcion", "reasignadocategoria"}


def row_dict(record):
    data = getattr(record, "data", None)
    return dict(data if data is not None else record)


def normalized(value):
    return re.sub(r"\s+", " ", str(value or "").strip()).casefold()


def signature(row):
    return (
        str(row.get("cr07a_fecha") or ""),
        str(row.get("cr07a_valorentrada") or 0),
        str(row.get("cr07a_valorsalida") or 0),
        normalized(row.get("cr07a_tipodocumento")),
        normalized(row.get("cr07a_observaciones")),
    )


def has_siigo_document(row):
    return bool(
        str(row.get("cr07a_siigodocumentid") or "").strip()
        or str(row.get("cr07a_siigodocumentname") or "").strip()
    )


def is_strongly_conciliated(row):
    status = normalized(row.get("cr07a_estado"))
    siigo_status = normalized(row.get("cr07a_siigoestado"))
    return has_siigo_document(row) and (
        status in COMPLETED_STATUSES or siigo_status in COMPLETED_STATUSES
    )


def is_clearly_pending(row):
    status = normalized(row.get("cr07a_estado"))
    siigo_status = normalized(row.get("cr07a_siigoestado"))
    return (
        not has_siigo_document(row)
        and status not in COMPLETED_STATUSES
        and siigo_status not in COMPLETED_STATUSES
    )


def concise(row):
    return {
        "id": row.get(MOVEMENT_ID),
        "flow": row.get("cr07a_origenflujo"),
        "account": row.get("cr07a_bancocuentacodigo"),
        "date": row.get("cr07a_fecha"),
        "entry": row.get("cr07a_valorentrada"),
        "exit": row.get("cr07a_valorsalida"),
        "description": row.get("cr07a_observaciones"),
        "status": row.get("cr07a_estado"),
        "siigo_status": row.get("cr07a_siigoestado"),
        "siigo_document": row.get("cr07a_siigodocumentname"),
        "external_key": row.get("cr07a_claveexterna"),
        "source_file": row.get("cr07a_archivoorigen"),
        "source_row": row.get("cr07a_filaorigen"),
    }


def read_august_rows(client):
    fields = [
        MOVEMENT_ID,
        "cr07a_fecha",
        "cr07a_valorentrada",
        "cr07a_valorsalida",
        "cr07a_tipodocumento",
        "cr07a_observaciones",
        "cr07a_origenflujo",
        "cr07a_bancocuentacodigo",
        "cr07a_estado",
        "cr07a_siigoestado",
        "cr07a_siigodocumentid",
        "cr07a_siigodocumentname",
        "cr07a_claveexterna",
        "cr07a_archivoorigen",
        "cr07a_filaorigen",
    ]
    sql = (
        f"SELECT TOP 5000 {', '.join(fields)} FROM {MOVEMENT_TABLE} "
        "WHERE cr07a_fecha >= '2026-08-01' AND cr07a_fecha < '2026-09-01'"
    )
    return [row_dict(record) for record in client.query.sql(sql)]


def read_match_references(client):
    fields = [
        "cr07a_cruceflujocajaid",
        "cr07a_movimientobancarioid",
        "cr07a_movimientoclaveexterna",
        "cr07a_estado",
        "cr07a_claveexterna",
        "cr07a_facturacionid",
        "cr07a_facturanumero",
        "cr07a_valorpago",
        "cr07a_jsonborradorsiigo",
    ]
    sql = f"SELECT TOP 5000 {', '.join(fields)} FROM {MATCH_TABLE}"
    return [row_dict(record) for record in client.query.sql(sql)]


def build_audit(rows, match_rows):
    grouped = defaultdict(list)
    for row in rows:
        grouped[signature(row)].append(row)

    references_by_id = defaultdict(list)
    references_by_key = defaultdict(list)
    for row in match_rows:
        movement_id = normalized(row.get("cr07a_movimientobancarioid"))
        movement_key = normalized(row.get("cr07a_movimientoclaveexterna"))
        if movement_id:
            references_by_id[movement_id].append(row)
        if movement_key:
            references_by_key[movement_key].append(row)

    eligible = []
    blocked = []
    untouched = []
    for group in grouped.values():
        flows = {normalized(row.get("cr07a_origenflujo")) for row in group}
        if len(group) != 2 or flows != {"cloud", "copiers"}:
            continue

        completed = [row for row in group if is_strongly_conciliated(row)]
        pending = [row for row in group if is_clearly_pending(row)]
        if len(completed) == 1 and len(pending) == 1:
            candidate = pending[0]
            candidate_id = normalized(candidate.get(MOVEMENT_ID))
            candidate_key = normalized(candidate.get("cr07a_claveexterna"))
            item = {
                "keep": concise(completed[0]),
                "delete": concise(candidate),
            }
            references = {
                str(reference.get("cr07a_cruceflujocajaid")): reference
                for reference in (
                    references_by_id.get(candidate_id, [])
                    + references_by_key.get(candidate_key, [])
                )
            }
            reference_rows = list(references.values())
            safe_references = all(
                normalized(reference.get("cr07a_estado")) in SAFE_PENDING_MATCH_STATUSES
                and not str(reference.get("cr07a_facturacionid") or "").strip()
                and not str(reference.get("cr07a_facturanumero") or "").strip()
                for reference in reference_rows
            )
            if not reference_rows or safe_references:
                item["delete_matches"] = [
                    str(reference.get("cr07a_cruceflujocajaid"))
                    for reference in reference_rows
                ]
                eligible.append(item)
            else:
                item["reason"] = "Pending copy has a non-draft client-payment reference"
                item["references"] = reference_rows
                blocked.append(item)
        else:
            untouched.append({"copies": [concise(row) for row in group]})

    return {
        "eligible": eligible,
        "blocked": blocked,
        "untouched": untouched,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--expected-count", type=int)
    parser.add_argument("--expected-match-count", type=int)
    args = parser.parse_args()

    load_env()
    environment_url = os.environ["DATAVERSE_URL"].rstrip("/") + "/"
    if environment_url.casefold() != EXPECTED_ENVIRONMENT.casefold():
        raise RuntimeError(f"Unexpected Dataverse target: {environment_url}")

    skill = "dv-data" if args.apply else "dv-query"
    client = get_client(skill)
    audit = build_audit(read_august_rows(client), read_match_references(client))
    eligible = audit["eligible"]

    result = {
        "environment_url": environment_url,
        "mode": "apply" if args.apply else "dry-run",
        "eligible_count": len(eligible),
        "eligible_match_count": sum(len(item["delete_matches"]) for item in eligible),
        "blocked_count": len(audit["blocked"]),
        "untouched_duplicate_groups": len(audit["untouched"]),
        "eligible": eligible,
        "blocked": audit["blocked"],
    }

    if args.apply:
        if args.expected_count is None:
            raise RuntimeError("--expected-count is required with --apply")
        if args.expected_match_count is None:
            raise RuntimeError("--expected-match-count is required with --apply")
        if len(eligible) != args.expected_count:
            raise RuntimeError(
                f"Expected {args.expected_count} eligible rows but found {len(eligible)}"
            )
        eligible_match_count = sum(len(item["delete_matches"]) for item in eligible)
        if eligible_match_count != args.expected_match_count:
            raise RuntimeError(
                f"Expected {args.expected_match_count} match rows but found {eligible_match_count}"
            )

        deleted_match_ids = []
        for item in eligible:
            for match_id in item["delete_matches"]:
                client.records.delete(MATCH_TABLE, match_id)
                deleted_match_ids.append(match_id)
        deleted_ids = []
        for item in eligible:
            record_id = str(item["delete"]["id"])
            client.records.delete(MOVEMENT_TABLE, record_id)
            deleted_ids.append(record_id)

        remaining = [
            record_id
            for record_id in deleted_ids
            if client.records.retrieve(MOVEMENT_TABLE, record_id) is not None
        ]
        if remaining:
            raise RuntimeError(f"Delete read-back failed for: {remaining}")
        remaining_matches = [
            match_id
            for match_id in deleted_match_ids
            if client.records.retrieve(MATCH_TABLE, match_id) is not None
        ]
        if remaining_matches:
            raise RuntimeError(f"Match delete read-back failed for: {remaining_matches}")
        result["deleted_ids"] = deleted_ids
        result["deleted_match_ids"] = deleted_match_ids
        result["read_back_missing_count"] = len(deleted_ids)
        result["match_read_back_missing_count"] = len(deleted_match_ids)

    print(json.dumps(result, ensure_ascii=True, indent=2, default=str), flush=True)


if __name__ == "__main__":
    main()
