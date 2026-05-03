using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CopiersLegacyCountersTableSetName = "cr07a_contadoreses";
    private const string CopiersLegacyCountersAlternateTableSetName = "cr07a_contadores";
    private const string CopiersLegacyCountersAlternatePluralTableSetName = "cr07a_contadors";
    private const string CopiersLegacyCountersDateField = "cr07a_fechadetomadecontador";
    private const string CopiersLegacyCountersEquipmentField = "cr07a_maquina";
    private const string CopiersLegacyCountersCopiesField = "cr07a_contador";
    private const string CopiersLegacyCountersScansField = "cr07a_contadorescaner";
    private const string CopiersMonthlyCountersTableSetName = "cr07a_contadoresmensualesequipos";
    private const string CopiersMonthlyCountersDateField = "cr07a_dt_fechalectura";
    private const string CopiersMonthlyCountersEquipmentLookupField = "cr07a_equipo";
    private const string CopiersMonthlyCountersEquipmentTextField = "cr07a_equipo";
    private const string CopiersMonthlyCountersCopiesField = "cr07a_dt_contadorpaginas";
    private const string CopiersMonthlyCountersScansField = "cr07a_dt_paginasescaneadas";

    public async Task<CopiersCountersDashboardDto> GetCopiersCountersDashboardAsync(
        int year,
        int month,
        string? clientId = null,
        string? clientName = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var selectedYear = year is >= 2000 and <= 2100 ? year : today.Year;
        var selectedMonth = month is >= 1 and <= 12 ? month : today.Month;
        var selectedClientId = NormalizeOptionalGuid(clientId);
        var selectedClientName = (clientName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(selectedClientId))
            throw new InvalidOperationException("El cliente seleccionado no es valido.");

        var periodStart = new DateOnly(selectedYear, selectedMonth, 1);
        var periodEnd = periodStart.AddMonths(1);
        var previousPeriodStart = periodStart.AddMonths(-1);

        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var allEquipmentRows = await GetEquipmentRecordsAsync(equipmentMetadata, httpContext.User, ct);
        var clientOptions = BuildCopiersCountersClientOptions(allEquipmentRows);
        var equipmentRows = allEquipmentRows;
        if (!string.IsNullOrWhiteSpace(selectedClientId))
        {
            equipmentRows = equipmentRows
                .Where(row => string.Equals(
                    NormalizeOptionalGuid(row.ClientId),
                    selectedClientId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(selectedClientName))
        {
            var comparableClientName = NormalizeCopiersComparableValue(selectedClientName);
            equipmentRows = equipmentRows
                .Where(row => NormalizeCopiersComparableValue(row.ClientName).Contains(comparableClientName, StringComparison.Ordinal))
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(selectedClientName) && !string.IsNullOrWhiteSpace(selectedClientId))
        {
            selectedClientName = equipmentRows
                .Select(static row => row.ClientName)
                .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name)) ?? "";
        }

        var semaphore = new SemaphoreSlim(8);
        var tasks = equipmentRows.Select(async equipment =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var actual = await GetCopiersLastCounterReadingAsync(
                    equipment.RecordId,
                    equipment.Serial,
                    periodStart,
                    periodEnd,
                    httpContext.User,
                    ct);
                var previous = await GetCopiersLastCounterReadingAsync(
                    equipment.RecordId,
                    equipment.Serial,
                    previousPeriodStart,
                    periodStart,
                    httpContext.User,
                    ct);

                var copiesConsumption = CalculateCopiersCounterDelta(actual.Copies, previous.Copies);
                var scansConsumption = CalculateCopiersCounterDelta(actual.Scans, previous.Scans);
                int? daysBetweenReadings = null;
                if (actual.Date.HasValue && previous.Date.HasValue)
                {
                    daysBetweenReadings = Math.Abs((actual.Date.Value.Date - previous.Date.Value.Date).Days);
                }

                var normalizedClientId = NormalizeOptionalGuid(equipment.ClientId);
                var clientName = FirstNonEmpty(
                    equipment.InStock ? "Sin cliente" : equipment.ClientName,
                    "Sin cliente");

                return new CopiersCountersEquipmentRowDto
                {
                    EquipmentId = equipment.RecordId,
                    EquipmentName = equipment.Serial,
                    ClientId = normalizedClientId,
                    ClientName = clientName,
                    CurrentDateValue = FormatCopiersCounterDateValue(actual.Date),
                    CurrentDateDisplay = FormatCopiersCounterDateDisplay(actual.Date),
                    PreviousDateValue = FormatCopiersCounterDateValue(previous.Date),
                    PreviousDateDisplay = FormatCopiersCounterDateDisplay(previous.Date),
                    CurrentCopiesCounter = actual.Copies,
                    PreviousCopiesCounter = previous.Copies,
                    CopiesConsumption = copiesConsumption,
                    CurrentScansCounter = actual.Scans,
                    PreviousScansCounter = previous.Scans,
                    ScansConsumption = scansConsumption,
                    DaysBetweenReadings = daysBetweenReadings,
                    TotalConsumption = (copiesConsumption ?? 0) + (scansConsumption ?? 0)
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var rows = (await Task.WhenAll(tasks))
            .OrderBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.EquipmentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clientSummaries = BuildCopiersCountersClientSummaries(rows);
        var totalCopies = rows.Sum(static row => row.CopiesConsumption ?? 0);
        var totalScans = rows.Sum(static row => row.ScansConsumption ?? 0);
        var periodLabel = ToTitleCase(periodStart.ToString("MMMM yyyy", DashboardCulture));

        return new CopiersCountersDashboardDto
        {
            Year = selectedYear,
            Month = selectedMonth,
            PeriodValue = periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PeriodLabel = periodLabel,
            DateRangeLabel = $"{periodStart:dd/MM/yyyy} - {periodEnd.AddDays(-1):dd/MM/yyyy}",
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = $"Consumo mensual de copias y escaneos - {periodLabel}",
            SelectedClientId = selectedClientId,
            SelectedClientName = selectedClientName,
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            EmptyStateTitle = "No encontramos equipos para el filtro seleccionado.",
            EmptyStateMessage = "Cambia el cliente o el periodo para consultar otros consumos.",
            Kpis = BuildCopiersCountersKpis(rows, totalCopies, totalScans),
            Clients = clientOptions,
            ClientSummaries = clientSummaries,
            EquipmentRows = rows
        };
    }

    private async Task<CopiersCounterReading> GetCopiersLastCounterReadingAsync(
        string equipmentId,
        string? serial,
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedEquipmentId = NormalizeOptionalGuid(equipmentId);
        var serialText = serial?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedEquipmentId) && string.IsNullOrWhiteSpace(serialText))
            return CopiersCounterReading.Empty;

        var startText = FormatCopiersCounterFilterDate(startInclusive);
        var endText = FormatCopiersCounterFilterDate(endExclusive);
        var queryOptions = new List<CopiersCounterQueryOption>();

        if (!string.IsNullOrWhiteSpace(normalizedEquipmentId))
        {
            queryOptions.AddRange(new[]
            {
                BuildLegacyCopiersCounterQueryOption(
                    CopiersLegacyCountersTableSetName,
                    normalizedEquipmentId,
                    startText,
                    endText),
                BuildLegacyCopiersCounterQueryOption(
                    CopiersLegacyCountersAlternateTableSetName,
                    normalizedEquipmentId,
                    startText,
                    endText),
                BuildLegacyCopiersCounterQueryOption(
                    CopiersLegacyCountersAlternatePluralTableSetName,
                    normalizedEquipmentId,
                    startText,
                    endText),
                BuildMonthlyCopiersCounterQueryOptionByLookup(
                    normalizedEquipmentId,
                    startText,
                    endText)
            });
        }

        if (!string.IsNullOrWhiteSpace(serialText))
        {
            queryOptions.Add(BuildMonthlyCopiersCounterQueryOptionBySerial(
                serialText,
                startText,
                endText));
        }

        foreach (var option in queryOptions)
        {
            try
            {
                var relativeUrl =
                    $"/api/data/v9.2/{option.EntitySetName}" +
                    $"?$select={option.Select}" +
                    $"&$filter={Uri.EscapeDataString(option.Filter)}" +
                    $"&$orderby={Uri.EscapeDataString(option.OrderBy)}" +
                    "&$top=1";
                var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
                var first = items.FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Undefined)
                    continue;

                return new CopiersCounterReading(
                    ReadCopiersCounterDate(first, option.DateField),
                    ReadCopiersCounterLong(first, option.CopiesField),
                    ReadCopiersCounterLong(first, option.ScansField));
            }
            catch (InvalidOperationException ex) when (ShouldTryNextCopiersCounterSource(ex))
            {
                _logger.LogDebug(
                    ex,
                    "No fue posible leer contadores desde {EntitySetName}. Se intentara el siguiente origen.",
                    option.EntitySetName);
            }
        }

        return CopiersCounterReading.Empty;
    }

    private static CopiersCounterQueryOption BuildLegacyCopiersCounterQueryOption(
        string entitySetName,
        string equipmentId,
        string startText,
        string endText)
    {
        var equipmentLookup = BuildDashboardLookupValuePropertyName(CopiersLegacyCountersEquipmentField);
        return new CopiersCounterQueryOption(
            entitySetName,
            string.Join(",", new[]
            {
                CopiersLegacyCountersCopiesField,
                CopiersLegacyCountersScansField,
                CopiersLegacyCountersDateField,
                equipmentLookup
            }),
            $"{equipmentLookup} eq {equipmentId} and {CopiersLegacyCountersDateField} ge {startText} and {CopiersLegacyCountersDateField} lt {endText}",
            $"{CopiersLegacyCountersDateField} desc",
            CopiersLegacyCountersDateField,
            CopiersLegacyCountersCopiesField,
            CopiersLegacyCountersScansField);
    }

    private static CopiersCounterQueryOption BuildMonthlyCopiersCounterQueryOptionByLookup(
        string equipmentId,
        string startText,
        string endText)
    {
        var equipmentLookup = BuildDashboardLookupValuePropertyName(CopiersMonthlyCountersEquipmentLookupField);
        return new CopiersCounterQueryOption(
            CopiersMonthlyCountersTableSetName,
            string.Join(",", new[]
            {
                CopiersMonthlyCountersCopiesField,
                CopiersMonthlyCountersScansField,
                CopiersMonthlyCountersDateField,
                equipmentLookup,
                CopiersMonthlyCountersEquipmentTextField
            }),
            $"{equipmentLookup} eq {equipmentId} and {CopiersMonthlyCountersDateField} ge {startText} and {CopiersMonthlyCountersDateField} lt {endText}",
            $"{CopiersMonthlyCountersDateField} desc",
            CopiersMonthlyCountersDateField,
            CopiersMonthlyCountersCopiesField,
            CopiersMonthlyCountersScansField);
    }

    private static CopiersCounterQueryOption BuildMonthlyCopiersCounterQueryOptionBySerial(
        string serial,
        string startText,
        string endText)
    {
        var safeSerial = EscapeOdataLiteral(serial);
        return new CopiersCounterQueryOption(
            CopiersMonthlyCountersTableSetName,
            string.Join(",", new[]
            {
                CopiersMonthlyCountersCopiesField,
                CopiersMonthlyCountersScansField,
                CopiersMonthlyCountersDateField,
                BuildDashboardLookupValuePropertyName(CopiersMonthlyCountersEquipmentLookupField),
                CopiersMonthlyCountersEquipmentTextField
            }),
            $"{CopiersMonthlyCountersEquipmentTextField} eq '{safeSerial}' and {CopiersMonthlyCountersDateField} ge {startText} and {CopiersMonthlyCountersDateField} lt {endText}",
            $"{CopiersMonthlyCountersDateField} desc",
            CopiersMonthlyCountersDateField,
            CopiersMonthlyCountersCopiesField,
            CopiersMonthlyCountersScansField);
    }

    private static IReadOnlyList<CopiersCountersClientOptionDto> BuildCopiersCountersClientOptions(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows)
    {
        return equipmentRows
            .Where(static row => !string.IsNullOrWhiteSpace(row.ClientId))
            .GroupBy(static row => NormalizeOptionalGuid(row.ClientId), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new CopiersCountersClientOptionDto
            {
                Id = group.Key,
                Name = group
                    .Select(static row => row.ClientName?.Trim() ?? "")
                    .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name)) ?? ""
            })
            .Where(static client => !string.IsNullOrWhiteSpace(client.Id))
            .OrderBy(static client => client.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CopiersCountersClientSummaryDto> BuildCopiersCountersClientSummaries(
        IReadOnlyList<CopiersCountersEquipmentRowDto> rows)
    {
        return rows
            .GroupBy(row => new
            {
                row.ClientId,
                ClientName = FirstNonEmpty(row.ClientName, "Sin cliente")
            })
            .Select(group =>
            {
                var totalCopies = group.Sum(static row => row.CopiesConsumption ?? 0);
                var totalScans = group.Sum(static row => row.ScansConsumption ?? 0);
                return new CopiersCountersClientSummaryDto
                {
                    ClientId = group.Key.ClientId,
                    ClientName = group.Key.ClientName,
                    TotalCopies = totalCopies,
                    TotalScans = totalScans,
                    TotalConsumption = totalCopies + totalScans,
                    EquipmentWithConsumption = group.Count(static row =>
                        (row.CopiesConsumption ?? 0) > 0 || (row.ScansConsumption ?? 0) > 0)
                };
            })
            .OrderBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PortfolioKpiDto> BuildCopiersCountersKpis(
        IReadOnlyList<CopiersCountersEquipmentRowDto> rows,
        long totalCopies,
        long totalScans)
    {
        return new[]
        {
            new PortfolioKpiDto
            {
                Key = "counter-copies",
                Label = "Copias del mes",
                Hint = "Diferencia entre la ultima lectura del mes y la lectura anterior.",
                Value = totalCopies,
                ValueFormat = "number",
                SecondaryLabel = "Equipos con copias",
                SecondaryValue = rows.Count(static row => (row.CopiesConsumption ?? 0) > 0).ToString("N0", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "counter-scans",
                Label = "Escaneos del mes",
                Hint = "Diferencia entre la ultima lectura del mes y la lectura anterior.",
                Value = totalScans,
                ValueFormat = "number",
                SecondaryLabel = "Equipos con escaneos",
                SecondaryValue = rows.Count(static row => (row.ScansConsumption ?? 0) > 0).ToString("N0", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "counter-total",
                Label = "Consumo total",
                Hint = "Copias y escaneos consolidados del periodo.",
                Value = totalCopies + totalScans,
                ValueFormat = "number",
                SecondaryLabel = "Equipos consultados",
                SecondaryValue = rows.Count.ToString("N0", DashboardCulture)
            }
        };
    }

    private static long? CalculateCopiersCounterDelta(long? current, long? previous)
    {
        if (!current.HasValue || !previous.HasValue)
            return null;

        var delta = current.Value - previous.Value;
        return delta < 0 ? null : delta;
    }

    private static string FormatCopiersCounterFilterDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string FormatCopiersCounterDateValue(DateTime? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static string FormatCopiersCounterDateDisplay(DateTime? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

    private static bool ShouldTryNextCopiersCounterSource(InvalidOperationException exception)
    {
        return exception.Message.Contains("Resource not found for the segment", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ReadCopiersCounterDate(JsonElement item, string propertyName)
    {
        var raw = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.UtcDateTime;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            return date;

        return null;
    }

    private static long? ReadCopiersCounterLong(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (property.TryGetInt64(out var longValue))
                return longValue;

            if (property.TryGetDecimal(out var decimalValue))
                return (long)decimalValue;
        }

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
            return parsedLong;

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal))
            return (long)parsedDecimal;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digitsValue)
            ? digitsValue
            : null;
    }

    private sealed record CopiersCounterQueryOption(
        string EntitySetName,
        string Select,
        string Filter,
        string OrderBy,
        string DateField,
        string CopiesField,
        string ScansField);

    private sealed record CopiersCounterReading(DateTime? Date, long? Copies, long? Scans)
    {
        public static CopiersCounterReading Empty { get; } = new(null, null, null);
    }
}
