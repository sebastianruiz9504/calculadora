using System.Globalization;
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
    private readonly ILogger<CashFlowImportService> _logger;

    public CashFlowImportService(
        IDataverseService dataverse,
        IOptions<CashFlowImportOptions> options,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<CashFlowImportService> logger)
    {
        _dataverse = dataverse;
        _options = options.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CashFlowImportResultDto> ImportAsync(bool dryRun = false, CancellationToken ct = default)
    {
        using var workbookStream = await DownloadWorkbookAsync(ct);
        var readResult = ReadWorkbookRowsWithSkipped(workbookStream, _options);
        var rows = readResult.Rows;
        var resolvedDryRun = dryRun || _options.DryRun;

        _logger.LogInformation(
            "Flujo de caja leido desde {FileName}: {Rows} filas validas, {Movements} movimientos, {Transfers} traslados. DryRun={DryRun}.",
            _options.FileName,
            rows.Count,
            rows.Count(static row => !row.IsTransfer),
            rows.Count(static row => row.IsTransfer),
            resolvedDryRun);

        var upsert = await _dataverse.UpsertCashFlowRowsAsync(rows, resolvedDryRun, ct);
        return BuildResult(rows, readResult.Skipped + upsert.Skipped, readResult.FutureRowsSkipped, upsert, resolvedDryRun);
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
        var skipped = 0;
        var futureRowsSkipped = 0;
        var today = ResolveLocalToday(options);

        skipped += ReadTableRows(workbook, options, options.CloudTableName, "Cloud", options.CloudBankAccountCode, options.CloudBankAccountName, today, rows, out var cloudFutureRows);
        skipped += ReadTableRows(workbook, options, options.CopiersTableName, "Copiers", options.CopiersBankAccountCode, options.CopiersBankAccountName, today, rows, out var copiersFutureRows);
        futureRowsSkipped = cloudFutureRows + copiersFutureRows;

        return new CashFlowWorkbookReadResult(rows, skipped, futureRowsSkipped);
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
                skipped++;
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
        IReadOnlyList<CashFlowImportRowDto> rows,
        int skipped,
        int futureRowsSkipped,
        CashFlowDataverseUpsertResultDto upsert,
        bool dryRun)
    {
        return new CashFlowImportResultDto
        {
            DryRun = dryRun,
            RowsRead = rows.Count,
            MovementsRead = rows.Count(static row => !row.IsTransfer),
            TransfersRead = rows.Count(static row => row.IsTransfer),
            Skipped = skipped,
            FutureRowsSkipped = futureRowsSkipped,
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
            SampleRows = rows
                .OrderByDescending(static row => row.Date)
                .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToArray()
        };
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

    private sealed record CashFlowWorkbookReadResult(IReadOnlyList<CashFlowImportRowDto> Rows, int Skipped, int FutureRowsSkipped);
}
