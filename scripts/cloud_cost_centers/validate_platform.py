"""Run the real Power Automate expressions against synthetic data only.

The temporary flow contains no connectors, HTTP calls, email, or invoice writes.
It is deleted after verification, including when a test fails.
"""
from copy import deepcopy
import json
import time

from access import client, load_flow, headers, request, save, FLOW_BASE, ENVIRONMENT, FLOW_ID
from flow_definition import build, FIELD, CENTERS


def fixture_definition(source, invalid=False):
    d = build(source)
    a = d['actions']
    p = a['Por_cliente']['actions']
    values = [10100, 10100, None, 10200, 10300, 10400, 10500, 10100]
    rows = [{'row':str(i+1),'_cr07a_clientelookup_value':'B' if i==7 else 'A',
             'cr07a_Producto':{FIELD:value}} for i,value in enumerate(values)]
    if invalid:
        rows = [{'row':'invalid','_cr07a_clientelookup_value':'A','cr07a_Producto':{FIELD:99999}}]
    select = deepcopy(a['Clientes_solo'])
    unique = deepcopy(a['Clientes_unicos'])
    filtered = deepcopy(p['Filtrar_matriz2'])
    filtered['runAfter'] = {}
    actions = {
        'Enumerar_filas':{'type':'Compose','inputs':{'body':{'value':rows}},'runAfter':{}},
        'Clientes_solo':select,
        'Clientes_unicos':unique,
        'Inicializar_resultados':{'type':'InitializeVariable','runAfter':{'Clientes_unicos':['Succeeded']},
            'inputs':{'variables':[{'name':'resultados','type':'array','value':[]}]}},
        'Por_cliente':{'type':'Foreach','operationOptions':'Sequential',
            'foreach':a['Por_cliente']['foreach'],'runAfter':{'Inicializar_resultados':['Succeeded']},
            'actions':{
                'Filtrar_matriz2':filtered,
                'NumeroFactura':{'type':'Compose','inputs':7622,'runAfter':{'Filtrar_matriz2':['Succeeded']}},
                'CentroCostoSiigo':deepcopy(p['CentroCostoSiigo']),
                'CentroCostoCodigo':deepcopy(p['CentroCostoCodigo']),
                'BodyFactura':{'type':'Compose','runAfter':deepcopy(p['BodyFactura']['runAfter']),
                    'inputs':{key:deepcopy(p['BodyFactura']['inputs'][key]) for key in ['document','number','cost_center']}},
                'Agregar_resultado':{'type':'AppendToArrayVariable','runAfter':{'BodyFactura':['Succeeded']},
                    'inputs':{'name':'resultados','value':{
                        'cliente':"@items('Por_cliente')?['cliente']",
                        'codigo':"@outputs('CentroCostoCodigo')",'factura':"@outputs('BodyFactura')",
                        'filas':"@body('Filtrar_matriz2')"}}}}},
        'Resumen':{'type':'Compose','inputs':"@variables('resultados')",'runAfter':{'Por_cliente':['Succeeded']}},
    }
    result = {'$schema':d['$schema'],'contentVersion':'1.0.0.0','parameters':{},
        'triggers':{'manual':{'type':'Request','kind':'Button','inputs':{'schema':{'type':'object','properties':{},'required':[]}}}},
        'actions':actions}
    def check(items):
        for item in items.values():
            assert item['type'] in {'Compose','Select','Query','InitializeVariable','Foreach','AppendToArrayVariable'}
            if item.get('actions'): check(item['actions'])
    check(actions)
    return result


def exercise(source, invalid=False):
    definition = fixture_definition(source, invalid)
    suffix = 'invalid' if invalid else 'all-sales-centers'
    save('test-definition-'+suffix+'.json',definition)
    c = client('dv-data')
    h = headers('https://service.flow.microsoft.com/.default')
    identifier = None
    try:
        data = {'properties':{'connectionReferences':{},'definition':definition},'schemaVersion':'1.0.0.0'}
        identifier = c.records.create('workflow',{'category':5,'type':1,'primaryentity':'none',
            'name':'VALIDACION TEMPORAL - Cloud centros costo - '+suffix,
            'description':'Synthetic expressions only; no invoice or email actions. Delete after test.',
            'clientdata':json.dumps(data,ensure_ascii=True,separators=(',',':'))})
        assert identifier != FLOW_ID
        save('temporary-flow-'+suffix+'.json',{'workflowid':identifier})
        c.records.update('workflow',identifier,{'statecode':1,'statuscode':2})
        info = c.records.retrieve('workflow',identifier,select=['workflowidunique'])
        flow_id = info['workflowidunique']
        url = f'{FLOW_BASE}/environments/{ENVIRONMENT}/flows/{flow_id}'
        request('POST',url+'/triggers/manual/run',h,params={'api-version':'2016-11-01'},json={})
        deadline = time.monotonic()+50
        run = None
        while time.monotonic()<deadline:
            data = request('GET',url+'/runs',h,params={'api-version':'2016-11-01','$top':1})
            if data.get('value'):
                run = data['value'][0]
                if run['properties']['status'] not in ('Running','Waiting'):
                    break
            time.sleep(3)
        assert run is not None, 'No test run found'
        status = run['properties']['status']
        actions = request('GET',url+'/runs/'+run['name']+'/actions',h,params={'api-version':'2016-11-01'})
        failed = [{'name':x['name'],'error':x['properties'].get('error')} for x in actions.get('value',[])
                  if x['properties']['status'] in ('Failed','TimedOut')]
        result = {'case':suffix,'status':status,'failed_actions':failed,'workflowid':identifier,'run':run['name']}
        if invalid:
            assert status == 'Failed', 'Invalid center must fail before producing a payload'
        else:
            if status != 'Succeeded':
                save('test-result-'+suffix+'.json',result)
                raise RuntimeError(json.dumps(result,ensure_ascii=True))
            summary = next(x for x in actions['value'] if x['name']=='Resumen')
            output = request('GET',summary['properties']['outputsLink']['uri'],{})
            result['groups'] = output
            assert len(output)==6
            rows = []
            mapping = {code:identifier for code,identifier in CENTERS.values()}
            for group in output:
                assert group['factura']['document']['id']==7481
                assert group['factura']['cost_center']==mapping[group['codigo']]
                assert all(x['_cr07a_clientelookup_value']==group['cliente'] for x in group['filas'])
                rows.extend(x['row'] for x in group['filas'])
            assert sorted(rows)==[str(i) for i in range(1,9)], 'Every line must appear exactly once'
            assert sorted(x['row'] for x in next(g for g in output if g['cliente']=='A' and g['codigo']=='101-00')['filas'])==['1','2','3']
        result['passed'] = True
        save('test-result-'+suffix+'.json',result)
        print(json.dumps({'case':suffix,'status':status,'passed':True}),flush=True)
    finally:
        if identifier:
            assert identifier != FLOW_ID
            current = c.records.retrieve('workflow',identifier,select=['statecode'])
            if current:
                if current['statecode']==1:
                    c.records.update('workflow',identifier,{'statecode':0,'statuscode':1})
                c.records.delete('workflow',identifier)
            assert c.records.retrieve('workflow',identifier,select=['workflowid']) is None
            print(json.dumps({'temporary_flow_deleted':identifier}),flush=True)


if __name__=='__main__':
    flow,_ = load_flow()
    exercise(flow['properties']['definition'])
    exercise(flow['properties']['definition'],invalid=True)
