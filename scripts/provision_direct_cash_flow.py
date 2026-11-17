"""Additive direct cash-flow schema. Dry-run unless --apply; existing CRM solution only."""
import argparse
import json
import os
from pathlib import Path
from auth import get_client, load_env
from ensure_bank_balance_schema import MetadataApi, column_payload, label

SOLUTION = 'CotizadorInternoCRM'
TABLES = {
    'cr07a_ClasificacionFlujoDirecto': {
        'display': 'Clasificacion de flujo de caja directo',
        'columns': [('Origen','Origen','String',30),('Fecha','Fecha','DateOnly',0),('Concepto','Concepto','String',40),('Estado','Estado','String',30),('Motivo','Motivo de sugerencia','String',500)]},
    'cr07a_MovimientoManualBanco': {
        'display': 'Registro bancario manual',
        'columns': [('Fecha','Fecha','DateOnly',0),('Banco','Cuenta bancaria','String',30),('Entrada','Ingreso','Decimal',2),('Salida','Egreso','Decimal',2),('Descripcion','Descripcion','String',500),('Referencia','Referencia bancaria','String',200),('Concepto','Concepto','String',40),('Origen','Origen','String',30),('EstadoSiigo','Estado Siigo','String',30),('LoteExtracto','Lote de extracto futuro','String',100)]},
    'cr07a_SaldoFlujoDirecto': {
        'display': 'Saldos de flujo de caja directo',
        'columns': [('Periodo','Periodo','String',7),('Inicial','Saldo inicial consolidado','Decimal',2),('Cloud','Saldo cierre Cloud','Decimal',2),('Copiers','Saldo cierre Copiers','Decimal',2),('Bolsillos','Saldo cierre Bolsillos','Decimal',2),('Fidurenta','Saldo cierre Fiduciaria','Decimal',2),('Extractos','Control de extractos bancarios','Memo',1048576)]}
}

def main():
    args=argparse.ArgumentParser()
    args.add_argument('--apply',action='store_true')
    options=args.parse_args()
    load_env()
    if os.environ['DATAVERSE_URL'].rstrip('/') != 'https://orgc79ca19c.crm2.dynamics.com':
        raise RuntimeError('Wrong environment: expected verified calculadoradt production database')
    api=MetadataApi()
    with get_client('dv-metadata') as client:
        solutions=list(client.records.list('solution',select=['uniquename','_publisherid_value'],filter="uniquename eq 'CotizadorInternoCRM'"))
        if len(solutions)!=1: raise RuntimeError('Expected existing CRM solution')
        publisher=client.records.retrieve('publisher',solutions[0]['_publisherid_value'])
        if publisher['customizationprefix']!='cr07a': raise RuntimeError('Unexpected existing publisher prefix')
        result=[]
        for schema,definition in TABLES.items():
            logical=schema.lower()
            existing=api.get("EntityDefinitions?$select=LogicalName,EntitySetName&$filter=LogicalName%20eq%20'"+logical+"'")['value']
            print(json.dumps({'table':logical,'action':'reuse' if existing else 'create','apply':options.apply}),flush=True)
            if not options.apply: continue
            if not existing:
                client.tables.create(schema,{},solution=SOLUTION,primary_column='cr07a_Name',display_name=definition['display'])
            attrs=api.get("EntityDefinitions(LogicalName='"+logical+"')/Attributes?$select=LogicalName")['value']
            names={a['LogicalName'] for a in attrs}
            for suffix,display,kind,size in definition['columns']:
                column='cr07a_'+suffix
                if column.lower() in names: continue
                if kind=='DateOnly':
                    payload={'@odata.type':'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata','SchemaName':column,'DisplayName':label(display),'RequiredLevel':{'Value':'None'},'Format':'DateOnly','DateTimeBehavior':{'Value':'DateOnly'}}
                elif kind=='Memo':
                    payload={'@odata.type':'Microsoft.Dynamics.CRM.MemoAttributeMetadata','SchemaName':column,'DisplayName':label(display),'RequiredLevel':{'Value':'None'},'Format':'Text','MaxLength':size}
                else:
                    # Custom lengths and monetary precision/ranges need the metadata API.
                    payload=column_payload(column,display,kind,size)
                api.request('POST',"EntityDefinitions(LogicalName='"+logical+"')/Attributes",payload,solution=True)
                print('Added '+logical+'.'+column,flush=True)
            metadata=api.get("EntityDefinitions(LogicalName='"+logical+"')?$select=MetadataId,LogicalName,EntitySetName")
            result.append(metadata)
        if options.apply:
            Path('tmp/direct-cash-schema.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
            print(json.dumps(result),flush=True)
if __name__=='__main__': main()
