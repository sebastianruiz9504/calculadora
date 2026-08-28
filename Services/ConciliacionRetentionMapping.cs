using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

internal sealed record ClientPaymentRetentionDefinition(
    string Kind,
    int TaxId,
    decimal CatalogRate,
    decimal EffectiveRate,
    string AccountCode);

internal static class ConciliacionRetentionMapping
{
    private const string ReteFuenteKind = "ReteFuente";
    private const string ReteIcaKind = "ReteIca";
    private const string RteIvaKind = "RteIva";

    private static readonly IReadOnlyList<ClientPaymentRetentionDefinition> ClientPaymentDefinitions =
    [
        new(ReteFuenteKind, 4027, 2.5m, 2.5m, "13551501"),
        new(ReteFuenteKind, 4038, 3.5m, 3.5m, "13551513"),
        new(ReteFuenteKind, 4026, 4m, 4m, "13551503"),
        new(ReteFuenteKind, 4024, 10m, 10m, "13551507"),
        new(ReteFuenteKind, 4023, 11m, 11m, "13551509"),
        new(ReteIcaKind, 4034, 4.14m, 4.14m, "13551813"),
        new(ReteIcaKind, 4033, 6.9m, 6.9m, "13551811"),
        // Siigo exposes tax 4031 as "ReteICA 8"/8.0, but the approved
        // accounting configuration applies 8.66 per thousand.
        new(ReteIcaKind, 4031, 8m, 8.66m, "13551807"),
        new(ReteIcaKind, 4030, 9.66m, 9.66m, "13551805"),
        new(ReteIcaKind, 4028, 11.04m, 11.04m, "13551801")
    ];

    internal static string ResolveAccountCode(string kind, SiigoTaxLookupDto tax, decimal rate)
    {
        if (string.Equals(NormalizeClientPaymentKind(kind), RteIvaKind, StringComparison.Ordinal))
            return "13551701";

        return FindClientPaymentDefinition(kind, tax.Id)?.AccountCode ?? "";
    }

    internal static ClientPaymentRetentionDefinition? ResolveClientPaymentDefinition(
        string kind,
        SiigoTaxLookupDto tax)
    {
        var definition = FindClientPaymentDefinition(kind, tax.Id);
        if (definition is null
            || !MatchesKind(tax, kind)
            || !IsRate(tax.Percentage, definition.CatalogRate))
        {
            return null;
        }

        return definition;
    }

    internal static ClientPaymentRetentionDefinition? FindClientPaymentDefinition(
        string kind,
        int taxId)
    {
        var normalizedKind = NormalizeClientPaymentKind(kind);
        return ClientPaymentDefinitions.FirstOrDefault(definition =>
            definition.TaxId == taxId
            && string.Equals(definition.Kind, normalizedKind, StringComparison.Ordinal));
    }

    internal static SiigoTaxLookupDto? FindClientPaymentTax(
        IReadOnlyList<SiigoTaxLookupDto> taxes,
        string kind,
        decimal rate)
    {
        if (string.Equals(NormalizeClientPaymentKind(kind), RteIvaKind, StringComparison.Ordinal))
            return FindTax(taxes, kind, rate);

        return taxes
            .Where(static tax => tax.Active && tax.Id > 0 && tax.Percentage > 0m)
            .Select(tax => new
            {
                Tax = tax,
                Definition = ResolveClientPaymentDefinition(kind, tax)
            })
            .Where(static item => item.Definition is not null)
            .Select(item => new
            {
                item.Tax,
                Difference = Math.Abs(item.Definition!.EffectiveRate - rate)
            })
            .Where(static item => item.Difference <= 0.1m)
            .OrderBy(static item => item.Difference)
            .ThenBy(static item => item.Tax.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Tax)
            .FirstOrDefault();
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

    private static string NormalizeClientPaymentKind(string kind)
    {
        if (string.Equals(kind, "ReteIca", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase))
        {
            return ReteIcaKind;
        }

        if (string.Equals(kind, "RteIva", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteIva", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteIVA", StringComparison.OrdinalIgnoreCase))
        {
            return RteIvaKind;
        }

        if (string.Equals(kind, "RteFte", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteFte", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "ReteFuente", StringComparison.OrdinalIgnoreCase))
        {
            return ReteFuenteKind;
        }

        return (kind ?? "").Trim();
    }

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
