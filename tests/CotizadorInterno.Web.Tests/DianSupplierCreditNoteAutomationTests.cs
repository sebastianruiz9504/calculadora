using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DianSupplierCreditNoteAutomationTests
{
    [Fact]
    public void PurchaseIdentityUsesDeterministicDianPrefixWhenExportHasNoPrefix()
    {
        var identity = new DianSupplierPurchasePayloadFactory().ResolveIdentity(
            Invoice("0000408409", "830068318", "2026-07-02", 166435920m, 0m, ""));

        Assert.Equal("DIAN", identity.Prefix);
        Assert.Equal("0000408409", identity.Number);
    }

    [Fact]
    public void ResolvesExactSupplierInvoiceBeforeTheCreditNote()
    {
        var note = CreditNote("432-C0021290", "900006621", "2026-07-14", 198254m, 31654m, "432");
        var expected = Invoice("B-16980", "900006621", "2026-07-02", 198254m, 31654m, "B");
        var unrelated = Invoice("B-1", "900000000", "2026-07-01", 198254m, 31654m, "B");

        var resolved = DianSupplierCreditNoteAutomationService.ResolveSourceInvoice(
            note,
            [unrelated, expected]);

        Assert.Equal("Resolved", resolved.Status);
        Assert.Same(expected, resolved.Invoice);
    }

    [Fact]
    public void ResolvesUniqueSamePrefixInvoiceForPartialSupplierCreditNotes()
    {
        var note = CreditNote("432-0000069072", "830068318", "2026-07-14", 1033077m, 0m, "432");
        var expected = Invoice("432-0000408409", "830068318", "2026-07-02", 166435920m, 0m, "432");
        var other = Invoice("OTRA-10", "830068318", "2026-07-01", 5000000m, 0m, "OTRA");

        var resolved = DianSupplierCreditNoteAutomationService.ResolveSourceInvoice(
            note,
            [other, expected]);

        Assert.Equal("Resolved", resolved.Status);
        Assert.Same(expected, resolved.Invoice);
    }

    [Fact]
    public void StopsWhenMoreThanOneSourceInvoiceRemains()
    {
        var note = CreditNote("NC-10", "900123456", "2026-07-14", 100000m, 0m, "NC");
        var first = Invoice("FC-1", "900123456", "2026-07-01", 500000m, 0m, "A");
        var second = Invoice("FC-2", "900123456", "2026-07-02", 600000m, 0m, "B");

        var resolved = DianSupplierCreditNoteAutomationService.ResolveSourceInvoice(
            note,
            [first, second]);

        Assert.Equal("Ambiguous", resolved.Status);
        Assert.Null(resolved.Invoice);
    }

    [Fact]
    public void JournalDebitsTheProviderInvoiceDueAndCreditsExpenseAndVat()
    {
        var note = CreditNote("432-C0021290", "900006621", "2026-07-14", 198254m, 31654m, "432");
        note.BaseAmount = 166600m;
        note.Cufe = "cude-1";
        var invoice = Invoice("B-16980", "900006621", "2026-07-02", 198254m, 31654m, "B");
        invoice.AccountCode = "613510";
        var purchase = new SiigoReconciliationPurchase
        {
            Id = "purchase-1",
            Name = "FC-1-651",
            Date = new DateOnly(2026, 7, 2),
            ProviderInvoicePrefix = "B",
            ProviderInvoiceNumber = "16980",
            PaymentDueDate = new DateOnly(2026, 7, 2),
            Balance = 198254m
        };
        var document = new SiigoDocumentTypeLookupDto
        {
            Id = 7502,
            Type = "CC",
            Code = "1",
            Name = "Ajustes contables",
            Active = true
        };
        var issues = new List<string>();

        var payload = DianSupplierCreditNoteAutomationService.BuildJournalPayload(
            note,
            invoice,
            purchase,
            document,
            issues);
        var json = JsonSerializer.Serialize(payload);

        Assert.Empty(issues);
        Assert.Contains("\"prefix\":\"B\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consecutive\":16980", json, StringComparison.Ordinal);
        Assert.Contains("\"date\":\"2026-07-02\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"22050501\",\"movement\":\"Debit\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"613510\",\"movement\":\"Credit\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"240803\",\"movement\":\"Credit\"", json, StringComparison.Ordinal);
        Assert.Contains("\"customer\":{\"identification\":\"900006621\",\"branch_office\":0}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingJournalMustReferenceTheProviderInvoiceDueDate()
    {
        var note = CreditNote("C0021290", "900006621", "2026-07-14", 198254m, 31654m, "");
        var purchase = new SiigoReconciliationPurchase
        {
            Id = "purchase-1",
            Name = "FC-1-651",
            Date = new DateOnly(2026, 7, 2),
            ProviderInvoicePrefix = "B",
            ProviderInvoiceNumber = "16980",
            PaymentDueDate = new DateOnly(2026, 7, 2),
            Total = 198254m,
            Balance = 198254m
        };
        var wrongReceipt = new SiigoVoucherCreateResultDto
        {
            Id = "receipt-1",
            Name = "RP-1-5",
            RawJson = """
                {
                  "items": [{
                    "account": { "code": "22050501", "movement": "Debit" },
                    "customer": { "identification": "900006621" },
                    "due": { "prefix": "B", "consecutive": 16980, "quote": 1, "date": "2026-07-14" },
                    "value": 198254
                  }]
                }
                """
        };
        var correctReceipt = new SiigoVoucherCreateResultDto
        {
            Id = "receipt-2",
            Name = "RP-1-6",
            RawJson = wrongReceipt.RawJson.Replace("2026-07-14", "2026-07-02", StringComparison.Ordinal)
        };

        Assert.False(
            DianSupplierCreditNoteAutomationService.ValidateExistingJournal(
                wrongReceipt,
                note,
                purchase,
                out var wrongIssue));
        Assert.Contains("fecha 2026-07-02", wrongIssue, StringComparison.Ordinal);
        Assert.True(
            DianSupplierCreditNoteAutomationService.ValidateExistingJournal(
                correctReceipt,
                note,
                purchase,
                out var correctIssue));
        Assert.Empty(correctIssue);
    }

    [Fact]
    public void PurchaseDueUsesProviderInvoiceAndPreservesItsNumericIdentity()
    {
        var purchase = new SiigoReconciliationPurchase
        {
            Id = "purchase-xcb",
            Name = "FC-1-676",
            ProviderInvoicePrefix = "DIAN",
            ProviderInvoiceNumber = "0000408409"
        };

        var resolved = DianSupplierCreditNoteAutomationService.TryResolvePurchaseDue(
            purchase,
            out var due,
            out var issue);

        Assert.True(resolved);
        Assert.Empty(issue);
        Assert.Equal("DIAN", due.Prefix);
        Assert.Equal(408409, due.Consecutive);
        Assert.Equal("DIAN-408409, cuota 1", due.Label);
    }

    [Fact]
    public void HistoryReportsSupplierCreditNotesSeparately()
    {
        var manifest = new DeduccionesIvaImportHistoryManifestDto
        {
            ImportId = "import-credit-note",
            Year = 2026,
            Month = 7,
            ImportedAtUtc = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero),
            ExternalKeys = ["invoice", "note"]
        };
        var rows = new[]
        {
            new ConciliacionDianSupplierInvoiceRowDto
            {
                ExcelKey = "invoice",
                DocumentType = "Factura electrónica de Venta",
                Stage = "enviadas",
                SiigoDocumentId = "purchase-1"
            },
            new ConciliacionDianSupplierInvoiceRowDto
            {
                ExcelKey = "note",
                DocumentType = "Nota de Crédito",
                Stage = "enviadas",
                SiigoDocumentId = "journal-1",
                TotalValue = 198254m
            }
        };

        var history = DeduccionesIvaImportHistoryService.BuildEntry(manifest, rows);

        Assert.Equal(1, history.SupplierCreditNotes);
        Assert.Equal(1, history.SupplierCreditNotesApplied);
        Assert.Equal(198254m, history.SupplierCreditNoteValue);
        Assert.Single(history.Documents, static row => row.IsSupplierCreditNote);
    }

    private static ConciliacionDianSupplierInvoiceRowDto CreditNote(
        string number,
        string nit,
        string date,
        decimal total,
        decimal vat,
        string prefix) =>
        new()
        {
            InvoiceNumber = number,
            SupplierNit = nit,
            EmissionDateValue = date,
            TotalValue = total,
            VatValue = vat,
            Prefix = prefix,
            DocumentType = "Nota de Crédito",
            DianGroup = "Recibido"
        };

    private static ConciliacionDianSupplierInvoiceRowDto Invoice(
        string number,
        string nit,
        string date,
        decimal total,
        decimal vat,
        string prefix) =>
        new()
        {
            InvoiceNumber = number,
            SupplierNit = nit,
            EmissionDateValue = date,
            TotalValue = total,
            VatValue = vat,
            Prefix = prefix,
            DocumentType = "Factura electrónica de Venta",
            DianGroup = "Recibido"
        };
}
