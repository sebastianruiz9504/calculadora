"use strict";
var DigitalTech = window.DigitalTech || {};
DigitalTech.SharedCostCenters = (function () {
    var defaults = {
        cr07a_salesperformancerecord: "101-00",
        cr07a_contratosboris: "103-00",
        cr07a_productoscopiers: "102-00"
    };
    function onLoad(executionContext) {
        var form = executionContext.getFormContext();
        var field = form.getAttribute("cr07a_centrocostoref");
        var code = defaults[form.data.entity.getEntityName()];
        if (form.ui.getFormType() !== 1 || !field || field.getValue() || !code) return;
        return Xrm.WebApi.retrieveMultipleRecords("cr07a_centrodecosto",
            "?$select=cr07a_centrodecostoid,cr07a_name&$filter=statecode eq 0 and cr07a_codigo eq '" + code + "'&$top=2"
        ).then(function (result) {
            if (field.getValue()) return; // Preserve a selection made while loading.
            if (result.entities.length !== 1) throw new Error("Missing default center");
            var center = result.entities[0];
            field.setValue([{id: center.cr07a_centrodecostoid, name: center.cr07a_name,
                entityType: "cr07a_centrodecosto"}]);
            field.fireOnChange();
            form.ui.clearFormNotification("sharedCostCenterDefault");
        }).catch(function () {
            form.ui.setFormNotification("Selecciona un centro de costo antes de guardar.",
                "WARNING", "sharedCostCenterDefault");
        });
    }
    return {onLoad: onLoad};
})();
