"""Run production expressions on synthetic records. No invoice connectors."""
import importlib.util
import json
import sys
import time
from copy import deepcopy
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT))
from scripts.shared_cost_centers.manage import save,read,CENTERS,access,OUT
from scripts.shared_cost_centers.flow_definition import build,FIELD,VALUE,NAV
from scripts.boris_cost_centers.flow_definition import actions_by_name


def fixture_rows(case):
    rows=[]
    values=[(choice,code,identifier) for choice,(code,identifier) in CENTERS.items()]
    values.append((10600,'106-00',7777))  # Proves the flow has no five-value map.
    for i,(choice,code,identifier) in enumerate(values):
        rows.append({'row':str(i),VALUE:'catalog-'+code,'cr07a_centrocosto':10500,
            NAV:{'cr07a_codigo':code,'cr07a_identificadorsiigo':identifier,'statecode':0},
            '_cr07a_clientelookup_value':'A','cr07a_contratosborisid':'fixture-'+str(i),
            'cr07a_name':'Validacion','cr07a_siigo_code':'VALIDACION','cr07a_costomensual':100,
            'cr07a_facturacionmensual':i!=4})
    if case=='valid':return rows
    rows=rows[:1]
    if case=='missing':rows[0][NAV]=None;rows[0][VALUE]=None
    elif case=='inactive':rows[0][NAV]['statecode']=1
    elif case=='invalid-id':rows[0][NAV]['cr07a_identificadorsiigo']=0
    else:raise ValueError(case)
    return rows


def cloud_fixture(candidate,case):
    a=actions_by_name(candidate);rows=fixture_rows(case)
    if case=='valid':
        rows.extend([dict(deepcopy(rows[0]),row='6'),dict(deepcopy(rows[0]),row='7',_cr07a_clientelookup_value='B')])
    per={n:deepcopy(a[n]) for n in ['Filtrar_matriz2','CentroCostoSiigo','CentroCostoCodigo']}
    per['Filtrar_matriz2']['runAfter']={}
    per['NumeroFactura']={'type':'Compose','inputs':7622,'runAfter':{'Filtrar_matriz2':['Succeeded']}}
    per['BodyFactura']={'type':'Compose','runAfter':deepcopy(a['BodyFactura']['runAfter']),
        'inputs':{k:deepcopy(a['BodyFactura']['inputs'][k]) for k in ['document','number','cost_center']}}
    per['Agregar_resultado']={'type':'AppendToArrayVariable','runAfter':{'BodyFactura':['Succeeded']},
        'inputs':{'name':'resultados','value':{'cliente':"@items('Por_cliente')?['cliente']",
            'codigo':"@outputs('CentroCostoCodigo')",'factura':"@outputs('BodyFactura')",'filas':"@body('Filtrar_matriz2')"}}}
    return {'$schema':candidate['$schema'],'contentVersion':'1.0.0.0','parameters':{},
        'triggers':{'manual':{'type':'Request','kind':'Button','inputs':{'schema':{'type':'object','properties':{}}}}},
        'actions':{
            'Enumerar_filas':{'type':'Compose','inputs':{'body':{'value':rows}},'runAfter':{}},
            'Clientes_solo':deepcopy(a['Clientes_solo']),'Clientes_unicos':deepcopy(a['Clientes_unicos']),
            'Inicializar_resultados':{'type':'InitializeVariable','runAfter':{'Clientes_unicos':['Succeeded']},
                'inputs':{'variables':[{'name':'resultados','type':'array','value':[]}]}},
            'Por_cliente':{'type':'Foreach','operationOptions':'Sequential','foreach':a['Por_cliente']['foreach'],
                'runAfter':{'Inicializar_resultados':['Succeeded']},'actions':per},
            'Resumen':{'type':'Compose','inputs':"@variables('resultados')",'runAfter':{'Por_cliente':['Succeeded']}}}}


def boris_fixture(candidate,case):
    # Reuse the already verified full-body/retry fixture, replacing only data
    # and its definition builder. The legacy builder itself is never invoked.
    folder=ROOT/'scripts'/'boris_cost_centers';sys.path.insert(0,str(folder))
    try:
        spec=importlib.util.spec_from_file_location('legacy_boris_validation',folder/'validate_platform.py')
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        module.build=lambda source:deepcopy(candidate)
        result=module.fixture(candidate)
    finally:sys.path.remove(str(folder))
    result['actions']['Por_contrato']['foreach']=fixture_rows(case)
    per=result['actions']['Por_contrato']['actions']
    per['Resultado']['inputs']['value']['code']=f"@items('Por_contrato')?['{NAV}']?['cr07a_codigo']"
    return result


def check_output(kind,output):
    mapping={code:identifier for code,identifier in CENTERS.values()};mapping['106-00']=7777
    if kind=='cloud':
        assert len(output)==7
        all_rows=[]
        for group in output:
            assert group['factura']['document']['id']==7481
            assert group['factura']['cost_center']==mapping[group['codigo']]
            assert all(x['_cr07a_clientelookup_value']==group['cliente'] and x[NAV]['cr07a_codigo']==group['codigo'] for x in group['filas'])
            all_rows.extend(x['row'] for x in group['filas'])
        assert sorted(all_rows)==[str(i) for i in range(8)]
    else:
        assert len(output)==6
        for item in output:
            body=item['body'];assert body['document']['id']==7481 and body['number']==7622
            assert body['cost_center']==mapping[item['code']]
            assert item['retryBody']=={**body,'number':7623}
            assert item['sameAllowed'] is True
            assert all(item[k] is False for k in ['oldDocumentAllowed','otherCenterAllowed','missingCenterAllowed'])
            assert body['stamp']['send'] is False and body['mail']['send'] is False
            assert body['payments'][0]['value']==(1428 if item['code']=='105-00' else 119)


def cleanup(c,identifier):
    for attempt in range(3):
        info=c.records.retrieve('workflow',identifier,select=['statecode'])
        if info is None:return
        if info['statecode']==1:c.records.update('workflow',identifier,{'statecode':0,'statuscode':1})
        try:
            c.records.delete('workflow',identifier)
            assert c.records.retrieve('workflow',identifier,select=['workflowid']) is None
            return
        except Exception:
            if attempt==2:raise
            time.sleep(3)


def exercise(kind,case):
    source=read(kind+'-flow-before')['properties']['definition'];candidate=build(source,kind)
    definition=(cloud_fixture if kind=='cloud' else boris_fixture)(candidate,case)
    allowed={'Compose','Select','Query','ParseJson','InitializeVariable','Foreach','AppendToArrayVariable','SetVariable'}
    assert all(a['type'] in allowed for a in actions_by_name(definition).values())
    save('test-definition-'+kind+'-'+case,definition)
    c=access.client('dv-data');h=access.headers('https://service.flow.microsoft.com/.default');identifier=None
    result={'kind':kind,'case':case}
    try:
        data={'properties':{'connectionReferences':{},'definition':definition},'schemaVersion':'1.0.0.0'}
        identifier=c.records.create('workflow',{'category':5,'type':1,'primaryentity':'none',
            'name':'VALIDACION TEMPORAL - Centros compartidos - '+kind+'-'+case,
            'description':'Synthetic expressions only; no invoice, HTTP or email actions.',
            'clientdata':json.dumps(data,separators=(',',':'))})
        save('temporary-'+kind+'-'+case,{'workflowid':identifier})
        c.records.update('workflow',identifier,{'statecode':1,'statuscode':2})
        info=c.records.retrieve('workflow',identifier,select=['workflowidunique'])
        url=f"{access.FLOW_BASE}/environments/{access.ENVIRONMENT}/flows/{info['workflowidunique']}"
        access.request('POST',url+'/triggers/manual/run',h,params={'api-version':'2016-11-01'},json={})
        deadline=time.monotonic()+50;run=None
        while time.monotonic()<deadline:
            runs=access.request('GET',url+'/runs',h,params={'api-version':'2016-11-01','$top':1})
            if runs.get('value'):
                run=runs['value'][0]
                if run['properties']['status'] not in ['Running','Waiting']:break
            time.sleep(3)
        assert run is not None
        result.update(status=run['properties']['status'],run=run['name'])
        actions=access.request('GET',url+'/runs/'+run['name']+'/actions',h,params={'api-version':'2016-11-01'})['value']
        if case=='valid':
            assert result['status']=='Succeeded', [(x['name'],x['properties'].get('error')) for x in actions if x['properties']['status']=='Failed']
            summary=next(x for x in actions if x['name']=='Resumen')
            result['output']=access.request('GET',summary['properties']['outputsLink']['uri'],{})
            check_output(kind,result['output'])
        else: assert result['status']=='Failed', 'Invalid center must not produce a payload'
        result['passed']=True;save('test-'+kind+'-'+case,result)
        print(json.dumps({k:v for k,v in result.items() if k!='output'}),flush=True)
    finally:
        if identifier:
            cleanup(c,identifier)
            save('temporary-'+kind+'-'+case,{'workflowid':identifier,'deleted':True})


if __name__=='__main__':
    for path in OUT.glob('temporary-*.json'):
        prior=json.loads(path.read_text())
        if not prior.get('deleted'):
            cleanup(access.client('dv-data'),prior['workflowid'])
            save(path.stem,{'workflowid':prior['workflowid'],'deleted':True})
    for kind in ['cloud','boris']:
        for case in ['valid','missing','inactive','invalid-id']:
            if (OUT/('test-'+kind+'-'+case+'.json')).exists() and read('test-'+kind+'-'+case).get('passed'):continue
            exercise(kind,case)
