using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services.Calculator;

public sealed class QuoteCalculator : IQuoteCalculator
{
    private const decimal USD_PER_100_POINTS = 900m;
    private const decimal COP_EXCHANGE_RATE = 4000m;

    public QuoteScenarioResult Calculate(QuoteScenarioInput input)
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
            var acceleratorFactor = NormalizeAccelerator(line.Acelerador);
            var monthly = saleUnit * line.Quantity;
            var total = monthly * line.ContractMonths;

            totalMonthlySale += monthly;
            totalSale += total;

            // Utilidad oculta por línea
            var lineUtility =
                ((saleUnit - line.CostUnit) + (line.CostUnit * acceleratorFactor))
                * line.Quantity
                * line.ContractMonths;

            utility += lineUtility;
        }

        result.TotalMonthlySale = RoundMoney(totalMonthlySale);
        result.TotalSale = RoundMoney(totalSale);
        result.UtilityRaw = utility;

        // 3) Ajustes a utilidad
        var adjusted = utility;

        adjusted *= DealTypeMultiplier(input.DealType);

        // 4) Prorrateo (sobre 365)
        adjusted *= factor;

        result.UtilityAdjusted = adjusted;

        // 5) Conversión utilidad → puntos
        // 3.000 USD de utilidad ajustada = 100 puntos
        var points = (adjusted / 3000m) * 100m;
        result.Points = Round2(points);

        // 6) Comisión
        // 1 punto = 9 USD, equivalente al 30% de la utilidad ajustada
        // Comisión USD = puntos * 9
        // Comisión COP = USD * 4000
        var commissionUsd = result.Points * (USD_PER_100_POINTS / 100m); // 9 USD por punto
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
            DealType.ClienteNuevo => 1.08m,   // +8%
            DealType.CrossSale => 1.0m,      // neutral
            DealType.Renovacion1 => 0.50m,
            DealType.Renovacion2 => 0.37m,
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

    private static decimal NormalizeAccelerator(decimal accelerator)
    {
        if (accelerator <= 0m)
            return 0m;

        // El catalogo guarda el acelerador como porcentaje: 4 significa 4%, no 4x.
        return accelerator >= 1m ? accelerator / 100m : accelerator;
    }

    private static decimal RoundMoney(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
