"""Export server-generated metadata and refresh the existing flow packages."""
import base64
import io
import json
import subprocess
import sys
import zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT))
from scripts.shared_cost_centers.manage import dv,SOLUTION,OUT,ROOT,CATALOG,TABLES,read


def export():
    assert read('verified')['tables'] and read('app-after')['validation']['AppValidationResponse']['ValidationSuccess']
    result=dv('POST','ExportSolution',json={'SolutionName':SOLUTION,'Managed':False})
    data=base64.b64decode(result['ExportSolutionFile'])
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        assert not any(n.lower().startswith('workflows/') for n in archive.namelist()), 'Shared metadata package must not contain credentialed flows'
    path=OUT/(SOLUTION+'.zip');path.write_bytes(data)
    commands=[['pac','solution','unpack','--zipfile',str(path),'--folder',str(ROOT/'solutions'/SOLUTION),'--packagetype','Unmanaged'],
        [sys.executable,'scripts/cloud_cost_centers/manage.py','export'],
        [sys.executable,'scripts/boris_cost_centers/manage.py','export'],
        [sys.executable,'scripts/copiers_cost_centers/manage.py','export']]
    for command in commands:
        process=subprocess.run(command,capture_output=True,text=True)
        if process.returncode:raise RuntimeError(process.stdout[-2500:]+process.stderr[-2500:])
        print(process.stdout[-1500:],flush=True)
    finalize()


def finalize():
    entities={p.name.lower() for p in (ROOT/'solutions'/SOLUTION/'Entities').iterdir() if p.is_dir()}
    assert entities=={CATALOG,*TABLES}, entities
    evidence=ROOT/'solutions'/SOLUTION/'Validation';evidence.mkdir(exist_ok=True,parents=True)
    (evidence/'verified.json').write_text(json.dumps(read('verified'),indent=2)+'\n',encoding='utf-8')
    (ROOT/'solutions'/SOLUTION/'catalog-seed.json').write_text(json.dumps([
        {key:r[key] for key in ['cr07a_codigo','cr07a_name','cr07a_identificadorsiigo']} for r in read('catalog-rows')],indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'shared_solution_exported':SOLUTION,'entities':sorted(entities)}),flush=True)


if __name__=='__main__':export()
