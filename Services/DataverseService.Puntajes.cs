using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private static readonly Regex ScoreDescriptionFieldRegex = new(
        "(?<key>Cliente|Fecha aprovisionamiento|Tipo contrato|Puntaje|Comisión|Comision|BusinessId)\\s*:\\s*(?<value>.*?)(?=(Cliente|Fecha aprovisionamiento|Tipo contrato|Puntaje|Comisión|Comision|BusinessId|Líneas|Lineas)\\s*:|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly HashSet<int> AllowedFirstContractOptionValues = new() { 1, 2 };
    private static readonly HashSet<int> AllowedLineOptionValues = new() { 645250000, 645250001, 645250002, 645250003, 645250004, 645250005, 645250006, 645250007 };
    private static readonly HashSet<int> AllowedVerticalOptionValues = new() { 645250000, 645250001 };

    public async Task<ScoreBoardDto> GetScoreBoardAsync(ScorePeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var filterParts = new List<string>
        {
            $"{_scoresContractStartDateField} ne null"
        };

        var periodFilter = BuildScorePeriodFilter(filter);
        if (!string.IsNullOrWhiteSpace(periodFilter))
        {
            filterParts.Add(periodFilter);
        }

        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(string.Join(" and ", filterParts))}&$orderby={_scoresContractStartDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var records = rawRecords
            .Select(ParseScoreRecord)
            .Where(item => item is not null)
            .Cast<ScoreRecordDto>()
            .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Offer, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = records
            .GroupBy(GetScoreClientGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedRecords = group
                    .OrderBy(item => item.ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Offer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SalesPerson, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var first = orderedRecords[0];
                return new ScoreClientGroupDto
                {
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    SalesPerson = orderedRecords
                        .Select(item => item.SalesPerson)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? "Sin vendedor",
                    AllVerified = orderedRecords.Count > 0 && orderedRecords.All(item => item.IsVerified),
                    RecordCount = orderedRecords.Count,
                    ProductLinesCount = orderedRecords.Sum(item => item.ProductLinesCount),
                    TotalCommission = RoundCurrency(orderedRecords.Sum(item => item.Commission)),
                    TotalScore = RoundCurrency(orderedRecords.Sum(item => item.Score)),
                    TotalMonthlyValue = RoundCurrency(orderedRecords.Sum(item => item.MonthlyValue)),
                    TotalAnnualValue = RoundCurrency(orderedRecords.Sum(item => item.AnnualValue)),
                    Records = orderedRecords
                };
            })
            .OrderBy(group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Records[0].ContractStartDateValue, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ScoreBoardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            ClientsCount = groups.Count,
            RecordsCount = records.Count,
            ProductLinesCount = records.Sum(item => item.ProductLinesCount),
            TotalCommission = RoundCurrency(records.Sum(item => item.Commission)),
            TotalScore = RoundCurrency(records.Sum(item => item.Score)),
            TotalMonthlyValue = RoundCurrency(records.Sum(item => item.MonthlyValue)),
            TotalAnnualValue = RoundCurrency(records.Sum(item => item.AnnualValue)),
            Groups = groups
        };
    }

    public async Task VerifyScoreRecordAsync(ScoreVerificationRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        if (!AllowedFirstContractOptionValues.Contains(request.FirstContractOptionValue))
            throw new InvalidOperationException("La opcion de primer contrato no es valida.");

        if (!AllowedLineOptionValues.Contains(request.LineOptionValue))
            throw new InvalidOperationException("La linea seleccionada no es valida.");

        if (!AllowedVerticalOptionValues.Contains(request.VerticalOptionValue))
            throw new InvalidOperationException("La vertical seleccionada no es valida.");

        var updateUrl = $"/api/data/v9.2/{_scoresTableSetName}({recordId})";
        Exception? lastError = null;
        foreach (var payload in BuildVerificationPayloadCandidates(request))
        {
            try
            {
                await CallDataverseSendAsync(updateUrl, "PATCH", payload, httpContext.User, ct);
                return;
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("No se pudo guardar la verificacion en Dataverse.", lastError);
    }

    public async Task<ScoreOfferDownloadResult?> DownloadScoreOfferAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadataUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})?$select={_scoresOfferField}";
        var metadataJson = await CallDataverseGetJsonAsync(metadataUrl, httpContext.User, ct, AddFormattedValueHeaders);

        using var metadataDocument = JsonDocument.Parse(metadataJson);
        var metadata = metadataDocument.RootElement;
        var offerValue = ReadString(metadata, _scoresOfferField).Trim();
        var offerDisplay = ReadString(metadata, $"{_scoresOfferField}{FormattedValueAnnotationSuffix}").Trim();
        var fileName = string.IsNullOrWhiteSpace(offerDisplay) ? offerValue : offerDisplay;

        if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(offerValue))
            return null;

        if (Uri.TryCreate(offerValue, UriKind.Absolute, out var absoluteOfferUrl)
            && (string.Equals(absoluteOfferUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteOfferUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return new ScoreOfferDownloadResult
            {
                RedirectUrl = absoluteOfferUrl.ToString(),
                FileName = string.IsNullOrWhiteSpace(fileName)
                    ? Path.GetFileName(absoluteOfferUrl.LocalPath)
                    : fileName
            };
        }

        var relativeFileUrl = $"/api/data/v9.2/{_scoresTableSetName}({normalizedRecordId})/{_scoresOfferField}/$value";
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeFileUrl;
                options.HttpMethod = "GET";
            },
            user: httpContext.User,
            cancellationToken: ct);

        if (result is not HttpResponseMessage response)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var memoryStream = new MemoryStream();
        await responseStream.CopyToAsync(memoryStream, ct);
        var bodyBytes = memoryStream.ToArray();

        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length > 0
                ? Encoding.UTF8.GetString(bodyBytes)
                : "";
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new ScoreOfferDownloadResult
        {
            Content = bodyBytes,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            FileName = ResolveOfferDownloadFileName(response, fileName, normalizedRecordId)
        };
    }

    private string BuildScorePeriodFilter(ScorePeriodFilter filter)
    {
        var today = GetBogotaToday();
        if (filter == ScorePeriodFilter.ThisYear)
        {
            var yearStart = new DateOnly(today.Year, 1, 1);
            var nextYearStart = yearStart.AddYears(1);
            return $"{_scoresContractStartDateField} ge {yearStart:yyyy-MM-dd} and {_scoresContractStartDateField} lt {nextYearStart:yyyy-MM-dd}";
        }

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var targetMonthStart = filter switch
        {
            ScorePeriodFilter.PreviousMonth => monthStart.AddMonths(-1),
            ScorePeriodFilter.NextMonth => monthStart.AddMonths(1),
            _ => monthStart
        };

        var nextMonthStart = targetMonthStart.AddMonths(1);
        return $"{_scoresContractStartDateField} ge {targetMonthStart:yyyy-MM-dd} and {_scoresContractStartDateField} lt {nextMonthStart:yyyy-MM-dd}";
    }

    private ScoreRecordDto? ParseScoreRecord(JsonElement item)
    {
        var recordId = ReadString(item, _scoresIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var contractStartDate = ReadDateOnly(item, _scoresContractStartDateField);
        if (!contractStartDate.HasValue)
            return null;

        var rawDescription = ReadString(item, _scoresDescriptionField);
        var parsedDescription = ParseScoreDescription(rawDescription);
        var productLines = parsedDescription.ProductLines
            .OrderBy(line => line.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.LineId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clientId = ReadDataverseLookupId(item, _scoresClientField, "cliente");
        var clientName = ReadDataverseDisplayValue(item, _scoresClientField, "cliente");
        clientName = string.IsNullOrWhiteSpace(clientName)
            ? parsedDescription.ClientName
            : clientName.Trim();

        clientName = string.IsNullOrWhiteSpace(clientName)
            ? "Cliente sin asignar"
            : clientName;

        var score = RoundCurrency(ReadDecimal(item, _scoresScoreField) ?? parsedDescription.Score ?? 0m);
        var commission = RoundCurrency(ReadDecimal(item, _scoresCommissionField) ?? parsedDescription.Commission ?? 0m);
        var salesPerson = ReadDataverseDisplayValue(item, _scoresSalesPersonField, "vendedor");
        var offer = ReadDataverseDisplayValue(item, _scoresOfferField, "oferta");
        var isVerified = ReadYesNoOption(item, _scoresVerifiedField);

        return new ScoreRecordDto
        {
            RecordId = recordId,
            ClientId = clientId,
            ClientName = clientName,
            ContractStartDateValue = contractStartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ContractStartDateDisplay = contractStartDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Score = score,
            Commission = commission,
            SalesPerson = string.IsNullOrWhiteSpace(salesPerson) ? "Sin vendedor" : salesPerson.Trim(),
            Offer = string.IsNullOrWhiteSpace(offer) ? "Sin oferta" : offer.Trim(),
            OfferFileName = offer.Trim(),
            HasOffer = !string.IsNullOrWhiteSpace(offer),
            IsVerified = isVerified,
            FirstContractOptionValue = ReadOptionValue(item, _scoresFirstContractField),
            LineOptionValue = ReadOptionValue(item, _scoresLineField),
            VerticalOptionValue = ReadOptionValue(item, _scoresVerticalField),
            DescriptionClientName = parsedDescription.ClientName,
            ProvisioningDateValue = parsedDescription.ProvisioningDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ProvisioningDateDisplay = parsedDescription.ProvisioningDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ContractType = parsedDescription.ContractType,
            BusinessId = parsedDescription.BusinessId,
            RawDescription = rawDescription,
            ProductLinesCount = productLines.Count,
            MonthlyValue = RoundCurrency(productLines.Sum(line => line.MonthlyValue)),
            AnnualValue = RoundCurrency(productLines.Sum(line => line.AnnualValue)),
            ProductLines = productLines
        };
    }

    private static string GetScoreClientGroupKey(ScoreRecordDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.ClientId))
            return $"id:{item.ClientId}";

        return $"name:{item.ClientName}";
    }

    private static ScoreDescriptionParseResult ParseScoreDescription(string? rawDescription)
    {
        var result = new ScoreDescriptionParseResult();
        var raw = (rawDescription ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var metadata = new StringBuilder(raw.Length);
        var cursor = 0;
        while (cursor < raw.Length)
        {
            var linesIndex = FindNextLinesLabel(raw, cursor);
            if (linesIndex < 0)
            {
                metadata.Append(raw, cursor, raw.Length - cursor);
                break;
            }

            metadata.Append(raw, cursor, linesIndex - cursor);
            var labelLength = raw.AsSpan(linesIndex).StartsWith("Líneas:", StringComparison.OrdinalIgnoreCase)
                ? "Líneas:".Length
                : "Lineas:".Length;

            var arrayStart = SkipWhitespace(raw, linesIndex + labelLength);
            if (arrayStart < raw.Length && raw[arrayStart] == '[')
            {
                var (jsonArray, nextIndex) = ExtractJsonArray(raw, arrayStart);
                if (!string.IsNullOrWhiteSpace(jsonArray))
                {
                    var parsedLines = DeserializeJsonOrDefault<List<RawScoreProductLine>>(jsonArray)
                        ?? new List<RawScoreProductLine>();
                    foreach (var line in parsedLines)
                    {
                        result.ProductLines.Add(ToScoreProductLine(line, result.ProductLines.Count + 1));
                    }

                    cursor = nextIndex;
                    continue;
                }
            }

            cursor = linesIndex + labelLength;
        }

        foreach (Match match in ScoreDescriptionFieldRegex.Matches(metadata.ToString()))
        {
            if (!match.Success)
                continue;

            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            switch (NormalizeDescriptionKey(key))
            {
                case "cliente":
                    if (string.IsNullOrWhiteSpace(result.ClientName))
                        result.ClientName = value;
                    break;
                case "fechaaprovisionamiento":
                    if (!result.ProvisioningDate.HasValue && TryParseDateOnly(value, out var provisioningDate))
                        result.ProvisioningDate = provisioningDate;
                    break;
                case "tipocontrato":
                    if (string.IsNullOrWhiteSpace(result.ContractType))
                        result.ContractType = value;
                    break;
                case "puntaje":
                    result.Score ??= ParseLooseDecimal(value);
                    break;
                case "comision":
                    result.Commission ??= ParseLooseDecimal(value);
                    break;
                case "businessid":
                    if (string.IsNullOrWhiteSpace(result.BusinessId))
                        result.BusinessId = value;
                    break;
            }
        }

        result.ClientName = result.ClientName.Trim();
        result.ContractType = result.ContractType.Trim();
        result.BusinessId = result.BusinessId.Trim();
        return result;
    }

    private static ScoreProductLineDto ToScoreProductLine(RawScoreProductLine rawLine, int index)
    {
        var quantity = Math.Max(rawLine.Quantity, 0);
        var unitMonthlyValue = RoundCurrency(rawLine.Number);
        var monthlyValue = RoundCurrency(quantity * unitMonthlyValue);
        var annualValue = RoundCurrency(monthlyValue * 12m);
        var productName = string.IsNullOrWhiteSpace(rawLine.ProductName)
            ? $"Producto {index}"
            : rawLine.ProductName.Trim();

        return new ScoreProductLineDto
        {
            LineId = rawLine.LineId?.Trim() ?? "",
            ProductId = rawLine.ProductId?.Trim() ?? "",
            ProductName = productName,
            Quantity = quantity,
            MonthlyUnitValue = unitMonthlyValue,
            MonthlyValue = monthlyValue,
            AnnualValue = annualValue
        };
    }

    private static string NormalizeDescriptionKey(string key)
    {
        var normalized = key
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(normalized)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static int FindNextLinesLabel(string raw, int startIndex)
    {
        var accented = raw.IndexOf("Líneas:", startIndex, StringComparison.OrdinalIgnoreCase);
        var plain = raw.IndexOf("Lineas:", startIndex, StringComparison.OrdinalIgnoreCase);

        if (accented < 0)
            return plain;

        if (plain < 0)
            return accented;

        return Math.Min(accented, plain);
    }

    private static int SkipWhitespace(string raw, int startIndex)
    {
        var index = startIndex;
        while (index < raw.Length && char.IsWhiteSpace(raw[index]))
        {
            index++;
        }

        return index;
    }

    private static (string JsonArray, int NextIndex) ExtractJsonArray(string raw, int startIndex)
    {
        if (startIndex < 0 || startIndex >= raw.Length || raw[startIndex] != '[')
            return ("", startIndex);

        var depth = 0;
        var insideString = false;
        var escapeNext = false;

        for (var index = startIndex; index < raw.Length; index++)
        {
            var current = raw[index];
            if (insideString)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (current == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (current == '"')
                {
                    insideString = false;
                }

                continue;
            }

            if (current == '"')
            {
                insideString = true;
                continue;
            }

            if (current == '[')
            {
                depth++;
                continue;
            }

            if (current != ']')
                continue;

            depth--;
            if (depth == 0)
            {
                return (raw[startIndex..(index + 1)], index + 1);
            }
        }

        return ("", startIndex);
    }

    private static decimal? ParseLooseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
            return invariantValue;

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var colombianValue))
            return colombianValue;

        var normalized = trimmed.Replace(" ", "");
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            var lastComma = normalized.LastIndexOf(',');
            var lastDot = normalized.LastIndexOf('.');
            normalized = lastComma > lastDot
                ? normalized.Replace(".", "").Replace(',', '.')
                : normalized.Replace(",", "");
        }
        else if (normalized.Contains(','))
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var normalizedValue)
            ? normalizedValue
            : null;
    }

    private static bool ReadYesNoOption(JsonElement item, string logicalName)
    {
        var formatted = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (string.Equals(formatted, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "sí", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.TryGetProperty(logicalName, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
                return numericValue == 1;

            if (property.ValueKind == JsonValueKind.String)
            {
                var raw = property.GetString();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
                    return parsedValue == 1;
            }
        }

        return false;
    }

    private static int ReadOptionValue(JsonElement item, string logicalName)
    {
        if (!item.TryGetProperty(logicalName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
            return numericValue;

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return 0;
    }

    private IEnumerable<Dictionary<string, object?>> BuildVerificationPayloadCandidates(ScoreVerificationRequest request)
    {
        var firstContractAsBool = request.FirstContractOptionValue == 1;

        yield return new Dictionary<string, object?>
        {
            [_scoresFirstContractField] = request.FirstContractOptionValue,
            [_scoresLineField] = request.LineOptionValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresVerifiedField] = 1
        };

        yield return new Dictionary<string, object?>
        {
            [_scoresFirstContractField] = request.FirstContractOptionValue,
            [_scoresLineField] = request.LineOptionValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresVerifiedField] = true
        };

        yield return new Dictionary<string, object?>
        {
            [_scoresFirstContractField] = firstContractAsBool,
            [_scoresLineField] = request.LineOptionValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresVerifiedField] = 1
        };

        yield return new Dictionary<string, object?>
        {
            [_scoresFirstContractField] = firstContractAsBool,
            [_scoresLineField] = request.LineOptionValue,
            [_scoresVerticalField] = request.VerticalOptionValue,
            [_scoresVerifiedField] = true
        };
    }

    private string ResolveOfferDownloadFileName(HttpResponseMessage response, string fallbackFileName, string recordId)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        if (!string.IsNullOrWhiteSpace(disposition?.FileNameStar))
            return disposition.FileNameStar.Trim('"');

        if (!string.IsNullOrWhiteSpace(disposition?.FileName))
            return disposition.FileName.Trim('"');

        if (response.Headers.TryGetValues("x-ms-file-name", out var fileNameValues))
        {
            var headerValue = fileNameValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
                return headerValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackFileName))
            return fallbackFileName;

        return $"oferta-{recordId}.bin";
    }

    private static string ReadDataverseDisplayValue(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        var formattedDirect = ReadString(item, $"{logicalName}{FormattedValueAnnotationSuffix}");
        if (!string.IsNullOrWhiteSpace(formattedDirect))
            return formattedDirect.Trim();

        var direct = ReadString(item, logicalName);
        if (!string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        foreach (var lookupProperty in GetLookupCandidateProperties(item, logicalName, fallbackTokens))
        {
            var formattedLookupValue = ReadLookupFormattedValue(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(formattedLookupValue))
                return formattedLookupValue.Trim();

            var rawLookupValue = ReadString(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(rawLookupValue))
                return rawLookupValue.Trim();
        }

        return "";
    }

    private static string ReadDataverseLookupId(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        foreach (var lookupProperty in GetLookupCandidateProperties(item, logicalName, fallbackTokens))
        {
            var rawLookupValue = ReadString(item, lookupProperty);
            if (!string.IsNullOrWhiteSpace(rawLookupValue))
                return rawLookupValue.Trim();
        }

        return "";
    }

    private static IEnumerable<string> GetLookupCandidateProperties(JsonElement item, string logicalName, params string[] fallbackTokens)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
                results.Add(trimmed);
        }

        AddCandidate($"_{logicalName}_value");

        foreach (var property in item.EnumerateObject())
        {
            if (!property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Name.Contains(logicalName, StringComparison.OrdinalIgnoreCase)
                || fallbackTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                AddCandidate(property.Name);
            }
        }

        return results;
    }

    private sealed class ScoreDescriptionParseResult
    {
        public string ClientName { get; set; } = "";
        public DateOnly? ProvisioningDate { get; set; }
        public string ContractType { get; set; } = "";
        public decimal? Score { get; set; }
        public decimal? Commission { get; set; }
        public string BusinessId { get; set; } = "";
        public List<ScoreProductLineDto> ProductLines { get; } = new();
    }

    private sealed class RawScoreProductLine
    {
        [JsonPropertyName("lineId")]
        public string? LineId { get; set; }

        [JsonPropertyName("productoId")]
        public string? ProductId { get; set; }

        [JsonPropertyName("productoNombre")]
        public string? ProductName { get; set; }

        [JsonPropertyName("cantidad")]
        public int Quantity { get; set; }

        [JsonPropertyName("number")]
        public decimal Number { get; set; }
    }
}
