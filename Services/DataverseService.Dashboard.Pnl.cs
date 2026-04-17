using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DashboardPnlVerticalAll = "all";
    private const string DashboardPnlVerticalCloud = "cloud";
    private const string DashboardPnlVerticalCopiers = "copiers";

    private const string DashboardExpenseCategoryField = "cr07a_categoria";
    private const string DashboardExpenseIssuerNameField = "cr07a_nombreemisor";
    private const string DashboardExpenseTotalField = "cr07a_total";
    private const string DashboardExpenseVatField = "cr07a_iva";
    private const string DashboardExpenseTotalBeforeVatField = "cr07a_totalantesdeiva";

    private const int PnlExpensePersonalCloudOption = 645250000;
    private const int PnlExpensePersonalCopiersOption = 645250001;
    private const int PnlExpensePersonalAdministrativeOption = 645250002;
    private const int PnlExpenseTransportOption = 645250003;
    private const int PnlExpenseTravelOption = 645250004;
    private const int PnlExpenseMarketingOption = 645250005;
    private const int PnlExpenseInternalOption = 645250006;
    private const int PnlExpenseTaxesOption = 645250007;
    private const int PnlExpenseMachinesOption = 645250008;
    private const int PnlExpenseSuppliesOption = 645250009;
    private const int PnlExpenseLicensingOption = 645250010;
    private const int PnlExpenseRecurringOption = 645250011;
    private const int PnlExpenseFinancialOption = 645250012;
    private const int PnlExpenseWarehouseOption = 645250013;
    private const int PnlExpenseEquipmentOption = 645250014;
    private const int PnlExpenseTechnicalServiceOption = 645250015;
    private const int PnlExpenseOfficeRentOption = 645250016;

    public async Task<PnlDashboardDto> GetPnlDashboardAsync(
        int year,
        int? monthCutoff = null,
        string? vertical = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var verticalKey = NormalizePnlVerticalKey(vertical);
        var verticalLabel = ResolvePnlVerticalLabel(verticalKey);
        var yearStart = new DateOnly(resolvedYear, 1, 1);
        var yearEnd = yearStart.AddYears(1);

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var billingRecords = await GetBillingRecordsAsync(
            metadata,
            yearStart,
            yearEnd,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var expenseRecords = await GetPnlExpenseRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var latestMonthAvailable = ResolveLatestPnlMonthAvailable(
            resolvedYear,
            today,
            verticalKey,
            billingRecords,
            expenseRecords);

        var resolvedMonthCutoff = ResolvePnlMonthCutoff(latestMonthAvailable, monthCutoff);
        var periodEndExclusive = new DateOnly(resolvedYear, resolvedMonthCutoff, 1).AddMonths(1);

        var scopedBillingRecords = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff)
            .ToList();

        var scopedExpenseRecords = expenseRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value.Year == resolvedYear
                && record.PaymentDate.Value.Month <= resolvedMonthCutoff)
            .ToList();

        var months = BuildPnlMonthColumns(resolvedYear, resolvedMonthCutoff);

        var copiersRevenue = BuildPnlBillingSeries(
            resolvedMonthCutoff,
            scopedBillingRecords,
            record => GetPnlRevenueAmount(record, verticalKey, DashboardVerticalCopiersOption));

        var cloudRevenue = BuildPnlBillingSeries(
            resolvedMonthCutoff,
            scopedBillingRecords,
            record => GetPnlRevenueAmount(record, verticalKey, DashboardVerticalCloudOption));

        var operatingRevenue = SumPnlSeries(copiersRevenue, cloudRevenue);

        var licensing = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "licensing");
        var rebates = EmptyPnlSeries(resolvedMonthCutoff);
        var supplies = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "supplies");
        var machines = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "machines");
        var technicalService = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "technical-service");
        var cogs = SumPnlSeries(licensing, rebates, supplies, machines, technicalService);
        var grossProfit = SubtractPnlSeries(operatingRevenue, cogs);

        var personalAdministrative = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-administrative");
        var personalCloud = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-cloud");
        var personalCopiers = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-copiers");
        var personalSubtotal = SumPnlSeries(personalAdministrative, personalCloud, personalCopiers);

        var officeRent = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "office-rent");
        var warehouse = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "warehouse");
        var transport = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "transport");
        var internalExpenses = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "internal");
        var recurring = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "recurring");
        var equipment = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "equipment");
        var travel = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "travel");
        var empty = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "empty");
        var administrativeSubtotal = SumPnlSeries(officeRent, warehouse, transport, internalExpenses, recurring, equipment, travel, empty);

        var marketing = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "marketing");
        var commercialSubtotal = SumPnlSeries(marketing);

        var operatingExpenses = SumPnlSeries(personalSubtotal, administrativeSubtotal, commercialSubtotal);
        var ebitda = SubtractPnlSeries(grossProfit, operatingExpenses);

        var taxes = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "taxes");
        var financial = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "financial");
        var otherNonOperating = SumPnlSeries(taxes, financial);

        var recordsCount = CountPnlRelevantBillingRecords(scopedBillingRecords, verticalKey)
            + CountPnlRelevantExpenseRecords(scopedExpenseRecords, verticalKey);

        var operatingRevenueTotal = SumPnlSeriesTotal(operatingRevenue);
        var grossProfitTotal = SumPnlSeriesTotal(grossProfit);
        var ebitdaTotal = SumPnlSeriesTotal(ebitda);
        var grossMargin = operatingRevenueTotal == 0m
            ? 0m
            : RoundCurrency((grossProfitTotal / operatingRevenueTotal) * 100m);
        var ebitdaMargin = operatingRevenueTotal == 0m
            ? 0m
            : RoundCurrency((ebitdaTotal / operatingRevenueTotal) * 100m);

        return new PnlDashboardDto
        {
            Year = resolvedYear,
            VerticalKey = verticalKey,
            VerticalLabel = verticalLabel,
            LatestMonthAvailable = latestMonthAvailable,
            LatestMonthAvailableLabel = ResolvePnlMonthLabel(resolvedYear, latestMonthAvailable),
            MonthCutoff = resolvedMonthCutoff,
            MonthCutoffLabel = ResolvePnlMonthLabel(resolvedYear, resolvedMonthCutoff),
            DateRangeLabel = BuildDateRangeLabel(yearStart, periodEndExclusive),
            FocusLabel = verticalKey == DashboardPnlVerticalAll
                ? "P&L mensual consolidado"
                : $"P&L mensual {verticalLabel}",
            Description = "Estructura P&L mensual bajo NIIF con corte al ultimo mes cargado. La fila de rebates queda en cero mientras no exista una fuente manual configurada.",
            HasData = recordsCount > 0,
            RecordsCount = recordsCount,
            EmptyStateTitle = "No encontramos movimientos para construir el P&L.",
            EmptyStateMessage = "Cuando existan ingresos o costos cargados en el año seleccionado veras la matriz mensual aqui.",
            Months = months,
            Kpis = BuildPnlKpis(
                operatingRevenueTotal,
                grossProfitTotal,
                ebitdaTotal,
                grossMargin,
                ebitdaMargin,
                verticalLabel,
                resolvedMonthCutoff,
                resolvedYear),
            Rows = BuildPnlRows(
                copiersRevenue,
                cloudRevenue,
                operatingRevenue,
                licensing,
                rebates,
                supplies,
                machines,
                technicalService,
                cogs,
                grossProfit,
                personalAdministrative,
                personalCloud,
                personalCopiers,
                personalSubtotal,
                officeRent,
                warehouse,
                transport,
                internalExpenses,
                recurring,
                equipment,
                travel,
                empty,
                administrativeSubtotal,
                marketing,
                commercialSubtotal,
                ebitda,
                taxes,
                financial,
                otherNonOperating)
        };
    }

    public async Task<PnlCellDetailDto> GetPnlCellDetailAsync(
        int year,
        int? monthCutoff,
        string? vertical,
        string rowKey,
        int? cellMonth = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rowKey))
            throw new InvalidOperationException("Debes indicar la fila del P&L que quieres revisar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var verticalKey = NormalizePnlVerticalKey(vertical);
        var verticalLabel = ResolvePnlVerticalLabel(verticalKey);
        var yearStart = new DateOnly(resolvedYear, 1, 1);
        var yearEnd = yearStart.AddYears(1);
        var rowMetadata = ResolvePnlRowMetadata(rowKey);

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var billingRecords = await GetBillingRecordsAsync(
            metadata,
            yearStart,
            yearEnd,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var expenseRecords = await GetPnlExpenseRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var latestMonthAvailable = ResolveLatestPnlMonthAvailable(
            resolvedYear,
            today,
            verticalKey,
            billingRecords,
            expenseRecords);

        var resolvedMonthCutoff = ResolvePnlMonthCutoff(latestMonthAvailable, monthCutoff);
        var resolvedCellMonth = ResolvePnlCellMonth(cellMonth, resolvedMonthCutoff);

        var scopedBillingRecords = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.EmissionDate.Value.Month == resolvedCellMonth.Value))
            .ToList();

        var scopedExpenseRecords = expenseRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value.Year == resolvedYear
                && record.PaymentDate.Value.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.PaymentDate.Value.Month == resolvedCellMonth.Value))
            .ToList();

        var records = new List<PnlCellDetailRecordDto>();

        foreach (var record in scopedBillingRecords)
        {
            var contribution = GetPnlBillingContributionForRow(record, verticalKey, rowMetadata.Key);
            if (Math.Abs(contribution) < 0.01m)
                continue;

            records.Add(BuildPnlBillingDetailRecord(record, contribution));
        }

        foreach (var record in scopedExpenseRecords)
        {
            var contribution = GetPnlExpenseContributionForRow(record, verticalKey, rowMetadata.Key);
            if (Math.Abs(contribution) < 0.01m)
                continue;

            records.Add(BuildPnlExpenseDetailRecord(record, contribution));
        }

        var orderedRecords = records
            .OrderByDescending(record => Math.Abs(record.CellValue))
            .ThenBy(record => record.DateDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.DocumentNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PnlCellDetailDto
        {
            Year = resolvedYear,
            MonthCutoff = resolvedMonthCutoff,
            CellMonth = resolvedCellMonth,
            RowKey = rowMetadata.Key,
            RowLabel = rowMetadata.Label,
            CellLabel = ResolvePnlCellLabel(resolvedYear, resolvedMonthCutoff, resolvedCellMonth),
            VerticalKey = verticalKey,
            VerticalLabel = verticalLabel,
            ValueFormat = rowMetadata.ValueFormat,
            Total = RoundCurrency(orderedRecords.Sum(record => record.CellValue)),
            RecordsCount = orderedRecords.Count,
            EmptyMessage = BuildPnlDetailEmptyMessage(rowMetadata.Key),
            VerticalOptions = BuildPnlVerticalOptions(),
            CategoryOptions = BuildPnlCategoryOptions(),
            Records = orderedRecords
        };
    }

    public async Task<PnlDetailRecordUpdateResultDto> UpdatePnlDetailRecordAsync(
        PnlDetailRecordUpdateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new InvalidOperationException("No recibimos informacion para actualizar el registro.");

        if (string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar el registro que quieres actualizar.");

        if (string.IsNullOrWhiteSpace(request.SourceType))
            throw new InvalidOperationException("Debes indicar el origen del registro que quieres actualizar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var sourceType = request.SourceType.Trim().ToLowerInvariant();
        switch (sourceType)
        {
            case "billing":
                await UpdatePnlBillingDetailRecordAsync(request, httpContext.User, ct);
                return new PnlDetailRecordUpdateResultDto
                {
                    RecordId = request.RecordId.Trim(),
                    Message = "La vertical de la factura se actualizo correctamente."
                };

            case "expense":
                await UpdatePnlExpenseDetailRecordAsync(request, httpContext.User, ct);
                return new PnlDetailRecordUpdateResultDto
                {
                    RecordId = request.RecordId.Trim(),
                    Message = "El gasto se actualizo correctamente en Dataverse."
                };

            default:
                throw new InvalidOperationException("El origen del registro no es compatible con el detalle del P&L.");
        }
    }

    private async Task UpdatePnlBillingDetailRecordAsync(
        PnlDetailRecordUpdateRequestDto request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var verticalOption = ResolveDashboardVerticalOptionValue(request.VerticalKey);
        var payload = new Dictionary<string, object>
        {
            [_dashboardBillingVerticalField] = verticalOption
        };

        var relativeUrl = $"/api/data/v9.2/{_dashboardBillingTableSetName}({request.RecordId.Trim()})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, user, ct);
    }

    private async Task UpdatePnlExpenseDetailRecordAsync(
        PnlDetailRecordUpdateRequestDto request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var recordId = request.RecordId.Trim();
        var current = await GetPnlExpenseRowByIdAsync(recordId, user, ct)
            ?? throw new InvalidOperationException("No encontramos el gasto seleccionado en Dataverse.");

        var requestedVerticalKey = NormalizeEditablePnlVerticalKey(request.VerticalKey);
        var finalCategoryOption = request.CategoryOptionValue ?? current.CategoryOptionValue;
        var payload = new Dictionary<string, object>();

        if (IsPnlExpensePersonalCategory(finalCategoryOption))
        {
            var personalVerticalKey = requestedVerticalKey
                ?? (finalCategoryOption == PnlExpensePersonalCloudOption ? DashboardPnlVerticalCloud : DashboardPnlVerticalCopiers);
            finalCategoryOption = personalVerticalKey == DashboardPnlVerticalCloud
                ? PnlExpensePersonalCloudOption
                : PnlExpensePersonalCopiersOption;
            requestedVerticalKey = personalVerticalKey;
        }
        else if (IsPnlExpensePersonalCategory(current.CategoryOptionValue) && requestedVerticalKey is null && request.CategoryOptionValue.HasValue)
        {
            requestedVerticalKey = ResolvePnlExpenseEditorVerticalKey(current);
        }

        if (finalCategoryOption != current.CategoryOptionValue)
        {
            payload[DashboardExpenseCategoryField] = finalCategoryOption;
        }

        var shouldRewriteAllocation = requestedVerticalKey is not null
            || (request.CategoryOptionValue.HasValue
                && (IsPnlExpensePersonalCategory(current.CategoryOptionValue) || IsPnlExpensePersonalCategory(finalCategoryOption)));

        if (shouldRewriteAllocation)
        {
            var allocationVerticalKey = requestedVerticalKey ?? ResolvePnlExpenseEditorVerticalKey(current);
            if (allocationVerticalKey is DashboardPnlVerticalCloud or DashboardPnlVerticalCopiers)
            {
                var baseValue = GetPnlExpenseBaseValue(current);
                payload[DashboardExpenseCloudField] = allocationVerticalKey == DashboardPnlVerticalCloud ? baseValue : 0m;
                payload[DashboardExpenseCopiersField] = allocationVerticalKey == DashboardPnlVerticalCopiers ? baseValue : 0m;
            }
        }

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos cambios para guardar en este registro.");

        var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}({recordId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, user, ct);
    }

    private async Task<PnlExpenseRow?> GetPnlExpenseRowByIdAsync(
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            _supplierExpensesIdField,
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentValueField,
            DashboardExpenseCloudField,
            DashboardExpenseCopiersField,
            DashboardExpenseCategoryField,
            DashboardExpenseTotalField,
            DashboardExpenseVatField,
            DashboardExpenseTotalBeforeVatField
        });

        var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}({recordId})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return ParsePnlExpenseRow(doc.RootElement);
    }

    private async Task<List<PnlExpenseRow>> GetPnlExpenseRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var fullSelect = string.Join(",", new[]
        {
            _supplierExpensesIdField,
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentValueField,
            DashboardExpenseCloudField,
            DashboardExpenseCopiersField,
            DashboardExpenseCategoryField,
            DashboardExpenseIssuerNameField,
            DashboardExpenseTotalField,
            DashboardExpenseVatField,
            DashboardExpenseTotalBeforeVatField
        });

        var fallbackSelect = string.Join(",", new[]
        {
            _supplierExpensesIdField,
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentValueField,
            DashboardExpenseCloudField,
            DashboardExpenseCopiersField,
            DashboardExpenseCategoryField,
            DashboardExpenseIssuerNameField
        });

        var filter = BuildBillingDateFilter(
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentDateFieldKind,
            startInclusive,
            endExclusive);

        var fullRelativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$select={fullSelect}&$filter={Uri.EscapeDataString(filter)}&$orderby={DashboardExpensePaymentDateField} asc";
        var fallbackRelativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$select={fallbackSelect}&$filter={Uri.EscapeDataString(filter)}&$orderby={DashboardExpensePaymentDateField} asc";

        IReadOnlyList<JsonElement> items;
        try
        {
            items = await GetDataverseEntitiesAsync(fullRelativeUrl, user, ct, AddFormattedValueHeaders);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            items = await GetDataverseEntitiesAsync(fallbackRelativeUrl, user, ct, AddFormattedValueHeaders);
        }

        return items
            .Select(ParsePnlExpenseRow)
            .Where(static row => row is not null)
            .Cast<PnlExpenseRow>()
            .ToList();
    }

    private PnlExpenseRow? ParsePnlExpenseRow(JsonElement item)
    {
        var categoryOptionValue = ReadInt(item, DashboardExpenseCategoryField);
        var recordId = FirstNonEmpty(
            ReadString(item, _supplierExpensesIdField),
            $"{categoryOptionValue}|{ReadString(item, DashboardExpensePaymentDateField)}|{ReadString(item, DashboardExpensePaymentValueField)}");

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var totalValue = RoundCurrency(ReadDecimal(item, DashboardExpenseTotalField) ?? 0m);
        var vatValue = RoundCurrency(ReadDecimal(item, DashboardExpenseVatField) ?? 0m);
        var totalBeforeVatValue = RoundCurrency(
            ReadDecimal(item, DashboardExpenseTotalBeforeVatField)
            ?? (totalValue != 0m || vatValue != 0m ? totalValue - vatValue : 0m));

        return new PnlExpenseRow
        {
            RecordId = recordId.Trim(),
            PaymentDate = ReadDateOnly(item, DashboardExpensePaymentDateField),
            PaymentValue = RoundCurrency(ReadDecimal(item, DashboardExpensePaymentValueField) ?? 0m),
            IssuerName = ReadString(item, DashboardExpenseIssuerNameField).Trim(),
            CategoryOptionValue = categoryOptionValue,
            CategoryLabel = FirstNonEmpty(
                ReadString(item, $"{DashboardExpenseCategoryField}{FormattedValueAnnotationSuffix}"),
                ResolvePnlExpenseCategoryLabel(categoryOptionValue)),
            TotalValue = totalValue,
            VatValue = vatValue,
            TotalBeforeVatValue = totalBeforeVatValue,
            CloudValue = RoundCurrency(ReadDecimal(item, DashboardExpenseCloudField) ?? 0m),
            CopiersValue = RoundCurrency(ReadDecimal(item, DashboardExpenseCopiersField) ?? 0m)
        };
    }

    private static IReadOnlyList<PnlKpiDto> BuildPnlKpis(
        decimal operatingRevenueTotal,
        decimal grossProfitTotal,
        decimal ebitdaTotal,
        decimal grossMargin,
        decimal ebitdaMargin,
        string verticalLabel,
        int monthCutoff,
        int year)
    {
        var cutoffLabel = ResolvePnlMonthLabel(year, monthCutoff);

        return new[]
        {
            BuildPnlKpi("operating-revenue", "Revenue", $"Ingresos operacionales acumulados hasta {cutoffLabel} {year} para {verticalLabel}.", operatingRevenueTotal, "currency"),
            BuildPnlKpi("gross-profit", "Gross Profit", "Utilidad bruta: ingresos operacionales menos COGS.", grossProfitTotal, "currency"),
            BuildPnlKpi("ebitda", "EBITDA", "EBITDA: utilidad bruta menos gastos operacionales.", ebitdaTotal, "currency"),
            BuildPnlKpi("gross-margin", "Gross Margin", "Margen bruto = utilidad bruta / ingresos operacionales.", grossMargin, "percent"),
            BuildPnlKpi("ebitda-margin", "EBITDA Margin", "Margen EBITDA = EBITDA / ingresos operacionales.", ebitdaMargin, "percent")
        };
    }

    private static PnlKpiDto BuildPnlKpi(string key, string label, string hint, decimal value, string valueFormat)
    {
        return new PnlKpiDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = RoundCurrency(value),
            ValueFormat = valueFormat,
            Tone = ResolvePnlTone(value)
        };
    }

    private static IReadOnlyList<PnlRowDto> BuildPnlRows(
        IReadOnlyList<decimal> copiersRevenue,
        IReadOnlyList<decimal> cloudRevenue,
        IReadOnlyList<decimal> operatingRevenue,
        IReadOnlyList<decimal> licensing,
        IReadOnlyList<decimal> rebates,
        IReadOnlyList<decimal> supplies,
        IReadOnlyList<decimal> machines,
        IReadOnlyList<decimal> technicalService,
        IReadOnlyList<decimal> cogs,
        IReadOnlyList<decimal> grossProfit,
        IReadOnlyList<decimal> personalAdministrative,
        IReadOnlyList<decimal> personalCloud,
        IReadOnlyList<decimal> personalCopiers,
        IReadOnlyList<decimal> personalSubtotal,
        IReadOnlyList<decimal> officeRent,
        IReadOnlyList<decimal> warehouse,
        IReadOnlyList<decimal> transport,
        IReadOnlyList<decimal> internalExpenses,
        IReadOnlyList<decimal> recurring,
        IReadOnlyList<decimal> equipment,
        IReadOnlyList<decimal> travel,
        IReadOnlyList<decimal> empty,
        IReadOnlyList<decimal> administrativeSubtotal,
        IReadOnlyList<decimal> marketing,
        IReadOnlyList<decimal> commercialSubtotal,
        IReadOnlyList<decimal> ebitda,
        IReadOnlyList<decimal> taxes,
        IReadOnlyList<decimal> financial,
        IReadOnlyList<decimal> otherNonOperating)
    {
        return new[]
        {
            BuildPnlSection("section-income", "1. Ingresos Operacionales", 0),
            BuildPnlValueRow("income-copiers", "Copiers", "detail", 1, copiersRevenue),
            BuildPnlValueRow("income-cloud", "Cloud", "detail", 1, cloudRevenue),
            BuildPnlValueRow("income-total", "INGRESOS OPERACIONALES (total)", "subtotal", 1, operatingRevenue),

            BuildPnlSection("section-cogs", "2. Costo de Ventas (COGS)", 0),
            BuildPnlValueRow("cogs-licensing", "Licenciamiento (gross)", "detail", 1, licensing),
            BuildPnlValueRow("cogs-rebates", "Rebates", "detail", 1, rebates),
            BuildPnlValueRow("cogs-supplies", "Suministros", "detail", 1, supplies),
            BuildPnlValueRow("cogs-machines", "Maquinas", "detail", 1, machines),
            BuildPnlValueRow("cogs-technical", "Servicio Tecnico", "detail", 1, technicalService),
            BuildPnlValueRow("cogs-total", "COGS (total)", "subtotal", 1, cogs),

            BuildPnlSection("section-gross-profit", "3. Utilidad Bruta", 0),
            BuildPnlValueRow("gross-profit", "UTILIDAD BRUTA", "formula", 1, grossProfit),

            BuildPnlSection("section-operating-expenses", "4. Gastos Operacionales", 0),
            BuildPnlSection("section-personal", "4.1 Gastos de personal", 1),
            BuildPnlValueRow("personal-administrative", "Personal Administrativo", "detail", 2, personalAdministrative),
            BuildPnlValueRow("personal-cloud", "Personal Cloud", "detail", 2, personalCloud),
            BuildPnlValueRow("personal-copiers", "Personal Copiers", "detail", 2, personalCopiers),
            BuildPnlValueRow("personal-total", "Subtotal personal", "subtotal", 2, personalSubtotal),

            BuildPnlSection("section-administrative", "4.2 Gastos administrativos", 1),
            BuildPnlValueRow("admin-office-rent", "Arriendo Oficina", "detail", 2, officeRent),
            BuildPnlValueRow("admin-warehouse", "Bodegaje", "detail", 2, warehouse),
            BuildPnlValueRow("admin-transport", "Transporte Equipos", "detail", 2, transport),
            BuildPnlValueRow("admin-internal", "Gastos internos", "detail", 2, internalExpenses),
            BuildPnlValueRow("admin-recurring", "Recurrente", "detail", 2, recurring),
            BuildPnlValueRow("admin-equipment", "Equipamiento", "detail", 2, equipment),
            BuildPnlValueRow("admin-travel", "Viaticos", "detail", 2, travel),
            BuildPnlValueRow("admin-empty", "Vacios", "detail", 2, empty),
            BuildPnlValueRow("admin-total", "Subtotal administrativos", "subtotal", 2, administrativeSubtotal),

            BuildPnlSection("section-commercial", "4.3 Gastos comerciales", 1),
            BuildPnlValueRow("commercial-marketing", "Marketing", "detail", 2, marketing),
            BuildPnlValueRow("commercial-total", "Subtotal comerciales", "subtotal", 2, commercialSubtotal),

            BuildPnlSection("section-ebitda", "5. EBITDA", 0),
            BuildPnlValueRow("ebitda", "EBITDA", "formula", 1, ebitda),

            BuildPnlSection("section-other", "6. Otros Ingresos / Gastos", 0),
            BuildPnlValueRow("other-taxes", "Impuestos", "detail", 1, taxes),
            BuildPnlValueRow("other-financial", "Financieros / Contables", "detail", 1, financial),
            BuildPnlValueRow("other-total", "Otros ingresos / gastos (total)", "subtotal", 1, otherNonOperating)
        };
    }

    private static PnlRowDto BuildPnlSection(string key, string label, int level)
    {
        return new PnlRowDto
        {
            Key = key,
            Label = label,
            RowType = "section",
            Level = level
        };
    }

    private static PnlRowDto BuildPnlValueRow(
        string key,
        string label,
        string rowType,
        int level,
        IReadOnlyList<decimal> values,
        string valueFormat = "currency")
    {
        return new PnlRowDto
        {
            Key = key,
            Label = label,
            RowType = rowType,
            Level = level,
            ValueFormat = valueFormat,
            Values = values,
            Total = SumPnlSeriesTotal(values)
        };
    }

    private static int? ResolvePnlCellMonth(int? requestedMonth, int resolvedMonthCutoff)
    {
        if (!requestedMonth.HasValue)
            return null;

        return Math.Clamp(requestedMonth.Value, 1, resolvedMonthCutoff);
    }

    private static decimal GetPnlBillingContributionForRow(BillingRecordRow row, string verticalKey, string rowKey)
    {
        var cloudAmount = GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCloudOption);
        var copiersAmount = GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption);

        return rowKey switch
        {
            "income-cloud" => cloudAmount,
            "income-copiers" => copiersAmount,
            "income-total" or "gross-profit" or "ebitda" => RoundCurrency(cloudAmount + copiersAmount),
            _ => 0m
        };
    }

    private static decimal GetPnlExpenseContributionForRow(PnlExpenseRow row, string verticalKey, string rowKey)
    {
        var amount = GetPnlExpenseViewAmount(row, verticalKey);
        if (Math.Abs(amount) < 0.01m)
            return 0m;

        var bucketKey = ResolvePnlExpenseBucketKey(row);

        return rowKey switch
        {
            "cogs-licensing" => bucketKey == "licensing" ? amount : 0m,
            "cogs-rebates" => 0m,
            "cogs-supplies" => bucketKey == "supplies" ? amount : 0m,
            "cogs-machines" => bucketKey == "machines" ? amount : 0m,
            "cogs-technical-service" => bucketKey == "technical-service" ? amount : 0m,
            "cogs-total" => IsPnlCogsBucket(bucketKey) ? amount : 0m,
            "gross-profit" => IsPnlCogsBucket(bucketKey) ? RoundCurrency(-amount) : 0m,
            "personal-administrative" => bucketKey == "personal-administrative" ? amount : 0m,
            "personal-cloud" => bucketKey == "personal-cloud" ? amount : 0m,
            "personal-copiers" => bucketKey == "personal-copiers" ? amount : 0m,
            "personal-total" => IsPnlPersonalBucket(bucketKey) ? amount : 0m,
            "admin-office-rent" => bucketKey == "office-rent" ? amount : 0m,
            "admin-warehouse" => bucketKey == "warehouse" ? amount : 0m,
            "admin-transport" => bucketKey == "transport" ? amount : 0m,
            "admin-internal" => bucketKey == "internal" ? amount : 0m,
            "admin-recurring" => bucketKey == "recurring" ? amount : 0m,
            "admin-equipment" => bucketKey == "equipment" ? amount : 0m,
            "admin-travel" => bucketKey == "travel" ? amount : 0m,
            "admin-empty" => bucketKey == "empty" ? amount : 0m,
            "admin-total" => IsPnlAdministrativeBucket(bucketKey) ? amount : 0m,
            "commercial-marketing" => bucketKey == "marketing" ? amount : 0m,
            "commercial-total" => bucketKey == "marketing" ? amount : 0m,
            "ebitda" => IsPnlEbitdaExpenseBucket(bucketKey) ? RoundCurrency(-amount) : 0m,
            "other-taxes" => bucketKey == "taxes" ? amount : 0m,
            "other-financial" => bucketKey == "financial" ? amount : 0m,
            "other-total" => bucketKey is "taxes" or "financial" ? amount : 0m,
            _ => 0m
        };
    }

    private static bool IsPnlCogsBucket(string bucketKey) =>
        bucketKey is "licensing" or "supplies" or "machines" or "technical-service";

    private static bool IsPnlPersonalBucket(string bucketKey) =>
        bucketKey is "personal-administrative" or "personal-cloud" or "personal-copiers";

    private static bool IsPnlAdministrativeBucket(string bucketKey) =>
        bucketKey is "office-rent" or "warehouse" or "transport" or "internal" or "recurring" or "equipment" or "travel" or "empty";

    private static bool IsPnlEbitdaExpenseBucket(string bucketKey) =>
        IsPnlCogsBucket(bucketKey) || IsPnlPersonalBucket(bucketKey) || IsPnlAdministrativeBucket(bucketKey) || bucketKey == "marketing";

    private static PnlCellDetailRecordDto BuildPnlBillingDetailRecord(BillingRecordRow row, decimal cellValue)
    {
        var verticalKey = ResolvePnlVerticalKeyFromOptionValue(row.VerticalOptionValue);
        return new PnlCellDetailRecordDto
        {
            SourceType = "billing",
            SourceLabel = "Facturacion",
            RecordId = row.RecordId,
            DocumentNumber = row.InvoiceNumber,
            Description = row.ClientName,
            DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
            VerticalKey = verticalKey,
            VerticalLabel = string.IsNullOrWhiteSpace(row.VerticalLabel) ? ResolvePnlDetailVerticalLabel(verticalKey) : row.VerticalLabel,
            CategoryLabel = "No aplica",
            TotalInvoice = row.TotalInvoice,
            VatValue = row.VatValue,
            TotalBeforeVatValue = CalculateInvoiceTaxBase(row),
            PaymentValue = row.PaymentValue,
            CloudValue = 0m,
            CopiersValue = 0m,
            CellValue = RoundCurrency(cellValue),
            CanEditVertical = true,
            CanEditCategory = false
        };
    }

    private static PnlCellDetailRecordDto BuildPnlExpenseDetailRecord(PnlExpenseRow row, decimal cellValue)
    {
        var verticalKey = ResolvePnlExpenseEditorVerticalKey(row);
        return new PnlCellDetailRecordDto
        {
            SourceType = "expense",
            SourceLabel = "Gasto",
            RecordId = row.RecordId,
            DocumentNumber = row.RecordId,
            Description = string.IsNullOrWhiteSpace(row.IssuerName) ? row.CategoryLabel : row.IssuerName,
            DateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
            VerticalKey = verticalKey,
            VerticalLabel = ResolvePnlDetailVerticalLabel(verticalKey),
            CategoryOptionValue = row.CategoryOptionValue,
            CategoryLabel = string.IsNullOrWhiteSpace(row.CategoryLabel) ? "Sin categoria" : row.CategoryLabel,
            TotalInvoice = row.TotalValue,
            VatValue = row.VatValue,
            TotalBeforeVatValue = row.TotalBeforeVatValue,
            PaymentValue = row.PaymentValue,
            CloudValue = row.CloudValue,
            CopiersValue = row.CopiersValue,
            CellValue = RoundCurrency(cellValue),
            CanEditVertical = true,
            CanEditCategory = true
        };
    }

    private static PnlRowMetadata ResolvePnlRowMetadata(string rowKey)
    {
        var normalizedKey = rowKey.Trim();
        return normalizedKey switch
        {
            "income-copiers" => new PnlRowMetadata(normalizedKey, "Copiers"),
            "income-cloud" => new PnlRowMetadata(normalizedKey, "Cloud"),
            "income-total" => new PnlRowMetadata(normalizedKey, "INGRESOS OPERACIONALES (total)"),
            "cogs-licensing" => new PnlRowMetadata(normalizedKey, "Licenciamiento (gross)"),
            "cogs-rebates" => new PnlRowMetadata(normalizedKey, "Rebates"),
            "cogs-supplies" => new PnlRowMetadata(normalizedKey, "Suministros"),
            "cogs-machines" => new PnlRowMetadata(normalizedKey, "Maquinas"),
            "cogs-technical-service" => new PnlRowMetadata(normalizedKey, "Servicio Tecnico"),
            "cogs-total" => new PnlRowMetadata(normalizedKey, "COGS (total)"),
            "gross-profit" => new PnlRowMetadata(normalizedKey, "UTILIDAD BRUTA"),
            "personal-administrative" => new PnlRowMetadata(normalizedKey, "Personal Administrativo"),
            "personal-cloud" => new PnlRowMetadata(normalizedKey, "Personal Cloud"),
            "personal-copiers" => new PnlRowMetadata(normalizedKey, "Personal Copiers"),
            "personal-total" => new PnlRowMetadata(normalizedKey, "Subtotal personal"),
            "admin-office-rent" => new PnlRowMetadata(normalizedKey, "Arriendo Oficina"),
            "admin-warehouse" => new PnlRowMetadata(normalizedKey, "Bodegaje"),
            "admin-transport" => new PnlRowMetadata(normalizedKey, "Transporte Equipos"),
            "admin-internal" => new PnlRowMetadata(normalizedKey, "Gastos internos"),
            "admin-recurring" => new PnlRowMetadata(normalizedKey, "Recurrente"),
            "admin-equipment" => new PnlRowMetadata(normalizedKey, "Equipamiento"),
            "admin-travel" => new PnlRowMetadata(normalizedKey, "Viaticos"),
            "admin-empty" => new PnlRowMetadata(normalizedKey, "Vacios"),
            "admin-total" => new PnlRowMetadata(normalizedKey, "Subtotal administrativos"),
            "commercial-marketing" => new PnlRowMetadata(normalizedKey, "Marketing"),
            "commercial-total" => new PnlRowMetadata(normalizedKey, "Subtotal comerciales"),
            "ebitda" => new PnlRowMetadata(normalizedKey, "EBITDA"),
            "other-taxes" => new PnlRowMetadata(normalizedKey, "Impuestos"),
            "other-financial" => new PnlRowMetadata(normalizedKey, "Financieros / Contables"),
            "other-total" => new PnlRowMetadata(normalizedKey, "Otros ingresos / gastos (total)"),
            _ => throw new InvalidOperationException("La fila seleccionada no existe dentro de la estructura del P&L.")
        };
    }

    private static string ResolvePnlCellLabel(int year, int monthCutoff, int? cellMonth)
    {
        if (cellMonth.HasValue)
            return $"{ResolvePnlMonthLabel(year, cellMonth.Value)} {year}";

        return $"Total acumulado a {ResolvePnlMonthLabel(year, monthCutoff)} {year}";
    }

    private static string BuildPnlDetailEmptyMessage(string rowKey) => rowKey switch
    {
        "cogs-rebates" => "La fila de rebates sigue siendo manual y por ahora no tiene registros de detalle en Dataverse.",
        _ => "No encontramos registros que compongan esta celda con los filtros actuales."
    };

    private static IReadOnlyList<PnlOptionDto> BuildPnlVerticalOptions() => new[]
    {
        new PnlOptionDto { Key = DashboardPnlVerticalCloud, Label = "Cloud" },
        new PnlOptionDto { Key = DashboardPnlVerticalCopiers, Label = "Copiers" }
    };

    private static IReadOnlyList<PnlOptionDto> BuildPnlCategoryOptions() => new[]
    {
        PnlExpensePersonalAdministrativeOption,
        PnlExpensePersonalCloudOption,
        PnlExpensePersonalCopiersOption,
        PnlExpenseOfficeRentOption,
        PnlExpenseWarehouseOption,
        PnlExpenseTransportOption,
        PnlExpenseInternalOption,
        PnlExpenseRecurringOption,
        PnlExpenseEquipmentOption,
        PnlExpenseTravelOption,
        PnlExpenseMarketingOption,
        PnlExpenseTaxesOption,
        PnlExpenseMachinesOption,
        PnlExpenseSuppliesOption,
        PnlExpenseLicensingOption,
        PnlExpenseFinancialOption,
        PnlExpenseTechnicalServiceOption
    }
        .Select(optionValue => new PnlOptionDto
        {
            Key = optionValue.ToString(CultureInfo.InvariantCulture),
            Label = ResolvePnlExpenseCategoryLabel(optionValue),
            Value = optionValue
        })
        .ToList();

    private static string ResolvePnlVerticalKeyFromOptionValue(int optionValue) => optionValue switch
    {
        DashboardVerticalCloudOption => DashboardPnlVerticalCloud,
        DashboardVerticalCopiersOption => DashboardPnlVerticalCopiers,
        _ => "none"
    };

    private static int ResolveDashboardVerticalOptionValue(string? verticalKey) => NormalizeEditablePnlVerticalKey(verticalKey) switch
    {
        DashboardPnlVerticalCloud => DashboardVerticalCloudOption,
        DashboardPnlVerticalCopiers => DashboardVerticalCopiersOption,
        _ => throw new InvalidOperationException("La vertical debe ser Cloud o Copiers para poder guardar.")
    };

    private static string? NormalizeEditablePnlVerticalKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            DashboardPnlVerticalCloud => DashboardPnlVerticalCloud,
            DashboardPnlVerticalCopiers => DashboardPnlVerticalCopiers,
            _ => throw new InvalidOperationException("La vertical seleccionada no es valida para actualizar el registro.")
        };
    }

    private static string ResolvePnlExpenseEditorVerticalKey(PnlExpenseRow row)
    {
        if (row.CategoryOptionValue == PnlExpensePersonalCloudOption)
            return DashboardPnlVerticalCloud;

        if (row.CategoryOptionValue == PnlExpensePersonalCopiersOption)
            return DashboardPnlVerticalCopiers;

        var hasCloud = Math.Abs(row.CloudValue) >= 0.01m;
        var hasCopiers = Math.Abs(row.CopiersValue) >= 0.01m;

        return (hasCloud, hasCopiers) switch
        {
            (true, false) => DashboardPnlVerticalCloud,
            (false, true) => DashboardPnlVerticalCopiers,
            (true, true) => "mixed",
            _ => "none"
        };
    }

    private static string ResolvePnlDetailVerticalLabel(string verticalKey) => verticalKey switch
    {
        DashboardPnlVerticalCloud => "Cloud",
        DashboardPnlVerticalCopiers => "Copiers",
        "mixed" => "Mixto",
        _ => "Sin vertical"
    };

    private static bool IsPnlExpensePersonalCategory(int optionValue) =>
        optionValue is PnlExpensePersonalCloudOption or PnlExpensePersonalCopiersOption;

    private static IReadOnlyList<PnlMonthColumnDto> BuildPnlMonthColumns(int year, int monthCutoff)
    {
        return Enumerable.Range(1, Math.Clamp(monthCutoff, 1, 12))
            .Select(month => new PnlMonthColumnDto
            {
                Month = month,
                Key = month.ToString(CultureInfo.InvariantCulture),
                Label = ResolvePnlMonthLabel(year, month)
            })
            .ToList();
    }

    private static IReadOnlyList<decimal> BuildPnlBillingSeries(
        int monthCutoff,
        IReadOnlyList<BillingRecordRow> rows,
        Func<BillingRecordRow, decimal> selector)
    {
        var values = new decimal[Math.Clamp(monthCutoff, 1, 12)];

        foreach (var row in rows)
        {
            if (row.EmissionDate is null)
                continue;

            var month = row.EmissionDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + selector(row));
        }

        return values;
    }

    private static IReadOnlyList<decimal> BuildPnlExpenseSeries(
        int monthCutoff,
        IReadOnlyList<PnlExpenseRow> rows,
        string verticalKey,
        string bucketKey)
    {
        var values = new decimal[Math.Clamp(monthCutoff, 1, 12)];

        foreach (var row in rows)
        {
            if (row.PaymentDate is null)
                continue;

            var month = row.PaymentDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            if (!string.Equals(ResolvePnlExpenseBucketKey(row), bucketKey, StringComparison.OrdinalIgnoreCase))
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + GetPnlExpenseViewAmount(row, verticalKey));
        }

        return values;
    }

    private static IReadOnlyList<decimal> SumPnlSeries(params IReadOnlyList<decimal>[] seriesCollection)
    {
        if (seriesCollection.Length == 0)
            return Array.Empty<decimal>();

        var length = seriesCollection.Max(series => series.Count);
        var values = new decimal[length];

        foreach (var series in seriesCollection)
        {
            for (var index = 0; index < series.Count; index += 1)
            {
                values[index] = RoundCurrency(values[index] + series[index]);
            }
        }

        return values;
    }

    private static IReadOnlyList<decimal> SubtractPnlSeries(IReadOnlyList<decimal> baseSeries, params IReadOnlyList<decimal>[] deductions)
    {
        var values = baseSeries.ToArray();

        foreach (var series in deductions)
        {
            for (var index = 0; index < series.Count && index < values.Length; index += 1)
            {
                values[index] = RoundCurrency(values[index] - series[index]);
            }
        }

        return values;
    }

    private static IReadOnlyList<decimal> EmptyPnlSeries(int monthCutoff) =>
        new decimal[Math.Clamp(monthCutoff, 1, 12)];

    private static decimal SumPnlSeriesTotal(IReadOnlyList<decimal> series) =>
        RoundCurrency(series.Sum());

    private static int CountPnlRelevantBillingRecords(IEnumerable<BillingRecordRow> rows, string verticalKey)
    {
        return rows.Count(row =>
            Math.Abs(GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCloudOption)) >= 0.01m
            || Math.Abs(GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption)) >= 0.01m);
    }

    private static int CountPnlRelevantExpenseRecords(IEnumerable<PnlExpenseRow> rows, string verticalKey)
    {
        return rows.Count(row => Math.Abs(GetPnlExpenseViewAmount(row, verticalKey)) >= 0.01m);
    }

    private static decimal GetPnlRevenueAmount(BillingRecordRow row, string verticalKey, int targetVerticalOption)
    {
        if (row.VerticalOptionValue != targetVerticalOption)
            return 0m;

        if (!MatchesPnlVerticalSelection(targetVerticalOption, verticalKey))
            return 0m;

        return row.TotalInvoice;
    }

    private static decimal GetPnlExpenseViewAmount(PnlExpenseRow row, string verticalKey)
    {
        var baseValue = GetPnlExpenseBaseValue(row);
        if (baseValue == 0m)
            return 0m;

        if (string.Equals(verticalKey, DashboardPnlVerticalAll, StringComparison.OrdinalIgnoreCase))
            return baseValue;

        if (row.CategoryOptionValue == PnlExpensePersonalCloudOption)
            return string.Equals(verticalKey, DashboardPnlVerticalCloud, StringComparison.OrdinalIgnoreCase) ? baseValue : 0m;

        if (row.CategoryOptionValue == PnlExpensePersonalCopiersOption)
            return string.Equals(verticalKey, DashboardPnlVerticalCopiers, StringComparison.OrdinalIgnoreCase) ? baseValue : 0m;

        return AllocatePnlExpenseByVertical(baseValue, row, verticalKey);
    }

    private static decimal AllocatePnlExpenseByVertical(decimal baseValue, PnlExpenseRow row, string verticalKey)
    {
        var cloudBase = Math.Max(row.CloudValue, 0m);
        var copiersBase = Math.Max(row.CopiersValue, 0m);
        var totalBase = cloudBase + copiersBase;

        if (totalBase <= 0m)
            return 0m;

        return string.Equals(verticalKey, DashboardPnlVerticalCloud, StringComparison.OrdinalIgnoreCase)
            ? RoundCurrency(baseValue * (cloudBase / totalBase))
            : RoundCurrency(baseValue * (copiersBase / totalBase));
    }

    private static decimal GetPnlExpenseBaseValue(PnlExpenseRow row)
    {
        if (Math.Abs(row.TotalBeforeVatValue) >= 0.01m)
            return row.TotalBeforeVatValue;

        if (Math.Abs(row.TotalValue - row.VatValue) >= 0.01m)
            return RoundCurrency(row.TotalValue - row.VatValue);

        return row.PaymentValue;
    }

    private static string ResolvePnlExpenseBucketKey(PnlExpenseRow row)
    {
        return row.CategoryOptionValue switch
        {
            PnlExpenseLicensingOption => "licensing",
            PnlExpenseSuppliesOption => "supplies",
            PnlExpenseMachinesOption => "machines",
            PnlExpenseTechnicalServiceOption => "technical-service",
            PnlExpensePersonalAdministrativeOption => "personal-administrative",
            PnlExpensePersonalCloudOption => "personal-cloud",
            PnlExpensePersonalCopiersOption => "personal-copiers",
            PnlExpenseOfficeRentOption => "office-rent",
            PnlExpenseWarehouseOption => "warehouse",
            PnlExpenseTransportOption => "transport",
            PnlExpenseInternalOption => "internal",
            PnlExpenseRecurringOption => "recurring",
            PnlExpenseEquipmentOption => "equipment",
            PnlExpenseTravelOption => "travel",
            PnlExpenseMarketingOption => "marketing",
            PnlExpenseTaxesOption => "taxes",
            PnlExpenseFinancialOption => "financial",
            _ => ResolvePnlExpenseBucketKeyFromLabel(row.CategoryLabel)
        };
    }

    private static string ResolvePnlExpenseBucketKeyFromLabel(string? label)
    {
        var normalized = NormalizePnlLabel(label);
        if (string.IsNullOrWhiteSpace(normalized))
            return "empty";

        if (normalized.Contains("licenc", StringComparison.Ordinal))
            return "licensing";

        if (normalized.Contains("sumin", StringComparison.Ordinal))
            return "supplies";

        if (normalized.Contains("maquin", StringComparison.Ordinal))
            return "machines";

        if (normalized.Contains("servicio", StringComparison.Ordinal) && normalized.Contains("tecn", StringComparison.Ordinal))
            return "technical-service";

        if (normalized.Contains("personal") && normalized.Contains("administr", StringComparison.Ordinal))
            return "personal-administrative";

        if (normalized.Contains("personal") && normalized.Contains("cloud", StringComparison.Ordinal))
            return "personal-cloud";

        if (normalized.Contains("personal") && normalized.Contains("copier", StringComparison.Ordinal))
            return "personal-copiers";

        if (normalized.Contains("arriendo", StringComparison.Ordinal) && normalized.Contains("oficina", StringComparison.Ordinal))
            return "office-rent";

        if (normalized.Contains("bodeg", StringComparison.Ordinal))
            return "warehouse";

        if (normalized.Contains("transporte", StringComparison.Ordinal))
            return "transport";

        if (normalized.Contains("intern", StringComparison.Ordinal))
            return "internal";

        if (normalized.Contains("recurrent", StringComparison.Ordinal))
            return "recurring";

        if (normalized.Contains("equip", StringComparison.Ordinal))
            return normalized.Contains("transporte", StringComparison.Ordinal) ? "transport" : "equipment";

        if (normalized.Contains("viatic", StringComparison.Ordinal))
            return "travel";

        if (normalized.Contains("market", StringComparison.Ordinal))
            return "marketing";

        if (normalized.Contains("impuesto", StringComparison.Ordinal))
            return "taxes";

        if (normalized.Contains("financ", StringComparison.Ordinal) || normalized.Contains("contab", StringComparison.Ordinal))
            return "financial";

        return "empty";
    }

    private static int ResolveLatestPnlMonthAvailable(
        int year,
        DateOnly today,
        string verticalKey,
        IReadOnlyList<BillingRecordRow> billingRecords,
        IReadOnlyList<PnlExpenseRow> expenseRecords)
    {
        var months = new List<int>();

        months.AddRange(
            billingRecords
                .Where(row => row.EmissionDate is not null
                    && row.EmissionDate.Value.Year == year
                    && (Math.Abs(GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCloudOption)) >= 0.01m
                        || Math.Abs(GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption)) >= 0.01m))
                .Select(row => row.EmissionDate!.Value.Month));

        months.AddRange(
            expenseRecords
                .Where(row => row.PaymentDate is not null
                    && row.PaymentDate.Value.Year == year
                    && Math.Abs(GetPnlExpenseViewAmount(row, verticalKey)) >= 0.01m)
                .Select(row => row.PaymentDate!.Value.Month));

        if (months.Count > 0)
            return Math.Clamp(months.Max(), 1, 12);

        if (year > today.Year)
            return 1;

        return year == today.Year
            ? Math.Clamp(today.Month, 1, 12)
            : 12;
    }

    private static int ResolvePnlMonthCutoff(int latestMonthAvailable, int? requestedMonth)
    {
        var maxMonth = Math.Clamp(latestMonthAvailable, 1, 12);
        if (requestedMonth is null or <= 0)
            return maxMonth;

        return Math.Clamp(requestedMonth.Value, 1, maxMonth);
    }

    private static bool MatchesPnlVerticalSelection(int verticalOptionValue, string verticalKey)
    {
        return verticalKey switch
        {
            DashboardPnlVerticalCloud => verticalOptionValue == DashboardVerticalCloudOption,
            DashboardPnlVerticalCopiers => verticalOptionValue == DashboardVerticalCopiersOption,
            _ => verticalOptionValue == DashboardVerticalCloudOption || verticalOptionValue == DashboardVerticalCopiersOption
        };
    }

    private static string NormalizePnlVerticalKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DashboardPnlVerticalAll;

        return value.Trim().ToLowerInvariant() switch
        {
            DashboardPnlVerticalCloud => DashboardPnlVerticalCloud,
            DashboardPnlVerticalCopiers => DashboardPnlVerticalCopiers,
            _ => DashboardPnlVerticalAll
        };
    }

    private static string ResolvePnlVerticalLabel(string verticalKey) => verticalKey switch
    {
        DashboardPnlVerticalCloud => "Cloud",
        DashboardPnlVerticalCopiers => "Copiers",
        _ => "Consolidado"
    };

    private static string ResolvePnlMonthLabel(int year, int month)
    {
        var resolvedMonth = Math.Clamp(month, 1, 12);
        return ToTitleCase(new DateOnly(year, resolvedMonth, 1).ToString("MMMM", DashboardCulture));
    }

    private static string ResolvePnlExpenseCategoryLabel(int optionValue) => optionValue switch
    {
        PnlExpensePersonalCloudOption => "Personal Cloud",
        PnlExpensePersonalCopiersOption => "Personal Copiers",
        PnlExpensePersonalAdministrativeOption => "Personal Administrativo",
        PnlExpenseTransportOption => "Transporte Equipos",
        PnlExpenseTravelOption => "Viaticos",
        PnlExpenseMarketingOption => "Marketing",
        PnlExpenseInternalOption => "Gastos internos",
        PnlExpenseTaxesOption => "Impuestos",
        PnlExpenseMachinesOption => "Maquinas",
        PnlExpenseSuppliesOption => "Suministros",
        PnlExpenseLicensingOption => "Licenciamiento",
        PnlExpenseRecurringOption => "Recurrente",
        PnlExpenseFinancialOption => "Financieros / Contables",
        PnlExpenseWarehouseOption => "Bodegaje",
        PnlExpenseEquipmentOption => "Equipamiento",
        PnlExpenseTechnicalServiceOption => "Servicio Tecnico",
        PnlExpenseOfficeRentOption => "Arriendo Oficina",
        _ => ""
    };

    private static string ResolvePnlTone(decimal value)
    {
        if (value > 0m)
            return "positive";

        if (value < 0m)
            return "negative";

        return "neutral";
    }

    private static string NormalizePnlLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record PnlRowMetadata(string Key, string Label, string ValueFormat = "currency");

    private sealed class PnlExpenseRow
    {
        public string RecordId { get; set; } = "";
        public DateOnly? PaymentDate { get; set; }
        public decimal PaymentValue { get; set; }
        public string IssuerName { get; set; } = "";
        public int CategoryOptionValue { get; set; }
        public string CategoryLabel { get; set; } = "";
        public decimal TotalValue { get; set; }
        public decimal VatValue { get; set; }
        public decimal TotalBeforeVatValue { get; set; }
        public decimal CloudValue { get; set; }
        public decimal CopiersValue { get; set; }
    }
}
