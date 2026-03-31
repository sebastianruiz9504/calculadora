using System.Security.Claims;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Metricas;

public enum MetricsRangeFilter
{
    ThisMonth = 0,
    ThisYear = 1,
    PreviousYear = 2
}

public static class MetricsRangeFilterExtensions
{
    public static MetricsRangeFilter ParseOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MetricsRangeFilter.ThisYear;

        return value.Trim().ToLowerInvariant() switch
        {
            "thismonth" or "this-month" or "este-mes" => MetricsRangeFilter.ThisMonth,
            "thisyear" or "this-year" or "este-ano" or "este-a\u00f1o" => MetricsRangeFilter.ThisYear,
            "previousyear" or "previous-year" or "ano-pasado" or "a\u00f1o-pasado" => MetricsRangeFilter.PreviousYear,
            _ => MetricsRangeFilter.ThisYear
        };
    }

    public static string ToKey(this MetricsRangeFilter value) => value switch
    {
        MetricsRangeFilter.ThisMonth => "this-month",
        MetricsRangeFilter.ThisYear => "this-year",
        MetricsRangeFilter.PreviousYear => "previous-year",
        _ => "this-year"
    };

    public static string ToLabel(this MetricsRangeFilter value) => value switch
    {
        MetricsRangeFilter.ThisMonth => "Este mes",
        MetricsRangeFilter.ThisYear => "Este a\u00f1o",
        MetricsRangeFilter.PreviousYear => "A\u00f1o pasado",
        _ => "Este a\u00f1o"
    };
}

public sealed class MetricasPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public MetricsRangeFilter InitialFilter { get; set; } = MetricsRangeFilter.ThisYear;
}

public sealed class MetricsDashboardDto
{
    public string Filter { get; set; } = MetricsRangeFilter.ThisYear.ToKey();
    public string FilterLabel { get; set; } = MetricsRangeFilter.ThisYear.ToLabel();
    public string GranularityLabel { get; set; } = "Mensual";
    public string AppliedSellerKey { get; set; } = "";
    public string AppliedSellerName { get; set; } = "Todos los vendedores";
    public int RecordsCount { get; set; }
    public int SellersCount { get; set; }
    public int VerticalsCount { get; set; }
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<MetricsSellerOptionDto> Sellers { get; set; } = Array.Empty<MetricsSellerOptionDto>();
    public IReadOnlyList<MetricsChartDto> Charts { get; set; } = Array.Empty<MetricsChartDto>();
}

public sealed class MetricsSellerOptionDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class MetricsChartDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string EmptyMessage { get; set; } = "No hay datos para este grafico.";
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<MetricsSeriesDto> Series { get; set; } = Array.Empty<MetricsSeriesDto>();
}

public sealed class MetricsSeriesDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public decimal TotalScore { get; set; }
    public decimal TotalAnnualValue { get; set; }
    public IReadOnlyList<decimal> Values { get; set; } = Array.Empty<decimal>();
    public IReadOnlyList<decimal> AnnualValues { get; set; } = Array.Empty<decimal>();
}

public static class MetricasAccessPolicy
{
    private static readonly HashSet<string> AllowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "sruiz@digitaltechcolombia.com",
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
