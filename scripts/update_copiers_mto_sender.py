"""Change only the V2 report sender after Exchange SendAs has been verified."""
import argparse, copy, json, pathlib, subprocess, requests

ENV='Default-cab7ea42-4a21-4548-952f-fcde81f2bdd6'
FLOW='66e7671c-17d4-4f82-b0f3-59070c0a00b8'
BASE=f'https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{ENV}/flows/{FLOW}'
MAIN="first(body('Read_pending_row')?['value'])"
FROM="@"+MAIN+"?['dtc_technicianemailsnapshot']"
GUARD="not(empty("+MAIN+"?['dtc_technicianemailsnapshot'])),endsWith(toLower(coalesce("+MAIN+"?['dtc_technicianemailsnapshot'],'')),'@digitaltechcolombia.com')"
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
def snapshot(f):return {k:f['properties'][k] for k in ('displayName','state','definition','connectionReferences')}
def main():
    p=argparse.ArgumentParser();p.add_argument('--apply',action='store_true');p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
    proofs=json.loads((a.output/'sendas-verified.json').read_text(encoding='utf-8-sig'))
    assert {x['Mailbox'] for x in proofs if x['SendAsVerified']}=={'jromero@digitaltechcolombia.com','lrivera@digitaltechcolombia.com'}
    token=subprocess.check_output(['az.cmd','account','get-access-token','--resource','https://service.flow.microsoft.com/','--query','accessToken','-o','tsv'],text=True).strip()
    session=requests.Session();session.headers['Authorization']='Bearer '+token
    def get(suffix='?api-version=2016-11-01'):
        r=session.get(BASE+suffix,timeout=90);r.raise_for_status();return r.json()
    before=get();assert before['properties']['state']=='Started'
    assert before['properties']['connectionReferences']['shared_office365']['connectionName']=='shared-office365-087e1413-772f-4e38-8ee3-9b0eb77c0d31'
    candidate=copy.deepcopy(snapshot(before));actions=candidate['definition']['actions'];send=actions['Claim_only_pending_report']['actions']['Send_only_complete_report']
    params=send['actions']['Send_complete_report_once']['inputs']['parameters']
    assert params.get('emailMessage/From') in (None,FROM)
    assert params['emailMessage/Cc']=='Germanruiz@digitaltechcolombia.com;soportecopiers@digitaltechcolombia.com'
    select=actions['Read_pending_row']['inputs']['parameters']['$select'].split(',')
    if 'dtc_technicianemailsnapshot' not in select:select.append('dtc_technicianemailsnapshot')
    actions['Read_pending_row']['inputs']['parameters']['$select']=','.join(select)
    params['emailMessage/From']=FROM;params['emailMessage/ReplyTo']=FROM
    if GUARD not in send['expression']:send['expression']='@and('+GUARD+','+send['expression'][1:]+')'
    save(a.output/'sender-before.json',before);save(a.output/'sender-candidate.json',{'properties':candidate})
    if not a.apply:print('Sender candidate prepared.');return
    active=[r for r in get('/runs?api-version=2016-11-01&$top=100').get('value',[]) if r['properties']['status'] in ('Running','Waiting','Suspended')]
    if active:raise RuntimeError('V2 flow has active runs; wait before changing sender')
    fresh=get()
    if snapshot(fresh)!=snapshot(before) or fresh['properties']['lastModifiedTime']!=before['properties']['lastModifiedTime']:raise RuntimeError('Concurrent flow edit')
    r=session.patch(BASE+'?api-version=2016-11-01',json={'properties':candidate},timeout=90)
    r.raise_for_status()
    after=get();save(a.output/'sender-after.json',after)
    if snapshot(after)!=candidate:raise RuntimeError('Flow read-back does not match candidate; do not retry')
    print(json.dumps({'flow':FLOW,'state':after['properties']['state'],'sender':FROM,'readBackVerified':True,'historicalTriggerUnchanged':True}))
if __name__=='__main__':main()
