using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.RH;

public static class RhModuleKeys
{
    public const string Employees = "empleados";
    public const string VacationRequests = "vacaciones";
    public const string Incapacities = "incapacidades";
}

public sealed class RhModuleDescriptor
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Description { get; init; } = "";
    public string LogicalName { get; init; } = "";
}

public static class RhModuleCatalog
{
    public static readonly IReadOnlyList<RhModuleDescriptor> All = new[]
    {
        new RhModuleDescriptor
        {
            Key = RhModuleKeys.Employees,
            Title = "Empleados",
            Subtitle = "cr07a_empleado",
            Description = "Administra informacion base del colaborador, contrato, foto y datos de compensacion.",
            LogicalName = "cr07a_empleado"
        },
        new RhModuleDescriptor
        {
            Key = RhModuleKeys.VacationRequests,
            Title = "Vacaciones",
            Subtitle = "cr07a_solicituddevacaciones",
            Description = "Gestiona solicitudes de vacaciones con fechas, cantidad de dias y empleado relacionado.",
            LogicalName = "cr07a_solicituddevacaciones"
        },
        new RhModuleDescriptor
        {
            Key = RhModuleKeys.Incapacities,
            Title = "Incapacidades",
            Subtitle = "cr07a_incapacidad",
            Description = "Registra incapacidades, motivo y el adjunto soporte correspondiente.",
            LogicalName = "cr07a_incapacidad"
        }
    };

    public static RhModuleDescriptor? Find(string? key) =>
        All.FirstOrDefault(item => string.Equals(item.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class RhHomePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public IReadOnlyList<RhModuleDescriptor> Modules { get; set; } = Array.Empty<RhModuleDescriptor>();
}

public sealed class RhTablePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public RhModuleDescriptor Module { get; set; } = new();
}

public sealed class VacationRequestPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public RhModuleDescriptor Module { get; set; } = new();
    public bool IsApprovalFlowConfigured { get; set; }
    public string ApprovalFlowConfigPath { get; set; } = "Rh:VacationApprovalFlowUrl";
    public string FormatFieldName { get; set; } = "cr07a_formato";
}

public sealed class RhTableDataResultDto
{
    public string TableKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string EmptyStateMessage { get; set; } = "";
    public IReadOnlyList<RhFieldDefinitionDto> Fields { get; set; } = Array.Empty<RhFieldDefinitionDto>();
    public IReadOnlyList<RhRecordDto> Records { get; set; } = Array.Empty<RhRecordDto>();
}

public sealed class RhFieldDefinitionDto
{
    public string LogicalName { get; set; } = "";
    public string Label { get; set; } = "";
    public string EditorType { get; set; } = "text";
    public string Placeholder { get; set; } = "";
    public string HelpText { get; set; } = "";
    public string Accept { get; set; } = "";
    public bool Required { get; set; }
    public bool ShowInList { get; set; } = true;
    public bool ShowInForm { get; set; } = true;
    public IReadOnlyList<RhOptionDto> Options { get; set; } = Array.Empty<RhOptionDto>();
}

public sealed class RhOptionDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class RhCellValueDto
{
    public string Value { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string LookupId { get; set; } = "";
    public string LookupLabel { get; set; } = "";
    public bool HasContent { get; set; }
    public string FileName { get; set; } = "";
}

public sealed class RhRecordDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public Dictionary<string, RhCellValueDto> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RhSaveRequest
{
    public string TableKey { get; set; } = "";
    public string RecordId { get; set; } = "";
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RhSaveResultDto
{
    public string Message { get; set; } = "";
    public RhRecordDto Record { get; set; } = new();
}

public sealed class RhFileUploadResultDto
{
    public string Message { get; set; } = "";
    public RhRecordDto Record { get; set; } = new();
}

public sealed class RhFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class VacationRequestContextDto
{
    public VacationEmployeeSummaryDto Employee { get; set; } = new();
    public decimal AccruedDays { get; set; }
    public decimal RegisteredDays { get; set; }
    public decimal AvailableDays { get; set; }
    public IReadOnlyList<VacationRequestHistoryDto> Requests { get; set; } = Array.Empty<VacationRequestHistoryDto>();
}

public sealed class VacationEmployeeSummaryDto
{
    public string EmployeeId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class VacationRequestHistoryDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string StartDateValue { get; set; } = "";
    public string StartDateDisplay { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string EndDateDisplay { get; set; } = "";
    public decimal RequestedDays { get; set; }
    public string Notes { get; set; } = "";
    public bool HasDocument { get; set; }
    public string DocumentFileName { get; set; } = "";
    public string CreatedOnDisplay { get; set; } = "";
}

public sealed class VacationRequestSubmitInput
{
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class VacationRequestSubmitResultDto
{
    public string Status { get; set; } = "success";
    public string Message { get; set; } = "";
    public bool FlowTriggered { get; set; }
    public string FlowMessage { get; set; } = "";
    public decimal RequestedDays { get; set; }
    public decimal AvailableDaysBefore { get; set; }
    public decimal AvailableDaysAfter { get; set; }
    public VacationRequestHistoryDto Request { get; set; } = new();
    public string DocumentUrl { get; set; } = "";
}
