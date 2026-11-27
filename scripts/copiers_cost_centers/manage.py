"""Centro de costo for the actual Productos COPIERS table in Gerencia.

Metadata and record updates only; no invoice or automation operations.
"""
import argparse
import json
import subprocess
import sys
import uuid
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT))
from scripts.cloud_cost_centers import access

TABLE = 'cr07a_productoscopiers'
PK = TABLE+'id'
FIELD = 'cr07a_centrocosto'
SOLUTION = 'CopiersCentrosCosto'
OUT = ROOT/'tmp'/'copiers-cost-centers-20261127'
OPTIONS = {value:f'{value//100}-00 - Ventas' for value in [10100,10200,10300,10400,10500]}


def save(name,value):
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/(name+'.json')).write_text(json.dumps(access.redact(value),indent=2)+'\n',encoding='utf-8')


def dv(method,path,**kwargs):
    # Choice defaults/labels and unbound publishing actions need the metadata
    # API. Reuse the explicit Digital Tech tenant, not another CLI profile.
    headers = access.headers(access.ORG+'/.default')
    headers['MSCRM.SolutionUniqueName'] = SOLUTION
    return access.request(method,access.ORG+'/api/data/v9.2/'+path,headers,**kwargs)


def forms(c):
    return [dict(x) for x in c.records.list('systemform',select=['formid','name','type','formxml'],
        filter=f"objecttypecode eq '{TABLE}' and (type eq 2 or type eq 7) and formactivationstate eq 1")]


def views(c):
    return [dict(x) for x in c.records.list('savedquery',select=['savedqueryid','name','querytype','fetchxml','layoutxml'],
        filter=f"returnedtypecode eq '{TABLE}' and querytype eq 0 and statecode eq 0")]


def component(identifier,kind):
    dv('POST','AddSolutionComponent',json={'ComponentId':identifier,'ComponentType':kind,
        'SolutionUniqueName':SOLUTION,'AddRequiredComponents':False,'DoNotIncludeSubcomponents':True})


def publish():
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities><entity>{TABLE}</entity></entities></importexportxml>'})


def plan():
    c = access.client()
    save('metadata',dv('GET',f"EntityDefinitions(LogicalName='{TABLE}')",
        params={'$select':'MetadataId,LogicalName,EntitySetName,PrimaryIdAttribute,DisplayName'}))
    save('columns',c.tables.list_columns(TABLE,select=['LogicalName','AttributeType','IsValidForUpdate','IsLogical','AttributeOf','SourceType']))
    save('forms-before',forms(c));save('views-before',views(c))
    rows = [dict(x) for x in c.records.list(TABLE)]
    save('products-before',rows)
    print(json.dumps({'products':len(rows),'active':sum(r['statecode']==0 for r in rows)}))


def schema():
    c = access.client()
    assert (OUT/'products-before.json').exists(), 'Read and back up the table first'
    publisher = list(c.records.list('publisher',select=['publisherid','customizationprefix'],filter="customizationprefix eq 'cr07a'"))
    assert len(publisher)==1
    solutions = list(c.records.list('solution',select=['solutionid','_publisherid_value'],filter=f"uniquename eq '{SOLUTION}'"))
    if solutions:
        assert solutions[0]['_publisherid_value']==publisher[0]['publisherid']
    else:
        c.records.create('solution',{'uniquename':SOLUTION,'friendlyname':'Copiers - Centros de costo',
            'version':'1.0.0.0','publisherid@odata.bind':f"/publishers({publisher[0]['publisherid']})"})
    meta = json.loads((OUT/'metadata.json').read_text())
    assert meta['LogicalName']==TABLE and meta['PrimaryIdAttribute']==PK
    component(meta['MetadataId'],1)
    if not any(x['LogicalName']==FIELD for x in c.tables.list_columns(TABLE,select=['LogicalName'])):
        label = lambda text:{'LocalizedLabels':[{'Label':text,'LanguageCode':3082}]}
        dv('POST',f"EntityDefinitions(LogicalName='{TABLE}')/Attributes",json={
            '@odata.type':'Microsoft.Dynamics.CRM.PicklistAttributeMetadata','SchemaName':'cr07a_CentroCosto',
            'DisplayName':label('Centro de costo'),'Description':label('Centro de costo de Ventas del producto Copiers.'),
            'RequiredLevel':{'Value':'ApplicationRequired'},'DefaultFormValue':10200,
            'OptionSet':{'IsGlobal':False,'OptionSetType':'Picklist','Options':[
                {'Value':value,'Label':label(text)} for value,text in OPTIONS.items()]}})
    column = dv('GET',f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{FIELD}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
        params={'$expand':'OptionSet'})
    assert column['DefaultFormValue']==10200
    assert {o['Value'] for o in column['OptionSet']['Options']}==set(OPTIONS)
    component(column['MetadataId'],2);save('column',column);publish()
    print(json.dumps({'column':FIELD,'default':10200,'published':True}),flush=True)


def ui():
    c = access.client();f,v = forms(c),views(c)
    assert f and v
    for form in f:
        root = ET.fromstring(form['formxml'])
        if root.find(f".//control[@datafieldname='{FIELD}']") is None:
            rows = next((r for r in root.findall('.//rows') if r.find(".//control[@datafieldname='cr07a_cliente']") is not None),None)
            assert rows is not None
            index = next(i+1 for i,row in enumerate(rows) if row.find(".//control[@datafieldname='cr07a_cliente']") is not None)
            row = ET.Element('row')
            cell = ET.SubElement(row,'cell',{'id':'{'+str(uuid.uuid4()).upper()+'}','showlabel':'true'})
            labels = ET.SubElement(cell,'labels')
            ET.SubElement(labels,'label',{'description':'Centro de costo','languagecode':'3082'})
            ET.SubElement(cell,'control',{'id':FIELD,'classid':'{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}',
                'datafieldname':FIELD,'disabled':'false'})
            rows.insert(index,row)
            c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')})
        component(form['formid'],60)
    for view in v:
        fetch,layout = ET.fromstring(view['fetchxml']),ET.fromstring(view['layoutxml'])
        entity,row = fetch.find('entity'),layout.find('row')
        if entity.find(f"attribute[@name='{FIELD}']") is None: ET.SubElement(entity,'attribute',{'name':FIELD})
        if row.find(f"cell[@name='{FIELD}']") is None:
            index = next((i+1 for i,cell in enumerate(row) if cell.attrib.get('name')=='cr07a_cliente'),1)
            row.insert(index,ET.Element('cell',{'name':FIELD,'width':'170'}))
        c.records.update('savedquery',view['savedqueryid'],{'fetchxml':ET.tostring(fetch,encoding='unicode'),
            'layoutxml':ET.tostring(layout,encoding='unicode'),'querytype':view['querytype']})
        component(view['savedqueryid'],26)
    publish()
    save('ui-published',{'forms':[x['formid'] for x in f],'views':[x['savedqueryid'] for x in v]})
    print(json.dumps({'forms':len(f),'views':len(v),'published':True}),flush=True)


def initialize():
    c = access.client('dv-data')
    before = list(c.records.list(TABLE,select=[PK,FIELD]))
    missing = [x[PK] for x in before if x.get(FIELD) is None]
    for i in range(0,len(missing),200): c.records.update(TABLE,missing[i:i+200],{FIELD:10200})
    after = [dict(x) for x in c.records.list(TABLE,select=[PK,FIELD])]
    assert {x[PK] for x in before}=={x[PK] for x in after}
    assert all(x.get(FIELD) in OPTIONS for x in after)
    save('products-centers-after',after)
    print(json.dumps({'initialized':len(missing),'products':len(after),'all_10200':all(x[FIELD]==10200 for x in after)}))


def verify():
    c = access.client('dv-query')
    columns = json.loads((OUT/'columns.json').read_text())
    protected = [('_'+x['LogicalName']+'_value' if x['AttributeType'] in ['Lookup','Customer','Owner'] else x['LogicalName'])
        for x in columns if x.get('IsValidForUpdate') and not x.get('IsLogical') and not x.get('AttributeOf') and x['LogicalName']!=FIELD]
    before = json.loads((OUT/'products-before.json').read_text())
    rows = [dict(x) for x in c.records.list(TABLE)]
    canonical = lambda data:{x[PK]:{k:x.get(k) for k in protected} for x in data}
    assert canonical(rows)==canonical(before), 'Existing product fields changed; inspect concurrent edits before proceeding'
    f,v = forms(c),views(c)
    assert all(ET.fromstring(x['formxml']).find(f".//control[@datafieldname='{FIELD}']") is not None for x in f)
    assert all(ET.fromstring(x['fetchxml']).find(f"entity/attribute[@name='{FIELD}']") is not None and
        ET.fromstring(x['layoutxml']).find(f"row/cell[@name='{FIELD}']") is not None for x in v)
    counts = {label:sum(x.get(FIELD)==value for x in rows) for value,label in OPTIONS.items()}
    assert sum(counts.values())==len(rows)
    result = {'table':TABLE,'field':FIELD,'products':len(rows),'centers':counts,'forms':len(f),'views':len(v),
        'default':10200,'existing_product_fields_unchanged':True,'automation_changes':False}
    save('verified',result);print(json.dumps(result))


def export():
    c = access.client('dv-solution')
    solution = c.records.list('solution',select=['solutionid'],filter=f"uniquename eq '{SOLUTION}'").first()
    components = list(c.records.list('solutioncomponent',select=['componenttype'],filter=f"_solutionid_value eq {solution['solutionid']}"))
    assert components and all(x['componenttype'] in [1,2,26,60] for x in components)
    archive = OUT/(SOLUTION+'.zip');dest = ROOT/'solutions'/SOLUTION
    commands = [ ['pac','solution','export','--name',SOLUTION,'--path',str(archive),'--managed','false','--overwrite','--environment',access.ORG],
        ['pac','solution','unpack','--zipfile',str(archive),'--folder',str(dest),'--packagetype','Unmanaged'] ]
    for command in commands:
        result = subprocess.run(command,capture_output=True,text=True)
        if result.returncode: raise RuntimeError(result.stdout[-2000:]+result.stderr[-2000:])
    assert not list(dest.rglob('Workflows'))
    print(json.dumps({'exported':SOLUTION,'files':sum(p.is_file() for p in dest.rglob('*')),'metadata_only':True}))


if __name__=='__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation',choices=['plan','schema','ui','initialize','verify','export'])
    globals()[parser.parse_args().operation]()
