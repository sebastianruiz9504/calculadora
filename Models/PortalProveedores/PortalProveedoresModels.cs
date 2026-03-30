using System.Globalization;
using System.Security.Claims;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.PortalProveedores;

public enum SupplierCertificateType
{
    ReteFuente = 1,
    ReteIca = 2
}

public static class SupplierCertificateTypeExtensions
{
    public static string ToKey(this SupplierCertificateType value) => value switch
    {
        SupplierCertificateType.ReteFuente => "retefuente",
        SupplierCertificateType.ReteIca => "reteica",
        _ => ""
    };

    public static string ToLabel(this SupplierCertificateType value) => value switch
    {
        SupplierCertificateType.ReteFuente => "Rete fuente",
        SupplierCertificateType.ReteIca => "Rete ICA",
        _ => ""
    };

    public static IReadOnlyList<SupplierCertificateType> ParseMany(IEnumerable<string>? values)
    {
        if (values is null)
            return Array.Empty<SupplierCertificateType>();

        return values
            .Select(ParseSingle)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
    }

    public static string ToSummaryLabel(this IReadOnlyCollection<SupplierCertificateType> values)
    {
        if (values is null || values.Count == 0)
            return "Sin tipo";

        return string.Join(" y ", values.Select(ToLabel));
    }

    private static SupplierCertificateType? ParseSingle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "retefuente" or "rete-fuente" or "rete_fuente" => SupplierCertificateType.ReteFuente,
            "reteica" or "rete-ica" or "rete_ica" => SupplierCertificateType.ReteIca,
            _ => null
        };
    }
}

public sealed class SupplierPortalPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string CompanyNit { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyCity { get; set; } = "";
    public bool IsRequestFlowConfigured { get; set; }
    public string RequestFlowConfigPath { get; set; } = "SupplierPortal:CertificateRequestFlowUrl";
}

public sealed class SupplierCertificateRequestInput
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class SupplierProviderLookupItem
{
    public string Nit { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class SupplierCertificateQuery
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SupplierNit { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public IReadOnlyList<SupplierCertificateType> CertificateTypes { get; set; } = Array.Empty<SupplierCertificateType>();
}

public sealed class SupplierCertificateRecordDto
{
    public string RecordId { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string SupplierNit { get; set; } = "";
    public string ExpenseDateValue { get; set; } = "";
    public string ExpenseDateDisplay { get; set; } = "";
    public decimal TotalInvoices { get; set; }
    public decimal TotalBase { get; set; }
    public decimal TotalReteFuente { get; set; }
    public decimal TotalReteIca { get; set; }
}

public sealed class SupplierCertificateSummaryDto
{
    public string SupplierName { get; set; } = "";
    public string SupplierNit { get; set; } = "";
    public string PeriodStartValue { get; set; } = "";
    public string PeriodEndValue { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public IReadOnlyList<SupplierCertificateType> CertificateTypes { get; set; } = Array.Empty<SupplierCertificateType>();
    public string CertificateTypesLabel { get; set; } = "";
    public int RecordsCount { get; set; }
    public decimal TotalInvoices { get; set; }
    public decimal TotalBase { get; set; }
    public decimal TotalReteFuente { get; set; }
    public decimal TotalReteIca { get; set; }
    public IReadOnlyList<SupplierCertificateRecordDto> Records { get; set; } = Array.Empty<SupplierCertificateRecordDto>();
}

public sealed class SupplierCertificateDocumentViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string CompanyNit { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyCity { get; set; } = "";
    public string IssueDateDisplay { get; set; } = DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public bool AutoPrint { get; set; }
    public SupplierCertificateSummaryDto Certificate { get; set; } = new();
}

public static class PortalProveedoresAccessPolicy
{
    private static readonly HashSet<string> AllowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "sruiz@digitaltechcolombia.com",
        "msuarez@digitaltechcolombia.com",
        "adaza@digitaltechcolombia.com"
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
