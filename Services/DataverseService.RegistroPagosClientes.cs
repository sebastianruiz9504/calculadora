using System.Globalization;
using CotizadorInterno.Web.Models.RegistroPagosClientes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const decimal RegistroPagosClientesBalancedTolerance = 2000m;

    public async Task<RegistroPagosClientesBoardDto> GetRegistroPagosClientesBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var today = GetBogotaToday();
        var rows = await GetAllBillingRecordsAsync(metadata, httpContext.User, ct);
        var invoices = BuildRegistroPagosClientesInvoices(rows, today);
        var unpaidRows = rows.Where(static row => row.IsPortfolioPending).ToList();
        var overdueRows = unpaidRows.Where(row => row.IsOverdue(today)).ToList();

        return new RegistroPagosClientesBoardDto
        {
            AsOfDateLabel = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            HasData = invoices.Count > 0,
            RecordsCount = invoices.Count,
            PaidCount = rows.Count(static row => row.HasPayment),
            OverdueCount = overdueRows.Count,
            PendingCount = Math.Max(0, unpaidRows.Count - overdueRows.Count),
            TotalInvoiceValue = RoundCurrency(rows.Sum(static row => row.NetTotalInvoice)),
            TotalPaidValue = RoundCurrency(rows.Sum(static row => row.PaymentValue)),
            TotalPendingValue = RoundCurrency(unpaidRows.Sum(static row => row.NetTotalInvoice)),
            Invoices = invoices
        };
    }

    public async Task<RegistroPagosClientesPaymentSaveResult> SaveRegistroPagosClientePaymentAsync(
        RegistroPagosClientesPaymentSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetBillingRecordByIdAsync(metadata, recordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos la factura seleccionada.");

        var paymentValue = NormalizeRegistroPagoPaymentValue(request.PaymentValue);
        var paymentDate = NormalizeBillingDateValue(request.PaymentDateValue, "fecha de pago")
            ?? throw new InvalidOperationException("Indica la fecha de pago.");
        var reteFtePercent = NormalizeRegistroPagoRetentionPercent(request.ReteFtePercent, "Rete FTE");
        var reteIcaPercent = NormalizeRegistroPagoReteIcaRate(request.ReteIcaPercent);
        var rteIvaPercent = NormalizeRegistroPagoRetentionPercent(request.RteIvaPercent, "Rte IVA");

        var reteFteValue = ResolveRegistroPagoRetentionValue(
            request.ReteFteValue,
            CalculateRegistroPagoReteFteValue(current.NetTotalInvoice, current.NetVatValue, reteFtePercent),
            "Rete FTE");
        var reteIcaValue = ResolveRegistroPagoRetentionValue(
            request.ReteIcaValue,
            CalculateRegistroPagoReteIcaValue(current.NetTotalInvoice, current.NetVatValue, reteIcaPercent),
            "Rete ICA");
        var rteIvaBaseValue = request.RteIvaBaseValue > 0m
            ? RoundCurrency(request.RteIvaBaseValue)
            : current.NetVatValue;
        var rteIvaValue = ResolveRegistroPagoRetentionValue(
            request.RteIvaValue,
            CalculateRegistroPagoRteIvaValue(current.NetTotalInvoice, rteIvaBaseValue, rteIvaPercent),
            "Rte IVA");
        var invoiceTotal = request.ExpectedInvoiceTotal is > 0m
            ? RoundCurrency(request.ExpectedInvoiceTotal.Value)
            : current.NetTotalInvoice;
        var difference = CalculateRegistroPagoDifference(
            invoiceTotal,
            paymentValue,
            reteFteValue,
            reteIcaValue,
            rteIvaValue);

        var payload = new Dictionary<string, object?>
        {
            [_dashboardBillingPaymentDateField] = paymentDate,
            [_dashboardBillingPaymentValueField] = paymentValue
        };
        foreach (var field in BuildRegistroPagoRetentionRatePayload(
                     _dashboardBillingRteFteRateField,
                     reteFtePercent,
                     _dashboardBillingReteIcaRateField,
                     reteIcaPercent,
                     _dashboardBillingRteIvaRateField,
                     rteIvaPercent))
        {
            payload[field.Key] = field.Value;
        }

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);

        var refreshedRows = await GetAllBillingRecordsAsync(metadata, httpContext.User, ct);
        var refreshed = refreshedRows
            .FirstOrDefault(item => string.Equals(item.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El pago se guardo, pero no pudimos reconstruir la factura actualizada.");
        var invoice = BuildRegistroPagosClientesInvoices(refreshedRows, GetBogotaToday())
            .First(item => string.Equals(item.RecordId, recordId, StringComparison.OrdinalIgnoreCase));
        var persistedDifference = CalculateRegistroPagoDifference(
            invoiceTotal,
            refreshed.PaymentValue,
            refreshed.RteFteValue,
            refreshed.ReteIcaValue,
            refreshed.RteIvaValue);

        return new RegistroPagosClientesPaymentSaveResult
        {
            Message = Math.Abs(difference) <= RegistroPagosClientesBalancedTolerance
                ? $"Pago registrado para la factura {invoice.InvoiceNumber}. Diferencia dentro del rango."
                : $"Pago registrado para la factura {invoice.InvoiceNumber}. Revisa la diferencia.",
            Invoice = invoice,
            PersistedPaymentValue = refreshed.PaymentValue,
            PersistedReteFteValue = refreshed.RteFteValue,
            PersistedReteIcaValue = refreshed.ReteIcaValue,
            PersistedRteIvaValue = refreshed.RteIvaValue,
            PersistedDifferenceValue = persistedDifference
        };
    }

    private IReadOnlyList<RegistroPagosClientesInvoiceDto> BuildRegistroPagosClientesInvoices(
        IReadOnlyList<BillingRecordRow> rows,
        DateOnly today)
    {
        return rows
            .Select(row => BuildRegistroPagosClientesInvoice(row, rows, today))
            .OrderByDescending(static row => row.EmissionDateValue)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private RegistroPagosClientesInvoiceDto BuildRegistroPagosClientesInvoice(
        BillingRecordRow row,
        IReadOnlyList<BillingRecordRow> allRows,
        DateOnly today)
    {
        var isOverdue = row.IsOverdue(today);
        var statusKey = row.HasPayment
            ? "paid"
            : row.IsFullyCredited ? "credited" : isOverdue ? "overdue" : "pending";
        var retention = CalculateRegistroPagoRetentionValues(row);

        return new RegistroPagosClientesInvoiceDto
        {
            RecordId = row.RecordId,
            InvoiceNumber = row.InvoiceNumber,
            EmissionDateValue = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            EmissionDateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            DueDateValue = row.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DueDateDisplay = row.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            ClientId = row.ClientId,
            ClientName = row.ClientName,
            CompanyTaxId = row.CompanyTaxId,
            SiigoInvoiceId = row.SiigoInvoiceId,
            SiigoInvoiceName = row.SiigoInvoiceName,
            InvoicePrefix = row.InvoicePrefix,
            InvoiceCode = row.InvoiceCode,
            TotalInvoice = row.NetTotalInvoice,
            PaymentStatusKey = statusKey,
            PaymentStatusLabel = statusKey switch
            {
                "paid" => "Paga",
                "credited" => "NC completa",
                "overdue" => row.IsPartiallyCredited ? "Vencida con NC parcial" : "Vencida",
                _ => row.IsPartiallyCredited ? "Pendiente con NC parcial" : "Pendiente de pago"
            },
            PaymentStatusTone = statusKey,
            AgeDays = row.GetOverdueDays(today),
            PaymentDateValue = row.PaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PaymentDateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin pago",
            PaymentValue = row.PaymentValue,
            VatValue = row.NetVatValue,
            ReteFtePercent = retention.ReteFteRate,
            ReteIcaPercent = retention.ReteIcaRate,
            RteIvaPercent = retention.RteIvaRate,
            ReteFteValue = retention.ReteFteValue,
            ReteIcaValue = retention.ReteIcaValue,
            RteIvaValue = retention.RteIvaValue,
            DifferenceValue = retention.DifferenceValue,
            CreditNoteTotal = row.CreditNoteTotal,
            CreditNoteCount = row.CreditNoteCount,
            IsFullyCredited = row.IsFullyCredited,
            IsPartiallyCredited = row.IsPartiallyCredited,
            Suggestion = BuildRegistroPagosClientesSuggestion(row, allRows)
        };
    }

    private RegistroPagosClientesRetentionSuggestionDto BuildRegistroPagosClientesSuggestion(
        BillingRecordRow current,
        IReadOnlyList<BillingRecordRow> allRows)
    {
        var sameClientRows = allRows
            .Where(row => !string.Equals(row.RecordId, current.RecordId, StringComparison.OrdinalIgnoreCase))
            .Where(row => RegistroPagosClientesSameClient(row, current))
            .Where(static row => row.NetTotalInvoice > 0m)
            .Where(static row => row.HasPayment || row.RteFteValue > 0m || row.ReteIcaValue > 0m || row.RteIvaValue > 0m)
            .ToList();

        var currentScenarioDate = GetRegistroPagosClientesScenarioDate(current);
        var pastRows = currentScenarioDate is null
            ? sameClientRows
            : sameClientRows
                .Where(row =>
                {
                    var scenarioDate = GetRegistroPagosClientesScenarioDate(row);
                    return scenarioDate is not null && scenarioDate.Value.DayNumber < currentScenarioDate.Value.DayNumber;
                })
                .ToList();

        var sourceRows = (pastRows.Count > 0 ? pastRows : sameClientRows)
            .OrderByDescending(row => GetRegistroPagosClientesScenarioDate(row))
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sourceRows.Count == 0)
            return new RegistroPagosClientesRetentionSuggestionDto();

        var scenarios = sourceRows
            .Take(5)
            .Select(BuildRegistroPagosClientesScenario)
            .ToList();

        var sourceCalculations = sourceRows
            .Select(CalculateRegistroPagoRetentionValues)
            .ToArray();
        return new RegistroPagosClientesRetentionSuggestionDto
        {
            HasSuggestion = true,
            SourceCount = sourceRows.Count,
            AverageReteFtePercent = RoundRegistroPagoPercent(sourceCalculations.Average(static item => item.ReteFteRate)),
            AverageReteIcaPercent = RoundRegistroPagoPercent(sourceCalculations.Average(static item => item.ReteIcaRate)),
            AverageRteIvaPercent = RoundRegistroPagoPercent(sourceCalculations.Average(static item => item.RteIvaRate)),
            LatestScenario = scenarios.FirstOrDefault(),
            Scenarios = scenarios
        };
    }

    private static RegistroPagosClientesRetentionScenarioDto BuildRegistroPagosClientesScenario(BillingRecordRow row)
    {
        var retention = CalculateRegistroPagoRetentionValues(row);

        return new RegistroPagosClientesRetentionScenarioDto
        {
            InvoiceNumber = row.InvoiceNumber,
            PaymentDateDisplay = (row.PaymentDate ?? row.EmissionDate)?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            TotalInvoice = row.NetTotalInvoice,
            PaymentValue = row.PaymentValue,
            ReteFtePercent = retention.ReteFteRate,
            ReteIcaPercent = retention.ReteIcaRate,
            RteIvaPercent = retention.RteIvaRate,
            DifferenceValue = retention.DifferenceValue
        };
    }

    private static bool RegistroPagosClientesSameClient(BillingRecordRow candidate, BillingRecordRow current)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ClientId) && !string.IsNullOrWhiteSpace(current.ClientId))
        {
            return string.Equals(candidate.ClientId, current.ClientId, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            NormalizeRegistroPagoClientKey(candidate.ClientName),
            NormalizeRegistroPagoClientKey(current.ClientName),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRegistroPagoClientKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return string.Join(" ", value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateOnly? GetRegistroPagosClientesScenarioDate(BillingRecordRow row) =>
        row.PaymentDate ?? row.EmissionDate ?? row.DueDate;

    private static decimal NormalizeRegistroPagoPaymentValue(decimal value)
    {
        if (value <= 0m)
            throw new InvalidOperationException("El valor pago debe ser mayor a cero.");

        return RoundCurrency(value);
    }

    private static decimal NormalizeRegistroPagoRetentionPercent(decimal value, string label)
    {
        if (value < 0m || value > 1m)
            throw new InvalidOperationException($"El valor de {label} debe estar entre 0 y 1. Usa 0,04 para 4%.");

        return RoundRegistroPagoPercent(value);
    }

    private static decimal NormalizeRegistroPagoReteIcaRate(decimal value)
    {
        if (value < 0m || value > 1000m)
            throw new InvalidOperationException("La tarifa de Rete ICA debe estar entre 0 y 1000. Usa 11,04 para 11,04 por mil.");

        return RoundRegistroPagoPercent(value);
    }

    internal static decimal ResolveRegistroPagoRetentionValue(
        decimal? requestedValue,
        decimal calculatedValue,
        string label)
    {
        if (requestedValue is null)
            return RoundCurrency(calculatedValue);
        if (requestedValue.Value < 0m)
            throw new InvalidOperationException($"El valor de {label} no puede ser negativo.");

        return RoundCurrency(requestedValue.Value);
    }

    internal static IReadOnlyDictionary<string, decimal> BuildRegistroPagoRetentionRatePayload(
        string reteFteRateField,
        decimal reteFteRate,
        string reteIcaRateField,
        decimal reteIcaRate,
        string rteIvaRateField,
        decimal rteIvaRate) =>
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [reteFteRateField] = reteFteRate,
            [reteIcaRateField] = reteIcaRate,
            [rteIvaRateField] = rteIvaRate
        };

    private static RegistroPagoRetentionCalculation CalculateRegistroPagoRetentionValues(BillingRecordRow row)
    {
        var reteFteCandidates = BuildRegistroPagoStoredValueCandidates(
            row.RteFteValue,
            row.RteFteValue is >= 0m and <= 1m
                ? CalculateRegistroPagoReteFteValue(row.NetTotalInvoice, row.NetVatValue, row.RteFteValue)
                : null);
        var reteIcaCandidates = BuildRegistroPagoStoredValueCandidates(
            row.ReteIcaValue,
            row.ReteIcaValue is >= 0m and <= 1000m
                ? CalculateRegistroPagoReteIcaValue(row.NetTotalInvoice, row.NetVatValue, row.ReteIcaValue)
                : null);
        var rteIvaCandidates = BuildRegistroPagoStoredValueCandidates(
            row.RteIvaValue,
            row.RteIvaValue is >= 0m and <= 1m
                ? CalculateRegistroPagoRteIvaValue(row.NetTotalInvoice, row.NetVatValue, row.RteIvaValue)
                : null);

        var selected = (
            from reteFteValue in reteFteCandidates
            from reteIcaValue in reteIcaCandidates
            from rteIvaValue in rteIvaCandidates
            let difference = CalculateRegistroPagoDifference(
                row.NetTotalInvoice,
                row.PaymentValue,
                reteFteValue,
                reteIcaValue,
                rteIvaValue)
            orderby Math.Abs(difference), reteFteValue + reteIcaValue + rteIvaValue
            select new { reteFteValue, reteIcaValue, rteIvaValue, difference })
            .First();
        var baseBeforeVat = CalculateRegistroPagoBaseBeforeVat(row.NetTotalInvoice, row.NetVatValue);
        var vatBase = ResolveRegistroPagoVatBase(row.NetTotalInvoice, row.NetVatValue);

        return new RegistroPagoRetentionCalculation(
            selected.reteFteValue,
            selected.reteIcaValue,
            selected.rteIvaValue,
            selected.difference,
            baseBeforeVat > 0m ? RoundRegistroPagoPercent(selected.reteFteValue / baseBeforeVat) : 0m,
            baseBeforeVat > 0m ? RoundRegistroPagoPercent(selected.reteIcaValue * 1000m / baseBeforeVat) : 0m,
            vatBase > 0m ? RoundRegistroPagoPercent(selected.rteIvaValue / vatBase) : 0m);
    }

    private static IReadOnlyList<decimal> BuildRegistroPagoStoredValueCandidates(
        decimal storedValue,
        decimal? calculatedFromLegacyRate)
    {
        var values = new List<decimal> { RoundCurrency(Math.Max(storedValue, 0m)) };
        if (calculatedFromLegacyRate is not null)
        {
            var calculated = RoundCurrency(Math.Max(calculatedFromLegacyRate.Value, 0m));
            if (!values.Contains(calculated))
                values.Add(calculated);
        }

        return values;
    }

    private static decimal CalculateRegistroPagoReteFteValue(decimal totalInvoice, decimal vatValue, decimal rate) =>
        RoundCurrency(CalculateRegistroPagoBaseBeforeVat(totalInvoice, vatValue) * rate);

    private static decimal CalculateRegistroPagoReteIcaValue(decimal totalInvoice, decimal vatValue, decimal ratePerThousand) =>
        RoundCurrency(CalculateRegistroPagoBaseBeforeVat(totalInvoice, vatValue) * ratePerThousand / 1000m);

    internal static decimal CalculateRegistroPagoRteIvaValue(decimal totalInvoice, decimal vatValue, decimal rate) =>
        RoundCurrency(ResolveRegistroPagoVatBase(totalInvoice, vatValue) * rate);

    private static decimal ResolveRegistroPagoVatBase(decimal totalInvoice, decimal vatValue) =>
        vatValue > 0m
            ? RoundCurrency(vatValue)
            : CalculateRegistroPagoVatFromIncludedTotal(totalInvoice);

    private static decimal CalculateRegistroPagoBaseBeforeVat(decimal totalInvoice, decimal vatValue)
    {
        if (totalInvoice <= 0m)
            return 0m;

        return RoundCurrency(Math.Max(totalInvoice - Math.Max(vatValue, 0m), 0m));
    }

    private static decimal CalculateRegistroPagoVatFromIncludedTotal(decimal totalInvoice)
    {
        if (totalInvoice <= 0m)
            return 0m;

        return RoundCurrency(totalInvoice - (totalInvoice / 1.19m));
    }

    private static decimal CalculateRegistroPagoDifference(
        decimal totalInvoice,
        decimal paymentValue,
        decimal reteFteValue,
        decimal reteIcaValue,
        decimal rteIvaValue) =>
        RoundCurrency(totalInvoice - paymentValue - reteFteValue - reteIcaValue - rteIvaValue);

    private static decimal RoundRegistroPagoPercent(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record RegistroPagoRetentionCalculation(
        decimal ReteFteValue,
        decimal ReteIcaValue,
        decimal RteIvaValue,
        decimal DifferenceValue,
        decimal ReteFteRate,
        decimal ReteIcaRate,
        decimal RteIvaRate);
}
