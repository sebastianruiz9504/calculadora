"""Minimal transformation of the live TRM flow, preserving unrelated behavior."""
from copy import deepcopy
import json

FIELD = 'cr07a_centrocosto'
TABLE = 'cr07a_precioscloud'
PK = TABLE + 'id'
SOLUTION = 'CloudFacturacionTRM'
CENTERS = {10100: ('101-00', 589), 10200: ('102-00', 602),
           10300: ('103-00', 611), 10400: ('104-00', 622), 10500: ('105-00', 633)}
PRODUCT_CHOICE = f"int(coalesce(item()?['cr07a_Producto']?['{FIELD}'],10100))"


def compose(value, after):
    return {'type': 'Compose', 'inputs': value, 'runAfter': {after: ['Succeeded']}}


def build(definition):
    result = deepcopy(definition)
    a = result['actions']
    p = a['Por_cliente']['actions']
    if p['BodyFactura']['inputs']['document']['id'] not in (31072, 7481):
        raise ValueError('Unexpected source invoice document')
    a['Enumerar_filas']['inputs']['parameters']['$expand'] = (
        f'cr07a_Producto($select=cr07a_siigo_code,{FIELD})')
    a['Clientes_solo']['inputs']['select']['centroCosto'] = '@' + PRODUCT_CHOICE
    p['Filtrar_matriz2']['inputs']['where'] = (
        "@and(equals(item()?['_cr07a_clientelookup_value'],items('Por_cliente')?['cliente']),"
        f"equals({PRODUCT_CHOICE},items('Por_cliente')?['centroCosto']))")
    mapping = json.dumps({str(k): v[1] for k, v in CENTERS.items()}, separators=(',', ':'))
    p['CentroCostoSiigo'] = compose(
        "@int(json('" + mapping + "')?[string(items('Por_cliente')?['centroCosto'])])", 'NumeroFactura')
    p['CentroCostoCodigo'] = compose(
        "@concat(substring(string(items('Por_cliente')?['centroCosto']),0,3),'-00')", 'CentroCostoSiigo')
    p['BodyFactura']['runAfter'] = {'CentroCostoCodigo': ['Succeeded']}
    p['BodyFactura']['inputs']['document']['id'] = 7481
    p['BodyFactura']['inputs']['cost_center'] = "@outputs('CentroCostoSiigo')"
    p['Filter_array']['inputs']['where'] = "@equals(item()?['id'],7481)"
    for name in ['Agregar_resumen_factura_emitida', 'Agregar_error_facturacion_siigo']:
        value = p[name]['inputs']['value']
        if "Centro de costo" not in value:
            p[name]['inputs']['value'] = value[:-1] + ", ' | Centro de costo ',outputs('CentroCostoCodigo'))"
    validate_shape(definition, result)
    return result


def differences(a, b, path=''):
    if isinstance(a, dict) and isinstance(b, dict):
        out = []
        for key in sorted(a.keys() | b.keys()):
            p = path + '/' + key
            out.extend(differences(a.get(key), b.get(key), p))
        return out
    return [] if a == b else [{'path': path, 'before': a, 'after': b}]


def validate_shape(before, after):
    allowed = {
        '/actions/Enumerar_filas/inputs/parameters/$expand',
        '/actions/Clientes_solo/inputs/select/centroCosto',
        '/actions/Por_cliente/actions/Filtrar_matriz2/inputs/where',
        '/actions/Por_cliente/actions/CentroCostoSiigo',
        '/actions/Por_cliente/actions/CentroCostoCodigo',
        '/actions/Por_cliente/actions/BodyFactura/runAfter/NumeroFactura',
        '/actions/Por_cliente/actions/BodyFactura/runAfter/CentroCostoCodigo',
        '/actions/Por_cliente/actions/BodyFactura/inputs/document/id',
        '/actions/Por_cliente/actions/BodyFactura/inputs/cost_center',
        '/actions/Por_cliente/actions/Filter_array/inputs/where',
        '/actions/Por_cliente/actions/Agregar_resumen_factura_emitida/inputs/value',
        '/actions/Por_cliente/actions/Agregar_error_facturacion_siigo/inputs/value',
    }
    changed = differences(before, after)
    unexpected = [d['path'] for d in changed if d['path'] not in allowed]
    if unexpected:
        raise ValueError('Unexpected changes: ' + str(unexpected))
    if after['actions']['Por_cliente'].get('operationOptions') != 'Sequential':
        raise ValueError('Invoice grouping must stay sequential')
    return changed
