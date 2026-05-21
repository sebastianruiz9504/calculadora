using CotizadorInterno.Web.Models.Automation;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class CashFlowMatchingService : ICashFlowMatchingService
{
    private readonly IDataverseService _dataverse;
    private readonly CashFlowMatchingOptions _options;
    private readonly ILogger<CashFlowMatchingService> _logger;

    public CashFlowMatchingService(
        IDataverseService dataverse,
        IOptions<CashFlowMatchingOptions> options,
        ILogger<CashFlowMatchingService> logger)
    {
        _dataverse = dataverse;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CashFlowClientPaymentMatchResultDto> MatchClientPaymentsAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var resolvedEnd = endDate ?? ResolveLocalToday(_options);
        var lookbackDays = Math.Max(1, _options.LookbackDays);
        var resolvedStart = startDate ?? resolvedEnd.AddDays(-lookbackDays);
        if (resolvedStart > resolvedEnd)
            throw new InvalidOperationException("El periodo de cruce de flujo de caja no es valido.");

        var resolvedDryRun = dryRun || _options.DryRun;
        _logger.LogInformation(
            "Cruce de pagos de clientes por flujo de caja {StartDate:yyyy-MM-dd} - {EndDate:yyyy-MM-dd}. DryRun={DryRun}.",
            resolvedStart,
            resolvedEnd,
            resolvedDryRun);

        return await _dataverse.MatchCashFlowClientPaymentsAsync(
            resolvedStart,
            resolvedEnd,
            resolvedDryRun,
            _options.DifferenceTolerance,
            ct);
    }

    private static DateOnly ResolveLocalToday(CashFlowMatchingOptions options)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }
}
