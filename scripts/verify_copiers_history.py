"""Independent read-only reconciliation after the approved historical migration."""
import argparse, collections, json, pathlib
from migrate_copiers_history import CachedCredential, migration_client, load_env, clean, canon, identity, FIELDS, OLD, NEW, EVIDENCE, EXCLUDED, HISTORICAL, NO_MAIL, FILE_PURPOSE, WORKER

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
    frozen=json.loads((a.output/'source-snapshot.json').read_text(encoding='utf-8'))['rows']
    expected={r['cr07a_mantenimientoid']:r for r in frozen if r['cr07a_mantenimientoid'] not in EXCLUDED}
    load_env();c=migration_client(CachedCredential())
    source=[clean(r) for r in c.records.list(OLD,select=FIELDS)]
    if canon(sorted(source,key=lambda r:r['cr07a_mantenimientoid']))!=canon(sorted(frozen,key=lambda r:r['cr07a_mantenimientoid'])):raise RuntimeError('Source changed; reconcile before cutover')
    rows=list(c.records.list(NEW,filter="dtc_formversion eq 'copiers-legacy-v1'"))
    if len(rows)!=len(expected) or {r.get('dtc_legacysourcekey') for r in rows}!=set(expected):raise RuntimeError('Historical record count or exact identities differ')
    evidence=list(c.records.list(EVIDENCE,filter=f'dtc_purpose eq {FILE_PURPOSE}'))
    by_parent=collections.defaultdict(list)
    for e in evidence:by_parent[e['_dtc_signedmto_value']].append(e)
    verified={x['source']:x for x in map(json.loads,(a.output/'verified.jsonl').read_text(encoding='utf-8').splitlines())}
    files=0;byte_count=0
    for row in rows:
        key=row['dtc_legacysourcekey'];original=expected[key];target=identity('record',key)
        assert row['dtc_copiersmtov2id']==target and key in verified
        assert row['_ownerid_value']==WORKER
        assert row['dtc_legacyjson']==canon(original)
        assert row['dtc_workflowstate']==HISTORICAL and row['dtc_emailstate']==NO_MAIL
        assert row['dtc_businessstatus']==original['cr07a_estadodelmantenimiento']
        assert row['dtc_technicianuserkey']==original['_ownerid_value']
        assert row['_dtc_client_value']==original['_cr07a_cliente_value']
        assert row.get('_dtc_equipment_value')==(original.get('_cr07a_iddeequipo_value') or original.get('_cr07a_numerodeserie_value'))
        assert row['dtc_servicedate'][:10]==original['cr07a_fechademantenimiento'][:10]
        assert row.get('dtc_legacyrequestkey')==original.get('_cr07a_solicitudrelacionada_value')
        assert not row.get('dtc_signatureevidencekey') and not row.get('dtc_readyatutc')
        linked=by_parent.get(target,[])
        if original.get('cr07a_actadeentregadeservicio'):
            assert len(linked)==1;file=linked[0]
            assert file['dtc_copiersmtoevidenciav2id']==identity('file',key) and file.get('dtc_filecontent')
            assert file['_ownerid_value']==WORKER
            assert file['dtc_originalfilename']==row['dtc_reportfilename']==original['cr07a_actadeentregadeservicio_name']
            assert file['dtc_evidencekey']==row['dtc_reportevidencekey']
            assert file['dtc_sha256']==row['dtc_reportsha256']==verified[key]['fileSha256']
            files+=1;byte_count+=file['dtc_bytelength']
        else:assert not linked and not row.get('dtc_reportevidencekey')
    assert len(evidence)==files
    result={'originalsUnchanged':len(source),'historicalRecords':len(rows),'originalFiles':files,'fileBytes':byte_count,
            'excludedNotMigrated':EXCLUDED,'businessStatusCounts':dict(collections.Counter(r['dtc_businessstatus'] for r in rows)),
            'nativeRecords':sum(1 for _ in c.records.list(NEW,select=['dtc_copiersmtov2id'],filter="dtc_formversion ne 'copiers-legacy-v1'")),
            'allMigratedMailNotApplicable':True,'individualFileReadBackSha256Verified':True}
    (a.output/'final-reconciliation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False))
if __name__=='__main__':main()
