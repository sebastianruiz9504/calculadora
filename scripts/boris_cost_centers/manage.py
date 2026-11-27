"""Authorized BORIS metadata/data/flow update. Never creates a real invoice."""
import argparse
import base64
import html
import io
import json
import subprocess
import uuid
import zipfile
from pathlib import Path
import xml.etree.ElementTree as ET

from access import client,dv,load_flow,request,siigo_headers,save,OUT,FLOW_URL,SOLUTION,redact,shared
from flow_definition import FIELD,TABLE,PK,CENTERS,build,actions_by_name


def label(text): return {'LocalizedLabels':[{'Label':text,'LanguageCode':3082}]}


def component(identifier,kind):
    dv('POST','AddSolutionComponent',json={'ComponentId':identifier,'ComponentType':kind,
        'SolutionUniqueName':SOLUTION,'AddRequiredComponents':False,'DoNotIncludeSubcomponents':True})


def publish():
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities><entity>{TABLE}</entity></entities></importexportxml>'})


def get_forms(c):
    return [dict(r) for r in c.records.list('systemform',select=['formid','name','formxml'],
        filter=f"objecttypecode eq '{TABLE}' and (type eq 2 or type eq 7) and formactivationstate eq 1")]


def get_views(c):
    return [dict(r) for r in c.records.list('savedquery',select=['savedqueryid','name','querytype','fetchxml','layoutxml'],
        filter=f"returnedtypecode eq '{TABLE}' and statecode eq 0 and querytype eq 0")]


def plan():
    flow,_=load_flow()
    assert flow['properties']['state']=='Started'
    assert flow['properties']['definition']['actions']['Configuracion']['inputs']['modo']=='Produccion'
    candidate=build(flow['properties']['definition'])
    save('flow-before.redacted.json',flow)
    save('flow-candidate.redacted.json',candidate)
    from scripts.cloud_cost_centers.flow_definition import differences
    save('flow-diff.json',differences(flow['properties']['definition'],candidate))
    h=siigo_headers(flow)
    catalog=request('GET','https://api.siigo.com/v1/cost-centers',h)
    for code,identifier in CENTERS.values():
        assert any(x['id']==identifier and x['code']==code and x['active'] for x in catalog)
    documents=request('GET','https://api.siigo.com/v1/document-types',h,params={'type':'FV'})
    assert any(x['id']==7481 and str(x['code'])=='1' and x['active'] for x in documents)
    save('catalogs.json',{'cost_centers':catalog,'documents':documents})
    c=client('dv-query')
    controls=[dict(r) for r in c.records.list('cr07a_boriscobro',select=['cr07a_boriscobroid','cr07a_estado','cr07a_siigofactura'])]
    pending=[x for x in controls if not x.get('cr07a_siigofactura')]
    print(json.dumps({'confirmed_controls':len(controls)-len(pending),'pending_controls':len(pending),
        'document_id':7481,'default_center':'103-00'}))


def schema():
    c=client()
    sol=c.records.list('solution',select=['solutionid','_publisherid_value'],filter=f"uniquename eq '{SOLUTION}'").first()
    assert sol is not None
    publisher=c.records.retrieve('publisher',sol['_publisherid_value'],select=['customizationprefix'])
    assert publisher['customizationprefix']=='cr07a'
    columns=c.tables.list_columns(TABLE,select=['LogicalName','MetadataId'])
    if not any(x['LogicalName']==FIELD for x in columns):
        dv('POST',f"EntityDefinitions(LogicalName='{TABLE}')/Attributes",json={
            '@odata.type':'Microsoft.Dynamics.CRM.PicklistAttributeMetadata','SchemaName':'cr07a_CentroCosto',
            'DisplayName':label('Centro de costo'),'Description':label('Centro de costo de Ventas en Siigo para la facturacion de este contrato Boris.'),
            'RequiredLevel':{'Value':'ApplicationRequired'},'DefaultFormValue':10300,
            'OptionSet':{'IsGlobal':False,'OptionSetType':'Picklist','Options':[
                {'Value':v,'Label':label(code+' - Ventas')} for v,(code,_) in CENTERS.items()]}})
    meta=dv('GET',f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{FIELD}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata",params={'$expand':'OptionSet'})
    assert meta['DefaultFormValue']==10300
    assert {x['Value'] for x in meta['OptionSet']['Options']}==set(CENTERS)
    component(meta['MetadataId'],2)
    save('column.json',meta)
    publish()
    print(json.dumps({'column':FIELD,'default':10300,'published':True}),flush=True)


def ui():
    c=client()
    forms,views=get_forms(c),get_views(c)
    for form in forms:
        root=ET.fromstring(form['formxml'])
        if root.find(f".//control[@datafieldname='{FIELD}']") is None:
            rows=root.find('.//tabs/tab/columns/column/sections/section/rows')
            assert rows is not None
            row=ET.Element('row')
            cell=ET.SubElement(row,'cell',{'id':'{'+str(uuid.uuid4()).upper()+'}','showlabel':'true'})
            labels=ET.SubElement(cell,'labels')
            ET.SubElement(labels,'label',{'description':'Centro de costo','languagecode':'3082'})
            ET.SubElement(cell,'control',{'id':FIELD,'classid':'{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}',
                'datafieldname':FIELD,'disabled':'false'})
            rows.insert(1,row)
            c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')})
        component(form['formid'],60)
    for view in views:
        f,l=ET.fromstring(view['fetchxml']),ET.fromstring(view['layoutxml'])
        if f.find(f"entity/attribute[@name='{FIELD}']") is None: ET.SubElement(f.find('entity'),'attribute',{'name':FIELD})
        if l.find(f"row/cell[@name='{FIELD}']") is None: l.find('row').insert(1,ET.Element('cell',{'name':FIELD,'width':'170'}))
        c.records.update('savedquery',view['savedqueryid'],{'fetchxml':ET.tostring(f,encoding='unicode'),
            'layoutxml':ET.tostring(l,encoding='unicode'),'querytype':view['querytype']})
        component(view['savedqueryid'],26)
    publish()
    save('ui-published.json',{'forms':[x['formid'] for x in forms],'views':[x['savedqueryid'] for x in views]})
    print(json.dumps({'forms':len(forms),'views':len(views),'published':True}),flush=True)


def initialize():
    c=client('dv-data')
    rows=[dict(x) for x in c.records.list(TABLE,select=[PK,FIELD])]
    missing=[x[PK] for x in rows if x.get(FIELD) is None]
    if missing: c.records.update(TABLE,missing,{FIELD:10300})
    after=[dict(x) for x in c.records.list(TABLE,select=[PK,FIELD])]
    assert {x[PK] for x in rows}=={x[PK] for x in after}
    assert all(x.get(FIELD) in CENTERS for x in after)
    save('contracts-centers-after.json',after)
    print(json.dumps({'initialized':len(missing),'rows':len(after),'all_10300':all(x[FIELD]==10300 for x in after)}))


def deploy():
    for case in ['valid','invalid']:
        assert json.loads((OUT/f'test-{case}.json').read_text())['passed']
    before=json.loads((OUT/'flow-before.redacted.json').read_text())
    flow,h=load_flow()
    assert redact(flow['properties']['definition'])==before['properties']['definition'],'Live source changed'
    runs=request('GET',FLOW_URL+'/runs',h,params={'api-version':'2016-11-01','$top':5})
    assert not any(x['properties']['status'] in ['Running','Waiting'] for x in runs['value'])
    p=flow['properties'];definition=build(p['definition'])
    request('PATCH',FLOW_URL,h,params={'api-version':'2016-11-01'},json={'properties':{
        'displayName':p['displayName'],'definition':definition,'connectionReferences':p['connectionReferences'],'state':p['state']}})
    live,_=load_flow()
    assert live['properties']['definition']==definition
    assert live['properties']['state']==p['state']=='Started'
    assert live['properties']['connectionReferences']==p['connectionReferences']
    save('flow-after.redacted.json',live)
    print(json.dumps({'state':'Started','modified':live['properties'].get('lastModifiedTime'),'document':7481}))


def verify():
    c=client('dv-query')
    rows=[dict(x) for x in c.records.list(TABLE,select=[PK,FIELD,'cr07a_name','statecode','cr07a_ultimoperiodofacturado','cr07a_siigo_invoice_id'])]
    before=json.loads((OUT/'contracts-before.json').read_text())
    old={x[PK]:{k:v for k,v in x.items() if k!='@odata.etag'} for x in before}
    now={x[PK]:{k:v for k,v in x.items() if k not in [FIELD,'@odata.etag']} for x in rows}
    assert old==now,'Billing markers changed'
    control_before=json.loads((OUT/'cr07a_boriscobro-before.json').read_text())
    keys=[k for k in control_before[0] if not k.startswith('@')]
    controls=[dict(x) for x in c.records.list('cr07a_boriscobro',select=keys)]
    canonical=lambda data:sorted([{k:v for k,v in x.items() if not k.startswith('@')} for x in data],key=lambda x:x['cr07a_boriscobroid'])
    assert canonical(controls)==canonical(control_before),'Billing controls changed'
    counts={code:sum(x.get(FIELD)==value for x in rows) for value,(code,_) in CENTERS.items()}
    assert sum(counts.values())==len(rows)
    forms,views=get_forms(c),get_views(c)
    assert all(ET.fromstring(x['formxml']).find(f".//control[@datafieldname='{FIELD}']") is not None for x in forms)
    assert all(ET.fromstring(x['layoutxml']).find(f"row/cell[@name='{FIELD}']") is not None for x in views)
    live,_=load_flow();a=actions_by_name(live['properties']['definition'])
    expected=json.loads((OUT/'flow-candidate.redacted.json').read_text())
    assert redact(live['properties']['definition'])==expected
    assert live['properties']['state']=='Started'
    trm,_=shared.load_flow()
    old_trm=json.loads((OUT/'trm-protected-before.redacted.json').read_text())
    for key in ['definition','connectionReferences','state','lastModifiedTime']:
        assert redact(trm['properties'].get(key))==old_trm['properties'].get(key),'TRM was changed: '+key
    result={'contracts':len(rows),'centers':counts,'forms':len(forms),'views':len(views),
        'field':FIELD,'document_id':a['BodyFactura']['inputs']['document']['id'],'flow_state':'Started',
        'mode':a['Configuracion']['inputs']['modo'],'billing_controls_unchanged':len(controls),'cloud_flow_unchanged':True}
    save('verified.json',result)
    print(json.dumps(result))


def export():
    flow,_=load_flow()
    auth=flow['properties']['definition']['actions']['Autenticar_Siigo']['inputs']['body']
    replacements={}
    for key,secret in auth.items():
        if isinstance(secret,str) and secret:
            for encoded in [secret,json.dumps(secret)[1:-1],html.escape(secret),html.escape(secret,quote=False)]:
                replacements[encoded]='__SIIGO_'+key.upper()+'__'
    result=dv('POST','ExportSolution',json={'SolutionName':SOLUTION,'Managed':False})
    raw=io.BytesIO(base64.b64decode(result['ExportSolutionFile']))
    clean=io.BytesIO()
    with zipfile.ZipFile(raw) as source,zipfile.ZipFile(clean,'w',zipfile.ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data=source.read(entry.filename)
            try:
                text=data.decode('utf-8')
            except UnicodeDecodeError: pass
            else:
                for secret,marker in replacements.items():text=text.replace(secret,marker)
                data=text.encode('utf-8')
            assert not any(s.encode('utf-8') in data for s in replacements),'Secret in export'
            target.writestr(entry,data)
    path=OUT/'BorisFacturacionCOP.sanitized.zip';path.write_bytes(clean.getvalue())
    dest=Path(__file__).resolve().parents[2]/'solutions'/SOLUTION
    result=subprocess.run(['pac','solution','unpack','--zipfile',str(path),'--folder',str(dest),'--packagetype','Unmanaged'],capture_output=True,text=True)
    if result.returncode: raise RuntimeError(result.stdout[-2000:]+result.stderr[-2000:])
    for file in dest.rglob('*'):
        if file.is_file():assert not any(s.encode('utf-8') in file.read_bytes() for s in replacements)
    flows=list((dest/'Workflows').glob('*.json'))
    assert len(flows)==1
    definition=json.loads(flows[0].read_text(encoding='utf-8-sig'))['properties']['definition']
    expected=redact(flow['properties']['definition'])
    # ExportSolution stores the description in the adjacent workflow XML.
    # Check it there, and compare every executable JSON field as exported.
    if 'description' not in definition:
        metadata=ET.parse(str(flows[0])+'.data.xml').getroot()
        assert metadata.attrib.get('Description')==expected.pop('description',None)
    assert redact(definition)==expected
    print(json.dumps({'exported':SOLUTION,'files':sum(p.is_file() for p in dest.rglob('*')),'secrets_redacted':True}))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('operation',choices=['plan','schema','ui','initialize','deploy','verify','export'])
    globals()[parser.parse_args().operation]()
