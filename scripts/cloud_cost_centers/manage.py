"""Provision, initialize and verify the authorized Cloud cost-center change.

Never posts an invoice or starts the production TRM flow.
"""
import argparse
import json
import hashlib
import uuid
from pathlib import Path
import xml.etree.ElementTree as ET

from access import client, dv, load_flow, request, siigo_headers, save, OUT, FLOW_URL, redact
from flow_definition import FIELD, TABLE, PK, SOLUTION, CENTERS, build, differences


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
    c = client()
    before_forms, before_views = forms(c), views(c)
    save('forms-before.json', before_forms)
    save('views-before.json', before_views)
    columns = c.tables.list_columns(TABLE, select=['LogicalName','AttributeType'])
    exists = any(x['LogicalName'] == FIELD for x in columns)
    select = [PK] + ([FIELD] if exists else [])
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
    rows = list(c.records.list(TABLE,select=[PK,FIELD],include_annotations='OData.Community.Display.V1.FormattedValue'))
    counts = {code:sum(r.get(FIELD)==value for r in rows) for value,(code,_) in CENTERS.items()}
    assert sum(counts.values()) == len(rows)
    f,v = forms(c),views(c)
    assert f and v
    assert all(ET.fromstring(x['formxml']).find(f".//control[@datafieldname='{FIELD}']") is not None for x in f)
    assert all(ET.fromstring(x['fetchxml']).find(f"entity/attribute[@name='{FIELD}']") is not None and
               ET.fromstring(x['layoutxml']).find(f"row/cell[@name='{FIELD}']") is not None for x in v)
    flow,_ = load_flow()
    p = flow['properties']['definition']['actions']['Por_cliente']['actions']
    assert p['BodyFactura']['inputs']['document']['id'] == 7481
    assert p['BodyFactura']['inputs']['cost_center'] == "@outputs('CentroCostoSiigo')"
    expanded = list(c.records.list('cr07a_salesperformancerecord',select=['cr07a_salesperformancerecordid'],
        expand=[f'cr07a_Producto($select=cr07a_siigo_code,{FIELD})'],filter='cr07a_facturableautomatico eq true'))
    missing_product = sum(not r.get('cr07a_Producto') for r in expanded)
    result = {'products':len(rows),'centers':counts,'forms':len(f),'views':len(v),
        'flow_state':flow['properties']['state'],'document_id':7481,
        'automatic_billing_rows':len(expanded),'billing_rows_without_product':missing_product,
        'field':FIELD,'example_label':rows[0].get(FIELD+'@OData.Community.Display.V1.FormattedValue')}
    save('verified.json',result)
    print(json.dumps(result,ensure_ascii=True))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation',choices=['plan','schema','ui','initialize','deploy','verify'])
    globals()[parser.parse_args().operation]()
