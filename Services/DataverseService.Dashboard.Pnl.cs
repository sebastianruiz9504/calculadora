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
    private const string DashboardExpenseEmissionDateField = "cr07a_fechaemision";
    private const string DashboardExpenseEmissionDateFieldKind = "date-only";
    private const string PnlExpensePrimasCesantiasBucket = "primas-cesantias";
    private const string PnlExpenseFinancialIncomeBucket = "financial-income";
    private const string PnlExpenseFinancialExpenseBucket = "financial-expense";
    private const string PnlExpenseOtherNonOperatingBucket = "other-non-operating";

    private static readonly PnlExpenseDateFieldCandidate[] DashboardExpenseEmissionDateFieldCandidates =
    {
        new(DashboardExpenseEmissionDateField, DashboardExpenseEmissionDateFieldKind),
        new("cr07a_fechadeemision", "date-only"),
        new("cr07a_fecha", "date-only")
    };

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

        var billingRecords = await GetSiigoRevenueLedgerRowsAsync(
            metadata,
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var expenseRecords = await GetPnlExpenseRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var manualRecords = await LoadPnlManualRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var rebatesSnapshot = await _sharePointRebatesProvider.GetSnapshotAsync(ct);

        var latestMonthAvailable = ResolveLatestPnlMonthAvailable(
            resolvedYear,
            today,
            verticalKey,
            billingRecords,
            expenseRecords,
            manualRecords,
            rebatesSnapshot.Records);

        var resolvedMonthCutoff = ResolvePnlMonthCutoff(latestMonthAvailable, monthCutoff);
        var periodEndExclusive = new DateOnly(resolvedYear, resolvedMonthCutoff, 1).AddMonths(1);

        var scopedBillingRecords = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff)
            .ToList();

        var scopedExpenseRecords = expenseRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff)
            .ToList();

        var scopedManualRecords = manualRecords
            .Where(record => record.Date is not null
                && record.Date.Value.Year == resolvedYear
                && record.Date.Value.Month <= resolvedMonthCutoff)
            .ToList();

        var scopedRebateRecords = rebatesSnapshot.Records
            .Where(record => record.Date.Year == resolvedYear
                && record.Date.Month <= resolvedMonthCutoff)
            .ToList();

        var months = BuildPnlMonthColumns(resolvedYear, resolvedMonthCutoff);
        var orphanRows = BuildPnlOrphanRows(
            resolvedMonthCutoff,
            scopedBillingRecords,
            scopedExpenseRecords);

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
        var rebates = BuildPnlRebateSeries(resolvedMonthCutoff, scopedRebateRecords);
        var supplies = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "supplies");
        var machines = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "machines");
        var technicalService = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "technical-service");
        var cogs = SumPnlSeries(licensing, rebates, supplies, machines, technicalService);
        var grossProfit = SubtractPnlSeries(operatingRevenue, cogs);

        var personalAdministrative = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-administrative");
        var primasCesantias = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, PnlExpensePrimasCesantiasBucket);
        var personalCloud = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-cloud");
        var personalCopiers = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "personal-copiers");
        var personalSubtotal = SumPnlSeries(personalAdministrative, primasCesantias, personalCloud, personalCopiers);

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

        var financialIncome = SumPnlSeries(
            BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, PnlExpenseFinancialIncomeBucket),
            BuildPnlManualSeries(resolvedMonthCutoff, scopedManualRecords, PnlManualItemFinancialIncomeKey));
        var financialExpenses = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, PnlExpenseFinancialExpenseBucket);
        var otherNonOperating = BuildPnlOtherNonOperatingSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey);
        var totalOtherIncomeExpenses = SumPnlSeries(financialIncome, NegatePnlSeries(financialExpenses), otherNonOperating);
        var incomeBeforeTaxes = SumPnlSeries(ebitda, totalOtherIncomeExpenses);

        var taxes = BuildPnlExpenseSeries(resolvedMonthCutoff, scopedExpenseRecords, verticalKey, "taxes");
        var netIncome = SubtractPnlSeries(incomeBeforeTaxes, taxes);

        var recordsCount = CountPnlRelevantBillingRecords(scopedBillingRecords, verticalKey)
            + CountPnlRelevantExpenseRecords(scopedExpenseRecords, verticalKey)
            + CountPnlRelevantManualRecords(scopedManualRecords)
            + scopedRebateRecords.Count(static record => Math.Abs(record.Value) >= 0.01m);

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
            Description = "P&L mensual sin IVA, asignado por Fecha de Emision. Rebates se lee en tiempo real desde la tabla Rebates de Facturacion DIGITAL TECH.xlsx; ingresos financieros continua desde Admin Rebates/Inversiones.",
            HasData = recordsCount > 0,
            RecordsCount = recordsCount,
            EmptyStateTitle = "No encontramos movimientos para construir el P&L.",
            EmptyStateMessage = "Cuando existan ingresos o costos cargados en el año seleccionado veras la matriz mensual aqui.",
            SourceWarning = rebatesSnapshot.Warning,
            OrphanDescription = "Estos conteos muestran facturas sin vertical y gastos pendientes de clasificacion o reparto. Haz clic sobre cualquier numero para abrir el detalle y corregir el registro.",
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
                primasCesantias,
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
                financialIncome,
                financialExpenses,
                otherNonOperating,
                totalOtherIncomeExpenses,
                incomeBeforeTaxes,
                taxes,
                netIncome),
            OrphanRows = orphanRows
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

        var billingRecords = await GetSiigoRevenueLedgerRowsAsync(
            metadata,
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var expenseRecords = await GetPnlExpenseRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var manualRecords = await LoadPnlManualRowsAsync(
            yearStart,
            yearEnd,
            httpContext.User,
            ct);

        var rebatesSnapshot = await _sharePointRebatesProvider.GetSnapshotAsync(ct);

        var latestMonthAvailable = ResolveLatestPnlMonthAvailable(
            resolvedYear,
            today,
            verticalKey,
            billingRecords,
            expenseRecords,
            manualRecords,
            rebatesSnapshot.Records);

        var resolvedMonthCutoff = ResolvePnlMonthCutoff(latestMonthAvailable, monthCutoff);
        var resolvedCellMonth = ResolvePnlCellMonth(cellMonth, resolvedMonthCutoff);

        if (IsPnlOrphanRow(rowMetadata.Key))
        {
            return BuildPnlOrphanCellDetail(
                resolvedYear,
                resolvedMonthCutoff,
                resolvedCellMonth,
                rowMetadata,
                billingRecords,
                expenseRecords);
        }

        var scopedBillingRecords = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.EmissionDate.Value.Month == resolvedCellMonth.Value))
            .ToList();

        var scopedExpenseRecords = expenseRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.EmissionDate.Value.Month == resolvedCellMonth.Value))
            .ToList();

        var scopedManualRecords = manualRecords
            .Where(record => record.Date is not null
                && record.Date.Value.Year == resolvedYear
                && record.Date.Value.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.Date.Value.Month == resolvedCellMonth.Value))
            .ToList();

        var scopedRebateRecords = rebatesSnapshot.Records
            .Where(record => record.Date.Year == resolvedYear
                && record.Date.Month <= resolvedMonthCutoff
                && (!resolvedCellMonth.HasValue || record.Date.Month == resolvedCellMonth.Value))
            .ToList();

        var records = new List<PnlCellDetailRecordDto>();

        foreach (var record in scopedBillingRecords)
        {
            var contribution = GetPnlBillingContributionForRow(record, verticalKey, rowMetadata.Key);
            var legalContribution = GetPnlBillingContributionForRow(
                record,
                verticalKey,
                rowMetadata.Key,
                useLegalInvoiceValue: true);
            if (Math.Abs(contribution) < 0.01m && Math.Abs(legalContribution) < 0.01m)
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

        foreach (var record in scopedManualRecords)
        {
            if (string.Equals(record.TypeKey, PnlManualItemRebateKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var contribution = GetPnlManualContributionForRow(record, rowMetadata.Key);
            if (Math.Abs(contribution) < 0.01m)
                continue;

            records.Add(BuildPnlManualDetailRecord(record, contribution));
        }

        foreach (var record in scopedRebateRecords)
        {
            var contribution = GetPnlRebateContributionForRow(record, rowMetadata.Key);
            if (Math.Abs(contribution) < 0.01m)
                continue;

            records.Add(BuildPnlRebateDetailRecord(record, contribution));
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
        var requestedCloudValue = NormalizeEditablePnlAllocationValue(request.CloudValue, "Cloud");
        var requestedCopiersValue = NormalizeEditablePnlAllocationValue(request.CopiersValue, "Copiers");
        var hasExplicitAllocation = requestedCloudValue.HasValue || requestedCopiersValue.HasValue;
        var finalCategoryOption = request.CategoryOptionValue ?? current.CategoryOptionValue;
        var finalCloudValue = requestedCloudValue ?? current.CloudValue;
        var finalCopiersValue = requestedCopiersValue ?? current.CopiersValue;
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

        if (finalCategoryOption != current.CategoryOptionValue)
        {
            payload[DashboardExpenseCategoryField] = finalCategoryOption;
        }

        var shouldRewriteAllocation = requestedVerticalKey is not null && !hasExplicitAllocation;
        if (IsPnlExpensePersonalCategory(finalCategoryOption))
        {
            shouldRewriteAllocation = true;
        }

        if (shouldRewriteAllocation)
        {
            var allocationVerticalKey = requestedVerticalKey ?? ResolvePnlExpenseEditorVerticalKey(current);
            if (allocationVerticalKey is DashboardPnlVerticalCloud or DashboardPnlVerticalCopiers)
            {
                var allocationValue = GetPnlExpenseAllocationReferenceValue(current);
                finalCloudValue = allocationVerticalKey == DashboardPnlVerticalCloud ? allocationValue : 0m;
                finalCopiersValue = allocationVerticalKey == DashboardPnlVerticalCopiers ? allocationValue : 0m;
            }
        }

        finalCloudValue = RoundCurrency(finalCloudValue);
        finalCopiersValue = RoundCurrency(finalCopiersValue);

        if (finalCloudValue != current.CloudValue)
        {
            payload[DashboardExpenseCloudField] = finalCloudValue;
        }

        if (finalCopiersValue != current.CopiersValue)
        {
            payload[DashboardExpenseCopiersField] = finalCopiersValue;
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
        Exception? lastError = null;
        foreach (var dateField in DashboardExpenseEmissionDateFieldCandidates)
        {
            var select = string.Join(",", new[]
            {
                _supplierExpensesIdField,
                dateField.FieldName,
                DashboardExpensePaymentValueField,
                DashboardExpenseCloudField,
                DashboardExpenseCopiersField,
                DashboardExpenseCategoryField,
                DashboardExpenseIssuerNameField,
                DashboardExpenseRecipientNameField,
                DashboardExpenseTotalField,
                DashboardExpenseVatField,
                DashboardExpenseTotalBeforeVatField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

            var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}({recordId})?$select={select}";
            try
            {
                var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
                using var doc = JsonDocument.Parse(json);
                return ParsePnlExpenseRow(doc.RootElement, dateField.FieldName);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastError = ex;
            }
        }

        try
        {
            var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}({recordId})";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            using var doc = JsonDocument.Parse(json);
            return ParsePnlExpenseRow(doc.RootElement, DashboardExpenseEmissionDateField);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("No fue posible leer el gasto P&L seleccionado desde Dataverse.", lastError ?? ex);
        }
    }

    private async Task<List<PnlExpenseRow>> GetPnlExpenseRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var dateField in DashboardExpenseEmissionDateFieldCandidates)
        {
            var fullSelect = string.Join(",", new[]
            {
                _supplierExpensesIdField,
                dateField.FieldName,
                DashboardExpensePaymentValueField,
                DashboardExpenseCloudField,
                DashboardExpenseCopiersField,
                DashboardExpenseCategoryField,
                DashboardExpenseIssuerNameField,
                DashboardExpenseRecipientNameField,
                DashboardExpenseTotalField,
                DashboardExpenseVatField,
                DashboardExpenseTotalBeforeVatField
            }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

            var filter = BuildBillingDateFilter(
                dateField.FieldName,
                dateField.FieldKind,
                startInclusive,
                endExclusive);

            var fullRelativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$select={fullSelect}&$filter={Uri.EscapeDataString(filter)}&$orderby={dateField.FieldName} asc";
            var fallbackRelativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$filter={Uri.EscapeDataString(filter)}&$orderby={dateField.FieldName} asc";

            IReadOnlyList<JsonElement> items;
            try
            {
                items = await GetDataverseEntitiesAsync(fullRelativeUrl, user, ct, AddFormattedValueHeaders);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastError = ex;
                try
                {
                    items = await GetDataverseEntitiesAsync(fallbackRelativeUrl, user, ct, AddFormattedValueHeaders);
                }
                catch (Exception fallbackEx) when (!ct.IsCancellationRequested)
                {
                    lastError = fallbackEx;
                    continue;
                }
            }

            return items
                .Select(item => ParsePnlExpenseRow(item, dateField.FieldName))
                .Where(static row => row is not null)
                .Cast<PnlExpenseRow>()
                .GroupBy(row => row.RecordId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        throw new InvalidOperationException(
            "No fue posible cargar gastos del P&L por Fecha de Emision. Valida que exista una columna de fecha de emision en la tabla de gastos.",
            lastError);
    }

    private PnlExpenseRow? ParsePnlExpenseRow(JsonElement item, string emissionDateField)
    {
        var categoryOptionValue = ReadInt(item, DashboardExpenseCategoryField);
        var recordId = FirstNonEmpty(
            ReadString(item, _supplierExpensesIdField),
            $"{categoryOptionValue}|{ReadString(item, emissionDateField)}|{ReadString(item, DashboardExpenseTotalField)}|{ReadString(item, DashboardExpensePaymentValueField)}");

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var totalValue = RoundCurrency(ReadFirstPnlDecimal(
            item,
            DashboardExpenseTotalField,
            "cr07a_totalfactura",
            "total_factura",
            "TOTAL_FACTURA",
            "Total Factura") ?? 0m);
        var vatValue = RoundCurrency(ReadFirstPnlDecimal(
            item,
            DashboardExpenseVatField,
            "cr07a_ivavalor",
            "iva_valor",
            "IVA_Valor",
            "IVA VALOR",
            "Valor IVA") ?? 0m);
        var totalBeforeVatValue = RoundCurrency(
            ReadFirstPnlDecimal(
                item,
                DashboardExpenseTotalBeforeVatField,
                "cr07a_base",
                "base",
                "Base")
            ?? (totalValue != 0m || vatValue != 0m ? totalValue - vatValue : 0m));

        return new PnlExpenseRow
        {
            RecordId = recordId.Trim(),
            EmissionDate = ReadDateOnly(item, emissionDateField),
            PaymentValue = RoundCurrency(ReadDecimal(item, DashboardExpensePaymentValueField) ?? 0m),
            IssuerName = ReadString(item, DashboardExpenseIssuerNameField).Trim(),
            RecipientName = ReadString(item, DashboardExpenseRecipientNameField).Trim(),
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

    private static decimal? ReadFirstPnlDecimal(JsonElement item, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var value = ReadDecimal(item, fieldName);
            if (value.HasValue)
                return value;
        }

        return null;
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
        IReadOnlyList<decimal> primasCesantias,
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
        IReadOnlyList<decimal> financialIncome,
        IReadOnlyList<decimal> financialExpenses,
        IReadOnlyList<decimal> otherNonOperating,
        IReadOnlyList<decimal> totalOtherIncomeExpenses,
        IReadOnlyList<decimal> incomeBeforeTaxes,
        IReadOnlyList<decimal> taxes,
        IReadOnlyList<decimal> netIncome)
    {
        var rows = new[]
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
            BuildPnlValueRow("cogs-technical-service", "Servicio Tecnico", "detail", 1, technicalService),
            BuildPnlValueRow("cogs-total", "COGS (total)", "subtotal", 1, cogs),

            BuildPnlSection("section-gross-profit", "3. Utilidad Bruta", 0),
            BuildPnlValueRow("gross-profit", "UTILIDAD BRUTA", "formula", 1, grossProfit),

            BuildPnlSection("section-operating-expenses", "4. Gastos Operacionales", 0),
            BuildPnlSection("section-personal", "4.1 Gastos de personal", 1),
            BuildPnlValueRow("personal-administrative", "Personal Administrativo", "detail", 2, personalAdministrative),
            BuildPnlValueRow("personal-primas-cesantias", "Primas/Cesantias", "detail", 2, primasCesantias),
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
            BuildPnlValueRow("other-financial-income", "Ingresos financieros", "detail", 1, financialIncome),
            BuildPnlValueRow("other-financial-expenses", "Gastos financieros", "detail", 1, financialExpenses),
            BuildPnlValueRow("other-non-operating", "Otros ingresos/gastos no operacionales", "detail", 1, otherNonOperating),
            BuildPnlValueRow("other-total", "Total otros ingresos/gastos", "subtotal", 1, totalOtherIncomeExpenses),

            BuildPnlSection("section-income-before-taxes", "7. Utilidad antes de impuestos", 0),
            BuildPnlValueRow("income-before-taxes", "UTILIDAD ANTES DE IMPUESTOS", "formula", 1, incomeBeforeTaxes),

            BuildPnlSection("section-taxes", "8. Impuestos", 0),
            BuildPnlValueRow("taxes", "Impuestos", "detail", 1, taxes),

            BuildPnlSection("section-net-income", "9. Utilidad neta", 0),
            BuildPnlValueRow("net-income", "UTILIDAD NETA", "formula", 1, netIncome)
        };

        ApplyPnlRevenuePercentages(rows, operatingRevenue);
        return rows;
    }

    private static IReadOnlyList<PnlOrphanRowDto> BuildPnlOrphanRows(
        int monthCutoff,
        IReadOnlyList<BillingRecordRow> billingRecords,
        IReadOnlyList<PnlExpenseRow> expenseRecords)
    {
        return new[]
        {
            BuildPnlOrphanRow(
                "orphan-billing-no-vertical",
                "Facturacion sin vertical",
                "Facturas emitidas sin Cloud o Copiers.",
                BuildPnlBillingOrphanSeries(monthCutoff, billingRecords, IsPnlBillingMissingVertical)),
            BuildPnlOrphanRow(
                "orphan-expense-no-category",
                "Gastos sin categoria",
                "Gastos que todavia no tienen categoria asignada.",
                BuildPnlExpenseOrphanSeries(monthCutoff, expenseRecords, IsPnlExpenseMissingCategory)),
            BuildPnlOrphanRow(
                "orphan-expense-allocation-mismatch",
                "Gastos con reparto invalido",
                "Gastos donde ni Cloud ni Copiers tienen valor asignado.",
                BuildPnlExpenseOrphanSeries(monthCutoff, expenseRecords, IsPnlExpenseAllocationMismatch))
        };
    }

    private static PnlOrphanRowDto BuildPnlOrphanRow(
        string key,
        string label,
        string hint,
        IReadOnlyList<int> values)
    {
        return new PnlOrphanRowDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Values = values,
            Total = values.Sum()
        };
    }

    private static IReadOnlyList<int> BuildPnlBillingOrphanSeries(
        int monthCutoff,
        IReadOnlyList<BillingRecordRow> rows,
        Func<BillingRecordRow, bool> predicate)
    {
        var values = new int[Math.Clamp(monthCutoff, 1, 12)];
        foreach (var row in rows)
        {
            if (row.EmissionDate is null || !predicate(row))
                continue;

            var month = row.EmissionDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            values[month - 1] += 1;
        }

        return values;
    }

    private static IReadOnlyList<int> BuildPnlExpenseOrphanSeries(
        int monthCutoff,
        IReadOnlyList<PnlExpenseRow> rows,
        Func<PnlExpenseRow, bool> predicate)
    {
        var values = new int[Math.Clamp(monthCutoff, 1, 12)];
        foreach (var row in rows)
        {
            if (row.EmissionDate is null || !predicate(row))
                continue;

            var month = row.EmissionDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            values[month - 1] += 1;
        }

        return values;
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

    private static void ApplyPnlRevenuePercentages(IReadOnlyList<PnlRowDto> rows, IReadOnlyList<decimal> operatingRevenue)
    {
        var operatingRevenueTotal = SumPnlSeriesTotal(operatingRevenue);
        foreach (var row in rows)
        {
            if (string.Equals(row.RowType, "section", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.ValueFormat, "currency", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            row.Percentages = row.Values
                .Select((value, index) =>
                {
                    var denominator = index < operatingRevenue.Count ? operatingRevenue[index] : 0m;
                    return CalculatePnlRevenuePercentage(value, denominator);
                })
                .ToList();
            row.TotalPercentage = CalculatePnlRevenuePercentage(row.Total, operatingRevenueTotal);
        }
    }

    private static decimal CalculatePnlRevenuePercentage(decimal value, decimal operatingRevenue)
    {
        if (Math.Abs(operatingRevenue) < 0.01m)
            return 0m;

        return RoundCurrency((value / operatingRevenue) * 100m);
    }

    private static int? ResolvePnlCellMonth(int? requestedMonth, int resolvedMonthCutoff)
    {
        if (!requestedMonth.HasValue)
            return null;

        return Math.Clamp(requestedMonth.Value, 1, resolvedMonthCutoff);
    }

    private static bool IsPnlOrphanRow(string rowKey) => rowKey switch
    {
        "orphan-billing-no-vertical" => true,
        "orphan-expense-no-category" => true,
        "orphan-expense-allocation-mismatch" => true,
        _ => false
    };

    private static PnlCellDetailDto BuildPnlOrphanCellDetail(
        int year,
        int monthCutoff,
        int? cellMonth,
        PnlRowMetadata rowMetadata,
        IReadOnlyList<BillingRecordRow> billingRecords,
        IReadOnlyList<PnlExpenseRow> expenseRecords)
    {
        var scopedBillingRecords = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == year
                && record.EmissionDate.Value.Month <= monthCutoff
                && (!cellMonth.HasValue || record.EmissionDate.Value.Month == cellMonth.Value))
            .ToList();

        var scopedExpenseRecords = expenseRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == year
                && record.EmissionDate.Value.Month <= monthCutoff
                && (!cellMonth.HasValue || record.EmissionDate.Value.Month == cellMonth.Value))
            .ToList();

        List<PnlCellDetailRecordDto> records = rowMetadata.Key switch
        {
            "orphan-billing-no-vertical" => scopedBillingRecords
                .Where(IsPnlBillingMissingVertical)
                .Select(record => BuildPnlBillingDetailRecord(record, 1m, "Facturacion sin vertical"))
                .ToList(),
            "orphan-expense-no-category" => scopedExpenseRecords
                .Where(IsPnlExpenseMissingCategory)
                .Select(record => BuildPnlExpenseDetailRecord(record, 1m, "Gasto sin categoria"))
                .ToList(),
            "orphan-expense-allocation-mismatch" => scopedExpenseRecords
                .Where(IsPnlExpenseAllocationMismatch)
                .Select(record => BuildPnlExpenseDetailRecord(record, 1m, "Gasto sin valor en Cloud/Copiers"))
                .ToList(),
            _ => new List<PnlCellDetailRecordDto>()
        };

        var orderedRecords = records
            .OrderByDescending(record => Math.Max(Math.Abs(record.TotalInvoice), Math.Abs(record.PaymentValue)))
            .ThenBy(record => record.DateDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.DocumentNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PnlCellDetailDto
        {
            Year = year,
            MonthCutoff = monthCutoff,
            CellMonth = cellMonth,
            RowKey = rowMetadata.Key,
            RowLabel = rowMetadata.Label,
            CellLabel = ResolvePnlCellLabel(year, monthCutoff, cellMonth),
            VerticalKey = DashboardPnlVerticalAll,
            VerticalLabel = "",
            ValueFormat = rowMetadata.ValueFormat,
            Total = orderedRecords.Count,
            RecordsCount = orderedRecords.Count,
            EmptyMessage = BuildPnlDetailEmptyMessage(rowMetadata.Key),
            VerticalOptions = BuildPnlVerticalOptions(),
            CategoryOptions = BuildPnlCategoryOptions(),
            Records = orderedRecords
        };
    }

    private static decimal GetPnlBillingContributionForRow(
        BillingRecordRow row,
        string verticalKey,
        string rowKey,
        bool useLegalInvoiceValue = false)
    {
        var cloudAmount = useLegalInvoiceValue
            ? GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCloudOption)
            : GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCloudOption);
        var copiersAmount = useLegalInvoiceValue
            ? GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption)
            : GetPnlRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption);

        return rowKey switch
        {
            "income-cloud" => cloudAmount,
            "income-copiers" => copiersAmount,
            "income-total" or "gross-profit" or "ebitda" or "income-before-taxes" or "net-income" => RoundCurrency(cloudAmount + copiersAmount),
            _ => 0m
        };
    }

    private static decimal GetPnlExpenseContributionForRow(
        PnlExpenseRow row,
        string verticalKey,
        string rowKey)
    {
        var amount = GetPnlExpenseViewAmount(row, verticalKey);
        if (Math.Abs(amount) < 0.01m)
            return 0m;

        var bucketKey = ResolvePnlExpenseBucketKey(row);
        var pnlAmount = amount;
        var otherSignedAmount = GetPnlOtherIncomeExpenseSignedAmount(row, amount, bucketKey);
        var incomeBeforeTaxContribution = ResolvePnlIncomeBeforeTaxExpenseContribution(bucketKey, pnlAmount, otherSignedAmount);

        return rowKey switch
        {
            "cogs-licensing" => bucketKey == "licensing" ? pnlAmount : 0m,
            "cogs-rebates" => 0m,
            "cogs-supplies" => bucketKey == "supplies" ? pnlAmount : 0m,
            "cogs-machines" => bucketKey == "machines" ? pnlAmount : 0m,
            "cogs-technical-service" => bucketKey == "technical-service" ? pnlAmount : 0m,
            "cogs-total" => IsPnlCogsBucket(bucketKey) ? pnlAmount : 0m,
            "gross-profit" => IsPnlCogsBucket(bucketKey) ? RoundCurrency(-pnlAmount) : 0m,
            "personal-administrative" => bucketKey == "personal-administrative" ? pnlAmount : 0m,
            "personal-primas-cesantias" => bucketKey == PnlExpensePrimasCesantiasBucket ? pnlAmount : 0m,
            "personal-cloud" => bucketKey == "personal-cloud" ? pnlAmount : 0m,
            "personal-copiers" => bucketKey == "personal-copiers" ? pnlAmount : 0m,
            "personal-total" => IsPnlPersonalBucket(bucketKey) ? pnlAmount : 0m,
            "admin-office-rent" => bucketKey == "office-rent" ? pnlAmount : 0m,
            "admin-warehouse" => bucketKey == "warehouse" ? pnlAmount : 0m,
            "admin-transport" => bucketKey == "transport" ? pnlAmount : 0m,
            "admin-internal" => bucketKey == "internal" ? pnlAmount : 0m,
            "admin-recurring" => bucketKey == "recurring" ? pnlAmount : 0m,
            "admin-equipment" => bucketKey == "equipment" ? pnlAmount : 0m,
            "admin-travel" => bucketKey == "travel" ? pnlAmount : 0m,
            "admin-empty" => bucketKey == "empty" ? pnlAmount : 0m,
            "admin-total" => IsPnlAdministrativeBucket(bucketKey) ? pnlAmount : 0m,
            "commercial-marketing" => bucketKey == "marketing" ? pnlAmount : 0m,
            "commercial-total" => bucketKey == "marketing" ? pnlAmount : 0m,
            "ebitda" => IsPnlEbitdaExpenseBucket(bucketKey) ? RoundCurrency(-pnlAmount) : 0m,
            "other-financial-income" => bucketKey == PnlExpenseFinancialIncomeBucket ? pnlAmount : 0m,
            "other-financial-expenses" => bucketKey == PnlExpenseFinancialExpenseBucket ? pnlAmount : 0m,
            "other-non-operating" => bucketKey == PnlExpenseOtherNonOperatingBucket ? otherSignedAmount : 0m,
            "other-total" => IsPnlOtherIncomeExpenseBucket(bucketKey) ? otherSignedAmount : 0m,
            "income-before-taxes" => incomeBeforeTaxContribution,
            "taxes" => bucketKey == "taxes" ? pnlAmount : 0m,
            "net-income" => bucketKey == "taxes" ? RoundCurrency(-pnlAmount) : incomeBeforeTaxContribution,
            _ => 0m
        };
    }

    private static bool IsPnlCogsBucket(string bucketKey) =>
        bucketKey is "licensing" or "supplies" or "machines" or "technical-service";

    private static bool IsPnlPersonalBucket(string bucketKey) =>
        bucketKey is "personal-administrative" or PnlExpensePrimasCesantiasBucket or "personal-cloud" or "personal-copiers";

    private static bool IsPnlAdministrativeBucket(string bucketKey) =>
        bucketKey is "office-rent" or "warehouse" or "transport" or "internal" or "recurring" or "equipment" or "travel" or "empty";

    private static bool IsPnlEbitdaExpenseBucket(string bucketKey) =>
        IsPnlCogsBucket(bucketKey) || IsPnlPersonalBucket(bucketKey) || IsPnlAdministrativeBucket(bucketKey) || bucketKey == "marketing";

    private static bool IsPnlOtherIncomeExpenseBucket(string bucketKey) =>
        bucketKey is PnlExpenseFinancialIncomeBucket or PnlExpenseFinancialExpenseBucket or PnlExpenseOtherNonOperatingBucket;

    private static decimal GetPnlManualContributionForRow(PnlManualRecord record, string rowKey)
    {
        var amount = record.Value;
        if (Math.Abs(amount) < 0.01m)
            return 0m;

        return record.TypeKey switch
        {
            PnlManualItemRebateKey => rowKey switch
            {
                "cogs-rebates" or "cogs-total" => amount,
                "gross-profit" or "ebitda" or "income-before-taxes" or "net-income" => RoundCurrency(-amount),
                _ => 0m
            },
            PnlManualItemFinancialIncomeKey => rowKey switch
            {
                "other-financial-income" or "other-total" or "income-before-taxes" or "net-income" => amount,
                _ => 0m
            },
            _ => 0m
        };
    }

    private static decimal GetPnlRebateContributionForRow(SharePointRebateRecord record, string rowKey)
    {
        var amount = record.Value;
        if (Math.Abs(amount) < 0.01m)
            return 0m;

        return rowKey switch
        {
            "cogs-rebates" or "cogs-total" => amount,
            "gross-profit" or "ebitda" or "income-before-taxes" or "net-income" => RoundCurrency(-amount),
            _ => 0m
        };
    }

    private static decimal GetPnlOtherIncomeExpenseSignedAmount(PnlExpenseRow row, decimal amount, string bucketKey)
    {
        return bucketKey switch
        {
            PnlExpenseFinancialIncomeBucket => amount,
            PnlExpenseFinancialExpenseBucket => RoundCurrency(-amount),
            PnlExpenseOtherNonOperatingBucket => IsPnlIncomeLabel(row.CategoryLabel) ? amount : RoundCurrency(-amount),
            _ => 0m
        };
    }

    private static decimal ResolvePnlIncomeBeforeTaxExpenseContribution(string bucketKey, decimal pnlAmount, decimal otherSignedAmount)
    {
        if (bucketKey == "taxes")
            return 0m;

        if (IsPnlEbitdaExpenseBucket(bucketKey))
            return RoundCurrency(-pnlAmount);

        if (IsPnlOtherIncomeExpenseBucket(bucketKey))
            return otherSignedAmount;

        return 0m;
    }

    private static PnlCellDetailRecordDto BuildPnlBillingDetailRecord(BillingRecordRow row, decimal cellValue, string sourceLabel = "Facturacion")
    {
        var verticalKey = ResolvePnlVerticalKeyFromOptionValue(row.VerticalOptionValue);
        return new PnlCellDetailRecordDto
        {
            SourceType = "billing",
            SourceLabel = sourceLabel,
            RecordId = row.RecordId,
            DocumentNumber = row.InvoiceNumber,
            Description = row.ClientName,
            DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
            AssignedMonthDisplay = ResolvePnlAssignedMonthDisplay(row.EmissionDate),
            VerticalKey = verticalKey,
            VerticalLabel = string.IsNullOrWhiteSpace(row.VerticalLabel) ? ResolvePnlDetailVerticalLabel(verticalKey) : row.VerticalLabel,
            CategoryLabel = "No aplica",
            TotalInvoice = row.TotalInvoice,
            VatValue = row.VatValue,
            TotalBeforeVatValue = GetPnlBillingLegalBaseValue(row),
            PaymentValue = row.PaymentValue,
            CloudValue = 0m,
            CopiersValue = 0m,
            CellValue = RoundCurrency(cellValue),
            CanEditVertical = true,
            CanEditCategory = false,
            CanEditAllocation = false
        };
    }

    private static PnlCellDetailRecordDto BuildPnlExpenseDetailRecord(PnlExpenseRow row, decimal cellValue, string sourceLabel = "Gasto")
    {
        var verticalKey = ResolvePnlExpenseEditorVerticalKey(row);
        return new PnlCellDetailRecordDto
        {
            SourceType = "expense",
            SourceLabel = sourceLabel,
            RecordId = row.RecordId,
            DocumentNumber = row.RecordId,
            Description = string.IsNullOrWhiteSpace(row.IssuerName) ? row.CategoryLabel : row.IssuerName,
            DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
            AssignedMonthDisplay = ResolvePnlAssignedMonthDisplay(row.EmissionDate),
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
            CanEditCategory = true,
            CanEditAllocation = true
        };
    }

    private static PnlCellDetailRecordDto BuildPnlManualDetailRecord(PnlManualRecord record, decimal cellValue)
    {
        return new PnlCellDetailRecordDto
        {
            SourceType = "manual",
            SourceLabel = "Manual",
            RecordId = record.RecordId,
            DocumentNumber = record.RecordId,
            Description = record.TypeLabel,
            DateDisplay = record.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
            AssignedMonthDisplay = ResolvePnlAssignedMonthDisplay(record.Date),
            VerticalKey = "none",
            VerticalLabel = "Consolidado",
            CategoryLabel = record.TypeLabel,
            TotalInvoice = record.Value,
            VatValue = 0m,
            TotalBeforeVatValue = record.Value,
            PaymentValue = record.Value,
            CloudValue = 0m,
            CopiersValue = 0m,
            CellValue = RoundCurrency(cellValue),
            CanEditVertical = false,
            CanEditCategory = false,
            CanEditAllocation = false
        };
    }

    private static PnlCellDetailRecordDto BuildPnlRebateDetailRecord(SharePointRebateRecord record, decimal cellValue)
    {
        return new PnlCellDetailRecordDto
        {
            SourceType = "sharepoint-rebate",
            SourceLabel = "SharePoint / Rebates",
            RecordId = record.RecordId,
            DocumentNumber = $"Fila {record.SourceRow}",
            Description = "Rebate desde Facturacion DIGITAL TECH.xlsx",
            DateDisplay = record.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            AssignedMonthDisplay = ResolvePnlAssignedMonthDisplay(record.Date),
            VerticalKey = "none",
            VerticalLabel = "Consolidado",
            CategoryLabel = "Rebates",
            TotalInvoice = record.Value,
            VatValue = 0m,
            TotalBeforeVatValue = record.Value,
            PaymentValue = record.Value,
            CloudValue = 0m,
            CopiersValue = 0m,
            CellValue = RoundCurrency(cellValue),
            CanEditVertical = false,
            CanEditCategory = false,
            CanEditAllocation = false
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
            "personal-primas-cesantias" => new PnlRowMetadata(normalizedKey, "Primas/Cesantias"),
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
            "other-financial-income" => new PnlRowMetadata(normalizedKey, "Ingresos financieros"),
            "other-financial-expenses" => new PnlRowMetadata(normalizedKey, "Gastos financieros"),
            "other-non-operating" => new PnlRowMetadata(normalizedKey, "Otros ingresos/gastos no operacionales"),
            "other-total" => new PnlRowMetadata(normalizedKey, "Total otros ingresos/gastos"),
            "income-before-taxes" => new PnlRowMetadata(normalizedKey, "UTILIDAD ANTES DE IMPUESTOS"),
            "taxes" => new PnlRowMetadata(normalizedKey, "Impuestos"),
            "net-income" => new PnlRowMetadata(normalizedKey, "UTILIDAD NETA"),
            "orphan-billing-no-vertical" => new PnlRowMetadata(normalizedKey, "Facturacion sin vertical", "number"),
            "orphan-expense-no-category" => new PnlRowMetadata(normalizedKey, "Gastos sin categoria", "number"),
            "orphan-expense-allocation-mismatch" => new PnlRowMetadata(normalizedKey, "Gastos con reparto invalido", "number"),
            _ => throw new InvalidOperationException("La fila seleccionada no existe dentro de la estructura del P&L.")
        };
    }

    private static string ResolvePnlCellLabel(int year, int monthCutoff, int? cellMonth)
    {
        if (cellMonth.HasValue)
            return $"{ResolvePnlMonthLabel(year, cellMonth.Value)} {year}";

        return $"Total acumulado a {ResolvePnlMonthLabel(year, monthCutoff)} {year}";
    }

    private static string ResolvePnlAssignedMonthDisplay(DateOnly? emissionDate)
    {
        if (!emissionDate.HasValue)
            return "-";

        return $"{ResolvePnlMonthLabel(emissionDate.Value.Year, emissionDate.Value.Month)} {emissionDate.Value.Year}";
    }

    private static string BuildPnlDetailEmptyMessage(string rowKey) => rowKey switch
    {
        "cogs-rebates" => "No encontramos rebates en la tabla Rebates de Facturacion DIGITAL TECH.xlsx para esta celda.",
        "other-financial-income" => "No encontramos ingresos financieros manuales para esta celda.",
        "orphan-billing-no-vertical" => "No encontramos facturas sin vertical para este corte.",
        "orphan-expense-no-category" => "No encontramos gastos sin categoria para este corte.",
        "orphan-expense-allocation-mismatch" => "No encontramos gastos sin valor asignado en Cloud y Copiers para este corte.",
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

    private static bool IsPnlBillingMissingVertical(BillingRecordRow row) =>
        !IsKnownDashboardVertical(row.VerticalOptionValue);

    private static bool IsKnownDashboardVertical(int optionValue) =>
        optionValue is DashboardVerticalCloudOption or DashboardVerticalCopiersOption;

    private static bool IsPnlExpenseMissingCategory(PnlExpenseRow row) =>
        row.CategoryOptionValue <= 0;

    private static bool IsPnlExpenseAllocationMismatch(PnlExpenseRow row)
    {
        return row.CloudValue <= 0m && row.CopiersValue <= 0m;
    }

    private static decimal? NormalizeEditablePnlAllocationValue(decimal? value, string fieldName)
    {
        if (!value.HasValue)
            return null;

        var rounded = RoundCurrency(value.Value);
        if (rounded < 0m)
            throw new InvalidOperationException($"El valor de {fieldName} no puede ser negativo.");

        return rounded;
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
            if (row.EmissionDate is null)
                continue;

            var month = row.EmissionDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            if (!string.Equals(ResolvePnlExpenseBucketKey(row), bucketKey, StringComparison.OrdinalIgnoreCase))
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + GetPnlExpenseViewAmount(row, verticalKey));
        }

        return values;
    }

    private static IReadOnlyList<decimal> BuildPnlManualSeries(
        int monthCutoff,
        IReadOnlyList<PnlManualRecord> records,
        string typeKey)
    {
        var values = new decimal[Math.Clamp(monthCutoff, 1, 12)];

        foreach (var record in records)
        {
            if (record.Date is null || !string.Equals(record.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                continue;

            var month = record.Date.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + record.Value);
        }

        return values;
    }

    internal static IReadOnlyList<decimal> BuildPnlRebateSeries(
        int monthCutoff,
        IReadOnlyList<SharePointRebateRecord> records)
    {
        var values = new decimal[Math.Clamp(monthCutoff, 1, 12)];
        foreach (var record in records)
        {
            var month = record.Date.Month;
            if (month > values.Length)
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + record.Value);
        }
        return values;
    }

    private static IReadOnlyList<decimal> BuildPnlOtherNonOperatingSeries(
        int monthCutoff,
        IReadOnlyList<PnlExpenseRow> rows,
        string verticalKey)
    {
        var values = new decimal[Math.Clamp(monthCutoff, 1, 12)];

        foreach (var row in rows)
        {
            if (row.EmissionDate is null)
                continue;

            var month = row.EmissionDate.Value.Month;
            if (month is < 1 or > 12 || month > values.Length)
                continue;

            var bucketKey = ResolvePnlExpenseBucketKey(row);
            if (!string.Equals(bucketKey, PnlExpenseOtherNonOperatingBucket, StringComparison.OrdinalIgnoreCase))
                continue;

            values[month - 1] = RoundCurrency(values[month - 1] + GetPnlOtherIncomeExpenseSignedAmount(row, GetPnlExpenseViewAmount(row, verticalKey), bucketKey));
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

    private static IReadOnlyList<decimal> NegatePnlSeries(IReadOnlyList<decimal> series) =>
        series.Select(value => RoundCurrency(-value)).ToList();

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
            Math.Abs(GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCloudOption)) >= 0.01m
            || Math.Abs(GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption)) >= 0.01m);
    }

    private static int CountPnlRelevantExpenseRecords(IEnumerable<PnlExpenseRow> rows, string verticalKey)
    {
        return rows.Count(row => Math.Abs(GetPnlExpenseViewAmount(row, verticalKey)) >= 0.01m);
    }

    private static int CountPnlRelevantManualRecords(IEnumerable<PnlManualRecord> records)
    {
        return records.Count(record =>
            !string.Equals(record.TypeKey, PnlManualItemRebateKey, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(record.Value) >= 0.01m);
    }

    private static decimal GetPnlBillingBaseValue(BillingRecordRow row)
        => RoundCurrency(row.NetBeforeVatValue);

    private static decimal GetPnlBillingLegalBaseValue(BillingRecordRow row)
    {
        if (Math.Abs(row.TotalInvoice) < 0.01m && Math.Abs(row.VatValue) < 0.01m)
            return 0m;

        return RoundCurrency(row.TotalInvoice - row.VatValue);
    }

    private static decimal GetPnlRevenueAmount(BillingRecordRow row, string verticalKey, int targetVerticalOption)
        => GetPnlRevenueAmountForBase(
            row,
            verticalKey,
            targetVerticalOption,
            GetPnlBillingBaseValue(row));

    private static decimal GetPnlLegalRevenueAmount(BillingRecordRow row, string verticalKey, int targetVerticalOption)
        => GetPnlRevenueAmountForBase(
            row,
            verticalKey,
            targetVerticalOption,
            GetPnlBillingLegalBaseValue(row));

    private static decimal GetPnlRevenueAmountForBase(
        BillingRecordRow row,
        string verticalKey,
        int targetVerticalOption,
        decimal baseValue)
    {
        if (row.VerticalOptionValue != targetVerticalOption)
            return 0m;

        if (!MatchesPnlVerticalSelection(targetVerticalOption, verticalKey))
            return 0m;

        return baseValue;
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

        return 0m;
    }

    private static decimal GetPnlExpenseAllocationReferenceValue(PnlExpenseRow row)
    {
        return GetPnlExpenseBaseValue(row);
    }

    private static string ResolvePnlExpenseBucketKey(PnlExpenseRow row)
    {
        var labelBucket = ResolvePnlExpenseBucketKeyFromLabel(row.CategoryLabel);
        if (labelBucket is PnlExpensePrimasCesantiasBucket or PnlExpenseFinancialIncomeBucket or PnlExpenseFinancialExpenseBucket or PnlExpenseOtherNonOperatingBucket)
            return labelBucket;

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
            PnlExpenseFinancialOption => PnlExpenseFinancialExpenseBucket,
            _ => labelBucket
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

        if (normalized.Contains("prima", StringComparison.Ordinal) || normalized.Contains("cesant", StringComparison.Ordinal))
            return PnlExpensePrimasCesantiasBucket;

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

        if (normalized.Contains("financ", StringComparison.Ordinal))
            return normalized.Contains("ingreso", StringComparison.Ordinal)
                ? PnlExpenseFinancialIncomeBucket
                : PnlExpenseFinancialExpenseBucket;

        if (normalized.Contains("no operacional", StringComparison.Ordinal)
            || normalized.Contains("no operacion", StringComparison.Ordinal)
            || (normalized.Contains("otro", StringComparison.Ordinal) && (normalized.Contains("ingreso", StringComparison.Ordinal) || normalized.Contains("gasto", StringComparison.Ordinal))))
            return PnlExpenseOtherNonOperatingBucket;

        if (normalized.Contains("contab", StringComparison.Ordinal))
            return PnlExpenseFinancialExpenseBucket;

        return "empty";
    }

    private static int ResolveLatestPnlMonthAvailable(
        int year,
        DateOnly today,
        string verticalKey,
        IReadOnlyList<BillingRecordRow> billingRecords,
        IReadOnlyList<PnlExpenseRow> expenseRecords,
        IReadOnlyList<PnlManualRecord> manualRecords,
        IReadOnlyList<SharePointRebateRecord> rebateRecords)
    {
        var months = new List<int>();

        months.AddRange(
            billingRecords
                .Where(row => row.EmissionDate is not null
                    && row.EmissionDate.Value.Year == year
                    && (Math.Abs(GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCloudOption)) >= 0.01m
                        || Math.Abs(GetPnlLegalRevenueAmount(row, verticalKey, DashboardVerticalCopiersOption)) >= 0.01m))
                .Select(row => row.EmissionDate!.Value.Month));

        months.AddRange(
            expenseRecords
                .Where(row => row.EmissionDate is not null
                    && row.EmissionDate.Value.Year == year
                    && Math.Abs(GetPnlExpenseViewAmount(row, verticalKey)) >= 0.01m)
                .Select(row => row.EmissionDate!.Value.Month));

        months.AddRange(
            manualRecords
                .Where(record => record.Date is not null
                    && record.Date.Value.Year == year
                    && !string.Equals(record.TypeKey, PnlManualItemRebateKey, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(record.Value) >= 0.01m)
                .Select(record => record.Date!.Value.Month));

        months.AddRange(
            rebateRecords
                .Where(record => record.Date.Year == year && Math.Abs(record.Value) >= 0.01m)
                .Select(record => record.Date.Month));

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

    private static bool IsPnlIncomeLabel(string? value)
    {
        var normalized = NormalizePnlLabel(value);
        return normalized.Contains("ingreso", StringComparison.Ordinal)
            || normalized.Contains("reintegro", StringComparison.Ordinal)
            || normalized.Contains("recuperacion", StringComparison.Ordinal);
    }

    private sealed record PnlRowMetadata(string Key, string Label, string ValueFormat = "currency");

    private sealed record PnlExpenseDateFieldCandidate(string FieldName, string FieldKind);

    private sealed class PnlExpenseRow
    {
        public string RecordId { get; set; } = "";
        public DateOnly? EmissionDate { get; set; }
        public decimal PaymentValue { get; set; }
        public string IssuerName { get; set; } = "";
        public string RecipientName { get; set; } = "";
        public int CategoryOptionValue { get; set; }
        public string CategoryLabel { get; set; } = "";
        public decimal TotalValue { get; set; }
        public decimal VatValue { get; set; }
        public decimal TotalBeforeVatValue { get; set; }
        public decimal CloudValue { get; set; }
        public decimal CopiersValue { get; set; }
    }
}
