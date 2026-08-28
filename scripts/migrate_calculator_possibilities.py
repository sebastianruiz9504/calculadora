"""Backfill legacy calculator scenarios into durable group/line records.

Dry-run is the default. Run only after the calculator schema and compatible app
have been deployed and verified. Legacy JSON remains in place for rollback.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import urllib.error
import urllib.parse
import urllib.request
import uuid
from decimal import Decimal, InvalidOperation

from auth import get_plugin_headers, get_token, load_env
from provision_calculator_possibilities_schema import (
    COLUMNS as SCHEMA_COLUMNS,
    KEYS as SCHEMA_KEYS,
    RELATIONSHIP_SCHEMA,
    column_contract_mismatches,
)


EXPECTED_ENVIRONMENT = "https://orgc79ca19c.crm2.dynamics.com/"
EXPECTED_PROFILE = "digital-tech-default"
SOLUTION = "CotizadorInternoCRM"
TABLE = "cr07a_negocioscomerciales"
SET = "cr07a_negocioscomercialeses"
ID = "cr07a_negocioscomercialesid"
GROUP = 645250000
POSSIBILITY = 645250001
LINE = 645250002
NAMESPACE = uuid.UUID("a9d37acb-cd7f-4b1c-9f34-22f96f1490f4")
DECIMAL_MIN = Decimal("-100000000000")
DECIMAL_MAX = Decimal("100000000000")
PARENT_LOOKUP_VALUE = "_cr07a_parentrecord_value"
LEGACY_REQUIRED_COLUMNS = {
    "cr07a_name",
    "cr07a_scenarioid",
    "cr07a_scenarioname",
    "cr07a_dealtype",
    "cr07a_requiresproration",
    "cr07a_startdate",
    "cr07a_enddate",
    "cr07a_linesjson",
    "cr07a_lastresultjson",
    "cr07a_systemuserid",
    "cr07a_displayname",
    "cr07a_email",
}


def clean(value) -> str:
    return str(value or "").strip()


def ci(mapping: dict, name: str, default=None):
    target = name.lower()
    for key, value in mapping.items():
        if key.lower() == target:
            return value
    return default


def parse_json(value, fallback):
    if not value:
        return fallback
    try:
        parsed = json.loads(value)
        return parsed if parsed is not None else fallback
    except (TypeError, json.JSONDecodeError):
        raise RuntimeError("A legacy calculator JSON snapshot is invalid")


def record_key(kind: str, value: str) -> str:
    normalized = "".join(
        character for character in clean(value).lower()
        if character.isalnum() or character in "-_:"
    )
    return f"{kind}:{normalized}"[:180]


def odata_literal(value: str) -> str:
    return clean(value).replace("'", "''")


def decimal_value(value, label: str) -> Decimal:
    if value is None or value == "":
        return Decimal(0)
    if isinstance(value, bool):
        raise RuntimeError(f"{label} must be a decimal number")
    try:
        number = Decimal(str(value))
    except (InvalidOperation, ValueError) as error:
        raise RuntimeError(f"{label} has an invalid decimal value: {value}") from error
    if not number.is_finite():
        raise RuntimeError(f"{label} must be a finite decimal number")
    return number


def decimal_text(value) -> str:
    number = decimal_value(value, "Calculator value")
    if number == 0:
        return "0"
    text = format(number, "f")
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return "0" if text in ("", "-0") else text


def bool_value(value, default: bool = False) -> bool:
    if value is None or value == "":
        return default
    if isinstance(value, bool):
        return value
    raise RuntimeError(f"Calculator boolean value is invalid: {value}")


def int_value(value, label: str, minimum: int, maximum: int, default: int) -> int:
    if value is None or value == "":
        number = default
    elif isinstance(value, bool):
        raise RuntimeError(f"{label} must be an integer")
    else:
        try:
            decimal = Decimal(str(value))
        except (InvalidOperation, ValueError) as error:
            raise RuntimeError(f"{label} has an invalid integer value: {value}") from error
        if not decimal.is_finite() or decimal != decimal.to_integral_value():
            raise RuntimeError(f"{label} must be an integer")
        number = int(decimal)
    if number < minimum or number > maximum:
        raise RuntimeError(f"{label} must be between {minimum} and {maximum}")
    return number


def bounded_decimal(value, label: str, precision: int) -> Decimal:
    number = decimal_value(value, label)
    if number < DECIMAL_MIN or number > DECIMAL_MAX:
        raise RuntimeError(f"{label} is outside the Dataverse decimal range")
    exponent = (number.normalize() if number else Decimal(0)).as_tuple().exponent
    decimal_places = max(0, -exponent)
    if decimal_places > precision:
        raise RuntimeError(
            f"{label} has {decimal_places} decimal places; Dataverse allows {precision}"
        )
    return number


def decimal_payload(value: Decimal) -> str:
    # IEEE754Compatible OData payloads carry Edm.Decimal values as strings, so
    # no precision is lost through a binary float before Dataverse receives it.
    return decimal_text(value)


def normalized_id(value) -> str:
    return clean(value).strip("{}").lower()


def lines_hash(lines: list[dict]) -> str:
    canonical: list[str] = []
    for index, line in enumerate(lines, start=1):
        description = " ".join(clean(ci(line, "ProductDescription")).split())
        fields = (
            str(index),
            str(int(ci(line, "BusinessType", 0) or 0)),
            clean(ci(line, "ProductId")),
            description,
            decimal_text(ci(line, "CostUnit", 0)),
            decimal_text(ci(line, "MarginPercent", 0)),
            str(int(ci(line, "ContractMonths", 12) or 12)),
            str(int(ci(line, "Quantity", 1) or 1)),
            decimal_text(ci(line, "SuggestedRetailPrice", 0)),
            decimal_text(ci(line, "Acelerador", 0)),
            "1" if bool_value(ci(line, "HasVat", False)) else "0",
        )
        canonical.append("\n" + "|".join(fields))
    return hashlib.sha256("".join(canonical).encode("utf-8")).hexdigest().upper()


class DataApi:
    def __init__(self) -> None:
        load_env()
        self.base = os.environ["DATAVERSE_URL"].rstrip("/")
        self.token = get_token()

    def request(self, method: str, path: str, body=None, prefer_representation=False,
                if_match: str = ""):
        url = path if path.startswith("http") else f"{self.base}/api/data/v9.2/{path}"
        headers = get_plugin_headers("dv-data", self.token)
        headers.update({
            "Accept": "application/json;IEEE754Compatible=true",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
        })
        if prefer_representation:
            headers["Prefer"] = "return=representation"
        if if_match:
            headers["If-Match"] = if_match
        data = None
        if body is not None:
            data = json.dumps(body, ensure_ascii=True).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8;IEEE754Compatible=true"
        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"{method} {path} failed HTTP {error.code}: {detail}") from error

    def collection(self, path: str) -> list[dict]:
        rows: list[dict] = []
        next_path = path
        while next_path:
            result = self.request("GET", next_path)
            rows.extend(result.get("value", []))
            next_path = result.get("@odata.nextLink", "")
        return rows

    def attributes(self) -> dict[str, dict]:
        rows = self.collection(
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes"
            "?$select=LogicalName,SchemaName,AttributeType,AttributeTypeName,RequiredLevel"
        )
        return {clean(row.get("LogicalName")).lower(): row for row in rows}

    def attribute_detail(self, logical_name: str, kind: str) -> dict:
        metadata_type = {
            "String": "StringAttributeMetadata",
            "Integer": "IntegerAttributeMetadata",
            "Decimal": "DecimalAttributeMetadata",
            "Boolean": "BooleanAttributeMetadata",
            "Choice": "PicklistAttributeMetadata",
            "File": "FileAttributeMetadata",
        }[kind]
        return self.request(
            "GET",
            f"EntityDefinitions(LogicalName='{TABLE}')/Attributes(LogicalName='{logical_name}')/"
            f"Microsoft.Dynamics.CRM.{metadata_type}",
        )

    def keys(self) -> list[dict]:
        return self.collection(
            f"EntityDefinitions(LogicalName='{TABLE}')/Keys"
            "?$select=SchemaName,KeyAttributes,EntityKeyIndexStatus"
        )

    def relationship(self) -> dict | None:
        escaped = urllib.parse.quote(f"SchemaName='{RELATIONSHIP_SCHEMA}'", safe="='_")
        try:
            return self.request(
                "GET",
                f"RelationshipDefinitions({escaped})/"
                "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?"
                "$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencedAttribute,ReferencingAttribute",
            )
        except RuntimeError as error:
            if "failed HTTP 404:" in str(error):
                return None
            raise


def verify_context(api: DataApi) -> None:
    environment_url = os.environ.get("DATAVERSE_URL", "")
    if environment_url.rstrip("/").lower() != EXPECTED_ENVIRONMENT.rstrip("/").lower():
        raise RuntimeError(f"Unexpected Dataverse environment: {environment_url}")
    if os.environ.get("PAC_AUTH_PROFILE", "") != EXPECTED_PROFILE:
        raise RuntimeError("Unexpected PAC profile")
    if os.environ.get("SOLUTION_NAME", "") != SOLUTION:
        raise RuntimeError("Unexpected solution")
    required = LEGACY_REQUIRED_COLUMNS | {
        schema.lower() for schema, *_ in SCHEMA_COLUMNS
    } | {"cr07a_parentrecord"}
    metadata = api.attributes()
    available = set(metadata)
    missing = sorted(required - available)
    if missing:
        raise RuntimeError(f"Calculator schema must be deployed first: {missing}")

    column_mismatches = column_contract_mismatches(api, metadata)
    if column_mismatches:
        raise RuntimeError(f"Calculator column contract mismatch: {column_mismatches}")

    actual_keys = {
        clean(row.get("SchemaName")).lower(): row
        for row in api.keys()
        if clean(row.get("SchemaName"))
    }
    key_errors: list[dict] = []
    for schema, expected_attributes in SCHEMA_KEYS:
        actual_key = actual_keys.get(schema.lower())
        if actual_key is None:
            key_errors.append({"key": schema, "error": "missing"})
            continue
        actual_attributes = tuple(
            clean(value).lower() for value in actual_key.get("KeyAttributes", [])
        )
        status = clean(actual_key.get("EntityKeyIndexStatus"))
        if actual_attributes != expected_attributes or status.lower() != "active":
            key_errors.append({
                "key": schema,
                "expectedAttributes": expected_attributes,
                "actualAttributes": actual_attributes,
                "status": status,
            })
    if key_errors:
        raise RuntimeError(f"Calculator alternate-key contract is not active: {key_errors}")
    relationship = api.relationship()
    if relationship is None:
        raise RuntimeError(f"Calculator parent relationship is missing: {RELATIONSHIP_SCHEMA}")
    expected_relationship = {
        "ReferencedEntity": TABLE,
        "ReferencingEntity": TABLE,
        "ReferencedAttribute": ID,
        "ReferencingAttribute": "cr07a_parentrecord",
    }
    relationship_errors = {
        field: {"expected": expected, "actual": clean(relationship.get(field))}
        for field, expected in expected_relationship.items()
        if clean(relationship.get(field)).lower() != expected.lower()
    }
    if relationship_errors:
        raise RuntimeError(f"Calculator parent relationship contract mismatch: {relationship_errors}")
    print(f"Context verified: {environment_url} | {SOLUTION}", flush=True)


def legacy_rows(api: DataApi) -> list[dict]:
    fields = ",".join((
        ID, "cr07a_scenarioid", "cr07a_scenarioname", "cr07a_dealtype",
        "cr07a_requiresproration", "cr07a_startdate", "cr07a_enddate",
        "cr07a_linesjson", "cr07a_lastresultjson", "cr07a_systemuserid",
        "cr07a_displayname", "cr07a_email", "cr07a_recordtype", "cr07a_groupid",
        "cr07a_groupname", "cr07a_possibilityname", "cr07a_possibilityorder",
    ))
    query = urllib.parse.urlencode({
        "$select": fields,
        # Typed possibilities may already contain user decisions (included and
        # recommended flags). They are deliberately outside this backfill.
        "$filter": "cr07a_scenarioid ne null and cr07a_recordtype eq null",
        "$orderby": "createdon asc",
    }, safe="$(),= '" )
    return api.collection(f"{SET}?{query}")


def query_one(api: DataApi, filter_text: str, select: str) -> dict | None:
    query = urllib.parse.urlencode({
        "$select": select,
        "$filter": filter_text,
        "$top": "2",
    }, safe="$(),= '")
    rows = api.collection(f"{SET}?{query}")
    if len(rows) > 1:
        raise RuntimeError(f"Duplicate calculator records for filter: {filter_text}")
    return rows[0] if rows else None


def bounded_text(value, label: str, maximum: int, fallback: str = "") -> str:
    text = clean(value) or fallback
    if len(text) > maximum:
        raise RuntimeError(f"{label} exceeds {maximum} characters")
    return text


def normalize_result(value, scenario_id: str) -> dict | None:
    if value is None:
        return None
    if not isinstance(value, dict):
        raise RuntimeError(f"Scenario {scenario_id} result JSON is not an object")
    input_hash = clean(ci(value, "InputHash"))
    if input_hash and (len(input_hash) != 64 or any(character not in "0123456789abcdefABCDEF" for character in input_hash)):
        raise RuntimeError(f"Scenario {scenario_id} has an invalid input hash")
    proration_text = bounded_text(
        ci(value, "ProrationText"),
        f"Scenario {scenario_id} proration text",
        300,
    )
    return {
        "InputHash": input_hash,
        "Points": bounded_decimal(ci(value, "Points", 0), f"Scenario {scenario_id} points", 6),
        "Commission": bounded_decimal(ci(value, "Commission", 0), f"Scenario {scenario_id} commission", 4),
        "ProrationDays": int_value(
            ci(value, "ProrationDays", 0), f"Scenario {scenario_id} proration days", 0, 10000, 0
        ),
        "ProrationFactor": bounded_decimal(
            ci(value, "ProrationFactor", 0), f"Scenario {scenario_id} proration factor", 10
        ),
        "ProrationText": proration_text,
        "TotalMonthlySale": bounded_decimal(
            ci(value, "TotalMonthlySale", 0), f"Scenario {scenario_id} monthly sale", 4
        ),
        "TotalSale": bounded_decimal(ci(value, "TotalSale", 0), f"Scenario {scenario_id} total sale", 4),
    }


def normalize_line(value, scenario_id: str, index: int) -> dict:
    if not isinstance(value, dict):
        raise RuntimeError(f"Scenario {scenario_id} line {index} is not an object")
    line_id = clean(ci(value, "LineId")) or str(uuid.uuid5(NAMESPACE, f"{scenario_id}:{index}"))
    if len(line_id) > 100:
        raise RuntimeError(f"Scenario {scenario_id} line {index} id exceeds 100 characters")
    return {
        "LineId": line_id,
        "LineOrder": index,
        "BusinessType": int_value(
            ci(value, "BusinessType", 0), f"Scenario {scenario_id} line {index} business type", 0, 20, 0
        ),
        "ProductId": bounded_text(
            ci(value, "ProductId"), f"Scenario {scenario_id} line {index} product id", 100
        ),
        "ProductDescription": bounded_text(
            ci(value, "ProductDescription"), f"Scenario {scenario_id} line {index} description", 500
        ),
        "CostUnit": bounded_decimal(
            ci(value, "CostUnit", 0), f"Scenario {scenario_id} line {index} cost", 4
        ),
        "MarginPercent": bounded_decimal(
            ci(value, "MarginPercent", 0), f"Scenario {scenario_id} line {index} margin", 6
        ),
        "ContractMonths": int_value(
            ci(value, "ContractMonths", 12), f"Scenario {scenario_id} line {index} months", 1, 1200, 12
        ),
        "Quantity": int_value(
            ci(value, "Quantity", 1), f"Scenario {scenario_id} line {index} quantity", 1, 1000000, 1
        ),
        "SuggestedRetailPrice": bounded_decimal(
            ci(value, "SuggestedRetailPrice", 0), f"Scenario {scenario_id} line {index} suggested price", 4
        ),
        "Acelerador": bounded_decimal(
            ci(value, "Acelerador", 0), f"Scenario {scenario_id} line {index} accelerator", 6
        ),
        "HasVat": bool_value(ci(value, "HasVat", False)),
    }


def build_migration_plans(rows: list[dict]) -> list[dict]:
    plans: list[dict] = []
    seen_scenarios: set[str] = set()
    for row in rows:
        scenario_id = bounded_text(row.get("cr07a_scenarioid"), "Scenario id", 100)
        if not scenario_id:
            raise RuntimeError("A legacy calculator row has no scenario id")
        scenario_key = scenario_id.lower()
        if scenario_key in seen_scenarios:
            raise RuntimeError(f"Duplicate legacy scenario id: {scenario_id}")
        seen_scenarios.add(scenario_key)
        record_id = normalized_id(row.get(ID))
        if not record_id:
            raise RuntimeError(f"Scenario {scenario_id} has no Dataverse record id")
        etag = clean(row.get("@odata.etag"))
        if not etag:
            raise RuntimeError(f"Scenario {scenario_id} has no Dataverse ETag")
        owner = bounded_text(row.get("cr07a_systemuserid"), f"Scenario {scenario_id} owner", 100)
        if not owner:
            raise RuntimeError(f"Scenario {scenario_id} has no owner system user id")
        scenario_name = bounded_text(
            row.get("cr07a_scenarioname"), f"Scenario {scenario_id} name", 200, "Escenario"
        )
        group_id = bounded_text(
            row.get("cr07a_groupid"), f"Scenario {scenario_id} group id", 100, scenario_id
        )
        group_name = bounded_text(
            row.get("cr07a_groupname"), f"Scenario {scenario_id} group name", 200, scenario_name
        )
        possibility_name = bounded_text(
            row.get("cr07a_possibilityname"),
            f"Scenario {scenario_id} possibility name",
            200,
            scenario_name,
        )
        order = int_value(
            row.get("cr07a_possibilityorder"), f"Scenario {scenario_id} possibility order", 1, 3, 1
        )
        source_lines = parse_json(row.get("cr07a_linesjson"), [])
        if not isinstance(source_lines, list):
            raise RuntimeError(f"Scenario {scenario_id} lines JSON is not an array")
        if len(source_lines) > 1000:
            raise RuntimeError(f"Scenario {scenario_id} exceeds 1000 lines")
        lines = [normalize_line(line, scenario_id, index) for index, line in enumerate(source_lines, start=1)]
        line_ids = [line["LineId"].lower() for line in lines]
        if len(line_ids) != len(set(line_ids)):
            raise RuntimeError(f"Scenario {scenario_id} contains duplicate line ids")
        result = normalize_result(parse_json(row.get("cr07a_lastresultjson"), None), scenario_id)
        plans.append({
            "row": row,
            "record_id": record_id,
            "etag": etag,
            "scenario_id": scenario_id,
            "group_id": group_id,
            "group_name": group_name,
            "possibility_name": possibility_name,
            "possibility_order": order,
            "include_in_proposal": True,
            "is_recommended": False,
            "owner": owner,
            "owner_name": bounded_text(
                row.get("cr07a_displayname"), f"Scenario {scenario_id} owner name", 200
            ),
            "owner_email": bounded_text(
                row.get("cr07a_email"), f"Scenario {scenario_id} owner email", 320
            ),
            "lines": lines,
            "lines_hash": lines_hash(lines),
            "result": result,
        })

    for group_id, grouped in group_by(plans, "group_id").items():
        orders = [plan["possibility_order"] for plan in grouped]
        if len(orders) != len(set(orders)):
            raise RuntimeError(f"Legacy group {group_id} contains duplicate possibility positions")
    return plans


def group_by(rows: list[dict], field: str) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {}
    for row in rows:
        grouped.setdefault(clean(row.get(field)).lower(), []).append(row)
    return grouped


def migration_state(api: DataApi) -> dict:
    common = ",".join((
        ID, "cr07a_recordtype", "cr07a_recordkey", "cr07a_groupid", "cr07a_scenarioid",
        "cr07a_possibilityorder", "cr07a_includeinproposal", "cr07a_isrecommended",
        "cr07a_systemuserid", "cr07a_inputhash", "cr07a_lineshash", PARENT_LOOKUP_VALUE,
        "cr07a_resultpoints", "cr07a_resultcommission", "cr07a_resultprorationdays",
        "cr07a_resultprorationfactor", "cr07a_resultprorationtext",
        "cr07a_resultmonthlysale", "cr07a_resulttotalsale",
    ))
    groups = api.collection(
        f"{SET}?$select={common}&$filter=cr07a_recordtype eq {GROUP}"
    )
    possibilities = api.collection(
        f"{SET}?$select={common}&$filter=cr07a_recordtype eq {POSSIBILITY}"
    )
    line_select = ",".join((
        common, "cr07a_possibilityid", "cr07a_lineid", "cr07a_lineorder",
        "cr07a_linebusinesstype", "cr07a_lineproductid", "cr07a_lineproductdescription",
        "cr07a_linecostunit", "cr07a_linemarginpercent", "cr07a_linecontractmonths",
        "cr07a_linequantity", "cr07a_linesuggestedprice", "cr07a_lineaccelerator", "cr07a_linehasvat",
    ))
    lines = api.collection(
        f"{SET}?$select={line_select}&$filter=cr07a_recordtype eq {LINE}"
    )
    keyed = api.collection(
        f"{SET}?$select={ID},cr07a_recordkey,cr07a_recordtype,cr07a_groupid,"
        "cr07a_scenarioid,cr07a_possibilityid,cr07a_lineid"
        "&$filter=cr07a_recordkey ne null"
    )
    return {"groups": groups, "possibilities": possibilities, "lines": lines, "keyed": keyed}


def unique_index(rows: list[dict], field: str, label: str) -> dict[str, dict]:
    result: dict[str, dict] = {}
    for row in rows:
        key = clean(row.get(field)).lower()
        if not key:
            continue
        if key in result:
            raise RuntimeError(f"Duplicate {label}: {key}")
        result[key] = row
    return result


def validate_record_key(keyed: dict[str, dict], expected_key: str, allowed_record_id: str, label: str) -> None:
    existing = keyed.get(expected_key.lower())
    if existing is None:
        return
    if normalized_id(existing.get(ID)) != normalized_id(allowed_record_id):
        raise RuntimeError(f"Record key collision for {label}: {expected_key}")


def prevalidate_migration(api: DataApi, plans: list[dict]) -> None:
    state = migration_state(api)
    groups = unique_index(state["groups"], "cr07a_groupid", "calculator group id")
    keyed = unique_index(state["keyed"], "cr07a_recordkey", "calculator record key")
    typed_by_group = group_by(state["possibilities"], "cr07a_groupid")
    lines_by_possibility = group_by(state["lines"], "cr07a_possibilityid")
    planned_groups = group_by(plans, "group_id")

    planned_line_keys: dict[str, str] = {}
    planned_possibility_keys: dict[str, str] = {}
    planned_group_keys: dict[str, str] = {}
    for group_key, group_plans in planned_groups.items():
        group_id = group_plans[0]["group_id"]
        owners = {plan["owner"].lower() for plan in group_plans}
        typed = typed_by_group.get(group_key, [])
        typed_owners = [clean(item.get("cr07a_systemuserid")).lower() for item in typed]
        if any(not owner for owner in typed_owners):
            raise RuntimeError(f"Group {group_id} has a typed possibility without an owner")
        owners.update(typed_owners)
        if len(owners) != 1:
            raise RuntimeError(f"Group {group_id} does not have one consistent owner")
        if len(group_plans) + len(typed) > 3:
            raise RuntimeError(f"Group {group_id} would exceed three possibilities")

        typed_orders = [
            int_value(item.get("cr07a_possibilityorder"), f"Typed group {group_id} order", 1, 3, 1)
            for item in typed
        ]
        planned_orders = [plan["possibility_order"] for plan in group_plans]
        if set(typed_orders).intersection(planned_orders):
            raise RuntimeError(f"Group {group_id} has a typed possibility in a legacy migration slot")
        typed_recommended = [
            item
            for item, order in zip(typed, typed_orders)
            if bool_value(item.get("cr07a_isrecommended"), default=order <= 1)
        ]
        if len(typed_recommended) > 1:
            raise RuntimeError(f"Group {group_id} already has multiple recommended typed possibilities")
        if not typed_recommended:
            min(group_plans, key=lambda plan: plan["possibility_order"])["is_recommended"] = True

        group_record = groups.get(group_key)
        if typed and group_record is None:
            raise RuntimeError(f"Group {group_id} has typed possibilities but no group record")
        if group_record is not None:
            if clean(group_record.get("cr07a_systemuserid")).lower() not in owners:
                raise RuntimeError(f"Group {group_id} belongs to another owner")
            group_record_id = normalized_id(group_record.get(ID))
            expected_group_key = record_key("group", group_id)
            if clean(group_record.get("cr07a_recordkey")).lower() != expected_group_key.lower():
                raise RuntimeError(f"Group {group_id} has an invalid record key")
            for typed_possibility in typed:
                if normalized_id(typed_possibility.get(PARENT_LOOKUP_VALUE)) != group_record_id:
                    raise RuntimeError(f"Group {group_id} has a typed possibility with another parent")
        else:
            group_record_id = ""
        expected_group_key = record_key("group", group_id)
        prior_group = planned_group_keys.get(expected_group_key.lower())
        if prior_group and prior_group != group_key:
            raise RuntimeError(f"Planned group record key collision: {expected_group_key}")
        planned_group_keys[expected_group_key.lower()] = group_key
        validate_record_key(keyed, expected_group_key, group_record_id, f"group {group_id}")

        for plan in group_plans:
            possibility_key = record_key("possibility", plan["scenario_id"])
            previous = planned_possibility_keys.get(possibility_key.lower())
            if previous and previous != plan["record_id"]:
                raise RuntimeError(f"Planned possibility record key collision: {possibility_key}")
            planned_possibility_keys[possibility_key.lower()] = plan["record_id"]
            validate_record_key(keyed, possibility_key, plan["record_id"], f"scenario {plan['scenario_id']}")

            existing_lines = lines_by_possibility.get(plan["scenario_id"].lower(), [])
            existing_by_line = unique_index(existing_lines, "cr07a_lineid", f"line id in {plan['scenario_id']}")
            plan["existing_lines"] = {
                key: {
                    "record_id": normalized_id(value.get(ID)),
                    "etag": clean(value.get("@odata.etag")),
                }
                for key, value in existing_by_line.items()
            }
            for existing in existing_lines:
                if clean(existing.get("cr07a_systemuserid")).lower() != plan["owner"].lower():
                    raise RuntimeError(f"Scenario {plan['scenario_id']} has a line owned by another user")
                if clean(existing.get("cr07a_groupid")).lower() != plan["group_id"].lower():
                    raise RuntimeError(f"Scenario {plan['scenario_id']} has a line in another group")
                if normalized_id(existing.get(PARENT_LOOKUP_VALUE)) != plan["record_id"]:
                    raise RuntimeError(f"Scenario {plan['scenario_id']} has a line with another parent")
                if not clean(existing.get("@odata.etag")):
                    raise RuntimeError(f"Scenario {plan['scenario_id']} has a line without ETag")

            for line in plan["lines"]:
                line_key = record_key("line", line["LineId"])
                prior_possibility = planned_line_keys.get(line_key.lower())
                if prior_possibility and prior_possibility != plan["scenario_id"].lower():
                    raise RuntimeError(f"Global line record key collision: {line_key}")
                planned_line_keys[line_key.lower()] = plan["scenario_id"].lower()
                existing_line = existing_by_line.get(line["LineId"].lower())
                allowed_id = normalized_id(existing_line.get(ID)) if existing_line else ""
                validate_record_key(
                    keyed, line_key, allowed_id, f"line {line['LineId']} in {plan['scenario_id']}"
                )

    print(
        f"Migration preflight: {len(plans)} legacy possibilities, "
        f"{sum(len(plan['lines']) for plan in plans)} lines, no contract conflicts",
        flush=True,
    )


def ensure_group(api: DataApi, plan: dict) -> str:
    existing = query_one(
        api,
        f"cr07a_recordtype eq {GROUP} and cr07a_groupid eq '{odata_literal(plan['group_id'])}'",
        f"{ID},cr07a_systemuserid",
    )
    if existing:
        if clean(existing.get("cr07a_systemuserid")).lower() != plan["owner"].lower():
            raise RuntimeError(f"Group {plan['group_id']} changed owner after preflight")
        return normalized_id(existing[ID])
    payload = {
        "cr07a_name": plan["group_name"][:100],
        "cr07a_recordtype": GROUP,
        "cr07a_recordkey": record_key("group", plan["group_id"]),
        "cr07a_groupid": plan["group_id"],
        "cr07a_groupname": plan["group_name"],
        "cr07a_systemuserid": plan["owner"],
        "cr07a_displayname": plan["owner_name"],
        "cr07a_email": plan["owner_email"],
    }
    created = api.request("POST", SET, payload, prefer_representation=True)
    return normalized_id(created[ID])


def line_payload(plan: dict, line: dict, group_record_id: str) -> dict:
    description = line["ProductDescription"]
    return {
        "cr07a_name": (description or f"Linea {line['LineOrder']}")[:100],
        "cr07a_recordtype": LINE,
        "cr07a_recordkey": record_key("line", line["LineId"]),
        "cr07a_groupid": plan["group_id"],
        "cr07a_possibilityid": plan["scenario_id"],
        "cr07a_lineid": line["LineId"],
        "cr07a_lineorder": line["LineOrder"],
        "cr07a_linebusinesstype": line["BusinessType"],
        "cr07a_lineproductid": line["ProductId"],
        "cr07a_lineproductdescription": description,
        "cr07a_linecostunit": decimal_payload(line["CostUnit"]),
        "cr07a_linemarginpercent": decimal_payload(line["MarginPercent"]),
        "cr07a_linecontractmonths": line["ContractMonths"],
        "cr07a_linequantity": line["Quantity"],
        "cr07a_linesuggestedprice": decimal_payload(line["SuggestedRetailPrice"]),
        "cr07a_lineaccelerator": decimal_payload(line["Acelerador"]),
        "cr07a_linehasvat": line["HasVat"],
        "cr07a_systemuserid": plan["owner"],
        "cr07a_displayname": plan["owner_name"],
        "cr07a_email": plan["owner_email"],
        "cr07a_ParentRecord@odata.bind": f"/{SET}({plan['record_id']})",
    }


def possibility_payload(plan: dict, group_record_id: str) -> dict:
    result = plan["result"]
    payload = {
        "cr07a_name": plan["possibility_name"][:100],
        "cr07a_recordkey": record_key("possibility", plan["scenario_id"]),
        "cr07a_groupid": plan["group_id"],
        "cr07a_groupname": plan["group_name"],
        "cr07a_possibilityname": plan["possibility_name"],
        "cr07a_possibilityorder": plan["possibility_order"],
        "cr07a_includeinproposal": plan["include_in_proposal"],
        "cr07a_isrecommended": plan["is_recommended"],
        "cr07a_inputhash": result["InputHash"] if result and result["InputHash"] else None,
        "cr07a_lineshash": None,
        "cr07a_resultpoints": decimal_payload(result["Points"]) if result else None,
        "cr07a_resultcommission": decimal_payload(result["Commission"]) if result else None,
        "cr07a_resultprorationdays": result["ProrationDays"] if result else None,
        "cr07a_resultprorationfactor": decimal_payload(result["ProrationFactor"]) if result else None,
        "cr07a_resultprorationtext": (
            result["ProrationText"] if result and result["ProrationText"] else None
        ),
        "cr07a_resultmonthlysale": decimal_payload(result["TotalMonthlySale"]) if result else None,
        "cr07a_resulttotalsale": decimal_payload(result["TotalSale"]) if result else None,
        "cr07a_ParentRecord@odata.bind": f"/{SET}({group_record_id})",
    }
    return payload


def query_live_lines(api: DataApi, scenario_id: str) -> dict[str, dict]:
    query = urllib.parse.urlencode({
        "$select": f"{ID},cr07a_lineid",
        "$filter": f"cr07a_recordtype eq {LINE} and cr07a_possibilityid eq '{odata_literal(scenario_id)}'",
    }, safe="$(),= '")
    return unique_index(
        api.collection(f"{SET}?{query}"), "cr07a_lineid", f"live line id in {scenario_id}"
    )


def migrate_plan(api: DataApi, plan: dict) -> tuple[int, str]:
    group_record_id = ensure_group(api, plan)
    plan["group_record_id"] = group_record_id
    updated = api.request(
        "PATCH",
        f"{SET}({plan['record_id']})",
        possibility_payload(plan, group_record_id),
        prefer_representation=True,
        if_match=plan["etag"],
    )
    commit_etag = clean((updated or {}).get("@odata.etag"))
    if not commit_etag:
        raise RuntimeError(f"Scenario {plan['scenario_id']} PATCH did not return an ETag")

    existing_lines = query_live_lines(api, plan["scenario_id"])
    live_state = {
        key: {
            "record_id": normalized_id(value.get(ID)),
            "etag": clean(value.get("@odata.etag")),
        }
        for key, value in existing_lines.items()
    }
    if live_state != plan.get("existing_lines", {}):
        raise RuntimeError(f"Scenario {plan['scenario_id']} lines changed after preflight")

    retained_line_ids: set[str] = set()
    for line in plan["lines"]:
        line_id_key = line["LineId"].lower()
        retained_line_ids.add(line_id_key)
        payload = line_payload(plan, line, group_record_id)
        existing = existing_lines.get(line_id_key)
        if existing:
            payload.pop("cr07a_systemuserid", None)
            payload.pop("cr07a_displayname", None)
            payload.pop("cr07a_email", None)
            api.request(
                "PATCH",
                f"{SET}({normalized_id(existing[ID])})",
                payload,
                if_match=clean(existing.get("@odata.etag")),
            )
        else:
            api.request("POST", SET, payload)
    for line_id_key, stale in existing_lines.items():
        if line_id_key not in retained_line_ids:
            api.request(
                "DELETE",
                f"{SET}({normalized_id(stale[ID])})",
                if_match=clean(stale.get("@odata.etag")),
            )
    api.request(
        "PATCH",
        f"{SET}({plan['record_id']})",
        {"cr07a_recordtype": POSSIBILITY, "cr07a_lineshash": plan["lines_hash"]},
        if_match=commit_etag,
    )
    return len(plan["lines"]), plan["group_id"]


def read_decimal(row: dict, field: str) -> Decimal:
    return decimal_value(row.get(field), field)


def has_decimal(row: dict, field: str, expected: Decimal) -> bool:
    return row.get(field) is not None and read_decimal(row, field) == expected


def has_integer(row: dict, field: str, expected: int) -> bool:
    value = row.get(field)
    return value is not None and not isinstance(value, bool) and int(value) == expected


def has_boolean(row: dict, field: str, expected: bool) -> bool:
    value = row.get(field)
    return isinstance(value, bool) and value == expected


def readback(api: DataApi, plans: list[dict]) -> dict:
    state = migration_state(api)
    groups = unique_index(state["groups"], "cr07a_groupid", "read-back group id")
    possibilities = unique_index(state["possibilities"], "cr07a_scenarioid", "read-back scenario id")
    lines_by_possibility = group_by(state["lines"], "cr07a_possibilityid")
    errors: list[str] = []
    verified_lines = 0

    def expect(condition: bool, message: str) -> None:
        if not condition and len(errors) < 30:
            errors.append(message)

    for plan in plans:
        scenario_id = plan["scenario_id"]
        group = groups.get(plan["group_id"].lower())
        possibility = possibilities.get(scenario_id.lower())
        expect(group is not None, f"{scenario_id}: group missing")
        expect(possibility is not None, f"{scenario_id}: typed possibility missing")
        if group is not None:
            expect(
                clean(group.get("cr07a_systemuserid")).lower() == plan["owner"].lower(),
                f"{scenario_id}: group owner mismatch",
            )
            expect(
                normalized_id(group.get(ID)) == normalized_id(plan.get("group_record_id")),
                f"{scenario_id}: group record mismatch",
            )
            expect(
                clean(group.get("cr07a_recordkey")).lower()
                == record_key("group", plan["group_id"]).lower(),
                f"{scenario_id}: group record key mismatch",
            )
        if possibility is None:
            continue
        expect(normalized_id(possibility.get(ID)) == plan["record_id"], f"{scenario_id}: record id mismatch")
        expect(clean(possibility.get("cr07a_groupid")) == plan["group_id"], f"{scenario_id}: group id mismatch")
        expect(
            clean(possibility.get("cr07a_recordkey")).lower()
            == record_key("possibility", scenario_id).lower(),
            f"{scenario_id}: possibility record key mismatch",
        )
        expect(
            has_integer(possibility, "cr07a_possibilityorder", plan["possibility_order"]),
            f"{scenario_id}: possibility order mismatch",
        )
        expect(
            clean(possibility.get("cr07a_systemuserid")).lower() == plan["owner"].lower(),
            f"{scenario_id}: owner mismatch",
        )
        expect(
            normalized_id(possibility.get(PARENT_LOOKUP_VALUE)) == normalized_id(plan.get("group_record_id")),
            f"{scenario_id}: parent mismatch",
        )
        expect(
            has_boolean(possibility, "cr07a_includeinproposal", plan["include_in_proposal"]),
            f"{scenario_id}: include flag mismatch",
        )
        expect(
            has_boolean(possibility, "cr07a_isrecommended", plan["is_recommended"]),
            f"{scenario_id}: recommended flag mismatch",
        )
        expect(clean(possibility.get("cr07a_lineshash")) == plan["lines_hash"], f"{scenario_id}: lines hash mismatch")

        result = plan["result"]
        if result is None:
            for field in (
                "cr07a_inputhash", "cr07a_resultpoints", "cr07a_resultcommission",
                "cr07a_resultprorationdays", "cr07a_resultprorationfactor",
                "cr07a_resultprorationtext", "cr07a_resultmonthlysale", "cr07a_resulttotalsale",
            ):
                expect(possibility.get(field) is None, f"{scenario_id}: {field} was not cleared")
        else:
            expect(
                clean(possibility.get("cr07a_inputhash")) == result["InputHash"],
                f"{scenario_id}: input hash mismatch",
            )
            expect(
                has_decimal(possibility, "cr07a_resultpoints", result["Points"]),
                f"{scenario_id}: points mismatch",
            )
            expect(
                has_decimal(possibility, "cr07a_resultcommission", result["Commission"]),
                f"{scenario_id}: commission mismatch",
            )
            expect(
                has_integer(possibility, "cr07a_resultprorationdays", result["ProrationDays"]),
                f"{scenario_id}: proration days mismatch",
            )
            expect(
                has_decimal(possibility, "cr07a_resultprorationfactor", result["ProrationFactor"]),
                f"{scenario_id}: proration factor mismatch",
            )
            expect(
                clean(possibility.get("cr07a_resultprorationtext")) == result["ProrationText"],
                f"{scenario_id}: proration text mismatch",
            )
            expect(
                has_decimal(possibility, "cr07a_resultmonthlysale", result["TotalMonthlySale"]),
                f"{scenario_id}: monthly sale mismatch",
            )
            expect(
                has_decimal(possibility, "cr07a_resulttotalsale", result["TotalSale"]),
                f"{scenario_id}: total sale mismatch",
            )

        actual_lines = sorted(
            lines_by_possibility.get(scenario_id.lower(), []),
            key=lambda item: int(item.get("cr07a_lineorder") or 0),
        )
        actual_by_id = unique_index(actual_lines, "cr07a_lineid", f"read-back line id in {scenario_id}")
        expected_ids = {line["LineId"].lower() for line in plan["lines"]}
        expect(set(actual_by_id) == expected_ids, f"{scenario_id}: line id set mismatch")
        for expected_line in plan["lines"]:
            actual = actual_by_id.get(expected_line["LineId"].lower())
            if actual is None:
                continue
            verified_lines += 1
            prefix = f"{scenario_id}/{expected_line['LineId']}"
            expect(clean(actual.get("cr07a_groupid")) == plan["group_id"], f"{prefix}: group mismatch")
            expect(clean(actual.get("cr07a_possibilityid")) == scenario_id, f"{prefix}: possibility mismatch")
            expect(clean(actual.get("cr07a_systemuserid")).lower() == plan["owner"].lower(), f"{prefix}: owner mismatch")
            expect(normalized_id(actual.get(PARENT_LOOKUP_VALUE)) == plan["record_id"], f"{prefix}: parent mismatch")
            expect(
                clean(actual.get("cr07a_recordkey")).lower()
                == record_key("line", expected_line["LineId"]).lower(),
                f"{prefix}: record key mismatch",
            )
            expect(has_integer(actual, "cr07a_lineorder", expected_line["LineOrder"]), f"{prefix}: order mismatch")
            expect(has_integer(actual, "cr07a_linebusinesstype", expected_line["BusinessType"]), f"{prefix}: business type mismatch")
            expect(clean(actual.get("cr07a_lineproductid")) == expected_line["ProductId"], f"{prefix}: product mismatch")
            expect(clean(actual.get("cr07a_lineproductdescription")) == expected_line["ProductDescription"], f"{prefix}: description mismatch")
            expect(has_decimal(actual, "cr07a_linecostunit", expected_line["CostUnit"]), f"{prefix}: cost mismatch")
            expect(has_decimal(actual, "cr07a_linemarginpercent", expected_line["MarginPercent"]), f"{prefix}: margin mismatch")
            expect(has_integer(actual, "cr07a_linecontractmonths", expected_line["ContractMonths"]), f"{prefix}: months mismatch")
            expect(has_integer(actual, "cr07a_linequantity", expected_line["Quantity"]), f"{prefix}: quantity mismatch")
            expect(has_decimal(actual, "cr07a_linesuggestedprice", expected_line["SuggestedRetailPrice"]), f"{prefix}: suggested price mismatch")
            expect(has_decimal(actual, "cr07a_lineaccelerator", expected_line["Acelerador"]), f"{prefix}: accelerator mismatch")
            expect(has_boolean(actual, "cr07a_linehasvat", expected_line["HasVat"]), f"{prefix}: VAT mismatch")

        actual_hash_lines = [{
            "BusinessType": int(item.get("cr07a_linebusinesstype") or 0),
            "ProductId": clean(item.get("cr07a_lineproductid")),
            "ProductDescription": clean(item.get("cr07a_lineproductdescription")),
            "CostUnit": read_decimal(item, "cr07a_linecostunit"),
            "MarginPercent": read_decimal(item, "cr07a_linemarginpercent"),
            "ContractMonths": int(item.get("cr07a_linecontractmonths") or 0),
            "Quantity": int(item.get("cr07a_linequantity") or 0),
            "SuggestedRetailPrice": read_decimal(item, "cr07a_linesuggestedprice"),
            "Acelerador": read_decimal(item, "cr07a_lineaccelerator"),
            "HasVat": bool_value(item.get("cr07a_linehasvat")),
        } for item in actual_lines]
        expect(lines_hash(actual_hash_lines) == plan["lines_hash"], f"{scenario_id}: structured line hash mismatch")

    if errors:
        raise RuntimeError(f"Migration read-back failed: {errors}")
    return {
        "verifiedGroups": len({plan["group_id"].lower() for plan in plans}),
        "verifiedMigratedPossibilities": len(plans),
        "verifiedLines": verified_lines,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="Write the migration. Default is read-only.")
    args = parser.parse_args()
    load_env()
    api = DataApi()
    verify_context(api)
    rows = legacy_rows(api)
    plans = build_migration_plans(rows)
    prevalidate_migration(api, plans)
    preview = [{
        "scenarioId": plan["scenario_id"],
        "groupId": plan["group_id"],
        "possibilityOrder": plan["possibility_order"],
        "includeInProposal": plan["include_in_proposal"],
        "isRecommended": plan["is_recommended"],
        "lineCount": len(plan["lines"]),
        "linesHash": plan["lines_hash"],
    } for plan in plans]
    print(json.dumps({"mode": "apply" if args.apply else "dry-run", "rows": preview}, indent=2))
    if not args.apply:
        return
    total_lines = 0
    for index, plan in enumerate(plans, start=1):
        line_count, group_id = migrate_plan(api, plan)
        total_lines += line_count
        print(
            f"Migrated {index}/{len(plans)}: {plan['scenario_id']} -> {group_id} ({line_count} lines)",
            flush=True,
        )
    report = readback(api, plans)
    report["migratedLines"] = total_lines
    print(json.dumps({"mode": "applied", "readback": report}, indent=2))


if __name__ == "__main__":
    main()
