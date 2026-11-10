"""Read-only schema and data coverage audit for the month-close dashboard."""
import json
from pathlib import Path
from auth import get_client

TABLES = ["cr07a_cliente", "cr07a_accountidicp", "cr07a_consumointcomex", "cr07a_entrega", "cr07a_facturasproveedorescopiers", "dtc_copiersmtov2", "cr07a_salesperformancerecord", "cr07a_licenciamientoaccountmap"]
with get_client("dv-metadata") as client:
    schemas = {}
    for table in TABLES:
        table_info = client.tables.get(table)
        print(table, json.dumps({k:v for k,v in table_info.items() if k.lower() in ("logicalname", "entitysetname", "table_schema_name", "entity_set_name")}, default=str), flush=True)
        columns = client.tables.list_columns(table)
        schemas[table] = columns
    output = Path("tmp/month-close-audit")
    output.mkdir(parents=True, exist_ok=True)
    (output / "columns.json").write_text(json.dumps(schemas, default=str, indent=2), encoding="utf-8")
    for table, columns in schemas.items():
        print(table, json.dumps(columns, default=str)[:220])
