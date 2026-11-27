using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services.Calculator;

public static partial class UpgradePricing
{
    public static void Validate(string? productId, string? description, decimal costUnit, ProductUpgrade? upgrade)
    {
        if (upgrade is null) return;

        if (string.IsNullOrWhiteSpace(productId)
            || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(upgrade.TargetProductId)
            || !string.Equals(productId.Trim(), upgrade.TargetProductId.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(upgrade.BaseProductId)
            || string.Equals(productId.Trim(), upgrade.BaseProductId.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(upgrade.BaseProductDescription))
            throw new InvalidOperationException("Selecciona una licencia base distinta del producto destino para el upgrade.");

        if (upgrade.BaseProductId.Length > 100 || upgrade.TargetProductId.Length > 100
            || upgrade.BaseProductDescription.Length > 500)
            throw new InvalidOperationException("Los datos de la licencia base superan el tamaño permitido.");

        if (!ValidPrice(upgrade.TargetCostUnit) || !ValidPrice(upgrade.BaseCostUnit)
            || !ValidPrice(upgrade.TargetSuggestedRetailPrice) || !ValidPrice(upgrade.BaseSuggestedRetailPrice)
            || upgrade.TargetCostUnit <= upgrade.BaseCostUnit)
            throw new InvalidOperationException("El costo del producto destino debe ser mayor al de la licencia base y los precios deben tener máximo cuatro decimales.");

        if (costUnit != upgrade.TargetCostUnit - upgrade.BaseCostUnit)
            throw new InvalidOperationException("El costo del upgrade debe coincidir con la diferencia entre el destino y la licencia base.");

        if (BillingPeriod(description) != BillingPeriod(upgrade.BaseProductDescription))
            throw new InvalidOperationException("Selecciona licencias con el mismo período y modalidad de facturación para el upgrade.");
    }

    private static bool ValidPrice(decimal value) =>
        value is >= 0m and <= 100_000_000_000m && decimal.Round(value, 4) == value;

    // The calculator catalog is expressed in USD. Billing terms are included in
    // its descriptions; keep these checks in sync with calculator-upgrades.js.
    public static string BillingPeriod(string? description)
    {
        var text = (description ?? "").ToLowerInvariant();
        var year = YearTerm().Match(text);
        var term = year.Success ? year.Groups[1].Value + "y"
            : Regex.IsMatch(text, @"\b(annual|annually|anual|yearly)\b") ? "1y" : "";
        var payment = text.Contains("prepaid", StringComparison.Ordinal) ? "prepaid"
            : Regex.IsMatch(text, @"\b(monthly|mensual)\b") ? "monthly" : "";
        return term + ":" + payment;
    }

    [GeneratedRegex(@"\b(\d+)\s*(?:years?|años?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearTerm();

    public static string Describe(string? description, ProductUpgrade? upgrade) => upgrade is null
        ? description?.Trim() ?? ""
        : $"Upgrade: {upgrade.BaseProductDescription.Trim()} → {description?.Trim()}";
}
