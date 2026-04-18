using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CuentasCobro;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CuentaCobroLogicalName = "cr07a_cuentasdecobro";
    private const string CuentaCobroFallbackEntitySetName = "cr07a_cuentasdecobros";
    private const string CuentaCobroFallbackIdField = "cr07a_cuentasdecobroid";
    private const string CuentaCobroFallbackPrimaryNameField = "cr07a_name";
    private const string CuentaCobroPeriodFallbackField = "createdon";
    private const string CuentaCobroModifiedOnField = "modifiedon";
    private const string CuentaCobroReceptorField = "cr07a_nombrereceptor";
    private const string CuentaCobroNitField = "cr07a_nitocedula";
    private const string CuentaCobroValorTotalField = "cr07a_valortotal";
    private const string CuentaCobroReteFuentePorcentajeField = "cr07a_retefuenteporcentaje";
    private const string CuentaCobroValorPagoField = "cr07a_valorpago";
    private const string CuentaCobroReteFuenteValorField = "cr07a_rteftevalor";
    private const string CuentaCobroObservacionesField = "cr07a_observaciones";
    private const string CuentaCobroAdjuntoField = "cr07a_adjunto";
    private const string CuentaCobroAdjuntoNameField = "cr07a_adjunto_name";
    private const string CuentaCobroImpresaField = "cr07a_impresa";
    private static readonly CultureInfo CuentaCobroCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] CuentaCobroPeriodFieldCandidates =
    {
        "cr07a_periodo",
        "cr07a_fecha",
        "cr07a_fechadecuenta",
        "cr07a_fechacuenta",
        "cr07a_fechadocumento",
        "cr07a_fecharegistro"
    };
    private static readonly HashSet<string> CuentaCobroAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png",
        ".doc",
        ".docx"
    };
    private readonly ConcurrentDictionary<string, CuentaCobroMetadata> _cuentasCobroMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<CuentaCobroBoardDto> GetCuentasCobroBoardAsync(int year, int month, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        var items = await LoadCuentaCobroEntitiesAsync(metadata, httpContext.User, ct);
        var rows = items
            .Select(item => BuildCuentaCobroRowDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.PeriodYear)
            .ThenByDescending(item => item.PeriodMonth)
            .ThenBy(item => item.Receptor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.NitOCedula, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = ResolveCuentaCobroNow();
        var availableYears = rows
            .Select(item => item.PeriodYear)
            .Where(value => value > 0)
            .Distinct()
            .OrderByDescending(value => value)
            .ToList();

        if (availableYears.Count == 0)
            availableYears.Add(now.Year);

        var selectedYear = year > 0 ? year : availableYears[0];
        if (!availableYears.Contains(selectedYear))
        {
            availableYears.Add(selectedYear);
            availableYears = availableYears
                .Distinct()
                .OrderByDescending(value => value)
                .ToList();
        }

        var resolvedMonth = ResolveCuentaCobroSelectedMonth(rows, selectedYear, month, now.Month);
        var availableMonths = Enumerable.Range(1, 12)
            .Select(value => new CuentaCobroMonthOptionDto
            {
                Value = value,
                Label = BuildCuentaCobroMonthLabel(value),
                Count = rows.Count(item => item.PeriodYear == selectedYear && item.PeriodMonth == value)
            })
            .ToList();

        var filteredRows = rows
            .Where(item => item.PeriodYear == selectedYear && item.PeriodMonth == resolvedMonth)
            .ToList();

        return new CuentaCobroBoardDto
        {
            SelectedYear = selectedYear,
            SelectedMonth = resolvedMonth,
            SelectedPeriodLabel = BuildCuentaCobroPeriodLabel(selectedYear, resolvedMonth),
            AvailableYears = availableYears,
            AvailableMonths = availableMonths,
            Records = filteredRows,
            TotalCount = filteredRows.Count,
            TotalValorTotal = RoundCurrency(filteredRows.Sum(item => item.ValorTotal)),
            TotalValorPago = RoundCurrency(filteredRows.Sum(item => item.ValorPago)),
            TotalReteFuenteValor = RoundCurrency(filteredRows.Sum(item => item.ReteFuenteValor)),
            Message = filteredRows.Count == 0
                ? $"No hay cuentas de cobro registradas para {BuildCuentaCobroPeriodLabel(selectedYear, resolvedMonth).ToLowerInvariant()}."
                : $"Se cargaron {filteredRows.Count} cuenta(s) de cobro para {BuildCuentaCobroPeriodLabel(selectedYear, resolvedMonth).ToLowerInvariant()}.",
            PeriodSourceLabel = metadata.PeriodSourceLabel
        };
    }

    public async Task<CuentaCobroSaveResultDto> SaveCuentaCobroAsync(CuentaCobroSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        var normalized = NormalizeCuentaCobroWriteModel(request);
        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.BaseMetadata.PrimaryNameField] = BuildCuentaCobroPrimaryName(normalized.Year, normalized.Month, normalized.Receptor),
            [CuentaCobroReceptorField] = normalized.Receptor,
            [CuentaCobroNitField] = normalized.NitOCedula,
            [CuentaCobroObservacionesField] = normalized.Observaciones,
            [CuentaCobroValorTotalField] = normalized.ValorTotal,
            [CuentaCobroReteFuentePorcentajeField] = normalized.ReteFuentePorcentaje,
            [CuentaCobroValorPagoField] = normalized.ValorPago,
            [CuentaCobroReteFuenteValorField] = normalized.ReteFuenteValor
        };

        ApplyCuentaCobroPeriodPayload(payload, metadata, normalized.Year, normalized.Month);

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})";

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            isCreate ? "POST" : "PATCH",
            httpContext.User,
            ct,
            content,
            AddRhReturnRepresentationHeaders);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var recordId = isCreate
            ? ExtractRhRecordId(response, body, metadata.BaseMetadata.PrimaryIdField)
            : normalizedRecordId;

        var record = await ResolveCuentaCobroSavedRecordAsync(metadata, response, body, recordId, httpContext.User, ct);
        return new CuentaCobroSaveResultDto
        {
            Message = isCreate
                ? "Cuenta de cobro creada correctamente."
                : "Cuenta de cobro actualizada correctamente.",
            Record = record
        };
    }

    public async Task<CuentaCobroFileUploadResultDto> UploadCuentaCobroAttachmentAsync(
        string recordId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var safeFileName = SanitizeRhFileName(fileName, "cuenta-de-cobro");
        ValidateCuentaCobroUpload(safeFileName, content);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{CuentaCobroAdjuntoField}/$value";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            httpContext.User,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("x-ms-file-name", safeFileName);
                request.Headers.TryAddWithoutValidation("If-Match", "*");
            });

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var record = await GetCuentaCobroRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        return new CuentaCobroFileUploadResultDto
        {
            Message = "Adjunto cargado correctamente.",
            Record = record
        };
    }

    public async Task<CuentaCobroFileDownloadResult?> DownloadCuentaCobroAttachmentAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({normalizedRecordId})/{CuentaCobroAdjuntoField}/$value";

        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new CuentaCobroFileDownloadResult
        {
            FileName = ResolveCuentaCobroDownloadFileName(response, normalizedRecordId),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
        };
    }

    public async Task<CuentaCobroPrintResultDto> MarkCuentaCobroAsPrintedAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var currentRecord = await GetCuentaCobroRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        if (!currentRecord.Impresa)
            await UpdateCuentaCobroPrintedFlagAsync(metadata, normalizedRecordId, httpContext.User, ct);

        var refreshedRecord = await GetCuentaCobroRecordCoreAsync(metadata, normalizedRecordId, httpContext.User, ct);
        return new CuentaCobroPrintResultDto
        {
            Message = refreshedRecord.Impresa
                ? "La cuenta de cobro quedo marcada como impresa."
                : "La cuenta de cobro no se pudo marcar como impresa.",
            Record = refreshedRecord
        };
    }

    public async Task<CuentaCobroRowDto> GetCuentaCobroByIdAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveCuentaCobroMetadataAsync(httpContext.User, ct);
        return await GetCuentaCobroRecordCoreAsync(metadata, NormalizeGuid(recordId, nameof(recordId)), httpContext.User, ct);
    }

    private async Task<CuentaCobroMetadata> ResolveCuentaCobroMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        const string cacheKey = CuentaCobroLogicalName;
        if (_cuentasCobroMetadataCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var baseMetadata = await ResolveRhEntityMetadataAsync(
            CuentaCobroLogicalName,
            CuentaCobroFallbackEntitySetName,
            CuentaCobroFallbackIdField,
            CuentaCobroFallbackPrimaryNameField,
            user,
            ct);

        var resolved = new CuentaCobroMetadata
        {
            BaseMetadata = baseMetadata,
            PeriodField = CuentaCobroPeriodFallbackField,
            UsesExplicitPeriodField = false,
            PeriodSourceLabel = "Mes de creacion",
            PrintedValueMode = CuentaCobroPrintedValueMode.Unknown
        };

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(CuentaCobroLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=Attributes($select=LogicalName,AttributeType,AttributeTypeName)";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Attributes", out var attributes)
                && attributes.ValueKind == JsonValueKind.Array)
            {
                var periodField = ResolveCuentaCobroPeriodField(attributes);
                if (!string.IsNullOrWhiteSpace(periodField)
                    && !string.Equals(periodField, CuentaCobroPeriodFallbackField, StringComparison.OrdinalIgnoreCase))
                {
                    resolved.PeriodField = periodField;
                    resolved.UsesExplicitPeriodField = true;
                    resolved.PeriodSourceLabel = "Periodo del documento";
                }

                resolved.PrintedValueMode = ResolveCuentaCobroPrintedValueMode(attributes);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver la metadata ampliada de cuentas de cobro. Se usaran valores de respaldo.");
        }

        _cuentasCobroMetadataCache[cacheKey] = resolved;
        return resolved;
    }

    private async Task<List<JsonElement>> LoadCuentaCobroEntitiesAsync(CuentaCobroMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var select = BuildCuentaCobroSelectClause(metadata);
        var orderField = string.Equals(metadata.PeriodField, CuentaCobroPeriodFallbackField, StringComparison.OrdinalIgnoreCase)
            ? CuentaCobroPeriodFallbackField
            : metadata.PeriodField;
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$orderby={orderField} desc";
        return await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
    }

    private static string BuildCuentaCobroSelectClause(CuentaCobroMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                CuentaCobroReceptorField,
                CuentaCobroNitField,
                CuentaCobroObservacionesField,
                CuentaCobroValorTotalField,
                CuentaCobroReteFuentePorcentajeField,
                CuentaCobroValorPagoField,
                CuentaCobroReteFuenteValorField,
                CuentaCobroAdjuntoField,
                CuentaCobroAdjuntoNameField,
                CuentaCobroImpresaField,
                CuentaCobroPeriodFallbackField,
                CuentaCobroModifiedOnField,
                metadata.PeriodField
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CuentaCobroRowDto? BuildCuentaCobroRowDto(CuentaCobroMetadata metadata, JsonElement item)
    {
        var recordId = ReadString(item, metadata.BaseMetadata.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var valorTotal = RoundCurrency(ReadDecimal(item, CuentaCobroValorTotalField) ?? 0m);
        var reteFuentePorcentaje = RoundCurrency(ReadDecimal(item, CuentaCobroReteFuentePorcentajeField) ?? 0m);
        var valorPago = RoundCurrency(ReadDecimal(item, CuentaCobroValorPagoField) ?? 0m);
        var reteFuenteValor = CalculateCuentaCobroReteFuenteValue(valorTotal, reteFuentePorcentaje);
        var periodDate = ResolveCuentaCobroPeriodDate(item, metadata) ?? ResolveCuentaCobroDate(item, CuentaCobroPeriodFallbackField) ?? ResolveCuentaCobroNow();
        var createdOn = ResolveCuentaCobroDate(item, CuentaCobroPeriodFallbackField) ?? periodDate;
        var modifiedOn = ResolveCuentaCobroDate(item, CuentaCobroModifiedOnField);
        var attachmentRaw = ReadString(item, CuentaCobroAdjuntoField);
        var attachmentFileName = ReadString(item, CuentaCobroAdjuntoNameField);
        var hasAdjunto = !string.IsNullOrWhiteSpace(attachmentRaw) || !string.IsNullOrWhiteSpace(attachmentFileName);

        return new CuentaCobroRowDto
        {
            RecordId = recordId,
            Receptor = ReadString(item, CuentaCobroReceptorField),
            NitOCedula = ReadString(item, CuentaCobroNitField),
            Observaciones = ReadString(item, CuentaCobroObservacionesField),
            ValorTotal = valorTotal,
            ReteFuentePorcentaje = reteFuentePorcentaje,
            ValorPago = valorPago,
            ReteFuenteValor = reteFuenteValor,
            TotalesCuadran = CuentaCobroTotalsMatch(valorTotal, valorPago, reteFuenteValor),
            Impresa = ReadCuentaCobroPrinted(item),
            HasAdjunto = hasAdjunto,
            AdjuntoFileName = hasAdjunto ? FirstNonEmpty(attachmentFileName, "Adjunto cargado") : "",
            PeriodYear = periodDate.Year,
            PeriodMonth = periodDate.Month,
            PeriodLabel = BuildCuentaCobroPeriodLabel(periodDate.Year, periodDate.Month),
            CreatedOnValue = createdOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CreatedOnDisplay = createdOn.ToString("dd/MM/yyyy", CuentaCobroCulture),
            ModifiedOnDisplay = modifiedOn?.ToString("dd/MM/yyyy", CuentaCobroCulture) ?? ""
        };
    }

    private async Task<CuentaCobroRowDto> ResolveCuentaCobroSavedRecordAsync(
        CuentaCobroMetadata metadata,
        HttpResponseMessage response,
        string body,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inlineRecord = BuildCuentaCobroRowDto(metadata, doc.RootElement);
            if (inlineRecord is not null)
                return inlineRecord;
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar la cuenta de cobro guardada.");

        return await GetCuentaCobroRecordCoreAsync(metadata, recordId, user, ct);
    }

    private async Task<CuentaCobroRowDto> GetCuentaCobroRecordCoreAsync(
        CuentaCobroMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = BuildCuentaCobroSelectClause(metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildCuentaCobroRowDto(metadata, doc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir la cuenta de cobro guardada.");
    }

    private async Task UpdateCuentaCobroPrintedFlagAsync(
        CuentaCobroMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var updateUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}({recordId})";
        var valuesToTry = metadata.PrintedValueMode switch
        {
            CuentaCobroPrintedValueMode.Boolean => new object?[] { true },
            CuentaCobroPrintedValueMode.Numeric => new object?[] { 1 },
            _ => new object?[] { true, 1, "true", "1" }
        };

        Exception? lastError = null;
        foreach (var candidate in valuesToTry)
        {
            try
            {
                await CallDataverseSendAsync(
                    updateUrl,
                    "PATCH",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CuentaCobroImpresaField] = candidate
                    },
                    user,
                    ct);
                return;
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("No fue posible actualizar el estado de impresion de la cuenta de cobro.", lastError);
    }

    private static CuentaCobroWriteModel NormalizeCuentaCobroWriteModel(CuentaCobroSaveRequest request)
    {
        var now = ResolveCuentaCobroNow();
        var year = request.Year is >= 2000 and <= 2100 ? request.Year : now.Year;
        var month = request.Month is >= 1 and <= 12 ? request.Month : now.Month;
        var receptor = request.Receptor?.Trim() ?? "";
        var nitOCedula = request.NitOCedula?.Trim() ?? "";
        var observaciones = request.Observaciones?.Trim() ?? "";
        var valorTotal = RoundCurrency(request.ValorTotal);
        var reteFuentePorcentaje = RoundCurrency(request.ReteFuentePorcentaje);
        var valorPago = RoundCurrency(request.ValorPago);

        if (string.IsNullOrWhiteSpace(receptor))
            throw new InvalidOperationException("El campo receptor es obligatorio.");

        if (string.IsNullOrWhiteSpace(nitOCedula))
            throw new InvalidOperationException("El campo NIT o cedula es obligatorio.");

        if (valorTotal <= 0m)
            throw new InvalidOperationException("El valor total debe ser mayor a cero.");

        if (reteFuentePorcentaje < 0m || reteFuentePorcentaje > 100m)
            throw new InvalidOperationException("La rete fuente % debe estar entre 0 y 100.");

        if (valorPago < 0m)
            throw new InvalidOperationException("El valor pago no puede ser negativo.");

        var reteFuenteValor = CalculateCuentaCobroReteFuenteValue(valorTotal, reteFuentePorcentaje);
        if (!CuentaCobroTotalsMatch(valorTotal, valorPago, reteFuenteValor))
            throw new InvalidOperationException("El valor total debe ser igual a valor pago + rete fuente valor.");

        return new CuentaCobroWriteModel
        {
            Year = year,
            Month = month,
            Receptor = receptor,
            NitOCedula = nitOCedula,
            Observaciones = observaciones,
            ValorTotal = valorTotal,
            ReteFuentePorcentaje = reteFuentePorcentaje,
            ValorPago = valorPago,
            ReteFuenteValor = reteFuenteValor
        };
    }

    private static decimal CalculateCuentaCobroReteFuenteValue(decimal valorTotal, decimal reteFuentePorcentaje)
    {
        return RoundCurrency(valorTotal * (reteFuentePorcentaje / 100m));
    }

    private static bool CuentaCobroTotalsMatch(decimal valorTotal, decimal valorPago, decimal reteFuenteValor)
    {
        return Math.Abs(valorTotal - (valorPago + reteFuenteValor)) <= 0.01m;
    }

    private static string BuildCuentaCobroPrimaryName(int year, int month, string receptor)
    {
        return $"Cuenta de cobro {year:D4}-{month:D2} - {FirstNonEmpty(receptor, "Sin receptor")}".Trim();
    }

    private static string BuildCuentaCobroMonthLabel(int month)
    {
        if (month < 1 || month > 12)
            return "Mes";

        return CuentaCobroCulture.DateTimeFormat.GetMonthName(month);
    }

    private static string BuildCuentaCobroPeriodLabel(int year, int month)
    {
        return $"{BuildCuentaCobroMonthLabel(month)} {year:D4}";
    }

    private static int ResolveCuentaCobroSelectedMonth(
        IReadOnlyList<CuentaCobroRowDto> rows,
        int selectedYear,
        int requestedMonth,
        int fallbackMonth)
    {
        if (requestedMonth is >= 1 and <= 12)
            return requestedMonth;

        var firstMonthWithData = rows
            .Where(item => item.PeriodYear == selectedYear)
            .Select(item => item.PeriodMonth)
            .FirstOrDefault(value => value is >= 1 and <= 12);

        return firstMonthWithData is >= 1 and <= 12 ? firstMonthWithData : fallbackMonth;
    }

    private static DateOnly? ResolveCuentaCobroPeriodDate(JsonElement item, CuentaCobroMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.PeriodField))
        {
            var explicitPeriod = ResolveCuentaCobroDate(item, metadata.PeriodField);
            if (explicitPeriod.HasValue)
                return explicitPeriod;
        }

        return ResolveCuentaCobroDate(item, CuentaCobroPeriodFallbackField);
    }

    private static DateOnly? ResolveCuentaCobroDate(JsonElement item, string logicalName)
    {
        var rawValue = ReadString(item, logicalName);
        return TryParseDateOnly(rawValue, out var parsedDate) ? parsedDate : null;
    }

    private static bool ReadCuentaCobroPrinted(JsonElement item)
    {
        var formatted = ReadString(item, $"{CuentaCobroImpresaField}{FormattedValueAnnotationSuffix}");
        if (string.Equals(formatted, "si", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "sí", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatted, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(formatted, "no", StringComparison.OrdinalIgnoreCase))
            return false;

        return ReadBool(item, CuentaCobroImpresaField);
    }

    private static string ResolveCuentaCobroPeriodField(JsonElement attributes)
    {
        foreach (var candidate in CuentaCobroPeriodFieldCandidates)
        {
            if (!TryFindCuentaCobroAttribute(attributes, candidate, out var attribute))
                continue;

            var attributeType = ReadCuentaCobroAttributeType(attribute);
            if (string.IsNullOrWhiteSpace(attributeType) || IsCuentaCobroDateAttributeType(attributeType))
                return candidate;
        }

        return CuentaCobroPeriodFallbackField;
    }

    private static CuentaCobroPrintedValueMode ResolveCuentaCobroPrintedValueMode(JsonElement attributes)
    {
        if (!TryFindCuentaCobroAttribute(attributes, CuentaCobroImpresaField, out var attribute))
            return CuentaCobroPrintedValueMode.Unknown;

        var attributeType = ReadCuentaCobroAttributeType(attribute);
        if (string.IsNullOrWhiteSpace(attributeType))
            return CuentaCobroPrintedValueMode.Unknown;

        return attributeType.Contains("boolean", StringComparison.OrdinalIgnoreCase)
            ? CuentaCobroPrintedValueMode.Boolean
            : CuentaCobroPrintedValueMode.Numeric;
    }

    private static bool TryFindCuentaCobroAttribute(JsonElement attributes, string logicalName, out JsonElement attribute)
    {
        foreach (var item in attributes.EnumerateArray())
        {
            if (string.Equals(ReadString(item, "LogicalName"), logicalName, StringComparison.OrdinalIgnoreCase))
            {
                attribute = item;
                return true;
            }
        }

        attribute = default;
        return false;
    }

    private static string ReadCuentaCobroAttributeType(JsonElement attribute)
    {
        var attributeType = ReadString(attribute, "AttributeType");
        if (!string.IsNullOrWhiteSpace(attributeType))
            return attributeType;

        if (!attribute.TryGetProperty("AttributeTypeName", out var property))
            return "";

        if (property.ValueKind == JsonValueKind.Object)
            return ReadString(property, "Value");

        return property.ToString();
    }

    private static bool IsCuentaCobroDateAttributeType(string attributeType)
    {
        return attributeType.Contains("date", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCuentaCobroPeriodPayload(
        Dictionary<string, object?> payload,
        CuentaCobroMetadata metadata,
        int year,
        int month)
    {
        if (!metadata.UsesExplicitPeriodField || string.IsNullOrWhiteSpace(metadata.PeriodField))
            return;

        payload[metadata.PeriodField] = new DateOnly(year, month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void ValidateCuentaCobroUpload(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo seleccionado esta vacio.");

        if (content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El archivo supera el limite permitido de 128 MB.");

        var extension = Path.GetExtension(fileName ?? "");
        if (string.IsNullOrWhiteSpace(extension) || !CuentaCobroAllowedExtensions.Contains(extension))
            throw new InvalidOperationException("El adjunto debe ser PDF, JPG/JPEG, PNG, DOC o DOCX.");
    }

    private static string ResolveCuentaCobroDownloadFileName(HttpResponseMessage response, string recordId)
    {
        var headerName = ReadHeaderValue(response, "x-ms-file-name");
        if (!string.IsNullOrWhiteSpace(headerName))
            return headerName.Trim();

        return $"CuentaCobro-{recordId}.bin";
    }

    private static DateOnly ResolveCuentaCobroNow()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.DateTime);
    }

    private sealed class CuentaCobroMetadata
    {
        public RhEntityMetadata BaseMetadata { get; init; } = new();
        public string PeriodField { get; set; } = CuentaCobroPeriodFallbackField;
        public bool UsesExplicitPeriodField { get; set; }
        public string PeriodSourceLabel { get; set; } = "Mes de creacion";
        public CuentaCobroPrintedValueMode PrintedValueMode { get; set; } = CuentaCobroPrintedValueMode.Unknown;
    }

    private sealed class CuentaCobroWriteModel
    {
        public int Year { get; init; }
        public int Month { get; init; }
        public string Receptor { get; init; } = "";
        public string NitOCedula { get; init; } = "";
        public string Observaciones { get; init; } = "";
        public decimal ValorTotal { get; init; }
        public decimal ReteFuentePorcentaje { get; init; }
        public decimal ValorPago { get; init; }
        public decimal ReteFuenteValor { get; init; }
    }

    private enum CuentaCobroPrintedValueMode
    {
        Unknown = 0,
        Boolean = 1,
        Numeric = 2
    }
}
