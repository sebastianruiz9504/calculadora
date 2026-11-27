"""Validate native BORIS payload/retry expressions without creating invoices."""
from copy import deepcopy
import json
import time

from access import client,load_flow,headers,request,save,FLOW_BASE,ENVIRONMENT,FLOW_ID
from flow_definition import build,actions_by_name,CENTERS,FIELD


def fixture(source,invalid=False):
    d=build(source);a=actions_by_name(d)
    values=[10100,10200,10300,10400,10500,None] if not invalid else [99999]
    rows=[{FIELD:value,'cr07a_contratosborisid':f'fixture-{i}','cr07a_name':'Validacion',
        'cr07a_siigo_code':'VALIDACION','cr07a_costomensual':100,'cr07a_facturacionmensual':i!=4} for i,value in enumerate(values)]
    parse=lambda value:{'type':'ParseJson','inputs':{'content':value,'schema':{'type':'array' if isinstance(value,list) else 'object'}}}
    compose=lambda value:{'type':'Compose','inputs':value}
    setpayload=lambda value:{'type':'SetVariable','inputs':{'name':'varPayload','value':value}}
    per={
        'Obtener_cliente':parse({'cr07a_nit':'900000000','cr07a_nombre':'Validacion sintetica'}),
        'Periodo':compose('M2026-11'),'Clave_idempotencia':compose('VALIDACION-SIN-ENVIO'),
        'Subtotal_COP':deepcopy(a['Subtotal_COP']),'IVA_COP':deepcopy(a['IVA_COP']),'Total_COP':deepcopy(a['Total_COP']),
        'Consultar_documentos':parse([{'id':31072,'active':True,'consecutive':8328},{'id':7481,'active':True,'consecutive':7622}]),
        'Documento_FV1':deepcopy(a['Documento_FV1']),
        'CentroCostoSiigo':deepcopy(a['CentroCostoSiigo']),'BodyFactura':deepcopy(a['BodyFactura']),
        'Usar_solicitud_actual':setpayload("@outputs('BodyFactura')"),
        'Validar_solicitud_actual':compose(a['Payload_reintentable_hoy']['expression']),
        'Consultar_consecutivo_actual':parse([{'id':31072,'active':True,'consecutive':8329},{'id':7481,'active':True,'consecutive':7623}]),
        'FV1_actual':deepcopy(a['FV1_actual']),'Nuevo_payload_consecutivo':deepcopy(a['Nuevo_payload_consecutivo']),
        'Usar_comprobante_anterior':setpayload("@setProperty(outputs('BodyFactura'),'document',json('{\"id\":31072}'))"),
        'Validar_comprobante_anterior':compose(a['Payload_reintentable_hoy']['expression']),
        'Usar_centro_distinto':setpayload("@setProperty(outputs('BodyFactura'),'cost_center',-1)"),
        'Validar_centro_distinto':compose(a['Payload_reintentable_hoy']['expression']),
        'Usar_sin_centro':setpayload("@setProperty(outputs('BodyFactura'),'cost_center',null)"),
        'Validar_sin_centro':compose(a['Payload_reintentable_hoy']['expression']),
        'Resultado':{'type':'AppendToArrayVariable','inputs':{'name':'resultados','value':{
            'choice':f"@items('Por_contrato')?['{FIELD}']",'body':"@outputs('BodyFactura')",
            'retryBody':"@outputs('Nuevo_payload_consecutivo')",'sameAllowed':"@outputs('Validar_solicitud_actual')",
            'oldDocumentAllowed':"@outputs('Validar_comprobante_anterior')",'otherCenterAllowed':"@outputs('Validar_centro_distinto')",
            'missingCenterAllowed':"@outputs('Validar_sin_centro')"}}},
    }
    previous=None
    for name,action in per.items():
        action['runAfter']={previous:['Succeeded']} if previous else {}
        previous=name
    result={'$schema':d['$schema'],'contentVersion':'1.0.0.0','parameters':{},
        'triggers':{'manual':{'type':'Request','kind':'Button','inputs':{'schema':{'type':'object','properties':{}}}}},
        'actions':{
            'Hoy_Bogota':{'type':'Compose','inputs':'2026-11-27','runAfter':{}},
            'varPayload':{'type':'InitializeVariable','runAfter':{'Hoy_Bogota':['Succeeded']},
                'inputs':{'variables':[{'name':'varPayload','type':'object','value':{}}]}},
            'resultados':{'type':'InitializeVariable','runAfter':{'varPayload':['Succeeded']},
                'inputs':{'variables':[{'name':'resultados','type':'array','value':[]}]}},
            'Por_contrato':{'type':'Foreach','foreach':rows,'operationOptions':'Sequential',
                'runAfter':{'resultados':['Succeeded']},'actions':per},
            'Resumen':{'type':'Compose','inputs':"@variables('resultados')",'runAfter':{'Por_contrato':['Succeeded']}}
        }}
    allowed={'Compose','ParseJson','Query','Foreach','InitializeVariable','SetVariable','AppendToArrayVariable'}
    assert all(x['type'] in allowed for x in actions_by_name(result).values())
    return result


def exercise(source,invalid=False):
    definition=fixture(source,invalid);case='invalid' if invalid else 'valid'
    save('test-definition-'+case+'.json',definition)
    c=client('dv-data');h=headers('https://service.flow.microsoft.com/.default');identifier=None
    try:
        data={'properties':{'connectionReferences':{},'definition':definition},'schemaVersion':'1.0.0.0'}
        identifier=c.records.create('workflow',{'category':5,'type':1,'primaryentity':'none',
            'name':'VALIDACION TEMPORAL - Boris centros costo - '+case,
            'description':'Synthetic payloads only; no HTTP, connectors, invoice or email actions.',
            'clientdata':json.dumps(data,separators=(',',':'))})
        assert identifier!=FLOW_ID
        save('temporary-flow-'+case+'.json',{'workflowid':identifier})
        c.records.update('workflow',identifier,{'statecode':1,'statuscode':2})
        info=c.records.retrieve('workflow',identifier,select=['workflowidunique'])
        url=f"{FLOW_BASE}/environments/{ENVIRONMENT}/flows/{info['workflowidunique']}"
        request('POST',url+'/triggers/manual/run',h,params={'api-version':'2016-11-01'},json={})
        deadline=time.monotonic()+50;run=None
        while time.monotonic()<deadline:
            runs=request('GET',url+'/runs',h,params={'api-version':'2016-11-01','$top':1})
            if runs.get('value'):
                run=runs['value'][0]
                if run['properties']['status'] not in ['Running','Waiting']:break
            time.sleep(3)
        assert run is not None
        result={'case':case,'status':run['properties']['status'],'run':run['name']}
        actions=request('GET',url+'/runs/'+run['name']+'/actions',h,params={'api-version':'2016-11-01'})['value']
        if invalid:
            assert result['status']=='Failed','Invalid cost center produced a payload'
        else:
            if result['status']!='Succeeded':
                result['errors']=[{'name':x['name'],'error':x['properties'].get('error')} for x in actions if x['properties']['status'] in ['Failed','TimedOut']]
                save('test-'+case+'.json',result)
                raise RuntimeError(json.dumps(result))
            summary=next(x for x in actions if x['name']=='Resumen')
            output=request('GET',summary['properties']['outputsLink']['uri'],{})
            assert len(output)==6
            for item in output:
                choice=item['choice'] or 10300
                body,retry=item['body'],item['retryBody']
                assert body['document']['id']==7481 and body['number']==7622
                assert body['cost_center']==CENTERS[choice][1]
                assert retry=={**body,'number':7623}
                assert item['sameAllowed'] is True
                assert item['oldDocumentAllowed'] is False
                assert item['otherCenterAllowed'] is False
                assert item['missingCenterAllowed'] is False
                assert body['stamp']['send'] is False and body['mail']['send'] is False
                assert body['payments'][0]['value']==(1428 if choice==10500 else 119)
            result['cases']=output
        result['passed']=True;save('test-'+case+'.json',result)
        print(json.dumps({'case':case,'status':result['status'],'passed':True}),flush=True)
    finally:
        if identifier:
            assert identifier!=FLOW_ID
            info=c.records.retrieve('workflow',identifier,select=['statecode'])
            if info:
                if info['statecode']==1:c.records.update('workflow',identifier,{'statecode':0,'statuscode':1})
                c.records.delete('workflow',identifier)
            assert c.records.retrieve('workflow',identifier,select=['workflowid']) is None
            print(json.dumps({'temporary_flow_deleted':identifier}),flush=True)


if __name__=='__main__':
    flow,_=load_flow()
    exercise(flow['properties']['definition'])
    exercise(flow['properties']['definition'],True)
