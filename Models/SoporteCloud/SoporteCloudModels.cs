using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.SoporteCloud;

public sealed class SoporteCloudPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string InitialStartDate { get; set; } = "";
    public string InitialEndDate { get; set; } = "";
}

public sealed class SoporteCloudOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class SoporteCloudTicketRowDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreationDateValue { get; set; } = "";
    public string CreationDateDisplay { get; set; } = "";
    public int? StateValue { get; set; }
    public string StateLabel { get; set; } = "";
    public int? TypeValue { get; set; }
    public string TypeLabel { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public string CategoryLabel { get; set; } = "";
    public string CreatorId { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public decimal HoursTaken { get; set; }
    public int? MethodValue { get; set; }
    public string MethodLabel { get; set; } = "";
    public string Solution { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string AttachmentFileName { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class SoporteCloudCreatorSummaryDto
{
    public string CreatorId { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class SoporteCloudBreakdownDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class SoporteCloudBoardDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalCreators { get; set; }
    public int TotalClients { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<SoporteCloudTicketRowDto> Records { get; set; } = Array.Empty<SoporteCloudTicketRowDto>();
    public IReadOnlyList<SoporteCloudCreatorSummaryDto> CreatorSummaries { get; set; } = Array.Empty<SoporteCloudCreatorSummaryDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> TypeBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> MethodBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> CategoryBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudOptionDto> StateOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> TypeOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> CategoryOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> MethodOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
}

public sealed class SoporteCloudTrainingRowDto
{
    public string RecordId { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public decimal DurationMinutes { get; set; }
    public decimal DurationHours { get; set; }
    public string DurationDisplay { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int Attendees { get; set; }
    public int? TopicValue { get; set; }
    public string TopicLabel { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
}

public sealed class SoporteCloudTrainingOwnerSummaryDto
{
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
}

public sealed class SoporteCloudTrainingBreakdownDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class SoporteCloudTrainingTimePointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
}

public sealed class SoporteCloudTrainingsBoardDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHoursDelivered { get; set; }
    public int TotalClients { get; set; }
    public int TotalAttendees { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<SoporteCloudTrainingRowDto> Records { get; set; } = Array.Empty<SoporteCloudTrainingRowDto>();
    public IReadOnlyList<SoporteCloudTrainingOwnerSummaryDto> OwnerSummaries { get; set; } = Array.Empty<SoporteCloudTrainingOwnerSummaryDto>();
    public IReadOnlyList<SoporteCloudTrainingBreakdownDto> TopicBreakdowns { get; set; } = Array.Empty<SoporteCloudTrainingBreakdownDto>();
    public IReadOnlyList<SoporteCloudTrainingBreakdownDto> ClientBreakdowns { get; set; } = Array.Empty<SoporteCloudTrainingBreakdownDto>();
    public IReadOnlyList<SoporteCloudTrainingTimePointDto> TimeSeries { get; set; } = Array.Empty<SoporteCloudTrainingTimePointDto>();
    public IReadOnlyList<SoporteCloudOptionDto> TopicOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
}

public sealed class SoporteCloudSaveRequest
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreationDateValue { get; set; } = "";
    public int? StateValue { get; set; }
    public int? TypeValue { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public decimal HoursTaken { get; set; }
    public int? MethodValue { get; set; }
    public string Solution { get; set; } = "";
}

public sealed class SoporteCloudSaveResultDto
{
    public string Message { get; set; } = "";
    public SoporteCloudTicketRowDto Record { get; set; } = new();
}

public sealed class SoporteCloudFileUploadResultDto
{
    public string Message { get; set; } = "";
    public SoporteCloudTicketRowDto Record { get; set; } = new();
}

public sealed class SoporteCloudFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
