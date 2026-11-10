"""Read-only verification of prepared close sections and preservation after deployment."""
import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path
from auth import get_client

parser = argparse.ArgumentParser()
parser.add_argument("--after", action="store_true")
parser.add_argument("--prepared", default="tmp/month-close-dashboard")
args = parser.parse_args()
evidence = Path("tmp/month-close-audit/upgrade-before.json")
def preserved(current, original):
    normalized = dict(original)
    # These empty collections were added to the model before this release;
    # older saved payloads omit them, and deserialization supplies empty lists.
    normalized.setdefault("bankAccounts", [])
    normalized.setdefault("settlementAssumptions", [])
    if current == normalized:
        return True
    print("report_difference", original.get("key"), {k: "added" if k not in original else "changed" for k in current if current.get(k) != original.get(k)})
    return False
with get_client("dv-query") as client:
    rows = list(client.records.list("cr07a_cierremensual", select=["cr07a_periodo", "cr07a_revision", "cr07a_hash", "cr07a_metodo", "cr07a_contenido"]))
    snapshots = {}
    for row in rows:
        raw = gzip.decompress(base64.b64decode(row["cr07a_contenido"]))
        digest = hashlib.sha256(raw).hexdigest()
        assert digest == row["cr07a_hash"], "Snapshot integrity mismatch"
        snapshots[digest] = json.loads(raw)
    if args.after:
        before = json.loads(evidence.read_text(encoding="utf-8"))
        assert all(h in snapshots for h in before["originalHashes"]), "An original close changed or disappeared"
        upgraded = [s for s in snapshots.values() if s.get("sourceHashes", {}).get("original-close") in before["originalHashes"]]
        assert len(upgraded) == before["preparedCount"], "Upgrade count mismatch"
        for current in upgraded:
            original = snapshots[current["sourceHashes"]["original-close"]]
            reports = {r["key"]: r for r in current["reports"]}
            assert all(preserved(reports[r["key"]], r) for r in original["reports"]), "Original financial report changed"
        result = {"originalVersionsPreserved": len(before["originalHashes"]), "newVersionsVerified": len(upgraded), "integrityVerified": len(snapshots)}
        evidence.with_name("upgrade-after.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    else:
        prepared = list(Path(args.prepared).glob("20??-??.json"))
        for path in prepared:
            current = json.loads(path.read_text(encoding="utf-8"))
            original = snapshots[current["sourceHashes"]["original-close"]]
            reports = {r["key"]: r for r in current["reports"]}
            assert all(preserved(reports[r["key"]], r) for r in original["reports"]), "Prepared report changed original data"
            assert current["summary"]["aging"] == original["summary"]["aging"]
            assert current["summary"]["billingLines"] == original["summary"]["billingLines"]
        result = {"originalHashes": [h for h, s in snapshots.items() if s["summary"]["mode"] != "live"], "preparedCount": len(prepared)}
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps(result, indent=2), encoding="utf-8")
        result = {"originalVersions": len(result["originalHashes"]), "preparedVersionsVerified": len(prepared)}
    print(json.dumps(result))
