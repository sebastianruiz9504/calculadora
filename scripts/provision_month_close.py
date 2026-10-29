"""Create the month-close store in the existing finance solution, never modify operational data.
Managed API is required for OrganizationOwned + memo columns with explicit length.
"""
import argparse,json,shutil,subprocess,time
from pathlib import Path

ENV='https://orgc79ca19c.crm2.dynamics.com/'
SOLUTION='InvoiceProcessing'
TABLE='cr07a_cierremensual'
CONTEXT='app=dataverse-skills/1.16.0;skill=dv-metadata;agent=codex'
cli=Path(shutil.which('dataverse'))
command=[shutil.which('node'),str(cli.parent/'node_modules/@microsoft/dataverse/bin/dataverse.js')]
def api(path,body=None):
 args=command+['api','request','--target','dataverse','--environment',ENV,'--context',CONTEXT,'--path','/api/data/v9.2/'+path]
 if body is not None:
  target=Path('tmp/month-close-metadata-body.json');target.parent.mkdir(exist_ok=True);target.write_text(json.dumps(body),encoding='utf-8')
  args+=['--method','POST','--body-file',str(target.resolve()),'--header','MSCRM.SolutionUniqueName:'+SOLUTION]
 p=subprocess.run(args,capture_output=True,encoding='utf-8',errors='replace',timeout=180)
 if p.returncode:raise RuntimeError((p.stderr or p.stdout)[-2000:])
 try:return json.loads(p.stdout)
 except json.JSONDecodeError:return {'ok':True}
def label(text):return {'LocalizedLabels':[{'Label':text,'LanguageCode':1033}]}
def attr(name,labeltext,kind,**kwargs):
 return {'@odata.type':'Microsoft.Dynamics.CRM.'+kind+'AttributeMetadata','SchemaName':name,'DisplayName':label(labeltext),'RequiredLevel':{'Value':'None'},**kwargs}
def main():
 args=argparse.ArgumentParser();args.add_argument('--apply',action='store_true');apply=args.parse_args().apply
 solution=api("solutions?%24select=solutionid,ismanaged&%24expand=publisherid(%24select=customizationprefix)&%24filter=uniquename%20eq%20'"+SOLUTION+"'")['value']
 assert len(solution)==1 and not solution[0]['ismanaged'] and solution[0]['publisherid']['customizationprefix']=='cr07a'
 assert api('WhoAmI')['OrganizationId']=='c5dd555a-b9f6-ee11-a1fb-6045bd3ad3e8'
 existing=api("EntityDefinitions?%24select=LogicalName,EntitySetName&%24filter=LogicalName%20eq%20'"+TABLE+"'")['value']
 entity={'@odata.type':'Microsoft.Dynamics.CRM.EntityMetadata','SchemaName':'cr07a_CierreMensual','EntitySetName':'cr07a_cierremensuals',
  'DisplayName':label('Cierre mensual'),'DisplayCollectionName':label('Cierres mensuales'),'Description':label('Versiones inmutables de cierres mensuales; datos comprimidos e integridad SHA-256.'),
  'OwnershipType':'OrganizationOwned','HasActivities':False,'HasNotes':False,'IsActivity':False,'PrimaryNameAttribute':'cr07a_name','IsAuditEnabled':{'Value':True},'Attributes':[
   attr('cr07a_Name','Nombre','String',MaxLength=120,IsPrimaryName=True),
   attr('cr07a_Periodo','Periodo','String',MaxLength=7),attr('cr07a_Anio','Año','Integer',MinValue=2026,MaxValue=2100),
   attr('cr07a_Revision','Versión','Integer',MinValue=1,MaxValue=10000),attr('cr07a_Metodo','Método','String',MaxLength=30),
   attr('cr07a_Hash','SHA-256','String',MaxLength=64),attr('cr07a_Capturado','Capturado UTC','DateTime',Format='DateAndTime',DateTimeBehavior={'Value':'UserLocal'}),
   attr('cr07a_Resumen','Resumen JSON','Memo',MaxLength=100000),attr('cr07a_Contenido','Contenido GZip Base64','Memo',MaxLength=1048576)]}
 if not existing:
  if not apply:print('Prepared schema for '+TABLE+' in '+SOLUTION);return
  api('EntityDefinitions',entity)
  api('PublishXml',{'ParameterXml':'<importexportxml><entities><entity>'+TABLE+'</entity></entities></importexportxml>'})
 metadata=api("EntityDefinitions(LogicalName='"+TABLE+"')?%24select=LogicalName,EntitySetName,OwnershipType&%24expand=Attributes(%24select=LogicalName,AttributeType)")
 assert metadata['EntitySetName']=='cr07a_cierremensuals'
 Path('tmp').mkdir(exist_ok=True)
 Path('tmp/month-close-schema-verified.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({'environment':ENV,'solution':SOLUTION,'table':TABLE,'columns':len(metadata['Attributes']),'created':not bool(existing)}))
if __name__=='__main__':main()
