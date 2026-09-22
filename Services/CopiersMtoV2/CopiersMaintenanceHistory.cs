using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>Projects one visit to its equipment rows; display and visit counts group by RecordId.</summary>
public static class CopiersMaintenanceHistory
{
    public const int HistoricalState = 827270004;
    public const int NoMailState = 827270005;
    public const int HistoricalFilePurpose = 827270004;
    public const int Completed = 645250000;
    public const int Pending = 645250001;
    public static bool IsHistorical(JsonElement row) => Number(row, "dtc_workflowstate") == HistoricalState
        && Text(row, "dtc_formversion") == "copiers-legacy-v1" && Guid.TryParse(Text(row, "dtc_legacysourcekey"), out _);
    public static bool IsVisible(JsonElement row) => IsHistorical(row) || Number(row,"dtc_workflowstate") is 827270002 or 827270003;
    public static string Text(JsonElement row,string field) => row.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() ?? "" : "";
    public static int Number(JsonElement row,string field) => row.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var result) ? result : -1;
    public static IReadOnlyList<CopiersMaintenanceRowDto> Project(JsonElement row)
    {
        if (!IsVisible(row)) return Array.Empty<CopiersMaintenanceRowDto>();
        var historical=IsHistorical(row);
        if (!DateOnly.TryParseExact(Text(row,"dtc_servicedate").Split('T')[0],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
            throw new InvalidOperationException("Un mantenimiento no tiene una fecha válida.");
        var completed=historical ? Number(row,"dtc_businessstatus")==Completed : Number(row,"dtc_workflowstate")==827270002;
        var equipment=new List<(string Id,string Serial,string Work)> { (Text(row,"_dtc_equipment_value"),Text(row,"dtc_equipmentserialsnapshot"),Text(row,"dtc_workperformed")) };
        if(!historical && Text(row,"dtc_answersjson") is {Length:>0} raw)
        {
            using var answers=JsonDocument.Parse(raw);
            foreach(var answer in answers.RootElement.EnumerateArray().Where(a=>Text(a,"key").StartsWith("equipment_item_",StringComparison.Ordinal)))
            {
                using var detail=JsonDocument.Parse(Text(answer,"value"));var item=detail.RootElement;
                var id=Text(item,"equipmentId");
                if(!Guid.TryParse(id,out _) || equipment.Any(e=>e.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("El detalle de equipos del mantenimiento no es válido.");
                equipment.Add((id,Text(item,"serial"),Text(item,"workPerformed")));
            }
        }
        return equipment.Select(e=>new CopiersMaintenanceRowDto {
            RecordId=Text(row,"dtc_copiersmtov2id"),Title=Text(row,"dtc_title"),InternalId=Text(row,"dtc_reference"),
            EquipmentId=e.Id,EquipmentSerial=string.IsNullOrWhiteSpace(e.Serial)?"Sin equipo vinculado":e.Serial,
            DateValue=date.ToString("yyyy-MM-dd"),DateDisplay=date.ToString("dd/MM/yyyy"),Description=e.Work,
            ClientId=Text(row,"_dtc_client_value"),ClientName=Text(row,"dtc_clientnamesnapshot"),
            TechnicianId=Text(row,"dtc_technicianuserkey"),TechnicianName=Text(row,"dtc_techniciannamesnapshot"),
            HasAttachment=!string.IsNullOrEmpty(Text(row,"dtc_reportevidencekey")),AttachmentFileName=Text(row,"dtc_reportfilename"),
            MaintenanceTypeValue=Number(row,"dtc_maintenancetype")==827270001?645250001:645250000,
            MaintenanceTypeLabel=Number(row,"dtc_maintenancetype")==827270001?"Preventivo":"Correctivo",
            MaintenanceStatusValue=completed?Completed:Pending,MaintenanceStatusLabel=completed?"Completado":"Pendiente",
            IsHistorical=historical,SourceLabel=historical?"Histórico migrado":"MTO V2"
        }).ToArray();
    }
}
