using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace CotizadorInterno.Web.Services;

public sealed partial class SharePointRebatesProvider : ISharePointRebatesProvider
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly IReadOnlyDictionary<string, int> SpanishMonths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["enero"] = 1,
            ["febrero"] = 2,
            ["marzo"] = 3,
            ["abril"] = 4,
            ["mayo"] = 5,
            ["junio"] = 6,
            ["julio"] = 7,
            ["agosto"] = 8,
            ["septiembre"] = 9,
            ["setiembre"] = 9,
            ["octubre"] = 10,
            ["noviembre"] = 11,
            ["diciembre"] = 12
        };

    private readonly SharePointRebatesOptions _options;
    private readonly M365Options _m365Options;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SharePointRebatesProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private SharePointRebatesSnapshot? _lastGood;

    public SharePointRebatesProvider(
        IOptions<SharePointRebatesOptions> options,
        IOptions<M365Options> m365Options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<SharePointRebatesProvider> logger)
    {
        _options = options.Value;
        _m365Options = m365Options.Value;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SharePointRebatesSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("La fuente SharePoint de Rebates esta deshabilitada.");

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.LocalFilePath))
            {
                await using var localStream = File.OpenRead(_options.LocalFilePath);
                var localRecords = ReadWorkbookRows(localStream, _options.TableName);
                return new SharePointRebatesSnapshot(
                    localRecords,
                    $"local:{File.GetLastWriteTimeUtc(_options.LocalFilePath).Ticks}",
                    File.GetLastWriteTimeUtc(_options.LocalFilePath),
                    false,
                    "");
            }

            ValidateRemoteOptions();
            var token = await GetGraphAccessTokenAsync(ct);
            var metadata = await GetItemMetadataAsync(token, ct);
            if (_lastGood is not null
                && !string.IsNullOrWhiteSpace(metadata.ETag)
                && string.Equals(_lastGood.ETag, metadata.ETag, StringComparison.Ordinal))
            {
                return _lastGood with { IsStale = false, Warning = "" };
            }

            await using var workbookStream = await DownloadWorkbookAsync(token, ct);
            var records = ReadWorkbookRows(workbookStream, _options.TableName);
            var snapshot = new SharePointRebatesSnapshot(
                records,
                metadata.ETag,
                metadata.LastModifiedUtc,
                false,
                "");
            _lastGood = snapshot;

            _logger.LogInformation(
                "Rebates actualizados desde SharePoint {FileName}/{TableName}: {Rows} filas, eTag {ETag}.",
                _options.FileName,
                _options.TableName,
                records.Count,
                metadata.ETag);
            return snapshot;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_lastGood is null)
            {
                throw new InvalidOperationException(
                    $"No fue posible leer la tabla {_options.TableName} de {_options.FileName} en SharePoint y no existe una copia valida en cache.",
                    ex);
            }

            var warning = $"Rebates usa la ultima lectura valida de SharePoint porque la actualizacion fallo: {ex.Message}";
            _logger.LogWarning(ex, "No fue posible refrescar Rebates desde SharePoint; se usa el ultimo snapshot valido.");
            return _lastGood with { IsStale = true, Warning = warning };
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal static IReadOnlyList<SharePointRebateRecord> ReadWorkbookRows(Stream workbookStream, string tableName = "Rebates")
    {
        using var document = SpreadsheetDocument.Open(workbookStream, false);
        var workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException("El archivo de rebates no contiene un libro de Excel valido.");
        var tableMatch = workbookPart.WorksheetParts
            .SelectMany(static worksheetPart => worksheetPart.TableDefinitionParts
                .Select(tablePart => new { WorksheetPart = worksheetPart, TablePart = tablePart, Table = tablePart.Table }))
            .FirstOrDefault(candidate =>
                candidate.Table is not null
                && (string.Equals(candidate.Table.Name?.Value, tableName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Table.DisplayName?.Value, tableName, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException($"No encontramos la tabla de Excel '{tableName}'.");

        var table = tableMatch.Table!;
        var tableReference = table.Reference?.Value
            ?? throw new InvalidOperationException($"La tabla '{tableName}' no tiene un rango valido.");
        var range = ParseRange(tableReference);
        var fieldNames = table.TableColumns?
            .Elements<TableColumn>()
            .Select(static field => field.Name?.Value?.Trim() ?? "")
            .ToList()
            ?? new List<string>();
        var dateIndex = fieldNames.FindIndex(static name => string.Equals(name, "Fecha", StringComparison.OrdinalIgnoreCase));
        var valueIndex = fieldNames.FindIndex(static name => string.Equals(name, "Valor", StringComparison.OrdinalIgnoreCase));
        if (dateIndex < 0 || valueIndex < 0)
            throw new InvalidOperationException($"La tabla '{tableName}' debe contener las columnas Fecha y Valor.");

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
            .Elements<SharedStringItem>()
            .Select(static item => item.InnerText)
            .ToList()
            ?? new List<string>();
        var cells = tableMatch.WorksheetPart.Worksheet
            .Descendants<Cell>()
            .Where(cell => TryParseCellReference(cell.CellReference?.Value, out var column, out var row)
                && row > range.StartRow
                && row <= range.EndRow
                && column >= range.StartColumn
                && column <= range.EndColumn)
            .ToDictionary(
                cell => cell.CellReference!.Value!,
                StringComparer.OrdinalIgnoreCase);

        var records = new List<SharePointRebateRecord>();
        for (var sourceRow = range.StartRow + 1; sourceRow <= range.EndRow; sourceRow++)
        {
            cells.TryGetValue(BuildCellReference(range.StartColumn + dateIndex, sourceRow), out var dateCell);
            cells.TryGetValue(BuildCellReference(range.StartColumn + valueIndex, sourceRow), out var valueCell);
            var rawDate = ReadCellText(dateCell, sharedStrings);
            var rawValue = ReadCellText(valueCell, sharedStrings);
            if (string.IsNullOrWhiteSpace(rawDate) && string.IsNullOrWhiteSpace(rawValue))
                continue;

            if (!TryReadDate(rawDate, out var date))
                throw new InvalidOperationException($"Fecha de rebate invalida en la fila {sourceRow}: '{rawDate}'.");
            if (!TryReadDecimal(rawValue, out var value))
                throw new InvalidOperationException($"Valor de rebate invalido en la fila {sourceRow}: '{rawValue}'.");

            records.Add(new SharePointRebateRecord(
                $"sharepoint-rebates-{sourceRow}",
                date,
                decimal.Round(value, 2, MidpointRounding.AwayFromZero),
                sourceRow));
        }

        return records;
    }

    private static bool TryReadDate(string raw, out DateOnly date)
    {
        raw = raw.Trim();
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serialDate)
            && serialDate is > 0 and < 2_958_466)
        {
            try
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
                return true;
            }
            catch (ArgumentException)
            {
                // Continue with text parsing.
            }
        }

        if (DateOnly.TryParse(raw, ColombianCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            || DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
        {
            return true;
        }

        var normalized = RemoveDiacritics(raw).ToLowerInvariant().Replace(" del ", " de ", StringComparison.Ordinal);
        var match = SpanishLongDateRegex().Match(normalized);
        if (!match.Success
            || !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(match.Groups["year"].Value.Replace(".", "", StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !SpanishMonths.TryGetValue(match.Groups["month"].Value, out var month))
        {
            date = default;
            return false;
        }

        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static bool TryReadDecimal(string raw, out decimal value)
    {
        raw = raw.Trim();
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(raw, NumberStyles.Currency, ColombianCulture, out value))
        {
            return true;
        }

        var formula = SimpleArithmeticFormulaRegex().Match(raw.TrimStart('='));
        if (!formula.Success
            || !decimal.TryParse(formula.Groups["left"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var left)
            || !decimal.TryParse(formula.Groups["right"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
        {
            value = 0m;
            return false;
        }

        switch (formula.Groups["operator"].Value)
        {
            case "+": value = left + right; return true;
            case "-": value = left - right; return true;
            case "*": value = left * right; return true;
            case "/" when right != 0m: value = left / right; return true;
            default: value = 0m; return false;
        }
    }

    private static string ReadCellText(Cell? cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell is null)
            return "";
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(cell.CellValue?.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";
        return cell.CellValue?.InnerText ?? cell.InnerText ?? "";
    }

    private static CellRange ParseRange(string reference)
    {
        var parts = reference.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !TryParseCellReference(parts[0], out var startColumn, out var startRow)
            || !TryParseCellReference(parts[1], out var endColumn, out var endRow))
        {
            throw new InvalidOperationException($"Rango de tabla invalido: '{reference}'.");
        }
        return new CellRange(startColumn, startRow, endColumn, endRow);
    }

    private static bool TryParseCellReference(string? reference, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }
        return column > 0
            && index < reference.Length
            && int.TryParse(reference[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out row)
            && row > 0;
    }

    private static string BuildCellReference(int column, int row)
    {
        var name = "";
        while (column > 0)
        {
            column--;
            name = (char)('A' + (column % 26)) + name;
            column /= 26;
        }
        return $"{name}{row}";
    }

    private async Task<GraphItemMetadata> GetItemMetadataAsync(string token, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/items/{Uri.EscapeDataString(_options.ItemId)}?$select=eTag,lastModifiedDateTime,name"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft Graph no pudo consultar el Excel de rebates: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 600)}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
        if (!string.Equals(name, _options.FileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El item configurado corresponde a '{name}', no a '{_options.FileName}'.");

        var etag = root.TryGetProperty("eTag", out var etagElement) ? etagElement.GetString() ?? "" : "";
        DateTimeOffset? modified = null;
        if (root.TryGetProperty("lastModifiedDateTime", out var modifiedElement)
            && DateTimeOffset.TryParse(modifiedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            modified = parsed;
        }

        return new GraphItemMetadata(etag, modified);
    }

    private async Task<MemoryStream> DownloadWorkbookAsync(string token, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildGraphUri($"/drives/{Uri.EscapeDataString(_options.DriveId)}/items/{Uri.EscapeDataString(_options.ItemId)}/content"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Microsoft Graph no pudo descargar el Excel de rebates: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 600)}");
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
            throw new InvalidOperationException("Configura M365:ClientId y AzureAd:TenantId para leer Rebates desde Microsoft Graph.");

        var builder = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"{NormalizeAuthorityHost(_m365Options.AuthorityHost)}/{tenantId}");
        var certificate = LoadCertificateOrDefault();
        if (certificate is not null)
            builder.WithCertificate(certificate);
        else if (!string.IsNullOrWhiteSpace(clientSecret))
            builder.WithClientSecret(clientSecret);
        else
            throw new InvalidOperationException("Configura una credencial M365 valida para leer Rebates desde Microsoft Graph.");

        try
        {
            var result = await builder.Build().AcquireTokenForClient(ResolveGraphTokenScopes()).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw new InvalidOperationException("No fue posible obtener token app-only de Microsoft Graph para leer Rebates.", ex);
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
        if (!Enum.TryParse<StoreName>(_m365Options.CertificateStoreName, true, out var storeName))
            storeName = StoreName.My;
        if (!Enum.TryParse<StoreLocation>(_m365Options.CertificateStoreLocation, true, out var storeLocation))
            storeLocation = StoreLocation.CurrentUser;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _m365Options.CertificateThumbprint.Trim(), false);
        return certificates.Count == 0 ? null : certificates[0];
    }

    private void ValidateRemoteOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.DriveId) || string.IsNullOrWhiteSpace(_options.ItemId))
            throw new InvalidOperationException("Configura SharePointRebates:DriveId y SharePointRebates:ItemId.");
        if (string.IsNullOrWhiteSpace(_options.TableName))
            throw new InvalidOperationException("Configura SharePointRebates:TableName.");
    }

    private Uri BuildGraphUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_m365Options.GraphBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _m365Options.GraphBaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private string[] ResolveGraphTokenScopes()
    {
        var scopes = _m365Options.Scopes?
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim())
            .ToArray();
        return scopes is { Length: > 0 } ? scopes : new[] { GraphDefaultScope };
    }

    private static string NormalizeAuthorityHost(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "https://login.microsoftonline.com" : value.TrimEnd('/');

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [GeneratedRegex(@"(?:^|,\s*)(?<day>\d{1,2})\s+de\s+(?<month>[a-z]+)\s+de\s+(?<year>[\d\.]{4,6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpanishLongDateRegex();

    [GeneratedRegex(@"^\s*(?<left>-?\d+(?:\.\d+)?)\s*(?<operator>[+\-*/])\s*(?<right>-?\d+(?:\.\d+)?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleArithmeticFormulaRegex();

    private sealed record GraphItemMetadata(string ETag, DateTimeOffset? LastModifiedUtc);
    private sealed record CellRange(int StartColumn, int StartRow, int EndColumn, int EndRow);
}
