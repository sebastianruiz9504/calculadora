"""Reversible Dataverse smoke test for the bank opening-balance alternate key."""

import argparse
import os
import time

from PowerPlatform.Dataverse.models import UpsertItem

from auth import get_client, load_env


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
TABLE = "cr07a_cierreflujocajabanco"
ID_FIELD = "cr07a_cierreflujocajabancoid"
TEST_KEY = "conciliacion:flujo-caja:banco:2099-12:cloud:11100504"


def query_test_rows(client):
    escaped_key = TEST_KEY.replace("'", "''")
    return list(client.records.list(
        TABLE,
        filter=f"cr07a_claveexterna eq '{escaped_key}'",
        select=[
            ID_FIELD,
            "cr07a_claveexterna",
            "cr07a_periodokey",
            "cr07a_origenflujo",
            "cr07a_bancocuentacodigo",
            "cr07a_saldoinicial",
        ],
        top=2,
    ).records)


def wait_for_rows(client, expected_count, attempts=12):
    for _ in range(attempts):
        rows = query_test_rows(client)
        if len(rows) == expected_count:
            return rows
        time.sleep(1)
    raise RuntimeError(
        f"Expected {expected_count} smoke rows, found {len(query_test_rows(client))}"
    )


def upsert(client, amount):
    client.records.upsert(TABLE, [UpsertItem(
        alternate_key={"cr07a_claveexterna": TEST_KEY},
        record={
            "cr07a_name": "Codex smoke saldo banco 2099-12",
            "cr07a_claveexterna": TEST_KEY,
            "cr07a_periodokey": "2099-12",
            "cr07a_origenflujo": "Cloud",
            "cr07a_bancocuentacodigo": "11100504",
            "cr07a_bancocuentanombre": "Bancolombia Cloud 8100",
            "cr07a_saldoinicial": amount,
        },
    )])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    load_env()
    environment = os.environ.get("DATAVERSE_URL", "")
    if environment.rstrip("/").lower() != EXPECTED_ENVIRONMENT.rstrip("/").lower():
        raise RuntimeError(f"Unexpected Dataverse environment: {environment}")

    client = get_client("dv-data")
    existing = query_test_rows(client)
    if existing:
        raise RuntimeError(
            f"Smoke key already exists; no records changed: {TEST_KEY}"
        )
    if not args.apply:
        print(f"Ready: {environment} | no existing smoke row | no changes made")
        return

    created_id = ""
    try:
        upsert(client, 123_456.78)
        first = wait_for_rows(client, 1)[0]
        created_id = str(first.get(ID_FIELD, ""))
        if not created_id or float(first.get("cr07a_saldoinicial", 0)) != 123_456.78:
            raise RuntimeError("The first upsert was not read back correctly")

        upsert(client, 234_567.89)
        second_rows = wait_for_rows(client, 1)
        second = second_rows[0]
        if str(second.get(ID_FIELD, "")) != created_id:
            raise RuntimeError("The alternate-key upsert created a duplicate record")
        if float(second.get("cr07a_saldoinicial", 0)) != 234_567.89:
            raise RuntimeError("The second upsert did not replace the opening balance")

        print(
            f"PASS create/update/read-back: id={created_id} rows={len(second_rows)}",
            flush=True,
        )
    finally:
        if created_id:
            client.records.delete(TABLE, created_id, use_bulk_delete=False)
            wait_for_rows(client, 0)
            print(f"PASS cleanup: deleted test record {created_id}", flush=True)


if __name__ == "__main__":
    main()
