using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Reconciliation;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public interface IFinancialReconciliationService
{
    Task<FinancialReconciliationSnapshotResult> BuildSnapshotAsync(int year, int month, CancellationToken ct = default);
    Task<FinancialReconciliationReportResult> BuildReportAsync(int year, int month, CancellationToken ct = default);
    Task<FinancialReconciliationRunResult> RunAndSendAsync(int year, int month, CancellationToken ct = default);
    Task<FinancialReconciliationRunResult> RunConfiguredPeriodAsync(DateTimeOffset? now = null, CancellationToken ct = default);
}

public sealed class FinancialReconciliationService : IFinancialReconciliationService
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IDataverseService _dataverse;
    private readonly ISiigoService _siigo;
    private readonly IReconciliationReportSender _sender;
    private readonly FinancialReconciliationOptions _options;
    private readonly ILogger<FinancialReconciliationService> _logger;

    public FinancialReconciliationService(
        IDataverseService dataverse,
        ISiigoService siigo,
        IReconciliationReportSender sender,
        IOptions<FinancialReconciliationOptions> options,
        ILogger<FinancialReconciliationService> logger)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FinancialReconciliationReportResult> BuildReportAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        return await BuildReportCoreAsync(year, month, applyBillingCorrections: false, ct);
    }

    public async Task<FinancialReconciliationSnapshotResult> BuildSnapshotAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion financiera no es valido.");

        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var periodLabel = ToTitleCase(start.ToString("MMMM yyyy", ColombianCulture));

        var dataverseBillingTask = _dataverse.GetFinancialReconciliationBillingRowsAsync(start, end, ct);
        var dataverseCreditNotesTask = _dataverse.GetFinancialReconciliationCreditNoteRowsAsync(start, end, ct);
        var dataverseExpensesTask = _dataverse.GetFinancialReconciliationExpenseRowsAsync(start, end, ct);
        var siigoTask = _siigo.GetFinancialReconciliationDocumentsAsync(start, end.AddDays(-1), ct);

        await Task.WhenAll(dataverseBillingTask, dataverseCreditNotesTask, dataverseExpensesTask, siigoTask);

        var billingComparisons = BuildBillingComparisons(
            dataverseBillingTask.Result,
            dataverseCreditNotesTask.Result,
            siigoTask.Result);
        var expenseComparisons = BuildExpenseComparisons(dataverseExpensesTask.Result, siigoTask.Result.Purchases);
        var summary = BuildSummary(
            dataverseBillingTask.Result,
            dataverseCreditNotesTask.Result,
            dataverseExpensesTask.Result,
            siigoTask.Result,
            billingComparisons,
            expenseComparisons);

        return new FinancialReconciliationSnapshotResult
        {
            Year = year,
            Month = month,
            PeriodLabel = periodLabel,
            GeneratedAt = DateTimeOffset.UtcNow,
            Summary = summary
        };
    }

    private async Task<FinancialReconciliationReportResult> BuildReportCoreAsync(
        int year,
        int month,
        bool applyBillingCorrections,
        CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion financiera no es valido.");

        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var periodLabel = ToTitleCase(start.ToString("MMMM yyyy", ColombianCulture));

        var dataverseBillingTask = _dataverse.GetFinancialReconciliationBillingRowsAsync(start, end, ct);
        var dataverseCreditNotesTask = _dataverse.GetFinancialReconciliationCreditNoteRowsAsync(start, end, ct);
        var dataverseExpensesTask = _dataverse.GetFinancialReconciliationExpenseRowsAsync(start, end, ct);
        var siigoTask = _siigo.GetFinancialReconciliationDocumentsAsync(start, end.AddDays(-1), ct);

        await Task.WhenAll(dataverseBillingTask, dataverseCreditNotesTask, dataverseExpensesTask, siigoTask);

        var dataverseBilling = dataverseBillingTask.Result;
        var dataverseCreditNotes = dataverseCreditNotesTask.Result;
        var dataverseExpenses = dataverseExpensesTask.Result;
        var siigo = siigoTask.Result;
        var beforeBillingComparisons = BuildBillingComparisons(dataverseBilling, dataverseCreditNotes, siigo);
        var expenseComparisons = BuildExpenseComparisons(dataverseExpenses, siigo.Purchases);
        var beforeSummary = BuildSummary(dataverseBilling, dataverseCreditNotes, dataverseExpenses, siigo, beforeBillingComparisons, expenseComparisons);
        var corrections = new FinancialReconciliationCorrectionResult();

        IReadOnlyList<BillingComparisonRow> afterBillingComparisons = beforeBillingComparisons;
        var afterSummary = beforeSummary;
        if (applyBillingCorrections)
        {
            corrections = await _dataverse.ApplyFinancialReconciliationBillingCorrectionsAsync(
                start,
                end,
                dataverseBilling,
                dataverseCreditNotes,
                siigo,
                ct);
            dataverseBilling = await _dataverse.GetFinancialReconciliationBillingRowsAsync(start, end, ct);
            dataverseCreditNotes = await _dataverse.GetFinancialReconciliationCreditNoteRowsAsync(start, end, ct);
            afterBillingComparisons = BuildBillingComparisons(dataverseBilling, dataverseCreditNotes, siigo);
            afterSummary = BuildSummary(dataverseBilling, dataverseCreditNotes, dataverseExpenses, siigo, afterBillingComparisons, expenseComparisons);
        }

        var workbookBytes = BuildWorkbook(
            periodLabel,
            beforeSummary,
            afterSummary,
            corrections,
            beforeBillingComparisons,
            afterBillingComparisons,
            expenseComparisons,
            dataverseBilling,
            dataverseCreditNotes,
            dataverseExpenses,
            siigo);

        return new FinancialReconciliationReportResult
        {
            Year = year,
            Month = month,
            PeriodLabel = periodLabel,
            FileName = $"conciliacion-financiera-{year:D4}-{month:D2}.xlsx",
            ExcelContent = workbookBytes,
            BeforeSummary = beforeSummary,
            Summary = afterSummary,
            Corrections = corrections
        };
    }

    public async Task<FinancialReconciliationRunResult> RunAndSendAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        var report = await BuildReportCoreAsync(year, month, applyBillingCorrections: true, ct);
        var hasDifferences = report.BeforeSummary.BillingDifferenceCount > 0
            || report.Summary.BillingDifferenceCount > 0
            || report.Summary.ExpenseDifferenceCount > 0
            || report.Corrections.Actions.Count > 0;
        if (!hasDifferences && !_options.SendWhenNoDifferences)
        {
            return new FinancialReconciliationRunResult
            {
                Report = report,
                EmailSent = false,
                EmailStatus = "No se envio correo porque no hubo diferencias y SendWhenNoDifferences esta desactivado."
            };
        }

        var recipient = ResolveRecipientEmail();
        await _sender.SendAsync(new ReconciliationEmailMessage
        {
            To = recipient,
            Subject = $"Conciliacion financiera {report.PeriodLabel}",
            HtmlBody = BuildEmailHtml(report),
            AttachmentFileName = Path.ChangeExtension(report.FileName, ".zip"),
            AttachmentContentType = "application/zip",
            AttachmentContent = BuildZipAttachment(report.FileName, report.ExcelContent)
        }, ct);

        _logger.LogInformation(
            "Conciliacion financiera {Year}-{Month:D2} enviada a {Recipient}. Diferencias facturacion: {BillingCount}. Diferencias gastos: {ExpenseCount}.",
            year,
            month,
            recipient,
            report.Summary.BillingDifferenceCount,
            report.Summary.ExpenseDifferenceCount);

        return new FinancialReconciliationRunResult
        {
            Report = report,
            EmailSent = true,
            EmailStatus = $"Enviado a {recipient}."
        };
    }

    public async Task<FinancialReconciliationRunResult> RunConfiguredPeriodAsync(
        DateTimeOffset? now = null,
        CancellationToken ct = default)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(_options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, timeZone);
        var offset = Math.Clamp(_options.PeriodOffsetMonths, 1, 24);
        var periodStart = new DateOnly(localNow.Year, localNow.Month, 1).AddMonths(-offset);
        return await RunAndSendAsync(periodStart.Year, periodStart.Month, ct);
    }

    private FinancialReconciliationSummary BuildSummary(
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        IReadOnlyList<ReconciliationDataverseExpenseRow> dataverseExpenses,
        SiigoFinancialReconciliationData siigo,
        IReadOnlyList<BillingComparisonRow> billingComparisons,
        IReadOnlyList<ExpenseComparisonRow> expenseComparisons)
    {
        var activeInvoices = FilterActiveInvoices(siigo.Invoices).ToList();
        var siigoBillingGross = SumCurrency(activeInvoices, static row => row.Total);
        var siigoBillingCreditNotes = SumCurrency(siigo.CreditNotes, static row => row.Total);
        var siigoVatGross = SumCurrency(activeInvoices, static row => row.Vat);
        var siigoVatCreditNotes = SumCurrency(siigo.CreditNotes, static row => row.Vat);
        var dataverseBillingGross = SumCurrency(dataverseBilling, static row => row.Total);
        var dataverseBillingCreditNotes = SumCurrency(dataverseCreditNotes, static row => row.Total);
        var dataverseBillingNet = RoundCurrency(dataverseBillingGross - dataverseBillingCreditNotes);
        var dataverseVatGross = SumCurrency(dataverseBilling, static row => row.Vat);
        var dataverseVatCreditNotes = SumCurrency(dataverseCreditNotes, static row => row.Vat);
        var dataverseVatNet = RoundCurrency(dataverseVatGross - dataverseVatCreditNotes);
        var powerAppsExpenses = SumCurrency(dataverseExpenses, static row => row.Total);
        var powerAppsExpenseVat = SumCurrency(dataverseExpenses, static row => row.Vat);
        var siigoExpenses = SumCurrency(siigo.Purchases, static row => row.Total);
        var siigoExpenseVat = SumCurrency(siigo.Purchases, static row => row.Vat);

        return new FinancialReconciliationSummary
        {
            SiigoBillingGross = siigoBillingGross,
            SiigoBillingCreditNotes = siigoBillingCreditNotes,
            SiigoBillingNet = RoundCurrency(siigoBillingGross - siigoBillingCreditNotes),
            SiigoBillingInvoiceCount = activeInvoices.Count,
            SiigoBillingCreditNoteCount = siigo.CreditNotes.Count,
            DataverseBillingGross = dataverseBillingGross,
            DataverseBillingCreditNotes = dataverseBillingCreditNotes,
            DataverseBillingNet = dataverseBillingNet,
            DataverseBilling = dataverseBillingNet,
            DataverseBillingInvoiceCount = dataverseBilling.Count,
            DataverseBillingCreditNoteCount = dataverseCreditNotes.Count,
            BillingDifference = RoundCurrency(dataverseBillingNet - (siigoBillingGross - siigoBillingCreditNotes)),
            SiigoVatGross = siigoVatGross,
            SiigoVatCreditNotes = siigoVatCreditNotes,
            SiigoVatNet = RoundCurrency(siigoVatGross - siigoVatCreditNotes),
            DataverseVatGross = dataverseVatGross,
            DataverseVatCreditNotes = dataverseVatCreditNotes,
            DataverseVatNet = dataverseVatNet,
            DataverseVat = dataverseVatNet,
            BillingVatDifference = RoundCurrency(dataverseVatNet - (siigoVatGross - siigoVatCreditNotes)),
            PowerAppsExpenses = powerAppsExpenses,
            SiigoExpenses = siigoExpenses,
            ExpenseDifference = RoundCurrency(siigoExpenses - powerAppsExpenses),
            PowerAppsExpenseCount = dataverseExpenses.Count,
            SiigoExpenseCount = siigo.Purchases.Count,
            PowerAppsExpenseVat = powerAppsExpenseVat,
            SiigoExpenseVat = siigoExpenseVat,
            ExpenseVatDifference = RoundCurrency(siigoExpenseVat - powerAppsExpenseVat),
            BillingDifferenceCount = billingComparisons.Count(row => !row.IsOk),
            ExpenseDifferenceCount = expenseComparisons.Count(row => !row.IsOk)
        };
    }

    private IReadOnlyList<BillingComparisonRow> BuildBillingComparisons(
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseRows,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        SiigoFinancialReconciliationData siigo)
    {
        var siigoSummaries = BuildSiigoBillingSummaries(siigo);
        var dataverseByKey = BuildDataverseBillingSummaries(dataverseRows, dataverseCreditNotes);

        return siigoSummaries.Keys
            .Concat(dataverseByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                siigoSummaries.TryGetValue(key, out var siigoRow);
                dataverseByKey.TryGetValue(key, out var dataverseRow);

                var totalDifference = RoundCurrency((dataverseRow?.NetTotal ?? 0m) - (siigoRow?.NetTotal ?? 0m));
                var vatDifference = RoundCurrency((dataverseRow?.NetVat ?? 0m) - (siigoRow?.NetVat ?? 0m));
                var status = ResolveBillingStatus(siigoRow, dataverseRow, totalDifference, vatDifference);

                return new BillingComparisonRow
                {
                    Status = status,
                    InvoiceNumber = FirstNonEmpty(siigoRow?.InvoiceNumber, dataverseRow?.InvoiceNumber, key),
                    ClientName = FirstNonEmpty(siigoRow?.CustomerIdentification, dataverseRow?.ClientName),
                    SiigoGrossTotal = siigoRow?.GrossTotal ?? 0m,
                    SiigoCreditNoteTotal = siigoRow?.CreditNoteTotal ?? 0m,
                    SiigoNetTotal = siigoRow?.NetTotal ?? 0m,
                    DataverseGrossTotal = dataverseRow?.GrossTotal ?? 0m,
                    DataverseCreditNoteTotal = dataverseRow?.CreditNoteTotal ?? 0m,
                    DataverseNetTotal = dataverseRow?.NetTotal ?? 0m,
                    TotalDifference = totalDifference,
                    SiigoGrossVat = siigoRow?.GrossVat ?? 0m,
                    SiigoCreditNoteVat = siigoRow?.CreditNoteVat ?? 0m,
                    SiigoNetVat = siigoRow?.NetVat ?? 0m,
                    DataverseGrossVat = dataverseRow?.GrossVat ?? 0m,
                    DataverseCreditNoteVat = dataverseRow?.CreditNoteVat ?? 0m,
                    DataverseNetVat = dataverseRow?.NetVat ?? 0m,
                    VatDifference = vatDifference,
                    Notes = BuildBillingNotes(siigoRow, dataverseRow)
                };
            })
            .OrderBy(static row => row.IsOk ? 1 : 0)
            .ThenBy(static row => row.Status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, SiigoBillingSummary> BuildSiigoBillingSummaries(SiigoFinancialReconciliationData siigo)
    {
        var activeInvoices = FilterActiveInvoices(siigo.Invoices).ToList();
        var invoiceKeyById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var summaries = activeInvoices
            .GroupBy(static invoice => BuildDocumentKey(invoice.Name, "siigo", invoice.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    foreach (var invoice in group.Where(static item => !string.IsNullOrWhiteSpace(item.Id)))
                        invoiceKeyById[invoice.Id.Trim()] = group.Key;

                    return new SiigoBillingSummary
                    {
                        InvoiceNumber = FirstNonEmpty(group.Select(static item => item.Name).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.First().Id),
                        CustomerIdentification = FirstNonEmpty(group.Select(static item => item.CustomerIdentification).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), ""),
                        GrossTotal = SumCurrency(group, static item => item.Total),
                        GrossVat = SumCurrency(group, static item => item.Vat)
                    };
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var creditNote in siigo.CreditNotes)
        {
            var key = ResolveCreditNoteBillingKey(creditNote, invoiceKeyById);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!summaries.TryGetValue(key, out var summary))
            {
                summary = new SiigoBillingSummary
                {
                    InvoiceNumber = FirstNonEmpty(creditNote.InvoiceName, creditNote.Name, creditNote.Id),
                    CustomerIdentification = creditNote.CustomerIdentification
                };
                summaries[key] = summary;
            }

            summary.CreditNoteTotal = RoundCurrency(summary.CreditNoteTotal + creditNote.Total);
            summary.CreditNoteVat = RoundCurrency(summary.CreditNoteVat + creditNote.Vat);
            if (!string.IsNullOrWhiteSpace(creditNote.Name))
                summary.CreditNoteNames.Add(creditNote.Name.Trim());
        }

        return summaries;
    }

    private Dictionary<string, DataverseBillingGroup> BuildDataverseBillingSummaries(
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseRows,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes)
    {
        var keyBySiigoId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var summaries = dataverseRows
            .GroupBy(row => BuildDataverseInvoiceKey(row), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    foreach (var row in group.Where(static item => !string.IsNullOrWhiteSpace(item.SiigoInvoiceId)))
                        keyBySiigoId[row.SiigoInvoiceId.Trim()] = group.Key;

                    return new DataverseBillingGroup
                    {
                        InvoiceNumber = FirstNonEmpty(group.Select(static item => item.InvoiceNumber).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.First().RecordId),
                        ClientName = FirstNonEmpty(group.Select(static item => item.ClientName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.Select(static item => item.CompanyTaxId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), ""),
                        GrossTotal = SumCurrency(group, static item => item.Total),
                        GrossVat = SumCurrency(group, static item => item.Vat),
                        Count = group.Count()
                    };
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var creditNote in dataverseCreditNotes)
        {
            var key = ResolveDataverseCreditNoteBillingKey(creditNote, keyBySiigoId);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!summaries.TryGetValue(key, out var summary))
            {
                summary = new DataverseBillingGroup
                {
                    InvoiceNumber = FirstNonEmpty(creditNote.InvoiceName, creditNote.CreditNoteName, creditNote.CreditNoteId),
                    ClientName = creditNote.CustomerIdentification
                };
                summaries[key] = summary;
            }

            summary.CreditNoteTotal = RoundCurrency(summary.CreditNoteTotal + creditNote.Total);
            summary.CreditNoteVat = RoundCurrency(summary.CreditNoteVat + creditNote.Vat);
            if (!string.IsNullOrWhiteSpace(creditNote.CreditNoteName))
                summary.CreditNoteNames.Add(creditNote.CreditNoteName.Trim());
        }

        return summaries;
    }

    private static string BuildDataverseInvoiceKey(ReconciliationDataverseBillingRow row) =>
        BuildDocumentKey(FirstNonEmpty(row.SiigoInvoiceName, row.InvoiceNumber), "dataverse", row.RecordId);

    private static string ResolveDataverseCreditNoteBillingKey(
        ReconciliationDataverseCreditNoteRow creditNote,
        IReadOnlyDictionary<string, string> invoiceKeyById)
    {
        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceId)
            && invoiceKeyById.TryGetValue(creditNote.InvoiceId.Trim(), out var invoiceKey))
        {
            return invoiceKey;
        }

        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceName))
            return BuildDocumentKey(creditNote.InvoiceName, "credit-note-invoice", creditNote.CreditNoteId);

        return BuildDocumentKey(creditNote.CreditNoteName, "credit-note", creditNote.CreditNoteId);
    }

    private static string BuildBillingNotes(SiigoBillingSummary? siigoRow, DataverseBillingGroup? dataverseRow)
    {
        var notes = new List<string>();
        if (siigoRow?.CreditNoteNames.Count > 0)
            notes.Add($"NC Siigo: {string.Join(", ", siigoRow.CreditNoteNames)}");
        if (dataverseRow?.CreditNoteNames.Count > 0)
            notes.Add($"NC Dataverse: {string.Join(", ", dataverseRow.CreditNoteNames)}");

        return string.Join(" | ", notes);
    }

    private IEnumerable<SiigoReconciliationInvoice> FilterActiveInvoices(IEnumerable<SiigoReconciliationInvoice> invoices) =>
        _options.ExcludeAnnulledSiigoInvoices
            ? invoices.Where(static invoice => !invoice.Annulled)
            : invoices;

    private static string ResolveCreditNoteBillingKey(
        SiigoReconciliationCreditNote creditNote,
        IReadOnlyDictionary<string, string> invoiceKeyById)
    {
        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceId)
            && invoiceKeyById.TryGetValue(creditNote.InvoiceId.Trim(), out var invoiceKey))
        {
            return invoiceKey;
        }

        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceName))
            return BuildDocumentKey(creditNote.InvoiceName, "credit-note-invoice", creditNote.Id);

        return BuildDocumentKey(creditNote.Name, "credit-note", creditNote.Id);
    }

    private IReadOnlyList<ExpenseComparisonRow> BuildExpenseComparisons(
        IReadOnlyList<ReconciliationDataverseExpenseRow> dataverseRows,
        IReadOnlyList<SiigoReconciliationPurchase> siigoPurchases)
    {
        var powerAppsByKey = dataverseRows
            .GroupBy(static row => BuildExpenseKey(row.InvoiceNumber, row.IssuerNit, row.IssuerName, row.EmissionDate, row.Total, row.RecordId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new PowerAppsExpenseGroup
                {
                    InvoiceNumber = FirstNonEmpty(group.Select(static item => item.InvoiceNumber).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.First().RecordId),
                    Supplier = FirstNonEmpty(group.Select(static item => item.IssuerName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.First().IssuerNit),
                    Date = group.Select(static item => item.EmissionDate).FirstOrDefault(static value => value.HasValue),
                    Total = SumCurrency(group, static item => item.Total),
                    Vat = SumCurrency(group, static item => item.Vat),
                    Count = group.Count()
                },
                StringComparer.OrdinalIgnoreCase);
        var siigoByKey = siigoPurchases
            .GroupBy(static row => BuildExpenseKey(FirstNonEmpty(row.ProviderInvoiceFullNumber, row.Name), row.SupplierIdentification, "", row.Date, row.Total, row.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => new SiigoExpenseGroup
                {
                    InvoiceNumber = FirstNonEmpty(group.Select(static item => item.ProviderInvoiceFullNumber).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), group.First().Name, group.First().Id),
                    Supplier = FirstNonEmpty(group.Select(static item => item.SupplierIdentification).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)), ""),
                    Date = group.Select(static item => item.Date).FirstOrDefault(static value => value.HasValue),
                    Total = SumCurrency(group, static item => item.Total),
                    Vat = SumCurrency(group, static item => item.Vat),
                    Count = group.Count()
                },
                StringComparer.OrdinalIgnoreCase);

        return powerAppsByKey.Keys
            .Concat(siigoByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                powerAppsByKey.TryGetValue(key, out var powerAppsRow);
                siigoByKey.TryGetValue(key, out var siigoRow);
                var totalDifference = RoundCurrency((siigoRow?.Total ?? 0m) - (powerAppsRow?.Total ?? 0m));
                var vatDifference = RoundCurrency((siigoRow?.Vat ?? 0m) - (powerAppsRow?.Vat ?? 0m));
                var status = ResolveExpenseStatus(powerAppsRow, siigoRow, totalDifference, vatDifference);

                return new ExpenseComparisonRow
                {
                    Status = status,
                    InvoiceNumber = FirstNonEmpty(powerAppsRow?.InvoiceNumber, siigoRow?.InvoiceNumber, key),
                    Supplier = FirstNonEmpty(powerAppsRow?.Supplier, siigoRow?.Supplier),
                    Date = powerAppsRow?.Date ?? siigoRow?.Date,
                    PowerAppsTotal = powerAppsRow?.Total ?? 0m,
                    SiigoTotal = siigoRow?.Total ?? 0m,
                    TotalDifference = totalDifference,
                    PowerAppsVat = powerAppsRow?.Vat ?? 0m,
                    SiigoVat = siigoRow?.Vat ?? 0m,
                    VatDifference = vatDifference
                };
            })
            .OrderBy(static row => row.IsOk ? 1 : 0)
            .ThenBy(static row => row.Status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveBillingStatus(
        SiigoBillingSummary? siigoRow,
        DataverseBillingGroup? dataverseRow,
        decimal totalDifference,
        decimal vatDifference)
    {
        if (siigoRow is null)
            return "Solo Dataverse";

        if (dataverseRow is null)
            return "Falta en Dataverse";

        if (HasDifference(totalDifference) && HasDifference(vatDifference))
            return "Diferencia total e IVA";

        if (HasDifference(totalDifference))
            return "Diferencia total";

        if (HasDifference(vatDifference))
            return "Diferencia IVA";

        return "OK";
    }

    private string ResolveExpenseStatus(
        PowerAppsExpenseGroup? powerAppsRow,
        SiigoExpenseGroup? siigoRow,
        decimal totalDifference,
        decimal vatDifference)
    {
        if (powerAppsRow is null)
            return "Solo Siigo";

        if (siigoRow is null)
            return "Falta en Siigo";

        if (HasDifference(totalDifference) && HasDifference(vatDifference))
            return "Diferencia total e IVA";

        if (HasDifference(totalDifference))
            return "Diferencia total";

        if (HasDifference(vatDifference))
            return "Diferencia IVA";

        return "OK";
    }

    private bool HasDifference(decimal value) =>
        Math.Abs(value) > Math.Max(0m, _options.DifferenceTolerance);

    private byte[] BuildWorkbook(
        string periodLabel,
        FinancialReconciliationSummary beforeSummary,
        FinancialReconciliationSummary afterSummary,
        FinancialReconciliationCorrectionResult corrections,
        IReadOnlyList<BillingComparisonRow> beforeBillingComparisons,
        IReadOnlyList<BillingComparisonRow> afterBillingComparisons,
        IReadOnlyList<ExpenseComparisonRow> expenseComparisons,
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        IReadOnlyList<ReconciliationDataverseExpenseRow> dataverseExpenses,
        SiigoFinancialReconciliationData siigo)
    {
        using var workbook = new XLWorkbook();
        AddSummaryWorksheet(workbook, periodLabel, beforeSummary, afterSummary, corrections);
        AddBillingDifferencesWorksheet(workbook, "Facturacion antes", beforeBillingComparisons);
        AddBillingDifferencesWorksheet(workbook, "Facturacion despues", afterBillingComparisons);
        AddCorrectionsWorksheet(workbook, corrections.Actions);
        AddExpenseDifferencesWorksheet(workbook, expenseComparisons);
        AddSiigoInvoicesWorksheet(workbook, siigo.Invoices);
        AddSiigoCreditNotesWorksheet(workbook, siigo.CreditNotes);
        AddDataverseBillingWorksheet(workbook, dataverseBilling);
        AddDataverseCreditNotesWorksheet(workbook, dataverseCreditNotes);
        AddSiigoPurchasesWorksheet(workbook, siigo.Purchases);
        AddPowerAppsExpensesWorksheet(workbook, dataverseExpenses);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSummaryWorksheet(
        XLWorkbook workbook,
        string periodLabel,
        FinancialReconciliationSummary beforeSummary,
        FinancialReconciliationSummary afterSummary,
        FinancialReconciliationCorrectionResult corrections)
    {
        var sheet = workbook.Worksheets.Add("Resumen");
        sheet.Cell(1, 1).Value = "Conciliacion financiera";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(2, 1).Value = "Periodo";
        sheet.Cell(2, 2).Value = periodLabel;
        sheet.Cell(3, 1).Value = "Generado";
        sheet.Cell(3, 2).Value = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var row = 5;
        WriteHeader(sheet, row++, "Concepto", "Base", "Dataverse antes", "Dif antes", "Dataverse despues", "Dif despues", "Notas");
        WriteSummaryRow(sheet, row++, "Facturacion", afterSummary.SiigoBillingNet, beforeSummary.DataverseBillingNet, beforeSummary.BillingDifference, afterSummary.DataverseBillingNet, afterSummary.BillingDifference, "Base Siigo: facturas menos notas credito. Dataverse: facturacion menos NC.");
        WriteSummaryRow(sheet, row++, "IVA generado", afterSummary.SiigoVatNet, beforeSummary.DataverseVatNet, beforeSummary.BillingVatDifference, afterSummary.DataverseVatNet, afterSummary.BillingVatDifference, "Base Siigo: IVA facturado menos IVA en NC.");
        WriteSummaryRow(sheet, row++, "Gastos", afterSummary.PowerAppsExpenses, beforeSummary.SiigoExpenses, beforeSummary.ExpenseDifference, afterSummary.SiigoExpenses, afterSummary.ExpenseDifference, "Base Power Apps. Diferencia = Siigo - Power Apps.");
        WriteSummaryRow(sheet, row++, "IVA pagado", afterSummary.PowerAppsExpenseVat, beforeSummary.SiigoExpenseVat, beforeSummary.ExpenseVatDifference, afterSummary.SiigoExpenseVat, afterSummary.ExpenseVatDifference, "Base Power Apps.");

        row += 2;
        WriteHeader(sheet, row++, "Detalle", "Valor");
        WritePair(sheet, row++, "Siigo facturacion bruta", afterSummary.SiigoBillingGross);
        WritePair(sheet, row++, "Siigo notas credito", afterSummary.SiigoBillingCreditNotes);
        WritePair(sheet, row++, "Siigo facturacion neta", afterSummary.SiigoBillingNet);
        WritePair(sheet, row++, "Dataverse facturacion bruta despues", afterSummary.DataverseBillingGross);
        WritePair(sheet, row++, "Dataverse NC despues", afterSummary.DataverseBillingCreditNotes);
        WritePair(sheet, row++, "Dataverse facturacion neta despues", afterSummary.DataverseBillingNet);
        WritePair(sheet, row++, "Siigo IVA neto", afterSummary.SiigoVatNet);
        WritePair(sheet, row++, "Dataverse IVA neto despues", afterSummary.DataverseVatNet);
        WritePair(sheet, row++, "Diferencias facturacion antes", beforeSummary.BillingDifferenceCount);
        WritePair(sheet, row++, "Diferencias facturacion despues", afterSummary.BillingDifferenceCount);
        WritePair(sheet, row++, "Diferencias gastos", afterSummary.ExpenseDifferenceCount);
        WritePair(sheet, row++, "Facturas creadas", corrections.CreatedInvoices);
        WritePair(sheet, row++, "Facturas actualizadas", corrections.UpdatedInvoices);
        WritePair(sheet, row++, "NC creadas", corrections.CreatedCreditNotes);
        WritePair(sheet, row++, "NC actualizadas", corrections.UpdatedCreditNotes);
        WritePair(sheet, row++, "Errores conciliacion", corrections.Errors);

        FormatUsedRange(sheet);
    }

    private void AddBillingDifferencesWorksheet(
        XLWorkbook workbook,
        string sheetName,
        IReadOnlyList<BillingComparisonRow> rows)
    {
        var sheet = workbook.Worksheets.Add(sheetName);
        WriteHeader(
            sheet,
            1,
            "Estado",
            "Factura",
            "Cliente/NIT",
            "Siigo bruto",
            "NC Siigo",
            "Siigo neto",
            "Dataverse bruto",
            "NC Dataverse",
            "Dataverse neto",
            "Dif total",
            "Siigo IVA bruto",
            "NC IVA Siigo",
            "Siigo IVA neto",
            "Dataverse IVA bruto",
            "NC IVA Dataverse",
            "Dataverse IVA neto",
            "Dif IVA",
            "Notas");

        var rowIndex = 2;
        foreach (var row in rows.Where(row => !row.IsOk))
        {
            WriteRow(
                sheet,
                rowIndex++,
                row.Status,
                row.InvoiceNumber,
                row.ClientName,
                row.SiigoGrossTotal,
                row.SiigoCreditNoteTotal,
                row.SiigoNetTotal,
                row.DataverseGrossTotal,
                row.DataverseCreditNoteTotal,
                row.DataverseNetTotal,
                row.TotalDifference,
                row.SiigoGrossVat,
                row.SiigoCreditNoteVat,
                row.SiigoNetVat,
                row.DataverseGrossVat,
                row.DataverseCreditNoteVat,
                row.DataverseNetVat,
                row.VatDifference,
                row.Notes);
        }

        if (rowIndex == 2)
            WriteRow(sheet, rowIndex, "OK", "Sin diferencias", "", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, "");

        FormatUsedRange(sheet);
    }

    private void AddExpenseDifferencesWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<ExpenseComparisonRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Gastos diferencias");
        WriteHeader(
            sheet,
            1,
            "Estado",
            "Factura proveedor",
            "Proveedor",
            "Fecha",
            "Power Apps",
            "Siigo",
            "Dif total",
            "Power Apps IVA",
            "Siigo IVA",
            "Dif IVA");

        var rowIndex = 2;
        foreach (var row in rows.Where(row => !row.IsOk))
        {
            WriteRow(
                sheet,
                rowIndex++,
                row.Status,
                row.InvoiceNumber,
                row.Supplier,
                FormatDate(row.Date),
                row.PowerAppsTotal,
                row.SiigoTotal,
                row.TotalDifference,
                row.PowerAppsVat,
                row.SiigoVat,
                row.VatDifference);
        }

        if (rowIndex == 2)
            WriteRow(sheet, rowIndex, "OK", "Sin diferencias", "", "", 0m, 0m, 0m, 0m, 0m, 0m);

        FormatUsedRange(sheet);
    }

    private static void AddCorrectionsWorksheet(
        XLWorkbook workbook,
        IReadOnlyList<FinancialReconciliationCorrectionAction> rows)
    {
        var sheet = workbook.Worksheets.Add("Ajustes Dataverse");
        WriteHeader(sheet, 1, "Entidad", "Accion", "Documento", "RecordId", "Total anterior", "Total nuevo", "IVA anterior", "IVA nuevo", "Notas");
        var rowIndex = 2;
        foreach (var row in rows)
        {
            WriteRow(
                sheet,
                rowIndex++,
                row.Entity,
                row.Action,
                row.Document,
                row.RecordId,
                row.PreviousTotal,
                row.NewTotal,
                row.PreviousVat,
                row.NewVat,
                row.Notes);
        }

        if (rowIndex == 2)
            WriteRow(sheet, rowIndex, "OK", "Sin ajustes", "", "", 0m, 0m, 0m, 0m, "");

        FormatUsedRange(sheet);
    }

    private static void AddSiigoInvoicesWorksheet(XLWorkbook workbook, IReadOnlyList<SiigoReconciliationInvoice> rows)
    {
        var sheet = workbook.Worksheets.Add("Siigo facturas");
        WriteHeader(sheet, 1, "Factura", "Fecha", "Cliente NIT", "Total", "IVA", "Anulada", "Id");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.Date).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.Name, FormatDate(row.Date), row.CustomerIdentification, row.Total, row.Vat, row.Annulled ? "Si" : "No", row.Id);
        }

        FormatUsedRange(sheet);
    }

    private static void AddSiigoCreditNotesWorksheet(XLWorkbook workbook, IReadOnlyList<SiigoReconciliationCreditNote> rows)
    {
        var sheet = workbook.Worksheets.Add("Siigo NC");
        WriteHeader(sheet, 1, "NC", "Fecha", "Factura afectada", "Cliente NIT", "Total", "IVA", "Id");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.Date).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.Name, FormatDate(row.Date), row.InvoiceName, row.CustomerIdentification, row.Total, row.Vat, row.Id);
        }

        FormatUsedRange(sheet);
    }

    private static void AddDataverseBillingWorksheet(XLWorkbook workbook, IReadOnlyList<ReconciliationDataverseBillingRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Dataverse facturas");
        WriteHeader(sheet, 1, "Factura", "Prefijo", "Codigo", "Fecha", "Cliente", "NIT empresa", "Total", "IVA", "Siigo ID", "RecordId");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.EmissionDate).ThenBy(static item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.InvoiceNumber, row.InvoicePrefix, row.InvoiceCode, FormatDate(row.EmissionDate), row.ClientName, row.CompanyTaxId, row.Total, row.Vat, row.SiigoInvoiceId, row.RecordId);
        }

        FormatUsedRange(sheet);
    }

    private static void AddDataverseCreditNotesWorksheet(XLWorkbook workbook, IReadOnlyList<ReconciliationDataverseCreditNoteRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Dataverse NC");
        WriteHeader(sheet, 1, "NC", "Fecha", "Factura afectada", "Cliente NIT", "Total", "IVA", "Procesada", "Match", "Siigo ID", "RecordId");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.Date).ThenBy(static item => item.CreditNoteName, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.CreditNoteName, FormatDate(row.Date), row.InvoiceName, row.CustomerIdentification, row.Total, row.Vat, row.Processed ? "Si" : "No", row.MatchBy, row.CreditNoteId, row.RecordId);
        }

        FormatUsedRange(sheet);
    }

    private static void AddSiigoPurchasesWorksheet(XLWorkbook workbook, IReadOnlyList<SiigoReconciliationPurchase> rows)
    {
        var sheet = workbook.Worksheets.Add("Siigo gastos");
        WriteHeader(sheet, 1, "Factura proveedor", "Comprobante Siigo", "Fecha", "Proveedor NIT", "Total", "IVA", "Saldo", "Id");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.Date).ThenBy(static item => item.ProviderInvoiceFullNumber, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.ProviderInvoiceFullNumber, row.Name, FormatDate(row.Date), row.SupplierIdentification, row.Total, row.Vat, row.Balance, row.Id);
        }

        FormatUsedRange(sheet);
    }

    private static void AddPowerAppsExpensesWorksheet(XLWorkbook workbook, IReadOnlyList<ReconciliationDataverseExpenseRow> rows)
    {
        var sheet = workbook.Worksheets.Add("PowerApps gastos");
        WriteHeader(sheet, 1, "Factura proveedor", "Fecha emision", "Fecha pago", "Proveedor", "Proveedor NIT", "Receptor", "Receptor NIT", "Total", "IVA", "Valor pago", "RecordId");
        var rowIndex = 2;
        foreach (var row in rows.OrderBy(static item => item.EmissionDate).ThenBy(static item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase))
        {
            WriteRow(sheet, rowIndex++, row.InvoiceNumber, FormatDate(row.EmissionDate), FormatDate(row.PaymentDate), row.IssuerName, row.IssuerNit, row.RecipientName, row.RecipientNit, row.Total, row.Vat, row.PaymentValue, row.RecordId);
        }

        FormatUsedRange(sheet);
    }

    private static void WriteSummaryRow(IXLWorksheet sheet, int row, string concept, decimal baseValue, decimal beforeValue, decimal beforeDifference, decimal afterValue, decimal afterDifference, string notes)
    {
        WriteRow(sheet, row, concept, baseValue, beforeValue, beforeDifference, afterValue, afterDifference, notes);
    }

    private static void WritePair(IXLWorksheet sheet, int row, string label, decimal value)
    {
        WriteRow(sheet, row, label, value);
    }

    private static void WritePair(IXLWorksheet sheet, int row, string label, int value)
    {
        WriteRow(sheet, row, label, value);
    }

    private static void WriteHeader(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var index = 0; index < headers.Length; index++)
        {
            var cell = sheet.Cell(row, index + 1);
            cell.Value = headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f4e78");
            cell.Style.Font.FontColor = XLColor.White;
        }
    }

    private static void WriteRow(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var cell = sheet.Cell(row, index + 1);
            var value = values[index];
            switch (value)
            {
                case decimal decimalValue:
                    cell.Value = decimalValue;
                    cell.Style.NumberFormat.Format = "$ #,##0.00;[Red]-$ #,##0.00";
                    break;
                case int intValue:
                    cell.Value = intValue;
                    break;
                default:
                    cell.Value = value?.ToString() ?? "";
                    break;
            }
        }
    }

    private static void FormatUsedRange(IXLWorksheet sheet)
    {
        var range = sheet.RangeUsed();
        if (range is null)
            return;

        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(1);
        sheet.ColumnsUsed().AdjustToContents();
    }

    private static string BuildEmailHtml(FinancialReconciliationReportResult report)
    {
        var before = report.BeforeSummary;
        var after = report.Summary;
        var corrections = report.Corrections;
        var builder = new StringBuilder();
        builder.Append("<p>Hola,</p>");
        builder.Append("<p>Adjunto en ZIP esta la conciliacion financiera de ");
        builder.Append(WebUtility.HtmlEncode(report.PeriodLabel));
        builder.Append(".</p>");
        builder.Append("<p><strong>Diferencias antes de conciliar</strong></p>");
        builder.Append("<ul>");
        builder.Append("<li>Facturacion: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(before.BillingDifference)));
        builder.Append(" de diferencia, ");
        builder.Append(before.BillingDifferenceCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" filas por revisar.</li>");
        builder.Append("<li>IVA generado: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(before.BillingVatDifference)));
        builder.Append(" de diferencia.</li>");
        builder.Append("<li>Gastos: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(before.ExpenseDifference)));
        builder.Append(" de diferencia, ");
        builder.Append(before.ExpenseDifferenceCount.ToString(CultureInfo.InvariantCulture));
        builder.Append(" filas por revisar.</li>");
        builder.Append("</ul>");
        builder.Append("<p><strong>Consolidacion despues de ajustar Dataverse</strong></p>");
        builder.Append("<ul>");
        builder.Append("<li>Facturacion Siigo neta: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.SiigoBillingNet)));
        builder.Append(" | Dataverse facturacion menos NC: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.DataverseBillingNet)));
        builder.Append(" | Diferencia: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.BillingDifference)));
        builder.Append(".</li>");
        builder.Append("<li>IVA Siigo neto: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.SiigoVatNet)));
        builder.Append(" | IVA Dataverse neto: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.DataverseVatNet)));
        builder.Append(" | Diferencia: ");
        builder.Append(WebUtility.HtmlEncode(FormatCurrency(after.BillingVatDifference)));
        builder.Append(".</li>");
        builder.Append("<li>Ajustes aplicados: ");
        builder.Append(corrections.Applied.ToString(CultureInfo.InvariantCulture));
        builder.Append(" (facturas creadas ");
        builder.Append(corrections.CreatedInvoices.ToString(CultureInfo.InvariantCulture));
        builder.Append(", facturas actualizadas ");
        builder.Append(corrections.UpdatedInvoices.ToString(CultureInfo.InvariantCulture));
        builder.Append(", NC creadas ");
        builder.Append(corrections.CreatedCreditNotes.ToString(CultureInfo.InvariantCulture));
        builder.Append(", NC actualizadas ");
        builder.Append(corrections.UpdatedCreditNotes.ToString(CultureInfo.InvariantCulture));
        builder.Append(", errores ");
        builder.Append(corrections.Errors.ToString(CultureInfo.InvariantCulture));
        builder.Append(").</li>");
        builder.Append("</ul>");
        builder.Append("<p>Base de facturacion: Siigo neto despues de notas credito. En Dataverse se cruza facturacion menos notas credito. Base de gastos: Power Apps.</p>");
        return builder.ToString();
    }

    private static byte[] BuildZipAttachment(string fileName, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        return stream.ToArray();
    }

    private string ResolveRecipientEmail()
    {
        var email = (_options.RecipientEmail ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Configura FinancialReconciliation:RecipientEmail.");

        return email;
    }

    private static string BuildDocumentKey(string? documentNumber, string fallbackPrefix, string? fallbackValue)
    {
        var normalized = NormalizeDocumentKey(documentNumber);
        if (!string.IsNullOrWhiteSpace(normalized))
            return $"DOC:{normalized}";

        var fallback = NormalizeDocumentKey(fallbackValue);
        return string.IsNullOrWhiteSpace(fallback)
            ? $"{fallbackPrefix}:empty"
            : $"{fallbackPrefix}:{fallback}";
    }

    private static string BuildExpenseKey(
        string? invoiceNumber,
        string? nit,
        string? name,
        DateOnly? date,
        decimal total,
        string fallbackValue)
    {
        var documentKey = NormalizeDocumentKey(invoiceNumber);
        if (!string.IsNullOrWhiteSpace(documentKey))
            return $"EXP-DOC:{documentKey}";

        var partyKey = FirstNonEmpty(NormalizeDocumentKey(nit), NormalizeDocumentKey(name), "empty");
        var dateKey = date?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "nodate";
        return $"EXP-ALT:{partyKey}:{dateKey}:{RoundCurrency(total):0.00}:{NormalizeDocumentKey(fallbackValue)}";
    }

    private static string NormalizeDocumentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static string FormatCurrency(decimal value) =>
        value.ToString("C0", ColombianCulture);

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal SumCurrency<T>(IEnumerable<T> rows, Func<T, decimal> selector) =>
        RoundCurrency(rows.Sum(selector));

    private static string ToTitleCase(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : ColombianCulture.TextInfo.ToTitleCase(value.Trim().ToLower(ColombianCulture));

    private sealed class BillingComparisonRow
    {
        public string Status { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public decimal SiigoGrossTotal { get; set; }
        public decimal SiigoCreditNoteTotal { get; set; }
        public decimal SiigoNetTotal { get; set; }
        public decimal DataverseGrossTotal { get; set; }
        public decimal DataverseCreditNoteTotal { get; set; }
        public decimal DataverseNetTotal { get; set; }
        public decimal TotalDifference { get; set; }
        public decimal SiigoGrossVat { get; set; }
        public decimal SiigoCreditNoteVat { get; set; }
        public decimal SiigoNetVat { get; set; }
        public decimal DataverseGrossVat { get; set; }
        public decimal DataverseCreditNoteVat { get; set; }
        public decimal DataverseNetVat { get; set; }
        public decimal VatDifference { get; set; }
        public string Notes { get; set; } = "";
        public bool IsOk => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SiigoBillingSummary
    {
        public string InvoiceNumber { get; set; } = "";
        public string CustomerIdentification { get; set; } = "";
        public decimal GrossTotal { get; set; }
        public decimal CreditNoteTotal { get; set; }
        public decimal NetTotal => RoundCurrency(GrossTotal - CreditNoteTotal);
        public decimal GrossVat { get; set; }
        public decimal CreditNoteVat { get; set; }
        public decimal NetVat => RoundCurrency(GrossVat - CreditNoteVat);
        public List<string> CreditNoteNames { get; } = new();
    }

    private sealed class DataverseBillingGroup
    {
        public string InvoiceNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public decimal GrossTotal { get; set; }
        public decimal CreditNoteTotal { get; set; }
        public decimal NetTotal => RoundCurrency(GrossTotal - CreditNoteTotal);
        public decimal GrossVat { get; set; }
        public decimal CreditNoteVat { get; set; }
        public decimal NetVat => RoundCurrency(GrossVat - CreditNoteVat);
        public int Count { get; set; }
        public List<string> CreditNoteNames { get; } = new();
    }

    private sealed class ExpenseComparisonRow
    {
        public string Status { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateOnly? Date { get; set; }
        public decimal PowerAppsTotal { get; set; }
        public decimal SiigoTotal { get; set; }
        public decimal TotalDifference { get; set; }
        public decimal PowerAppsVat { get; set; }
        public decimal SiigoVat { get; set; }
        public decimal VatDifference { get; set; }
        public bool IsOk => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PowerAppsExpenseGroup
    {
        public string InvoiceNumber { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateOnly? Date { get; set; }
        public decimal Total { get; set; }
        public decimal Vat { get; set; }
        public int Count { get; set; }
    }

    private sealed class SiigoExpenseGroup
    {
        public string InvoiceNumber { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateOnly? Date { get; set; }
        public decimal Total { get; set; }
        public decimal Vat { get; set; }
        public int Count { get; set; }
    }
}
