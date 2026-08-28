using System.Globalization;
using System.Security.Claims;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string YtdUnassignedClientKey = "";
    private const string YtdUnassignedVerticalKey = "sin-vertical";
    private const string YtdUnassignedVerticalLabel = "Sin vertical";
    private const string YtdLicensingCategoryKey = "licensing";
    private const string YtdLicensingCategoryLabel = "Licenciamiento";
    private const string YtdRebatesCategoryKey = "rebates";
    private const string YtdRebatesCategoryLabel = "Rebates";
    private const decimal YtdXcbMinimumInvoiceValue = 100_000_000m;

    public async Task<YtdDashboardDto> GetYtdDashboardAsync(int year, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var resolvedYear = year is >= 2000 and <= 2100 ? year : today.Year;
        var monthCutoff = resolvedYear == today.Year ? today.Month : 12;
        monthCutoff = Math.Clamp(monthCutoff, 1, 12);

        var yearStart = new DateOnly(resolvedYear, 1, 1);
        var periodEndExclusive = new DateOnly(resolvedYear, monthCutoff, 1).AddMonths(1);

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var billingTask = GetSiigoRevenueLedgerRowsAsync(
            metadata,
            yearStart,
            periodEndExclusive,
            httpContext.User,
            ct);

        var expensesTask = GetPnlExpenseRowsAsync(
            yearStart,
            periodEndExclusive,
            httpContext.User,
            ct);

        var rebatesTask = _sharePointRebatesProvider.GetSnapshotAsync(ct);

        var licensingTask = GetLicenciamientoCruceDashboardAsync(
            resolvedYear,
            monthCutoff,
            "ytd",
            ct);
        await Task.WhenAll(billingTask, expensesTask, rebatesTask, licensingTask);

        var billingRecords = await billingTask;
        var expenseRecords = await expensesTask;
        var rebatesSnapshot = await rebatesTask;
        var licensingDashboard = await licensingTask;

        var scopedBilling = billingRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= monthCutoff)
            .ToList();
        var scopedExpenses = expenseRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value.Year == resolvedYear
                && record.EmissionDate.Value.Month <= monthCutoff)
            .ToList();
        var scopedRebates = rebatesSnapshot.Records
            .Where(record => record.Date.Year == resolvedYear
                && record.Date.Month <= monthCutoff)
            .ToList();
        var scopedLicensingRows = licensingDashboard.Rows
            .Where(row => IsYtdLicensingRowInPeriod(row, resolvedYear, monthCutoff))
            .ToList();

        var revenueContributions = BuildYtdRevenueContributions(scopedBilling, scopedRebates);
        var expenseBuild = BuildYtdExpenseContributions(
            scopedExpenses,
            scopedLicensingRows,
            resolvedYear,
            monthCutoff);

        var chart = BuildYtdChart(
            resolvedYear,
            monthCutoff,
            revenueContributions,
            expenseBuild.Contributions);

        var hasData = chart.HasData;
        var recordsCount = scopedBilling.Count + scopedExpenses.Count + scopedRebates.Count + scopedLicensingRows.Count;

        return new YtdDashboardDto
        {
            Year = resolvedYear,
            MonthCutoff = monthCutoff,
            PeriodLabel = $"YTD {resolvedYear}",
            DateRangeLabel = $"Enero {resolvedYear} - {ResolvePnlMonthLabel(resolvedYear, monthCutoff)} {resolvedYear}",
            FocusLabel = "Ingresos, gastos y utilidad por mes",
            HasData = hasData,
            RecordsCount = recordsCount,
            EmptyStateMessage = "No encontramos movimientos para construir el grafico YTD.",
            SourceWarning = rebatesSnapshot.Warning,
            Chart = chart,
            Charts = new[] { chart },
            RevenueFilters = BuildYtdRevenueFilters(revenueContributions),
            ExpenseFilters = BuildYtdExpenseFilters(revenueContributions, expenseBuild.Contributions),
            EditorOptions = BuildYtdEditorOptions(),
            LicensingReconciliation = expenseBuild.Reconciliation
        };
    }

    private static IReadOnlyList<YtdContribution> BuildYtdRevenueContributions(
        IReadOnlyList<BillingRecordRow> billingRecords,
        IReadOnlyList<SharePointRebateRecord> rebateRecords)
    {
        var contributions = new List<YtdContribution>();
        foreach (var row in billingRecords)
        {
            if (row.EmissionDate is null)
                continue;

            var amount = GetPnlBillingBaseValue(row);
            if (Math.Abs(GetPnlBillingLegalBaseValue(row)) < 0.01m)
                continue;

            var vertical = ResolveYtdBillingVertical(row);
            var client = ResolveYtdBillingClient(row);
            var contractType = ResolveYtdBillingContractType(row);
            contributions.Add(new YtdContribution(
                Month: row.EmissionDate.Value.Month,
                Value: amount,
                ClientKey: client.Key,
                ClientLabel: client.Label,
                CategoryKey: "billing",
                CategoryLabel: "Facturacion",
                VerticalKey: vertical.Key,
                VerticalLabel: vertical.Label,
                ContractTypeKey: contractType.Key,
                ContractTypeLabel: contractType.Label,
                RecordsCount: 1,
                Records: new[] { BuildYtdBillingRecord(row, amount, client.Key, client.Label, vertical.Label, contractType.Label) }));
        }

        foreach (var record in rebateRecords)
        {
            if (Math.Abs(record.Value) < 0.01m)
                continue;

            contributions.Add(new YtdContribution(
                Month: record.Date.Month,
                Value: RoundCurrency(record.Value),
                ClientKey: YtdUnassignedClientKey,
                ClientLabel: "",
                CategoryKey: YtdRebatesCategoryKey,
                CategoryLabel: YtdRebatesCategoryLabel,
                VerticalKey: YtdUnassignedVerticalKey,
                VerticalLabel: YtdUnassignedVerticalLabel,
                ContractTypeKey: "",
                ContractTypeLabel: "",
                RecordsCount: 1,
                Records: new[] { BuildYtdRebateRecord(record) }));
        }

        return contributions;
    }

    private YtdExpenseBuildResult BuildYtdExpenseContributions(
        IReadOnlyList<PnlExpenseRow> expenseRecords,
        IReadOnlyList<LicenciamientoCruceRowDto> licensingRows,
        int year,
        int monthCutoff)
    {
        var contributions = new List<YtdContribution>();
        var licensingByMonth = BuildYtdLicensingClientCostsByMonth(licensingRows, year, monthCutoff);
        var xcbRowsByMonth = new Dictionary<int, List<PnlExpenseRow>>();
        var reconciliationMonths = new List<YtdLicensingReconciliationMonthDto>();

        foreach (var row in expenseRecords)
        {
            if (row.EmissionDate is null)
                continue;

            var amount = ResolveYtdExpenseAmount(row);
            if (Math.Abs(amount) < 0.01m)
                continue;

            if (IsYtdXcbLicensingExpense(row, amount))
            {
                var month = row.EmissionDate.Value.Month;
                if (!xcbRowsByMonth.TryGetValue(month, out var xcbMonthRows))
                {
                    xcbMonthRows = new List<PnlExpenseRow>();
                    xcbRowsByMonth[month] = xcbMonthRows;
                }

                xcbMonthRows.Add(row);
                continue;
            }

            var category = ResolveYtdExpenseCategory(row);
            foreach (var vertical in ResolveYtdExpenseVerticalAmounts(row, amount, defaultCloudWhenUnassigned: false))
            {
                contributions.Add(new YtdContribution(
                    Month: row.EmissionDate.Value.Month,
                    Value: vertical.Value,
                    ClientKey: YtdUnassignedClientKey,
                    ClientLabel: "",
                    CategoryKey: category.Key,
                    CategoryLabel: category.Label,
                    VerticalKey: vertical.Key,
                    VerticalLabel: vertical.Label,
                    ContractTypeKey: "",
                    ContractTypeLabel: "",
                    RecordsCount: 1,
                    Records: new[] { BuildYtdExpenseRecord(row, vertical.Value, category.Key, category.Label, vertical.Key, vertical.Label, "Gasto") }));
            }
        }

        foreach (var group in xcbRowsByMonth.OrderBy(static item => item.Key))
        {
            var month = group.Key;
            var rows = group.Value;
            var invoiceValue = RoundCurrency(rows.Sum(ResolveYtdExpenseAmount));
            var licensingClients = licensingByMonth.TryGetValue(month, out var monthClients)
                ? monthClients
                : Array.Empty<YtdLicensingClientCost>();
            var licensingValue = RoundCurrency(licensingClients.Sum(static item => item.Value));
            var difference = RoundCurrency(invoiceValue - licensingValue);
            var differencePercent = invoiceValue == 0m ? 0m : RoundCurrency((difference / invoiceValue) * 100m);

            reconciliationMonths.Add(new YtdLicensingReconciliationMonthDto
            {
                Key = $"{year:D4}-{month:D2}",
                Label = ResolvePnlMonthLabel(year, month),
                Month = month,
                InvoiceValue = invoiceValue,
                LicensingValue = licensingValue,
                Difference = difference,
                DifferencePercent = differencePercent
            });

            var verticalAllocations = rows
                .SelectMany(row => ResolveYtdExpenseVerticalAmounts(row, ResolveYtdExpenseAmount(row), defaultCloudWhenUnassigned: true)
                    .Select(vertical => new YtdXcbVerticalAllocation(row, vertical.Key, vertical.Label, vertical.Value)))
                .Where(static item => Math.Abs(item.Value) >= 0.01m)
                .ToList();
            var verticalAmounts = verticalAllocations
                .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(grouped => new YtdDimensionAmount(
                    grouped.Key,
                    grouped.Select(static item => item.Label).FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label)) ?? YtdUnassignedVerticalLabel,
                    RoundCurrency(grouped.Sum(static item => item.Value))))
                .Where(static item => Math.Abs(item.Value) >= 0.01m)
                .ToList();

            if (verticalAmounts.Count == 0 && Math.Abs(invoiceValue) >= 0.01m)
            {
                verticalAmounts.Add(new YtdDimensionAmount(DashboardPnlVerticalCloud, "Cloud", invoiceValue));
                verticalAllocations = rows
                    .Select(row => new YtdXcbVerticalAllocation(row, DashboardPnlVerticalCloud, "Cloud", ResolveYtdExpenseAmount(row)))
                    .Where(static item => Math.Abs(item.Value) >= 0.01m)
                    .ToList();
            }

            if (licensingClients.Count == 0 || Math.Abs(licensingValue) < 0.01m)
            {
                foreach (var vertical in verticalAmounts)
                {
                    contributions.Add(new YtdContribution(
                        Month: month,
                        Value: vertical.Value,
                        ClientKey: YtdUnassignedClientKey,
                        ClientLabel: "",
                        CategoryKey: YtdLicensingCategoryKey,
                        CategoryLabel: YtdLicensingCategoryLabel,
                        VerticalKey: vertical.Key,
                        VerticalLabel: vertical.Label,
                        ContractTypeKey: "",
                        ContractTypeLabel: "",
                        RecordsCount: rows.Count,
                        Records: BuildYtdXcbExpenseRecords(
                            verticalAllocations.Where(item => string.Equals(item.Key, vertical.Key, StringComparison.OrdinalIgnoreCase)),
                            vertical.Value,
                            vertical.Value,
                            "",
                            "",
                            vertical.Key,
                            vertical.Label,
                            "",
                            "",
                            null,
                            Array.Empty<string>())));
                }

                continue;
            }

            foreach (var client in licensingClients)
            {
                var clientInvoiceValue = RoundCurrency(invoiceValue * (client.Value / licensingValue));
                foreach (var vertical in verticalAmounts)
                {
                    var allocatedValue = invoiceValue == 0m
                        ? 0m
                        : RoundCurrency(clientInvoiceValue * (vertical.Value / invoiceValue));
                    if (Math.Abs(allocatedValue) < 0.01m)
                        continue;

                    contributions.Add(new YtdContribution(
                        Month: month,
                        Value: allocatedValue,
                        ClientKey: client.Key,
                        ClientLabel: client.Label,
                        CategoryKey: YtdLicensingCategoryKey,
                        CategoryLabel: YtdLicensingCategoryLabel,
                        VerticalKey: vertical.Key,
                        VerticalLabel: vertical.Label,
                        ContractTypeKey: client.ContractTypeKey,
                        ContractTypeLabel: client.ContractTypeLabel,
                        RecordsCount: client.RecordsCount,
                        Records: BuildYtdXcbExpenseRecords(
                            verticalAllocations.Where(item => string.Equals(item.Key, vertical.Key, StringComparison.OrdinalIgnoreCase)),
                            vertical.Value,
                            allocatedValue,
                            client.Key,
                            client.Label,
                            vertical.Key,
                            vertical.Label,
                            client.ContractTypeKey,
                            client.ContractTypeLabel,
                            client.ContractTypeValue,
                            client.CostRecordIds)));
                }
            }
        }

        return new YtdExpenseBuildResult(
            contributions,
            BuildYtdLicensingReconciliation(reconciliationMonths));
    }

    private static YtdChartDto BuildYtdChart(
        int year,
        int monthCutoff,
        IReadOnlyList<YtdContribution> revenueContributions,
        IReadOnlyList<YtdContribution> expenseContributions)
    {
        var points = Enumerable.Range(1, monthCutoff)
            .Select(month =>
            {
                var revenueSegments = BuildYtdSegments(
                    "revenue",
                    revenueContributions.Where(item => item.Month == month));
                var expenseSegments = BuildYtdSegments(
                    "expense",
                    expenseContributions.Where(item => item.Month == month));
                var sales = RoundCurrency(revenueSegments.Sum(static item => item.Value));
                var expenses = RoundCurrency(expenseSegments.Sum(static item => item.Value));

                return new YtdChartPointDto
                {
                    Key = $"{year:D4}-{month:D2}",
                    Label = ResolvePnlMonthLabel(year, month),
                    Month = month,
                    Sales = sales,
                    Expenses = expenses,
                    Utility = RoundCurrency(sales - expenses),
                    RevenueSegments = revenueSegments,
                    ExpenseSegments = expenseSegments
                };
            })
            .ToList();

        return new YtdChartDto
        {
            Key = "total",
            Title = "TOTAL",
            Subtitle = "Consolidado con gasto XCB de licenciamiento distribuido por cliente",
            HasData = points.Any(static point =>
                Math.Abs(point.Sales) >= 0.01m
                || Math.Abs(point.Expenses) >= 0.01m
                || Math.Abs(point.Utility) >= 0.01m),
            TotalSales = RoundCurrency(points.Sum(static point => point.Sales)),
            TotalExpenses = RoundCurrency(points.Sum(static point => point.Expenses)),
            TotalUtility = RoundCurrency(points.Sum(static point => point.Utility)),
            Points = points
        };
    }

    private static IReadOnlyList<YtdBreakdownSegmentDto> BuildYtdSegments(
        string kind,
        IEnumerable<YtdContribution> contributions)
    {
        return contributions
            .GroupBy(
                item => string.Join(
                    "|",
                    item.ClientKey,
                    item.CategoryKey,
                    item.VerticalKey,
                    item.ContractTypeKey),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new YtdBreakdownSegmentDto
                {
                    Key = $"{kind}:{group.Key}",
                    Label = kind == "revenue"
                        ? FirstNonEmpty(first.ClientLabel, first.VerticalLabel, "Sin cliente")
                        : FirstNonEmpty(first.CategoryLabel, first.ClientLabel, first.VerticalLabel, "Sin categoria"),
                    Kind = kind,
                    ClientKey = first.ClientKey,
                    ClientLabel = first.ClientLabel,
                    CategoryKey = first.CategoryKey,
                    CategoryLabel = first.CategoryLabel,
                    VerticalKey = first.VerticalKey,
                    VerticalLabel = first.VerticalLabel,
                    ContractTypeKey = first.ContractTypeKey,
                    ContractTypeLabel = first.ContractTypeLabel,
                    Value = RoundCurrency(group.Sum(static item => item.Value)),
                    RecordsCount = group.Sum(static item => item.RecordsCount),
                    Records = group
                        .SelectMany(static item => item.Records)
                        .OrderByDescending(static record => Math.Abs(record.Value))
                        .ThenBy(static record => record.DateDisplay, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            })
            .Where(item => string.Equals(kind, "revenue", StringComparison.OrdinalIgnoreCase)
                ? item.RecordsCount > 0
                : Math.Abs(item.Value) >= 0.01m)
            .OrderByDescending(static item => Math.Abs(item.Value))
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static YtdBreakdownRecordDto BuildYtdBillingRecord(
        BillingRecordRow row,
        decimal value,
        string clientKey,
        string clientLabel,
        string verticalLabel,
        string contractTypeLabel)
    {
        return new YtdBreakdownRecordDto
        {
            SourceType = "billing",
            SourceLabel = row.IsCreditNoteLedgerEntry ? "Nota credito cliente" : "Factura cliente",
            RecordId = row.RecordId,
            DocumentNumber = FirstNonEmpty(row.InvoiceNumber, row.InvoiceCode, row.SiigoInvoiceName, row.RecordId),
            DateDisplay = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Counterparty = FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Cliente sin nombre"),
            RecipientLabel = "",
            ClientKey = clientKey,
            ClientLabel = clientLabel,
            CategoryKey = "billing",
            CategoryLabel = "Facturacion",
            VerticalKey = ResolveYtdBillingVertical(row).Key,
            VerticalLabel = verticalLabel,
            VerticalOptionValue = row.VerticalOptionValue > 0 ? row.VerticalOptionValue : null,
            ContractTypeKey = ResolveYtdBillingContractType(row).Key,
            ContractTypeLabel = contractTypeLabel,
            ContractTypeOptionValue = row.ContractTypeOptionValue > 0 ? row.ContractTypeOptionValue : null,
            Description = row.IsCreditNoteLedgerEntry
                ? FirstNonEmpty(row.ContractTypeLabel, row.InvoicePrefix, "Nota credito emitida")
                : FirstNonEmpty(row.ContractTypeLabel, row.InvoicePrefix, "Factura emitida"),
            TotalInvoice = row.TotalInvoice,
            VatValue = row.VatValue,
            TotalBeforeVatValue = GetPnlBillingLegalBaseValue(row),
            PaymentValue = row.PaymentValue,
            Value = RoundCurrency(value),
            CanEditVertical = true,
            CanEditContractType = true
        };
    }

    private static YtdBreakdownRecordDto BuildYtdExpenseRecord(
        PnlExpenseRow row,
        decimal value,
        string categoryKey,
        string categoryLabel,
        string verticalKey,
        string verticalLabel,
        string sourceLabel)
    {
        return new YtdBreakdownRecordDto
        {
            SourceType = "expense",
            SourceLabel = sourceLabel,
            RecordId = row.RecordId,
            DocumentNumber = row.RecordId,
            DateDisplay = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Counterparty = FirstNonEmpty(row.IssuerName, "Proveedor sin nombre"),
            RecipientLabel = FirstNonEmpty(row.RecipientName, "Sin receptor"),
            ClientKey = YtdUnassignedClientKey,
            ClientLabel = "",
            CategoryOptionValue = row.CategoryOptionValue > 0 ? row.CategoryOptionValue : null,
            CategoryKey = categoryKey,
            CategoryLabel = categoryLabel,
            VerticalKey = verticalKey,
            VerticalLabel = verticalLabel,
            ContractTypeLabel = "",
            Description = FirstNonEmpty(row.CategoryLabel, categoryLabel, "Gasto"),
            TotalInvoice = row.TotalValue,
            VatValue = row.VatValue,
            TotalBeforeVatValue = row.TotalBeforeVatValue,
            PaymentValue = row.PaymentValue,
            CloudValue = row.CloudValue,
            CopiersValue = row.CopiersValue,
            Value = RoundCurrency(value),
            CanEditCategory = true,
            CanEditVertical = true,
            CanEditAllocation = true
        };
    }

    private static YtdBreakdownRecordDto BuildYtdRebateRecord(SharePointRebateRecord record)
    {
        return new YtdBreakdownRecordDto
        {
            SourceType = "sharepoint-rebate",
            SourceLabel = "SharePoint / Rebates",
            RecordId = record.RecordId,
            DocumentNumber = $"Fila {record.SourceRow}",
            DateDisplay = record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Counterparty = "Facturacion DIGITAL TECH.xlsx",
            RecipientLabel = "",
            ClientKey = YtdUnassignedClientKey,
            ClientLabel = "",
            CategoryKey = YtdRebatesCategoryKey,
            CategoryLabel = YtdRebatesCategoryLabel,
            VerticalKey = YtdUnassignedVerticalKey,
            VerticalLabel = YtdUnassignedVerticalLabel,
            ContractTypeLabel = "",
            Description = "Rebate desde tabla de Excel",
            Value = RoundCurrency(record.Value)
        };
    }

    private static IReadOnlyList<YtdBreakdownRecordDto> BuildYtdXcbExpenseRecords(
        IEnumerable<YtdXcbVerticalAllocation> allocations,
        decimal verticalTotal,
        decimal segmentTotal,
        string clientKey,
        string clientLabel,
        string verticalKey,
        string verticalLabel,
        string contractTypeKey,
        string contractTypeLabel,
        int? contractTypeValue,
        IReadOnlyList<string> licensingCostRecordIds)
    {
        var allocationList = allocations.ToList();
        if (allocationList.Count == 0 || Math.Abs(segmentTotal) < 0.01m)
            return Array.Empty<YtdBreakdownRecordDto>();

        return allocationList
            .Select(allocation =>
            {
                var value = Math.Abs(verticalTotal) < 0.01m
                    ? 0m
                    : RoundCurrency(segmentTotal * (allocation.Value / verticalTotal));
                return new YtdBreakdownRecordDto
                {
                    SourceType = "expense",
                    SourceLabel = "Gasto XCB licenciamiento",
                    RecordId = allocation.Row.RecordId,
                    DocumentNumber = allocation.Row.RecordId,
                    DateDisplay = allocation.Row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    Counterparty = FirstNonEmpty(allocation.Row.IssuerName, "XCB"),
                    RecipientLabel = FirstNonEmpty(allocation.Row.RecipientName, "Sin receptor"),
                    ClientKey = clientKey,
                    ClientLabel = clientLabel,
                    CategoryOptionValue = allocation.Row.CategoryOptionValue > 0 ? allocation.Row.CategoryOptionValue : null,
                    CategoryKey = YtdLicensingCategoryKey,
                    CategoryLabel = YtdLicensingCategoryLabel,
                    VerticalKey = verticalKey,
                    VerticalLabel = verticalLabel,
                    ContractTypeKey = contractTypeKey,
                    ContractTypeLabel = contractTypeLabel,
                    ContractTypeOptionValue = contractTypeValue,
                    Description = string.IsNullOrWhiteSpace(clientLabel)
                        ? "Factura XCB sin desglose de cliente"
                        : $"Factura XCB asignada a {clientLabel}{(string.IsNullOrWhiteSpace(contractTypeLabel) ? "" : $" / {contractTypeLabel}")}",
                    TotalInvoice = allocation.Row.TotalValue,
                    VatValue = allocation.Row.VatValue,
                    TotalBeforeVatValue = allocation.Row.TotalBeforeVatValue,
                    PaymentValue = allocation.Row.PaymentValue,
                    CloudValue = allocation.Row.CloudValue,
                    CopiersValue = allocation.Row.CopiersValue,
                    Value = value,
                    CanEditCategory = true,
                    CanEditVertical = true,
                    CanEditAllocation = true,
                    CanEditContractType = licensingCostRecordIds.Count > 0,
                    LicensingCostRecordIds = licensingCostRecordIds
                };
            })
            .Where(static record => Math.Abs(record.Value) >= 0.01m)
            .OrderByDescending(static record => Math.Abs(record.Value))
            .ToList();
    }

    private static YtdFilterSetDto BuildYtdRevenueFilters(IReadOnlyList<YtdContribution> revenueContributions)
    {
        return new YtdFilterSetDto
        {
            Clients = BuildYtdFilterOptions(
                revenueContributions.Where(static item => !string.IsNullOrWhiteSpace(item.ClientKey)),
                static item => item.ClientKey,
                static item => item.ClientLabel),
            Categories = BuildYtdFilterOptions(
                revenueContributions,
                static item => item.CategoryKey,
                static item => item.CategoryLabel),
            Verticals = BuildYtdFilterOptions(
                revenueContributions,
                static item => item.VerticalKey,
                static item => item.VerticalLabel),
            ContractTypes = BuildYtdFilterOptions(
                revenueContributions,
                static item => item.ContractTypeKey,
                static item => item.ContractTypeLabel),
            BreakdownModes = new[]
            {
                new YtdBreakdownModeDto { Key = "global", Label = "Global" },
                new YtdBreakdownModeDto { Key = "category", Label = "Categoria" },
                new YtdBreakdownModeDto { Key = "client", Label = "Clientes" },
                new YtdBreakdownModeDto { Key = "vertical", Label = "Vertical" },
                new YtdBreakdownModeDto { Key = "contractType", Label = "Tipo contrato" }
            }
        };
    }

    private static YtdFilterSetDto BuildYtdExpenseFilters(
        IReadOnlyList<YtdContribution> revenueContributions,
        IReadOnlyList<YtdContribution> expenseContributions)
    {
        var clientOptions = revenueContributions
            .Where(static item => !string.IsNullOrWhiteSpace(item.ClientKey))
            .Select(item => new YtdFilterOptionSeed(item.ClientKey, item.ClientLabel, 0m, 0))
            .Concat(expenseContributions
                .Where(static item => !string.IsNullOrWhiteSpace(item.ClientKey))
                .Select(item => new YtdFilterOptionSeed(item.ClientKey, item.ClientLabel, item.Value, item.RecordsCount)))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new YtdFilterOptionDto
            {
                Key = group.Key,
                Label = FirstNonEmpty(group.Select(static item => item.Label).FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label)), group.Key),
                Total = RoundCurrency(group.Sum(static item => item.Value)),
                RecordsCount = group.Sum(static item => item.RecordsCount)
            })
            .OrderByDescending(static option => Math.Abs(option.Total))
            .ThenBy(static option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new YtdFilterSetDto
        {
            Clients = clientOptions,
            Categories = BuildYtdFilterOptions(
                expenseContributions,
                static item => item.CategoryKey,
                static item => item.CategoryLabel),
            Verticals = BuildYtdFilterOptions(
                expenseContributions,
                static item => item.VerticalKey,
                static item => item.VerticalLabel),
            ContractTypes = BuildYtdFilterOptions(
                expenseContributions.Where(static item => !string.IsNullOrWhiteSpace(item.ContractTypeKey)),
                static item => item.ContractTypeKey,
                static item => item.ContractTypeLabel),
            BreakdownModes = new[]
            {
                new YtdBreakdownModeDto { Key = "global", Label = "Global" },
                new YtdBreakdownModeDto { Key = "category", Label = "Categoria" },
                new YtdBreakdownModeDto { Key = "vertical", Label = "Vertical" },
                new YtdBreakdownModeDto { Key = "client", Label = "Clientes" },
                new YtdBreakdownModeDto { Key = "contractType", Label = "Tipo contrato" }
            }
        };
    }

    private static IReadOnlyList<YtdFilterOptionDto> BuildYtdFilterOptions(
        IEnumerable<YtdContribution> contributions,
        Func<YtdContribution, string> keySelector,
        Func<YtdContribution, string> labelSelector)
    {
        return contributions
            .Where(item => !string.IsNullOrWhiteSpace(keySelector(item)))
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new YtdFilterOptionDto
                {
                    Key = group.Key,
                    Label = FirstNonEmpty(labelSelector(first), group.Key),
                    Total = RoundCurrency(group.Sum(static item => item.Value)),
                    RecordsCount = group.Sum(static item => item.RecordsCount)
                };
            })
            .OrderByDescending(static item => Math.Abs(item.Total))
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static YtdEditorOptionsDto BuildYtdEditorOptions()
    {
        return new YtdEditorOptionsDto
        {
            BillingVerticals = BuildBillingVerticalOptions()
                .Select(static option => new YtdEditorOptionDto
                {
                    Key = option.Value switch
                    {
                        DashboardVerticalCloudOption => DashboardPnlVerticalCloud,
                        DashboardVerticalCopiersOption => DashboardPnlVerticalCopiers,
                        _ => ""
                    },
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList(),
            BillingContractTypes = BuildBillingContractTypeOptions()
                .Select(static option => new YtdEditorOptionDto
                {
                    Key = option.Value switch
                    {
                        DashboardContractTypeMonthlyOption => LicenciamientoCruceMonthlyKey,
                        DashboardContractTypeOneTimeOption => LicenciamientoCruceOneTimeKey,
                        _ => ""
                    },
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList(),
            ExpenseVerticals = BuildPnlVerticalOptions()
                .Where(static option => option.Key is DashboardPnlVerticalCloud or DashboardPnlVerticalCopiers)
                .Select(static option => new YtdEditorOptionDto
                {
                    Key = option.Key,
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList(),
            ExpenseCategories = BuildPnlCategoryOptions()
                .Select(static option => new YtdEditorOptionDto
                {
                    Key = option.Key,
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList(),
            ExpenseContractTypes = BuildLicenciamientoCruceCostContractTypeOptions()
                .Select(static option => new YtdEditorOptionDto
                {
                    Key = ResolveLicenciamientoCruceContractKey(option.Value, option.Label, isBillingSource: false),
                    Label = option.Label,
                    Value = option.Value
                })
                .ToList()
        };
    }

    private static YtdLicensingReconciliationDto BuildYtdLicensingReconciliation(
        IReadOnlyList<YtdLicensingReconciliationMonthDto> months)
    {
        var invoiceTotal = RoundCurrency(months.Sum(static item => item.InvoiceValue));
        var licensingTotal = RoundCurrency(months.Sum(static item => item.LicensingValue));
        var difference = RoundCurrency(invoiceTotal - licensingTotal);
        var differencePercent = invoiceTotal == 0m ? 0m : RoundCurrency((difference / invoiceTotal) * 100m);
        var disclaimer = invoiceTotal == 0m
            ? "No se encontraron facturas XCB de licenciamiento superiores a $100M en el periodo."
            : $"Control XCB: factura(s) {FormatCurrencyValue(invoiceTotal)} vs licenciamiento desglosado {FormatCurrencyValue(licensingTotal)}. Desfase {FormatCurrencyValue(difference)} ({differencePercent.ToString("N2", DashboardCulture)}%).";

        return new YtdLicensingReconciliationDto
        {
            InvoiceTotal = invoiceTotal,
            LicensingTotal = licensingTotal,
            Difference = difference,
            DifferencePercent = differencePercent,
            Disclaimer = disclaimer,
            Months = months
        };
    }

    private static Dictionary<int, IReadOnlyList<YtdLicensingClientCost>> BuildYtdLicensingClientCostsByMonth(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        int year,
        int monthCutoff)
    {
        return rows
            .Select(row => new
            {
                Row = row,
                Month = ResolveYtdLicensingCostMonth(row),
                ClientKey = ResolveYtdLicensingClientKey(row),
                ClientLabel = ResolveYtdLicensingClientLabel(row),
                ContractType = ResolveYtdLicensingContractType(row),
                ContractTypeValue = ResolveYtdLicensingContractTypeValue(row),
                CostRecordIds = ResolveYtdLicensingCostRecordIds(row)
            })
            .Where(item => item.Month is not null
                && item.Month.Value.Year == year
                && item.Month.Value.Month <= monthCutoff
                && Math.Abs(item.Row.CostoLicenciamiento) >= 0.01m)
            .GroupBy(item => item.Month!.Value.Month)
            .ToDictionary(
                static group => group.Key,
                group => (IReadOnlyList<YtdLicensingClientCost>)group
                    .GroupBy(item => string.Join("|", item.ClientKey, item.ContractType.Key), StringComparer.OrdinalIgnoreCase)
                    .Select(clientGroup =>
                    {
                        var first = clientGroup.First();
                        return new YtdLicensingClientCost(
                            first.ClientKey,
                            first.ClientLabel,
                            first.ContractType.Key,
                            first.ContractType.Label,
                            first.ContractTypeValue,
                            RoundCurrency(clientGroup.Sum(static item => item.Row.CostoLicenciamiento)),
                            clientGroup.Count(),
                            clientGroup
                                .SelectMany(static item => item.CostRecordIds)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList());
                    })
                    .Where(static item => Math.Abs(item.Value) >= 0.01m)
                    .OrderByDescending(static item => Math.Abs(item.Value))
                    .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static bool IsYtdLicensingRowInPeriod(LicenciamientoCruceRowDto row, int year, int monthCutoff)
    {
        var month = ResolveYtdLicensingCostMonth(row);
        return month is not null
            && month.Value.Year == year
            && month.Value.Month <= monthCutoff;
    }

    private static DateOnly? ResolveYtdLicensingCostMonth(LicenciamientoCruceRowDto row) =>
        TryParseLicenciamientoCruceMonth(row.MesCosto)
        ?? TryParseLicenciamientoCruceMonth(row.MesCierre)
        ?? TryParseLicenciamientoCruceMonth(row.MesFacturacion);

    private static string ResolveYtdLicensingClientKey(LicenciamientoCruceRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.MatrixClientKey))
            return row.MatrixClientKey;

        var key = ResolveLicenciamientoCruceMatrixClientKey(row);
        return !string.IsNullOrWhiteSpace(key)
            ? key
            : $"name:{NormalizeLicenciamientoCruceClientKey(ResolveYtdLicensingClientLabel(row))}";
    }

    private static string ResolveYtdLicensingClientLabel(LicenciamientoCruceRowDto row) =>
        FirstNonEmpty(
            row.GrupoEmpresarial,
            row.Trace?.BillingBusinessGroupName,
            row.Trace?.CostBusinessGroupName,
            row.Cliente,
            row.NitCliente,
            "Cliente sin nombre");

    private static YtdDimension ResolveYtdBillingClient(BillingRecordRow row)
    {
        var clientId = NormalizeOptionalGuid(row.ClientId);
        if (!string.IsNullOrWhiteSpace(clientId))
            return new YtdDimension($"client:{clientId}", FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Cliente sin nombre"));

        var taxId = NormalizeLicenciamientoCruceMapKey(row.CompanyTaxId);
        if (!string.IsNullOrWhiteSpace(taxId))
            return new YtdDimension($"nit:{taxId}", FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Cliente sin nombre"));

        var nameKey = NormalizeLicenciamientoCruceClientKey(row.ClientName);
        return new YtdDimension(
            !string.IsNullOrWhiteSpace(nameKey) ? $"name:{nameKey}" : "client:sin-cliente",
            FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Cliente sin nombre"));
    }

    private static YtdDimension ResolveYtdBillingVertical(BillingRecordRow row)
    {
        return row.VerticalOptionValue switch
        {
            DashboardVerticalCloudOption => new YtdDimension(DashboardPnlVerticalCloud, "Cloud"),
            DashboardVerticalCopiersOption => new YtdDimension(DashboardPnlVerticalCopiers, "Copiers"),
            _ => new YtdDimension(YtdUnassignedVerticalKey, FirstNonEmpty(row.VerticalLabel, YtdUnassignedVerticalLabel))
        };
    }

    private static YtdDimension ResolveYtdBillingContractType(BillingRecordRow row)
    {
        var key = row.ContractTypeOptionValue switch
        {
            DashboardContractTypeMonthlyOption => LicenciamientoCruceMonthlyKey,
            DashboardContractTypeOneTimeOption => LicenciamientoCruceOneTimeKey,
            _ => ResolveLicenciamientoCruceContractKey(row.ContractTypeLabel)
        };
        var label = key switch
        {
            LicenciamientoCruceMonthlyKey => FirstNonEmpty(row.ContractTypeLabel, "Mensual"),
            LicenciamientoCruceOneTimeKey => FirstNonEmpty(row.ContractTypeLabel, "OneTime"),
            _ => FirstNonEmpty(row.ContractTypeLabel, "Sin contrato")
        };

        return new YtdDimension(key, label);
    }

    private static YtdDimension ResolveYtdLicensingContractType(LicenciamientoCruceRowDto row)
    {
        var key = ResolveLicenciamientoCruceContractKey(FirstNonEmpty(row.TipoContratoKey, row.TipoContrato));
        return new YtdDimension(key, ResolveLicenciamientoCruceContractLabel(key));
    }

    private static int? ResolveYtdLicensingContractTypeValue(LicenciamientoCruceRowDto row)
    {
        var traceValue = row.Trace?.CostItems?
            .Select(static item => item.TipoContratoValue)
            .Where(static value => value.HasValue && value.Value > 0)
            .GroupBy(static value => value!.Value)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key)
            .Select(static group => (int?)group.Key)
            .FirstOrDefault();
        if (traceValue.HasValue)
            return traceValue.Value;

        return ResolveLicenciamientoCruceContractKey(FirstNonEmpty(row.TipoContratoKey, row.TipoContrato)) switch
        {
            LicenciamientoCruceMonthlyKey => LicenciamientoCruceCostMonthlyOption,
            LicenciamientoCruceOneTimeKey => LicenciamientoCruceCostPrepaidOption,
            _ => null
        };
    }

    private static IReadOnlyList<string> ResolveYtdLicensingCostRecordIds(LicenciamientoCruceRowDto row)
    {
        return row.Trace?.CostItems?
            .Select(static item => NormalizeOptionalGuid(item.RecordId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }

    private static YtdDimension ResolveYtdExpenseCategory(PnlExpenseRow row)
    {
        var bucketKey = ResolvePnlExpenseBucketKey(row);
        var label = FirstNonEmpty(
            row.CategoryLabel,
            ResolvePnlExpenseCategoryLabel(row.CategoryOptionValue),
            ResolveYtdExpenseBucketLabel(bucketKey));

        return new YtdDimension(bucketKey, label);
    }

    private static string ResolveYtdExpenseBucketLabel(string bucketKey) => bucketKey switch
    {
        "licensing" => YtdLicensingCategoryLabel,
        "supplies" => "Suministros",
        "machines" => "Maquinas",
        "technical-service" => "Servicio Tecnico",
        "personal-administrative" => "Personal Administrativo",
        "personal-cloud" => "Personal Cloud",
        "personal-copiers" => "Personal Copiers",
        PnlExpensePrimasCesantiasBucket => "Primas/Cesantias",
        "office-rent" => "Arriendo Oficina",
        "warehouse" => "Bodegaje",
        "transport" => "Transporte Equipos",
        "internal" => "Gastos internos",
        "recurring" => "Recurrente",
        "equipment" => "Equipamiento",
        "travel" => "Viaticos",
        "marketing" => "Marketing",
        "taxes" => "Impuestos",
        PnlExpenseFinancialExpenseBucket => "Gastos financieros",
        PnlExpenseOtherNonOperatingBucket => "Otros gastos no operacionales",
        _ => "Sin categoria"
    };

    private static decimal ResolveYtdExpenseAmount(PnlExpenseRow row)
    {
        var amount = GetPnlExpenseBaseValue(row);
        if (Math.Abs(amount) < 0.01m)
            return 0m;

        var bucketKey = ResolvePnlExpenseBucketKey(row);
        if (IsPnlEbitdaExpenseBucket(bucketKey)
            || bucketKey == "taxes"
            || bucketKey == PnlExpenseFinancialExpenseBucket)
        {
            return amount;
        }

        if (bucketKey == PnlExpenseOtherNonOperatingBucket)
        {
            var signed = GetPnlOtherIncomeExpenseSignedAmount(row, amount, bucketKey);
            return signed < 0m ? RoundCurrency(Math.Abs(signed)) : 0m;
        }

        return 0m;
    }

    private static IReadOnlyList<YtdDimensionAmount> ResolveYtdExpenseVerticalAmounts(
        PnlExpenseRow row,
        decimal amount,
        bool defaultCloudWhenUnassigned)
    {
        if (Math.Abs(amount) < 0.01m)
            return Array.Empty<YtdDimensionAmount>();

        if (row.CategoryOptionValue == PnlExpensePersonalCloudOption)
        {
            return new[] { new YtdDimensionAmount(DashboardPnlVerticalCloud, "Cloud", amount) };
        }

        if (row.CategoryOptionValue == PnlExpensePersonalCopiersOption)
        {
            return new[] { new YtdDimensionAmount(DashboardPnlVerticalCopiers, "Copiers", amount) };
        }

        var cloudBase = Math.Max(row.CloudValue, 0m);
        var copiersBase = Math.Max(row.CopiersValue, 0m);
        var totalBase = cloudBase + copiersBase;
        if (totalBase > 0m)
        {
            var values = new List<YtdDimensionAmount>();
            if (cloudBase > 0m)
            {
                values.Add(new YtdDimensionAmount(
                    DashboardPnlVerticalCloud,
                    "Cloud",
                    RoundCurrency(amount * (cloudBase / totalBase))));
            }

            if (copiersBase > 0m)
            {
                values.Add(new YtdDimensionAmount(
                    DashboardPnlVerticalCopiers,
                    "Copiers",
                    RoundCurrency(amount * (copiersBase / totalBase))));
            }

            return values;
        }

        if (defaultCloudWhenUnassigned)
        {
            return new[] { new YtdDimensionAmount(DashboardPnlVerticalCloud, "Cloud", amount) };
        }

        return new[] { new YtdDimensionAmount(YtdUnassignedVerticalKey, YtdUnassignedVerticalLabel, amount) };
    }

    private static bool IsYtdXcbLicensingExpense(PnlExpenseRow row, decimal amount)
    {
        var invoiceReferenceValue = new[]
        {
            Math.Abs(amount),
            Math.Abs(row.TotalValue),
            Math.Abs(row.PaymentValue)
        }.Max();
        if (invoiceReferenceValue < YtdXcbMinimumInvoiceValue)
            return false;

        var normalizedIssuer = NormalizePnlLabel(row.IssuerName);
        return normalizedIssuer.Contains("xcb", StringComparison.Ordinal)
            && string.Equals(ResolvePnlExpenseBucketKey(row), YtdLicensingCategoryKey, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record YtdContribution(
        int Month,
        decimal Value,
        string ClientKey,
        string ClientLabel,
        string CategoryKey,
        string CategoryLabel,
        string VerticalKey,
        string VerticalLabel,
        string ContractTypeKey,
        string ContractTypeLabel,
        int RecordsCount,
        IReadOnlyList<YtdBreakdownRecordDto> Records);

    private sealed record YtdDimension(string Key, string Label);

    private sealed record YtdDimensionAmount(string Key, string Label, decimal Value);

    private sealed record YtdXcbVerticalAllocation(PnlExpenseRow Row, string Key, string Label, decimal Value);

    private sealed record YtdLicensingClientCost(
        string Key,
        string Label,
        string ContractTypeKey,
        string ContractTypeLabel,
        int? ContractTypeValue,
        decimal Value,
        int RecordsCount,
        IReadOnlyList<string> CostRecordIds);

    private sealed record YtdFilterOptionSeed(string Key, string Label, decimal Value, int RecordsCount);

    private sealed record YtdExpenseBuildResult(
        IReadOnlyList<YtdContribution> Contributions,
        YtdLicensingReconciliationDto Reconciliation);
}
