using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class DianSupplierDocumentImportService : IDianSupplierDocumentImportService
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    private readonly IDataverseService _dataverse;
    private readonly ISiigoService _siigo;
    private readonly IDianSupplierInvoiceAutomationService _invoiceAutomation;
    private readonly IDianSupplierCreditNoteAutomationService _creditNoteAutomation;
    private readonly DianSupplierDocumentImportOptions _options;
    private readonly ILogger<DianSupplierDocumentImportService> _logger;

    public DianSupplierDocumentImportService(
        IDataverseService dataverse,
        ISiigoService siigo,
        IDianSupplierInvoiceAutomationService invoiceAutomation,
        IOptions<DianSupplierDocumentImportOptions> options,
        ILogger<DianSupplierDocumentImportService> logger)
        : this(
            dataverse,
            siigo,
            invoiceAutomation,
            new NoOpDianSupplierCreditNoteAutomationService(),
            options,
            logger)
    {
    }

    public DianSupplierDocumentImportService(
        IDataverseService dataverse,
        ISiigoService siigo,
        IDianSupplierInvoiceAutomationService invoiceAutomation,
        IDianSupplierCreditNoteAutomationService creditNoteAutomation,
        IOptions<DianSupplierDocumentImportOptions> options,
        ILogger<DianSupplierDocumentImportService> logger)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _invoiceAutomation = invoiceAutomation;
        _creditNoteAutomation = creditNoteAutomation;
        _options = options.Value;
        _logger = logger;
    }

    private sealed class NoOpDianSupplierCreditNoteAutomationService : IDianSupplierCreditNoteAutomationService
    {
        public Task<DianSupplierCreditNoteAutomationResultDto> ProcessPeriodAsync(
            DateOnly periodStart,
            bool dryRun = false,
            IReadOnlySet<string>? externalKeys = null,
            CancellationToken ct = default) =>
            Task.FromResult(new DianSupplierCreditNoteAutomationResultDto
            {
                DryRun = dryRun,
                PeriodStart = periodStart,
                PeriodEndExclusive = periodStart.AddMonths(1),
                Completed = !dryRun,
                Status = dryRun ? "DryRunReady" : "Completed",
                Message = "La automatizacion de notas de proveedor no esta configurada en este contexto."
            });
    }

    public async Task<DianSupplierDocumentImportResultDto> ImportAsync(
        string? localFilePath = null,
        bool dryRun = false,
        CancellationToken ct = default,
        DateOnly? periodStart = null)
    {
        var path = FirstNonEmpty(localFilePath, _options.LocalFilePath).Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Indica la ruta local del Excel DIAN para importar.");

        if (!File.Exists(path))
            throw new InvalidOperationException($"No encontramos el archivo DIAN: {path}");

        await using var stream = File.OpenRead(path);
        return await ImportAsync(stream, Path.GetFileName(path), dryRun, ct, periodStart);
    }

    public async Task<DianSupplierDocumentImportResultDto> ImportAsync(
        Stream workbookStream,
        string sourceFileName,
        bool dryRun = false,
        CancellationToken ct = default,
        DateOnly? periodStart = null)
    {
        if (workbookStream is null)
            throw new InvalidOperationException("Debes adjuntar el ZIP o Excel DIAN para importar.");

        var resolvedSourceFileName = Path.GetFileName(FirstNonEmpty(sourceFileName, _options.FileName, "Reporte DIAN.xlsx"));
        var fallbackPeriodStart = ResolvePeriodStart(periodStart);
        var read = ReadSource(workbookStream, resolvedSourceFileName);
        var resolvedDryRun = dryRun || _options.DryRun;
        _logger.LogInformation(
            "Archivo DIAN leido desde {SourceFileName}: {Rows} filas importables de {RowsRead}. DryRun={DryRun}.",
            resolvedSourceFileName,
            read.Rows.Count,
            read.RowsRead,
            resolvedDryRun);

        return await ImportReadResultAsync(read, resolvedDryRun, resolvedSourceFileName, fallbackPeriodStart, ct);
    }

    private async Task<DianSupplierDocumentImportResultDto> ImportReadResultAsync(
        DianSupplierDocumentWorkbookReadResult read,
        bool resolvedDryRun,
        string sourceFileName,
        DateOnly fallbackPeriodStart,
        CancellationToken ct)
    {
        var upsert = await _dataverse.UpsertDianSupplierDocumentRowsAsync(read.Rows, resolvedDryRun, ct);
        var siigoRows = read.Rows.Where(IsSiigoEligibleDocument).ToArray();
        var supplierResolution = new DianSupplierDocumentSiigoSupplierResolutionResultDto();
        ExpenseAccountingRuleApplyResultDto? autoClassification = null;
        var autoClassificationMessage = "";
        DianSupplierInvoiceAutomationResultDto? siigoAutomation = null;
        DianSupplierCreditNoteAutomationResultDto? creditNoteAutomation = null;

        if (!resolvedDryRun && siigoRows.Length > 0)
        {
            supplierResolution = await ResolveSiigoSuppliersAsync(siigoRows, resolvedDryRun, ct);
            try
            {
                autoClassification = await ApplyAutoClassificationAsync(siigoRows, ct);
            }
            catch (Exception ex)
            {
                autoClassificationMessage = $"Importacion guardada, pero no se pudo aplicar la autoclasificacion: {ex.Message}";
                _logger.LogWarning(ex, "No se pudo aplicar autoclasificacion DIAN despues de importar {SourceFileName}.", sourceFileName);
            }
        }

        var periodGroups = siigoRows
            .GroupBy(row => ResolveDocumentPeriod(row) ?? fallbackPeriodStart)
            .OrderBy(static group => group.Key)
            .ToArray();
        if (resolvedDryRun && periodGroups.Length > 0)
        {
            var firstPeriod = periodGroups[0].Key;
            var lastPeriod = periodGroups[^1].Key;
            var invoiceCount = siigoRows.Count(static row =>
                row.DocumentKind.Equals("FacturaElectronica", StringComparison.OrdinalIgnoreCase));
            var creditNoteCount = siigoRows.Count(static row =>
                row.DocumentKind.Equals("NotaCreditoProveedor", StringComparison.OrdinalIgnoreCase));
            siigoAutomation = new DianSupplierInvoiceAutomationResultDto
            {
                DryRun = true,
                PeriodStart = firstPeriod,
                PeriodEndExclusive = lastPeriod.AddMonths(1),
                Eligible = invoiceCount,
                EligibleRows = invoiceCount,
                Status = "DryRunImportOnly",
                Message = $"La simulacion valida {periodGroups.Length:N0} periodo(s) del archivo sin crear registros ni compras en Siigo."
            };
            creditNoteAutomation = new DianSupplierCreditNoteAutomationResultDto
            {
                DryRun = true,
                PeriodStart = firstPeriod,
                PeriodEndExclusive = lastPeriod.AddMonths(1),
                Eligible = creditNoteCount,
                Status = "DryRunImportOnly",
                Message = $"La simulacion valida las notas de {periodGroups.Length:N0} periodo(s) sin aplicarlas en Siigo."
            };
        }
        else if (periodGroups.Length > 0)
        {
            var invoiceResults = new List<DianSupplierInvoiceAutomationResultDto>(periodGroups.Length);
            var creditNoteResults = new List<DianSupplierCreditNoteAutomationResultDto>(periodGroups.Length);
            foreach (var periodGroup in periodGroups)
            {
                var externalKeys = periodGroup
                    .Select(static row => row.ExternalKey)
                    .Where(static key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                try
                {
                    invoiceResults.Add(await _invoiceAutomation.ProcessPeriodAsync(
                        periodGroup.Key,
                        dryRun: false,
                        externalKeys: externalKeys,
                        ct: ct));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "La importacion DIAN quedo en Dataverse, pero fallo la automatizacion de facturas Siigo para {PeriodStart}.", periodGroup.Key);
                    invoiceResults.Add(BuildInvoiceAutomationError(periodGroup.Key, ex));
                }

                try
                {
                    creditNoteResults.Add(await _creditNoteAutomation.ProcessPeriodAsync(
                        periodGroup.Key,
                        dryRun: false,
                        externalKeys: externalKeys,
                        ct: ct));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "La importacion DIAN quedo en Dataverse, pero fallo la automatizacion de notas Siigo para {PeriodStart}.", periodGroup.Key);
                    creditNoteResults.Add(BuildCreditNoteAutomationError(periodGroup.Key, ex));
                }
            }

            siigoAutomation = AggregateInvoiceAutomationResults(invoiceResults);
            creditNoteAutomation = AggregateCreditNoteAutomationResults(creditNoteResults);
        }

        return BuildResult(
            read,
            upsert,
            supplierResolution,
            autoClassification,
            autoClassificationMessage,
            siigoAutomation,
            creditNoteAutomation,
            resolvedDryRun,
            sourceFileName,
            fallbackPeriodStart);
    }

    private static DianSupplierInvoiceAutomationResultDto BuildInvoiceAutomationError(DateOnly periodStart, Exception ex) =>
        new()
        {
            PeriodStart = periodStart,
            PeriodEndExclusive = periodStart.AddMonths(1),
            Completed = false,
            IsComplete = false,
            CanComplete = false,
            Status = "CompletedWithErrors",
            Failed = 1,
            Message = $"La importacion quedo guardada en Dataverse, pero fallo la automatizacion Siigo: {ex.Message}"
        };

    private static DianSupplierCreditNoteAutomationResultDto BuildCreditNoteAutomationError(DateOnly periodStart, Exception ex) =>
        new()
        {
            PeriodStart = periodStart,
            PeriodEndExclusive = periodStart.AddMonths(1),
            Completed = false,
            Status = "CompletedWithErrors",
            Failed = 1,
            Message = $"No fue posible ejecutar las notas de proveedor: {ex.Message}"
        };

    internal static DianSupplierInvoiceAutomationResultDto AggregateInvoiceAutomationResults(
        IReadOnlyList<DianSupplierInvoiceAutomationResultDto> results)
    {
        if (results.Count == 0)
            return new DianSupplierInvoiceAutomationResultDto();

        var failed = results.Sum(static item => item.Failed);
        var completed = results.All(static item => item.Completed);
        var isComplete = results.All(static item => item.IsComplete);
        return new DianSupplierInvoiceAutomationResultDto
        {
            DryRun = results.All(static item => item.DryRun),
            PeriodStart = results.Min(static item => item.PeriodStart),
            PeriodEndExclusive = results.Max(static item => item.PeriodEndExclusive),
            Completed = completed,
            Status = failed > 0 ? "CompletedWithErrors" : completed ? "Completed" : "Pending",
            Eligible = results.Sum(static item => item.Eligible),
            Created = results.Sum(static item => item.Created),
            ExistingLinked = results.Sum(static item => item.ExistingLinked),
            AlreadyImported = results.Sum(static item => item.AlreadyImported),
            PendingSupplierInvoices = results.Sum(static item => item.PendingSupplierInvoices),
            PendingClassification = results.Sum(static item => item.PendingClassification),
            ConcurrentProcessing = results.Sum(static item => item.ConcurrentProcessing),
            AmbiguousWritePending = results.Sum(static item => item.AmbiguousWritePending),
            RowsReviewed = results.Sum(static item => item.RowsReviewed),
            EligibleRows = results.Sum(static item => item.EligibleRows),
            FilteredOutRows = results.Sum(static item => item.FilteredOutRows),
            SupplierGroupsReviewed = results.Sum(static item => item.SupplierGroupsReviewed),
            SupplierGroupsFound = results.Sum(static item => item.SupplierGroupsFound),
            SupplierGroupsMissing = results.Sum(static item => item.SupplierGroupsMissing),
            SupplierLookupFailed = results.Sum(static item => item.SupplierLookupFailed),
            SupplierRowsAssociated = results.Sum(static item => item.SupplierRowsAssociated),
            AlreadyLinked = results.Sum(static item => item.AlreadyLinked),
            BlockedMissingAccount = results.Sum(static item => item.BlockedMissingAccount),
            ExistingPurchasesLinked = results.Sum(static item => item.ExistingPurchasesLinked),
            PurchasesReadyInDryRun = results.Sum(static item => item.PurchasesReadyInDryRun),
            PurchasesCreated = results.Sum(static item => item.PurchasesCreated),
            PurchasesRecoveredAfterAmbiguousError = results.Sum(static item => item.PurchasesRecoveredAfterAmbiguousError),
            Failed = failed,
            CanComplete = results.All(static item => item.CanComplete),
            IsComplete = isComplete,
            Message = BuildMultiPeriodAutomationMessage("facturas", results.Count, results.Select(static item => item.Message)),
            PendingSuppliers = results.SelectMany(static item => item.PendingSuppliers).ToArray(),
            Rows = results.SelectMany(static item => item.Rows).ToArray()
        };
    }

    internal static DianSupplierCreditNoteAutomationResultDto AggregateCreditNoteAutomationResults(
        IReadOnlyList<DianSupplierCreditNoteAutomationResultDto> results)
    {
        if (results.Count == 0)
            return new DianSupplierCreditNoteAutomationResultDto();

        var failed = results.Sum(static item => item.Failed);
        var completed = results.All(static item => item.Completed);
        return new DianSupplierCreditNoteAutomationResultDto
        {
            DryRun = results.All(static item => item.DryRun),
            PeriodStart = results.Min(static item => item.PeriodStart),
            PeriodEndExclusive = results.Max(static item => item.PeriodEndExclusive),
            Completed = completed,
            Status = failed > 0 ? "CompletedWithErrors" : completed ? "Completed" : "Pending",
            RowsReviewed = results.Sum(static item => item.RowsReviewed),
            Eligible = results.Sum(static item => item.Eligible),
            Applied = results.Sum(static item => item.Applied),
            AlreadyApplied = results.Sum(static item => item.AlreadyApplied),
            PendingSupplier = results.Sum(static item => item.PendingSupplier),
            PendingSourcePurchase = results.Sum(static item => item.PendingSourcePurchase),
            AmbiguousSourcePurchase = results.Sum(static item => item.AmbiguousSourcePurchase),
            ConcurrentProcessing = results.Sum(static item => item.ConcurrentProcessing),
            Failed = failed,
            AppliedValue = RoundCurrency(results.Sum(static item => item.AppliedValue)),
            Message = BuildMultiPeriodAutomationMessage("notas de proveedor", results.Count, results.Select(static item => item.Message)),
            Rows = results.SelectMany(static item => item.Rows).ToArray()
        };
    }

    private static string BuildMultiPeriodAutomationMessage(
        string operation,
        int periodCount,
        IEnumerable<string> messages)
    {
        var detail = string.Join(" ", messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return $"Automatizacion de {operation} ejecutada para {periodCount:N0} periodo(s). {detail}".Trim();
    }

    public async Task<DianSupplierDocumentSupplierLookupRunResultDto> ResolvePendingSuppliersAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo para validar proveedores DIAN no es valido.");

        var rows = await _dataverse.GetDianSupplierDocumentRowsForSupplierLookupAsync(startDate, endDate, onlyPending: true, ct);
        var supplierResolution = await ResolveSiigoSuppliersAsync(rows, dryRun, ct);
        ExpenseAccountingRuleApplyResultDto? autoClassification = null;
        var autoClassificationMessage = "";

        if (!dryRun && rows.Count > 0)
        {
            try
            {
                autoClassification = await ApplyAutoClassificationAsync(rows, ct);
            }
            catch (Exception ex)
            {
                autoClassificationMessage = $"Proveedores actualizados, pero no se pudo aplicar la autoclasificacion: {ex.Message}";
                _logger.LogWarning(ex, "No se pudo aplicar autoclasificacion DIAN despues de validar proveedores para {StartDate} - {EndDate}.", startDate, endDate);
            }
        }

        return new DianSupplierDocumentSupplierLookupRunResultDto
        {
            DryRun = dryRun,
            StartDate = startDate,
            EndDate = endDate,
            PendingRowsReviewed = rows.Count,
            SupplierLookupReviewed = supplierResolution.Reviewed,
            SupplierLookupFound = supplierResolution.Found,
            SupplierLookupMissing = supplierResolution.Missing,
            SupplierLookupFailed = supplierResolution.Failed,
            SupplierLookupRowsUpdated = supplierResolution.Updated,
            AutoClassificationReviewed = autoClassification?.Reviewed ?? 0,
            AutoClassificationUpdated = autoClassification?.Updated ?? 0,
            AutoClassificationAlreadyAssigned = autoClassification?.AlreadyAssigned ?? 0,
            AutoClassificationNoRule = autoClassification?.NoRule ?? 0,
            AutoClassificationInvalidRule = autoClassification?.InvalidRule ?? 0,
            AutoClassificationMessage = autoClassificationMessage
        };
    }

    private async Task<DianSupplierDocumentSiigoSupplierResolutionResultDto> ResolveSiigoSuppliersAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        bool dryRun,
        CancellationToken ct)
    {
        var uniqueSupplierNits = rows
            .Select(static row => row.SupplierNit)
            .Where(static nit => ExtractDigits(nit).Length >= 5)
            .GroupBy(static nit => ExtractDigits(nit), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var result = new DianSupplierDocumentSiigoSupplierResolutionResultDto
        {
            Reviewed = uniqueSupplierNits.Length
        };
        var resolvedSuppliers = new List<DianSupplierDocumentResolvedSupplierDto>();

        foreach (var supplierNit in uniqueSupplierNits)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var candidates = await _siigo.SearchCustomersAsync(supplierNit, top: 50, ct);
                var match = FindExactActiveSupplier(supplierNit, candidates);
                if (match is null)
                {
                    result.Missing++;
                    continue;
                }

                result.Found++;
                resolvedSuppliers.Add(new DianSupplierDocumentResolvedSupplierDto
                {
                    SupplierNit = supplierNit,
                    SiigoSupplierId = match.Id,
                    SiigoSupplierName = FirstNonEmpty(match.DisplayName, match.Name, match.CommercialName, match.Identification)
                });
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.LogWarning(ex, "No se pudo validar el proveedor DIAN {SupplierNit} contra Siigo.", supplierNit);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        if (resolvedSuppliers.Count == 0)
            return result;

        var dataverseResult = await _dataverse.ResolveDianSupplierDocumentSiigoSuppliersAsync(
            rows,
            resolvedSuppliers,
            dryRun: dryRun,
            ct);
        result.MatchedRows = dataverseResult.MatchedRows;
        result.Updated = dataverseResult.Updated;
        return result;
    }

    private async Task<IReadOnlyList<DianSupplierDocumentImportRowDto>> GetPeriodPendingSupplierRowsAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> importedRows,
        CancellationToken ct)
    {
        var dates = importedRows
            .Select(static row => row.ReceptionDate.HasValue
                ? DateOnly.FromDateTime(row.ReceptionDate.Value.ToOffset(TimeSpan.FromHours(-5)).DateTime)
                : (DateOnly?)null)
            .Where(static date => date.HasValue)
            .Select(static date => date!.Value)
            .ToArray();
        if (dates.Length == 0)
            return importedRows;

        var periodRows = await _dataverse.GetDianSupplierDocumentRowsForSupplierLookupAsync(
            dates.Min(),
            dates.Max(),
            onlyPending: true,
            ct);
        return periodRows.Count > 0 ? periodRows : importedRows;
    }

    private async Task<ExpenseAccountingRuleApplyResultDto?> ApplyAutoClassificationAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        CancellationToken ct)
    {
        var dates = rows
            .Select(static row => row.EmissionDate)
            .Where(static date => date.HasValue)
            .Select(static date => date!.Value)
            .ToArray();
        if (dates.Length == 0)
            return null;

        var externalKeys = rows
            .Select(static row => row.ExternalKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await _dataverse.ApplyExpenseAccountingRulesAsync(
            dates.Min(),
            dates.Max(),
            movementType: "Compra",
            overwrite: false,
            ct: ct,
            externalKeys: externalKeys);
    }

    internal static DianSupplierDocumentWorkbookReadResult ReadSource(
        Stream stream,
        string sourceFileName)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        return string.Equals(Path.GetExtension(sourceFileName), ".zip", StringComparison.OrdinalIgnoreCase)
            ? ReadZipArchive(stream, sourceFileName)
            : ReadWorkbook(stream, sourceFileName);
    }

    private static DianSupplierDocumentWorkbookReadResult ReadZipArchive(
        Stream stream,
        string sourceFileName)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = archive.Entries
            .Where(IsExcelZipEntry)
            .OrderBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
            throw new InvalidOperationException("El ZIP DIAN no contiene un Excel .xlsx o .xlsm para importar.");

        var rows = new List<DianSupplierDocumentImportRowDto>();
        var skipped = new List<DianSupplierDocumentSkippedRowDto>();
        var rowsRead = 0;

        foreach (var entry in entries)
        {
            using var entryStream = entry.Open();
            using var workbookStream = new MemoryStream();
            entryStream.CopyTo(workbookStream);
            workbookStream.Position = 0;

            var read = ReadWorkbook(workbookStream, $"{sourceFileName}!{Path.GetFileName(entry.FullName)}");
            rowsRead += read.RowsRead;
            rows.AddRange(read.Rows);
            skipped.AddRange(read.Skipped);
        }

        return DeduplicateReadResult(rowsRead, rows, skipped);
    }

    private static bool IsExcelZipEntry(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0)
            return false;

        var fileName = Path.GetFileName(entry.FullName);
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.Contains("__MACOSX/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    private static DianSupplierDocumentWorkbookReadResult ReadWorkbook(
        Stream stream,
        string sourceFileName)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("El Excel DIAN no contiene hojas.");
        var usedRange = sheet.RangeUsed()
            ?? throw new InvalidOperationException("El Excel DIAN esta vacio.");
        var headerRow = FindHeaderRow(usedRange)
            ?? throw new InvalidOperationException("No encontramos la fila de encabezados DIAN.");
        var headers = BuildHeaderMap(headerRow);
        var rows = new List<DianSupplierDocumentImportRowDto>();
        var skipped = new List<DianSupplierDocumentSkippedRowDto>();
        var rowsRead = 0;

        foreach (var row in usedRange.RowsUsed().Where(row => row.RowNumber() > headerRow.RowNumber()))
        {
            rowsRead++;
            var raw = ParseRawRow(row, headers);
            if (IsBlankRawRow(raw))
                continue;

            if (!TryResolveDocumentKind(raw, out var kind, out var skipReason))
            {
                skipped.Add(BuildSkipped(raw, skipReason));
                continue;
            }

            if (raw.EmissionDate is null)
            {
                skipped.Add(BuildSkipped(raw, "Sin fecha de emision."));
                continue;
            }

            if (!IsDataverseOnlyDocument(kind) && !raw.ReceptionDate.HasValue)
            {
                skipped.Add(BuildSkipped(raw, "Documento electronico sin fecha de recepcion; no pertenece a un mes recibido verificable."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw.CufeCude))
            {
                skipped.Add(BuildSkipped(raw, "Documento electronico sin CUFE/CUDE; no se importa para evitar duplicados."));
                continue;
            }

            if (raw.TotalValue <= 0m)
            {
                skipped.Add(BuildSkipped(raw, "Sin total valido."));
                continue;
            }

            rows.Add(BuildImportRow(raw, kind, sourceFileName, sheet.Name));
        }

        return DeduplicateReadResult(rowsRead, rows, skipped);
    }

    private static IXLRangeRow? FindHeaderRow(IXLRange range)
    {
        return range.RowsUsed()
            .Take(12)
            .FirstOrDefault(row =>
            {
                var headers = row.CellsUsed().Select(cell => NormalizeHeader(ReadText(cell))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return headers.Contains("tipodedocumento")
                    && headers.Contains("cufecude")
                    && headers.Contains("folio")
                    && headers.Contains("grupo");
            });
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLRangeRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = NormalizeHeader(ReadText(cell));
            if (!string.IsNullOrWhiteSpace(key))
                map[key] = cell.Address.ColumnNumber;
        }

        var required = new[] { "tipodedocumento", "cufecude", "folio", "fechaemision", "fecharecepcion", "nitemisor", "nombreemisor", "nitreceptor", "nombrereceptor", "total", "grupo" };
        var missing = required.Where(header => !map.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"El Excel DIAN no tiene estas columnas esperadas: {string.Join(", ", missing)}.");

        return map;
    }

    private static DianRawDocumentRow ParseRawRow(IXLRangeRow row, IReadOnlyDictionary<string, int> headers)
    {
        var raw = new DianRawDocumentRow
        {
            RowNumber = row.RowNumber(),
            DocumentType = ReadText(GetCell(row, headers, "tipodedocumento")),
            CufeCude = ReadText(GetCell(row, headers, "cufecude")),
            Folio = ReadText(GetCell(row, headers, "folio")),
            Prefix = ReadText(GetCell(row, headers, "prefijo")),
            Currency = ReadText(GetCell(row, headers, "divisa")),
            PaymentForm = ReadText(GetCell(row, headers, "formadepago")),
            PaymentMethod = ReadText(GetCell(row, headers, "mediodepago")),
            EmissionDate = ReadDate(GetCell(row, headers, "fechaemision")),
            ReceptionDate = ReadDateTimeOffset(GetCell(row, headers, "fecharecepcion")),
            IssuerNit = ReadText(GetCell(row, headers, "nitemisor")),
            IssuerName = ReadText(GetCell(row, headers, "nombreemisor")),
            RecipientNit = ReadText(GetCell(row, headers, "nitreceptor")),
            RecipientName = ReadText(GetCell(row, headers, "nombrereceptor")),
            VatValue = ReadDecimal(GetCell(row, headers, "iva")),
            IcaValue = ReadDecimal(GetCell(row, headers, "ica")),
            ReteIvaValue = ReadDecimal(GetCell(row, headers, "reteiva")),
            ReteFuenteValue = ReadDecimal(GetCell(row, headers, "reterenta")),
            ReteIcaValue = ReadDecimal(GetCell(row, headers, "reteica")),
            TotalValue = ReadDecimal(GetCell(row, headers, "total")),
            DianStatus = ReadText(GetCell(row, headers, "estado")),
            Group = ReadText(GetCell(row, headers, "grupo"))
        };

        raw.OtherTaxValue = new[]
        {
            "ic",
            "inc",
            "timbre",
            "incbolsas",
            "incarbono",
            "incombustibles",
            "icdatos",
            "icl",
            "inpp",
            "ibua",
            "icui"
        }.Sum(header => ReadDecimal(GetCell(row, headers, header)));
        return raw;
    }

    private static DianSupplierDocumentImportRowDto BuildImportRow(
        DianRawDocumentRow raw,
        string kind,
        string sourceFileName,
        string sheetName)
    {
        var supportIssuedByCompany = string.Equals(kind, "DocumentoSoporte", StringComparison.OrdinalIgnoreCase)
            && NormalizeText(raw.Group).Contains("EMITIDO", StringComparison.OrdinalIgnoreCase);
        var payrollIssuedByCompany = string.Equals(kind, "NominaIndividual", StringComparison.OrdinalIgnoreCase)
            && NormalizeText(raw.Group).Contains("EMITIDO", StringComparison.OrdinalIgnoreCase);
        var companyIssuedExpense = supportIssuedByCompany || payrollIssuedByCompany;
        var supplierNit = companyIssuedExpense ? raw.RecipientNit : raw.IssuerNit;
        var supplierName = companyIssuedExpense ? raw.RecipientName : raw.IssuerName;
        var companyNit = companyIssuedExpense ? raw.IssuerNit : raw.RecipientNit;
        var companyName = companyIssuedExpense ? raw.IssuerName : raw.RecipientName;
        var invoiceNumber = BuildInvoiceNumber(raw.Prefix, raw.Folio);
        var baseAmount = RoundCurrency(Math.Max(0m, raw.TotalValue - raw.VatValue - raw.IcaValue - raw.OtherTaxValue));

        var row = new DianSupplierDocumentImportRowDto
        {
            SourceFileName = sourceFileName,
            SheetName = sheetName,
            RowNumber = raw.RowNumber,
            DocumentKind = kind,
            DocumentType = raw.DocumentType,
            DianGroup = raw.Group,
            DianStatus = raw.DianStatus,
            CufeCude = raw.CufeCude,
            Prefix = raw.Prefix,
            Folio = raw.Folio,
            InvoiceNumber = invoiceNumber,
            Currency = raw.Currency,
            PaymentForm = raw.PaymentForm,
            PaymentMethod = raw.PaymentMethod,
            EmissionDate = raw.EmissionDate,
            ReceptionDate = raw.ReceptionDate,
            SupplierNit = supplierNit,
            SupplierName = supplierName,
            CompanyNit = companyNit,
            CompanyName = companyName,
            BaseAmount = baseAmount,
            VatValue = RoundCurrency(raw.VatValue),
            IcaValue = RoundCurrency(raw.IcaValue),
            ReteIvaValue = RoundCurrency(raw.ReteIvaValue),
            ReteFuenteValue = RoundCurrency(raw.ReteFuenteValue),
            ReteIcaValue = RoundCurrency(raw.ReteIcaValue),
            TotalValue = RoundCurrency(raw.TotalValue)
        };
        row.ExternalKey = BuildExternalKey(row);
        row.SourceHash = BuildSourceHash(row);
        return row;
    }

    private static bool TryResolveDocumentKind(DianRawDocumentRow raw, out string kind, out string reason)
    {
        var type = NormalizeText(raw.DocumentType);
        var group = NormalizeText(raw.Group);
        if (type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("APPLICATIONRESPONSE", StringComparison.OrdinalIgnoreCase))
        {
            kind = "";
            reason = "Application response; no se importa.";
            return false;
        }

        if (type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase))
        {
            if (!group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
                || group.Contains("EMITID", StringComparison.OrdinalIgnoreCase))
            {
                kind = "";
                reason = group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
                    ? "Nota credito emitida por la empresa; no se importa como ajuste de proveedor."
                    : "Nota credito que no pertenece al grupo Recibidos; no se importa.";
                return false;
            }
            if (IsDigitalTechCopiersIssuer(raw.IssuerName))
            {
                kind = "";
                reason = "Nota credito emitida por DIGITAL TECH COPIERS S A S; no corresponde a un proveedor.";
                return false;
            }

            kind = "NotaCreditoProveedor";
            reason = "";
            return true;
        }

        if (type.Contains("NOTA", StringComparison.OrdinalIgnoreCase))
        {
            kind = "";
            reason = "Nota o ajuste distinto de nota credito de proveedor; no se importa.";
            return false;
        }

        if (type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase))
        {
            if (!group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
                || group.Contains("EMITID", StringComparison.OrdinalIgnoreCase))
            {
                kind = "";
                reason = group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
                    ? "Factura emitida; no se importa."
                    : "Factura electronica que no pertenece al grupo Recibidos; no se importa.";
                return false;
            }

            if (IsDigitalTechCopiersIssuer(raw.IssuerName))
            {
                kind = "";
                reason = "Factura electronica emitida por DIGITAL TECH COPIERS S A S; no se importa como gasto.";
                return false;
            }

            kind = "FacturaElectronica";
            reason = "";
            return true;
        }

        if (type.Contains("DOCUMENTO SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("DOC SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SOPORTE CON NO OBLIGADOS", StringComparison.OrdinalIgnoreCase))
        {
            if (!group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
                || group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase))
            {
                kind = "";
                reason = "Documento soporte que no pertenece al grupo Emitidos; no se importa.";
                return false;
            }

            kind = "DocumentoSoporte";
            reason = "";
            return true;
        }

        if (type.Contains("NOMINA INDIVIDUAL", StringComparison.OrdinalIgnoreCase))
        {
            if (!group.Contains("EMITID", StringComparison.OrdinalIgnoreCase)
                || group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase))
            {
                kind = "";
                reason = "Nomina individual que no pertenece al grupo Emitidos; no se importa.";
                return false;
            }

            kind = "NominaIndividual";
            reason = "";
            return true;
        }

        kind = "";
        reason = "No corresponde a una factura electronica recibida importable.";
        return false;
    }

    private static DianSupplierDocumentWorkbookReadResult DeduplicateReadResult(
        int rowsRead,
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        IReadOnlyList<DianSupplierDocumentSkippedRowDto> skipped)
    {
        var uniqueRows = new List<DianSupplierDocumentImportRowDto>(rows.Count);
        var allSkipped = new List<DianSupplierDocumentSkippedRowDto>(skipped);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (seen.Add(row.ExternalKey))
            {
                uniqueRows.Add(row);
                continue;
            }

            allSkipped.Add(new DianSupplierDocumentSkippedRowDto
            {
                RowNumber = row.RowNumber,
                DocumentType = row.DocumentType,
                Group = row.DianGroup,
                Prefix = row.Prefix,
                Folio = row.Folio,
                EmissionDate = row.EmissionDate,
                ReceptionDate = row.ReceptionDate,
                Reason = "CUFE/CUDE duplicado dentro del archivo/ZIP; se conserva un solo documento."
            });
        }

        return new DianSupplierDocumentWorkbookReadResult(rowsRead, uniqueRows, allSkipped);
    }

    private static DateOnly ResolvePeriodStart(DateOnly? periodStart)
    {
        var resolved = periodStart
            ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        return new DateOnly(resolved.Year, resolved.Month, 1);
    }

    internal static bool IsSiigoEligibleDocument(DianSupplierDocumentImportRowDto row) =>
        row.DocumentKind.Equals("FacturaElectronica", StringComparison.OrdinalIgnoreCase)
        || row.DocumentKind.Equals("NotaCreditoProveedor", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDataverseOnlyDocument(string documentKind) =>
        documentKind.Equals("NominaIndividual", StringComparison.OrdinalIgnoreCase)
        || documentKind.Equals("DocumentoSoporte", StringComparison.OrdinalIgnoreCase);

    internal static DateOnly? ResolveDocumentPeriod(DianSupplierDocumentImportRowDto row)
    {
        if (IsDataverseOnlyDocument(row.DocumentKind)
            && row.EmissionDate.HasValue)
        {
            return new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1);
        }

        if (row.ReceptionDate.HasValue)
        {
            var receptionDate = DateOnly.FromDateTime(
                row.ReceptionDate.Value.ToOffset(TimeSpan.FromHours(-5)).DateTime);
            return new DateOnly(receptionDate.Year, receptionDate.Month, 1);
        }

        return row.EmissionDate.HasValue
            ? new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1)
            : null;
    }

    private static IReadOnlyList<string> ResolveObservedPeriods(
        DianSupplierDocumentWorkbookReadResult read,
        DateOnly fallbackPeriodStart)
    {
        var periods = read.Rows
            .Select(ResolveDocumentPeriod)
            .Concat(read.Skipped.Select(static row =>
            {
                if (row.ReceptionDate.HasValue)
                {
                    var receptionDate = DateOnly.FromDateTime(
                        row.ReceptionDate.Value.ToOffset(TimeSpan.FromHours(-5)).DateTime);
                    return (DateOnly?)new DateOnly(receptionDate.Year, receptionDate.Month, 1);
                }

                return row.EmissionDate.HasValue
                    ? new DateOnly(row.EmissionDate.Value.Year, row.EmissionDate.Value.Month, 1)
                    : null;
            }))
            .Where(static period => period.HasValue)
            .Select(static period => period!.Value)
            .Distinct()
            .OrderBy(static period => period)
            .ToArray();
        if (periods.Length == 0)
            periods = [fallbackPeriodStart];

        return periods
            .Select(static period => period.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string FormatPeriodLabel(IReadOnlyList<string> periods)
    {
        var labels = periods
            .Select(static value => DateOnly.TryParseExact(
                $"{value}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var period)
                ? period.ToString("MMMM yyyy", ColombianCulture)
                : value)
            .ToArray();
        return labels.Length == 0 ? "Periodo no disponible" : string.Join(", ", labels);
    }

    private static bool IsDigitalTechCopiersIssuer(string issuerName)
    {
        var normalizedIssuer = NormalizeCompanyName(issuerName);
        return normalizedIssuer.Contains("DIGITALTECHCOPIERSSAS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlankRawRow(DianRawDocumentRow row)
    {
        return string.IsNullOrWhiteSpace(row.DocumentType)
            && string.IsNullOrWhiteSpace(row.CufeCude)
            && string.IsNullOrWhiteSpace(row.Folio)
            && string.IsNullOrWhiteSpace(row.IssuerNit)
            && string.IsNullOrWhiteSpace(row.RecipientNit)
            && row.TotalValue == 0m;
    }

    private static DianSupplierDocumentImportResultDto BuildResult(
        DianSupplierDocumentWorkbookReadResult read,
        DianSupplierDocumentDataverseUpsertResultDto upsert,
        DianSupplierDocumentSiigoSupplierResolutionResultDto supplierResolution,
        ExpenseAccountingRuleApplyResultDto? autoClassification,
        string autoClassificationMessage,
        DianSupplierInvoiceAutomationResultDto? siigoAutomation,
        DianSupplierCreditNoteAutomationResultDto? creditNoteAutomation,
        bool dryRun,
        string sourceFileName,
        DateOnly fallbackPeriodStart)
    {
        var periods = ResolveObservedPeriods(read, fallbackPeriodStart);
        var siigoPeriods = read.Rows
            .Where(IsSiigoEligibleDocument)
            .Select(ResolveDocumentPeriod)
            .Where(static period => period.HasValue)
            .Select(static period => period!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static period => period, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DianSupplierDocumentImportResultDto
        {
            DryRun = dryRun,
            SourceFileName = sourceFileName,
            Periods = periods,
            SiigoPeriods = siigoPeriods,
            PeriodLabel = FormatPeriodLabel(periods),
            RowsRead = read.RowsRead,
            ImportableRows = read.Rows.Count,
            InvoiceRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "FacturaElectronica", StringComparison.OrdinalIgnoreCase)),
            SupplierCreditNoteRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "NotaCreditoProveedor", StringComparison.OrdinalIgnoreCase)),
            SupportDocumentRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "DocumentoSoporte", StringComparison.OrdinalIgnoreCase)),
            PayrollRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "NominaIndividual", StringComparison.OrdinalIgnoreCase)),
            SkippedRows = read.Skipped.Count,
            Created = upsert.Created,
            Updated = upsert.Updated,
            Unchanged = upsert.Unchanged,
            DataverseRowsSkipped = upsert.Skipped,
            SupplierLookupReviewed = supplierResolution.Reviewed,
            SupplierLookupFound = supplierResolution.Found,
            SupplierLookupMissing = supplierResolution.Missing,
            SupplierLookupFailed = supplierResolution.Failed,
            SupplierLookupRowsUpdated = supplierResolution.Updated,
            AutoClassificationReviewed = autoClassification?.Reviewed ?? 0,
            AutoClassificationUpdated = autoClassification?.Updated ?? 0,
            AutoClassificationAlreadyAssigned = autoClassification?.AlreadyAssigned ?? 0,
            AutoClassificationNoRule = autoClassification?.NoRule ?? 0,
            AutoClassificationInvalidRule = autoClassification?.InvalidRule ?? 0,
            AutoClassificationMessage = autoClassificationMessage,
            SiigoAutomation = siigoAutomation,
            CreditNoteAutomation = creditNoteAutomation,
            TotalValue = RoundCurrency(read.Rows.Sum(static row => row.TotalValue)),
            VatValue = RoundCurrency(read.Rows.Sum(static row => row.VatValue)),
            ReteFuenteValue = RoundCurrency(read.Rows.Sum(static row => row.ReteFuenteValue)),
            ReteIcaValue = RoundCurrency(read.Rows.Sum(static row => row.ReteIcaValue)),
            ReteIvaValue = RoundCurrency(read.Rows.Sum(static row => row.ReteIvaValue)),
            Skipped = read.Skipped.Take(500).ToArray(),
            SampleRows = read.Rows.Take(500).ToArray(),
            UpsertRows = upsert.Rows.Take(500).ToArray()
        };
    }

    private static DianSupplierDocumentSkippedRowDto BuildSkipped(DianRawDocumentRow row, string reason) =>
        new()
        {
            RowNumber = row.RowNumber,
            DocumentType = row.DocumentType,
            Group = row.Group,
            Prefix = row.Prefix,
            Folio = row.Folio,
            EmissionDate = row.EmissionDate,
            ReceptionDate = row.ReceptionDate,
            Reason = reason
        };

    private static IXLCell? GetCell(IXLRangeRow row, IReadOnlyDictionary<string, int> headers, string normalizedHeader)
    {
        return headers.TryGetValue(normalizedHeader, out var columnIndex)
            ? row.WorksheetRow().Cell(columnIndex)
            : null;
    }

    private static string ReadText(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty())
            return "";

        if (cell.DataType == XLDataType.Text
            && cell.TryGetValue<string>(out var text))
            return text.Trim();

        if (cell.TryGetValue<decimal>(out var numericValue))
            return numericValue.ToString("0.############################", CultureInfo.InvariantCulture);

        return (cell.GetFormattedString(ColombianCulture) ?? "").Trim();
    }

    private static DateOnly? ReadDate(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty())
            return null;

        if (cell.TryGetValue<DateTime>(out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        if (cell.TryGetValue<double>(out var serial) && serial > 0)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));

        var raw = ReadText(cell);
        foreach (var format in new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd" })
        {
            if (DateOnly.TryParseExact(raw, format, ColombianCulture, DateTimeStyles.None, out var date))
                return date;
        }

        if (DateTime.TryParse(raw, ColombianCulture, DateTimeStyles.None, out dateTime)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty())
            return null;

        if (cell.TryGetValue<DateTime>(out var dateTime))
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.FromHours(-5));

        var raw = ReadText(cell);
        foreach (var format in new[] { "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy" })
        {
            if (DateTime.TryParseExact(raw, format, ColombianCulture, DateTimeStyles.None, out dateTime))
                return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.FromHours(-5));
        }

        if (DateTime.TryParse(raw, ColombianCulture, DateTimeStyles.None, out dateTime)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.FromHours(-5));
        }

        return null;
    }

    private static decimal ReadDecimal(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty())
            return 0m;

        if (cell.TryGetValue<decimal>(out var value))
            return value;

        if (cell.TryGetValue<double>(out var doubleValue))
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);

        return TryParseDecimal(ReadText(cell), out value) ? value : 0m;
    }

    private static bool TryParseDecimal(string? rawValue, out decimal value)
    {
        value = 0m;
        var raw = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        raw = raw.Replace("COP", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(raw, NumberStyles.Number, ColombianCulture, out value)
            || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var filtered = new string(raw.Where(static ch => char.IsDigit(ch) || ch is '-' or ',' or '.').ToArray());
        var lastDot = filtered.LastIndexOf('.');
        var lastComma = filtered.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
            filtered = lastComma > lastDot ? filtered.Replace(".", "").Replace(',', '.') : filtered.Replace(",", "");
        else if (lastComma >= 0)
            filtered = filtered.Replace(',', '.');

        return decimal.TryParse(filtered, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = RemoveDiacritics(value).ToLowerInvariant();
        return Regex.Replace(normalized, @"[^a-z0-9]", "", RegexOptions.CultureInvariant);
    }

    private static string NormalizeText(string value) =>
        RemoveDiacritics(value).ToUpperInvariant().Trim();

    private static string NormalizeCompanyName(string value) =>
        Regex.Replace(NormalizeText(value), @"[^A-Z0-9]+", "", RegexOptions.CultureInvariant);

    private static string RemoveDiacritics(string value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildInvoiceNumber(string prefix, string folio)
    {
        return string.Join("-", new[] { prefix, folio }.Where(static value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private static string BuildExternalKey(DianSupplierDocumentImportRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.CufeCude))
            return $"dian-cufe:{BuildHashKey(row.CufeCude)}";

        var fallback = string.Join(
            ":",
            new[]
            {
                "dian",
                NormalizeKey(row.DocumentType),
                NormalizeKey(row.Prefix),
                NormalizeKey(row.Folio),
                NormalizeKey(row.SupplierNit),
                row.EmissionDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "sinfecha",
                row.TotalValue.ToString("0.##", CultureInfo.InvariantCulture)
            });
        return fallback.Length <= 100 ? fallback : $"dian-fallback:{BuildHashKey(fallback)}";
    }

    private static string BuildSourceHash(DianSupplierDocumentImportRowDto row)
    {
        var source = string.Join("|", new[]
        {
            row.DocumentType,
            row.DianGroup,
            row.CufeCude,
            row.Prefix,
            row.Folio,
            row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.ReceptionDate?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            row.SupplierNit,
            row.SupplierName,
            row.CompanyNit,
            row.CompanyName,
            row.TotalValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.VatValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.ReteIvaValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.ReteFuenteValue.ToString("0.##", CultureInfo.InvariantCulture),
            row.ReteIcaValue.ToString("0.##", CultureInfo.InvariantCulture)
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeKey(string value) =>
        Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"\s+", "", RegexOptions.CultureInvariant);

    private static SiigoCustomerLookupItemDto? FindExactActiveSupplier(
        string supplierNit,
        IReadOnlyList<SiigoCustomerLookupItemDto> candidates)
    {
        var supplierDigits = ExtractDigits(supplierNit);
        if (supplierDigits.Length < 5)
            return null;

        return candidates
            .Where(static candidate => candidate.Active)
            .FirstOrDefault(candidate => IsSameTaxId(supplierDigits, ExtractDigits(candidate.Identification)));
    }

    private static bool IsSameTaxId(string leftDigits, string rightDigits)
    {
        if (string.IsNullOrWhiteSpace(leftDigits) || string.IsNullOrWhiteSpace(rightDigits))
            return false;

        if (string.Equals(leftDigits, rightDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return (leftDigits.Length >= 9 && leftDigits.Length == rightDigits.Length + 1 && leftDigits.StartsWith(rightDigits, StringComparison.OrdinalIgnoreCase))
            || (rightDigits.Length >= 9 && rightDigits.Length == leftDigits.Length + 1 && rightDigits.StartsWith(leftDigits, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string BuildHashKey(string value)
    {
        var normalized = NormalizeKey(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed class DianRawDocumentRow
    {
        public int RowNumber { get; set; }
        public string DocumentType { get; set; } = "";
        public string CufeCude { get; set; } = "";
        public string Folio { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Currency { get; set; } = "";
        public string PaymentForm { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public DateOnly? EmissionDate { get; set; }
        public DateTimeOffset? ReceptionDate { get; set; }
        public string IssuerNit { get; set; } = "";
        public string IssuerName { get; set; } = "";
        public string RecipientNit { get; set; } = "";
        public string RecipientName { get; set; } = "";
        public decimal VatValue { get; set; }
        public decimal IcaValue { get; set; }
        public decimal OtherTaxValue { get; set; }
        public decimal ReteIvaValue { get; set; }
        public decimal ReteFuenteValue { get; set; }
        public decimal ReteIcaValue { get; set; }
        public decimal TotalValue { get; set; }
        public string DianStatus { get; set; } = "";
        public string Group { get; set; } = "";
    }

    internal sealed record DianSupplierDocumentWorkbookReadResult(
        int RowsRead,
        IReadOnlyList<DianSupplierDocumentImportRowDto> Rows,
        IReadOnlyList<DianSupplierDocumentSkippedRowDto> Skipped);
}
