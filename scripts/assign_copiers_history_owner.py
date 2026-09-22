"""Assign ONLY the verified imported copies to the existing, minimally privileged V2 worker."""
import argparse,json,pathlib
from migrate_copiers_history import CachedCredential,migration_client,load_env,identity,NEW,EVIDENCE,EXCLUDED

WORKER='6b07e603-7ca2-f111-aaad-70a8a5a95cf5'
APPLICATION='ebe37e5d-c246-4310-a7aa-a6e6686bc90e'
def main():
 p=argparse.ArgumentParser();p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
 results=json.loads((a.output/'run-result.json').read_text(encoding='utf-8'))['results']
 assert len(results)==747 and not ({r['source'] for r in results}&set(EXCLUDED))
 load_env();c=migration_client(CachedCredential())
 worker=c.records.retrieve('systemuser',WORKER,select=['fullname','applicationid','isdisabled'])
 assert worker['applicationid']==APPLICATION and not worker['isdisabled']
 scopes=[(EVIDENCE,'dtc_copiersmtoevidenciav2id',[identity('file',r['source']) for r in results if r['fileSha256']], 'dtc_purpose eq 827270004'),
         (NEW,'dtc_copiersmtov2id',[r['target'] for r in results],"dtc_formversion eq 'copiers-legacy-v1'")]
 report=[]
 for table,primary,ids,filter in scopes:
  rows=list(c.records.list(table,select=[primary,'_ownerid_value'],filter=filter))
  assert set(ids)=={r[primary] for r in rows}
  pending=[r[primary] for r in rows if r['_ownerid_value']!=WORKER]
  for offset in range(0,len(pending),50):
   c.records.update(table,pending[offset:offset+50],{'ownerid@odata.bind':f'/systemusers({WORKER})'})
   print(json.dumps({'table':table,'assigned':min(offset+50,len(pending)),'of':len(pending)}),flush=True)
  after=list(c.records.list(table,select=[primary,'_ownerid_value'],filter=filter))
  assert set(ids)=={r[primary] for r in after} and all(r['_ownerid_value']==WORKER for r in after)
  report.append({'table':table,'records':len(after),'workerOwnerVerified':True})
 (a.output/'ownership-verified.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
if __name__=='__main__':main()
