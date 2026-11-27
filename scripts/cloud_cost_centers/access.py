"""Tenant-scoped SDK and management API access; secrets stay in memory."""
import json
from pathlib import Path

import requests
from azure.identity import AzureCliCredential
from PowerPlatform.Dataverse.client import DataverseClient
from PowerPlatform.Dataverse.core.config import OperationContext

ORG = 'https://orgc79ca19c.crm2.dynamics.com'
TENANT = 'cab7ea42-4a21-4548-952f-fcde81f2bdd6'
ENVIRONMENT = 'Default-' + TENANT
FLOW_ID = '50a4b698-8c77-4c02-a309-d6d7acae2256'
FLOW_BASE = 'https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple'
FLOW_URL = f'{FLOW_BASE}/environments/{ENVIRONMENT}/flows/{FLOW_ID}'
OUT = Path(__file__).resolve().parents[2] / 'tmp' / 'cloud-cost-centers-20261127'


def credential():
    return AzureCliCredential(tenant_id=TENANT)


def client(skill='dv-metadata'):
    return DataverseClient(ORG, credential(), context=OperationContext(
        user_agent_context=f'app=dataverse-skills/1.16.0;skill={skill};agent=codex'))


def headers(scope, skill='dv-metadata'):
    return {'Authorization': 'Bearer ' + credential().get_token(scope).token,
            'Accept': 'application/json', 'Content-Type': 'application/json',
            'User-Agent': f'app=dataverse-skills/1.16.0;skill={skill};agent=codex'}


def request(method, url, auth, **kwargs):
    r = requests.request(method, url, headers=auth, timeout=90, **kwargs)
    if not r.ok:
        try:
            error = r.json().get('error', {})
            # Do not echo server validation messages: they may include a flow body.
            code = error.get('code', 'unknown')
        except (ValueError, AttributeError):
            code = 'unknown'
        raise RuntimeError(f'{method} {url.split("?")[0]}: HTTP {r.status_code}, {code}')
    return r.json() if r.content else None


def dv(method, path, **kwargs):
    # The SDK cannot express custom choice labels/default/required level, or
    # unbound PublishXml/AddSolutionComponent. The CLI profile belongs to a
    # different tenant; these operations use explicitly scoped Azure auth.
    h = headers(ORG + '/.default')
    h['MSCRM.SolutionUniqueName'] = 'CloudFacturacionTRM'
    return request(method, ORG + '/api/data/v9.2/' + path, h, **kwargs)


def load_flow():
    h = headers('https://service.flow.microsoft.com/.default')
    return request('GET', FLOW_URL, h, params={'api-version': '2016-11-01'}), h


def redact(value):
    if isinstance(value, dict):
        return {k: '[REDACTED]' if k.lower() in {
            'access_key', 'access_token', 'refresh_token', 'authorization',
            'password', 'client_secret', 'username', 'sig'
        } else redact(v) for k, v in value.items()}
    if isinstance(value, list):
        return [redact(v) for v in value]
    return value


def save(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_text(json.dumps(redact(value), ensure_ascii=True, indent=2) + '\n', encoding='utf-8')


def siigo_headers(flow):
    a = flow['properties']['definition']['actions']['HTTP_2']['inputs']
    if a['uri'] != 'https://api.siigo.com/auth':
        raise RuntimeError('Unexpected Siigo authentication endpoint')
    partner = a.get('headers', {}).get('Partner-Id', 'digitalTechFacturacion')
    result = request('POST', a['uri'], {'Partner-Id': partner}, json=a['body'])
    return {'Authorization': 'Bearer ' + result['access_token'], 'Partner-Id': partner}
