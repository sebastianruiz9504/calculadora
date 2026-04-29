using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Envios;

public sealed class EnviosPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int InitialYear { get; set; }
    public int InitialMonth { get; set; }
    public string CurrentUserLabel { get; set; } = "";
}

public sealed class EnvioOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class EnvioCalendarDayDto
{
    public int DayNumber { get; set; }
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public int ScheduledCount { get; set; }
}

public sealed class EnvioRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string WhatIsSent { get; set; } = "";
    public string Observations { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
    public int StatusValue { get; set; }
    public string StatusLabel { get; set; } = "";
    public string RequestDateValue { get; set; } = "";
    public string RequestDateDisplay { get; set; } = "";
    public string ScheduledAtValue { get; set; } = "";
    public string ScheduledAtDisplay { get; set; } = "";
    public string TransporterId { get; set; } = "";
    public string TransporterName { get; set; } = "";
    public decimal FreightValue { get; set; }
    public bool PickupApproved { get; set; }
    public string PickupApprovedAtDisplay { get; set; } = "";
    public string PickupApprovedByName { get; set; } = "";
    public string DeliveryConfirmedAtDisplay { get; set; } = "";
    public string DeliveredByName { get; set; } = "";
    public bool ReceivedSatisfied { get; set; }
    public string ReceivedSatisfiedAtDisplay { get; set; } = "";
    public string ReceivedSatisfiedByName { get; set; } = "";
    public bool HasDeliveryAct { get; set; }
    public string DeliveryActFileName { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class EnviosBoardDto
{
    public string Mode { get; set; } = "";
    public int SelectedYear { get; set; }
    public int SelectedMonth { get; set; }
    public string SelectedMonthValue { get; set; } = "";
    public string SelectedMonthLabel { get; set; } = "";
    public string Message { get; set; } = "";
    public int TotalRecords { get; set; }
    public int OpenCount { get; set; }
    public int ScheduledCount { get; set; }
    public int PickupApprovedCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ClosedCount { get; set; }
    public decimal TotalFreightValue { get; set; }
    public IReadOnlyList<EnvioCalendarDayDto> CalendarDays { get; set; } = Array.Empty<EnvioCalendarDayDto>();
    public IReadOnlyList<EnvioRowDto> Records { get; set; } = Array.Empty<EnvioRowDto>();
    public IReadOnlyList<EnvioOptionDto> StatusOptions { get; set; } = Array.Empty<EnvioOptionDto>();
}

public sealed class EnvioCreateRequest
{
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string WhatIsSent { get; set; } = "";
    public string Observations { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
}

public sealed class EnvioScheduleRequest
{
    public string RecordId { get; set; } = "";
    public string ScheduledAtValue { get; set; } = "";
    public decimal FreightValue { get; set; }
}

public sealed class EnvioRecordActionRequest
{
    public string RecordId { get; set; } = "";
}

public sealed class EnvioSaveResultDto
{
    public string Message { get; set; } = "";
    public EnvioRowDto Record { get; set; } = new();
}

public sealed class EnvioFileUploadResultDto
{
    public string Message { get; set; } = "";
    public EnvioRowDto Record { get; set; } = new();
}

public sealed class EnvioFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
