using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services.Calculator;

public sealed class QuoteCalculator : IQuoteCalculator
{
    private const decimal USD_PER_100_POINTS = 750m;
    private const decimal COP_EXCHANGE_RATE = 3800m;

    public QuoteScenarioResult Calculate(QuoteScenarioInput input, UserSegment segment)
    {
        var result = new QuoteScenarioResult();

        // 1) Prorrateo
        var (days, factor) = GetProration(input.RequiresProration, input.StartDate, input.EndDate);
        result.ProrationDays = days;
        result.ProrationFactor = factor;

        // 2) Totales visibles y utilidad oculta
        decimal totalMonthlySale = 0m;
        decimal totalSale = 0m;
        decimal utility = 0m;

        foreach (var line in input.Lines)
        {
            var saleUnit = CalculateSaleUnit(line.CostUnit, line.MarginPercent);
            var monthly = saleUnit * line.Quantity;
            var total = monthly * line.ContractMonths;

            totalMonthlySale += monthly;
            totalSale += total;

            // Utilidad oculta por línea
            var lineUtility =
                ((saleUnit - line.CostUnit) + (line.CostUnit * line.Acelerador))
                * line.Quantity
                * line.ContractMonths;

            utility += lineUtility;
        }

        result.TotalMonthlySale = RoundMoney(totalMonthlySale);
        result.TotalSale = RoundMoney(totalSale);
        result.UtilityRaw = utility;

        // 3) Ajustes a utilidad
        var adjusted = utility;

        if (segment == UserSegment.Corporate)
            adjusted *= 1.05m; // +5%

        adjusted *= DealTypeMultiplier(input.DealType);

        // 4) Prorrateo (sobre 365)
        adjusted *= factor;

        result.UtilityAdjusted = adjusted;

        // 5) Conversión utilidad → puntos
        // 750 USD utilidad anual = 100 puntos
        var points = (adjusted / 3000m) * 100m;
        result.Points = Round2(points);

        // 6) Comisión
        // 1 punto = 7.5 USD
        // Comisión USD = puntos * 7.5
        // Comisión COP = USD * 4000
        var commissionUsd = result.Points * (USD_PER_100_POINTS / 100m); // 7.5 USD por punto
        var commissionCop = commissionUsd * COP_EXCHANGE_RATE;

        result.Commission = RoundMoney(commissionCop);

        return result;
    }

    // ================= Helpers =================

    private static (int days, decimal factor) GetProration(bool requires, DateTime? start, DateTime? end)
    {
        if (!requires || start is null || end is null)
            return (0, 1m);

        var s = start.Value.Date;
        var e = end.Value.Date;

        if (e < s)
            return (0, 1m);

        var days = (e - s).Days + 1;
        var factor = days / 365m;

        return (days, factor);
    }

    private static decimal DealTypeMultiplier(DealType dealType)
    {
        return dealType switch
        {
            DealType.ClienteNuevo => 1.08m,   // +5%
            DealType.CrossSale => 1.0m,      // neutral
            DealType.Renovacion1 => 0.50m,    // queda en 40%
            DealType.Renovacion2 => 0.25m,
            DealType.Renovacion3Plus => 0.20m,
            _ => 1.00m
        };
    }

    private static decimal CalculateSaleUnit(decimal cost, decimal marginPercent)
    {
        // Venta UND = Costo UND + Margen %
        var sale = cost * (1m + (marginPercent / 100m));
        return RoundMoney(sale);
    }

    private static decimal RoundMoney(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
