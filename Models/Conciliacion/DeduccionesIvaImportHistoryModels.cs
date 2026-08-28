using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Models.Conciliacion;

public sealed class DeduccionesIvaImportHistoryManifestDto
{
    public int Version { get; set; } = 3;
    public string ImportId { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string SharePointWebUrl { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public List<string> Periods { get; set; } = new();
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ImportedBy { get; set; } = "";
    public bool DryRun { get; set; }
    public int RowsRead { get; set; }
    public int ImportableRows { get; set; }
    public int SupplierCreditNoteRows { get; set; }
    public int PayrollRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int SkippedRows { get; set; }
    public decimal TotalValue { get; set; }
    public decimal VatValue { get; set; }
    public decimal SupplierCreditNoteValue { get; set; }
    public List<string> ExternalKeys { get; set; } = new();
    public List<DianSupplierDocumentSkippedRowDto> Skipped { get; set; } = new();
}

public sealed class DeduccionesIvaImportHistoryEntryDto
{
    public string ImportId { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string SharePointWebUrl { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public IReadOnlyList<string> Periods { get; set; } = Array.Empty<string>();
    public string PeriodLabel { get; set; } = "";
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ImportedAtDisplay { get; set; } = "";
    public string ImportedBy { get; set; } = "";
    public bool DryRun { get; set; }
    public int RowsRead { get; set; }
    public int ImportableRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int SkippedRows { get; set; }
    public decimal TotalValue { get; set; }
    public decimal VatValue { get; set; }
    public int CurrentRows { get; set; }
    public int SiigoRows { get; set; }
    public int SentToSiigo { get; set; }
    public int SupplierCreditNotes { get; set; }
    public int SupplierCreditNotesApplied { get; set; }
    public int PayrollRows { get; set; }
    public decimal SupplierCreditNoteValue { get; set; }
    public int PendingRut { get; set; }
    public int PendingRutSuppliers { get; set; }
    public int PendingClassification { get; set; }
    public int PendingSiigo { get; set; }
    public int WithErrors { get; set; }
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public IReadOnlyList<DianSupplierDocumentSkippedRowDto> Skipped { get; set; } =
        Array.Empty<DianSupplierDocumentSkippedRowDto>();
    public IReadOnlyList<DeduccionesIvaImportHistoryDocumentDto> Documents { get; set; } =
        Array.Empty<DeduccionesIvaImportHistoryDocumentDto>();
}

public sealed class DeduccionesIvaImportHistoryDocumentDto
{
    public string RecordId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public bool IsSupplierCreditNote { get; set; }
    public bool IsPayroll { get; set; }
    public string SupplierNit { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string EmissionDateDisplay { get; set; } = "";
    public decimal TotalValue { get; set; }
    public string AccountCode { get; set; } = "";
    public string SiigoDocumentName { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string StatusTone { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool NeedsRut { get; set; }
}
