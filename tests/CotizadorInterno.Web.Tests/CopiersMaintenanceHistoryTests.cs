using System.Text.Json;
using System.Text.Json.Nodes;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMaintenanceHistoryTests
{
    private static JsonObject Row() => new() {
        ["dtc_copiersmtov2id"] = Guid.NewGuid().ToString(), ["dtc_servicedate"] = "2025-01-01T00:00:00Z",
        ["dtc_workflowstate"] = 827270002, ["dtc_businessstatus"] = null, ["dtc_maintenancetype"] = 827270001,
        ["_dtc_equipment_value"] = Guid.NewGuid().ToString(), ["dtc_equipmentserialsnapshot"] = "Principal",
        ["dtc_answersjson"] = "[]"
    };

    [Fact]
    public void MultiEquipmentProjectsEveryEquipmentUnderOneVisitIdentity()
    {
        var row=Row(); var extra=Guid.NewGuid().ToString();
        row["dtc_answersjson"]=JsonSerializer.Serialize(new[]{new {key="equipment_item_2",value=JsonSerializer.Serialize(new {equipmentId=extra,serial="Segundo",workPerformed="Limpieza"})}});
        var result=CopiersMaintenanceHistory.Project(JsonSerializer.SerializeToElement(row));
        Assert.Equal(2,result.Count); Assert.Single(result.Select(x=>x.RecordId).Distinct());
        Assert.Contains(result,x=>x.EquipmentId==extra && x.Description=="Limpieza");
        Assert.All(result,x=>Assert.Equal(CopiersMaintenanceHistory.Completed,x.MaintenanceStatusValue));
    }

    [Theory]
    [InlineData(645250000)]
    [InlineData(645250001)]
    public void HistoryPreservesBusinessStatusDateAndMissingEquipment(int status)
    {
        var row=Row(); row["dtc_workflowstate"]=CopiersMaintenanceHistory.HistoricalState;
        row["dtc_formversion"]="copiers-legacy-v1";row["dtc_legacysourcekey"]=Guid.NewGuid().ToString();
        row["dtc_businessstatus"]=status;row["_dtc_equipment_value"]=null;row["dtc_equipmentserialsnapshot"]=null;
        var result=Assert.Single(CopiersMaintenanceHistory.Project(JsonSerializer.SerializeToElement(row)));
        Assert.True(result.IsHistorical);Assert.Equal(status,result.MaintenanceStatusValue);
        Assert.Equal("2025-01-01",result.DateValue);Assert.Empty(result.EquipmentId);
    }

    [Fact]
    public void FailedNativeReportIsNeverCountedAsCompletedAndDraftIsHidden()
    {
        var row=Row();row["dtc_workflowstate"]=827270003;
        Assert.Equal(CopiersMaintenanceHistory.Pending,Assert.Single(CopiersMaintenanceHistory.Project(JsonSerializer.SerializeToElement(row))).MaintenanceStatusValue);
        row["dtc_workflowstate"]=827270000;
        Assert.Empty(CopiersMaintenanceHistory.Project(JsonSerializer.SerializeToElement(row)));
    }
}
