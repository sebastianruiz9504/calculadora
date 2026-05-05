using System.Globalization;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.RebatesInversiones;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string PnlManualItemLogicalName = "cr07a_pnlmanualitem";
    private const string PnlManualItemFallbackEntitySetName = "cr07a_pnlmanualitems";
    private const string PnlManualItemFallbackIdField = "cr07a_pnlmanualitemid";
    private const string PnlManualItemFallbackPrimaryNameField = "cr07a_name";
    private const string PnlManualItemTypeField = "cr07a_tipo";
    private const string PnlManualItemDateField = "cr07a_fecha";
    private const string PnlManualItemDateFieldKind = "date-only";
    private const string PnlManualItemValueField = "cr07a_valor";
    private const string PnlManualItemCreatedOnField = "createdon";
    private const string PnlManualItemModifiedOnField = "modifiedon";
    private const string PnlManualItemRebateKey = "rebate";
    private const string PnlManualItemFinancialIncomeKey = "financial-income";
    private const int PnlManualItemRebateOption = 645250000;
    private const int PnlManualItemFinancialIncomeOption = 645250001;
    private static readonly CultureInfo PnlManualItemCulture = CultureInfo.GetCultureInfo("es-CO");

    public async Task<RebatesInversionesBoardDto> GetRebatesInversionesBoardAsync(int year, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var records = await LoadPnlManualRowsAsync(httpContext.User, ct);
        var availableYears = records
            .Where(static record => record.Date.HasValue)
            .Select(static record => record.Date!.Value.Year)
            .Append(today.Year)
            .Distinct()
            .OrderByDescending(static value => value)
            .ToList();

        var selectedYear = year is >= 2000 and <= 2100 ? year : today.Year;
        if (!availableYears.Contains(selectedYear))
        {
            availableYears.Add(selectedYear);
            availableYears = availableYears
                .Distinct()
                .OrderByDescending(static value => value)
                .ToList();
        }

        var filtered = records
            .Where(record => record.Date.HasValue && record.Date.Value.Year == selectedYear)
            .OrderByDescending(static record => record.Date)
            .ThenByDescending(static record => Math.Abs(record.Value))
            .ToList();
        var rebates = filtered
            .Where(static record => string.Equals(record.TypeKey, PnlManualItemRebateKey, StringComparison.OrdinalIgnoreCase))
            .Select(BuildRebatesInversionesRecordDto)
            .ToList();
        var financialIncome = filtered
            .Where(static record => string.Equals(record.TypeKey, PnlManualItemFinancialIncomeKey, StringComparison.OrdinalIgnoreCase))
            .Select(BuildRebatesInversionesRecordDto)
            .ToList();

        return new RebatesInversionesBoardDto
        {
            SelectedYear = selectedYear,
            AvailableYears = availableYears,
            Months = BuildRebatesInversionesMonthSummaries(selectedYear, rebates, financialIncome),
            Rebates = rebates,
            FinancialIncome = financialIncome,
            RebatesTotal = RoundCurrency(rebates.Sum(static record => record.Value)),
            FinancialIncomeTotal = RoundCurrency(financialIncome.Sum(static record => record.Value)),
            TotalCount = filtered.Count,
            Message = filtered.Count == 0
                ? $"No hay registros manuales para {selectedYear}."
                : $"Se cargaron {filtered.Count} registro(s) manuales para {selectedYear}."
        };
    }

    public async Task<RebatesInversionesSaveResultDto> SaveRebatesInversionesRecordAsync(
        RebatesInversionesSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolvePnlManualItemMetadataAsync(httpContext.User, ct);
        var normalized = NormalizeRebatesInversionesSaveRequest(request);
        var recordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(recordId);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] = BuildPnlManualPrimaryName(normalized.TypeKey, normalized.Date, normalized.Value),
            [PnlManualItemTypeField] = ResolvePnlManualItemTypeOption(normalized.TypeKey),
            [PnlManualItemDateField] = FormatPnlManualDateValue(normalized.Date),
            [PnlManualItemValueField] = normalized.Value
        };

        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";

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

        var savedId = isCreate
            ? ExtractRhRecordId(response, body, metadata.PrimaryIdField)
            : recordId;
        var record = await ResolvePnlManualSavedRecordAsync(metadata, response, body, savedId, httpContext.User, ct);

        return new RebatesInversionesSaveResultDto
        {
            Message = isCreate
                ? "Registro creado correctamente."
                : "Registro actualizado correctamente.",
            Record = BuildRebatesInversionesRecordDto(record)
        };
    }

    public async Task<RebatesInversionesDeleteResultDto> DeleteRebatesInversionesRecordAsync(
        RebatesInversionesDeleteRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar el registro que quieres eliminar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolvePnlManualItemMetadataAsync(httpContext.User, ct);
        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        await CallDataverseDeleteAsync($"/api/data/v9.2/{metadata.EntitySetName}({recordId})", httpContext.User, ct);

        return new RebatesInversionesDeleteResultDto
        {
            Message = "Registro eliminado correctamente.",
            RecordId = recordId
        };
    }

    private async Task<List<PnlManualRecord>> LoadPnlManualRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var metadata = await ResolvePnlManualItemMetadataAsync(user, ct);
            var select = BuildPnlManualItemSelect(metadata);
            var filter = BuildBillingDateFilter(PnlManualItemDateField, PnlManualItemDateFieldKind, startInclusive, endExclusive);
            var relativeUrl =
                $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={PnlManualItemDateField} asc";

            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            return items
                .Select(item => ParsePnlManualRecord(metadata, item))
                .Where(static record => record is not null)
                .Cast<PnlManualRecord>()
                .ToList();
        }
        catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "No fue posible cargar registros manuales de P&L. El tablero continuara sin Rebates/Inversiones manuales.");
            return new List<PnlManualRecord>();
        }
    }

    private async Task<List<PnlManualRecord>> LoadPnlManualRowsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolvePnlManualItemMetadataAsync(user, ct);
        var select = BuildPnlManualItemSelect(metadata);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$orderby={PnlManualItemDateField} desc";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item => ParsePnlManualRecord(metadata, item))
            .Where(static record => record is not null)
            .Cast<PnlManualRecord>()
            .ToList();
    }

    private async Task<RhEntityMetadata> ResolvePnlManualItemMetadataAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await ResolveRhEntityMetadataAsync(
            PnlManualItemLogicalName,
            PnlManualItemFallbackEntitySetName,
            PnlManualItemFallbackIdField,
            PnlManualItemFallbackPrimaryNameField,
            user,
            ct);

    private static string BuildPnlManualItemSelect(RhEntityMetadata metadata)
    {
        return string.Join(",",
            new[]
            {
                metadata.PrimaryIdField,
                metadata.PrimaryNameField,
                PnlManualItemTypeField,
                PnlManualItemDateField,
                PnlManualItemValueField,
                PnlManualItemCreatedOnField,
                PnlManualItemModifiedOnField
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private PnlManualRecord? ParsePnlManualRecord(RhEntityMetadata metadata, JsonElement item)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var optionValue = ReadInt(item, PnlManualItemTypeField);
        var date = ReadDateOnly(item, PnlManualItemDateField);
        var typeKey = ResolvePnlManualItemTypeKey(optionValue, ReadString(item, $"{PnlManualItemTypeField}{FormattedValueAnnotationSuffix}"));
        if (string.IsNullOrWhiteSpace(typeKey) || !date.HasValue)
            return null;

        return new PnlManualRecord
        {
            RecordId = recordId.Trim(),
            Name = ReadString(item, metadata.PrimaryNameField).Trim(),
            TypeKey = typeKey,
            TypeLabel = ResolvePnlManualItemTypeLabel(typeKey),
            TypeOptionValue = optionValue,
            Date = date,
            Value = RoundCurrency(ReadDecimal(item, PnlManualItemValueField) ?? 0m),
            CreatedOn = ReadPnlManualDateTime(item, PnlManualItemCreatedOnField),
            ModifiedOn = ReadPnlManualDateTime(item, PnlManualItemModifiedOnField)
        };
    }

    private async Task<PnlManualRecord> ResolvePnlManualSavedRecordAsync(
        RhEntityMetadata metadata,
        HttpResponseMessage response,
        string body,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var inline = ParsePnlManualRecord(metadata, doc.RootElement);
            if (inline is not null)
                return inline;
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar el registro guardado.");

        var select = BuildPnlManualItemSelect(metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var savedDoc = JsonDocument.Parse(json);
        return ParsePnlManualRecord(metadata, savedDoc.RootElement)
            ?? throw new InvalidOperationException("No fue posible reconstruir el registro guardado.");
    }

    private static RebatesInversionesRecordDto BuildRebatesInversionesRecordDto(PnlManualRecord record)
    {
        var date = record.Date ?? default;
        return new RebatesInversionesRecordDto
        {
            RecordId = record.RecordId,
            TypeKey = record.TypeKey,
            TypeLabel = record.TypeLabel,
            DateValue = date == default ? "" : FormatPnlManualDateValue(date),
            DateDisplay = date == default ? "-" : date.ToString("dd/MM/yyyy", PnlManualItemCulture),
            Year = date == default ? 0 : date.Year,
            Month = date == default ? 0 : date.Month,
            MonthLabel = date == default ? "Sin mes" : ResolvePnlMonthLabel(date.Year, date.Month),
            Value = record.Value,
            CreatedOnDisplay = FormatPnlManualDateTimeDisplay(record.CreatedOn),
            ModifiedOnDisplay = FormatPnlManualDateTimeDisplay(record.ModifiedOn)
        };
    }

    private static IReadOnlyList<RebatesInversionesMonthSummaryDto> BuildRebatesInversionesMonthSummaries(
        int year,
        IReadOnlyList<RebatesInversionesRecordDto> rebates,
        IReadOnlyList<RebatesInversionesRecordDto> financialIncome)
    {
        return Enumerable.Range(1, 12)
            .Select(month => new RebatesInversionesMonthSummaryDto
            {
                Month = month,
                Label = ResolvePnlMonthLabel(year, month),
                RebatesCount = rebates.Count(record => record.Month == month),
                FinancialIncomeCount = financialIncome.Count(record => record.Month == month),
                RebatesTotal = RoundCurrency(rebates.Where(record => record.Month == month).Sum(record => record.Value)),
                FinancialIncomeTotal = RoundCurrency(financialIncome.Where(record => record.Month == month).Sum(record => record.Value))
            })
            .ToList();
    }

    private static PnlManualWriteModel NormalizeRebatesInversionesSaveRequest(RebatesInversionesSaveRequest request)
    {
        var typeKey = NormalizePnlManualItemTypeKey(request.TypeKey);
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("Selecciona si el registro es rebate o ingreso financiero.");

        if (!TryParseDateOnly(request.DateValue?.Trim(), out var date))
            throw new InvalidOperationException("La fecha es obligatoria y debe ser valida.");

        var value = RoundCurrency(request.Value);
        if (Math.Abs(value) < 0.01m)
            throw new InvalidOperationException("El valor debe ser diferente de cero.");

        return new PnlManualWriteModel(typeKey, date, value);
    }

    private static string NormalizePnlManualItemTypeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant() switch
        {
            PnlManualItemRebateKey or "rebates" => PnlManualItemRebateKey,
            PnlManualItemFinancialIncomeKey or "financialincome" or "inversion" or "inversiones" => PnlManualItemFinancialIncomeKey,
            _ => ""
        };
    }

    private static int ResolvePnlManualItemTypeOption(string typeKey) => NormalizePnlManualItemTypeKey(typeKey) switch
    {
        PnlManualItemRebateKey => PnlManualItemRebateOption,
        PnlManualItemFinancialIncomeKey => PnlManualItemFinancialIncomeOption,
        _ => throw new InvalidOperationException("El tipo del registro manual no es valido.")
    };

    private static string ResolvePnlManualItemTypeKey(int optionValue, string? formattedLabel = null)
    {
        if (optionValue == PnlManualItemRebateOption)
            return PnlManualItemRebateKey;

        if (optionValue == PnlManualItemFinancialIncomeOption)
            return PnlManualItemFinancialIncomeKey;

        var normalizedLabel = NormalizePnlLabel(formattedLabel);
        if (normalizedLabel.Contains("rebate", StringComparison.Ordinal))
            return PnlManualItemRebateKey;

        if (normalizedLabel.Contains("financier", StringComparison.Ordinal)
            || normalizedLabel.Contains("inversion", StringComparison.Ordinal))
            return PnlManualItemFinancialIncomeKey;

        return "";
    }

    private static string ResolvePnlManualItemTypeLabel(string typeKey) => NormalizePnlManualItemTypeKey(typeKey) switch
    {
        PnlManualItemRebateKey => "Rebates",
        PnlManualItemFinancialIncomeKey => "Ingresos financieros",
        _ => "Registro manual"
    };

    private static string BuildPnlManualPrimaryName(string typeKey, DateOnly date, decimal value) =>
        $"{ResolvePnlManualItemTypeLabel(typeKey)} - {date:yyyy-MM-dd} - {value.ToString("C0", PnlManualItemCulture)}";

    private static string FormatPnlManualDateValue(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadPnlManualDateTime(JsonElement item, string propertyName)
    {
        var raw = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            return new DateTimeOffset(dateTime);

        return null;
    }

    private static string FormatPnlManualDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "";

        return value.Value.ToString("dd/MM/yyyy HH:mm", PnlManualItemCulture);
    }

    private sealed record PnlManualWriteModel(string TypeKey, DateOnly Date, decimal Value);

    private sealed class PnlManualRecord
    {
        public string RecordId { get; set; } = "";
        public string Name { get; set; } = "";
        public string TypeKey { get; set; } = "";
        public string TypeLabel { get; set; } = "";
        public int TypeOptionValue { get; set; }
        public DateOnly? Date { get; set; }
        public decimal Value { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? ModifiedOn { get; set; }
    }
}
