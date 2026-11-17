"""Import locally verified statement rows. Dry-run first; never changes Siigo records.

Input: JSON array of {bank,period,account,opening,credits,debits,closing,rows},
with rows {date,amount,balance,page,line,description}. Amounts are signed.
OCR is intentionally separate: every running balance and both monthly totals
must agree before this script can write. Private PDFs/JSON remain outside git.
"""
import argparse
import hashlib
import json
import os
import uuid
from collections import Counter
from datetime import date
from decimal import Decimal
from pathlib import Path

from auth import get_client, load_env

MANUAL = 'cr07a_movimientomanualbanco'
BALANCE = 'cr07a_saldoflujodirecto'
CLOSE = 'cr07a_cierreflujocajabanco'


def money(value):
    amount = Decimal(str(value))
    if not amount.is_finite() or amount.quantize(Decimal('.01')) != amount:
        raise ValueError('Amounts must be finite, with at most two decimals')
    return amount


def validate(statements):
    if not statements or len({(s['bank'], s['period']) for s in statements}) != len(statements):
        raise ValueError('Empty or duplicate statement accounts')
    if len({s['period'] for s in statements}) != 1 or {s['bank'] for s in statements} != {'Cloud', 'Copiers'}:
        raise ValueError('Provide both configured accounts for the same month')
    for statement in statements:
        prior = money(statement['opening'])
        credits = debits = Decimal(0)
        previous_date = statement['period'] + '-01'
        for index, row in enumerate(statement['rows'], 1):
            if row['line'] != index or row['page'] < 1:
                raise ValueError('Statement row order is invalid')
            parsed_date = date.fromisoformat(row['date'])
            if parsed_date.isoformat()[:7] != statement['period'] or row['date'] < previous_date:
                raise ValueError('Unexpected transaction date')
            previous_date = row['date']
            amount = money(row['amount'])
            balance = money(row['balance'])
            if amount == 0 or prior + amount != balance:
                raise ValueError(f"Running balance mismatch: {statement['bank']} row {index}")
            if not 1 <= len(row['description']) <= 500:
                raise ValueError('Missing or oversized description')
            credits += max(0, amount)
            debits += max(0, -amount)
            prior = balance
        if not statement['rows'] or (credits, debits, prior) != tuple(money(statement[k]) for k in ('credits', 'debits', 'closing')):
            raise ValueError('Monthly summary does not agree with every statement row')


def read(client, table, filter):
    return [record.data for record in client.records.list(table, filter=filter)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--pdf', action='append', required=True, help='Bank=absolute PDF path')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    statements = json.loads(args.input.read_text(encoding='utf-8'))
    validate(statements)
    pdfs = dict(item.split('=', 1) for item in args.pdf)
    for statement in statements:
        statement['fileHash'] = hashlib.sha256(Path(pdfs[statement['bank']]).read_bytes()).hexdigest()
    load_env()
    if os.environ['DATAVERSE_URL'].rstrip('/') != 'https://orgc79ca19c.crm2.dynamics.com':
        raise RuntimeError('Unexpected production environment')
    period = statements[0]['period']
    first = date.fromisoformat(period + '-01')
    end = date(first.year + (first.month == 12), first.month % 12 + 1, 1)
    filter = f'cr07a_fecha ge {first}T00:00:00Z and cr07a_fecha lt {end}T00:00:00Z and statecode eq 0'
    args.output.mkdir(parents=True, exist_ok=True)
    with get_client('dv-data' if args.apply else 'dv-query') as client:
        solutions=read(client,'solution',"uniquename eq 'CotizadorInternoCRM'")
        if len(solutions)!=1 or client.records.retrieve('publisher',solutions[0]['_publisherid_value'])['customizationprefix']!='cr07a':
            raise RuntimeError('Unexpected existing solution or publisher')
        originals = read(client, 'cr07a_movimientobancario', filter)
        manual = read(client, MANUAL, filter)
        existing_balances = read(client, BALANCE, f"cr07a_periodo eq '{period}'")
        existing_openings = read(client, CLOSE, f"cr07a_periodokey eq '{period}'")
        if len(existing_balances) > 1:
            raise RuntimeError('Duplicate monthly balance records')
        backup = args.output / 'before-import.json'
        if not backup.exists():
            backup.write_text(json.dumps({'originals': originals, 'manual': manual, 'balances': existing_balances, 'openings': existing_openings}, indent=2), encoding='utf8')
        existing_statement_data = json.loads((existing_balances[0].get('cr07a_extractos') or '[]') if existing_balances else '[]')
        if existing_statement_data and {(s['bank'], s['fileHash']) for s in existing_statement_data} != {(s['bank'], s['fileHash']) for s in statements}:
            raise RuntimeError('Different statement already imported for this period; explicit review required')
        available = []
        for row in originals:
            available.append({'recordId': row['cr07a_movimientobancarioid'], 'sourceKind': 'Movimiento', 'bank': row['cr07a_origenflujo'], 'date': row['cr07a_fecha'][:10], 'amount': money(row.get('cr07a_valorentrada') or 0) - money(row.get('cr07a_valorsalida') or 0)})
        for row in manual:
            available.append({'recordId': row[MANUAL+'id'], 'sourceKind': 'Manual', 'bank': row['cr07a_banco'], 'date': row['cr07a_fecha'][:10], 'amount': money(row.get('cr07a_entrada') or 0) - money(row.get('cr07a_salida') or 0)})
        used = set()
        creates = []
        stored = []
        for statement in statements:
            audit = {key: statement[key] for key in ('bank', 'period', 'account', 'fileHash')}
            audit.update({key: float(money(statement[key])) for key in ('opening', 'credits', 'debits', 'closing')})
            audit['rows'] = []
            for row in statement['rows']:
                amount = money(row['amount'])
                stable_id = str(uuid.uuid5(uuid.NAMESPACE_URL, f"bank-statement:{statement['bank']}:{period}:{statement['fileHash']}:{row['line']}"))
                candidates = [r for r in available if r['recordId'] not in used and r['bank'] == statement['bank'] and r['date'] == row['date'] and r['amount'] == amount]
                selected = next((r for r in candidates if r['recordId'] == stable_id), candidates[0] if candidates else None)
                if selected:
                    used.add(selected['recordId'])
                    record_id, kind = selected['recordId'], selected['sourceKind']
                else:
                    record_id, kind = stable_id, 'Manual'
                    creates.append({MANUAL+'id': record_id, 'cr07a_name': f"Extracto {period} {statement['bank']} {row['line']}", 'cr07a_fecha': row['date'], 'cr07a_banco': statement['bank'], 'cr07a_entrada': float(max(0, amount)), 'cr07a_salida': float(max(0, -amount)), 'cr07a_descripcion': row['description'], 'cr07a_referencia': f"Extracto {period} · página {row['page']} · fila {row['line']}", 'cr07a_concepto': '', 'cr07a_origen': 'Extracto', 'cr07a_estadosiigo': 'NoEnviar', 'cr07a_loteextracto': statement['fileHash']})
                audit['rows'].append({'recordId': record_id, 'sourceKind': kind, 'date': row['date'], 'amount': float(amount), 'balance': float(money(row['balance'])), 'page': row['page'], 'line': row['line']})
            stored.append(audit)
        unmatched = [r['recordId'] for r in available if r['recordId'] not in used]
        if unmatched:
            raise RuntimeError(f'{len(unmatched)} existing movements have no statement match; review before importing: {unmatched}')
        plan = {'period': period, 'matched': len(used), 'create': len(creates), 'byBank': dict(Counter(r['cr07a_banco'] for r in creates)), 'statements': stored, 'newRecords': creates}
        (args.output / 'import-plan.json').write_text(json.dumps(plan, indent=2, ensure_ascii=False), encoding='utf8')
        print(json.dumps({k: v for k, v in plan.items() if k not in ('statements', 'newRecords')}), flush=True)
        if not args.apply:
            return
        for start in range(0, len(creates), 50):
            client.records.create(MANUAL, creates[start:start+50])
            print(f'Created {min(start+50,len(creates))}/{len(creates)}', flush=True)
        persisted = {r[MANUAL+'id']: r for r in read(client, MANUAL, filter)}
        for expected in creates:
            actual = persisted[expected[MANUAL+'id']]
            for field in ('cr07a_entrada', 'cr07a_salida'):
                if money(actual[field]) != money(expected[field]):
                    raise RuntimeError('Persisted amount differs from statement')
            if actual['cr07a_origen'] != 'Extracto' or actual['cr07a_estadosiigo'] != 'NoEnviar':
                raise RuntimeError('Unexpected bank-only import status')
        balance_id = str(uuid.UUID(bytes_le=hashlib.sha256(('direct-cash-balance:'+period).encode()).digest()[:16]))
        payload = {'cr07a_name': period, 'cr07a_periodo': period, 'cr07a_inicial': float(sum(money(s['opening']) for s in statements)), 'cr07a_cloud': next(s['closing'] for s in stored if s['bank']=='Cloud'), 'cr07a_copiers': next(s['closing'] for s in stored if s['bank']=='Copiers'), 'cr07a_extractos': json.dumps(stored, ensure_ascii=False, separators=(',', ':'))}
        if existing_balances:
            client.records.update(BALANCE, existing_balances[0][BALANCE+'id'], payload)
        else:
            client.records.create(BALANCE, {BALANCE+'id': balance_id, **payload})
        for statement in statements:
            bank = statement['bank']
            code = '11100504' if bank == 'Cloud' else '11100503'
            # Use the configured account code from actual matched reconciliation rows.
            codes = {r['cr07a_bancocuentacodigo'] for r in originals if r['cr07a_origenflujo']==bank}
            if len(codes)!=1:
                raise RuntimeError('Ambiguous configured bank account code')
            code = codes.pop()
            key = f'conciliacion:flujo-caja:banco:{period}:{bank.lower()}:{code}'
            fields = {'cr07a_name': f'{period} - {bank} - {code}', 'cr07a_claveexterna': key, 'cr07a_periodokey': period, 'cr07a_origenflujo': bank, 'cr07a_bancocuentacodigo': code, 'cr07a_bancocuentanombre': f'Bancolombia {bank} {statement["account"][-4:]}', 'cr07a_saldoinicial': float(money(statement['opening']))}
            prior = [r for r in existing_openings if r['cr07a_origenflujo']==bank]
            if len(prior)>1: raise RuntimeError('Duplicate opening balances')
            if prior: client.records.update(CLOSE, prior[0][CLOSE+'id'], fields)
            else: client.records.create(CLOSE, {CLOSE+'id':str(uuid.uuid5(uuid.NAMESPACE_URL,key)), **fields})
        final = read(client, BALANCE, f"cr07a_periodo eq '{period}'")
        if len(final)!=1 or json.loads(final[0]['cr07a_extractos'])!=stored:
            raise RuntimeError('Statement control was not persisted correctly')
        if read(client,'cr07a_movimientobancario',filter)!=originals:
            # Compare unordered records as SDK pagination order can vary.
            now={r['cr07a_movimientobancarioid']:r for r in read(client,'cr07a_movimientobancario',filter)}
            if now!={r['cr07a_movimientobancarioid']:r for r in originals}:
                raise RuntimeError('Original bank records changed during import; review concurrency')
        print('Verified: statements, amounts, bank-only status, opening and closing balances; original movements unchanged.', flush=True)


if __name__ == '__main__':
    main()
