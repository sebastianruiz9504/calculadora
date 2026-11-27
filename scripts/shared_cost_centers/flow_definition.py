"""Replace local choices with the shared lookup, preserving invoice behavior."""
from copy import deepcopy
from scripts.cloud_cost_centers.flow_definition import differences
from scripts.boris_cost_centers.flow_definition import actions_by_name

FIELD='cr07a_centrocostoref'
VALUE='_'+FIELD+'_value'
NAV='cr07a_CentroCostoRef'
EXPAND=NAV+'($select=cr07a_codigo,cr07a_identificadorsiigo,statecode)'


def identifier(row):
    center=f"{row}?['{NAV}']"
    value=f"{center}?['cr07a_identificadorsiigo']"
    # Deliberately fail before producing a body if the relation is missing,
    # inactive, or its Siigo identifier is not positive. No legacy fallback.
    return f"@int(if(and(equals({center}?['statecode'],0),greater(coalesce({value},0),0)),{value},null))"


def build(source, kind):
    result=deepcopy(source);a=actions_by_name(result)
    if kind=='cloud':
        p=a['Enumerar_filas']['inputs']['parameters']
        entries=[x for x in p.get('$expand','').split(',') if not x.startswith(NAV+'(')]
        # This function is applied to the frozen pre-migration definition only.
        assert NAV not in p.get('$expand','')
        p['$expand']=','.join([*entries,EXPAND])
        a['Clientes_solo']['inputs']['select']['centroCosto']=f"@item()?['{VALUE}']"
        a['Filtrar_matriz2']['inputs']['where']=(
            "@and(equals(item()?['_cr07a_clientelookup_value'],items('Por_cliente')?['cliente']),"
            f"equals(item()?['{VALUE}'],items('Por_cliente')?['centroCosto']))")
        a['CentroCostoSiigo']['inputs']=identifier("first(body('Filtrar_matriz2'))")
        a['CentroCostoCodigo']['inputs']=f"@first(body('Filtrar_matriz2'))?['{NAV}']?['cr07a_codigo']"
        allowed={'/actions/Enumerar_filas/inputs/parameters/$expand',
            '/actions/Clientes_solo/inputs/select/centroCosto',
            '/actions/Por_cliente/actions/Filtrar_matriz2/inputs/where',
            '/actions/Por_cliente/actions/CentroCostoSiigo/inputs',
            '/actions/Por_cliente/actions/CentroCostoCodigo/inputs'}
    elif kind=='boris':
        p=a['Listar_contratos']['inputs']['parameters']
        selected=[x for x in p['$select'].split(',') if x!='cr07a_centrocosto']
        if VALUE not in selected:selected.append(VALUE)
        p['$select']=','.join(selected)
        assert NAV not in p.get('$expand','')
        p['$expand']=','.join(x for x in [p.get('$expand'),EXPAND] if x)
        a['CentroCostoSiigo']['inputs']=identifier("items('Por_contrato')")
        allowed={'/actions/Listar_contratos/inputs/parameters/$select','/actions/Listar_contratos/inputs/parameters/$expand'}
        changed=differences(source,result)
        center=[x['path'] for x in changed if x['path'].endswith('/actions/CentroCostoSiigo/inputs')]
        assert len(center)==1;allowed.update(center)
    else:raise ValueError(kind)
    changed=differences(source,result)
    assert {x['path'] for x in changed}==allowed, changed
    assert a['BodyFactura']['inputs']['document']['id']==7481
    assert a['BodyFactura']['inputs']['cost_center']=="@outputs('CentroCostoSiigo')"
    return result
