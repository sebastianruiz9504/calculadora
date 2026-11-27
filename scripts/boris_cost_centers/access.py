"""Reuse tenant-scoped access, with separate BORIS endpoints and evidence."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from scripts.cloud_cost_centers import access as shared

ORG, ENVIRONMENT, FLOW_BASE = shared.ORG, shared.ENVIRONMENT, shared.FLOW_BASE
FLOW_ID = '76ea371e-9a4f-4862-90cd-4df8fe3a606a'
FLOW_URL = f'{FLOW_BASE}/environments/{ENVIRONMENT}/flows/{FLOW_ID}'
SOLUTION = 'BorisFacturacionCOP'
OUT = Path(__file__).resolve().parents[2] / 'tmp' / 'boris-cost-centers-20261127'
client, headers, request, redact = shared.client, shared.headers, shared.request, shared.redact


def save(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/name).write_text(json.dumps(redact(value),ensure_ascii=True,indent=2)+'\n',encoding='utf-8')


def load_flow():
    h=headers('https://service.flow.microsoft.com/.default')
    return request('GET',FLOW_URL,h,params={'api-version':'2016-11-01'}),h


def dv(method,path,**kwargs):
    h=headers(ORG+'/.default')
    h['MSCRM.SolutionUniqueName']=SOLUTION
    return request(method,ORG+'/api/data/v9.2/'+path,h,**kwargs)


def siigo_headers(flow):
    a=flow['properties']['definition']['actions']['Autenticar_Siigo']['inputs']
    assert a['uri']=='https://api.siigo.com/auth'
    partner=a['headers']['Partner-Id']
    token=request('POST',a['uri'],{'Partner-Id':partner},json=a['body'])
    return {'Authorization':'Bearer '+token['access_token'],'Partner-Id':partner}
