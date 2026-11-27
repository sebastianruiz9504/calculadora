"""Provision, initialize and verify the authorized Cloud cost-center change.

Never posts an invoice or starts the production TRM flow.
"""
import argparse
import json
import hashlib
import uuid
import sys
import base64
import html
import io
import subprocess
import zipfile
from pathlib import Path
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from access import client, dv, load_flow, request, siigo_headers, save, OUT, FLOW_URL, redact
from flow_definition import FIELD, TABLE, PK, SOLUTION, CENTERS, build, differences

PROTECTED_FIELDS = ['statecode','cr07a_facturado','cr07a_fechaultimafactura',
    'cr07a_siigo_invoice_id','cr07a_error_facturacion','cr07a_quantity',
    'cr07a_facturableautomatico','cr07a_unitsalevalueusd','cr07a_valorventaunidadusd',
    'cr07a_billingday','cr07a_fecharenovacion','_cr07a_clientelookup_value','_cr07a_producto_value']


def label(text):
    return {'LocalizedLabels': [{'Label': text, 'LanguageCode': 3082}]}


def add_component(identifier, kind):
    dv('POST', 'AddSolutionComponent', json={
        'ComponentId': identifier, 'ComponentType': kind, 'SolutionUniqueName': SOLUTION,
        'AddRequiredComponents': False, 'DoNotIncludeSubcomponents': True})


def forms(c):
    return [dict(r) for r in c.records.list('systemform',
        select=['formid', 'name', 'type', 'formxml', 'formactivationstate'],
        filter=f"objecttypecode eq '{TABLE}' and (type eq 2 or type eq 7) and formactivationstate eq 1")]


def views(c):
    return [dict(r) for r in c.records.list('savedquery',
        select=['savedqueryid', 'name', 'querytype', 'fetchxml', 'layoutxml'],
        filter=f"returnedtypecode eq '{TABLE}' and statecode eq 0 and querytype eq 0") if r.get('layoutxml')]


def plan():
    flow, _ = load_flow()
    save('flow-before.redacted.json', flow)
    definition = build(flow['properties']['definition'])
    save('flow-candidate.redacted.json', definition)
    save('flow-diff.json', differences(flow['properties']['definition'], definition))
    from scripts.boris_cost_centers.access import load_flow as load_boris
    boris, _ = load_boris()
    save('boris-protected-before.redacted.json', boris)
    c = client()
    before_forms, before_views = forms(c), views(c)
    save('forms-before.json', before_forms)
    save('views-before.json', before_views)
    columns = c.tables.list_columns(TABLE, select=['LogicalName','AttributeType'])
    exists = any(x['LogicalName'] == FIELD for x in columns)
    select = [PK] + PROTECTED_FIELDS + ([FIELD] if exists else [])
    rows = [dict(r) for r in c.records.list(TABLE, select=select)]
    save('products-before.json', rows)
    sh = siigo_headers(flow)
    centers = request('GET', 'https://api.siigo.com/v1/cost-centers', sh)
    for code, identifier in CENTERS.values():
        assert any(x['id'] == identifier and x['code'] == code and x['active'] for x in centers), code
    documents = request('GET', 'https://api.siigo.com/v1/document-types', sh, params={'type':'FV'})
    assert any(x['id'] == 7481 and str(x['code']) == '1' and x['active'] for x in documents)
    save('siigo-catalogs.json', {'cost_centers':centers,'documents':documents})
    print(json.dumps({'products':len(rows), 'column_exists':exists,
        'forms':[{k:f[k] for k in ['formid','name']} for f in before_forms],
        'views':[{k:v[k] for k in ['savedqueryid','name']} for v in before_views],
        'flow_state':flow['properties']['state']},ensure_ascii=True))


def schema():
    c = client()
    found = list(c.records.list('solution', select=['solutionid'], filter=f"uniquename eq '{SOLUTION}'"))
    if not found:
        publisher = list(c.records.list('publisher', select=['publisherid'], filter="customizationprefix eq 'cr07a'"))
        assert len(publisher) == 1
        c.records.create('solution', {'uniquename':SOLUTION, 'friendlyname':'Cloud - Facturacion TRM',
            'version':'1.0.0.0', 'publisherid@odata.bind':f"/publishers({publisher[0]['publisherid']})"})
    metadata = dv('GET', f"EntityDefinitions(LogicalName='{TABLE}')", params={'$select':'MetadataId'})
    add_component(metadata['MetadataId'], 1)
    cols = c.tables.list_columns(TABLE, select=['LogicalName','MetadataId','AttributeType'])
    match = next((x for x in cols if x['LogicalName'] == FIELD), None)
    if not match:
        body = {'@odata.type':'Microsoft.Dynamics.CRM.PicklistAttributeMetadata',
            'SchemaName':'cr07a_CentroCosto', 'DisplayName':label('Centro de costo'),
            'Description':label('Centro de costo de Ventas en Siigo para la facturacion Cloud.'),
            'RequiredLevel':{'Value':'ApplicationRequired'}, 'DefaultFormValue':10100,
            'OptionSet':{'IsGlobal':False, 'OptionSetType':'Picklist', 'Options':[
                {'Value':value, 'Label':label(code+' - Ventas')} for value,(code,_) in CENTERS.items()]}}
        dv('POST', f"EntityDefinitions(LogicalName='{TABLE}')/Attributes", json=body)
    meta = dv('GET', f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{FIELD}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata", params={'$expand':'OptionSet'})
    assert meta['DefaultFormValue'] == 10100
    assert {o['Value'] for o in meta['OptionSet']['Options']} == set(CENTERS)
    add_component(meta['MetadataId'], 2)
    # Views resolve the published column definition during validation.
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities><entity>{TABLE}</entity></entities></importexportxml>'})
    save('column.json', meta)
    print(json.dumps({'column':FIELD,'type':meta['AttributeType'],'default':10100,'solution':SOLUTION}))


def ui():
    c = client()
    updated_forms, updated_views = [], []
    for form in forms(c):
        root = ET.fromstring(form['formxml'])
        if not root.findall(f".//control[@datafieldname='{FIELD}']"):
            rows = root.find('.//tabs/tab/columns/column/sections/section/rows')
            if rows is None:
                raise RuntimeError('No main section on form '+form['formid'])
            row = ET.Element('row')
            cell = ET.SubElement(row,'cell',{'id':'{'+str(uuid.uuid4()).upper()+'}','showlabel':'true'})
            labels = ET.SubElement(cell,'labels')
            ET.SubElement(labels,'label',{'description':'Centro de costo','languagecode':'3082'})
            ET.SubElement(cell,'control',{'id':FIELD,'classid':'{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}',
                'datafieldname':FIELD,'disabled':'false'})
            rows.insert(1,row)
            c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')})
        add_component(form['formid'],60)
        updated_forms.append(form['formid'])
    for view in views(c):
        print(json.dumps({'updating_view':view['name']},ensure_ascii=True),flush=True)
        fetch, layout = ET.fromstring(view['fetchxml']), ET.fromstring(view['layoutxml'])
        entity, row = fetch.find('entity'), layout.find('row')
        if entity is None or row is None:
            raise RuntimeError('Unexpected view shape')
        if entity.find(f"attribute[@name='{FIELD}']") is None:
            ET.SubElement(entity,'attribute',{'name':FIELD})
        if row.find(f"cell[@name='{FIELD}']") is None:
            row.insert(1,ET.Element('cell',{'name':FIELD,'width':'170'}))
        c.records.update('savedquery',view['savedqueryid'],{
            'fetchxml':ET.tostring(fetch,encoding='unicode'),'layoutxml':ET.tostring(layout,encoding='unicode'),
            'querytype':view['querytype']})
        add_component(view['savedqueryid'],26)
        updated_views.append(view['savedqueryid'])
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities><entity>{TABLE}</entity></entities></importexportxml>'})
    save('ui-published.json',{'forms':updated_forms,'views':updated_views})
    print(json.dumps({'published_forms':len(updated_forms),'published_views':len(updated_views)}))


def initialize():
    c = client('dv-data')
    rows = [dict(r) for r in c.records.list(TABLE,select=[PK,FIELD])]
    # Initial run fills every existing row. Re-runs preserve the user's edits.
    missing = [r[PK] for r in rows if r.get(FIELD) is None]
    for i in range(0,len(missing),200):
        c.records.update(TABLE,missing[i:i+200],{FIELD:10100})
        print(json.dumps({'initialized':min(i+200,len(missing)),'total_missing':len(missing)}),flush=True)
    after = [dict(r) for r in c.records.list(TABLE,select=[PK,FIELD])]
    assert {r[PK] for r in after} == {r[PK] for r in rows}
    assert all(r.get(FIELD) in CENTERS for r in after)
    save('products-after.json',after)
    print(json.dumps({'rows_verified':len(after),'all_default':all(r[FIELD]==10100 for r in after)}))


def retire_catalog_ui():
    # Remove only the controls/columns added to the catalog in the earlier
    # change. Keep the underlying attribute and values for rollback.
    c = client()
    catalog = 'cr07a_precioscloud'
    catalog_forms = [dict(r) for r in c.records.list('systemform',select=['formid','name','formxml'],
        filter=f"objecttypecode eq '{catalog}' and type eq 2 and formactivationstate eq 1")]
    catalog_views = [dict(r) for r in c.records.list('savedquery',select=['savedqueryid','name','querytype','fetchxml','layoutxml'],
        filter=f"returnedtypecode eq '{catalog}' and querytype eq 0 and statecode eq 0")]
    save('catalog-ui-before-retirement.json',{'forms':catalog_forms,'views':catalog_views})
    for form in catalog_forms:
        root = ET.fromstring(form['formxml'])
        for rows in root.findall('.//rows'):
            for row in list(rows):
                if row.find(f".//control[@datafieldname='{FIELD}']") is not None:
                    assert len(row.findall('cell')) == 1 and len(row.findall('.//control')) == 1
                    rows.remove(row)
        assert root.find(f".//control[@datafieldname='{FIELD}']") is None
        c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')})
    for view in catalog_views:
        fetch, layout = ET.fromstring(view['fetchxml']), ET.fromstring(view['layoutxml'])
        for parent, tag, attr in [(fetch.find('entity'),'attribute','name'),(layout.find('row'),'cell','name')]:
            for node in list(parent):
                if node.tag == tag and node.attrib.get(attr) == FIELD: parent.remove(node)
        c.records.update('savedquery',view['savedqueryid'],{'fetchxml':ET.tostring(fetch,encoding='unicode'),
            'layoutxml':ET.tostring(layout,encoding='unicode'),'querytype':view['querytype']})
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities><entity>{catalog}</entity></entities></importexportxml>'})
    print(json.dumps({'catalog_forms_cleaned':len(catalog_forms),'catalog_views_cleaned':len(catalog_views),
        'catalog_column_and_data_preserved':True}))


def deploy():
    for case in ['all-sales-centers','invalid']:
        test = json.loads((OUT/('test-result-'+case+'.json')).read_text())
        assert test.get('passed') is True, 'Native expression validation has not passed'
    flow,h = load_flow()
    before = json.loads((OUT/'flow-before.redacted.json').read_text())
    if redact(flow['properties']['definition']) != before['properties']['definition']:
        raise RuntimeError('Source flow changed since plan; review a fresh plan before deploying')
    runs = request('GET',FLOW_URL+'/runs',h,params={'api-version':'2016-11-01','$top':5})
    if any(r['properties']['status'] in ('Running','Waiting') for r in runs.get('value',[])):
        raise RuntimeError('Production run is still active')
    definition = build(flow['properties']['definition'])
    p = flow['properties']
    payload = {'properties':{'displayName':p['displayName'],'definition':definition,
        'connectionReferences':p['connectionReferences'],'state':p['state']}}
    request('PATCH',FLOW_URL,h,params={'api-version':'2016-11-01'},json=payload)
    after,_ = load_flow()
    assert after['properties']['definition'] == definition
    assert after['properties']['state'] == p['state'] == 'Started'
    assert after['properties']['connectionReferences'] == p['connectionReferences']
    save('flow-after.redacted.json',after)
    dest = Path(__file__).resolve().parents[2]/'solutions'/SOLUTION/'FlowSource'
    dest.mkdir(parents=True,exist_ok=True)
    (dest/'Facturacion-con-TRM.redacted.json').write_text(json.dumps(redact(definition),ensure_ascii=True,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'state':after['properties']['state'],'last_modified':after['properties'].get('lastModifiedTime'),
        'document_id':7481,'sha256':hashlib.sha256(json.dumps(definition,sort_keys=True).encode()).hexdigest()}))


def verify():
    c = client('dv-query')
    rows = list(c.records.list(TABLE,select=[PK,FIELD]+PROTECTED_FIELDS,include_annotations='OData.Community.Display.V1.FormattedValue'))
    before = json.loads((OUT/'products-before.json').read_text())
    protected = lambda data:{r[PK]:{k:r.get(k) for k in PROTECTED_FIELDS} for r in data}
    old, current = protected(before), protected(rows)
    assert set(old)==set(current), 'Product rows changed'
    changed = {identifier:{k:(old[identifier][k],current[identifier][k]) for k in PROTECTED_FIELDS
        if old[identifier][k]!=current[identifier][k]} for identifier in old}
    changed = {identifier:fields for identifier,fields in changed.items() if fields}
    reviewed_path = OUT/'reviewed-concurrent-changes.json'
    reviewed = json.loads(reviewed_path.read_text()) if reviewed_path.exists() else []
    allowed = {r['id']:{k:(v['before'],v['after']) for k,v in r['fields'].items()} for r in reviewed}
    for identifier, fields in changed.items():
        assert fields==allowed.get(identifier), 'Unreviewed concurrent product change: '+identifier
        assert set(fields)=={'cr07a_valorventaunidadusd'}, 'Billing controls must not change'
    counts = {code:sum(r.get(FIELD)==value for r in rows) for value,(code,_) in CENTERS.items()}
    assert sum(counts.values()) == len(rows)
    f,v = forms(c),views(c)
    assert f and v
    assert all(ET.fromstring(x['formxml']).find(f".//control[@datafieldname='{FIELD}']") is not None for x in f)
    assert all(ET.fromstring(x['fetchxml']).find(f"entity/attribute[@name='{FIELD}']") is not None and
               ET.fromstring(x['layoutxml']).find(f"row/cell[@name='{FIELD}']") is not None for x in v)
    for form in c.records.list('systemform',select=['formxml'],
            filter="objecttypecode eq 'cr07a_precioscloud' and type eq 2 and formactivationstate eq 1"):
        assert ET.fromstring(form['formxml']).find(f".//control[@datafieldname='{FIELD}']") is None
    for view in c.records.list('savedquery',select=['layoutxml'],
            filter="returnedtypecode eq 'cr07a_precioscloud' and querytype eq 0 and statecode eq 0"):
        assert ET.fromstring(view['layoutxml']).find(f"row/cell[@name='{FIELD}']") is None
    flow,_ = load_flow()
    p = flow['properties']['definition']['actions']['Por_cliente']['actions']
    assert p['BodyFactura']['inputs']['document']['id'] == 7481
    assert p['BodyFactura']['inputs']['cost_center'] == "@outputs('CentroCostoSiigo')"
    expected = json.loads((OUT/'flow-candidate.redacted.json').read_text())
    assert redact(flow['properties']['definition']) == expected
    assert flow['properties']['state'] == 'Started'
    expanded = list(c.records.list('cr07a_salesperformancerecord',select=['cr07a_salesperformancerecordid'],
        expand=['cr07a_Producto($select=cr07a_siigo_code)'],filter='cr07a_facturableautomatico eq true'))
    missing_product = sum(not r.get('cr07a_Producto') for r in expanded)
    from scripts.boris_cost_centers.access import load_flow as load_boris
    boris, _ = load_boris()
    protected_boris = json.loads((OUT/'boris-protected-before.redacted.json').read_text())
    for key in ['definition','connectionReferences','state','lastModifiedTime']:
        assert redact(boris['properties'].get(key)) == protected_boris['properties'].get(key), 'Boris changed: '+key
    result = {'products':len(rows),'centers':counts,'forms':len(f),'views':len(v),
        'flow_state':flow['properties']['state'],'document_id':7481,
        'automatic_billing_rows':len(expanded),'billing_rows_without_product':missing_product,
        'table':TABLE,'field':FIELD,'example_label':rows[0].get(FIELD+'@OData.Community.Display.V1.FormattedValue'),
        'billing_controls_unchanged':True,'concurrent_price_changes_preserved':len(changed),
        'boris_flow_unchanged':True,
        'catalog_control_retired':True}
    save('verified.json',result)
    print(json.dumps(result,ensure_ascii=True))


def export():
    flow, _ = load_flow()
    auth = flow['properties']['definition']['actions']['HTTP_2']['inputs']['body']
    replacements = {}
    for key, secret in auth.items():
        if isinstance(secret,str) and secret:
            for encoded in [secret,json.dumps(secret)[1:-1],html.escape(secret),html.escape(secret,quote=False)]:
                replacements[encoded] = '__SIIGO_'+key.upper()+'__'
    # Redact in memory before any solution archive is written to disk.
    result = dv('POST','ExportSolution',json={'SolutionName':SOLUTION,'Managed':False})
    raw, clean = io.BytesIO(base64.b64decode(result['ExportSolutionFile'])), io.BytesIO()
    with zipfile.ZipFile(raw) as source, zipfile.ZipFile(clean,'w',zipfile.ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            try: text = data.decode('utf-8')
            except UnicodeDecodeError: pass
            else:
                for secret,marker in replacements.items(): text = text.replace(secret,marker)
                data = text.encode('utf-8')
            assert not any(secret.encode('utf-8') in data for secret in replacements)
            target.writestr(entry,data)
    archive = OUT/'CloudFacturacionTRM.sanitized.zip'
    archive.write_bytes(clean.getvalue())
    dest = Path(__file__).resolve().parents[2]/'solutions'/SOLUTION
    process = subprocess.run(['pac','solution','unpack','--zipfile',str(archive),'--folder',str(dest),
        '--packagetype','Unmanaged'],capture_output=True,text=True)
    if process.returncode: raise RuntimeError(process.stdout[-2000:]+process.stderr[-2000:])
    for file in dest.rglob('*'):
        if file.is_file(): assert not any(secret.encode('utf-8') in file.read_bytes() for secret in replacements)
    exported = json.loads((dest/'FlowSource'/'Facturacion-con-TRM.redacted.json').read_text())
    assert exported == redact(flow['properties']['definition'])
    print(json.dumps({'exported':SOLUTION,'files':sum(p.is_file() for p in dest.rglob('*')),
        'secrets_redacted':True,'flow_source_matches_production':True}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation',choices=['plan','schema','ui','initialize','retire_catalog_ui','deploy','verify','export'])
    operation = parser.parse_args().operation
    if operation in {'schema','ui','initialize','deploy','retire_catalog_ui'}:
        raise SystemExit('Superseded by scripts/shared_cost_centers. Legacy choice writes are disabled.')
    globals()[operation]()
