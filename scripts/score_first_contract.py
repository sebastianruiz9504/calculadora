"""Approved first-contract migration and synchronous rule registration.

Uses the Dataverse skills SDK for metadata/CRUD. Conditional updates and actions
use the managed CLI because the Python SDK has no public ETag/action interface.
Evidence contains business records: keep --evidence outside tracked source.
"""
import argparse
import base64
import collections
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import uuid

ENV = 'https://orgc79ca19c.crm2.dynamics.com'
SOLUTION = 'CotizadorInternoCRM'
TABLE = 'cr07a_contractrecord1'
ID = TABLE + 'id'
CLIENT = '_cr07a_cliente_value'
FLAG = 'cr07a_esprimercontratoconelcliente'
START = 'cr07a_contractstartdate'
LOCK = 'cr07a_scoreclientlock'
ASSEMBLY = 'DigitalTech.Puntajes.FirstContract'
TYPE = 'DigitalTech.Puntajes.FirstContractPlugin'


def api(method, path, body=None, headers=()):
    executable = shutil.which('dataverse')
    launcher = Path(executable).parent / 'node_modules/@microsoft/dataverse/bin/dataverse.js'
    command = [shutil.which('node'), str(launcher)] if launcher.exists() else [executable]
    args = command + ['api', 'request', '--target', 'dataverse', '--method', method,
        '--path', '/api/data/v9.2/' + path.replace('$', '%24'), '--environment', ENV,
        '--context', 'app=dataverse-skills/1.11.3;skill=dv-data;agent=codex']
    with tempfile.TemporaryDirectory() as tmp:
        if body is not None:
            p = Path(tmp) / 'body.json'
            p.write_text(json.dumps(body), encoding='utf-8')
            args += ['--body-file', str(p)]
        for h in headers:
            args += ['--header', h]
        r = subprocess.run(args, capture_output=True, text=True, encoding='utf-8', errors='replace')
    s = re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', r.stdout + r.stderr)
    a, b = s.find('{'), s.rfind('}')
    value = json.loads(s[a:b+1]) if a >= 0 else {}
    if r.returncode or 'error' in value:
        raise RuntimeError(s[-2500:])
    return value


def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True), encoding='utf-8')


def plan(rows):
    groups = collections.defaultdict(list)
    for row in rows:
        if row.get(CLIENT): groups[row[CLIENT]].append(row)
    desired = {}
    for rs in groups.values():
        ordered = sorted(rs, key=lambda x: (x.get('createdon') or '9999', x.get(START) or '9999', x[ID].lower()))
        for i, row in enumerate(ordered): desired[row[ID]] = 1 if i == 0 else 2
    return desired


def list_rows(client):
    return [dict(r) for r in client.records.list(TABLE)]


def preflight(client):
    org = api('GET', 'WhoAmI')
    assert org['OrganizationId'] == 'c5dd555a-b9f6-ee11-a1fb-6045bd3ad3e8', org
    solution = client.records.retrieve('solution', '062440a6-ee86-f111-8075-70a8a5a95cf5')
    assert solution['uniquename'] == SOLUTION and not solution['ismanaged']
    publisher = client.records.retrieve('publisher', solution['_publisherid_value'])
    assert publisher['customizationprefix'] == 'cr07a'


def add_component(identity, kind):
    return api('POST', 'AddSolutionComponent', {'ComponentId': identity, 'ComponentType': kind,
        'SolutionUniqueName': SOLUTION, 'AddRequiredComponents': False})


def register(client, evidence, dll):
    existing = api('GET', "EntityDefinitions?$select=MetadataId,LogicalName&$filter=LogicalName eq '" + LOCK + "'")['value']
    if not existing:
        # SDK 1.0.0 hardcodes LCID 1033; this Spanish-only org requires 3082.
        # Metadata API also permits OrganizationOwned, absent from the SDK create signature.
        label = lambda text: {'@odata.type': 'Microsoft.Dynamics.CRM.Label',
            'LocalizedLabels': [{'@odata.type': 'Microsoft.Dynamics.CRM.LocalizedLabel', 'Label': text, 'LanguageCode': 3082}]}
        def string_column(schema, primary=False):
            return {'@odata.type': 'Microsoft.Dynamics.CRM.StringAttributeMetadata', 'SchemaName': schema,
                'DisplayName': label(schema), 'RequiredLevel': {'Value': 'None'}, 'MaxLength': 100,
                'IsPrimaryName': primary}
        payload = {'@odata.type': 'Microsoft.Dynamics.CRM.EntityMetadata', 'SchemaName': 'cr07a_ScoreClientLock',
            'DisplayName': label('Puntajes - bloqueo por cliente'), 'DisplayCollectionName': label('Puntajes - bloqueos por cliente'),
            'OwnershipType': 'OrganizationOwned', 'HasActivities': False, 'HasNotes': False, 'IsActivity': False,
            'PrimaryNameAttribute': 'cr07a_name', 'Attributes': [string_column('cr07a_Name', True), string_column('cr07a_Nonce')]}
        print('Creating Spanish technical lock table', flush=True)
        api('POST', 'EntityDefinitions', payload, ['MSCRM.SolutionUniqueName:' + SOLUTION])
        info = api('GET', "EntityDefinitions(LogicalName='" + LOCK + "')?$select=MetadataId,LogicalName,EntitySetName")
        save(evidence / 'lock-table-created.json', info)
    print('Registering signed assembly', flush=True)
    assembly = list(client.records.list('pluginassembly', filter=f"name eq '{ASSEMBLY}'", select=['pluginassemblyid']))
    content = base64.b64encode(dll.read_bytes()).decode()
    if assembly:
        aid = assembly[0]['pluginassemblyid']
        client.records.update('pluginassembly', aid, {'content': content})
    else:
        aid = client.records.create('pluginassembly', {'name': ASSEMBLY, 'content': content, 'isolationmode': 2, 'sourcetype': 0})
    types = list(client.records.list('plugintype', filter=f"typename eq '{TYPE}'", select=['plugintypeid']))
    pid = types[0]['plugintypeid'] if types else client.records.create('plugintype',
        {'typename': TYPE, 'name': TYPE, 'friendlyname': TYPE, 'pluginassemblyid@odata.bind': f'/pluginassemblies({aid})'})
    add_component(aid, 91)
    steps = []
    for message in ('Create', 'Update', 'Delete'):
        mid = list(client.records.list('sdkmessage', filter=f"name eq '{message}'", select=['sdkmessageid']))[0]['sdkmessageid']
        filters = list(client.records.list('sdkmessagefilter', filter=f"_sdkmessageid_value eq {mid} and primaryobjecttypecode eq '{TABLE}'", select=['sdkmessagefilterid']))
        assert len(filters) == 1
        fid = filters[0]['sdkmessagefilterid']
        for stage in (20, 40):
            name = f'DigitalTech FirstContract: {message} {stage}'
            found = list(client.records.list('sdkmessageprocessingstep', filter=f"name eq '{name}'", select=['sdkmessageprocessingstepid','statecode']))
            if found:
                sid = found[0]['sdkmessageprocessingstepid']
            else:
                payload = {'name': name, 'stage': stage, 'mode': 0, 'rank': 1, 'supporteddeployment': 0,
                    'configuration': 'disabled',
                    'eventhandler_plugintype@odata.bind': f'/plugintypes({pid})',
                    'sdkmessageid@odata.bind': f'/sdkmessages({mid})',
                    'sdkmessagefilterid@odata.bind': f'/sdkmessagefilters({fid})'}
                sid = client.records.create('sdkmessageprocessingstep', payload)
            update = {'configuration': 'disabled', 'statecode': 1, 'statuscode': 2}
            if message == 'Update':
                update['filteringattributes'] = 'cr07a_cliente,createdon,cr07a_contractstartdate' + (',' + FLAG if stage == 20 else '')
            client.records.update('sdkmessageprocessingstep', sid, update)
            if message != 'Create':
                images = list(client.records.list('sdkmessageprocessingstepimage', filter=f"_sdkmessageprocessingstepid_value eq {sid}", select=['sdkmessageprocessingstepimageid']))
                if not images:
                    client.records.create('sdkmessageprocessingstepimage', {'name': 'Before', 'entityalias': 'Before',
                        'imagetype': 0, 'attributes': 'cr07a_cliente', 'messagepropertyname': 'Target',
                        'sdkmessageprocessingstepid@odata.bind': f'/sdkmessageprocessingsteps({sid})'})
            add_component(sid, 92)
            print('Registered disabled step: ' + name, flush=True)
            steps.append({'id': sid, 'message': message, 'stage': stage})
            save(evidence / 'registration.json', {'assemblyId': aid, 'typeId': pid, 'steps': steps, 'dllSha256': hashlib.sha256(dll.read_bytes()).hexdigest()})
    print(json.dumps({'registeredDisabledSteps': len(steps), 'assemblyId': aid}), flush=True)


def validate(client, evidence):
    rows = list_rows(client)
    desired = plan(rows)
    bad = [r[ID] for r in rows if r[ID] in desired and r.get(FLAG) != desired[r[ID]]]
    baseline = json.loads((evidence / 'before.json').read_text())
    current = {r[ID]: r for r in rows}
    # Business columns must remain identical; only flag and system audit fields may change.
    ignore = {FLAG, 'modifiedon', '_modifiedby_value', '_modifiedonbehalfby_value', 'versionnumber'}
    changed_other = []
    for original in baseline:
        after = current.get(original[ID])
        if after is None: changed_other.append(original[ID]); continue
        for key in set(original) | set(after):
            if key.startswith('@') or '@' in key or key in ignore: continue
            if original.get(key) != after.get(key): changed_other.append((original[ID], key))
    summary = {'records': len(rows), 'clients': len({r[CLIENT] for r in rows if r.get(CLIENT)}),
        'yes': sum(r.get(FLAG) == 1 for r in rows if r.get(CLIENT)),
        'no': sum(r.get(FLAG) == 2 for r in rows if r.get(CLIENT)),
        'unlinked': sum(not r.get(CLIENT) for r in rows), 'invalid': bad, 'otherBusinessChanges': changed_other}
    save(evidence / 'after.json', rows)
    save(evidence / 'verification.json', summary)
    print(json.dumps(summary), flush=True)
    if bad or changed_other: raise RuntimeError('Read-back failed')


def main():
    p = argparse.ArgumentParser()
    p.add_argument('mode', choices=['backup', 'register', 'activate', 'backfill', 'verify', 'disable'])
    p.add_argument('--auth-dir', required=True)
    p.add_argument('--evidence', required=True, type=Path)
    p.add_argument('--dll', type=Path)
    args = p.parse_args()
    os.environ.update(DATAVERSE_URL=ENV, TENANT_ID='cab7ea42-4a21-4548-952f-fcde81f2bdd6',
        DATAVERSE_PLUGIN_VERSION='1.11.3', DATAVERSE_PLUGIN_AGENT='codex')
    sys.path.insert(0, args.auth_dir)
    from auth import get_client
    args.evidence.mkdir(parents=True, exist_ok=True)
    with get_client('dv-data') as client:
        preflight(client)
        if args.mode == 'backup':
            if (args.evidence / 'before.json').exists(): raise RuntimeError('Refusing to replace baseline')
            rows = list_rows(client)
            save(args.evidence / 'before.json', rows)
            desired = plan(rows)
            changes = [{'id': r[ID], 'client': r.get(CLIENT), 'before': r.get(FLAG), 'after': desired[r[ID]]}
                for r in rows if r[ID] in desired and r.get(FLAG) != desired[r[ID]]]
            save(args.evidence / 'plan.json', changes)
            print(json.dumps({'backupRecords': len(rows), 'plannedChanges': len(changes)}), flush=True)
        elif args.mode == 'register': register(client, args.evidence, args.dll)
        elif args.mode in ('activate', 'disable'):
            registration = json.loads((args.evidence / 'registration.json').read_text())
            steps = registration['steps']
            assert len(steps) == 6
            if args.mode == 'activate':
                assembly = client.records.retrieve('pluginassembly', registration['assemblyId'])
                assert hashlib.sha256(base64.b64decode(assembly['content'])).hexdigest() == registration['dllSha256']
                for step in steps:
                    actual = client.records.retrieve('sdkmessageprocessingstep', step['id'])
                    assert actual['stage'] == step['stage'] and actual['mode'] == 0
                    assert actual['_eventhandler_value'] == registration['typeId']
                    if step['message'] != 'Create':
                        images = list(client.records.list('sdkmessageprocessingstepimage', filter=f"_sdkmessageprocessingstepid_value eq {step['id']}"))
                        assert len(images) == 1 and images[0]['entityalias'] == 'Before' and images[0]['attributes'] == 'cr07a_cliente'
            # Enable guards before reconciliation; disable reconciliation before guards.
            for step in sorted(steps, key=lambda x: x['stage'], reverse=args.mode == 'disable'):
                enabled = args.mode == 'activate'
                client.records.update('sdkmessageprocessingstep', step['id'], {'configuration': 'enabled' if enabled else 'disabled', 'statecode': 0 if enabled else 1, 'statuscode': 1 if enabled else 2})
            print(args.mode, len(steps), flush=True)
        elif args.mode == 'backfill':
            baseline = json.loads((args.evidence / 'before.json').read_text())
            desired = plan(baseline)
            # Each flag-only write is normalized in PreOperation without recursive sibling updates.
            groups = collections.defaultdict(list)
            for row in baseline:
                if row.get(CLIENT): groups[row[CLIENT]].append(row)
            stop = threading.Event()
            def reconcile(cid, rs):
                if stop.is_set(): return
                path = f'{TABLE}s?$select={ID},{FLAG},createdon,{START},{CLIENT}&$filter={CLIENT} eq {cid}'
                response = api('GET', path)
                if response.get('@odata.nextLink'): raise RuntimeError('Unexpected paging; refusing incomplete client')
                current = response['value']
                baseline_keys = sorted((r[ID], r.get('createdon'), r.get(START)) for r in rs)
                live_keys = sorted((r[ID], r.get('createdon'), r.get(START)) for r in current)
                if baseline_keys != live_keys: raise RuntimeError('Client chronology changed since approval: ' + cid)
                mismatches = sorted((r for r in current if r.get(FLAG) != desired[r[ID]]), key=lambda r: -desired[r[ID]])
                for mismatch in mismatches:
                    if stop.is_set(): return
                    rid = mismatch[ID]
                    api('PATCH', f'{TABLE}s({rid})', {FLAG: desired[rid]}, ['If-Match:' + mismatch['@odata.etag']])
                checked = api('GET', path)['value'] if mismatches else current
                if any(r.get(FLAG) != desired[r[ID]] for r in checked):
                    raise RuntimeError('Synchronous rule did not reconcile entire client: ' + cid)
            # Independent clients only; one worker owns all writes for a client.
            with ThreadPoolExecutor(max_workers=4) as pool:
                tasks = [pool.submit(reconcile, cid, rs) for cid, rs in groups.items()]
                try:
                    for index, future in enumerate(as_completed(tasks)):
                        future.result()
                        print(f'Client {index+1}/{len(groups)} reconciled', flush=True)
                except Exception:
                    stop.set()
                    for future in tasks: future.cancel()
                    raise
            validate(client, args.evidence)
        elif args.mode == 'verify': validate(client, args.evidence)


if __name__ == '__main__': main()
