using System.Security.Claims;
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

public static class RhAccessPolicy
{
    private static readonly HashSet<string> AllowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "msuarez@digitaltechcolombia.com",
        "adaza@digitaltechcolombia.com",
        "sruiz@digitaltechcolombia.com"
    };

    public static bool HasAccess(string? email) =>
        !string.IsNullOrWhiteSpace(email) && AllowedEmails.Contains(email.Trim());

    public static bool HasAccess(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var candidateEmails = new[]
        {
            user.Identity?.Name,
            user.FindFirstValue("preferred_username"),
            user.FindFirstValue("upn"),
            user.FindFirstValue(ClaimTypes.Upn),
            user.FindFirstValue(ClaimTypes.Email),
            user.FindFirstValue("email")
        };

        return candidateEmails.Any(HasAccess);
    }
}
