using System.Globalization;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed class DeduccionesIvaImportHistoryService : IDeduccionesIvaImportHistoryService
{
    private static readonly CultureInfo ColombiaCulture = CultureInfo.GetCultureInfo("es-CO");
    private readonly IDeduccionesIvaSharePointStorageService _storage;
    private readonly IDataverseService _dataverse;
    private readonly IDianSupplierDocumentImportService? _import;

    public DeduccionesIvaImportHistoryService(
        IDeduccionesIvaSharePointStorageService storage,
        IDataverseService dataverse)
    {
        _storage = storage;
        _dataverse = dataverse;
    }

    public DeduccionesIvaImportHistoryService(
        IDeduccionesIvaSharePointStorageService storage,
        IDataverseService dataverse,
        IDianSupplierDocumentImportService import)
    {
        _storage = storage;
        _dataverse = dataverse;
        _import = import;
    }

    public Task RecordAsync(
        string originalFileName,
        DeduccionesIvaSharePointUploadResult upload,
        DateOnly periodStart,
        string importedBy,
        DianSupplierDocumentImportResultDto import,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(import);
        var periods = NormalizePeriods(import.Periods, periodStart);
        var primaryPeriod = ParsePeriod(periods[0]) ?? periodStart;

        var manifest = new DeduccionesIvaImportHistoryManifestDto
        {
            ImportId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            OriginalFileName = Path.GetFileName(originalFileName ?? ""),
            StoredFileName = upload.StoredFileName,
            SharePointWebUrl = upload.WebUrl,
            Year = primaryPeriod.Year,
            Month = primaryPeriod.Month,
            Periods = periods.ToList(),
            ImportedAtUtc = DateTimeOffset.UtcNow,
            ImportedBy = (importedBy ?? "").Trim(),
            DryRun = import.DryRun,
            RowsRead = import.RowsRead,
            ImportableRows = import.ImportableRows,
            SupplierCreditNoteRows = import.SupplierCreditNoteRows,
            PayrollRows = import.PayrollRows,
            Created = import.Created,
            Updated = import.Updated,
            Unchanged = import.Unchanged,
            SkippedRows = import.SkippedRows,
            TotalValue = import.TotalValue,
            VatValue = import.VatValue,
            SupplierCreditNoteValue = import.SampleRows
                .Where(static row => row.DocumentKind.Equals("NotaCreditoProveedor", StringComparison.OrdinalIgnoreCase))
                .Sum(static row => row.TotalValue),
            ExternalKeys = import.UpsertRows
                .Select(static row => row.ExternalKey?.Trim() ?? "")
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Skipped = import.Skipped.Take(500).ToList()
        };

        return _storage.SaveImportHistoryAsync(manifest, ct);
    }

    public async Task<IReadOnlyList<DeduccionesIvaImportHistoryEntryDto>> GetHistoryAsync(
        int top = 25,
        CancellationToken ct = default)
    {
        var manifests = await _storage.GetImportHistoryAsync(Math.Clamp(top, 1, 100), ct);
        if (manifests.Count == 0)
            return Array.Empty<DeduccionesIvaImportHistoryEntryDto>();

        var validPeriods = manifests
            .SelectMany(EnumerateManifestPeriods)
            .Select(static period => (period.Year, period.Month))
            .Distinct()
            .ToArray();
        var periodTasks = validPeriods.ToDictionary(
            static period => period,
            period => _dataverse.GetConciliacionDianSupplierDocumentsForHistoryAsync(
                new DateOnly(period.Year, period.Month, 1),
                new DateOnly(period.Year, period.Month, 1).AddMonths(1),
                ct));
        await Task.WhenAll(periodTasks.Values);

        var rowsByPeriod = periodTasks.ToDictionary(
            static item => item.Key,
            static item => item.Value.Result);
        return manifests
            .OrderByDescending(static item => item.ImportedAtUtc)
            .Select(manifest =>
            {
                var periodRows = EnumerateManifestPeriods(manifest)
                    .SelectMany(period => rowsByPeriod.TryGetValue((period.Year, period.Month), out var rows)
                        ? rows
                        : Array.Empty<ConciliacionDianSupplierInvoiceRowDto>())
                    .GroupBy(static row => FirstNonEmpty(row.RecordId, row.ExcelKey), StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
                    .ToArray();
                return BuildEntry(manifest, periodRows);
            })
            .ToArray();
    }

    public async Task<DianSupplierDocumentImportResultDto> ReprocessLatestAsync(
        CancellationToken ct = default)
    {
        var manifest = (await _storage.GetImportHistoryAsync(100, ct))
            .Where(static item => !item.DryRun
                && !string.IsNullOrWhiteSpace(item.StoredFileName)
                && item.Year is >= 2020 and <= 2100
                && item.Month is >= 1 and <= 12)
            .OrderByDescending(static item => item.ImportedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No hay una importacion real de deducciones IVA para reprocesar.");
        if (_import is null)
            throw new InvalidOperationException("El servicio de reimportacion DIAN no esta disponible.");
        var storedFile = await _storage.DownloadAsync(manifest.StoredFileName, ct);
        await using var stream = new MemoryStream(storedFile.Content, writable: false);
        var result = await _import.ImportAsync(
            stream,
            storedFile.StoredFileName,
            dryRun: false,
            ct: ct,
            periodStart: new DateOnly(manifest.Year, manifest.Month, 1));

        var existingKeys = manifest.ExternalKeys
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newlyAdded = result.UpsertRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ExternalKey)
                && !existingKeys.Contains(row.ExternalKey))
            .ToArray();
        foreach (var key in result.UpsertRows
                     .Select(static row => row.ExternalKey?.Trim() ?? "")
                     .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            existingKeys.Add(key);
        }

        var periods = NormalizePeriods(result.Periods, new DateOnly(manifest.Year, manifest.Month, 1));
        var primaryPeriod = ParsePeriod(periods[0]) ?? new DateOnly(manifest.Year, manifest.Month, 1);
        manifest.Version = Math.Max(3, manifest.Version);
        manifest.Year = primaryPeriod.Year;
        manifest.Month = primaryPeriod.Month;
        manifest.Periods = periods.ToList();
        manifest.RowsRead = result.RowsRead;
        manifest.ImportableRows = existingKeys.Count;
        manifest.SupplierCreditNoteRows = result.SupplierCreditNoteRows;
        manifest.PayrollRows = result.PayrollRows;
        manifest.Created += newlyAdded.Count(static row =>
            row.Outcome.Equals("Created", StringComparison.OrdinalIgnoreCase));
        manifest.Updated += newlyAdded.Count(static row =>
            row.Outcome.Equals("Updated", StringComparison.OrdinalIgnoreCase));
        manifest.Unchanged += newlyAdded.Count(static row =>
            row.Outcome.Equals("Unchanged", StringComparison.OrdinalIgnoreCase));
        manifest.SkippedRows = result.SkippedRows;
        manifest.TotalValue = result.TotalValue;
        manifest.VatValue = result.VatValue;
        manifest.SupplierCreditNoteValue = result.SampleRows
            .Where(static row => row.DocumentKind.Equals("NotaCreditoProveedor", StringComparison.OrdinalIgnoreCase))
            .Sum(static row => row.TotalValue);
        manifest.ExternalKeys = existingKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToList();
        manifest.Skipped = result.Skipped.Take(500).ToList();
        await _storage.SaveImportHistoryAsync(manifest, ct);
        return result;
    }

    internal static DeduccionesIvaImportHistoryEntryDto BuildEntry(
        DeduccionesIvaImportHistoryManifestDto manifest,
        IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto> periodRows)
    {
        var externalKeys = manifest.ExternalKeys
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestPeriods = NormalizePeriods(manifest.Periods, ResolveManifestFallbackPeriod(manifest));
        var rows = periodRows
            .Where(row => externalKeys.Contains(row.ExcelKey))
            .OrderByDescending(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var payrollRows = rows.Where(IsPayroll).ToArray();
        var siigoRows = rows.Where(static row => !IsPayroll(row)).ToArray();
        var sent = siigoRows.Count(static row => !string.IsNullOrWhiteSpace(row.SiigoDocumentId));
        var supplierCreditNotes = siigoRows.Count(IsSupplierCreditNote);
        var supplierCreditNotesApplied = siigoRows.Count(static row =>
            IsSupplierCreditNote(row) && !string.IsNullOrWhiteSpace(row.SiigoDocumentId));
        var supplierCreditNoteValue = siigoRows
            .Where(IsSupplierCreditNote)
            .Sum(static row => row.TotalValue);
        var pendingRut = siigoRows.Count(static row => row.Stage.Equals("proveedor", StringComparison.OrdinalIgnoreCase));
        var pendingRutSuppliers = siigoRows
            .Where(static row => row.Stage.Equals("proveedor", StringComparison.OrdinalIgnoreCase))
            .Select(static row => NormalizeSupplierKey(row.SupplierNit, row.RecordId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var pendingClassification = siigoRows.Count(static row => row.Stage.Equals("clasificacion", StringComparison.OrdinalIgnoreCase));
        var withErrors = siigoRows.Count(IsHistoryError);
        var pendingSiigo = siigoRows.Count(row =>
            row.Stage.Equals("prevalidacion", StringComparison.OrdinalIgnoreCase)
            && !IsHistoryError(row));

        var (statusLabel, statusTone) = ResolveStatus(
            manifest.DryRun,
            siigoRows.Length,
            Math.Max(payrollRows.Length, manifest.PayrollRows),
            sent,
            pendingRut,
            pendingClassification,
            pendingSiigo,
            withErrors);
        var importedAtColombia = manifest.ImportedAtUtc.ToOffset(TimeSpan.FromHours(-5));

        return new DeduccionesIvaImportHistoryEntryDto
        {
            ImportId = manifest.ImportId,
            OriginalFileName = manifest.OriginalFileName,
            StoredFileName = manifest.StoredFileName,
            SharePointWebUrl = manifest.SharePointWebUrl,
            Year = manifest.Year,
            Month = manifest.Month,
            Periods = manifestPeriods,
            PeriodLabel = FormatPeriodLabel(manifestPeriods),
            ImportedAtUtc = manifest.ImportedAtUtc,
            ImportedAtDisplay = importedAtColombia.ToString("dd/MM/yyyy HH:mm", ColombiaCulture),
            ImportedBy = manifest.ImportedBy,
            DryRun = manifest.DryRun,
            RowsRead = manifest.RowsRead,
            ImportableRows = manifest.ImportableRows,
            Created = manifest.Created,
            Updated = manifest.Updated,
            Unchanged = manifest.Unchanged,
            SkippedRows = manifest.SkippedRows,
            TotalValue = manifest.TotalValue,
            VatValue = manifest.VatValue,
            CurrentRows = rows.Length,
            SiigoRows = siigoRows.Length,
            SentToSiigo = sent,
            SupplierCreditNotes = supplierCreditNotes,
            SupplierCreditNotesApplied = supplierCreditNotesApplied,
            PayrollRows = payrollRows.Length > 0 ? payrollRows.Length : manifest.PayrollRows,
            SupplierCreditNoteValue = supplierCreditNoteValue,
            PendingRut = pendingRut,
            PendingRutSuppliers = pendingRutSuppliers,
            PendingClassification = pendingClassification,
            PendingSiigo = pendingSiigo,
            WithErrors = withErrors,
            StatusLabel = statusLabel,
            StatusTone = statusTone,
            Skipped = manifest.Skipped ?? new List<DianSupplierDocumentSkippedRowDto>(),
            Documents = rows.Select(BuildDocument).ToArray()
        };
    }

    private static DeduccionesIvaImportHistoryDocumentDto BuildDocument(
        ConciliacionDianSupplierInvoiceRowDto row)
    {
        var isPayroll = IsPayroll(row);
        var needsRut = row.Stage.Equals("proveedor", StringComparison.OrdinalIgnoreCase);
        return new DeduccionesIvaImportHistoryDocumentDto
        {
            RecordId = row.RecordId,
            InvoiceNumber = row.InvoiceNumber,
            DocumentType = row.DocumentType,
            IsSupplierCreditNote = IsSupplierCreditNote(row),
            IsPayroll = isPayroll,
            SupplierNit = row.SupplierNit,
            SupplierName = row.SupplierName,
            EmissionDateDisplay = row.EmissionDateDisplay,
            TotalValue = row.TotalValue,
            AccountCode = row.AccountCode,
            SiigoDocumentName = row.SiigoDocumentName,
            StatusLabel = isPayroll ? "Guardada en Dataverse" : needsRut ? "RUT pendiente" : row.StageLabel,
            StatusTone = isPayroll ? "success" : needsRut ? "warning" : row.StageTone,
            Detail = isPayroll ? "Nomina importada unicamente a Dataverse; no se envia a Siigo." : row.ReviewReason,
            NeedsRut = !isPayroll && needsRut
        };
    }

    private static bool IsSupplierCreditNote(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var type = NormalizeDocumentType(row.DocumentType);
        return type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPayroll(ConciliacionDianSupplierInvoiceRowDto row) =>
        NormalizeDocumentType(row.DocumentType).Contains("NOMINA INDIVIDUAL", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDocumentType(string value)
    {
        var decomposed = (value ?? "").Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Normalize(System.Text.NormalizationForm.FormC)
            .Trim()
            .ToUpperInvariant();
    }

    private static bool IsHistoryError(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || row.StageTone.Equals("danger", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSupplierKey(string supplierNit, string recordId)
    {
        var digits = new string((supplierNit ?? "").Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? $"record:{recordId}" : $"nit:{digits}";
    }

    private static (string Label, string Tone) ResolveStatus(
        bool dryRun,
        int siigoRows,
        int payrollRows,
        int sent,
        int pendingRut,
        int pendingClassification,
        int pendingSiigo,
        int withErrors)
    {
        if (dryRun)
            return ("Simulacion", "info");
        if (withErrors > 0)
            return ("Con errores", "danger");
        if (pendingRut > 0)
            return ("Pendiente de RUT", "warning");
        if (pendingClassification > 0)
            return ("Pendiente de clasificacion", "warning");
        if (pendingSiigo > 0)
            return ("Pendiente de Siigo", "info");
        if (siigoRows > 0 && sent == siigoRows)
            return ("Completada", "success");
        if (siigoRows == 0 && payrollRows > 0)
            return ("Guardada en Dataverse", "success");
        return ("Sin detalle actual", "neutral");
    }

    private static IReadOnlyList<DateOnly> EnumerateManifestPeriods(DeduccionesIvaImportHistoryManifestDto manifest) =>
        NormalizePeriods(manifest.Periods, ResolveManifestFallbackPeriod(manifest))
            .Select(ParsePeriod)
            .Where(static period => period.HasValue)
            .Select(static period => period!.Value)
            .ToArray();

    private static IReadOnlyList<string> NormalizePeriods(
        IEnumerable<string>? periods,
        DateOnly fallbackPeriod)
    {
        var normalized = (periods ?? Array.Empty<string>())
            .Select(ParsePeriod)
            .Where(static period => period.HasValue)
            .Select(static period => period!.Value)
            .Distinct()
            .OrderBy(static period => period)
            .Select(static period => period.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToArray();
        return normalized.Length > 0
            ? normalized
            : [new DateOnly(fallbackPeriod.Year, fallbackPeriod.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture)];
    }

    private static DateOnly? ParsePeriod(string? value) =>
        DateOnly.TryParseExact(
            $"{(value ?? "").Trim()}-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var period)
            && period.Year is >= 2020 and <= 2100
            ? period
            : null;

    private static DateOnly ResolveManifestFallbackPeriod(DeduccionesIvaImportHistoryManifestDto manifest)
    {
        if (manifest.Year is >= 2020 and <= 2100 && manifest.Month is >= 1 and <= 12)
            return new DateOnly(manifest.Year, manifest.Month, 1);

        var importedAt = manifest.ImportedAtUtc == default
            ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5))
            : manifest.ImportedAtUtc.ToOffset(TimeSpan.FromHours(-5));
        return new DateOnly(importedAt.Year, importedAt.Month, 1);
    }

    private static string FormatPeriodLabel(IReadOnlyList<string> periods) =>
        periods.Count == 0
            ? "Periodo no disponible"
            : string.Join(", ", periods.Select(value =>
                ParsePeriod(value)?.ToString("MMMM yyyy", ColombiaCulture) ?? value));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
