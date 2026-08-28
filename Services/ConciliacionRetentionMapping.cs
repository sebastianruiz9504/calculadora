using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

internal static class ConciliacionRetentionMapping
{
    internal static string ResolveAccountCode(string kind, SiigoTaxLookupDto tax, decimal rate)
    {
        if (string.Equals(kind, "RteIva", StringComparison.OrdinalIgnoreCase))
            return "13551701";

        if (string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
        {
            return tax.Id switch
            {
                4028 => "13551801",
                4030 => "13551805",
                4033 => "13551811",
                4034 => "13551813",
                _ when IsRate(rate, 11.04m) => "13551801",
                _ when IsRate(rate, 9.66m) => "13551805",
                _ when IsRate(rate, 6.9m) => "13551811",
                _ when IsRate(rate, 4.14m) => "13551813",
                _ => ""
            };
        }

        return tax.Id switch
        {
            4027 => "13551501",
            4038 => "13551513",
            4026 => "13551503",
            4024 => "13551507",
            4023 => "13551509",
            _ when IsRate(rate, 2.5m) => "13551501",
            _ when IsRate(rate, 3.5m) => "13551513",
            _ when IsRate(rate, 4m) => "13551503",
            _ when IsRate(rate, 10m) => "13551507",
            _ when IsRate(rate, 11m) => "13551509",
            _ => ""
        };
    }

    internal static SiigoTaxLookupDto? FindTax(
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        string kind,
        decimal rate)
    {
        return taxes
            .Where(tax => tax.Active
                && tax.Id > 0
                && MatchesKind(tax, kind))
            .Select(tax => new
            {
                Tax = tax,
                Difference = Math.Abs(tax.Percentage - rate)
            })
            .Where(static item => item.Difference <= 0.1m)
            .OrderBy(static item => item.Difference)
            .ThenBy(static item => item.Tax.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Tax)
            .FirstOrDefault();
    }

    internal static bool MatchesKind(SiigoTaxLookupDto tax, string kind)
    {
        var text = NormalizeTaxText($"{tax.Type} {tax.Name}");
        if (string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase))
            return text.Contains("ICA", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(kind, "RteIva", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("IVA", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("RETE", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("RETENCION", StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains("FUENTE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("RETEFTE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("RETEFUENTE", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("RETENCION", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("ICA", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("IVA", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRate(decimal value, decimal expected) =>
        Math.Abs(value - expected) <= 0.01m;

    private static string NormalizeTaxText(string value)
    {
        return (value ?? "").Trim().ToUpperInvariant()
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
    }
}
