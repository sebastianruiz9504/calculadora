"""Independent read-back of data, published UI metadata and live flows."""
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT))
from scripts.shared_cost_centers.manage import *
from scripts.shared_cost_centers.flows import FLOWS,load
from scripts.shared_cost_centers.flow_definition import build,VALUE,EXPAND


def verify():
    c=access.client('dv-query');result={'tables':{},'flows':{},'catalog':{}}
    catalog=[dict(r) for r in c.records.list(CATALOG)]
    assert len(catalog)==5 and {r['cr07a_codigo']:r['cr07a_identificadorsiigo'] for r in catalog}==dict(CENTERS.values())
    assert all(r['statecode']==0 for r in catalog)
    by_id={r[CATPK]:r for r in catalog};result['catalog']={'rows':len(catalog),'codes':[r['cr07a_codigo'] for r in catalog]}
    for table in TABLES:
        pk=table+'id';rows=[dict(r) for r in c.records.list(table)]
        before=read(table+'-migration-before');columns=read(table+'-columns')
        protected=[('_'+x['LogicalName']+'_value' if x['AttributeType'] in ['Lookup','Customer','Owner'] else x['LogicalName'])
            for x in columns if x.get('IsValidForUpdate') and not x.get('IsLogical') and not x.get('AttributeOf') and x['LogicalName']!=FIELD]
        canonical=lambda records:{r[pk]:{k:r.get(k) for k in protected} for r in records}
        assert canonical(rows)==canonical(before),'Business values changed; inspect concurrent user edits'
        assert all(r.get(VALUE) in by_id for r in rows)
        assert all(by_id[r[VALUE]]['cr07a_codigo']==CENTERS[r[OLD]][0] for r in rows),'Current center differs from migration source'
        f,v=forms(c,table),views(c,table)
        assert f and v
        for form in f:
            root=ET.fromstring(form['formxml'])
            assert root.find(f".//control[@datafieldname='{FIELD}']") is not None
            assert root.find(f".//control[@datafieldname='{OLD}']") is None
            assert root.find(f".//Handler[@libraryName='{LIBRARY}']") is not None
        for view in v:
            assert ET.fromstring(view['fetchxml']).find(f"entity/attribute[@name='{FIELD}']") is not None
            assert ET.fromstring(view['layoutxml']).find(f"row/cell[@name='{FIELD}']") is not None
            assert ET.fromstring(view['fetchxml']).find(f"entity/attribute[@name='{OLD}']") is None
            assert ET.fromstring(view['layoutxml']).find(f"row/cell[@name='{OLD}']") is None
        result['tables'][table]={'rows':len(rows),'forms':len(f),'views':len(v),'values_preserved':True,
            'centers':{r['cr07a_codigo']:sum(x[VALUE]==r[CATPK] for x in rows) for r in catalog}}
    ledger=read('boris-controls-before');current=[dict(r) for r in c.records.list('cr07a_boriscobro',select=list(ledger[0]))]
    ordered=lambda rows:sorted(rows,key=lambda r:r['cr07a_boriscobroid'])
    assert ordered(current)==ordered(ledger);result['boris_billing_controls_unchanged']=len(ledger)
    h=access.headers('https://service.flow.microsoft.com/.default')
    for kind in FLOWS:
        live=load(kind,h)['properties'];before=read(kind+'-flow-before')['properties']
        expected=build(before['definition'],kind)
        assert access.redact(live['definition'])==expected
        assert live['state']=='Started' and live['connectionReferences']==before['connectionReferences']
        result['flows'][kind]={'state':live['state'],'last_modified':live['lastModifiedTime'],'document_id':7481,'shared_catalog':True}
    result['native_tests']=[{'flow':kind,'case':case,'passed':read('test-'+kind+'-'+case)['passed']}
        for kind in FLOWS for case in ['valid','missing','inactive','invalid-id']]
    assert all(read('temporary-'+kind+'-'+case)['deleted'] for kind in FLOWS for case in ['valid','missing','inactive','invalid-id'])
    save('verified',result);print(json.dumps(result),flush=True)


if __name__=='__main__':verify()
