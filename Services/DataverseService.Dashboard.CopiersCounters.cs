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
        var copiersProductMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            httpContext.User,
            ct);
        var allEquipmentRows = await GetEquipmentRecordsAsync(equipmentMetadata, httpContext.User, ct);
        var allContractRows = await GetCopiersRecordsAsync(copiersProductMetadata, httpContext.User, ct);
        var clientOptions = BuildCopiersCountersClientOptions(allEquipmentRows);
        var equipmentRows = allEquipmentRows;
        var contractRows = allContractRows;
        if (!string.IsNullOrWhiteSpace(selectedClientId))
        {
            equipmentRows = equipmentRows
                .Where(row => string.Equals(
                    NormalizeOptionalGuid(row.ClientId),
                    selectedClientId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            contractRows = contractRows
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
            contractRows = contractRows
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
                    Area = equipment.Area,
                    Site = equipment.Site,
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
                    TotalConsumption = (copiesConsumption ?? 0) + (scansConsumption ?? 0),
                    HasCurrentCounter = actual.Date.HasValue
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
        var periodLabel = ToTitleCase(periodStart.ToString("MMMM yyyy", DashboardCulture));
        var assignmentRows = await TryLoadCopiersLineEquipmentAssignmentRecordsByClientsAsync(
            equipmentRows.Select(static row => row.ClientId).Concat(contractRows.Select(static row => row.ClientId)),
            httpContext.User,
            ct);
        var contractContext = BuildCopiersCountersContractContext(
            rows,
            equipmentRows,
            contractRows,
            assignmentRows,
            periodLabel);
        var clientSummaries = contractContext.ClientSummaries;
        var totalCopies = rows.Sum(static row => row.CopiesConsumption ?? 0);
        var totalScans = rows.Sum(static row => row.ScansConsumption ?? 0);

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
            CanExport = rows.Count > 0 && contractContext.ExportBlockers.Count == 0,
            RecordsCount = rows.Count,
            EmptyStateTitle = "No encontramos equipos para el filtro seleccionado.",
            EmptyStateMessage = "Cambia el cliente o el periodo para consultar otros consumos.",
            Kpis = BuildCopiersCountersKpis(rows, totalCopies, totalScans, clientSummaries.Sum(static row => row.ExcessTotal)),
            Clients = clientOptions,
            ClientSummaries = clientSummaries,
            EquipmentRows = rows,
            ExportBlockers = contractContext.ExportBlockers
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

    private CopiersCountersContractContext BuildCopiersCountersContractContext(
        IReadOnlyList<CopiersCountersEquipmentRowDto> counterRows,
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersBillingRecordRow> contractRows,
        IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow> assignmentRows,
        string periodLabel)
    {
        var blockers = new List<CopiersCountersExportBlockerDto>();
        var summaries = new List<CopiersCountersClientSummaryDto>();
        var clientRefs = BuildCopiersCountersClientRefs(equipmentRows, contractRows);

        foreach (var clientRef in clientRefs)
        {
            var analysis = BuildCopiersContractAnalysis(
                clientRef.ClientId,
                clientRef.ClientName,
                contractRows,
                equipmentRows,
                assignmentRows);
            var clientRows = counterRows
                .Where(row => CopiersCounterRowClientMatches(row, clientRef.ClientId, clientRef.ClientName))
                .ToList();

            foreach (var issue in analysis.Issues)
            {
                blockers.Add(new CopiersCountersExportBlockerDto
                {
                    Code = issue.Code,
                    ClientId = analysis.ClientId,
                    ClientName = analysis.ClientName,
                    Message = issue.Message,
                    Severity = issue.Severity
                });
            }

            ApplyCopiersCountersEquipmentContractInfo(clientRows, analysis);
            AddCopiersCountersMissingCounterBlockers(blockers, clientRows, analysis, periodLabel);

            foreach (var group in analysis.Groups)
            {
                var groupRows = clientRows
                    .Where(row => row.BillingDay == group.BillingDay)
                    .ToList();
                var summary = BuildCopiersCountersGroupSummary(group, groupRows, blockers, periodLabel);
                summaries.Add(summary);
            }

            if (analysis.Groups.Count == 0 && clientRows.Count > 0)
            {
                var totalCopies = clientRows.Sum(static row => row.CopiesConsumption ?? 0);
                var totalScans = clientRows.Sum(static row => row.ScansConsumption ?? 0);
                summaries.Add(new CopiersCountersClientSummaryDto
                {
                    GroupId = BuildCopiersContractGroupId(analysis.ClientId, analysis.ClientName, 0),
                    ClientId = analysis.ClientId,
                    ClientName = analysis.ClientName,
                    BillingDayDisplay = "Sin contrato",
                    TotalCopies = totalCopies,
                    TotalScans = totalScans,
                    TotalConsumption = totalCopies + totalScans,
                    EquipmentWithConsumption = clientRows.Count(static row => row.TotalConsumption > 0),
                    ValidationSummary = "Sin lineas contratadas"
                });
            }
        }

        return new CopiersCountersContractContext
        {
            ClientSummaries = summaries
                .OrderBy(static row => row.BillingDay is >= 1 and <= 31 ? row.BillingDay : int.MaxValue)
                .ThenBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ExportBlockers = blockers
                .Where(static blocker => !string.IsNullOrWhiteSpace(blocker.Message))
                .GroupBy(static blocker => $"{blocker.Code}|{blocker.ClientId}|{blocker.BillingDay}|{blocker.EquipmentId}|{blocker.Message}", StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static blocker => blocker.ClientName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static blocker => blocker.BillingDay is >= 1 and <= 31 ? blocker.BillingDay : int.MaxValue)
                .ThenBy(static blocker => blocker.Message, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IReadOnlyList<CopiersCountersClientRef> BuildCopiersCountersClientRefs(
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersBillingRecordRow> contractRows)
    {
        return equipmentRows
            .Select(row => new CopiersCountersClientRef(
                NormalizeOptionalGuid(row.ClientId),
                FirstNonEmpty(row.ClientName, "Sin cliente")))
            .Concat(contractRows.Select(row => new CopiersCountersClientRef(
                NormalizeOptionalGuid(row.ClientId),
                FirstNonEmpty(row.ClientName, "Sin cliente"))))
            .Where(static row => !string.IsNullOrWhiteSpace(row.ClientId) || !string.IsNullOrWhiteSpace(row.ClientName))
            .GroupBy(static row => BuildDashboardGroupKey(row.ClientId, row.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new CopiersCountersClientRef(
                group.Select(static row => row.ClientId).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "",
                group.Select(static row => row.ClientName).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "Sin cliente"))
            .ToList();
    }

    private static bool CopiersCounterRowClientMatches(CopiersCountersEquipmentRowDto row, string clientId, string clientName)
    {
        var rowClientId = NormalizeOptionalGuid(row.ClientId);
        var normalizedClientId = NormalizeOptionalGuid(clientId);
        if (!string.IsNullOrWhiteSpace(normalizedClientId)
            && !string.IsNullOrWhiteSpace(rowClientId)
            && string.Equals(rowClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rowClientName = NormalizeCopiersComparableValue(row.ClientName);
        var normalizedClientName = NormalizeCopiersComparableValue(clientName);
        return !string.IsNullOrWhiteSpace(rowClientName)
            && !string.IsNullOrWhiteSpace(normalizedClientName)
            && string.Equals(rowClientName, normalizedClientName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCopiersCountersEquipmentContractInfo(
        IReadOnlyList<CopiersCountersEquipmentRowDto> rows,
        CopiersContractAnalysis analysis)
    {
        var lineByEquipment = analysis.ContractLines
            .SelectMany(line => line.AssignedEquipmentIds.Select(equipmentId => new { equipmentId, line }))
            .GroupBy(item => item.equipmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().line, StringComparer.OrdinalIgnoreCase);
        var groupByLineId = analysis.Groups
            .SelectMany(group => group.Lines.Select(line => new { line.LineId, group }))
            .GroupBy(item => item.LineId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().group, StringComparer.OrdinalIgnoreCase);
        var fallbackGroup = analysis.Groups.Count == 1 ? analysis.Groups[0] : null;

        foreach (var row in rows)
        {
            if (analysis.BackupEquipmentIds.Contains(row.EquipmentId))
            {
                row.IsBackup = true;
                row.AssignmentStatus = "Backup";
                ApplyCopiersCountersGroupToEquipmentRow(row, fallbackGroup);
                continue;
            }

            if (lineByEquipment.TryGetValue(row.EquipmentId, out var line))
            {
                row.ProductLineId = line.LineId;
                row.ProductLineName = line.ProductName;
                row.AssignmentStatus = "Linea contratada";
                row.IncludedOperations = line.IncludedOperations;
                row.UnitExcessCost = line.AdditionalOperation;
                ApplyCopiersCountersGroupToEquipmentRow(
                    row,
                    groupByLineId.TryGetValue(line.LineId, out var group) ? group : fallbackGroup);
                continue;
            }

            row.AssignmentStatus = "Sin clasificar";
            ApplyCopiersCountersGroupToEquipmentRow(row, fallbackGroup);
        }
    }

    private static void ApplyCopiersCountersGroupToEquipmentRow(
        CopiersCountersEquipmentRowDto row,
        CopiersContractGroupAnalysis? group)
    {
        if (group is null)
            return;

        row.BillingDay = group.BillingDay;
        row.BillingDayDisplay = group.BillingDayDisplay;
        if (row.UnitExcessCost <= 0m)
            row.UnitExcessCost = group.UnitExcessCost;
    }

    private static void AddCopiersCountersMissingCounterBlockers(
        List<CopiersCountersExportBlockerDto> blockers,
        IReadOnlyList<CopiersCountersEquipmentRowDto> clientRows,
        CopiersContractAnalysis analysis,
        string periodLabel)
    {
        foreach (var group in analysis.Groups)
        {
            var missing = clientRows
                .Where(row => row.BillingDay == group.BillingDay)
                .Where(static row => !row.HasCurrentCounter)
                .ToList();
            if (missing.Count == 0)
                continue;

            blockers.Add(new CopiersCountersExportBlockerDto
            {
                Code = "missing-current-counter",
                ClientId = group.ClientId,
                ClientName = group.ClientName,
                BillingDay = group.BillingDay,
                BillingDayDisplay = group.BillingDayDisplay,
                Message = $"{FirstNonEmpty(group.ClientName, "Cliente")} {group.BillingDayDisplay} tiene {missing.Count.ToString("N0", DashboardCulture)} equipo(s) sin contador registrado en {periodLabel}."
            });
        }
    }

    private CopiersCountersClientSummaryDto BuildCopiersCountersGroupSummary(
        CopiersContractGroupAnalysis group,
        IReadOnlyList<CopiersCountersEquipmentRowDto> groupRows,
        List<CopiersCountersExportBlockerDto> blockers,
        string periodLabel)
    {
        var totalCopies = groupRows.Sum(static row => row.CopiesConsumption ?? 0);
        var totalScans = groupRows.Sum(static row => row.ScansConsumption ?? 0);
        var totalConsumption = totalCopies + totalScans;
        long excessQuantity;
        decimal excessTotal;

        if (group.GroupIncludedOperations)
        {
            excessQuantity = Math.Max(totalConsumption - (long)Math.Round(group.IncludedOperationsTotal, MidpointRounding.AwayFromZero), 0);
            excessTotal = RoundCurrency(excessQuantity * group.UnitExcessCost);
        }
        else
        {
            ApplyCopiersCountersPerEquipmentExcess(groupRows, group);
            excessQuantity = groupRows.Sum(static row => row.ExcessQuantity);
            excessTotal = RoundCurrency(groupRows.Sum(static row => row.ExcessTotal));
        }

        if (excessQuantity > 0 && group.UnitExcessCost <= 0m)
        {
            blockers.Add(new CopiersCountersExportBlockerDto
            {
                Code = "missing-additional-cost",
                ClientId = group.ClientId,
                ClientName = group.ClientName,
                BillingDay = group.BillingDay,
                BillingDayDisplay = group.BillingDayDisplay,
                Message = $"{FirstNonEmpty(group.ClientName, "Cliente")} {group.BillingDayDisplay} tiene excedentes en {periodLabel}, pero no tiene costo unitario de excedente configurado."
            });
        }

        return new CopiersCountersClientSummaryDto
        {
            GroupId = group.GroupId,
            ClientId = group.ClientId,
            ClientName = group.ClientName,
            BillingDay = group.BillingDay,
            BillingDayDisplay = group.BillingDayDisplay,
            TotalCopies = totalCopies,
            TotalScans = totalScans,
            TotalConsumption = totalConsumption,
            IncludedOperations = group.IncludedOperationsTotal,
            UnitExcessCost = group.UnitExcessCost,
            ExcessQuantity = excessQuantity,
            ExcessTotal = excessTotal,
            EquipmentWithConsumption = groupRows.Count(static row => row.TotalConsumption > 0),
            AssignmentModeLabel = group.AssignmentModeLabel,
            ValidationSummary = groupRows.Any(static row => string.Equals(row.AssignmentStatus, "Sin clasificar", StringComparison.OrdinalIgnoreCase))
                ? "Pendiente por clasificar"
                : "Listo"
        };
    }

    private static void ApplyCopiersCountersPerEquipmentExcess(
        IReadOnlyList<CopiersCountersEquipmentRowDto> groupRows,
        CopiersContractGroupAnalysis group)
    {
        foreach (var row in groupRows)
        {
            var included = row.IsBackup || string.IsNullOrWhiteSpace(row.ProductLineId)
                ? 0m
                : row.IncludedOperations;
            var unitCost = row.UnitExcessCost > 0m ? row.UnitExcessCost : group.UnitExcessCost;
            row.ExcessQuantity = Math.Max(row.TotalConsumption - (long)Math.Round(included, MidpointRounding.AwayFromZero), 0);
            row.ExcessTotal = RoundCurrency(row.ExcessQuantity * unitCost);
            row.UnitExcessCost = unitCost;
        }
    }

    private static IReadOnlyList<PortfolioKpiDto> BuildCopiersCountersKpis(
        IReadOnlyList<CopiersCountersEquipmentRowDto> rows,
        long totalCopies,
        long totalScans,
        decimal excessTotal)
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
            },
            new PortfolioKpiDto
            {
                Key = "counter-excess",
                Label = "Excedentes",
                Hint = "Valor total calculado por operaciones adicionales sobre lo contratado.",
                Value = excessTotal,
                ValueFormat = "currency",
                SecondaryLabel = "Equipos clasificados",
                SecondaryValue = rows.Count(static row => !string.IsNullOrWhiteSpace(row.AssignmentStatus)
                    && !string.Equals(row.AssignmentStatus, "Sin clasificar", StringComparison.OrdinalIgnoreCase)).ToString("N0", DashboardCulture)
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

    private sealed record CopiersCountersClientRef(string ClientId, string ClientName);

    private sealed class CopiersCountersContractContext
    {
        public IReadOnlyList<CopiersCountersClientSummaryDto> ClientSummaries { get; init; } =
            Array.Empty<CopiersCountersClientSummaryDto>();
        public IReadOnlyList<CopiersCountersExportBlockerDto> ExportBlockers { get; init; } =
            Array.Empty<CopiersCountersExportBlockerDto>();
    }
}
