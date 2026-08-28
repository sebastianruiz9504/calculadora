"""Audit and reset the Dataverse cash-flow records used by Conciliacion.

The reset is intentionally narrow:
- deletes cash-flow matches, internal transfers, and bank movements;
- deletes only monthly-close tasks whose unique key starts with the cash-flow prefix;
- leaves invoices, expenses, accounts payable, customers, catalogs, and Siigo untouched;
- writes a JSON backup before the first delete and verifies every target is empty.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import warnings
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent
sys.path.insert(0, str(SCRIPT_DIR))

from auth import get_client, load_env


warnings.filterwarnings("ignore", category=DeprecationWarning)

EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
MONTH_CLOSE_PREFIX = "conciliacion:flujo-caja:cierre:"

TABLES = (
    {
        "logical_name": "cr07a_cruceflujocaja",
        "id_field": "cr07a_cruceflujocajaid",
        "select": [
            "cr07a_cruceflujocajaid",
            "cr07a_name",
            "cr07a_tipo",
            "cr07a_estado",
            "cr07a_confianza",
            "cr07a_motivo",
            "cr07a_diferencia",
            "cr07a_movimientobancarioid",
            "cr07a_movimientoclaveexterna",
            "cr07a_fechamovimiento",
            "cr07a_origenflujo",
            "cr07a_bancocuentacodigo",
            "cr07a_bancocuentanombre",
            "cr07a_descripcionmovimiento",
            "cr07a_valorentrada",
            "cr07a_facturacionid",
            "cr07a_facturanumero",
            "cr07a_cliente",
            "cr07a_valorfactura",
            "cr07a_valorpago",
            "cr07a_reteftevalor",
            "cr07a_reteicavalor",
            "cr07a_rteivavalor",
            "cr07a_jsonborradorsiigo",
            "cr07a_preflightestado",
            "cr07a_preflightmensaje",
            "cr07a_preflightfecha",
            "cr07a_preflightdebito",
            "cr07a_preflightcredito",
            "cr07a_claveexterna",
            "cr07a_hashorigen",
        ],
    },
    {
        "logical_name": "cr07a_trasladointernoflujocaja",
        "id_field": "cr07a_trasladointernoflujocajaid",
        "select": [
            "cr07a_trasladointernoflujocajaid",
            "cr07a_name",
            "cr07a_fecha",
            "cr07a_origenflujo",
            "cr07a_flujodesde",
            "cr07a_flujohacia",
            "cr07a_entrada",
            "cr07a_salida",
            "cr07a_valor",
            "cr07a_descripcion",
            "cr07a_destinatario",
            "cr07a_bancodestino",
            "cr07a_tipodocumento",
            "cr07a_observaciones",
            "cr07a_estado",
            "cr07a_claveexterna",
            "cr07a_archivoorigen",
            "cr07a_tablaorigen",
            "cr07a_filaorigen",
            "cr07a_hashorigen",
        ],
    },
    {
        "logical_name": "cr07a_movimientobancario",
        "id_field": "cr07a_movimientobancarioid",
        "select": [
            "cr07a_movimientobancarioid",
            "cr07a_name",
            "cr07a_fecha",
            "cr07a_banco",
            "cr07a_descripcion",
            "cr07a_valorentrada",
            "cr07a_valorsalida",
            "cr07a_referencia",
            "cr07a_tipomovimiento",
            "cr07a_estado",
            "cr07a_siigodocumentid",
            "cr07a_siigodocumentname",
            "cr07a_cuentacontablecodigo",
            "cr07a_cuentacontablenombre",
            "cr07a_siigoterceroclave",
            "cr07a_siigoterceroidentificacion",
            "cr07a_siigoterceronombre",
            "cr07a_siigotercerosucursal",
            "cr07a_motivorevision",
            "cr07a_origenflujo",
            "cr07a_bancocuentacodigo",
            "cr07a_bancocuentanombre",
            "cr07a_destinatario",
            "cr07a_bancodestino",
            "cr07a_tipodocumento",
            "cr07a_observaciones",
            "cr07a_siigoestado",
            "cr07a_claveexterna",
            "cr07a_archivoorigen",
            "cr07a_tablaorigen",
            "cr07a_filaorigen",
            "cr07a_hashorigen",
        ],
    },
)

TASK_TABLE = {
    "logical_name": "cr07a_tarea",
    "id_field": "cr07a_tareaid",
    "select": [
        "cr07a_tareaid",
        "cr07a_name",
        "cr07a_claveunica",
        "cr07a_estado",
        "cr07a_modulo",
        "cr07a_tipo",
        "cr07a_sourceid",
        "cr07a_periodokey",
        "cr07a_fechalimite",
        "cr07a_fechacierre",
        "cr07a_comentariocierre",
        "cr07a_esmanual",
        "cr07a_actionurl",
    ],
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Delete the audited records. Without this flag the script is read-only.",
    )
    parser.add_argument(
        "--backup-dir",
        default=str(PROJECT_DIR / "a" / "dataverse-backups"),
    )
    return parser.parse_args()


def flatten(pages: Any) -> list[dict[str, Any]]:
    if isinstance(pages, dict):
        value_rows = pages.get("value")
        if isinstance(value_rows, list):
            return [dict(row) for row in value_rows]
        return [dict(pages)]
    materialized = list(pages)
    if not materialized:
        return []
    if all(isinstance(row, dict) for row in materialized):
        return [dict(row) for row in materialized]
    return [
        dict(row)
        for page in materialized
        for row in page
    ]


def read_rows(
    client: Any,
    table: dict[str, Any],
    filter_text: str | None = None,
) -> list[dict[str, Any]]:
    pages = client.records.get(
        table["logical_name"],
        select=table["select"],
        filter=filter_text,
    )
    return flatten(pages)


def read_scope(client: Any) -> dict[str, list[dict[str, Any]]]:
    scope: dict[str, list[dict[str, Any]]] = {}
    for table in TABLES:
        scope[table["logical_name"]] = read_rows(client, table)

    task_candidates = read_rows(
        client,
        TASK_TABLE,
        filter_text="cr07a_claveunica ne null",
    )
    scope[TASK_TABLE["logical_name"]] = [
        row
        for row in task_candidates
        if str(row.get("cr07a_claveunica", "") or "").casefold().startswith(
            MONTH_CLOSE_PREFIX.casefold()
        )
    ]
    return scope


def scope_summary(
    scope: dict[str, list[dict[str, Any]]],
) -> dict[str, Any]:
    counts = {
        table: len(rows)
        for table, rows in scope.items()
    }
    return {
        "counts": counts,
        "total": sum(counts.values()),
        "samples": {
            table: [
                {
                    key: value
                    for key, value in row.items()
                    if key in {
                        "cr07a_cruceflujocajaid",
                        "cr07a_trasladointernoflujocajaid",
                        "cr07a_movimientobancarioid",
                        "cr07a_tareaid",
                        "cr07a_name",
                        "cr07a_fecha",
                        "cr07a_claveunica",
                        "cr07a_claveexterna",
                    }
                }
                for row in rows[:3]
            ]
            for table, rows in scope.items()
        },
    }


def write_json(
    directory: Path,
    prefix: str,
    payload: dict[str, Any],
) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    path = directory / f"{prefix}-{timestamp}.json"
    path.write_text(
        json.dumps(payload, indent=2, ensure_ascii=True, default=str),
        encoding="utf-8",
    )
    return path


def delete_rows(
    client: Any,
    table: dict[str, Any],
    rows: list[dict[str, Any]],
) -> int:
    deleted = 0
    total = len(rows)
    for row in rows:
        record_id = str(row.get(table["id_field"], "") or "").strip()
        if not record_id:
            raise RuntimeError(
                f"Missing {table['id_field']} in {table['logical_name']} backup row."
            )
        client.records.delete(table["logical_name"], record_id)
        deleted += 1
        if deleted % 25 == 0 or deleted == total:
            print(
                f"Deleted {deleted}/{total} from {table['logical_name']}",
                flush=True,
            )
    return deleted


def main() -> int:
    args = parse_args()
    load_env()
    environment_url = os.environ["DATAVERSE_URL"].rstrip("/") + "/"
    if environment_url.casefold() != EXPECTED_ENVIRONMENT.casefold():
        raise RuntimeError(f"Unexpected Dataverse target: {environment_url}")

    client = get_client("dv-data" if args.apply else "dv-query")
    before = read_scope(client)
    before_summary = scope_summary(before)
    audit = {
        "mode": "apply" if args.apply else "audit",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "environment_url": environment_url,
        "month_close_task_prefix": MONTH_CLOSE_PREFIX,
        "scope": before_summary,
    }

    if not args.apply:
        print(json.dumps(audit, indent=2, ensure_ascii=True, default=str))
        return 0

    backup_dir = Path(args.backup_dir).resolve()
    backup_payload = {
        **audit,
        "records": before,
    }
    backup_path = write_json(
        backup_dir,
        "cash-flow-reset-before",
        backup_payload,
    )
    print(f"Backup written: {backup_path}", flush=True)

    deleted: dict[str, int] = {}
    for table in (*TABLES, TASK_TABLE):
        name = table["logical_name"]
        deleted[name] = delete_rows(client, table, before[name])

    after = read_scope(client)
    after_summary = scope_summary(after)
    remaining = {
        table: count
        for table, count in after_summary["counts"].items()
        if count != 0
    }
    if remaining:
        raise RuntimeError(
            "Cash-flow reset read-back found remaining records: "
            + json.dumps(remaining, ensure_ascii=True)
        )

    result = {
        **audit,
        "mode": "applied",
        "backup_path": str(backup_path),
        "deleted": deleted,
        "deleted_total": sum(deleted.values()),
        "after": after_summary,
    }
    result_path = write_json(
        backup_dir,
        "cash-flow-reset-result",
        result,
    )
    print(json.dumps(result, indent=2, ensure_ascii=True, default=str))
    print(f"Result written: {result_path}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
