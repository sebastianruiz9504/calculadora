"""Deploy only the reviewed lookup changes; production flows are never run."""
import json
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT))
from scripts.shared_cost_centers.manage import access,read,save,TABLES,FIELD,CATALOG,CATPK,CENTERS,forms,views
from scripts.shared_cost_centers.flow_definition import build,differences,EXPAND,NAV,VALUE

FLOWS={'cloud':'50a4b698-8c77-4c02-a309-d6d7acae2256','boris':'76ea371e-9a4f-4862-90cd-4df8fe3a606a'}


def url(kind):return f'{access.FLOW_BASE}/environments/{access.ENVIRONMENT}/flows/{FLOWS[kind]}'
def load(kind,h):return access.request('GET',url(kind),h,params={'api-version':'2016-11-01'})


def deploy():
    h=access.headers('https://service.flow.microsoft.com/.default')
    # Validate real expanded rows before a connector uses the same projection.
    c=access.client('dv-query')
    for table in TABLES:
        relationship=read(table+'-relationship');assert relationship['ReferencingEntityNavigationPropertyName']==NAV
        rows=[dict(r) for r in c.records.list(table,select=[table+'id',VALUE],expand=[EXPAND])]
        assert rows and all(r.get(VALUE) and r[NAV]['statecode']==0 and r[NAV]['cr07a_identificadorsiigo']>0 for r in rows)
        save(table+'-expanded-validation',{'count':len(rows),'all_expanded_active_ids_valid':True})
    for kind in FLOWS:
        for case in ['valid','missing','inactive','invalid-id']:
            assert read('test-'+kind+'-'+case)['passed'] and read('temporary-'+kind+'-'+case)['deleted']
        before=read(kind+'-flow-before')['properties'];live=load(kind,h);p=live['properties']
        if access.redact(p['definition'])==build(before['definition'],kind):
            assert p['state']=='Started';print(json.dumps({'already_deployed':kind}),flush=True);continue
        candidate=build(p['definition'],kind)
        assert access.redact(p['definition'])==before['definition'],'Flow changed since review'
        runs=access.request('GET',url(kind)+'/runs',h,params={'api-version':'2016-11-01','$top':5})
        assert not any(r['properties']['status'] in ['Running','Waiting'] for r in runs.get('value',[]))
        save(kind+'-flow-diff',differences(access.redact(p['definition']),access.redact(candidate)))
        access.request('PATCH',url(kind),h,params={'api-version':'2016-11-01'},json={'properties':{
            'displayName':p['displayName'],'definition':candidate,'connectionReferences':p['connectionReferences'],'state':p['state']}})
        after=load(kind,h);q=after['properties']
        assert q['definition']==candidate and q['state']==p['state']=='Started'
        assert q['connectionReferences']==p['connectionReferences']
        assert q['definition']['triggers']==p['definition']['triggers']
        save(kind+'-flow-after',after)
        if kind=='cloud':
            target=ROOT/'solutions'/'CloudFacturacionTRM'/'FlowSource'
            target.mkdir(exist_ok=True,parents=True)
            (target/'Facturacion-con-TRM.redacted.json').write_text(
                json.dumps(access.redact(candidate),ensure_ascii=True,indent=2)+'\n',encoding='utf-8')
        print(json.dumps({'deployed':kind,'state':q['state'],'document':7481,'modified':q['lastModifiedTime']}),flush=True)


if __name__=='__main__':deploy()
