namespace CotizadorInterno.Web.Models.Copiers;

public sealed class CopiersPreventiveMaintenanceBoardDto
{
    public string AsOfDateLabel { get; set; } = "";
    public string CounterPeriodLabel { get; set; } = "";
    public string PeriodFilter { get; set; } = "this-month";
    public string PeriodLabel { get; set; } = "";
    public string PeriodValue { get; set; } = "";
    public string PeriodStartValue { get; set; } = "";
    public string PeriodEndValue { get; set; } = "";
    public bool CanEditMaintenanceFrequency { get; set; }
    public int RecordsCount { get; set; }
    public IReadOnlyList<CopiersPreventiveMaintenanceClientDto> Clients { get; set; } = Array.Empty<CopiersPreventiveMaintenanceClientDto>();
}

public sealed class CopiersPreventiveMaintenanceClientDto
{
    public string ClientKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientCity { get; set; } = "";
    public int EquipmentCount { get; set; }
    public string MaintenanceFrequencyKey { get; set; } = "monthly";
    public string MaintenanceFrequencyLabel { get; set; } = "Mensual";
    public bool IsBimonthlyMaintenance { get; set; }
    public bool IsScheduledThisMonth { get; set; }
    public string ScheduledDateDisplay { get; set; } = "";
    public string MonthlyStatusLabel { get; set; } = "";
    public string MonthlyStatusTone { get; set; } = "";
    public string ScheduleButtonLabel { get; set; } = "";
    public string ScheduleButtonTone { get; set; } = "";
    public bool ScheduleButtonDisabled { get; set; }
    public int MaintenanceRegisteredCount { get; set; }
    public int PendingMaintenanceCount { get; set; }
    public int CountersRegisteredCount { get; set; }
    public int PendingCountersCount { get; set; }
    public IReadOnlyList<CopiersPreventiveMaintenanceEquipmentDto> Equipment { get; set; } = Array.Empty<CopiersPreventiveMaintenanceEquipmentDto>();
}

public sealed class CopiersPreventiveMaintenanceEquipmentDto
{
    public string RecordId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Area { get; set; } = "";
    public string Site { get; set; } = "";
    public bool HasCurrentCounter { get; set; }
    public bool HasMonthlyMaintenance { get; set; }
    public bool HasMonthlyCounter { get; set; }
    public string MaintenanceButtonLabel { get; set; } = "";
    public string MaintenanceButtonTone { get; set; } = "";
    public string CounterButtonLabel { get; set; } = "";
    public string CounterButtonTone { get; set; } = "";
    public string CounterDateValue { get; set; } = "";
    public string CounterDateDisplay { get; set; } = "";
    public long? CounterCopies { get; set; }
    public long? CounterScans { get; set; }
    public string CounterStatusLabel { get; set; } = "";
    public string CounterStatusTone { get; set; } = "";
}

public sealed class CopiersPreventiveMaintenanceScheduleRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string TimeValue { get; set; } = "";
    public int? DurationMinutes { get; set; }
}

public sealed class CopiersPreventiveMaintenanceScheduleResultDto
{
    public string Message { get; set; } = "";
    public string EventId { get; set; } = "";
    public string WebLink { get; set; } = "";
}

public sealed class CopiersPreventiveMaintenanceFrequencyUpdateRequestDto
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string FrequencyKey { get; set; } = "";
}

public sealed class CopiersPreventiveMaintenanceFrequencyUpdateResultDto
{
    public string Message { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string MaintenanceFrequencyKey { get; set; } = "monthly";
    public string MaintenanceFrequencyLabel { get; set; } = "Mensual";
    public bool IsBimonthlyMaintenance { get; set; }
}

public sealed class CopiersCounterSaveRequestDto
{
    public string EquipmentId { get; set; } = "";
    public long? CopiesCounter { get; set; }
    public long? ScansCounter { get; set; }
    public string DateValue { get; set; } = "";
}

public sealed class CopiersCounterSaveResultDto
{
    public string Message { get; set; } = "";
    public string RecordId { get; set; } = "";
}
