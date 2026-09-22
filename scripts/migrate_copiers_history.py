"""Approved legacy -> V2 migration; originals and the ten explicit exclusions are never written.
Default: inventory only. --schema prepares additive metadata; --apply imports and reads back.
SDK for data writes and upload; CLI for unsupported metadata; HTTP only for file downloads (SDK gap).
"""
import argparse
import hashlib
import json
import pathlib
import time
import uuid
import requests
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from auth import _credential, _operation_context, load_env
from PowerPlatform.Dataverse.client import DataverseClient
from PowerPlatform.Dataverse.core.config import OperationContext
import os

class CachedCredential:
    def __init__(self): self.inner=_credential(); self.cached=None; self.lock=threading.Lock()
    def get_token(self,*scopes,**kwargs):
        with self.lock:
            if self.cached is None or self.cached.expires_on-time.time()<300:
                self.cached=self.inner.get_token(*scopes,**kwargs)
            return self.cached

def migration_client(credential):
    return DataverseClient(base_url=os.environ["DATAVERSE_URL"],credential=credential,context=OperationContext(user_agent_context=_operation_context("dv-data")))
from auth import get_client, get_token, get_plugin_headers
from provision_copiers_mto_v2_capture_fields import api, label, SOLUTION, SOLUTION_ID

OLD='cr07a_mantenimiento'; NEW='dtc_copiersmtov2'; EVIDENCE='dtc_copiersmtoevidenciav2'
HISTORICAL=827270004; NO_MAIL=827270005; FILE_PURPOSE=827270004
EXCLUDED={
 'a3efde8c-bab2-f111-aaac-70a8a5a95cf5':'coincidence', 'ad7c71e0-bab2-f111-aaac-70a8a5a95cf5':'coincidence',
 'dd008208-bbb2-f111-aaac-70a8a5a95cf5':'coincidence', '51bca9e9-b9b2-f111-aaac-70a8a5a95cf5':'coincidence',
 '9967bf49-90b6-f111-aaae-70a8a5a95cf5':'coincidence', '32d90281-ca81-f011-b4cc-000d3ac071b1':'no_client',
 '953fbf24-b356-f011-bec2-000d3ac071b1':'no_client', 'c75bcb40-d6a2-f011-bbd2-002248dfe7af':'no_client',
 'd01a0db0-2df2-f011-8407-6045bd38ece5':'no_client', 'a9754ce7-dad9-f011-8543-6045bd38ece5':'no_client'}
FIELDS=['cr07a_mantenimientoid','cr07a_id','cr07a_mantenimiento1','createdon','modifiedon','statecode',
 '_cr07a_cliente_value','_cr07a_iddeequipo_value','_cr07a_numerodeserie_value','_cr07a_iddetecnico_value',
 '_cr07a_solicitudrelacionada_value','_ownerid_value','_createdby_value','cr07a_fechademantenimiento',
 'cr07a_tipodemantenimiento','cr07a_estadodelmantenimiento','cr07a_actadeentregadeservicio',
 'cr07a_actadeentregadeservicio_name','cr07a_descripciondelmantenimiento']

def clean(row): return {k:v for k,v in dict(row).items() if '@' not in k}
def canon(obj): return json.dumps(obj,ensure_ascii=False,sort_keys=True,separators=(',',':'))
def digest(data): return hashlib.sha256(data).hexdigest()
def identity(kind,source): return str(uuid.uuid5(uuid.NAMESPACE_URL,f'https://orgc79ca19c.crm2.dynamics.com/copiers-history/{kind}/{source}'))
def save(path,obj): path.write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8')

def schema():
    solution=api('GET',f"solutions({SOLUTION_ID})?%24select=uniquename&%24expand=publisherid(%24select=customizationprefix)")
    if solution['uniquename']!=SOLUTION or solution['publisherid']['customizationprefix']!='dtc': raise RuntimeError('Solution mismatch')
    specs=[('dtc_LegacySourceKey','String',36),('dtc_LegacyJson','Memo',100000),('dtc_LegacyRequestKey','String',36),('dtc_BusinessStatus','Integer',None)]
    current={x['LogicalName']:x for x in api('GET',f"EntityDefinitions(LogicalName='{NEW}')/Attributes?%24select=LogicalName,AttributeType").get('value',[])}
    for name,typ,length in specs:
        if name.lower() in current:
            if current[name.lower()]['AttributeType']!=typ: raise RuntimeError('Column mismatch '+name)
            continue
        body={'@odata.type':f'Microsoft.Dynamics.CRM.{typ}AttributeMetadata','SchemaName':name,'DisplayName':label(name),
              'RequiredLevel':{'Value':'None'},'IsAuditEnabled':{'Value':True}}
        if length: body['MaxLength']=length
        if typ=='Integer': body.update(MinValue=0,MaxValue=2147483647,Format='None')
        api('POST',f"EntityDefinitions(LogicalName='{NEW}')/Attributes",body)
    for table,col,value,text in [(NEW,'dtc_workflowstate',HISTORICAL,'Histórico importado'),(NEW,'dtc_emailstate',NO_MAIL,'No aplica: histórico'),(EVIDENCE,'dtc_purpose',FILE_PURPOSE,'Documento histórico original')]:
        path=f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{col}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?%24expand=OptionSet"
        existing=api('GET',path)['OptionSet']['Options']
        if not any(o['Value']==value for o in existing):
            api('POST','InsertOptionValue',{'EntityLogicalName':table,'AttributeLogicalName':col,'Value':value,'Label':label(text),'SolutionUniqueName':SOLUTION})
        verified=api('GET',path)['OptionSet']['Options']
        if not any(o['Value']==value for o in verified):raise RuntimeError('Choice read-back failed')
    path=f"EntityDefinitions(LogicalName='{EVIDENCE}')/Attributes(LogicalName='dtc_filecontent')"
    file=api('GET',path+'/Microsoft.Dynamics.CRM.FileAttributeMetadata')
    if file['MaxSizeInKB'] not in [12288,32768]:raise RuntimeError('Unexpected file limit')
    if file['MaxSizeInKB']!=32768:
        body={k:v for k,v in file.items() if k!='@odata.context'}
        body['@odata.type']='Microsoft.Dynamics.CRM.FileAttributeMetadata';body['MaxSizeInKB']=32768
        api('PUT',path,body)
    if api('GET',path+'/Microsoft.Dynamics.CRM.FileAttributeMetadata')['MaxSizeInKB']!=32768:raise RuntimeError('File limit read-back failed')
    api('POST','PublishXml',{'ParameterXml':f'<importexportxml><entities><entity>{NEW}</entity><entity>{EVIDENCE}</entity></entities></importexportxml>'})
    print('Additive schema verified.',flush=True)

def main():
    p=argparse.ArgumentParser();p.add_argument('--schema',action='store_true');p.add_argument('--apply',action='store_true')
    p.add_argument('--limit',type=int,default=0);p.add_argument('--output',type=pathlib.Path,required=True);args=p.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    if args.schema: schema()
    load_env();credential=CachedCredential();c=migration_client(credential)
    rows=[clean(x) for x in c.records.list(OLD,select=FIELDS)]
    users={x['systemuserid']:clean(x) for x in c.records.list('systemuser',select=['systemuserid','fullname','internalemailaddress'])}
    clients={x['cr07a_clienteid']:clean(x) for x in c.records.list('cr07a_cliente',select=['cr07a_clienteid','cr07a_nombre'])}
    equipment={x['cr07a_equipoid']:clean(x) for x in c.records.list('cr07a_equipo',select=['cr07a_equipoid','cr07a_nombredelequipo'])}
    if set(EXCLUDED)-{r['cr07a_mantenimientoid'] for r in rows}:raise RuntimeError('An excluded original is missing')
    candidates=[r for r in rows if r['cr07a_mantenimientoid'] not in EXCLUDED]
    for r in candidates:
        if r.get('_cr07a_cliente_value') not in clients:raise RuntimeError('Unresolved client '+r['cr07a_mantenimientoid'])
        if r.get('_ownerid_value') not in users:raise RuntimeError('Unresolved owner')
        if r['cr07a_tipodemantenimiento'] not in [645250000,645250001] or r['cr07a_estadodelmantenimiento'] not in [645250000,645250001]:raise RuntimeError('Unexpected business choice')
    snapshot={'capturedUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'excluded':EXCLUDED,'rows':rows}
    frozen=args.output/'source-snapshot.json'
    if frozen.exists():
        prior=json.loads(frozen.read_text(encoding='utf-8'))
        if canon(sorted(prior['rows'],key=lambda r:r['cr07a_mantenimientoid']))!=canon(sorted(rows,key=lambda r:r['cr07a_mantenimientoid'])):
            raise RuntimeError('Source changed since snapshot; reconcile explicitly before continuing')
    else:save(frozen,snapshot)
    print(json.dumps({'source':len(rows),'excluded':len(EXCLUDED),'candidate':len(candidates),'files':sum(bool(r.get('cr07a_actadeentregadeservicio')) for r in candidates),'apply':args.apply}),flush=True)
    if not args.apply:return
    local=threading.local()
    def download(table,record,column,path):
        if not hasattr(local,'session'):local.session=requests.Session()
        local.session.headers.update(get_plugin_headers('dv-data',credential.get_token('https://orgc79ca19c.crm2.dynamics.com/.default').token))
        url=f'https://orgc79ca19c.crm2.dynamics.com/api/data/v9.2/{table}({record})/{column}/$value'
        with local.session.get(url,stream=True,timeout=120) as response:
            response.raise_for_status()
            with path.open('wb') as f:
                for block in response.iter_content(1024*1024):f.write(block)
        return digest(path.read_bytes())
    verification=[]
    def migrate(r):
        if not hasattr(local,"client"):local.client=migration_client(credential)
        c=local.client
        source=r['cr07a_mantenimientoid']; target=identity('record',source);eid=identity('file',source)
        origin=canon(r);user=users[r['_ownerid_value']];eq=r.get('_cr07a_iddeequipo_value') or r.get('_cr07a_numerodeserie_value')
        existing=c.records.retrieve(NEW,target)
        if existing and (existing.get('dtc_legacysourcekey')!=source or existing.get('dtc_legacyjson')!=origin):raise RuntimeError('Existing destination mismatch '+source)
        payload={'dtc_copiersmtov2id':target,'dtc_legacysourcekey':source,'dtc_legacyjson':origin,
            'dtc_legacyrequestkey':r.get('_cr07a_solicitudrelacionada_value'),'dtc_businessstatus':r['cr07a_estadodelmantenimiento'],
            'dtc_operationkey':'legacy:'+source,'dtc_reference':'HIST-'+(r.get('cr07a_id') or source),
            'dtc_name':r['cr07a_mantenimiento1'],'dtc_title':r['cr07a_mantenimiento1'],'dtc_formversion':'copiers-legacy-v1',
            'dtc_workflowstate':827270000,'dtc_emailstate':NO_MAIL,'dtc_Client@odata.bind':f"/cr07a_clientes({r['_cr07a_cliente_value']})",
            'dtc_clientnamesnapshot':clients[r['_cr07a_cliente_value']]['cr07a_nombre'],
            'dtc_technicianuserkey':r['_ownerid_value'],'dtc_techniciannamesnapshot':user['fullname'],
            'dtc_technicianemailsnapshot':user.get('internalemailaddress') or '',
            'dtc_servicedate':r['cr07a_fechademantenimiento'][:10], 'dtc_workperformed':r['cr07a_descripciondelmantenimiento'],
            'dtc_maintenancetype':827270001 if r['cr07a_tipodemantenimiento']==645250001 else 827270000,
            'dtc_equipmentserialsnapshot':equipment.get(eq,{}).get('cr07a_nombredelequipo','Sin equipo vinculado'),
            'dtc_answersjson':'[]','dtc_attachmentcount':0}
        if eq:payload['dtc_Equipment@odata.bind']=f'/cr07a_equipos({eq})'
        if not existing:c.records.create(NEW,payload)
        filehash=None;filename=r.get('cr07a_actadeentregadeservicio_name')
        finish={'dtc_workflowstate':HISTORICAL,'dtc_emailstate':NO_MAIL}
        if r.get('cr07a_actadeentregadeservicio'):
            folder=args.output/'files'/source;folder.mkdir(parents=True,exist_ok=True)
            if not filename or pathlib.Path(filename).name!=filename or any(ch in filename for ch in ['/', '\\']):raise RuntimeError('Unsafe source filename')
            original=folder/filename
            filehash=download('cr07a_mantenimientos',source,'cr07a_actadeentregadeservicio',original)
            raw=original.read_bytes();size=len(raw)
            mime='application/pdf' if raw.startswith(b'%PDF-') else 'image/jpeg' if raw.startswith(b'\xff\xd8\xff') else None
            if mime is None or size>32*1024*1024:raise RuntimeError('Unsupported historical file')
            ekey=digest(('legacy-file:'+source).encode())
            ep={'dtc_copiersmtoevidenciav2id':eid,'dtc_name':'Histórico '+(r.get('cr07a_id') or source),'dtc_evidencekey':ekey,
                'dtc_SignedMto@odata.bind':f'/dtc_copiersmtov2s({target})','dtc_purpose':FILE_PURPOSE,'dtc_sequence':0,
                'dtc_originalfilename':filename,'dtc_contenttype':mime,'dtc_bytelength':size,'dtc_sha256':filehash,'dtc_securitystate':827270000}
            er=c.records.retrieve(EVIDENCE,eid)
            if er and (er.get('dtc_sha256')!=filehash or er.get('dtc_evidencekey')!=ekey):raise RuntimeError('Evidence collision')
            if not er:c.records.create(EVIDENCE,ep)
            if not er or not er.get('dtc_filecontent'):c.files.upload(EVIDENCE,eid,'dtc_filecontent',str(original),mime_type='application/octet-stream')
            readback=folder/'verified-download.bin'
            if download('dtc_copiersmtoevidenciav2s',eid,'dtc_filecontent',readback)!=filehash:raise RuntimeError('File read-back hash mismatch')
            readback.unlink()
            finish.update(dtc_reportevidencekey=ekey,dtc_reportsha256=filehash,dtc_reportfilename=filename)
        # Source and destination are independently read before making the migrated record visible.
        if canon(clean(c.records.retrieve(OLD,source,select=FIELDS)))!=origin:raise RuntimeError('Original changed during migration')
        current=c.records.retrieve(NEW,target)
        compare={k:v for k,v in payload.items() if not k.endswith('@odata.bind') and k not in ['dtc_workflowstate','dtc_emailstate','dtc_servicedate'] and v is not None}
        if any(current.get(k)!=v for k,v in compare.items()):raise RuntimeError('Destination data read-back mismatch '+source)
        if current.get('_dtc_client_value')!=r['_cr07a_cliente_value'] or current.get('_dtc_equipment_value')!=eq or (current.get('dtc_servicedate') or '')[:10]!=r['cr07a_fechademantenimiento'][:10]:raise RuntimeError('Relationship/date read-back mismatch')
        if current.get('dtc_workflowstate')!=HISTORICAL:c.records.update(NEW,target,finish)
        after=c.records.retrieve(NEW,target,select=['dtc_workflowstate','dtc_emailstate','dtc_legacysourcekey','dtc_reportsha256'])
        if after['dtc_workflowstate']!=HISTORICAL or after['dtc_emailstate']!=NO_MAIL or after['dtc_legacysourcekey']!=source or (filehash and after.get('dtc_reportsha256')!=filehash):raise RuntimeError('Publication read-back mismatch')
        result={'source':source,'target':target,'fileSha256':filehash,'verified':True}
        return result
    with ThreadPoolExecutor(max_workers=4) as pool:
        tasks=[pool.submit(migrate,r) for r in candidates[:args.limit or None]]
        for task in as_completed(tasks):
            result=task.result();verification.append(result)
            with (args.output/'verified.jsonl').open('a',encoding='utf-8') as log:log.write(canon(result)+'\n')
            if len(verification)%10==0 or len(verification)==1:print(json.dumps({'verified':len(verification),'of':len(candidates)}),flush=True)
    save(args.output/'run-result.json',{'verified':len(verification),'candidateCount':len(candidates),'excluded':EXCLUDED,'results':verification})
    print('Migration pass verified: '+str(len(verification)),flush=True)

if __name__=='__main__': main()
