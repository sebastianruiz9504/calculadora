using System.Globalization;
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
    private readonly DianSupplierDocumentImportOptions _options;
    private readonly ILogger<DianSupplierDocumentImportService> _logger;

    public DianSupplierDocumentImportService(
        IDataverseService dataverse,
        ISiigoService siigo,
        IOptions<DianSupplierDocumentImportOptions> options,
        ILogger<DianSupplierDocumentImportService> logger)
    {
        _dataverse = dataverse;
        _siigo = siigo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DianSupplierDocumentImportResultDto> ImportAsync(
        string? localFilePath = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var path = FirstNonEmpty(localFilePath, _options.LocalFilePath).Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Indica la ruta local del Excel DIAN para importar.");

        if (!File.Exists(path))
            throw new InvalidOperationException($"No encontramos el archivo DIAN: {path}");

        await using var stream = File.OpenRead(path);
        var read = ReadWorkbook(stream, Path.GetFileName(path));
        var resolvedDryRun = dryRun || _options.DryRun;
        _logger.LogInformation(
            "Excel DIAN leido desde {Path}: {Rows} filas importables de {RowsRead}. DryRun={DryRun}.",
            path,
            read.Rows.Count,
            read.RowsRead,
            resolvedDryRun);

        var upsert = await _dataverse.UpsertDianSupplierDocumentRowsAsync(read.Rows, resolvedDryRun, ct);
        var supplierResolution = new DianSupplierDocumentSiigoSupplierResolutionResultDto();
        ExpenseAccountingRuleApplyResultDto? autoClassification = null;
        var autoClassificationMessage = "";

        if (!resolvedDryRun && read.Rows.Count > 0)
        {
            supplierResolution = await ResolveSiigoSuppliersAsync(read.Rows, ct);
            try
            {
                autoClassification = await ApplyAutoClassificationAsync(read.Rows, ct);
            }
            catch (Exception ex)
            {
                autoClassificationMessage = $"Importacion guardada, pero no se pudo aplicar la autoclasificacion: {ex.Message}";
                _logger.LogWarning(ex, "No se pudo aplicar autoclasificacion DIAN despues de importar {Path}.", path);
            }
        }

        return BuildResult(
            read,
            upsert,
            supplierResolution,
            autoClassification,
            autoClassificationMessage,
            resolvedDryRun,
            Path.GetFileName(path));
    }

    private async Task<DianSupplierDocumentSiigoSupplierResolutionResultDto> ResolveSiigoSuppliersAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
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
                var candidates = await _siigo.SearchCustomersAsync(supplierNit, top: 1, ct);
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
            dryRun: false,
            ct);
        result.MatchedRows = dataverseResult.MatchedRows;
        result.Updated = dataverseResult.Updated;
        return result;
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

    private static DianSupplierDocumentWorkbookReadResult ReadWorkbook(Stream stream, string sourceFileName)
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

            if (!TryResolveDocumentKind(raw.DocumentType, raw.Group, out var kind, out var skipReason))
            {
                skipped.Add(BuildSkipped(raw, skipReason));
                continue;
            }

            if (raw.EmissionDate is null)
            {
                skipped.Add(BuildSkipped(raw, "Sin fecha de emision."));
                continue;
            }

            if (raw.TotalValue <= 0m)
            {
                skipped.Add(BuildSkipped(raw, "Sin total valido."));
                continue;
            }

            rows.Add(BuildImportRow(raw, kind, sourceFileName, sheet.Name));
        }

        return new DianSupplierDocumentWorkbookReadResult(rowsRead, rows, skipped);
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

        var required = new[] { "tipodedocumento", "cufecude", "folio", "fechaemision", "nitemisor", "nombreemisor", "nitreceptor", "nombrereceptor", "total", "grupo" };
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
        var supplierNit = supportIssuedByCompany ? raw.RecipientNit : raw.IssuerNit;
        var supplierName = supportIssuedByCompany ? raw.RecipientName : raw.IssuerName;
        var companyNit = supportIssuedByCompany ? raw.IssuerNit : raw.RecipientNit;
        var companyName = supportIssuedByCompany ? raw.IssuerName : raw.RecipientName;
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

    private static bool TryResolveDocumentKind(string documentType, string group, out string kind, out string reason)
    {
        var type = NormalizeText(documentType);
        var normalizedGroup = NormalizeText(group);
        if (type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && normalizedGroup.Contains("RECIBIDO", StringComparison.OrdinalIgnoreCase))
        {
            kind = "FacturaElectronica";
            reason = "";
            return true;
        }

        if (type.Contains("DOCUMENTO SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("DOC SOPORTE", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SOPORTE CON NO OBLIGADOS", StringComparison.OrdinalIgnoreCase))
        {
            kind = "DocumentoSoporte";
            reason = "";
            return true;
        }

        kind = "";
        reason = "No corresponde a factura electronica recibida ni documento soporte.";
        return false;
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
        bool dryRun,
        string sourceFileName)
    {
        return new DianSupplierDocumentImportResultDto
        {
            DryRun = dryRun,
            SourceFileName = sourceFileName,
            RowsRead = read.RowsRead,
            ImportableRows = read.Rows.Count,
            InvoiceRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "FacturaElectronica", StringComparison.OrdinalIgnoreCase)),
            SupportDocumentRows = read.Rows.Count(static row => string.Equals(row.DocumentKind, "DocumentoSoporte", StringComparison.OrdinalIgnoreCase)),
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
            TotalValue = RoundCurrency(read.Rows.Sum(static row => row.TotalValue)),
            VatValue = RoundCurrency(read.Rows.Sum(static row => row.VatValue)),
            ReteFuenteValue = RoundCurrency(read.Rows.Sum(static row => row.ReteFuenteValue)),
            ReteIcaValue = RoundCurrency(read.Rows.Sum(static row => row.ReteIcaValue)),
            ReteIvaValue = RoundCurrency(read.Rows.Sum(static row => row.ReteIvaValue)),
            Skipped = read.Skipped.Take(150).ToArray(),
            SampleRows = read.Rows.Take(50).ToArray()
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

        return (cell.GetString() ?? "").Trim();
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

    private sealed record DianSupplierDocumentWorkbookReadResult(
        int RowsRead,
        IReadOnlyList<DianSupplierDocumentImportRowDto> Rows,
        IReadOnlyList<DianSupplierDocumentSkippedRowDto> Skipped);
}
