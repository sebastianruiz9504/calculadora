using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Automation;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed class CashFlowImportService : ICashFlowImportService
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IDataverseService _dataverse;
    private readonly CashFlowImportOptions _options;
    private readonly M365Options _m365Options;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReconciliationReportSender _reportSender;
    private readonly ILogger<CashFlowImportService> _logger;

    public CashFlowImportService(
        IDataverseService dataverse,
        IOptions<CashFlowImportOptions> options,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IReconciliationReportSender reportSender,
        ILogger<CashFlowImportService> logger)
    {
        _dataverse = dataverse;
        _options = options.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _reportSender = reportSender;
        _logger = logger;
    }

    public async Task<CashFlowImportResultDto> ImportAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var resolvedDryRun = dryRun || _options.DryRun;

        try
        {
            using var workbookStream = await DownloadWorkbookAsync(ct);
            var readResult = ReadWorkbookRowsWithSkipped(workbookStream, _options);
            var rows = readResult.Rows;

            _logger.LogInformation(
                "Flujo de caja leido desde {FileName}: {Rows} filas validas, {Movements} movimientos, {Transfers} traslados. DryRun={DryRun}.",
                _options.FileName,
                rows.Count,
                rows.Count(static row => !row.IsTransfer),
                rows.Count(static row => row.IsTransfer),
                resolvedDryRun);

            var upsert = await _dataverse.UpsertCashFlowRowsAsync(rows, resolvedDryRun, ct);
            var result = BuildResult(readResult, upsert, resolvedDryRun);
            await TrySendImportSummaryEmailAsync(result, ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TrySendImportFailureEmailAsync(resolvedDryRun, ex, ct);
            throw;
        }
    }

    public static IReadOnlyList<CashFlowImportRowDto> ReadWorkbookRows(Stream workbookStream, CashFlowImportOptions options)
    {
        return ReadWorkbookRowsWithSkipped(workbookStream, options).Rows;
    }

    private async Task<MemoryStream> DownloadWorkbookAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.LocalFilePath))
        {
            var localBytes = await File.ReadAllBytesAsync(_options.LocalFilePath, ct);
            return new MemoryStream(localBytes);
        }

        if (string.IsNullOrWhiteSpace(_options.DriveId) || string.IsNullOrWhiteSpace(_options.ItemId))
            throw new InvalidOperationException("Configura CashFlowImport:DriveId y CashFlowImport:ItemId para leer el Excel de flujo de caja desde SharePoint.");

        var token = await GetGraphAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/items/{Uri.EscapeDataString(_options.ItemId)}/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Microsoft Graph no pudo descargar el flujo de caja: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");
        }

        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, ct);
        memory.Position = 0;
        return memory;
    }

    private async Task<string> GetGraphAccessTokenAsync(CancellationToken ct)
    {
        var tenantId = FirstNonEmpty(_configuration["M365:TenantId"], _configuration["AzureAd:TenantId"]);
        var clientId = FirstNonEmpty(_m365Options.ClientId, _configuration["AzureAd:ClientId"]);
        var clientSecret = FirstNonEmpty(_m365Options.ClientSecret, _configuration["AzureAd:ClientSecret"]);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Configura M365:ClientId y AzureAd:TenantId para leer el flujo de caja desde Microsoft Graph.");

        var builder = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"{NormalizeAuthorityHost(_m365Options.AuthorityHost)}/{tenantId}");
        var certificate = LoadCertificateOrDefault();
        if (certificate is not null)
        {
            builder.WithCertificate(certificate);
        }
        else if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            builder.WithClientSecret(clientSecret);
        }
        else
        {
            throw new InvalidOperationException("Configura M365:ClientSecret, AzureAd:ClientSecret o un certificado M365 para leer el flujo de caja desde Microsoft Graph.");
        }

        try
        {
            var app = builder.Build();
            var result = await app.AcquireTokenForClient(ResolveGraphTokenScopes()).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw new InvalidOperationException("No fue posible obtener token app-only de Microsoft Graph para leer el flujo de caja.", ex);
        }
    }

    private X509Certificate2? LoadCertificateOrDefault()
    {
        if (!string.IsNullOrWhiteSpace(_m365Options.CertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                _m365Options.CertificatePath,
                _m365Options.CertificatePassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
        }

        if (string.IsNullOrWhiteSpace(_m365Options.CertificateThumbprint))
            return null;

        if (!Enum.TryParse<StoreName>(_m365Options.CertificateStoreName, ignoreCase: true, out var storeName))
            storeName = StoreName.My;

        if (!Enum.TryParse<StoreLocation>(_m365Options.CertificateStoreLocation, ignoreCase: true, out var storeLocation))
            storeLocation = StoreLocation.CurrentUser;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            _m365Options.CertificateThumbprint.Trim(),
            validOnly: false);

        return certificates.Count == 0 ? null : certificates[0];
    }

    private Uri BuildGraphUri(string relativePath)
    {
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        var baseUrl = string.IsNullOrWhiteSpace(_m365Options.GraphBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _m365Options.GraphBaseUrl.TrimEnd('/');
        var normalizedPath = relativePath.StartsWith("/", StringComparison.Ordinal)
            ? relativePath
            : $"/{relativePath}";

        return new Uri($"{baseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private string[] ResolveGraphTokenScopes()
    {
        var scopes = _m365Options.Scopes?
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .ToArray();

        return scopes is { Length: > 0 } ? scopes : new[] { GraphDefaultScope };
    }

    private static CashFlowWorkbookReadResult ReadWorkbookRowsWithSkipped(Stream workbookStream, CashFlowImportOptions options)
    {
        using var workbook = new XLWorkbook(workbookStream);
        var rows = new List<CashFlowImportRowDto>();
        var blankRowsSkipped = 0;
        var futureRowsSkipped = 0;
        var skippedRows = new List<CashFlowImportSkippedRowDto>();
        var today = ResolveLocalToday(options);

        blankRowsSkipped += ReadTableRows(workbook, options, options.CloudTableName, "Cloud", options.CloudBankAccountCode, options.CloudBankAccountName, today, rows, skippedRows, out var cloudFutureRows);
        blankRowsSkipped += ReadTableRows(workbook, options, options.CopiersTableName, "Copiers", options.CopiersBankAccountCode, options.CopiersBankAccountName, today, rows, skippedRows, out var copiersFutureRows);
        futureRowsSkipped = cloudFutureRows + copiersFutureRows;

        return new CashFlowWorkbookReadResult(rows, blankRowsSkipped, futureRowsSkipped, skippedRows);
    }

    private static int ReadTableRows(
        XLWorkbook workbook,
        CashFlowImportOptions options,
        string tableName,
        string sourceFlow,
        string bankAccountCode,
        string bankAccountName,
        DateOnly today,
        List<CashFlowImportRowDto> output,
        List<CashFlowImportSkippedRowDto> skippedRows,
        out int futureRowsSkipped)
    {
        var table = FindTable(workbook, tableName)
            ?? throw new InvalidOperationException($"No encontre la tabla {tableName} dentro del archivo {options.FileName}.");
        var headers = BuildHeaderMap(table);
        var skipped = 0;
        futureRowsSkipped = 0;

        if (table.DataRange is null)
            return 0;

        foreach (var rangeRow in table.DataRange.Rows())
        {
            var rowNumber = rangeRow.RangeAddress.FirstAddress.RowNumber;
            var row = new CashFlowImportRowDto
            {
                SourceFileName = options.FileName,
                SourceFlow = sourceFlow,
                TableName = table.Name,
                RowNumber = rowNumber,
                Date = ReadDate(GetCell(rangeRow, headers, "fecha")),
                MovementType = ReadText(GetCell(rangeRow, headers, "tipodemovimiento")),
                Category = ReadText(GetCell(rangeRow, headers, "categoria")),
                Entry = ReadDecimal(GetCell(rangeRow, headers, "entrada")),
                Exit = ReadDecimal(GetCell(rangeRow, headers, "salida")),
                Description = ReadText(GetCell(rangeRow, headers, "descripcion")),
                Recipient = ReadText(GetCell(rangeRow, headers, "destinatario")),
                DestinationBank = ReadText(GetCell(rangeRow, headers, "bancodestino")),
                DocumentType = ReadText(GetCell(rangeRow, headers, "tipodocumento")),
                Observations = ReadText(GetCell(rangeRow, headers, "observaciones")),
                SiigoStatus = ReadText(GetCell(rangeRow, headers, "siigo")),
                BankAccountCode = bankAccountCode,
                BankAccountName = bankAccountName
            };

            if (IsEmptyRow(row))
            {
                skipped++;
                continue;
            }

            if (!options.IncludeFutureRows && row.Date.HasValue && row.Date.Value > today)
            {
                futureRowsSkipped++;
                skippedRows.Add(BuildSkippedRow(row, "Fecha futura fuera del periodo importable"));
                continue;
            }

            row.MovementType = string.IsNullOrWhiteSpace(row.MovementType)
                ? InferMovementType(row)
                : row.MovementType.Trim();
            row.IsTransfer = IsTransfer(row);
            (row.TransferFrom, row.TransferTo) = ResolveTransferSides(row);
            row.ExternalKey = BuildExternalKey(row);
            row.SourceHash = BuildSourceHash(row);
            output.Add(row);
        }

        return skipped;
    }

    private static IXLTable? FindTable(XLWorkbook workbook, string tableName)
    {
        return workbook.Worksheets
            .SelectMany(static sheet => sheet.Tables)
            .FirstOrDefault(table => string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLTable table)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        foreach (var cell in table.HeadersRow().Cells())
        {
            var key = NormalizeHeader(ReadText(cell));
            if (!string.IsNullOrWhiteSpace(key))
                map[key] = index;
            index++;
        }

        var required = new[]
        {
            "fecha",
            "tipodemovimiento",
            "categoria",
            "entrada",
            "salida",
            "descripcion",
            "destinatario",
            "bancodestino",
            "tipodocumento",
            "observaciones",
            "siigo"
        };
        var missing = required.Where(header => !map.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"La tabla {table.Name} no tiene estas columnas esperadas: {string.Join(", ", missing)}.");

        return map;
    }

    private static IXLCell GetCell(IXLRangeRow row, IReadOnlyDictionary<string, int> headers, string header)
    {
        return headers.TryGetValue(header, out var columnIndex)
            ? row.Cell(columnIndex)
            : row.Cell(1);
    }

    private static string ReadText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return "";

        var raw = cell.GetString();
        return (raw ?? "").Trim();
    }

    private static DateOnly? ReadDate(IXLCell cell)
    {
        if (cell.IsEmpty())
            return null;

        if (cell.TryGetValue<DateTime>(out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        if (cell.TryGetValue<double>(out var serial) && serial > 0)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));

        var raw = ReadText(cell);
        if (DateOnly.TryParse(raw, ColombianCulture, DateTimeStyles.None, out var date))
            return date;

        if (DateTime.TryParse(raw, ColombianCulture, DateTimeStyles.None, out dateTime))
            return DateOnly.FromDateTime(dateTime);

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;

        return null;
    }

    private static decimal ReadDecimal(IXLCell cell)
    {
        if (cell.IsEmpty())
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

        var negative = raw.StartsWith("(", StringComparison.Ordinal) && raw.EndsWith(")", StringComparison.Ordinal);
        raw = raw
            .Replace("COP", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace('\u00a0'.ToString(), "", StringComparison.OrdinalIgnoreCase)
            .Replace("'", "", StringComparison.OrdinalIgnoreCase)
            .Replace("´", "", StringComparison.OrdinalIgnoreCase)
            .Trim('(', ')');

        if (decimal.TryParse(raw, NumberStyles.Number, ColombianCulture, out value)
            || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            if (negative)
                value *= -1;
            return true;
        }

        var normalized = NormalizeDecimalText(raw);
        var parsed = decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        if (parsed && negative)
            value *= -1;
        return parsed;
    }

    private static string NormalizeDecimalText(string value)
    {
        var filtered = new string(value.Where(static ch => char.IsDigit(ch) || ch is '-' or ',' or '.').ToArray());
        var lastDot = filtered.LastIndexOf('.');
        var lastComma = filtered.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            return lastComma > lastDot
                ? filtered.Replace(".", "").Replace(',', '.')
                : filtered.Replace(",", "");
        }

        if (lastComma >= 0 && lastDot < 0)
            return filtered.Replace(',', '.');

        return filtered;
    }

    private static bool IsEmptyRow(CashFlowImportRowDto row)
    {
        return row.Date is null
            && string.IsNullOrWhiteSpace(row.MovementType)
            && string.IsNullOrWhiteSpace(row.Category)
            && string.IsNullOrWhiteSpace(row.Description)
            && string.IsNullOrWhiteSpace(row.Recipient)
            && row.Entry == 0m
            && row.Exit == 0m;
    }

    private static string InferMovementType(CashFlowImportRowDto row)
    {
        if (row.Entry > 0 && row.Exit == 0)
            return "Entrada";

        if (row.Exit > 0 && row.Entry == 0)
            return "Salida";

        return "Sin clasificar";
    }

    private static bool IsTransfer(CashFlowImportRowDto row)
    {
        var movement = NormalizeHeader(row.MovementType);
        var category = NormalizeHeader(row.Category);
        return movement == "traslado" || category.Contains("traslado", StringComparison.OrdinalIgnoreCase);
    }

    private static (string From, string To) ResolveTransferSides(CashFlowImportRowDto row)
    {
        if (!row.IsTransfer)
            return ("", "");

        var counterpart = DetectCounterpartFlow(row);
        if (row.Exit > 0 && row.Entry == 0)
            return (row.SourceFlow, counterpart);

        if (row.Entry > 0 && row.Exit == 0)
            return (counterpart, row.SourceFlow);

        return (row.SourceFlow, counterpart);
    }

    private static string DetectCounterpartFlow(CashFlowImportRowDto row)
    {
        var raw = NormalizeTextForMatching($"{row.Description} {row.Recipient} {row.DestinationBank} {row.Observations}");
        if (raw.Contains("COPIERS", StringComparison.OrdinalIgnoreCase) || raw.Contains("7316", StringComparison.OrdinalIgnoreCase))
            return "Copiers";

        if (raw.Contains("CLOUD", StringComparison.OrdinalIgnoreCase) || raw.Contains("8100", StringComparison.OrdinalIgnoreCase))
            return "Cloud";

        if (raw.Contains("IVA", StringComparison.OrdinalIgnoreCase))
            return "Bolsillo IVA";

        return "No identificado";
    }

    private static string BuildExternalKey(CashFlowImportRowDto row)
    {
        return $"cashflow:{NormalizeKeyPart(row.SourceFlow)}:{NormalizeKeyPart(row.TableName)}:{row.RowNumber}";
    }

    private static string BuildSourceHash(CashFlowImportRowDto row)
    {
        var raw = string.Join("|", new[]
        {
            row.SourceFileName,
            row.SourceFlow,
            row.TableName,
            row.RowNumber.ToString(CultureInfo.InvariantCulture),
            row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.MovementType,
            row.Category,
            row.Entry.ToString("0.##", CultureInfo.InvariantCulture),
            row.Exit.ToString("0.##", CultureInfo.InvariantCulture),
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations,
            row.SiigoStatus,
            row.BankAccountCode,
            row.BankAccountName,
            row.IsTransfer ? "transfer" : "movement",
            row.TransferFrom,
            row.TransferTo
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static CashFlowImportResultDto BuildResult(
        CashFlowWorkbookReadResult readResult,
        CashFlowDataverseUpsertResultDto upsert,
        bool dryRun)
    {
        var rows = readResult.Rows;
        var dataverseSkippedRows = rows
            .Where(static row => row.Date is null || string.IsNullOrWhiteSpace(row.ExternalKey))
            .Select(static row => BuildSkippedRow(row, row.Date is null
                ? "Fecha vacia o no valida"
                : "Clave externa vacia"))
            .ToArray();
        var skippedRows = readResult.SkippedRows
            .Concat(dataverseSkippedRows)
            .OrderBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.RowNumber)
            .ToArray();

        return new CashFlowImportResultDto
        {
            DryRun = dryRun,
            RowsRead = rows.Count,
            MovementsRead = rows.Count(static row => !row.IsTransfer),
            TransfersRead = rows.Count(static row => row.IsTransfer),
            Skipped = readResult.BlankRowsSkipped + readResult.FutureRowsSkipped + upsert.Skipped,
            BlankRowsSkipped = readResult.BlankRowsSkipped,
            FutureRowsSkipped = readResult.FutureRowsSkipped,
            DataverseRowsSkipped = upsert.Skipped,
            Created = upsert.Created,
            Updated = upsert.Updated,
            Unchanged = upsert.Unchanged,
            TotalEntries = rows.Sum(static row => row.Entry),
            TotalExits = rows.Sum(static row => row.Exit),
            TransferValue = rows.Where(static row => row.IsTransfer).Sum(static row => Math.Max(row.Entry, row.Exit)),
            FlowSummaries = rows
                .GroupBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static group => new CashFlowImportFlowSummaryDto
                {
                    SourceFlow = group.Key,
                    Rows = group.Count(),
                    Movements = group.Count(static row => !row.IsTransfer),
                    Transfers = group.Count(static row => row.IsTransfer),
                    Entries = group.Sum(static row => row.Entry),
                    Exits = group.Sum(static row => row.Exit),
                    TransferValue = group.Where(static row => row.IsTransfer).Sum(static row => Math.Max(row.Entry, row.Exit))
                })
                .ToArray(),
            SkippedRows = skippedRows,
            SampleRows = rows
                .OrderByDescending(static row => row.Date)
                .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToArray()
        };
    }

    private static CashFlowImportSkippedRowDto BuildSkippedRow(CashFlowImportRowDto row, string reason)
    {
        return new CashFlowImportSkippedRowDto
        {
            SourceFlow = row.SourceFlow,
            TableName = row.TableName,
            RowNumber = row.RowNumber,
            Date = row.Date,
            Reason = reason,
            Entry = row.Entry,
            Exit = row.Exit,
            Description = row.Description
        };
    }

    private async Task TrySendImportSummaryEmailAsync(CashFlowImportResultDto result, CancellationToken ct)
    {
        if (!ShouldSendSummaryEmail(result.DryRun))
            return;

        try
        {
            var localNow = ResolveLocalNow(_options);
            var hasRowsNotImported = result.FutureRowsSkipped > 0
                || result.DataverseRowsSkipped > 0
                || result.SkippedRows.Count > 0;
            var status = hasRowsNotImported ? "REVISION" : "OK";
            var dryRunSuffix = result.DryRun ? " - SIMULACION" : "";

            await _reportSender.SendAsync(new ReconciliationEmailMessage
            {
                To = _options.SummaryRecipientEmail.Trim(),
                Subject = $"Importacion flujo de caja - {localNow:yyyy-MM-dd} - {status}{dryRunSuffix}",
                HtmlBody = BuildImportSummaryEmailHtml(result, localNow, status)
            }, ct);

            _logger.LogInformation(
                "Correo resumen de importacion de flujo de caja enviado a {Recipient}. Estado={Status}.",
                _options.SummaryRecipientEmail,
                status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible enviar el correo resumen de importacion de flujo de caja.");
        }
    }

    private async Task TrySendImportFailureEmailAsync(bool dryRun, Exception error, CancellationToken ct)
    {
        if (!ShouldSendSummaryEmail(dryRun))
            return;

        try
        {
            var localNow = ResolveLocalNow(_options);
            await _reportSender.SendAsync(new ReconciliationEmailMessage
            {
                To = _options.SummaryRecipientEmail.Trim(),
                Subject = $"Importacion flujo de caja - {localNow:yyyy-MM-dd} - ERROR",
                HtmlBody = BuildImportFailureEmailHtml(error, localNow, dryRun)
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible enviar el correo de error de importacion de flujo de caja.");
        }
    }

    private bool ShouldSendSummaryEmail(bool dryRun)
    {
        return _options.SendSummaryEmail
            && (!dryRun || _options.SendSummaryEmailOnDryRun)
            && !string.IsNullOrWhiteSpace(_options.SummaryRecipientEmail);
    }

    private string BuildImportSummaryEmailHtml(CashFlowImportResultDto result, DateTimeOffset localNow, string status)
    {
        var hasSkippedRows = result.FutureRowsSkipped > 0
            || result.DataverseRowsSkipped > 0
            || result.SkippedRows.Count > 0;
        var statusColor = hasSkippedRows ? "#9a4d00" : "#156f3d";
        var mode = result.DryRun ? "Simulacion" : "Importacion real";

        var builder = new StringBuilder();
        builder.AppendLine("<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#17263c;line-height:1.45;\">");
        builder.AppendLine("<h2 style=\"margin:0 0 8px;\">Resumen importacion flujo de caja</h2>");
        builder.AppendLine($"<p style=\"margin:0 0 16px;color:#526173;\">Archivo: {EncodeHtml(_options.FileName)} | Modo: {EncodeHtml(mode)} | Corte Bogota: {localNow:yyyy-MM-dd HH:mm}</p>");
        builder.AppendLine($"<p style=\"margin:0 0 16px;color:{statusColor};\"><strong>Estado: {EncodeHtml(status)}.</strong> {(hasSkippedRows ? "Hay filas omitidas accionables para revisar." : "No se detectaron filas accionables sin importar.")}</p>");
        builder.AppendLine("<table style=\"border-collapse:collapse;min-width:620px;margin:12px 0 18px;\">");
        builder.AppendLine(BuildMetricRow("Filas validas leidas", Number(result.RowsRead)));
        builder.AppendLine(BuildMetricRow("Movimientos", Number(result.MovementsRead)));
        builder.AppendLine(BuildMetricRow("Traslados internos", Number(result.TransfersRead)));
        builder.AppendLine(BuildMetricRow("Creadas en Dataverse", Number(result.Created)));
        builder.AppendLine(BuildMetricRow("Actualizadas en Dataverse", Number(result.Updated)));
        builder.AppendLine(BuildMetricRow("Sin cambios", Number(result.Unchanged)));
        builder.AppendLine(BuildMetricRow("Omitidas totales", Number(result.Skipped)));
        builder.AppendLine(BuildMetricRow("Filas vacias omitidas", Number(result.BlankRowsSkipped)));
        builder.AppendLine(BuildMetricRow("Fechas futuras omitidas", Number(result.FutureRowsSkipped)));
        builder.AppendLine(BuildMetricRow("Omitidas por Dataverse", Number(result.DataverseRowsSkipped)));
        builder.AppendLine(BuildMetricRow("Total entradas", Money(result.TotalEntries)));
        builder.AppendLine(BuildMetricRow("Total salidas", Money(result.TotalExits)));
        builder.AppendLine(BuildMetricRow("Valor traslados", Money(result.TransferValue)));
        builder.AppendLine("</table>");

        builder.AppendLine("<h3 style=\"margin:18px 0 8px;\">Resumen por flujo</h3>");
        builder.AppendLine("<table style=\"border-collapse:collapse;min-width:720px;margin:8px 0 18px;\">");
        builder.AppendLine("<tr><th style=\"text-align:left;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Flujo</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Filas</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Movimientos</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Traslados</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Entradas</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#eef3f8;\">Salidas</th></tr>");
        foreach (var flow in result.FlowSummaries)
        {
            builder.AppendLine($"<tr><td style=\"border:1px solid #d6dee6;padding:8px;\">{EncodeHtml(flow.SourceFlow)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Number(flow.Rows)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Number(flow.Movements)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Number(flow.Transfers)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Money(flow.Entries)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Money(flow.Exits)}</td></tr>");
        }
        builder.AppendLine("</table>");

        builder.AppendLine("<h3 style=\"margin:18px 0 8px;\">Filas no importadas para revisar</h3>");
        if (result.SkippedRows.Count == 0)
        {
            builder.AppendLine("<p style=\"margin:0 0 18px;\">Sin filas accionables omitidas. Solo puede haber filas vacias omitidas.</p>");
        }
        else
        {
            builder.AppendLine("<table style=\"border-collapse:collapse;min-width:760px;margin:8px 0 18px;\">");
            builder.AppendLine("<tr><th style=\"text-align:left;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Flujo</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Fila Excel</th><th style=\"text-align:left;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Fecha</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Entrada</th><th style=\"text-align:right;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Salida</th><th style=\"text-align:left;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Motivo</th><th style=\"text-align:left;border:1px solid #d6dee6;padding:8px;background:#fff3cd;\">Descripcion</th></tr>");
            foreach (var row in result.SkippedRows.Take(75))
            {
                builder.AppendLine($"<tr><td style=\"border:1px solid #d6dee6;padding:8px;\">{EncodeHtml(row.SourceFlow)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Number(row.RowNumber)}</td><td style=\"border:1px solid #d6dee6;padding:8px;\">{EncodeHtml(row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Sin fecha")}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Money(row.Entry)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{Money(row.Exit)}</td><td style=\"border:1px solid #d6dee6;padding:8px;\">{EncodeHtml(row.Reason)}</td><td style=\"border:1px solid #d6dee6;padding:8px;\">{EncodeHtml(Truncate(row.Description, 160))}</td></tr>");
            }
            builder.AppendLine("</table>");
            if (result.SkippedRows.Count > 75)
                builder.AppendLine($"<p style=\"font-size:12px;color:#607080;\">Se muestran 75 de {Number(result.SkippedRows.Count)} filas omitidas accionables.</p>");
        }

        builder.AppendLine("<p style=\"font-size:12px;color:#607080;margin-top:18px;\">Nota: las filas vacias del Excel se cuentan aparte y no se listan como pendientes. Las fechas futuras se omiten cuando CashFlowImport:IncludeFutureRows esta desactivado.</p>");
        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private string BuildImportFailureEmailHtml(Exception error, DateTimeOffset localNow, bool dryRun)
    {
        var mode = dryRun ? "Simulacion" : "Importacion real";
        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:#17263c;line-height:1.45;">
              <h2 style="margin:0 0 8px;">Error importando flujo de caja</h2>
              <p style="margin:0 0 16px;color:#526173;">Archivo: {EncodeHtml(_options.FileName)} | Modo: {EncodeHtml(mode)} | Corte Bogota: {localNow:yyyy-MM-dd HH:mm}</p>
              <p style="color:#9b1c1c;"><strong>La importacion no finalizo correctamente.</strong></p>
              <pre style="white-space:pre-wrap;background:#fff3f3;border:1px solid #f0c4c4;padding:12px;border-radius:4px;">{EncodeHtml(Truncate(error.ToString(), 4000))}</pre>
            </div>
            """;
    }

    private static string BuildMetricRow(string label, string value)
    {
        return $"<tr><td style=\"border:1px solid #d6dee6;padding:8px;background:#f8fafc;\">{EncodeHtml(label)}</td><td style=\"border:1px solid #d6dee6;padding:8px;text-align:right;\">{EncodeHtml(value)}</td></tr>";
    }

    private static string Money(decimal value) => value.ToString("C0", ColombianCulture);

    private static string Number(int value) => value.ToString("N0", ColombianCulture);

    private static string EncodeHtml(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static DateTimeOffset ResolveLocalNow(CashFlowImportOptions options)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(options.TimeZoneId);
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeTextForMatching(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeKeyPart(string value)
    {
        var normalized = NormalizeHeader(value);
        return string.IsNullOrWhiteSpace(normalized) ? "na" : normalized;
    }

    private static string NormalizeAuthorityHost(string? value)
    {
        var authority = string.IsNullOrWhiteSpace(value)
            ? "https://login.microsoftonline.com"
            : value.Trim();

        return authority.TrimEnd('/');
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static DateOnly ResolveLocalToday(CashFlowImportOptions options)
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone(options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private sealed record CashFlowWorkbookReadResult(
        IReadOnlyList<CashFlowImportRowDto> Rows,
        int BlankRowsSkipped,
        int FutureRowsSkipped,
        IReadOnlyList<CashFlowImportSkippedRowDto> SkippedRows);
}
