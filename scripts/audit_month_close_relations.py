"""Read-only audit: exact account relations, invoice-month costs and Copiers coverage."""
import json
from collections import Counter
from pathlib import Path
from auth import get_client

queries = {
    "clients": ("cr07a_cliente", ["cr07a_clienteid", "cr07a_nombre", "cr07a_nit", "cr07a_ciudad"], None),
    "accounts": ("cr07a_accountidicp", ["cr07a_accountidicpid", "cr07a_name", "_cr07a_cliente_value", "cr07a_grupoempresarialid", "cr07a_grupoempresarialname", "statecode"], None),
    "account-maps": ("cr07a_licenciamientoaccountmap", ["cr07a_licenciamientoaccountmapid", "cr07a_active", "cr07a_sourceaccountid", "cr07a_sourceaccountname", "cr07a_targetaccountid", "cr07a_targetclientid", "cr07a_targetclientname"], None),
    "licensing": ("cr07a_consumointcomex", ["cr07a_consumointcomexid", "cr07a_nombrecliente", "_cr07a_accountid_value", "cr07a_factura", "cr07a_mesconsumo", "cr07a_pesostotal", "cr07a_valortotalusd", "cr07a_trm", "cr07a_tipocontrato", "cr07a_name", "cr07a_productocorto"], "cr07a_factura ge 2026-01-01T00:00:00Z"),
    "supply-purchases": ("cr07a_facturasproveedorescopiers", ["cr07a_facturasproveedorescopiersid", "cr07a_name", "_cr07a_suministro_value", "cr07a_cantidad", "cr07a_valorunitarioantesdeiva", "cr07a_aprobadoeingresado", "createdon"], None),
    "deliveries": ("cr07a_entrega", ["cr07a_entregaid", "cr07a_entrega1", "_cr07a_iddecliente_value", "_cr07a_iddesuministro_value", "cr07a_fechadeentrega", "cr07a_cantidadentregada", "cr07a_estadodeentrega"], "cr07a_fechadeentrega ge 2026-01-01T00:00:00Z"),
    "maintenance": ("dtc_copiersmtov2", ["dtc_copiersmtov2id", "dtc_reference", "_dtc_client_value", "dtc_clientnamesnapshot", "dtc_servicedate", "dtc_workflowstate", "dtc_businessstatus", "dtc_formversion", "dtc_legacysourcekey", "dtc_serviceaddressinternal"], "dtc_servicedate ge 2026-01-01T00:00:00Z"),
}
root = Path("tmp/month-close-audit")
root.mkdir(parents=True, exist_ok=True)
with get_client("dv-query") as client:
    data = {}
    for key, (table, fields, where) in queries.items():
        data[key] = [dict(r) for r in client.records.list(table, select=fields, filter=where, include_annotations="OData.Community.Display.V1.FormattedValue")]
        print(key, len(data[key]), flush=True)
    (root / "relations.json").write_text(json.dumps(data, default=str), encoding="utf-8")
    print("licensing_invoice_months", dict(Counter(str(r.get("cr07a_factura", ""))[:7] for r in data["licensing"])))
    print("missing_account", sum(not r.get("_cr07a_accountid_value") for r in data["licensing"]))
    print("accounts_without_client", sum(not r.get("_cr07a_cliente_value") for r in data["accounts"]))
    print("duplicate_account_labels", {k:v for k,v in Counter(str(r.get("cr07a_name", "")).strip().lower() for r in data["accounts"]).items() if v > 1})
