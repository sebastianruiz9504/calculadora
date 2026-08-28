"""Read one client invoice payment record from the configured Dataverse."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

from auth import get_client, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
TABLE = "cr07a_facturacion"
OUTPUT_FIELDS = {
    "cr07a_facturacionid",
    "cr07a_name",
    "cr07a_totalfactura",
    "cr07a_ivavalor",
    "cr07a_valorpago",
    "cr07a_reteftevalor",
    "cr07a_retefuentevalor",
    "cr07a_reteicavalor",
    "cr07a_reteivavalor",
    "cr07a_rteivavalor",
    "cr07a_diferencia",
    "cr07a_fechadepago",
    "cr07a_siigoinvoiceid",
    "cr07a_siigoinvoicename",
}
MOVEMENT_FIELDS = {
    "cr07a_movimientobancarioid",
    "cr07a_name",
    "cr07a_fecha",
    "cr07a_descripcion",
    "cr07a_valorentrada",
    "cr07a_estado",
    "cr07a_claveexterna",
    "cr07a_siigodocumentid",
    "cr07a_siigodocumentname",
}
MATCH_FIELDS = {
    "cr07a_cruceflujocajaid",
    "cr07a_name",
    "cr07a_estado",
    "cr07a_diferencia",
    "cr07a_valorentrada",
    "cr07a_valorpago",
    "cr07a_reteftevalor",
    "cr07a_reteicavalor",
    "cr07a_rteivavalor",
    "cr07a_facturanumero",
    "cr07a_movimientobancarioid",
    "cr07a_movimientoclaveexterna",
}


def available_columns(client, table: str) -> dict[str, str]:
    column_rows = client.query.sql_columns(table)
    column_names = [
        str(column.get("name") or column.get("logicalname") or "")
        if isinstance(column, dict)
        else str(column)
        for column in column_rows
    ]
    return {
        column.casefold(): column
        for column in column_names
        if column
    }


def selected_columns(
    available: dict[str, str],
    requested: set[str],
) -> list[str]:
    return [
        available[field]
        for field in requested
        if field in available
    ]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("invoice_number", nargs="+")
    args = parser.parse_args()

    load_env()
    environment_url = os.environ["DATAVERSE_URL"].rstrip("/") + "/"
    if environment_url.casefold() != EXPECTED_ENVIRONMENT.casefold():
        raise RuntimeError(f"Unexpected Dataverse target: {environment_url}")

    invoice_numbers = [value.strip() for value in args.invoice_number if value.strip()]
    escaped = [value.replace("'", "''") for value in invoice_numbers]
    client = get_client("dv-query")
    available = available_columns(client, TABLE)
    selected = selected_columns(available, OUTPUT_FIELDS)
    if "cr07a_name" not in {field.casefold() for field in selected}:
        raise RuntimeError("Invoice number column was not found.")
    queried = client.query.sql(
        f"SELECT TOP 10 {', '.join(selected)} "
        f"FROM {TABLE} WHERE cr07a_name IN ({', '.join(repr(value) for value in escaped)})"
    )
    rows = [
        {
            key: value
            for key, value in dict(row).items()
            if key.casefold() in OUTPUT_FIELDS
        }
        for row in queried
    ]
    payment_value = sum(float(row.get("cr07a_valorpago") or 0) for row in rows)
    related = {}
    if payment_value > 0:
        lower = payment_value - 1
        upper = payment_value + 1
        for table, value_field, requested in (
            ("cr07a_movimientobancario", "cr07a_valorentrada", MOVEMENT_FIELDS),
            ("cr07a_cruceflujocaja", "cr07a_valorentrada", MATCH_FIELDS),
        ):
            table_available = available_columns(client, table)
            table_selected = selected_columns(table_available, requested)
            queried_related = client.query.sql(
                f"SELECT TOP 20 {', '.join(table_selected)} "
                f"FROM {table} "
                f"WHERE {value_field} >= {lower:.2f} "
                f"AND {value_field} <= {upper:.2f}"
            )
            related[table] = [dict(row) for row in queried_related]
    result = {
        "environment_url": environment_url,
        "invoice_numbers": invoice_numbers,
        "count": len(rows),
        "records": rows,
        "related": related,
    }
    print(json.dumps(result, indent=2, ensure_ascii=True, default=str))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
