namespace CotizadorInterno.Web.Models.Dashboard;

public sealed class CopiersCountersDashboardDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodValue { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string AsOfDateLabel { get; set; } = "";
    public string FocusLabel { get; set; } = "";
    public string SelectedClientId { get; set; } = "";
    public string SelectedClientName { get; set; } = "";
    public bool HasData { get; set; }
    public bool CanExport { get; set; } = true;
    public int RecordsCount { get; set; }
    public string EmptyStateTitle { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<PortfolioKpiDto> Kpis { get; set; } = Array.Empty<PortfolioKpiDto>();
    public IReadOnlyList<CopiersCountersClientOptionDto> Clients { get; set; } = Array.Empty<CopiersCountersClientOptionDto>();
    public IReadOnlyList<CopiersCountersClientSummaryDto> ClientSummaries { get; set; } = Array.Empty<CopiersCountersClientSummaryDto>();
    public IReadOnlyList<CopiersCountersEquipmentRowDto> EquipmentRows { get; set; } = Array.Empty<CopiersCountersEquipmentRowDto>();
    public IReadOnlyList<CopiersCountersExportBlockerDto> ExportBlockers { get; set; } = Array.Empty<CopiersCountersExportBlockerDto>();
}

public sealed class CopiersCountersClientOptionDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class CopiersCountersClientSummaryDto
{
    public string GroupId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public long TotalCopies { get; set; }
    public long TotalScans { get; set; }
    public long TotalConsumption { get; set; }
    public decimal IncludedOperations { get; set; }
    public decimal UnitExcessCost { get; set; }
    public long ExcessQuantity { get; set; }
    public decimal ExcessTotal { get; set; }
    public int EquipmentWithConsumption { get; set; }
    public string AssignmentModeLabel { get; set; } = "";
    public string ValidationSummary { get; set; } = "";
}

public sealed class CopiersCountersEquipmentRowDto
{
    public string EquipmentId { get; set; } = "";
    public string EquipmentName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public string ProductLineId { get; set; } = "";
    public string ProductLineName { get; set; } = "";
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public bool IsBackup { get; set; }
    public string AssignmentStatus { get; set; } = "";
    public decimal IncludedOperations { get; set; }
    public decimal UnitExcessCost { get; set; }
    public long ExcessQuantity { get; set; }
    public decimal ExcessTotal { get; set; }
    public string CurrentDateValue { get; set; } = "";
    public string CurrentDateDisplay { get; set; } = "";
    public string PreviousDateValue { get; set; } = "";
    public string PreviousDateDisplay { get; set; } = "";
    public long? CurrentCopiesCounter { get; set; }
    public long? PreviousCopiesCounter { get; set; }
    public long? CopiesConsumption { get; set; }
    public long? CurrentScansCounter { get; set; }
    public long? PreviousScansCounter { get; set; }
    public long? ScansConsumption { get; set; }
    public int? DaysBetweenReadings { get; set; }
    public long TotalConsumption { get; set; }
    public bool HasCurrentCounter { get; set; }
}

public sealed class CopiersCountersExportBlockerDto
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "error";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int BillingDay { get; set; }
    public string BillingDayDisplay { get; set; } = "";
    public string EquipmentId { get; set; } = "";
    public string EquipmentName { get; set; } = "";
    public string Message { get; set; } = "";
}
