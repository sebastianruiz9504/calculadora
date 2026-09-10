using System.Text.Json;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>Separate signed activity journal; never a maintenance/business-table substitute.</summary>
public static class CopiersActivityV2Bindings
{
    public const string FormVersion = "copiers-activity-v2-2026-09-10";
    public const int MovementType = 827270010;
    public const int TonerType = 827270011;
    public const string MainEntitySet = "dtc_copiersactivityv2s";
    public static CopiersMaintenanceV2DataverseOptions Create(CopiersMaintenanceV2DataverseOptions source)
    {
        var result = JsonSerializer.Deserialize<CopiersMaintenanceV2DataverseOptions>(JsonSerializer.Serialize(source))!;
        result.MainEntitySetName = MainEntitySet;
        result.MainIdField = "dtc_copiersactivityv2id";
        result.EvidenceEntitySetName = "dtc_copiersactivityevidencev2s";
        result.EvidenceIdField = "dtc_copiersactivityevidencev2id";
        result.EvidenceParentLookupLogicalName = "dtc_signedactivity";
        result.EvidenceParentNavigationProperty = "dtc_SignedActivity";
        result.MaintenanceTypeCorrectiveValue = MovementType;
        result.MaintenanceTypePreventiveValue = TonerType;
        return result;
    }
}
