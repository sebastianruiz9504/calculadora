using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Licenciamiento;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string LicenciamientoCruceStatusExact = "Match exacto";
    private const string LicenciamientoCruceStatusProbable = "Match probable";
    private const string LicenciamientoCruceStatusCostOnly = "Costo sin facturacion";
    private const string LicenciamientoCruceStatusBillingOnly = "Facturacion sin costo";
    private const decimal LicenciamientoCruceProbableThreshold = 0.76m;

    private static readonly string[] LicenciamientoCruceLegalTokens =
    {
        "SAS",
        "SA",
        "S A S",
        "S A",
        "LTDA",
        "LIMITADA",
        "INC",
        "CORP",
        "CORPORACION",
        "FUNDACION",
        "EMPRESA",
        "UNION TEMPORAL"
    };

    public async Task<LicenciamientoCruceDashboardDto> GetLicenciamientoCruceDashboardAsync(
        int year,
        int month,
        int billingOffsetMonths = 1,
        decimal marginThresholdPercent = 20m,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var resolvedMonth = month is < 1 or > 12 ? Math.Max(today.Month - 1, 1) : month;
        var resolvedOffset = Math.Clamp(billingOffsetMonths, -12, 12);
        var resolvedThreshold = Math.Clamp(marginThresholdPercent, -100m, 1000m);

        var closeMonth = new DateOnly(resolvedYear, resolvedMonth, 1);
        var billingMonth = closeMonth.AddMonths(resolvedOffset);
        var billingMonthEnd = billingMonth.AddMonths(1);

        var licensingMetadata = await ResolveLicensingMetadataAsync(httpContext.User, ct);
        var billingMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var costRows = await GetLicenciamientoCruceCostRowsAsync(
            licensingMetadata,
            billingMonth,
            billingMonthEnd,
            closeMonth,
            resolvedOffset,
            httpContext.User,
            ct);
        var billingRows = await GetBillingRecordsAsync(
            billingMetadata,
            billingMonth,
            billingMonthEnd,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var costGroups = BuildLicenciamientoCruceCostGroups(costRows, closeMonth);
        var billingGroups = BuildLicenciamientoCruceBillingGroups(billingRows);
        var rows = BuildLicenciamientoCruceRows(
                costGroups,
                billingGroups,
                closeMonth,
                billingMonth,
                resolvedThreshold)
            .OrderBy(row => ResolveLicenciamientoCruceStateOrder(row.EstadoCruce))
            .ThenBy(row => row.Cliente, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCostSource = RoundCurrency(costRows.Sum(static row => row.CostCop));
        var totalCostCross = RoundCurrency(rows.Sum(static row => row.CostoLicenciamiento));
        var totalBillingSource = RoundCurrency(billingGroups.Sum(static row => row.BillingWithoutVat));
        var totalBillingCross = RoundCurrency(rows.Sum(static row => row.FacturacionSinIva));
        var totalMargin = RoundCurrency(totalBillingCross - totalCostCross);
        var totalMarginPct = CalculateLicenciamientoCruceMarginPercent(totalMargin, totalBillingCross);

        var totals = new LicenciamientoCruceTotalsDto
        {
            TotalCostosLicenciamiento = totalCostCross,
            TotalFacturacionRelacionada = totalBillingCross,
            MargenBrutoTotal = totalMargin,
            MargenBrutoPct = totalMarginPct,
            TotalCostosFuente = totalCostSource,
            TotalCostosCruce = totalCostCross,
            TotalFacturacionFuenteSinIva = totalBillingSource
        };

        return new LicenciamientoCruceDashboardDto
        {
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(closeMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            BillingOffsetMonths = resolvedOffset,
            MarginThresholdPercent = resolvedThreshold,
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            Totals = totals,
            StatusCounts = BuildLicenciamientoCruceStatusCounts(rows),
            Rows = rows,
            MonthSummaries = BuildLicenciamientoCruceMonthSummaries(rows),
            Alerts = BuildLicenciamientoCruceAlerts(rows, resolvedThreshold),
            Validations = BuildLicenciamientoCruceValidations(
                costRows,
                billingRows,
                rows,
                totalCostSource,
                totalCostCross,
                billingMonth),
            Message = rows.Count == 0
                ? "No hay costos ni facturacion para el periodo seleccionado."
                : $"Cruce listo para {FormatLicenciamientoCruceMonth(closeMonth)} contra facturacion {FormatLicenciamientoCruceMonth(billingMonth)}."
        };
    }

    private async Task<List<LicenciamientoCruceCostRow>> GetLicenciamientoCruceCostRowsAsync(
        LicensingMetadata metadata,
        DateOnly invoiceMonthStart,
        DateOnly invoiceMonthEnd,
        DateOnly fallbackCostMonth,
        int billingOffsetMonths,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = BuildLicensingSelectClause(metadata);
        var filter = BuildBillingDateFilter(
            LicensingInvoiceDateField,
            "date-only",
            invoiceMonthStart,
            invoiceMonthEnd);
        var orderBy = Uri.EscapeDataString($"{LicensingInvoiceDateField} asc,{LicensingCustomerNameField} asc");
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseLicenciamientoCruceCostRow(metadata, item, fallbackCostMonth, billingOffsetMonths))
            .Where(static row => row is not null)
            .Cast<LicenciamientoCruceCostRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private LicenciamientoCruceCostRow? ParseLicenciamientoCruceCostRow(
        LicensingMetadata metadata,
        JsonElement item,
        DateOnly fallbackCostMonth,
        int billingOffsetMonths)
    {
        var record = BuildLicensingRecordDto(metadata, item);
        if (record is null)
            return null;

        var invoiceDate = ReadDateOnly(item, LicensingInvoiceDateField);
        var costMonth = TryParseLicenciamientoCruceMonth(record.BillingInterval)
            ?? invoiceDate?.AddMonths(-billingOffsetMonths)
            ?? fallbackCostMonth;
        costMonth = new DateOnly(costMonth.Year, costMonth.Month, 1);

        var costCop = record.PesosTotal;
        if (Math.Abs(costCop) < 0.01m && Math.Abs(record.ValorTotalUsd) >= 0.01m && Math.Abs(record.Trm) >= 0.01m)
            costCop = RoundCurrency(record.ValorTotalUsd * record.Trm);

        var clientName = FirstNonEmpty(
            record.NombreCliente,
            record.CompanyAccountDisplay,
            record.CompanyAccountId,
            "Cliente sin nombre");

        return new LicenciamientoCruceCostRow
        {
            RecordId = record.RecordId,
            ClientName = clientName,
            CompanyAccountId = record.CompanyAccountId,
            CompanyAccountDisplay = record.CompanyAccountDisplay,
            ProductName = FirstNonEmpty(record.ProductDisplay, "Licencia sin producto"),
            Vendor = record.Vendor,
            InvoiceDate = invoiceDate,
            CostMonth = costMonth,
            CostCop = RoundCurrency(costCop)
        };
    }

    private static IReadOnlyList<LicenciamientoCruceCostGroup> BuildLicenciamientoCruceCostGroups(
        IReadOnlyList<LicenciamientoCruceCostRow> rows,
        DateOnly fallbackCostMonth)
    {
        return rows
            .GroupBy(row => BuildLicenciamientoCruceGroupingKey(row.ClientName, row.CompanyAccountDisplay, row.CompanyAccountId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var clientName = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ClientName), "Cliente sin nombre");
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    AddLicenciamientoCruceMatchKeys(keys, item.ClientName, item.CompanyAccountDisplay, item.CompanyAccountId);
                }

                return new LicenciamientoCruceCostGroup
                {
                    GroupKey = group.Key,
                    ClientName = clientName,
                    ProductSummary = BuildLicenciamientoCruceProductSummary(items.Select(static row => row.ProductName)),
                    Vertical = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.Vendor), "Licenciamiento"),
                    CostMonth = ResolveLicenciamientoCruceMostCommonMonth(items.Select(static row => row.CostMonth)) ?? fallbackCostMonth,
                    CostCop = RoundCurrency(items.Sum(static row => row.CostCop)),
                    RecordIds = items.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    MatchKeys = keys
                };
            })
            .OrderBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceBillingGroup> BuildLicenciamientoCruceBillingGroups(
        IReadOnlyList<BillingRecordRow> rows)
    {
        return rows
            .GroupBy(row => BuildLicenciamientoCruceGroupingKey(row.ClientName, row.CompanyTaxId, row.ClientId), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var clientName = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.ClientName), "Cliente sin nombre");
                var nit = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.CompanyTaxId), "");
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    AddLicenciamientoCruceMatchKeys(keys, item.ClientName, item.CompanyTaxId, item.ClientId);
                }

                return new LicenciamientoCruceBillingGroup
                {
                    GroupKey = group.Key,
                    ClientName = clientName,
                    Nit = nit,
                    Vertical = ResolveLicenciamientoCruceMostCommonText(items.Select(static row => row.VerticalLabel), "Sin vertical"),
                    BillingWithoutVat = RoundCurrency(items.Sum(CalculateLicenciamientoCruceBillingWithoutVat)),
                    BillingRecordIds = items.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    MatchKeys = keys,
                    HasInvalidVat = items.Any(static row => row.TotalInvoice < row.VatValue),
                    HasMissingVatValue = items.Any(static row => row.TotalInvoice > 0m && row.VatPercent > 0m && Math.Abs(row.VatValue) < 0.01m)
                };
            })
            .OrderBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceRowDto> BuildLicenciamientoCruceRows(
        IReadOnlyList<LicenciamientoCruceCostGroup> costGroups,
        IReadOnlyList<LicenciamientoCruceBillingGroup> billingGroups,
        DateOnly closeMonth,
        DateOnly billingMonth,
        decimal marginThresholdPercent)
    {
        var rows = new List<LicenciamientoCruceRowDto>();
        var unmatchedCosts = new List<LicenciamientoCruceCostGroup>();
        var usedBillingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cost in costGroups)
        {
            var exactBilling = billingGroups.FirstOrDefault(billing =>
                !usedBillingKeys.Contains(billing.GroupKey)
                && cost.MatchKeys.Overlaps(billing.MatchKeys));
            if (exactBilling is null)
            {
                unmatchedCosts.Add(cost);
                continue;
            }

            usedBillingKeys.Add(exactBilling.GroupKey);
            rows.Add(BuildLicenciamientoCruceRow(
                cost,
                exactBilling,
                closeMonth,
                billingMonth,
                LicenciamientoCruceStatusExact,
                100m,
                marginThresholdPercent));
        }

        foreach (var cost in unmatchedCosts)
        {
            var probableBilling = billingGroups
                .Where(billing => !usedBillingKeys.Contains(billing.GroupKey))
                .Select(billing => new
                {
                    Billing = billing,
                    Score = CalculateLicenciamientoCruceClientSimilarity(cost.ClientName, billing.ClientName)
                })
                .Where(item => item.Score >= LicenciamientoCruceProbableThreshold)
                .OrderByDescending(static item => item.Score)
                .ThenByDescending(static item => item.Billing.BillingWithoutVat)
                .FirstOrDefault();

            if (probableBilling is null)
            {
                rows.Add(BuildLicenciamientoCruceCostOnlyRow(cost, closeMonth, billingMonth));
                continue;
            }

            usedBillingKeys.Add(probableBilling.Billing.GroupKey);
            rows.Add(BuildLicenciamientoCruceRow(
                cost,
                probableBilling.Billing,
                closeMonth,
                billingMonth,
                LicenciamientoCruceStatusProbable,
                RoundCurrency(probableBilling.Score * 100m),
                marginThresholdPercent));
        }

        foreach (var billing in billingGroups.Where(billing => !usedBillingKeys.Contains(billing.GroupKey)))
        {
            rows.Add(BuildLicenciamientoCruceBillingOnlyRow(billing, closeMonth, billingMonth));
        }

        return rows;
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceRow(
        LicenciamientoCruceCostGroup cost,
        LicenciamientoCruceBillingGroup billing,
        DateOnly closeMonth,
        DateOnly billingMonth,
        string status,
        decimal matchScore,
        decimal marginThresholdPercent)
    {
        var margin = RoundCurrency(billing.BillingWithoutVat - cost.CostCop);
        var marginPct = CalculateLicenciamientoCruceMarginPercent(margin, billing.BillingWithoutVat);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"match:{cost.GroupKey}:{billing.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(cost.CostMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            Cliente = FirstNonEmpty(billing.ClientName, cost.ClientName, "Cliente sin nombre"),
            NitCliente = billing.Nit,
            ProductoLicencia = cost.ProductSummary,
            Vertical = FirstNonEmpty(billing.Vertical, cost.Vertical, "Licenciamiento"),
            CostoLicenciamiento = cost.CostCop,
            FacturacionSinIva = billing.BillingWithoutVat,
            MargenBruto = margin,
            MargenBrutoPct = marginPct,
            EstadoCruce = status,
            FuenteCosto = "cr07a_consumointcomex",
            FuenteFacturacion = "cr07a_facturacion",
            CostRecordCount = cost.RecordIds.Count,
            BillingRecordCount = billing.BillingRecordIds.Count,
            MatchScore = matchScore,
            IsMarginAlert = margin < 0m || (marginPct.HasValue && marginPct.Value < marginThresholdPercent)
        };
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceCostOnlyRow(
        LicenciamientoCruceCostGroup cost,
        DateOnly closeMonth,
        DateOnly billingMonth)
    {
        var margin = RoundCurrency(0m - cost.CostCop);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"cost:{cost.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(cost.CostMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            Cliente = cost.ClientName,
            ProductoLicencia = cost.ProductSummary,
            Vertical = FirstNonEmpty(cost.Vertical, "Licenciamiento"),
            CostoLicenciamiento = cost.CostCop,
            FacturacionSinIva = 0m,
            MargenBruto = margin,
            MargenBrutoPct = null,
            EstadoCruce = LicenciamientoCruceStatusCostOnly,
            FuenteCosto = "cr07a_consumointcomex",
            FuenteFacturacion = "",
            CostRecordCount = cost.RecordIds.Count,
            BillingRecordCount = 0,
            MatchScore = 0m,
            IsMarginAlert = true
        };
    }

    private static LicenciamientoCruceRowDto BuildLicenciamientoCruceBillingOnlyRow(
        LicenciamientoCruceBillingGroup billing,
        DateOnly closeMonth,
        DateOnly billingMonth)
    {
        var margin = RoundCurrency(billing.BillingWithoutVat);
        var marginPct = CalculateLicenciamientoCruceMarginPercent(margin, billing.BillingWithoutVat);

        return new LicenciamientoCruceRowDto
        {
            RowKey = $"billing:{billing.GroupKey}",
            MesCierre = FormatLicenciamientoCruceMonth(closeMonth),
            MesCosto = FormatLicenciamientoCruceMonth(closeMonth),
            MesFacturacion = FormatLicenciamientoCruceMonth(billingMonth),
            Cliente = billing.ClientName,
            NitCliente = billing.Nit,
            ProductoLicencia = "",
            Vertical = FirstNonEmpty(billing.Vertical, "Sin vertical"),
            CostoLicenciamiento = 0m,
            FacturacionSinIva = billing.BillingWithoutVat,
            MargenBruto = margin,
            MargenBrutoPct = marginPct,
            EstadoCruce = LicenciamientoCruceStatusBillingOnly,
            FuenteCosto = "",
            FuenteFacturacion = "cr07a_facturacion",
            CostRecordCount = 0,
            BillingRecordCount = billing.BillingRecordIds.Count,
            MatchScore = 0m,
            IsMarginAlert = false
        };
    }

    private static LicenciamientoCruceStatusCountsDto BuildLicenciamientoCruceStatusCounts(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        return new LicenciamientoCruceStatusCountsDto
        {
            MatchExacto = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusExact, StringComparison.OrdinalIgnoreCase)),
            MatchProbable = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusProbable, StringComparison.OrdinalIgnoreCase)),
            CostoSinFacturacion = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)),
            FacturacionSinCosto = rows.Count(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static IReadOnlyList<LicenciamientoCruceMonthSummaryDto> BuildLicenciamientoCruceMonthSummaries(
        IReadOnlyList<LicenciamientoCruceRowDto> rows)
    {
        return rows
            .GroupBy(static row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var cost = RoundCurrency(group.Sum(static row => row.CostoLicenciamiento));
                var billing = RoundCurrency(group.Sum(static row => row.FacturacionSinIva));
                var margin = RoundCurrency(billing - cost);
                return new LicenciamientoCruceMonthSummaryDto
                {
                    MesCierre = group.Key,
                    CostosLicenciamiento = cost,
                    FacturacionRelacionada = billing,
                    MargenBruto = margin,
                    MargenBrutoPct = CalculateLicenciamientoCruceMarginPercent(margin, billing)
                };
            })
            .OrderBy(static row => row.MesCierre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LicenciamientoCruceAlertDto> BuildLicenciamientoCruceAlerts(
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        decimal marginThresholdPercent)
    {
        var negativeRows = rows.Where(static row => row.MargenBruto < 0m).ToList();
        var lowMarginRows = rows
            .Where(row => row.MargenBrutoPct.HasValue
                && row.FacturacionSinIva > 0m
                && row.MargenBrutoPct.Value < marginThresholdPercent)
            .ToList();
        var costOnlyRows = rows
            .Where(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var billingOnlyRows = rows
            .Where(static row => string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new[]
        {
            new LicenciamientoCruceAlertDto
            {
                Key = "negative-margin",
                Label = "Margen negativo",
                Severity = negativeRows.Count > 0 ? "danger" : "ok",
                Count = negativeRows.Count,
                Value = RoundCurrency(negativeRows.Sum(static row => row.MargenBruto))
            },
            new LicenciamientoCruceAlertDto
            {
                Key = "cost-without-billing",
                Label = "Costo sin facturacion",
                Severity = costOnlyRows.Count > 0 ? "warning" : "ok",
                Count = costOnlyRows.Count,
                Value = RoundCurrency(costOnlyRows.Sum(static row => row.CostoLicenciamiento))
            },
            new LicenciamientoCruceAlertDto
            {
                Key = "billing-without-cost",
                Label = "Facturacion sin costo",
                Severity = billingOnlyRows.Count > 0 ? "warning" : "ok",
                Count = billingOnlyRows.Count,
                Value = RoundCurrency(billingOnlyRows.Sum(static row => row.FacturacionSinIva))
            },
            new LicenciamientoCruceAlertDto
            {
                Key = "low-margin",
                Label = $"Margen menor a {marginThresholdPercent:N2}%",
                Severity = lowMarginRows.Count > 0 ? "warning" : "ok",
                Count = lowMarginRows.Count,
                Value = RoundCurrency(lowMarginRows.Sum(static row => row.MargenBruto))
            }
        };
    }

    private IReadOnlyList<LicenciamientoCruceValidationDto> BuildLicenciamientoCruceValidations(
        IReadOnlyList<LicenciamientoCruceCostRow> costRows,
        IReadOnlyList<BillingRecordRow> billingRows,
        IReadOnlyList<LicenciamientoCruceRowDto> rows,
        decimal totalCostSource,
        decimal totalCostCross,
        DateOnly billingMonth)
    {
        var invalidVatRows = billingRows.Count(static row => row.TotalInvoice < row.VatValue);
        var missingVatValueRows = billingRows.Count(static row => row.TotalInvoice > 0m && row.VatPercent > 0m && Math.Abs(row.VatValue) < 0.01m);
        var duplicateCostCount = costRows.Count - costRows.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var duplicateBillingCount = billingRows.Count - billingRows.Select(static row => row.RecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var unmatchedCount = rows.Count(static row =>
            string.Equals(row.EstadoCruce, LicenciamientoCruceStatusCostOnly, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.EstadoCruce, LicenciamientoCruceStatusBillingOnly, StringComparison.OrdinalIgnoreCase));
        var costDelta = Math.Abs(totalCostSource - totalCostCross);

        return new[]
        {
            new LicenciamientoCruceValidationDto
            {
                Key = "cost-date",
                Label = "Fecha costo",
                Status = "ok",
                Detail = $"Los costos se filtran por {LicensingInvoiceDateField} en {FormatLicenciamientoCruceMonth(billingMonth)}; no se usa modifiedon."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "billing-date",
                Label = "Fecha facturacion",
                Status = "ok",
                Detail = $"La facturacion se filtra por {_dashboardBillingEmissionDateField}; no se usa fecha de modificacion."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "billing-without-vat",
                Label = "Facturacion sin IVA",
                Status = invalidVatRows > 0 ? "error" : missingVatValueRows > 0 ? "warning" : "ok",
                Detail = invalidVatRows > 0
                    ? $"{invalidVatRows:N0} factura(s) tienen IVA mayor al total."
                    : missingVatValueRows > 0
                        ? $"{missingVatValueRows:N0} factura(s) tienen porcentaje de IVA pero valor IVA en cero; se uso total_factura - iva."
                        : "La base se calcula como total_factura - iva."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "duplicates",
                Label = "Duplicados",
                Status = duplicateCostCount > 0 || duplicateBillingCount > 0 ? "warning" : "ok",
                Detail = $"Costos duplicados por ID: {duplicateCostCount:N0}. Facturas duplicadas por ID: {duplicateBillingCount:N0}."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "unmatched-visible",
                Label = "Sin match visibles",
                Status = "ok",
                Detail = $"{unmatchedCount:N0} registro(s) sin match quedan visibles en el detalle."
            },
            new LicenciamientoCruceValidationDto
            {
                Key = "cost-total",
                Label = "Total costos",
                Status = costDelta < 0.01m ? "ok" : "error",
                Detail = $"Fuente: {totalCostSource:N2}. Cruce: {totalCostCross:N2}."
            }
        };
    }

    private static decimal CalculateLicenciamientoCruceBillingWithoutVat(BillingRecordRow row)
    {
        var value = row.TotalInvoice - row.VatValue;
        return RoundCurrency(value < 0m ? 0m : value);
    }

    private static decimal? CalculateLicenciamientoCruceMarginPercent(decimal margin, decimal billingWithoutVat)
    {
        if (Math.Abs(billingWithoutVat) < 0.01m)
            return null;

        return RoundCurrency((margin / billingWithoutVat) * 100m);
    }

    private static void AddLicenciamientoCruceMatchKeys(HashSet<string> keys, params string?[] values)
    {
        foreach (var value in values)
        {
            var clientKey = NormalizeLicenciamientoCruceClientKey(value);
            if (!string.IsNullOrWhiteSpace(clientKey))
                keys.Add($"name:{clientKey}");

            var taxKey = NormalizeLicenciamientoCruceTaxId(value);
            if (!string.IsNullOrWhiteSpace(taxKey) && taxKey.Length >= 5)
                keys.Add($"tax:{taxKey}");
        }
    }

    private static string BuildLicenciamientoCruceGroupingKey(params string?[] values)
    {
        foreach (var value in values)
        {
            var taxKey = NormalizeLicenciamientoCruceTaxId(value);
            if (!string.IsNullOrWhiteSpace(taxKey) && taxKey.Length >= 5)
                return $"tax:{taxKey}";
        }

        foreach (var value in values)
        {
            var clientKey = NormalizeLicenciamientoCruceClientKey(value);
            if (!string.IsNullOrWhiteSpace(clientKey))
                return $"name:{clientKey}";
        }

        return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    private static decimal CalculateLicenciamientoCruceClientSimilarity(string? left, string? right)
    {
        var leftKey = NormalizeLicenciamientoCruceClientKey(left);
        var rightKey = NormalizeLicenciamientoCruceClientKey(right);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
            return 0m;

        if (string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (leftKey.Contains(rightKey, StringComparison.OrdinalIgnoreCase)
            || rightKey.Contains(leftKey, StringComparison.OrdinalIgnoreCase))
        {
            var minLength = Math.Min(leftKey.Length, rightKey.Length);
            var maxLength = Math.Max(leftKey.Length, rightKey.Length);
            return maxLength == 0 ? 0m : minLength / (decimal)maxLength;
        }

        var leftTokens = leftKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTokens = rightKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0m;

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0m : intersection / (decimal)union;
    }

    private static string NormalizeLicenciamientoCruceClientKey(string? value)
    {
        var text = RemoveLicenciamientoCruceDiacritics(value ?? "").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        var tokens = builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !LicenciamientoCruceLegalTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return string.Join(" ", tokens);
    }

    private static string NormalizeLicenciamientoCruceTaxId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string RemoveLicenciamientoCruceDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static DateOnly? TryParseLicenciamientoCruceMonth(string? rawValue)
    {
        var raw = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (TryParseDateOnly(raw, out var parsedDate))
            return new DateOnly(parsedDate.Year, parsedDate.Month, 1);

        var normalized = raw
            .Replace('.', '/')
            .Replace('-', '/')
            .Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var first = parts[0];
        var second = parts[1];
        int year;
        int month;
        if (first.Length == 4)
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out year)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out month))
                return null;
        }
        else
        {
            if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return null;
        }

        if (month is < 1 or > 12 || year is < 1900 or > 2100)
            return null;

        return new DateOnly(year, month, 1);
    }

    private static string ResolveLicenciamientoCruceMostCommonText(IEnumerable<string?> values, string fallback)
    {
        return values
            .Select(static value => (value ?? "").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Key)
            .FirstOrDefault() ?? fallback;
    }

    private static DateOnly? ResolveLicenciamientoCruceMostCommonMonth(IEnumerable<DateOnly> values)
    {
        return values
            .Select(static value => new DateOnly(value.Year, value.Month, 1))
            .GroupBy(static value => value)
            .OrderByDescending(static group => group.Count())
            .ThenByDescending(static group => group.Key)
            .Select(static group => (DateOnly?)group.Key)
            .FirstOrDefault();
    }

    private static string BuildLicenciamientoCruceProductSummary(IEnumerable<string?> productNames)
    {
        var products = productNames
            .Select(static value => (value ?? "").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return products.Count switch
        {
            0 => "Sin licencia",
            1 => products[0],
            <= 3 => string.Join(", ", products),
            _ => $"{products.Count:N0} licencias"
        };
    }

    private static string FormatLicenciamientoCruceMonth(DateOnly value) =>
        value.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static int ResolveLicenciamientoCruceStateOrder(string status)
    {
        return status switch
        {
            LicenciamientoCruceStatusCostOnly => 0,
            LicenciamientoCruceStatusBillingOnly => 1,
            LicenciamientoCruceStatusProbable => 2,
            LicenciamientoCruceStatusExact => 3,
            _ => 4
        };
    }

    private sealed class LicenciamientoCruceCostRow
    {
        public string RecordId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string CompanyAccountId { get; init; } = "";
        public string CompanyAccountDisplay { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string Vendor { get; init; } = "";
        public DateOnly? InvoiceDate { get; init; }
        public DateOnly CostMonth { get; init; }
        public decimal CostCop { get; init; }
    }

    private sealed class LicenciamientoCruceCostGroup
    {
        public string GroupKey { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string ProductSummary { get; init; } = "";
        public string Vertical { get; init; } = "";
        public DateOnly CostMonth { get; init; }
        public decimal CostCop { get; init; }
        public IReadOnlyList<string> RecordIds { get; init; } = Array.Empty<string>();
        public HashSet<string> MatchKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LicenciamientoCruceBillingGroup
    {
        public string GroupKey { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string Nit { get; init; } = "";
        public string Vertical { get; init; } = "";
        public decimal BillingWithoutVat { get; init; }
        public IReadOnlyList<string> BillingRecordIds { get; init; } = Array.Empty<string>();
        public HashSet<string> MatchKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasInvalidVat { get; init; }
        public bool HasMissingVatValue { get; init; }
    }
}
