using CotizadorInterno.Web.Models.Automation;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class SiigoAccountCatalogSyncService : ISiigoAccountCatalogSyncService
{
    private readonly ISiigoService _siigo;
    private readonly IDataverseService _dataverse;
    private readonly SiigoAccountCatalogSyncOptions _options;
    private readonly ILogger<SiigoAccountCatalogSyncService> _logger;

    public SiigoAccountCatalogSyncService(
        ISiigoService siigo,
        IDataverseService dataverse,
        IOptions<SiigoAccountCatalogSyncOptions> options,
        ILogger<SiigoAccountCatalogSyncService> logger)
    {
        _siigo = siigo;
        _dataverse = dataverse;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AccountCatalogSyncResultDto> SyncAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var period = ResolvePeriod(startDate, endDate);
        _logger.LogInformation(
            "Sincronizando catalogo de cuentas contables Siigo para {StartDate:yyyy-MM-dd} - {EndDate:yyyy-MM-dd}.",
            period.StartDate,
            period.EndDate);

        var accounts = await _siigo.GetObservedAccountCatalogAsync(period.StartDate, period.EndDate, ct);
        var result = await _dataverse.UpsertSiigoAccountCatalogAsync(period.StartDate, period.EndDate, accounts, ct);

        _logger.LogInformation(
            "Catalogo de cuentas contables Siigo sincronizado. Observadas={Observed} Creadas={Created} Actualizadas={Updated} SinCambios={Unchanged}.",
            result.ObservedAccounts,
            result.Created,
            result.Updated,
            result.Unchanged);

        return result;
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lookbackMonths = Math.Clamp(_options.LookbackMonths, 1, 36);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-lookbackMonths);
        return (start, today);
    }
}
