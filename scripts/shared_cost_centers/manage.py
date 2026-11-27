"""Shared Siigo cost-center catalog. Tenant-scoped, resumable migration.

SDK CRUD is used wherever supported. Metadata ownership, publishing, app
components and conditional ETag writes require the scoped Dataverse Web API.
Never sends an invoice or changes existing business amounts.
"""
import argparse
import base64
import json
import re
import sys
import uuid
from pathlib import Path
import xml.etree.ElementTree as ET
import requests

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts.cloud_cost_centers import access

OUT = ROOT/'tmp'/'shared-cost-centers-20261127'
SOLUTION = 'CentrosCostoCompartidos'
CATALOG = 'cr07a_centrodecosto'
CATSET = 'cr07a_centrosdecosto'
CATPK = CATALOG+'id'
FIELD = 'cr07a_centrocostoref'
OLD = 'cr07a_centrocosto'
APP = 'c2c05c79-1385-f011-b4cc-000d3ac071b1'
TABLES = {'cr07a_salesperformancerecord':10100, 'cr07a_contratosboris':10300, 'cr07a_productoscopiers':10200}
CENTERS = {10100:('101-00',589),10200:('102-00',602),10300:('103-00',611),10400:('104-00',622),10500:('105-00',633)}
LIBRARY = 'cr07a_/sharedCostCenters.js'


def save(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/(name+'.json')).write_text(json.dumps(access.redact(value),ensure_ascii=True,indent=2)+'\n',encoding='utf-8')


def read(name): return json.loads((OUT/(name+'.json')).read_text(encoding='utf-8'))
def label(value): return {'LocalizedLabels':[{'Label':value,'LanguageCode':3082}]}


def dv(method, path, **kwargs):
    h=access.headers(access.ORG+'/.default');h['MSCRM.SolutionUniqueName']=SOLUTION
    return access.request(method,access.ORG+'/api/data/v9.2/'+path,h,**kwargs)


def component(identifier, kind, full=False):
    dv('POST','AddSolutionComponent',json={'ComponentId':identifier,'ComponentType':kind,
        'SolutionUniqueName':SOLUTION,'AddRequiredComponents':False,'DoNotIncludeSubcomponents':kind==1 and not full})


def publish(extra=''):
    entities=''.join(f'<entity>{t}</entity>' for t in [CATALOG,*TABLES])
    dv('POST','PublishXml',json={'ParameterXml':f'<importexportxml><entities>{entities}</entities>{extra}</importexportxml>'})


def forms(c, table):
    return [dict(r) for r in c.records.list('systemform',select=['formid','name','type','formxml'],
        filter=f"objecttypecode eq '{table}' and (type eq 2 or type eq 7) and formactivationstate eq 1")]


def views(c, table, all_types=False):
    return [dict(r) for r in c.records.list('savedquery',select=['savedqueryid','name','querytype','fetchxml','layoutxml','isdefault'],
        filter=f"returnedtypecode eq '{table}' and statecode eq 0"+('' if all_types else ' and querytype eq 0'))]


def schema():
    c=access.client()
    assert read('existing-center-tables')==[], 'Review the existing catalog before creating a duplicate'
    pub=list(c.records.list('publisher',select=['publisherid','customizationprefix'],filter="customizationprefix eq 'cr07a'"))
    assert len(pub)==1
    sol=list(c.records.list('solution',select=['solutionid','_publisherid_value'],filter=f"uniquename eq '{SOLUTION}'"))
    if not sol:
        c.records.create('solution',{'uniquename':SOLUTION,'friendlyname':'Centros de costo compartidos',
            'version':'1.0.0.0','publisherid@odata.bind':f"/publishers({pub[0]['publisherid']})"})
    else: assert sol[0]['_publisherid_value']==pub[0]['publisherid']
    if c.tables.get(CATALOG) is None:
        # OrganizationOwned, language, max length and numeric constraints are
        # not exposed by SDK tables.create; make one advanced metadata request.
        dv('POST','EntityDefinitions',json={
            '@odata.type':'Microsoft.Dynamics.CRM.EntityMetadata','SchemaName':'cr07a_CentroDeCosto',
            'EntitySetName':CATSET,'DisplayName':label('Centro de costo'),'DisplayCollectionName':label('Centros de costo'),
            'Description':label('Catalogo unico de centros de costo de Ventas de Siigo.'),
            'OwnershipType':'OrganizationOwned','HasActivities':False,'HasNotes':False,'IsActivity':False,
            'PrimaryNameAttribute':'cr07a_name','Attributes':[
                {'@odata.type':'Microsoft.Dynamics.CRM.StringAttributeMetadata','SchemaName':'cr07a_Name',
                 'DisplayName':label('Nombre'),'IsPrimaryName':True,'MaxLength':100,'RequiredLevel':{'Value':'ApplicationRequired'}},
                {'@odata.type':'Microsoft.Dynamics.CRM.StringAttributeMetadata','SchemaName':'cr07a_Codigo',
                 'DisplayName':label('Codigo'),'MaxLength':20,'RequiredLevel':{'Value':'ApplicationRequired'}},
                {'@odata.type':'Microsoft.Dynamics.CRM.IntegerAttributeMetadata','SchemaName':'cr07a_IdentificadorSiigo',
                 'DisplayName':label('ID en Siigo'),'MinValue':1,'MaxValue':2147483647,'Format':'None',
                 'RequiredLevel':{'Value':'ApplicationRequired'}}]})
    meta=dv('GET',f"EntityDefinitions(LogicalName='{CATALOG}')",params={'$select':'MetadataId,LogicalName,EntitySetName,OwnershipType,ObjectTypeCode'})
    assert meta['EntitySetName']==CATSET and meta['OwnershipType']=='OrganizationOwned'
    save('catalog-metadata',meta);component(meta['MetadataId'],1,True)
    keys=c.tables.get_alternate_keys(CATALOG)
    if not any(k.schema_name=='cr07a_CentroDeCostoCodigo' for k in keys):
        c.tables.create_alternate_key(CATALOG,'cr07a_CentroDeCostoCodigo',['cr07a_codigo'],display_name='Codigo de centro de costo',language_code=3082)
    current=[dict(r) for r in c.records.list(CATALOG)]
    verified=read('siigo-centers')
    for choice,(code,identifier) in CENTERS.items():
        assert any(x['code']==code and x['id']==identifier and x['active'] for x in verified)
        existing=[r for r in current if r['cr07a_codigo']==code]
        if not existing:
            c.records.create(CATALOG,{'cr07a_name':code+' - Ventas','cr07a_codigo':code,'cr07a_identificadorsiigo':identifier})
        else:
            assert len(existing)==1 and existing[0]['cr07a_identificadorsiigo']==identifier
    save('catalog-rows',[dict(r) for r in c.records.list(CATALOG)])
    for table in TABLES:
        columns=c.tables.list_columns(table,select=['LogicalName','MetadataId','AttributeType','IsValidForUpdate','IsLogical','AttributeOf','SourceType'])
        save(table+'-columns',columns)
        if not any(x['LogicalName']==FIELD for x in columns):
            c.tables.create_lookup_field(table,'cr07a_CentroCostoRef',CATALOG,display_name='Centro de costo',
                description='Centro de costo del catalogo compartido de Siigo.',required=True,
                cascade_delete='Restrict',solution=SOLUTION,language_code=3082)
        rel=dv('GET',f"EntityDefinitions(LogicalName='{table}')/ManyToOneRelationships",
            params={'$filter':f"ReferencingAttribute eq '{FIELD}'"})['value']
        assert len(rel)==1 and rel[0]['ReferencedEntity']==CATALOG and rel[0]['CascadeConfiguration']['Delete']=='Restrict'
        save(table+'-relationship',rel[0]);component(rel[0]['MetadataId'],10)
        field=dv('GET',f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{FIELD}')")
        component(field['MetadataId'],2)
        print(json.dumps({'table':table,'lookup':FIELD,'navigation':rel[0]['ReferencingEntityNavigationPropertyName']}),flush=True)
    publish();print('Shared catalog and lookup schema published',flush=True)


def migrate():
    c=access.client('dv-data')
    by_choice={v:next(r for r in read('catalog-rows') if r['cr07a_codigo']==code)[CATPK] for v,(code,_) in CENTERS.items()}
    for table in TABLES:
        pk=table+'id';before=[dict(r) for r in c.records.list(table)]
        # Keep the first migration snapshot for audit; fresh row versions are
        # used for every conditional write, so a concurrent edit is never lost.
        if not (OUT/(table+'-migration-before.json')).exists():save(table+'-migration-before',before)
        pending=[r for r in before if not r.get('_'+FIELD+'_value')]
        assert all(r.get(OLD) in CENTERS for r in before), 'Unmapped current choice needs review'
        rel=read(table+'-relationship');nav=rel['ReferencingEntityNavigationPropertyName']
        entity=dv('GET',f"EntityDefinitions(LogicalName='{table}')",params={'$select':'EntitySetName'})['EntitySetName']
        for start in range(0,len(pending),100):
            chunk=pending[start:start+100];boundary='batch_'+uuid.uuid4().hex;parts=[]
            for i,row in enumerate(chunk):
                change='changeset_'+uuid.uuid4().hex
                data=json.dumps({nav+'@odata.bind':f'/{CATSET}({by_choice[row[OLD]]})'})
                parts.extend([f'--{boundary}',f'Content-Type: multipart/mixed; boundary={change}','',f'--{change}',
                    'Content-Type: application/http','Content-Transfer-Encoding: binary',f'Content-ID: {i+1}','',
                    f'PATCH {access.ORG}/api/data/v9.2/{entity}({row[pk]}) HTTP/1.1',
                    'Content-Type: application/json',f'If-Match: W/"{row["versionnumber"]}"','',data,f'--{change}--',''])
            parts.extend([f'--{boundary}--',''])
            h=access.headers(access.ORG+'/.default','dv-data');h['Content-Type']='multipart/mixed; boundary='+boundary
            response=requests.post(access.ORG+'/api/data/v9.2/$batch',headers=h,data='\r\n'.join(parts).encode(),timeout=90)
            statuses=re.findall(r'HTTP/1\.1 (\d{3})',response.text)
            assert response.ok and statuses==['204']*len(chunk), f'Conditional migration statuses: {statuses}; reread before retry'
            print(json.dumps({'migrated':table,'count':start+len(chunk),'total':len(pending)}),flush=True)
        after=[dict(r) for r in c.records.list(table)]
        assert {r[pk] for r in before}=={r[pk] for r in after}
        assert all(r.get('_'+FIELD+'_value')==by_choice[r[OLD]] for r in after)
        columns=read(table+'-columns')
        protected=[('_'+x['LogicalName']+'_value' if x['AttributeType'] in ['Lookup','Customer','Owner'] else x['LogicalName'])
            for x in columns if x.get('IsValidForUpdate') and not x.get('IsLogical') and not x.get('AttributeOf') and x['LogicalName']!=FIELD]
        canonical=lambda data:{r[pk]:{k:r.get(k) for k in protected} for r in data}
        assert canonical(before)==canonical(after), 'Concurrent business change: inspect before continuing'
        save(table+'-migration-after',after)
        save(table+'-migration-result',{'rows':len(after),'migrated':len(pending),'choices_preserved':True,'business_fields_unchanged':True})


def ui():
    c=access.client();resource=list(c.records.list('webresource',select=['webresourceid'],filter=f"name eq '{LIBRARY}'"))
    data={'name':LIBRARY,'displayname':'Centros de costo - Valores iniciales','webresourcetype':3,
        'content':base64.b64encode((Path(__file__).parent/'sharedCostCenters.js').read_bytes()).decode()}
    if resource:
        resource_id=resource[0]['webresourceid'];c.records.update('webresource',resource_id,data)
    else:resource_id=c.records.create('webresource',data)
    component(resource_id,61)
    for table in TABLES:
        assert read(table+'-migration-result')['choices_preserved']
        for form in forms(c,table):
            root=ET.fromstring(form['formxml'])
            control=root.find(f".//control[@datafieldname='{OLD}']")
            if control is not None:
                control.set('id',FIELD);control.set('datafieldname',FIELD)
                control.set('classid','{270BD3DB-D9AF-4782-9025-509E298DEC0A}')
                for child in list(control):control.remove(child)
            assert root.find(f".//control[@datafieldname='{FIELD}']") is not None
            libraries=root.find('formLibraries')
            if libraries is None:libraries=ET.SubElement(root,'formLibraries')
            if not any(x.get('name')==LIBRARY for x in libraries):
                ET.SubElement(libraries,'Library',{'name':LIBRARY,'libraryUniqueId':'{'+str(uuid.uuid4())+'}'})
            events=root.find('events')
            if events is None:events=ET.SubElement(root,'events')
            event=events.find("event[@name='onload']")
            if event is None:event=ET.SubElement(events,'event',{'name':'onload','application':'false','active':'true'})
            handlers=event.find('Handlers')
            if handlers is None:handlers=ET.SubElement(event,'Handlers')
            if not any(x.get('libraryName')==LIBRARY for x in handlers):
                ET.SubElement(handlers,'Handler',{'functionName':'DigitalTech.SharedCostCenters.onLoad','libraryName':LIBRARY,
                    'handlerUniqueId':'{'+str(uuid.uuid4())+'}','enabled':'true','passExecutionContext':'true','parameters':''})
            c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')});component(form['formid'],60)
        for view in views(c,table):
            fetch,layout=ET.fromstring(view['fetchxml']),ET.fromstring(view['layoutxml'])
            for element in fetch.findall(f".//attribute[@name='{OLD}']"):element.set('name',FIELD)
            for element in layout.findall(f".//cell[@name='{OLD}']"):element.set('name',FIELD);element.set('width','200')
            assert fetch.find(f"entity/attribute[@name='{FIELD}']") is not None
            c.records.update('savedquery',view['savedqueryid'],{'fetchxml':ET.tostring(fetch,encoding='unicode'),
                'layoutxml':ET.tostring(layout,encoding='unicode'),'querytype':view['querytype']});component(view['savedqueryid'],26)
        # Preserve the old data as a rollback snapshot, but remove the former
        # mandatory choice constraint so the new lookup is the editable field.
        old=dv('GET',f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{OLD}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata")
        dv('PUT',f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{OLD}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
            json={'@odata.type':'Microsoft.Dynamics.CRM.PicklistAttributeMetadata','MetadataId':old['MetadataId'],
                'SchemaName':old['SchemaName'],'RequiredLevel':{'Value':'None'},
                'DisplayName':label('Centro de costo (anterior)'),'DefaultFormValue':-1})
        component(old['MetadataId'],2)
        print(json.dumps({'ui_lookup_updated':table}),flush=True)
    catalog_ui(c,resource_id)


def catalog_ui(c=None,resource_id=None):
    c=c or access.client()
    if resource_id is None:
        resource_id=c.records.list('webresource',select=['webresourceid'],filter=f"name eq '{LIBRARY}'").first()['webresourceid']
    for form in forms(c,CATALOG):
        root=ET.fromstring(form['formxml']);rows=root.find('.//rows')
        for field,name,classid in [('cr07a_codigo','Codigo','{4273EDBD-AC1D-40d3-9FB2-095C621B552D}'),
                ('cr07a_identificadorsiigo','ID en Siigo','{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}')]:
            if root.find(f".//control[@datafieldname='{field}']") is not None:continue
            row=ET.SubElement(rows,'row');cell=ET.SubElement(row,'cell',{'id':'{'+str(uuid.uuid4())+'}'})
            labels=ET.SubElement(cell,'labels');ET.SubElement(labels,'label',{'description':name,'languagecode':'3082'})
            ET.SubElement(cell,'control',{'id':field,'datafieldname':field,'classid':classid})
        c.records.update('systemform',form['formid'],{'formxml':ET.tostring(root,encoding='unicode')});component(form['formid'],60)
    for view in views(c,CATALOG,True):
        if view['querytype'] not in [0,64]:continue
        fetch,layout=ET.fromstring(view['fetchxml']),ET.fromstring(view['layoutxml']);entity,row=fetch.find('entity'),layout.find('row')
        for field,width in [('cr07a_codigo','120'),('cr07a_identificadorsiigo','120')]:
            if entity.find(f"attribute[@name='{field}']") is None:ET.SubElement(entity,'attribute',{'name':field})
            if row.find(f"cell[@name='{field}']") is None:ET.SubElement(row,'cell',{'name':field,'width':width})
        if ET.tostring(fetch,encoding='unicode')!=view['fetchxml'] or ET.tostring(layout,encoding='unicode')!=view['layoutxml']:
            c.records.update('savedquery',view['savedqueryid'],{'fetchxml':ET.tostring(fetch,encoding='unicode'),
                'layoutxml':ET.tostring(layout,encoding='unicode'),'querytype':view['querytype']})
        component(view['savedqueryid'],26)
    save('webresource',{'id':resource_id,'name':LIBRARY})
    publish(f'<webresources><webresource>{resource_id}</webresource></webresources>')


def app():
    c=access.client();before=dv('GET',f'RetrieveAppComponents(AppModuleId={APP})')['value']
    sitemap_ids=[x['objectid'] for x in before if x['componenttype']==62];assert len(sitemap_ids)==1
    sitemap=dict(c.records.retrieve('sitemap',sitemap_ids[0]))
    if not (OUT/'gerencia-sitemap-before.json').exists():save('gerencia-sitemap-before',sitemap)
    root=ET.fromstring(sitemap['sitemapxml'])
    if root.find(f".//SubArea[@Entity='{CATALOG}']") is None:
        group=next(g for g in root.findall('.//Group') if g.find("SubArea[@Entity='cr07a_salesperformancerecord']") is not None)
        sub=ET.SubElement(group,'SubArea',{'Id':'cr07a_shared_cost_centers','Entity':CATALOG,'Client':'All','AvailableOffline':'false'})
        titles=ET.SubElement(sub,'Titles');ET.SubElement(titles,'Title',{'LCID':'3082','Title':'Centros de costo'})
        c.records.update('sitemap',sitemap['sitemapid'],{'sitemapxml':ET.tostring(root,encoding='unicode')})
    components=[{'formid':f['formid'],'@odata.type':'Microsoft.Dynamics.CRM.systemform'} for f in forms(c,CATALOG)]
    components += [{'savedqueryid':v['savedqueryid'],'@odata.type':'Microsoft.Dynamics.CRM.savedquery'} for v in views(c,CATALOG)]
    # Ordinary entity components are resolved by their logical type. The
    # metadata `entity` type would add the internal Entity table instead.
    components.insert(0,{CATPK:read('catalog-rows')[0][CATPK],'@odata.type':'Microsoft.Dynamics.CRM.'+CATALOG})
    dv('POST','AddAppComponents',json={'AppId':APP,'Components':components})
    # Remove only the internal metadata-table component introduced by the
    # initial API probe. It was absent from the frozen original app baseline.
    metadata_table_id='9d0f025b-11ce-40f1-a7f4-a8088f4985aa'
    original=read('app-before')['components']['value']
    assert not any(x['objectid']==metadata_table_id for x in original)
    descriptor=json.loads(dv('GET',f'appmodules({APP})',params={'$select':'descriptor'})['descriptor'])
    if any(x['Id']==metadata_table_id for x in descriptor['appInfo']['Components']):
        dv('POST','RemoveAppComponents',json={'AppId':APP,'Components':[
            {'entityid':metadata_table_id,'@odata.type':'Microsoft.Dynamics.CRM.entity'}]})
    component(sitemap['sitemapid'],62);component(APP,80)
    publish(f'<sitemaps><sitemap>{sitemap["sitemapid"]}</sitemap></sitemaps><appmodules><appmodule>{APP}</appmodule></appmodules>')
    live=dv('GET',f'appmodules({APP})',params={'$select':'descriptor,componentstate'})
    assert live['componentstate']==0
    descriptor=json.loads(live['descriptor'])
    after=[{'componenttype':x['Type'],'objectid':x['Id']} for x in descriptor['appInfo']['Components']]
    assert any(x['componenttype']==1 and x['objectid']==read('catalog-metadata')['MetadataId'] for x in after)
    assert {(x['componenttype'],x['objectid']) for x in original}<={(x['componenttype'],x['objectid']) for x in after}
    assert not any(x['objectid']==metadata_table_id for x in after)
    validation=dv('GET',f'ValidateApp(AppModuleId={APP})');assert validation['AppValidationResponse']['ValidationSuccess']
    save('app-after',{'components':after,'validation':validation});print('Gerencia catalog navigation published',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('operation',choices=['schema','migrate','ui','catalog_ui','app'])
    globals()[parser.parse_args().operation]()
