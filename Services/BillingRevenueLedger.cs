using System.Globalization;
using System.Text;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

/// <summary>
/// Canonical Siigo revenue movement. Amounts are signed: invoices are positive and
/// credit notes are negative. The document date determines the accounting period.
/// </summary>
internal sealed record BillingRevenueMovement
{
    public string DocumentId { get; init; } = "";
    public string DocumentName { get; init; } = "";
    public DateOnly DocumentDate { get; init; }
    public bool IsCreditNote { get; init; }
    public decimal ApiTotal { get; init; }
    public decimal SuggestedWithholdingTotal { get; init; }
    public decimal GrossTotal { get; init; }
    public decimal Vat { get; init; }
    public decimal RevenueBeforeVat => BillingRevenueLedger.Round(GrossTotal - Vat);
    public string InvoiceId { get; init; } = "";
    public string InvoiceName { get; init; } = "";
    public string InvoicePrefix { get; init; } = "";
    public long? InvoiceNumber { get; init; }
    public string CustomerId { get; init; } = "";
    public string CustomerIdentification { get; init; } = "";
}

internal static class BillingRevenueLedger
{
    public static IReadOnlyList<BillingRevenueMovement> Build(
        SiigoFinancialReconciliationData documents,
        DateOnly startInclusive,
        DateOnly endExclusive)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (startInclusive >= endExclusive)
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "El fin exclusivo debe ser posterior al inicio.");

        var movements = new List<BillingRevenueMovement>();

        foreach (var invoice in documents.Invoices
                     .Where(invoice => IsInsidePeriod(invoice.Date, startInclusive, endExclusive))
                     .Where(IsAcceptedInvoice)
                     .DistinctBy(BuildInvoiceDeduplicationKey, StringComparer.OrdinalIgnoreCase))
        {
            var grossTotal = ResolveGrossTotal(
                invoice.Total,
                invoice.SuggestedWithholdingTotal,
                invoice.GrossTotal);

            movements.Add(new BillingRevenueMovement
            {
                DocumentId = invoice.Id.Trim(),
                DocumentName = invoice.Name.Trim(),
                DocumentDate = invoice.Date!.Value,
                ApiTotal = Round(invoice.Total),
                SuggestedWithholdingTotal = Round(invoice.SuggestedWithholdingTotal),
                GrossTotal = grossTotal,
                Vat = Round(invoice.Vat),
                InvoiceId = invoice.Id.Trim(),
                InvoiceName = invoice.Name.Trim(),
                InvoicePrefix = invoice.Prefix.Trim(),
                InvoiceNumber = invoice.Number,
                CustomerId = invoice.CustomerId.Trim(),
                CustomerIdentification = invoice.CustomerIdentification.Trim()
            });
        }

        foreach (var creditNote in documents.CreditNotes
                     .Where(note => IsInsidePeriod(note.Date, startInclusive, endExclusive))
                     .Where(IsAcceptedCreditNote)
                     .DistinctBy(BuildCreditNoteDeduplicationKey, StringComparer.OrdinalIgnoreCase))
        {
            var grossTotal = ResolveGrossTotal(
                creditNote.Total,
                creditNote.SuggestedWithholdingTotal,
                creditNote.GrossTotal);

            movements.Add(new BillingRevenueMovement
            {
                DocumentId = creditNote.Id.Trim(),
                DocumentName = creditNote.Name.Trim(),
                DocumentDate = creditNote.Date!.Value,
                IsCreditNote = true,
                ApiTotal = -Round(creditNote.Total),
                SuggestedWithholdingTotal = -Round(creditNote.SuggestedWithholdingTotal),
                GrossTotal = -grossTotal,
                Vat = -Round(creditNote.Vat),
                InvoiceId = creditNote.InvoiceId.Trim(),
                InvoiceName = creditNote.InvoiceName.Trim(),
                InvoicePrefix = creditNote.InvoicePrefix.Trim(),
                InvoiceNumber = creditNote.InvoiceNumber,
                CustomerId = creditNote.CustomerId.Trim(),
                CustomerIdentification = creditNote.CustomerIdentification.Trim()
            });
        }

        return movements
            .OrderBy(static movement => movement.DocumentDate)
            .ThenBy(static movement => movement.IsCreditNote)
            .ThenBy(static movement => movement.DocumentName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsAcceptedInvoice(SiigoReconciliationInvoice invoice) =>
        !invoice.Annulled
        && string.Equals(invoice.StampStatus?.Trim(), "Accepted", StringComparison.OrdinalIgnoreCase);

    private static bool IsAcceptedCreditNote(SiigoReconciliationCreditNote creditNote) =>
        string.Equals(creditNote.StampStatus?.Trim(), "Accepted", StringComparison.OrdinalIgnoreCase);

    private static bool IsInsidePeriod(DateOnly? date, DateOnly startInclusive, DateOnly endExclusive) =>
        date.HasValue && date.Value >= startInclusive && date.Value < endExclusive;

    private static decimal ResolveGrossTotal(decimal apiTotal, decimal suggestedWithholdingTotal, decimal mappedGrossTotal)
    {
        var calculated = Round(apiTotal + suggestedWithholdingTotal);
        return mappedGrossTotal == 0m && calculated != 0m
            ? calculated
            : Round(mappedGrossTotal);
    }

    private static string BuildInvoiceDeduplicationKey(SiigoReconciliationInvoice invoice) =>
        BuildDeduplicationKey("FV", invoice.Id, invoice.Name, invoice.Date, invoice.Number);

    private static string BuildCreditNoteDeduplicationKey(SiigoReconciliationCreditNote creditNote) =>
        BuildDeduplicationKey("NC", creditNote.Id, creditNote.Name, creditNote.Date, creditNote.Number);

    private static string BuildDeduplicationKey(
        string kind,
        string? id,
        string? name,
        DateOnly? date,
        long? number)
    {
        if (!string.IsNullOrWhiteSpace(id))
            return $"{kind}:ID:{id.Trim()}";

        var normalizedName = NormalizeDocumentKey(name);
        var numberValue = number?.ToString(CultureInfo.InvariantCulture) ?? "";
        return $"{kind}:DOC:{normalizedName}:{numberValue}:{date:yyyy-MM-dd}";
    }

    private static string NormalizeDocumentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
