"""Additive equipment custody metadata; default is a read-only plan.

Reuse the authenticated CLI with an explicit environment, without switching the
global PAC/CLI profile. Never writes business rows, creates a solution or publisher.
After --apply, export/unpack CopiersMtoFirmadoV2 before releasing the application.
"""
import argparse
import json
import provision_copiers_activity_v2 as metadata

FIELDS = {
    "cr07a_equipo": metadata.base.memo_column("dtc_transitjson", "dtc_TransitJson", "Custodia de equipo en tr\u00e1nsito", 4000,
        "Uso interno. Salida pendiente con responsable, origen, destino previsto y fecha. Se limpia al confirmar recepci\u00f3n."),
    "cr07a_movimientosequipos": metadata.base.memo_column("dtc_operationjson", "dtc_OperationJson", "Operaci\u00f3n de equipo", 12000,
        "Uso interno. Instant\u00e1nea inmutable de la operaci\u00f3n f\u00edsica y v\u00ednculo entre salida, recepci\u00f3n y certificado."),
}

def plan():
    solutions = metadata.rows("solutions?$select=solutionid,uniquename,_publisherid_value&$filter=uniquename eq 'CopiersMtoFirmadoV2'")
    if len(solutions) != 1:
        raise RuntimeError("The approved existing solution is not unique")
    publisher = metadata.api("GET", f"publishers({solutions[0]['_publisherid_value']})?$select=customizationprefix")
    if publisher.get("customizationprefix") != "dtc":
        raise RuntimeError("Unexpected publisher")
    missing = []
    for table, spec in FIELDS.items():
        entity = metadata.api("GET", f"EntityDefinitions(LogicalName='{table}')?$select=MetadataId,LogicalName,IsOptimisticConcurrencyEnabled")
        if table == "cr07a_equipo" and entity.get("IsOptimisticConcurrencyEnabled") is not True:
            raise RuntimeError("Equipment concurrency is not enabled; no schema mutation attempted")
        rows = metadata.rows(f"EntityDefinitions(LogicalName='{table}')/Attributes/Microsoft.Dynamics.CRM.MemoAttributeMetadata?$select=LogicalName,SchemaName,MaxLength,IsSecured&$filter=LogicalName eq '{spec.logical_name}'")
        if not rows:
            missing.append((table, spec))
        elif len(rows) != 1 or rows[0].get("SchemaName") != spec.schema_name or rows[0].get("MaxLength") != spec.payload["MaxLength"] or rows[0].get("IsSecured") is not False:
            raise RuntimeError("Incompatible existing column " + table + "." + spec.logical_name)
    return missing

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    missing = plan()
    actions = [table + "." + spec.logical_name for table, spec in missing]
    if args.apply:
        for table, spec in missing:
            metadata.api("POST", f"EntityDefinitions(LogicalName='{table}')/Attributes", spec.payload)
        if missing:
            entities = "".join("<entity>" + table + "</entity>" for table in FIELDS)
            metadata.api("POST", "PublishXml", {"ParameterXml": "<importexportxml><entities>" + entities + "</entities></importexportxml>"})
        if plan():
            raise RuntimeError("Schema read-back failed")
        solution = metadata.rows("solutions?$select=solutionid&$filter=uniquename eq 'CopiersMtoFirmadoV2'")[0]["solutionid"]
        for table, spec in FIELDS.items():
            attribute = metadata.api("GET", f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{spec.logical_name}')?$select=MetadataId")
            component = metadata.rows(f"solutioncomponents?$select=objectid,componenttype&$filter=_solutionid_value eq {solution} and objectid eq {attribute['MetadataId']} and componenttype eq 2")
            entity = metadata.api("GET", f"EntityDefinitions(LogicalName='{table}')?$select=MetadataId")
            roots = metadata.rows(f"solutioncomponents?$select=objectid,rootcomponentbehavior&$filter=_solutionid_value eq {solution} and objectid eq {entity['MetadataId']} and componenttype eq 1")
            # Full-table roots include their attributes implicitly: there need not
            # be an individual solutioncomponent row. Confirm again in exported XML.
            if len(roots) == 1 and roots[0].get("rootcomponentbehavior") == 0:
                continue
            if not component:
                metadata.api("POST", "AddSolutionComponent", {"ComponentId": attribute["MetadataId"], "ComponentType": 2,
                    "SolutionUniqueName": metadata.SOLUTION, "AddRequiredComponents": False})
                component = metadata.rows(f"solutioncomponents?$select=objectid,componenttype&$filter=_solutionid_value eq {solution} and objectid eq {attribute['MetadataId']} and componenttype eq 2")
            if len(component) != 1:
                raise RuntimeError("Column not included uniquely in approved solution: " + table + "." + spec.logical_name)
    print(json.dumps({"environment": metadata.ENV, "solution": metadata.SOLUTION, "applied": args.apply, "actions": actions, "businessWrites": 0}, ensure_ascii=False))

if __name__ == "__main__":
    main()
