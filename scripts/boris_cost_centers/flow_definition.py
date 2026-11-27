"""Targeted BORIS flow update; preserve billing periods and confirmed invoices."""
from copy import deepcopy
import json

FIELD='cr07a_centrocosto'
TABLE='cr07a_contratosboris'
PK=TABLE+'id'
CENTERS={10100:('101-00',589),10200:('102-00',602),10300:('103-00',611),10400:('104-00',622),10500:('105-00',633)}


def actions_by_name(definition):
    result={}
    def visit(actions):
        for name,action in actions.items():
            if name in result: raise ValueError('Duplicate action '+name)
            result[name]=action
            visit(action.get('actions',{}))
            visit(action.get('else',{}).get('actions',{}))
    visit(definition['actions'])
    return result


def rename_references(value):
    names={'Documento_FV2':'Documento_FV1','FV2_actual':'FV1_actual'}
    if isinstance(value,dict):
        return {names.get(k,k):rename_references(v) for k,v in value.items()}
    if isinstance(value,list): return [rename_references(v) for v in value]
    if isinstance(value,str) and value.startswith('@'):
        for old,new in names.items(): value=value.replace("('"+old+"')","('"+new+"')")
    return value


def build(definition):
    result=rename_references(deepcopy(definition))
    a=actions_by_name(result)
    selected=a['Listar_contratos']['inputs']['parameters']['$select'].split(',')
    if FIELD not in selected: selected.append(FIELD)
    a['Listar_contratos']['inputs']['parameters']['$select']=','.join(selected)
    for name in ['Documento_FV1','FV1_actual']:
        a[name]['inputs']['where']="@and(equals(item()?['id'],7481),equals(item()?['active'],true))"
    mapping=json.dumps({str(k):v[1] for k,v in CENTERS.items()},separators=(',',':'))
    target=a['Referencias_validas']['actions']
    target['CentroCostoSiigo']={'type':'Compose','runAfter':{'Total_COP':['Succeeded']},
        'inputs':"@int(json('"+mapping+"')?[string(coalesce(items('Por_contrato')?['"+FIELD+"'],10300))])"}
    target['BodyFactura']['runAfter']={'CentroCostoSiigo':['Succeeded']}
    body=target['BodyFactura']['inputs']
    assert body['document']['id'] in (31072,7481)
    body['document']['id']=7481
    body['cost_center']="@outputs('CentroCostoSiigo')"
    # Reusing an older prepared request must never silently change its meaning.
    guard=a['Payload_reintentable_hoy']['expression']
    if "?['cost_center']" not in guard:
        guard=guard[:-1]+",equals(variables('varPayload')?['document']?['id'],outputs('BodyFactura')?['document']?['id']),equals(variables('varPayload')?['cost_center'],outputs('BodyFactura')?['cost_center']))"
    a['Payload_reintentable_hoy']['expression']=guard
    a['Reintento_requiere_revision']['inputs']['value']=(
        "@concat('Revisar payload pendiente de ',outputs('Clave_idempotencia'),': cambio la fecha, el valor, el comprobante o el centro de costo. No se envio otra factura.')")
    # Assert that the requested change does not alter calendar, pricing,
    # idempotency keys, confirmations, HTTP delivery policy, or connections.
    old=actions_by_name(definition)
    new=actions_by_name(result)
    changed={name for name in old if name in new and old[name]!=new[name]}
    leaf_changes={name for name in changed if 'actions' not in old[name]}
    allowed={'Listar_contratos','BodyFactura','Nuevo_payload_consecutivo','Reintento_requiere_revision'}
    assert leaf_changes<=allowed,leaf_changes-allowed
    for name in ['Configuracion','Hoy_Bogota','Datos_contrato_validos','Corresponde_facturar','Periodo_pendiente',
                 'Clave_idempotencia','Actualizar_contrato_facturado','Ya_existe_factura_control',
                 'Crear_borrador_Siigo','Reintentar_colision_Siigo','Payload_guardado']:
        if 'actions' in old[name]:
            assert old[name].get('expression')==new[name].get('expression'),name
        else: assert old[name]==new[name],name
    assert definition['triggers']==result['triggers']
    return result
