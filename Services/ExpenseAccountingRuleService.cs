using CotizadorInterno.Web.Models.Automation;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class ExpenseAccountingRuleService : IExpenseAccountingRuleService
{
    private readonly IDataverseService _dataverse;
    private readonly ExpenseAccountingRulesOptions _options;
    private readonly ILogger<ExpenseAccountingRuleService> _logger;

    public ExpenseAccountingRuleService(
        IDataverseService dataverse,
        IOptions<ExpenseAccountingRulesOptions> options,
        ILogger<ExpenseAccountingRuleService> logger)
    {
        _dataverse = dataverse;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExpenseAccountingRuleApplyResultDto> ApplyAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? movementType = null,
        bool overwrite = false,
        CancellationToken ct = default)
    {
        var period = ResolvePeriod(startDate, endDate);
        var resolvedMovementType = string.IsNullOrWhiteSpace(movementType)
            ? _options.MovementType
            : movementType.Trim();

        _logger.LogInformation(
            "Aplicando reglas contables a gastos para {StartDate:yyyy-MM-dd} - {EndDate:yyyy-MM-dd}. Movimiento={MovementType} Sobrescribir={Overwrite}.",
            period.StartDate,
            period.EndDate,
            resolvedMovementType,
            overwrite);

        return await _dataverse.ApplyExpenseAccountingRulesAsync(
            period.StartDate,
            period.EndDate,
            resolvedMovementType,
            overwrite,
            ct);
    }

    private (DateOnly StartDate, DateOnly EndDate) ResolvePeriod(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue || endDate.HasValue)
        {
            if (!startDate.HasValue || !endDate.HasValue)
                throw new InvalidOperationException("Periodo invalido. Usa startDate=YYYY-MM-DD y endDate=YYYY-MM-DD.");

            if (startDate.Value > endDate.Value)
                throw new InvalidOperationException("La fecha inicial no puede ser mayor que la fecha final.");

            return (startDate.Value, endDate.Value);
        }

        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(_options.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        var lookbackDays = Math.Clamp(_options.LookbackDays, 1, 366);
        return (today.AddDays(-lookbackDays), today);
    }
}
