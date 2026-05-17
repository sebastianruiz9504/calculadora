using System.Globalization;
using CotizadorInterno.Web.Models.RegistroPagosClientes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const decimal RegistroPagosClientesBalancedTolerance = 5000m;

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
        var unpaidRows = rows.Where(static row => !row.HasPayment).ToList();
        var overdueRows = unpaidRows.Where(row => row.IsOverdue(today)).ToList();

        return new RegistroPagosClientesBoardDto
        {
            AsOfDateLabel = today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            HasData = invoices.Count > 0,
            RecordsCount = invoices.Count,
            PaidCount = rows.Count(static row => row.HasPayment),
            OverdueCount = overdueRows.Count,
            PendingCount = Math.Max(0, unpaidRows.Count - overdueRows.Count),
            TotalInvoiceValue = RoundCurrency(rows.Sum(static row => row.TotalInvoice)),
            TotalPaidValue = RoundCurrency(rows.Sum(static row => row.PaymentValue)),
            TotalPendingValue = RoundCurrency(unpaidRows.Sum(static row => row.TotalInvoice)),
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

        var reteFteValue = CalculateRegistroPagoReteFteValue(current.TotalInvoice, current.VatValue, reteFtePercent);
        var reteIcaValue = CalculateRegistroPagoReteIcaValue(current.TotalInvoice, current.VatValue, reteIcaPercent);
        var rteIvaValue = CalculateRegistroPagoRteIvaValue(current.TotalInvoice, rteIvaPercent);
        var difference = CalculateRegistroPagoDifference(
            current.TotalInvoice,
            paymentValue,
            reteFteValue,
            reteIcaValue,
            rteIvaValue);

        var payload = new Dictionary<string, object?>
        {
            [_dashboardBillingPaymentDateField] = paymentDate,
            [_dashboardBillingPaymentValueField] = paymentValue,
            [_dashboardBillingRteFteField] = reteFtePercent,
            [_dashboardBillingReteIcaField] = reteIcaPercent,
            [_dashboardBillingRteIvaField] = rteIvaPercent
        };

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);

        var refreshedRows = await GetAllBillingRecordsAsync(metadata, httpContext.User, ct);
        var invoice = BuildRegistroPagosClientesInvoices(refreshedRows, GetBogotaToday())
            .FirstOrDefault(item => string.Equals(item.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El pago se guardo, pero no pudimos reconstruir la factura actualizada.");

        return new RegistroPagosClientesPaymentSaveResult
        {
            Message = Math.Abs(difference) <= RegistroPagosClientesBalancedTolerance
                ? $"Pago registrado para la factura {invoice.InvoiceNumber}. Diferencia dentro del rango."
                : $"Pago registrado para la factura {invoice.InvoiceNumber}. Revisa la diferencia.",
            Invoice = invoice
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
            : isOverdue ? "overdue" : "pending";
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
            TotalInvoice = row.TotalInvoice,
            PaymentStatusKey = statusKey,
            PaymentStatusLabel = statusKey switch
            {
                "paid" => "Paga",
                "overdue" => "Vencida",
                _ => "Pendiente de pago"
            },
            PaymentStatusTone = statusKey,
            AgeDays = row.GetOverdueDays(today),
            PaymentDateValue = row.PaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PaymentDateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin pago",
            PaymentValue = row.PaymentValue,
            VatValue = row.VatValue,
            ReteFtePercent = row.RteFteValue,
            ReteIcaPercent = row.ReteIcaValue,
            RteIvaPercent = row.RteIvaValue,
            ReteFteValue = retention.ReteFteValue,
            ReteIcaValue = retention.ReteIcaValue,
            RteIvaValue = retention.RteIvaValue,
            DifferenceValue = retention.DifferenceValue,
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
            .Where(static row => row.TotalInvoice > 0m)
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

        return new RegistroPagosClientesRetentionSuggestionDto
        {
            HasSuggestion = true,
            SourceCount = sourceRows.Count,
            AverageReteFtePercent = RoundRegistroPagoPercent(sourceRows.Average(static row => row.RteFteValue)),
            AverageReteIcaPercent = RoundRegistroPagoPercent(sourceRows.Average(static row => row.ReteIcaValue)),
            AverageRteIvaPercent = RoundRegistroPagoPercent(sourceRows.Average(static row => row.RteIvaValue)),
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
            TotalInvoice = row.TotalInvoice,
            PaymentValue = row.PaymentValue,
            ReteFtePercent = row.RteFteValue,
            ReteIcaPercent = row.ReteIcaValue,
            RteIvaPercent = row.RteIvaValue,
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

    private static RegistroPagoRetentionCalculation CalculateRegistroPagoRetentionValues(BillingRecordRow row)
    {
        var reteFteValue = CalculateRegistroPagoReteFteValue(row.TotalInvoice, row.VatValue, row.RteFteValue);
        var reteIcaValue = CalculateRegistroPagoReteIcaValue(row.TotalInvoice, row.VatValue, row.ReteIcaValue);
        var rteIvaValue = CalculateRegistroPagoRteIvaValue(row.TotalInvoice, row.RteIvaValue);
        var difference = CalculateRegistroPagoDifference(row.TotalInvoice, row.PaymentValue, reteFteValue, reteIcaValue, rteIvaValue);

        return new RegistroPagoRetentionCalculation(reteFteValue, reteIcaValue, rteIvaValue, difference);
    }

    private static decimal CalculateRegistroPagoReteFteValue(decimal totalInvoice, decimal vatValue, decimal rate) =>
        RoundCurrency(CalculateRegistroPagoBaseBeforeVat(totalInvoice, vatValue) * rate);

    private static decimal CalculateRegistroPagoReteIcaValue(decimal totalInvoice, decimal vatValue, decimal ratePerThousand) =>
        RoundCurrency(CalculateRegistroPagoBaseBeforeVat(totalInvoice, vatValue) * ratePerThousand / 1000m);

    private static decimal CalculateRegistroPagoRteIvaValue(decimal totalInvoice, decimal rate) =>
        RoundCurrency(CalculateRegistroPagoVatFromIncludedTotal(totalInvoice) * rate);

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
        decimal DifferenceValue);
}
