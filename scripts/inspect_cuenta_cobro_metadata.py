"""Read-only inspection of the canonical expense table metadata."""

import json
import os
import urllib.request

from auth import get_client, get_plugin_headers, get_token, load_env


TABLE = "cr07a_gastodelaempresa"
SOLUTION = "CotizadorInternoCRM"
REQUIRED_COLUMNS = {
    "cr07a_excelkey",
    "cr07a_cuentacontablecodigo",
    "cr07a_cuentacontablenombre",
    "cr07a_estadoautomatizacion",
    "cr07a_motivorevision",
    "cr07a_retencionesjson",
    "cr07a_iva",
    "cr07a_siigodocumentid",
    "cr07a_siigodocumentname",
    "cr07a_siigopaymentid",
    "cr07a_siigopaymentname",
    "cr07a_siigorespuesta",
    "cr07a_siigopaymentresponse",
}


def flatten(pages):
    return [item for page in pages for item in page]


def web_api_get(relative_url):
    load_env()
    base_url = os.environ["DATAVERSE_URL"].rstrip("/")
    token = get_token()
    headers = get_plugin_headers("dv-metadata", token)
    headers.update({
        "Accept": "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
    })
    request = urllib.request.Request(
        f"{base_url}/api/data/v9.2/{relative_url}",
        headers=headers,
    )
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read())


def main():
    client = get_client("dv-metadata")
    metadata = client.tables.get(TABLE)
    if not metadata:
        raise RuntimeError(f"Table not found: {TABLE}")

    solutions = flatten(client.records.get(
        "solution",
        filter=f"uniquename eq '{SOLUTION}'",
        select=[
            "solutionid",
            "uniquename",
            "friendlyname",
            "version",
            "_publisherid_value",
        ],
        top=1,
    ))
    if len(solutions) != 1:
        raise RuntimeError(f"Expected one solution named {SOLUTION}")

    solution = solutions[0]
    publisher_id = solution.get("_publisherid_value")
    publisher = client.records.get("publisher", publisher_id)

    table = web_api_get(
        "EntityDefinitions(LogicalName='cr07a_gastodelaempresa')"
        "?$select=MetadataId,LogicalName,SchemaName,EntitySetName"
    )
    attributes_payload = web_api_get(
        "EntityDefinitions(LogicalName='cr07a_gastodelaempresa')/Attributes"
        "?$select=MetadataId,LogicalName,SchemaName,AttributeType,AttributeTypeName"
    )
    keys_payload = web_api_get(
        "EntityDefinitions(LogicalName='cr07a_gastodelaempresa')/Keys"
        "?$select=MetadataId,LogicalName,SchemaName,EntityKeyIndexStatus,KeyAttributes"
    )

    attributes = {
        attribute["LogicalName"]: attribute
        for attribute in attributes_payload.get("value", [])
        if attribute.get("LogicalName") in REQUIRED_COLUMNS
    }
    typed_details = {}
    type_selects = {
        "String": (
            "Microsoft.Dynamics.CRM.StringAttributeMetadata",
            "MetadataId,LogicalName,SchemaName,MaxLength",
        ),
        "Memo": (
            "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
            "MetadataId,LogicalName,SchemaName,MaxLength",
        ),
        "Decimal": (
            "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
            "MetadataId,LogicalName,SchemaName,Precision,MinValue,MaxValue",
        ),
        "Money": (
            "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
            "MetadataId,LogicalName,SchemaName,Precision,PrecisionSource,MinValue,MaxValue",
        ),
    }
    for logical_name, attribute in attributes.items():
        attribute_type = attribute.get("AttributeType")
        if attribute_type not in type_selects:
            continue
        cast, selected_properties = type_selects[attribute_type]
        typed_details[logical_name] = web_api_get(
            "EntityDefinitions(LogicalName='cr07a_gastodelaempresa')"
            f"/Attributes(LogicalName='{logical_name}')/{cast}"
            f"?$select={selected_properties}"
        )

    report = {
        "sdk_table": metadata,
        "solution": {
            "solutionid": solution.get("solutionid"),
            "uniquename": solution.get("uniquename"),
            "friendlyname": solution.get("friendlyname"),
            "version": solution.get("version"),
            "publisherid": publisher_id,
        },
        "publisher": {
            "publisherid": publisher.get("publisherid"),
            "uniquename": publisher.get("uniquename"),
            "friendlyname": publisher.get("friendlyname"),
            "customizationprefix": publisher.get("customizationprefix"),
        },
        "table": table,
        "required_attributes": attributes,
        "typed_details": typed_details,
        "missing_attributes": sorted(REQUIRED_COLUMNS - attributes.keys()),
        "keys": keys_payload.get("value", []),
    }
    print(json.dumps(report, indent=2, ensure_ascii=True, default=str))


if __name__ == "__main__":
    main()
