"""Add collection history and client recipients to the existing InvoiceProcessing solution.

Read-only unless --apply. Uses organization-owned history with custom memo lengths
and optimistic concurrency; these metadata settings require the Web API surface.
"""
import argparse
import json
import os
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from auth import get_client, get_token, get_plugin_headers, load_env

URL = 'https://orgc79ca19c.crm2.dynamics.com'
SOLUTION = 'InvoiceProcessing'

def label(text):
    return {'@odata.type':'Microsoft.Dynamics.CRM.Label','LocalizedLabels':[{'Label':text,'LanguageCode':3082}]}

def text_column(name, title, length=100, memo=False):
    return {'@odata.type':'Microsoft.Dynamics.CRM.' + ('Memo' if memo else 'String') + 'AttributeMetadata', 'SchemaName':name, 'DisplayName':label(title), 'RequiredLevel':{'Value':'None'}, 'MaxLength':length}

def main():
    apply = argparse.ArgumentParser()
    apply.add_argument('--apply', action='store_true')
    args = apply.parse_args()
    load_env()
    if os.environ['DATAVERSE_URL'].rstrip('/') != URL: raise RuntimeError('Unexpected environment')
    client = get_client('dv-metadata')
    solutions = list(client.records.list('solution', filter=f"uniquename eq '{SOLUTION}'",select=['solutionid','_publisherid_value','ismanaged']))
    if len(solutions) != 1 or solutions[0]['ismanaged']: raise RuntimeError('Expected unmanaged InvoiceProcessing solution')
    publisher = client.records.retrieve('publisher',solutions[0]['_publisherid_value'])
    if publisher['customizationprefix'] != 'cr07a': raise RuntimeError('Unexpected publisher prefix')
    token=get_token()
    def request(method,path,data=None):
        headers=get_plugin_headers('dv-metadata',token)
        headers.update({'Accept':'application/json','Content-Type':'application/json','MSCRM.SolutionUniqueName':SOLUTION})
        for attempt in range(5):
            try:
                with urlopen(Request(URL+'/api/data/v9.2/'+path,data=json.dumps(data).encode() if data is not None else None,headers=headers,method=method),timeout=90) as response:
                    body=response.read()
                    return json.loads(body) if body else {}
            except HTTPError as error:
                body=error.read().decode()
                if error.code == 404 and method == 'GET': return None
                if any(k in body for k in ['another','0x80040216','0x80060891']) and attempt<4:
                    time.sleep(5*(attempt+1)); continue
                raise RuntimeError(f'{method} {path}: HTTP {error.code}: {body}') from error
    table=request('GET',"EntityDefinitions(LogicalName='cr07a_carteragestion')?$select=MetadataId,EntitySetName,IsOptimisticConcurrencyEnabled")
    contact=request('GET',"EntityDefinitions(LogicalName='cr07a_cliente')/Attributes(LogicalName='cr07a_correocobranza')?$select=MetadataId,LogicalName,AttributeType")
    print(json.dumps({'environment':URL,'solution':SOLUTION,'historyExists':bool(table),'collectionEmailExists':bool(contact),'apply':args.apply}),flush=True)
    if not args.apply: return
    if not table:
        attrs=[text_column('cr07a_name','Gestion',150),text_column('cr07a_factura','Factura',36),text_column('cr07a_cliente','Cliente',36),text_column('cr07a_tipo','Tipo',30),text_column('cr07a_estado','Estado',30),text_column('cr07a_autor','Autor',250),text_column('cr07a_destinatarios','Destinatarios',4000),text_column('cr07a_asunto','Asunto',250),text_column('cr07a_mensaje','Mensaje',100000,True),text_column('cr07a_facturas','Facturas incluidas',100000,True),{'@odata.type':'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata','SchemaName':'cr07a_enviadoel','DisplayName':label('Enviado el'),'RequiredLevel':{'Value':'None'},'Format':'DateAndTime','DateTimeBehavior':{'Value':'UserLocal'}}]
        attrs[0]['IsPrimaryName']=True
        request('POST','EntityDefinitions',{'@odata.type':'Microsoft.Dynamics.CRM.EntityMetadata','SchemaName':'cr07a_CarteraGestion','EntitySetName':'cr07a_carteragestions','DisplayName':label('Gestion de cartera'),'DisplayCollectionName':label('Gestiones de cartera'),'OwnershipType':'OrganizationOwned','HasActivities':False,'HasNotes':False,'IsActivity':False,'IsOptimisticConcurrencyEnabled':True,'PrimaryNameAttribute':'cr07a_name','Attributes':attrs})
        print('History table created',flush=True)
        time.sleep(8)
    if not contact:
        request('POST',"EntityDefinitions(LogicalName='cr07a_cliente')/Attributes",text_column('cr07a_CorreoCobranza','Correo cobranza',4000))
        print('Collection email column created',flush=True)
    table=request('GET',"EntityDefinitions(LogicalName='cr07a_carteragestion')?$select=MetadataId,EntitySetName")
    contact=request('GET',"EntityDefinitions(LogicalName='cr07a_cliente')/Attributes(LogicalName='cr07a_correocobranza')?$select=MetadataId,LogicalName,AttributeType")
    for meta, kind in [(table,1),(contact,2)]:
        request('POST','AddSolutionComponent',{'ComponentId':meta['MetadataId'],'ComponentType':kind,'SolutionUniqueName':SOLUTION,'AddRequiredComponents':False})
    request('POST','PublishXml',{'ParameterXml':'<importexportxml><entities><entity>cr07a_cliente</entity><entity>cr07a_carteragestion</entity></entities></importexportxml>'})
    print(json.dumps({'history':table,'collectionEmail':contact}),flush=True)

if __name__=='__main__': main()
