using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int DashboardVerticalCloudOption = 645250000;
    private const int DashboardVerticalCopiersOption = 645250001;
    private const int DashboardContractTypeMonthlyOption = 645250000;
    private const int DashboardContractTypeOneTimeOption = 645250001;
    private const decimal DashboardAutoFuenteRate = 0.00414m;
    private const decimal DashboardIcaRate = 0.0069m;
    private const string DashboardExpensePaymentDateField = "cr07a_fechadepago";
    private const string DashboardExpensePaymentDateFieldKind = "date-only";
    private const string DashboardExpensePaymentValueField = "cr07a_valorpago";
    private const string DashboardExpenseReteFuenteField = "cr07a_retefuente";
    private const string DashboardExpenseRecipientNameField = "cr07a_nombrereceptor";
    private const string DashboardExpenseRecipientNitField = "cr07a_nitreceptor";
    private const string DashboardExpenseCloudField = "cr07a_cloud";
    private const string DashboardExpenseCopiersField = "cr07a_copiers";
    private const string DashboardCopiersAdditionalOperationField = "cr07a_operacionadicional";
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("es-CO");
    private readonly ConcurrentDictionary<string, string> _dashboardAttributeTypeCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PortfolioDashboardDto> GetPortfolioDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var portfolioCandidates = await GetBillingRecordsAsync(
            metadata,
            new DateOnly(2000, 1, 1),
            new DateOnly(2101, 1, 1),
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var unpaidInvoices = portfolioCandidates
            .Where(static record => !record.HasPayment)
            .ToList();

        var overdueInvoices = unpaidInvoices
            .Where(record => record.IsOverdue(today))
            .ToList();

        return new PortfolioDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Cartera total y vencida",
            HasData = unpaidInvoices.Count > 0,
            RecordsCount = overdueInvoices.Count,
            EmptyStateTitle = "No encontramos facturas pendientes de pago.",
            EmptyStateMessage = "Cuando existan facturas sin pago o vencidas las veras aqui.",
            Kpis = BuildPortfolioKpis(unpaidInvoices, overdueInvoices),
            OverdueInvoices = BuildUnpaidInvoices(overdueInvoices, today)
        };
    }

    public async Task<CopiersDashboardDto> GetCopiersDashboardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            httpContext.User,
            ct);

        var rows = await GetCopiersRecordsAsync(metadata, httpContext.User, ct);

        return new CopiersDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = "Ordenado por dia de facturacion, cliente y producto",
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            EmptyStateTitle = "No encontramos registros de facturacion copiers.",
            EmptyStateMessage = "Cuando Dataverse tenga filas en cr07a_productoscopiers las veras aqui.",
            Kpis = BuildCopiersKpis(rows),
            Rows = BuildCopiersRows(rows)
        };
    }

    public async Task<CopiersClientInvoicesDetailDto> GetCopiersClientInvoicesAsync(
        string clientId,
        string? clientName = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedClientId = NormalizeOptionalGuid(clientId);
        if (string.IsNullOrWhiteSpace(normalizedClientId) && !string.IsNullOrWhiteSpace(clientName))
        {
            normalizedClientId = await ResolveCopiersClientIdAsync(clientName.Trim(), ct);
        }

        if (string.IsNullOrWhiteSpace(normalizedClientId))
            throw new InvalidOperationException("No encontramos un cliente valido para consultar sus facturas emitidas.");

        var invoices = await GetBillingRecordsByClientAsync(metadata, normalizedClientId, httpContext.User, ct);
        var resolvedClientName = FirstNonEmpty(
            invoices.Select(static row => row.ClientName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            clientName?.Trim(),
            "Cliente");

        return new CopiersClientInvoicesDetailDto
        {
            ClientId = normalizedClientId,
            ClientName = resolvedClientName,
            HasData = invoices.Count > 0,
            RecordsCount = invoices.Count,
            EmptyStateTitle = "No encontramos facturas emitidas para este cliente.",
            EmptyStateMessage = "Cuando existan registros emitidos en cr07a_facturacion para este cliente los veras aqui.",
            Invoices = invoices
                .Select(row => new CopiersClientInvoiceRowDto
                {
                    RecordId = row.RecordId,
                    InvoiceNumber = row.InvoiceNumber,
                    TotalInvoice = row.TotalInvoice,
                    EmissionDateValue = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    EmissionDateDisplay = row.EmissionDate?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha"
                })
                .ToList()
        };
    }

    public async Task<CopiersRecordSaveResultDto> SaveCopiersRecordAsync(CopiersRecordSaveRequestDto request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedRecordId = NormalizeOptionalGuid(request.RecordId);
        var isCreate = string.IsNullOrWhiteSpace(normalizedRecordId);
        var current = isCreate
            ? null
            : await GetCopiersRecordByIdAsync(metadata, normalizedRecordId!, httpContext.User, ct)
                ?? throw new InvalidOperationException("No encontramos el registro de facturacion copiers que quieres editar.");

        var payload = await BuildCopiersSavePayloadAsync(metadata, request, current, httpContext.User, ct);
        var relativeUrl = isCreate
            ? $"/api/data/v9.2/{metadata.EntitySetName}"
            : $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})";

        await CallDataverseSendAsync(relativeUrl, isCreate ? "POST" : "PATCH", payload, httpContext.User, ct);

        return new CopiersRecordSaveResultDto
        {
            RecordId = normalizedRecordId ?? "",
            IsCreated = isCreate,
            Message = isCreate
                ? "Registro creado correctamente en facturacion copiers."
                : "Registro actualizado correctamente en facturacion copiers."
        };
    }

    public async Task<BillingDashboardDto> GetBillingDashboardAsync(
        int year,
        BillingPeriodKind periodKind,
        int? periodValue = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var period = BuildBillingPeriodDefinition(year, periodKind, periodValue, today);
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var emissionRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var paymentRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentDateFieldKind,
            httpContext.User,
            ct);

        var currentEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CurrentStartInclusive
                && record.EmissionDate.Value < period.CurrentEndExclusive)
            .ToList();

        var compareEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CompareStartInclusive
                && record.EmissionDate.Value < period.CompareEndExclusive)
            .ToList();

        var currentPayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();

        var comparePayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CompareStartInclusive
                && record.PaymentDate.Value < period.CompareEndExclusive)
            .ToList();

        var totalBilling = SumCurrency(currentEmission, static record => record.TotalInvoice);
        var previousTotalBilling = SumCurrency(compareEmission, static record => record.TotalInvoice);
        var totalCollections = SumCurrency(currentPayments, static record => record.PaymentValue);
        var previousTotalCollections = SumCurrency(comparePayments, static record => record.PaymentValue);
        var totalVat = SumCurrency(currentEmission, static record => record.VatValue);
        var previousTotalVat = SumCurrency(compareEmission, static record => record.VatValue);
        var totalRetentions = SumCurrency(currentPayments, static record => record.RetentionsTotal);
        var previousTotalRetentions = SumCurrency(comparePayments, static record => record.RetentionsTotal);
        var unpaidInvoices = BuildUnpaidInvoices(currentEmission, today);
        var previousUnpaidAmount = SumCurrency(compareEmission.Where(static record => !record.HasPayment), static record => record.TotalInvoice);
        var differenceInvoices = BuildDifferenceInvoices(currentEmission);
        var previousDifferenceAmount = SumCurrency(
            compareEmission.Where(static record => record.HasPayment),
            static record => Math.Abs(record.DifferenceValue));

        var hasData = currentEmission.Count > 0
            || compareEmission.Count > 0
            || currentPayments.Count > 0
            || comparePayments.Count > 0;

        return new BillingDashboardDto
        {
            Year = period.Year,
            CompareYear = period.CompareYear,
            PeriodKind = period.PeriodKind.ToKey(),
            PeriodKindLabel = period.PeriodKind.ToLabel(),
            PeriodValue = period.PeriodValue,
            PeriodLabel = period.PeriodLabel,
            DateRangeLabel = period.DateRangeLabel,
            CompareLabel = period.CompareLabel,
            GranularityLabel = period.GranularityLabel,
            EmptyStateTitle = "No encontramos facturacion para este periodo.",
            EmptyStateMessage = "Cambia el rango y seguimos comparando contra el mismo periodo del año anterior.",
            HasData = hasData,
            RecordsCount = currentEmission.Count,
            CompareRecordsCount = compareEmission.Count,
            Kpis = BuildBillingKpis(
                currentEmission,
                compareEmission,
                currentPayments,
                comparePayments,
                totalBilling,
                previousTotalBilling,
                totalCollections,
                previousTotalCollections,
                totalVat,
                previousTotalVat,
                totalRetentions,
                previousTotalRetentions,
                unpaidInvoices,
                previousUnpaidAmount,
                differenceInvoices,
                previousDifferenceAmount),
            Trend = BuildBillingTrend(period, currentEmission, compareEmission, currentPayments, comparePayments),
            Verticals = BuildVerticalSummaries(currentEmission, compareEmission),
            TopClients = BuildClientSummaries(currentEmission, compareEmission),
            Retentions = BuildRetentionSummaries(currentPayments, comparePayments),
            UnpaidInvoices = unpaidInvoices,
            DifferenceInvoices = differenceInvoices
        };
    }

    public async Task<TaxesDashboardDto> GetTaxesDashboardAsync(
        int year,
        BillingPeriodKind periodKind,
        int? periodValue = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var period = BuildBillingPeriodDefinition(year, periodKind, periodValue, today);
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var emissionRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var paymentRecords = await GetBillingRecordsAsync(
            metadata,
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentDateFieldKind,
            httpContext.User,
            ct);

        var expenseRecords = await GetTaxExpenseRowsAsync(
            period.CompareStartInclusive,
            period.CurrentEndExclusive,
            httpContext.User,
            ct);

        var currentEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CurrentStartInclusive
                && record.EmissionDate.Value < period.CurrentEndExclusive)
            .ToList();

        var compareEmission = emissionRecords
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CompareStartInclusive
                && record.EmissionDate.Value < period.CompareEndExclusive)
            .ToList();

        var currentPayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();

        var comparePayments = paymentRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CompareStartInclusive
                && record.PaymentDate.Value < period.CompareEndExclusive)
            .ToList();

        var currentExpenses = expenseRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();

        var compareExpenses = expenseRecords
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CompareStartInclusive
                && record.PaymentDate.Value < period.CompareEndExclusive)
            .ToList();

        var hasData = currentEmission.Count > 0
            || compareEmission.Count > 0
            || currentPayments.Count > 0
            || comparePayments.Count > 0
            || currentExpenses.Count > 0
            || compareExpenses.Count > 0;

        var expenseDetails = BuildTaxExpenseDetails(currentExpenses);
        var currentExpenseVerticals = CalculateExpenseRetentionByVertical(currentExpenses);
        var compareExpenseVerticals = CalculateExpenseRetentionByVertical(compareExpenses);

        var currentClientReteFuente = SumCurrency(currentPayments, static row => row.RteFteValue);
        var compareClientReteFuente = SumCurrency(comparePayments, static row => row.RteFteValue);
        var currentExpenseReteFuente = SumExpenseCurrency(currentExpenses, static row => row.ReteFuenteValue);
        var compareExpenseReteFuente = SumExpenseCurrency(compareExpenses, static row => row.ReteFuenteValue);
        var currentAutoFuente = CalculateAutoFuente(currentEmission);
        var compareAutoFuente = CalculateAutoFuente(compareEmission);
        var currentAutoFuenteVerticals = SumBillingCurrencyByVertical(currentEmission, static row => CalculateInvoiceTaxBase(row) * DashboardAutoFuenteRate);
        var compareAutoFuenteVerticals = SumBillingCurrencyByVertical(compareEmission, static row => CalculateInvoiceTaxBase(row) * DashboardAutoFuenteRate);
        var currentReteFuentePayable = RoundCurrency(currentAutoFuente + currentExpenseReteFuente);
        var compareReteFuentePayable = RoundCurrency(compareAutoFuente + compareExpenseReteFuente);
        var currentReteFuentePayableVerticals = SumTaxVerticalAmounts(currentAutoFuenteVerticals, currentExpenseVerticals);
        var compareReteFuentePayableVerticals = SumTaxVerticalAmounts(compareAutoFuenteVerticals, compareExpenseVerticals);

        var currentGeneratedVat = SumCurrency(currentEmission, static row => row.VatValue);
        var compareGeneratedVat = SumCurrency(compareEmission, static row => row.VatValue);
        var currentClientReteIva = SumCurrency(currentPayments, static row => row.RteIvaValue);
        var compareClientReteIva = SumCurrency(comparePayments, static row => row.RteIvaValue);
        var currentVatPeriod = RoundCurrency(currentGeneratedVat - currentClientReteIva);
        var compareVatPeriod = RoundCurrency(compareGeneratedVat - compareClientReteIva);
        var currentGeneratedVatVerticals = SumBillingCurrencyByVertical(currentEmission, static row => row.VatValue);
        var compareGeneratedVatVerticals = SumBillingCurrencyByVertical(compareEmission, static row => row.VatValue);
        var currentClientReteIvaVerticals = SumBillingCurrencyByVertical(currentPayments, static row => row.RteIvaValue);
        var compareClientReteIvaVerticals = SumBillingCurrencyByVertical(comparePayments, static row => row.RteIvaValue);
        var currentVatPeriodVerticals = SubtractTaxVerticalAmounts(currentGeneratedVatVerticals, currentClientReteIvaVerticals);
        var compareVatPeriodVerticals = SubtractTaxVerticalAmounts(compareGeneratedVatVerticals, compareClientReteIvaVerticals);

        var currentIcaPeriodValue = CalculateIcaGenerated(currentEmission);
        var compareIcaPeriodValue = CalculateIcaGenerated(compareEmission);
        var currentClientReteIca = SumCurrency(currentPayments, static row => row.ReteIcaValue);
        var compareClientReteIca = SumCurrency(comparePayments, static row => row.ReteIcaValue);
        var currentIcaTotal = RoundCurrency(currentIcaPeriodValue - currentClientReteIca);
        var compareIcaTotal = RoundCurrency(compareIcaPeriodValue - compareClientReteIca);
        var currentIcaGeneratedVerticals = SumBillingCurrencyByVertical(currentEmission, static row => CalculateInvoiceTaxBase(row) * DashboardIcaRate);
        var compareIcaGeneratedVerticals = SumBillingCurrencyByVertical(compareEmission, static row => CalculateInvoiceTaxBase(row) * DashboardIcaRate);
        var currentClientReteIcaVerticals = SumBillingCurrencyByVertical(currentPayments, static row => row.ReteIcaValue);
        var compareClientReteIcaVerticals = SumBillingCurrencyByVertical(comparePayments, static row => row.ReteIcaValue);
        var currentIcaTotalVerticals = SubtractTaxVerticalAmounts(currentIcaGeneratedVerticals, currentClientReteIcaVerticals);
        var compareIcaTotalVerticals = SubtractTaxVerticalAmounts(compareIcaGeneratedVerticals, compareClientReteIcaVerticals);

        return new TaxesDashboardDto
        {
            Year = period.Year,
            CompareYear = period.CompareYear,
            PeriodKind = period.PeriodKind.ToKey(),
            PeriodKindLabel = period.PeriodKind.ToLabel(),
            PeriodValue = period.PeriodValue,
            PeriodLabel = period.PeriodLabel,
            DateRangeLabel = period.DateRangeLabel,
            CompareLabel = period.CompareLabel,
            GranularityLabel = period.GranularityLabel,
            EmptyStateTitle = "No encontramos movimientos de impuestos para este periodo.",
            EmptyStateMessage = "Cambia el rango y seguimos comparando contra el mismo periodo del año anterior.",
            HasData = hasData,
            RecordsCount = currentEmission.Count + currentPayments.Count + currentExpenses.Count,
            ReteFuente = BuildTaxesSection(
                "retefuente",
                "Retefuente",
                "Prioriza el total a pagar del periodo y separa sus componentes por vertical: autofuente mas retefuente practicada en gastos.",
                new[]
                {
                    BuildBillingKpi("rtefte-payable", "Total a pagar", "Autofuente del periodo mas retefuente practicada en gastos.", currentReteFuentePayable, compareReteFuentePayable, "currency", "", ""),
                    BuildBillingKpi("autofuente", "Autofuente del periodo", "Formula: (Total factura - IVA) x 0.00414 para la facturacion emitida.", currentAutoFuente, compareAutoFuente, "currency", "", ""),
                    BuildBillingKpi("expense-rtefte", "Retefuente en gastos", "Retefuente practicada en gastos pagados durante el periodo.", currentExpenseReteFuente, compareExpenseReteFuente, "currency", "Registros", expenseDetails.Count.ToString("N0", DashboardCulture)),
                    BuildBillingKpi("client-rtefte", "Clientes nos retuvieron", "ReteFuente retenida por clientes sobre pagos del periodo.", currentClientReteFuente, compareClientReteFuente, "currency", "", "")
                },
                BuildTaxVerticalSummaries(
                    "Total a pagar",
                    currentReteFuentePayableVerticals,
                    compareReteFuentePayableVerticals,
                    new TaxVerticalComponentSet("autofuente", "Autofuente", currentAutoFuenteVerticals, compareAutoFuenteVerticals),
                    new TaxVerticalComponentSet("expense-rtefte", "Retefuente gastos", currentExpenseVerticals, compareExpenseVerticals))),
            ReteIva = BuildTaxesSection(
                "reteiva",
                "Rete IVA",
                "Mide el IVA del periodo y muestra cuanto queda a pagar por vertical despues de la reteIVA practicada por clientes.",
                new[]
                {
                    BuildBillingKpi("vat-net", "IVA a pagar", "IVA generado menos reteIVA practicada por clientes.", currentVatPeriod, compareVatPeriod, "currency", "", ""),
                    BuildBillingKpi("generated-vat", "IVA generado", "IVA de la facturacion emitida en el periodo.", currentGeneratedVat, compareGeneratedVat, "currency", "", ""),
                    BuildBillingKpi("client-rteiva", "ReteIVA clientes", "ReteIVA retenida por clientes sobre pagos del periodo.", currentClientReteIva, compareClientReteIva, "currency", "", "")
                },
                BuildTaxVerticalSummaries(
                    "IVA a pagar",
                    currentVatPeriodVerticals,
                    compareVatPeriodVerticals,
                    new TaxVerticalComponentSet("generated-vat", "IVA generado", currentGeneratedVatVerticals, compareGeneratedVatVerticals),
                    new TaxVerticalComponentSet("client-rteiva", "ReteIVA clientes", currentClientReteIvaVerticals, compareClientReteIvaVerticals))),
            ReteIca = BuildTaxesSection(
                "reteica",
                "Rete ICA",
                "Calcula el total del periodo y lo reparte por vertical con ICA generado menos ICA retenido por clientes.",
                new[]
                {
                    BuildBillingKpi("ica-net", "Total del periodo", "ICA generado menos ICA retenido por clientes.", currentIcaTotal, compareIcaTotal, "currency", "", ""),
                    BuildBillingKpi("generated-ica", "ICA del periodo", "Formula: (Total factura - IVA) x 0.0069 para la facturacion emitida.", currentIcaPeriodValue, compareIcaPeriodValue, "currency", "", ""),
                    BuildBillingKpi("client-reteica", "ICA retenido clientes", "ICA retenido por clientes sobre pagos del periodo.", currentClientReteIca, compareClientReteIca, "currency", "", "")
                },
                BuildTaxVerticalSummaries(
                    "Total del periodo",
                    currentIcaTotalVerticals,
                    compareIcaTotalVerticals,
                    new TaxVerticalComponentSet("generated-ica", "ICA generado", currentIcaGeneratedVerticals, compareIcaGeneratedVerticals),
                    new TaxVerticalComponentSet("client-reteica", "ICA retenido clientes", currentClientReteIcaVerticals, compareClientReteIcaVerticals))),
            ExpenseDetails = expenseDetails
        };
    }

    private async Task<List<CopiersBillingRecordRow>> GetCopiersRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            return await GetCopiersRecordsCoreAsync(metadata, user, ct, preferProductLookup: false);
        }
        catch (InvalidOperationException ex) when (ShouldRetryCopiersQueryWithProductLookup(ex))
        {
            return await GetCopiersRecordsCoreAsync(metadata, user, ct, preferProductLookup: true);
        }
    }

    private async Task<List<CopiersBillingRecordRow>> GetCopiersRecordsCoreAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool preferProductLookup)
    {
        var select = BuildCopiersSelectClause(metadata, preferProductLookup);

        var orderBy = Uri.EscapeDataString($"{_dashboardCopiersBillingDayField} asc");
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseCopiersRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static item => item is not null)
            .Cast<CopiersBillingRecordRow>()
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(static item => item.BillingDay is >= 1 and <= 31 ? item.BillingDay : int.MaxValue)
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string BuildCopiersSelectClause(RhEntityMetadata metadata, bool preferProductLookup)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            _dashboardCopiersQuantityField,
            preferProductLookup
                ? BuildDashboardLookupValuePropertyName(_dashboardCopiersProductField)
                : _dashboardCopiersProductField,
            _dashboardCopiersUnitValueBeforeVatField,
            _dashboardCopiersBillingDayField,
            _dashboardCopiersIncludedOperationsField,
            DashboardCopiersAdditionalOperationField,
            BuildDashboardLookupValuePropertyName(_dashboardCopiersClientField),
            _dashboardCopiersUnitValueWithVatField,
            _dashboardCopiersTotalWithVatField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private bool ShouldRetryCopiersQueryWithProductLookup(InvalidOperationException exception)
    {
        var lookupField = BuildDashboardLookupValuePropertyName(_dashboardCopiersProductField);
        if (string.IsNullOrWhiteSpace(_dashboardCopiersProductField)
            || string.Equals(lookupField, _dashboardCopiersProductField, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return exception.Message.Contains(_dashboardCopiersProductField, StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CopiersBillingRecordRow?> GetCopiersRecordByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            return await GetCopiersRecordByIdCoreAsync(metadata, recordId, user, ct, preferProductLookup: false);
        }
        catch (InvalidOperationException ex) when (ShouldRetryCopiersQueryWithProductLookup(ex))
        {
            return await GetCopiersRecordByIdCoreAsync(metadata, recordId, user, ct, preferProductLookup: true);
        }
    }

    private async Task<CopiersBillingRecordRow?> GetCopiersRecordByIdCoreAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool preferProductLookup)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={BuildCopiersSelectClause(metadata, preferProductLookup)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return ParseCopiersRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private CopiersBillingRecordRow? ParseCopiersRecord(JsonElement item, string primaryIdField, string primaryNameField)
    {
        var productName = ReadCopiersFieldDisplayValue(
            item,
            _dashboardCopiersProductField,
            "producto",
            FirstNonEmpty(ReadString(item, primaryNameField).Trim(), "Producto sin nombre"));
        var clientName = ReadCopiersFieldDisplayValue(
            item,
            _dashboardCopiersClientField,
            "cliente",
            "Cliente sin nombre");
        var billingDay = ReadIntFlexible(item, _dashboardCopiersBillingDayField);
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, _dashboardCopiersIdField),
            $"{clientName}|{productName}|{billingDay}");

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new CopiersBillingRecordRow
        {
            RecordId = recordId.Trim(),
            ClientId = ReadCopiersLookupId(item, _dashboardCopiersClientField, "cliente"),
            ProductId = ReadCopiersLookupId(item, _dashboardCopiersProductField, "producto"),
            ClientName = clientName,
            ProductName = productName,
            Quantity = RoundCurrency(ReadDecimal(item, _dashboardCopiersQuantityField) ?? 0m),
            IncludedOperations = RoundCurrency(ReadDecimal(item, _dashboardCopiersIncludedOperationsField) ?? 0m),
            AdditionalOperation = RoundCurrency(ReadDecimal(item, DashboardCopiersAdditionalOperationField) ?? 0m),
            UnitValueBeforeVat = RoundCurrency(ReadDecimal(item, _dashboardCopiersUnitValueBeforeVatField) ?? 0m),
            UnitValueWithVat = RoundCurrency(ReadDecimal(item, _dashboardCopiersUnitValueWithVatField) ?? 0m),
            TotalWithVat = RoundCurrency(ReadDecimal(item, _dashboardCopiersTotalWithVatField) ?? 0m),
            BillingDay = billingDay
        };
    }

    private IReadOnlyList<PortfolioKpiDto> BuildCopiersKpis(IReadOnlyList<CopiersBillingRecordRow> rows)
    {
        var totalWithVat = RoundCurrency(rows.Sum(static row => row.TotalWithVat));
        var totalQuantity = RoundCurrency(rows.Sum(static row => row.Quantity));
        var totalOperations = RoundCurrency(rows.Sum(static row => row.IncludedOperations));
        var averageUnitWithVat = rows.Count == 0
            ? 0m
            : RoundCurrency(rows.Average(static row => row.UnitValueWithVat));
        var averageTotal = rows.Count == 0
            ? 0m
            : RoundCurrency(rows.Average(static row => row.TotalWithVat));
        var firstBillingDay = rows
            .Where(static row => row.BillingDay is >= 1 and <= 31)
            .Select(static row => row.BillingDay)
            .DefaultIfEmpty(0)
            .Min();
        var uniqueClients = rows
            .Select(static row => NormalizeBillingGroupKey(row.ClientName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(key => !string.Equals(key, "empty", StringComparison.OrdinalIgnoreCase));
        var uniqueProducts = rows
            .Select(static row => NormalizeBillingGroupKey(row.ProductName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(key => !string.Equals(key, "empty", StringComparison.OrdinalIgnoreCase));

        return new[]
        {
            new PortfolioKpiDto
            {
                Key = "copiers-total-with-vat",
                Label = "Total con IVA",
                Hint = "Suma del campo cr07a_totalconiva para los productos copiers.",
                Value = totalWithVat,
                ValueFormat = "currency",
                SecondaryLabel = "Promedio por registro",
                SecondaryValue = FormatCurrencyValue(averageTotal)
            },
            new PortfolioKpiDto
            {
                Key = "copiers-quantity",
                Label = "Cantidad total",
                Hint = "Suma de cr07a_cantidad en la tabla de productos copiers.",
                Value = totalQuantity,
                ValueFormat = "number",
                SecondaryLabel = "Operaciones incluidas",
                SecondaryValue = totalOperations.ToString("N2", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "copiers-clients",
                Label = "Clientes",
                Hint = "Clientes distintos vinculados desde el lookup cr07a_cliente.",
                Value = uniqueClients,
                ValueFormat = "number",
                SecondaryLabel = "Productos distintos",
                SecondaryValue = uniqueProducts.ToString("N0", DashboardCulture)
            },
            new PortfolioKpiDto
            {
                Key = "copiers-unit-with-vat",
                Label = "Valor unitario con IVA",
                Hint = "Promedio de cr07a_valorunidadconiva sobre los registros cargados.",
                Value = averageUnitWithVat,
                ValueFormat = "currency",
                SecondaryLabel = "Dia mas temprano",
                SecondaryValue = firstBillingDay > 0
                    ? $"Dia {firstBillingDay}"
                    : "Sin dia"
            }
        };
    }

    private IReadOnlyList<CopiersBillingRowDto> BuildCopiersRows(IReadOnlyList<CopiersBillingRecordRow> rows)
    {
        return rows
            .Select(row => new CopiersBillingRowDto
            {
                RecordId = row.RecordId,
                ClientId = row.ClientId,
                ProductId = row.ProductId,
                ClientName = row.ClientName,
                ProductName = row.ProductName,
                Quantity = row.Quantity,
                IncludedOperations = row.IncludedOperations,
                AdditionalOperation = row.AdditionalOperation,
                UnitValueBeforeVat = row.UnitValueBeforeVat,
                UnitValueWithVat = row.UnitValueWithVat,
                TotalWithVat = row.TotalWithVat,
                BillingDay = row.BillingDay,
                BillingDayDisplay = row.BillingDay is >= 1 and <= 31 ? $"Dia {row.BillingDay}" : "Sin dia"
            })
            .ToList();
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsByClientAsync(
        RhEntityMetadata metadata,
        string clientId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedClientId = NormalizeGuid(clientId, nameof(clientId));
        var lookupFieldCandidates = new[]
        {
            BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
            "_cr07a_clientenit_value",
            "_cr07a_clientenitid_value",
            "_cr07a_cliente_value",
            "_cr07a_clienteid_value",
            "_cr07a_clientelookup_value"
        }
        .Where(field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        List<BillingRecordRow>? emptySuccessfulResult = null;
        Exception? lastError = null;

        foreach (var lookupField in lookupFieldCandidates)
        {
            try
            {
                var rows = await GetBillingRecordsByClientCoreAsync(metadata, normalizedClientId, lookupField, user, ct);
                if (rows.Count > 0)
                    return rows;

                emptySuccessfulResult ??= rows;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Fallo la consulta de facturas emitidas para cliente {ClientId} usando lookup {LookupField}.",
                    normalizedClientId,
                    lookupField);
                lastError = ex;
            }
        }

        if (emptySuccessfulResult is not null)
            return emptySuccessfulResult;

        throw new InvalidOperationException(
            "No fue posible consultar las facturas emitidas del cliente seleccionado en cr07a_facturacion.",
            lastError);
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsByClientCoreAsync(
        RhEntityMetadata metadata,
        string clientId,
        string lookupField,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            _dashboardBillingInvoiceNumberField,
            _dashboardBillingClientField,
            BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
            _dashboardBillingEmissionDateField,
            _dashboardBillingTotalField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = $"{lookupField} eq {clientId} and {_dashboardBillingEmissionDateField} ne null";
        var orderBy = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} desc");
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static item => item is not null && item.EmissionDate is not null)
            .Cast<BillingRecordRow>()
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(static item => item.EmissionDate)
            .ThenBy(static item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, object>> BuildCopiersSavePayloadAsync(
        RhEntityMetadata metadata,
        CopiersRecordSaveRequestDto request,
        CopiersBillingRecordRow? current,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientName = (request.ClientName ?? "").Trim();
        var productName = (request.ProductName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes indicar un cliente para el registro.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Debes indicar un producto para el registro.");

        var payload = new Dictionary<string, object>
        {
            [_dashboardCopiersQuantityField] = NormalizeCopiersAmount(request.Quantity, "cantidad"),
            [_dashboardCopiersIncludedOperationsField] = NormalizeCopiersAmount(request.IncludedOperations, "operaciones incluidas"),
            [DashboardCopiersAdditionalOperationField] = NormalizeCopiersAmount(request.AdditionalOperation, "cr07a_operacionadicional"),
            [_dashboardCopiersUnitValueBeforeVatField] = NormalizeCopiersAmount(request.UnitValueBeforeVat, "valor unitario antes de IVA"),
            [_dashboardCopiersBillingDayField] = NormalizeCopiersBillingDay(request.BillingDay),
            [_dashboardCopiersUnitValueWithVatField] = NormalizeCopiersAmount(request.UnitValueWithVat, "valor unitario con IVA"),
            [_dashboardCopiersTotalWithVatField] = NormalizeCopiersAmount(request.TotalWithVat, "total con IVA")
        };

        await ApplyCopiersClientPayloadAsync(payload, clientName, request.ClientId, current, user, ct);
        await ApplyCopiersProductPayloadAsync(payload, productName, request.ProductId, current, user, ct);

        var primaryNameField = metadata.PrimaryNameField?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(primaryNameField)
            && !payload.ContainsKey(primaryNameField)
            && !string.Equals(primaryNameField, _dashboardCopiersClientField, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(primaryNameField, _dashboardCopiersProductField, StringComparison.OrdinalIgnoreCase))
        {
            payload[primaryNameField] = productName;
        }

        return payload;
    }

    private async Task ApplyCopiersClientPayloadAsync(
        IDictionary<string, object> payload,
        string clientName,
        string? rawClientId,
        CopiersBillingRecordRow? current,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (await IsCopiersLookupFieldAsync(_dashboardCopiersClientField, user, ct))
        {
            var requestedClientId = NormalizeOptionalGuid(rawClientId);
            var resolvedClientId = !string.IsNullOrWhiteSpace(requestedClientId)
                ? requestedClientId
                : string.Equals(
                    NormalizeCopiersComparableValue(clientName),
                    NormalizeCopiersComparableValue(current?.ClientName),
                    StringComparison.Ordinal)
                    ? NormalizeOptionalGuid(current?.ClientId)
                    : await ResolveCopiersClientIdAsync(clientName, ct);

            if (string.IsNullOrWhiteSpace(resolvedClientId))
                throw new InvalidOperationException("No encontramos un cliente valido para el valor digitado. Selecciona una opcion sugerida.");

            var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                _dashboardCopiersTableLogicalName,
                _dashboardCopiersClientField,
                _dashboardCopiersClientField,
                user,
                ct);

            payload[$"{navigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({resolvedClientId})";
            return;
        }

        payload[_dashboardCopiersClientField] = clientName;
    }

    private async Task ApplyCopiersProductPayloadAsync(
        IDictionary<string, object> payload,
        string productName,
        string? rawProductId,
        CopiersBillingRecordRow? current,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var requestedProductId = NormalizeOptionalGuid(rawProductId);
        var currentProductId = NormalizeOptionalGuid(current?.ProductId);
        var useLookup = !string.IsNullOrWhiteSpace(requestedProductId)
            || !string.IsNullOrWhiteSpace(currentProductId)
            || await IsCopiersLookupFieldAsync(_dashboardCopiersProductField, user, ct);

        if (useLookup)
        {
            var resolvedProductId = !string.IsNullOrWhiteSpace(requestedProductId)
                ? requestedProductId
                : string.Equals(
                    NormalizeCopiersComparableValue(productName),
                    NormalizeCopiersComparableValue(current?.ProductName),
                    StringComparison.Ordinal)
                    ? currentProductId
                    : await ResolveCopiersProductIdAsync(productName, ct);

            if (string.IsNullOrWhiteSpace(resolvedProductId))
                throw new InvalidOperationException("No encontramos un producto valido para el valor digitado. Selecciona una opcion sugerida.");

            var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                _dashboardCopiersTableLogicalName,
                _dashboardCopiersProductField,
                _dashboardCopiersProductField,
                user,
                ct);

            payload[$"{navigationProperty}@odata.bind"] = $"/{ProductsEntitySetName}({resolvedProductId})";
            return;
        }

        payload[_dashboardCopiersProductField] = productName;
    }

    private async Task<string> ResolveCopiersClientIdAsync(string clientName, CancellationToken ct)
    {
        var matches = (await SearchClientsAsync(clientName, top: 25, ct: ct))
            .Where(item => string.Equals(
                NormalizeCopiersComparableValue(item.Name),
                NormalizeCopiersComparableValue(clientName),
                StringComparison.Ordinal))
            .Select(item => NormalizeOptionalGuid(item.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count > 1)
            throw new InvalidOperationException("Hay varios clientes con el mismo nombre. Selecciona una opcion sugerida para continuar.");

        return matches.FirstOrDefault() ?? "";
    }

    private async Task<string> ResolveCopiersProductIdAsync(string productName, CancellationToken ct)
    {
        var matches = (await SearchProductsAsync(productName, top: 25, ct: ct))
            .Where(item => string.Equals(
                NormalizeCopiersComparableValue(item.Description),
                NormalizeCopiersComparableValue(productName),
                StringComparison.Ordinal))
            .Select(item => NormalizeOptionalGuid(item.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count > 1)
            throw new InvalidOperationException("Hay varios productos con el mismo nombre. Selecciona una opcion sugerida para continuar.");

        return matches.FirstOrDefault() ?? "";
    }

    private async Task<bool> IsCopiersLookupFieldAsync(string fieldName, ClaimsPrincipal user, CancellationToken ct)
    {
        var attributeType = await ResolveCopiersAttributeTypeAsync(fieldName, user, ct);
        return string.Equals(attributeType, "Lookup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributeType, "Customer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributeType, "Owner", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveCopiersAttributeTypeAsync(string fieldName, ClaimsPrincipal user, CancellationToken ct)
    {
        var cacheKey = $"{_dashboardCopiersTableLogicalName}|{fieldName}";
        if (_dashboardAttributeTypeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(_dashboardCopiersTableLogicalName)}')" +
                $"/Attributes(LogicalName='{EscapeOdataLiteral(fieldName)}')?$select=LogicalName,AttributeType";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            using var doc = JsonDocument.Parse(json);
            var attributeType = ReadString(doc.RootElement, "AttributeType").Trim();
            if (!string.IsNullOrWhiteSpace(attributeType))
            {
                _dashboardAttributeTypeCache[cacheKey] = attributeType;
                return attributeType;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible resolver el tipo del atributo {FieldName} en la entidad {EntityLogicalName}.",
                fieldName,
                _dashboardCopiersTableLogicalName);
        }

        var fallback = string.Equals(fieldName, _dashboardCopiersClientField, StringComparison.OrdinalIgnoreCase)
            ? "Lookup"
            : "";
        _dashboardAttributeTypeCache[cacheKey] = fallback;
        return fallback;
    }

    private static string NormalizeCopiersComparableValue(string? value)
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

    private static decimal NormalizeCopiersAmount(decimal value, string label)
    {
        if (value < 0m)
            throw new InvalidOperationException($"El valor de {label} no puede ser negativo.");

        return RoundCurrency(value);
    }

    private static int NormalizeCopiersBillingDay(int? billingDay)
    {
        if (!billingDay.HasValue || billingDay.Value <= 0)
            return 0;

        if (billingDay.Value > 31)
            throw new InvalidOperationException("El dia de facturacion debe estar entre 1 y 31.");

        return billingDay.Value;
    }

    private string ReadCopiersFieldDisplayValue(JsonElement item, string fieldName, string containsToken, string fallbackValue)
    {
        var configuredLookupProperty = BuildDashboardLookupValuePropertyName(fieldName);
        var lookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                configuredLookupProperty,
                $"_{fieldName}id_value"
            },
            containsToken);

        var scannedValue = item.EnumerateObject()
            .Where(property =>
                property.Value.ValueKind == JsonValueKind.String
                && property.Name.Contains(containsToken, StringComparison.OrdinalIgnoreCase)
                && !property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase)
                && !property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Value.GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return FirstNonEmpty(
            ReadLookupFormattedValue(item, lookupProperty),
            ReadLookupFormattedValue(item, configuredLookupProperty),
            ReadString(item, $"{fieldName}{FormattedValueAnnotationSuffix}"),
            ReadString(item, $"{fieldName}_name"),
            ReadString(item, fieldName),
            scannedValue,
            fallbackValue);
    }

    private string ReadCopiersLookupId(JsonElement item, string fieldName, string containsToken)
    {
        var configuredLookupProperty = BuildDashboardLookupValuePropertyName(fieldName);
        var lookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                configuredLookupProperty,
                $"_{fieldName}id_value"
            },
            containsToken);

        return ReadString(item, lookupProperty).Trim();
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsAsync(
        RhEntityMetadata metadata,
        DateOnly startInclusive,
        DateOnly endExclusive,
        string filterField,
        string filterFieldKind,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            _dashboardBillingInvoiceNumberField,
            _dashboardBillingCompanyTaxIdField,
            _dashboardBillingClientField,
            BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
            _dashboardBillingVerticalField,
            _dashboardBillingContractTypeField,
            _dashboardBillingDueDateField,
            _dashboardBillingEmissionDateField,
            _dashboardBillingTotalField,
            _dashboardBillingVatField,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentValueField,
            _dashboardBillingReteIcaField,
            _dashboardBillingRteIvaField,
            _dashboardBillingRteFteField,
            _dashboardBillingDifferenceField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = BuildBillingDateFilter(filterField, filterFieldKind, startInclusive, endExclusive);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={filterField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static item => item is not null)
            .Cast<BillingRecordRow>()
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private BillingRecordRow? ParseBillingRecord(JsonElement item, string primaryIdField, string primaryNameField)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, _dashboardBillingIdField),
            ReadString(item, _dashboardBillingInvoiceNumberField));

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var verticalOption = ReadInt(item, _dashboardBillingVerticalField);
        var contractTypeOption = ReadInt(item, _dashboardBillingContractTypeField);

        return new BillingRecordRow
        {
            RecordId = recordId.Trim(),
            InvoiceNumber = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingInvoiceNumberField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, _dashboardBillingInvoiceNumberField),
                ReadString(item, primaryNameField),
                recordId),
            CompanyTaxId = ReadString(item, _dashboardBillingCompanyTaxIdField).Trim(),
            ClientName = ReadDashboardClientName(item),
            VerticalOptionValue = verticalOption,
            VerticalLabel = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingVerticalField}{FormattedValueAnnotationSuffix}"),
                ResolveDashboardVerticalLabel(verticalOption),
                "Sin vertical"),
            ContractTypeOptionValue = contractTypeOption,
            ContractTypeLabel = FirstNonEmpty(
                ReadString(item, $"{_dashboardBillingContractTypeField}{FormattedValueAnnotationSuffix}"),
                ResolveDashboardContractTypeLabel(contractTypeOption),
                "Sin contrato"),
            DueDate = ReadDateOnly(item, _dashboardBillingDueDateField),
            EmissionDate = ReadDateOnly(item, _dashboardBillingEmissionDateField),
            PaymentDate = ReadDateOnly(item, _dashboardBillingPaymentDateField),
            TotalInvoice = RoundCurrency(ReadDecimal(item, _dashboardBillingTotalField) ?? 0m),
            VatValue = RoundCurrency(ReadDecimal(item, _dashboardBillingVatField) ?? 0m),
            PaymentValue = RoundCurrency(ReadDecimal(item, _dashboardBillingPaymentValueField) ?? 0m),
            ReteIcaValue = RoundCurrency(ReadDecimal(item, _dashboardBillingReteIcaField) ?? 0m),
            RteIvaValue = RoundCurrency(ReadDecimal(item, _dashboardBillingRteIvaField) ?? 0m),
            RteFteValue = RoundCurrency(ReadDecimal(item, _dashboardBillingRteFteField) ?? 0m),
            DifferenceValue = RoundCurrency(ReadDecimal(item, _dashboardBillingDifferenceField) ?? 0m)
        };
    }

    private IReadOnlyList<BillingKpiDto> BuildBillingKpis(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission,
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments,
        decimal totalBilling,
        decimal previousTotalBilling,
        decimal totalCollections,
        decimal previousTotalCollections,
        decimal totalVat,
        decimal previousTotalVat,
        decimal totalRetentions,
        decimal previousTotalRetentions,
        IReadOnlyList<BillingUnpaidInvoiceDto> unpaidInvoices,
        decimal previousUnpaidAmount,
        IReadOnlyList<BillingDifferenceInvoiceDto> differenceInvoices,
        decimal previousDifferenceAmount)
    {
        var currentCloudBilling = SumCurrency(
            currentEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption),
            static record => record.TotalInvoice);
        var previousCloudBilling = SumCurrency(
            compareEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption),
            static record => record.TotalInvoice);
        var currentCopiersBilling = SumCurrency(
            currentEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption),
            static record => record.TotalInvoice);
        var previousCopiersBilling = SumCurrency(
            compareEmission.Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption),
            static record => record.TotalInvoice);
        var cloudRows = currentEmission
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var copiersRows = currentEmission
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();

        return new[]
        {
            BuildBillingKpi("total-billing", "Facturacion total", "Emitida con fecha de emision dentro del periodo.", totalBilling, previousTotalBilling, "currency", "Facturas", currentEmission.Count.ToString("N0", DashboardCulture)),
            BuildBillingKpi(
                "cloud-billing",
                "Facturacion Vertical Cloud",
                "Facturacion emitida en Cloud.",
                currentCloudBilling,
                previousCloudBilling,
                "currency",
                "Participacion periodo",
                FormatPercentValue(totalBilling == 0m ? 0m : (currentCloudBilling / totalBilling) * 100m),
                breakdowns: BuildVerticalContractBreakdowns(cloudRows)),
            BuildBillingKpi(
                "copiers-billing",
                "Facturacion Vertical Copiers",
                "Facturacion emitida en Copiers.",
                currentCopiersBilling,
                previousCopiersBilling,
                "currency",
                "Participacion periodo",
                FormatPercentValue(totalBilling == 0m ? 0m : (currentCopiersBilling / totalBilling) * 100m),
                breakdowns: BuildVerticalContractBreakdowns(copiersRows))
        };
    }

    private IReadOnlyList<PortfolioKpiDto> BuildPortfolioKpis(
        IReadOnlyList<BillingRecordRow> unpaidInvoices,
        IReadOnlyList<BillingRecordRow> overdueInvoices)
    {
        var unpaidCloudRows = unpaidInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var unpaidCopiersRows = unpaidInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();
        var overdueCloudRows = overdueInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCloudOption)
            .ToList();
        var overdueCopiersRows = overdueInvoices
            .Where(static record => record.VerticalOptionValue == DashboardVerticalCopiersOption)
            .ToList();

        return new[]
        {
            BuildPortfolioKpi(
                "cloud-portfolio",
                "Cartera Cloud",
                "Total de facturas Cloud sin pago, incluyendo el monto ya vencido.",
                SumCurrency(unpaidCloudRows, static record => record.TotalInvoice),
                SumCurrency(overdueCloudRows, static record => record.TotalInvoice)),
            BuildPortfolioKpi(
                "copiers-portfolio",
                "Cartera Copiers",
                "Total de facturas Copiers sin pago, incluyendo el monto ya vencido.",
                SumCurrency(unpaidCopiersRows, static record => record.TotalInvoice),
                SumCurrency(overdueCopiersRows, static record => record.TotalInvoice))
        };
    }

    private TaxesSectionDto BuildTaxesSection(
        string key,
        string label,
        string description,
        IReadOnlyList<BillingKpiDto> metrics,
        IReadOnlyList<TaxVerticalSummaryDto>? verticalSummaries = null)
    {
        return new TaxesSectionDto
        {
            Key = key,
            Label = label,
            Description = description,
            Metrics = metrics,
            VerticalSummaries = verticalSummaries ?? Array.Empty<TaxVerticalSummaryDto>()
        };
    }

    private IReadOnlyList<TaxVerticalSummaryDto> BuildTaxVerticalSummaries(
        string primaryLabel,
        (decimal Cloud, decimal Copiers, decimal Unassigned) current,
        (decimal Cloud, decimal Copiers, decimal Unassigned) previous,
        params TaxVerticalComponentSet[] components)
    {
        return new[]
        {
            BuildTaxVerticalSummary("cloud", "Cloud", primaryLabel, current.Cloud, previous.Cloud, components),
            BuildTaxVerticalSummary("copiers", "Copiers", primaryLabel, current.Copiers, previous.Copiers, components)
        }
        .Concat(ShouldShowUnassignedTaxVertical(current, previous, components)
            ? new[] { BuildTaxVerticalSummary("unassigned", "Sin vertical", primaryLabel, current.Unassigned, previous.Unassigned, components) }
            : Array.Empty<TaxVerticalSummaryDto>())
        .ToList();
    }

    private TaxVerticalSummaryDto BuildTaxVerticalSummary(
        string key,
        string label,
        string primaryLabel,
        decimal currentValue,
        decimal previousValue,
        IReadOnlyList<TaxVerticalComponentSet> components)
    {
        return new TaxVerticalSummaryDto
        {
            Key = key,
            Label = label,
            PrimaryLabel = primaryLabel,
            PrimaryValue = RoundCurrency(currentValue),
            PreviousPrimaryValue = RoundCurrency(previousValue),
            GrowthPercent = CalculateGrowthPercent(currentValue, previousValue),
            Tone = ResolveTrendTone(currentValue, previousValue, lowerIsBetter: false),
            Components = components
                .Select(component => new TaxVerticalComponentDto
                {
                    Key = component.Key,
                    Label = component.Label,
                    Value = GetTaxVerticalAmount(component.Current, key),
                    PreviousValue = GetTaxVerticalAmount(component.Previous, key)
                })
                .ToList()
        };
    }

    private static bool ShouldShowUnassignedTaxVertical(
        (decimal Cloud, decimal Copiers, decimal Unassigned) current,
        (decimal Cloud, decimal Copiers, decimal Unassigned) previous,
        IReadOnlyList<TaxVerticalComponentSet> components)
    {
        return Math.Abs(current.Unassigned) > 0.01m
            || Math.Abs(previous.Unassigned) > 0.01m
            || components.Any(component =>
                Math.Abs(component.Current.Unassigned) > 0.01m
                || Math.Abs(component.Previous.Unassigned) > 0.01m);
    }

    private static decimal GetTaxVerticalAmount((decimal Cloud, decimal Copiers, decimal Unassigned) values, string key) =>
        key switch
        {
            "cloud" => values.Cloud,
            "copiers" => values.Copiers,
            _ => values.Unassigned
        };

    private PortfolioKpiDto BuildPortfolioKpi(
        string key,
        string label,
        string hint,
        decimal value,
        decimal overdueValue)
    {
        return new PortfolioKpiDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = RoundCurrency(value),
            ValueFormat = "currency",
            SecondaryLabel = "Vencidas sin pago",
            SecondaryValue = FormatCurrencyValue(overdueValue)
        };
    }

    private BillingKpiDto BuildBillingKpi(
        string key,
        string label,
        string hint,
        decimal value,
        decimal previousValue,
        string valueFormat,
        string secondaryLabel,
        string secondaryValue,
        IReadOnlyList<BillingKpiBreakdownDto>? breakdowns = null,
        bool lowerIsBetter = false)
    {
        return new BillingKpiDto
        {
            Key = key,
            Label = label,
            Hint = hint,
            Value = RoundCurrency(value),
            PreviousValue = RoundCurrency(previousValue),
            GrowthPercent = CalculateGrowthPercent(value, previousValue),
            ValueFormat = valueFormat,
            Tone = ResolveTrendTone(value, previousValue, lowerIsBetter),
            SecondaryLabel = secondaryLabel,
            SecondaryValue = secondaryValue,
            Breakdowns = breakdowns ?? Array.Empty<BillingKpiBreakdownDto>()
        };
    }

    private IReadOnlyList<BillingKpiBreakdownDto> BuildVerticalContractBreakdowns(IReadOnlyList<BillingRecordRow> rows)
    {
        var total = SumCurrency(rows, static row => row.TotalInvoice);
        var mensual = SumCurrency(
            rows.Where(static row => row.ContractTypeOptionValue == DashboardContractTypeMonthlyOption),
            static row => row.TotalInvoice);
        var oneTime = SumCurrency(
            rows.Where(static row => row.ContractTypeOptionValue == DashboardContractTypeOneTimeOption),
            static row => row.TotalInvoice);

        return new[]
        {
            new BillingKpiBreakdownDto
            {
                Key = "mensual",
                Label = "Mensual",
                Value = mensual,
                SharePercent = total == 0m ? 0m : RoundCurrency((mensual / total) * 100m)
            },
            new BillingKpiBreakdownDto
            {
                Key = "onetime",
                Label = "OneTime",
                Value = oneTime,
                SharePercent = total == 0m ? 0m : RoundCurrency((oneTime / total) * 100m)
            }
        };
    }

    private async Task<List<TaxExpenseRow>> GetTaxExpenseRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            _supplierExpensesIdField,
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentValueField,
            DashboardExpenseReteFuenteField,
            DashboardExpenseRecipientNameField,
            DashboardExpenseRecipientNitField,
            DashboardExpenseCloudField,
            DashboardExpenseCopiersField
        });

        var filter = BuildBillingDateFilter(
            DashboardExpensePaymentDateField,
            DashboardExpensePaymentDateFieldKind,
            startInclusive,
            endExclusive);

        var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={DashboardExpensePaymentDateField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(ParseTaxExpenseRow)
            .Where(static row => row is not null)
            .Cast<TaxExpenseRow>()
            .ToList();
    }

    private TaxExpenseRow? ParseTaxExpenseRow(JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, _supplierExpensesIdField),
            $"{ReadString(item, DashboardExpenseRecipientNitField)}|{ReadString(item, DashboardExpenseRecipientNameField)}|{ReadString(item, DashboardExpensePaymentDateField)}");

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new TaxExpenseRow
        {
            RecordId = recordId.Trim(),
            PaymentDate = ReadDateOnly(item, DashboardExpensePaymentDateField),
            PaymentValue = RoundCurrency(ReadDecimal(item, DashboardExpensePaymentValueField) ?? 0m),
            ReteFuenteValue = RoundCurrency(ReadDecimal(item, DashboardExpenseReteFuenteField) ?? 0m),
            RecipientName = ReadString(item, DashboardExpenseRecipientNameField).Trim(),
            RecipientNit = ReadString(item, DashboardExpenseRecipientNitField).Trim(),
            CloudValue = RoundCurrency(ReadDecimal(item, DashboardExpenseCloudField) ?? 0m),
            CopiersValue = RoundCurrency(ReadDecimal(item, DashboardExpenseCopiersField) ?? 0m)
        };
    }

    private IReadOnlyList<BillingTrendPointDto> BuildBillingTrend(
        BillingPeriodDefinition period,
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission,
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments)
    {
        return period.Categories
            .Select(category => new BillingTrendPointDto
            {
                Key = category.Key,
                Label = category.Label,
                BillingCurrent = SumCurrency(
                    currentEmission.Where(record => record.EmissionDate is not null
                        && string.Equals(GetBillingCategoryKey(record.EmissionDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.TotalInvoice),
                BillingPrevious = SumCurrency(
                    compareEmission.Where(record => record.EmissionDate is not null
                        && string.Equals(GetBillingCategoryKey(record.EmissionDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.TotalInvoice),
                CollectionsCurrent = SumCurrency(
                    currentPayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.PaymentValue),
                CollectionsPrevious = SumCurrency(
                    comparePayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.PaymentValue),
                RetentionsCurrent = SumCurrency(
                    currentPayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CurrentStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.RetentionsTotal),
                RetentionsPrevious = SumCurrency(
                    comparePayments.Where(record => record.PaymentDate is not null
                        && string.Equals(GetBillingCategoryKey(record.PaymentDate.Value, period.CompareStartInclusive, period.TrendGranularity), category.Key, StringComparison.OrdinalIgnoreCase)),
                    static record => record.RetentionsTotal)
            })
            .ToList();
    }

    private IReadOnlyList<BillingVerticalSummaryDto> BuildVerticalSummaries(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission)
    {
        var currentGroups = currentEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.VerticalLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.VerticalLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentRows);
                compareGroups.TryGetValue(key, out var compareRows);
                currentRows ??= new List<BillingRecordRow>();
                compareRows ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentRows, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareRows, static row => row.TotalInvoice);
                var currentVat = SumCurrency(currentRows, static row => row.VatValue);
                var compareVat = SumCurrency(compareRows, static row => row.VatValue);

                return new BillingVerticalSummaryDto
                {
                    Key = key,
                    Label = currentRows.FirstOrDefault()?.VerticalLabel
                        ?? compareRows.FirstOrDefault()?.VerticalLabel
                        ?? "Sin vertical",
                    InvoicesCount = currentRows.Count,
                    UnpaidInvoicesCount = currentRows.Count(static row => !row.HasPayment),
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    TotalVat = currentVat,
                    PreviousTotalVat = compareVat,
                    VatGrowthPercent = CalculateGrowthPercent(currentVat, compareVat),
                    UnpaidAmount = SumCurrency(currentRows.Where(static row => !row.HasPayment), static row => row.TotalInvoice),
                    ContractTypes = BuildContractTypeSummaries(currentRows, compareRows)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<BillingContractTypeSummaryDto> BuildContractTypeSummaries(
        IReadOnlyList<BillingRecordRow> currentRows,
        IReadOnlyList<BillingRecordRow> compareRows)
    {
        var currentGroups = currentRows
            .GroupBy(static row => NormalizeBillingGroupKey(row.ContractTypeLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareRows
            .GroupBy(static row => NormalizeBillingGroupKey(row.ContractTypeLabel), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var verticalTotal = SumCurrency(currentRows, static row => row.TotalInvoice);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentItems);
                compareGroups.TryGetValue(key, out var compareItems);
                currentItems ??= new List<BillingRecordRow>();
                compareItems ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentItems, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareItems, static row => row.TotalInvoice);

                return new BillingContractTypeSummaryDto
                {
                    Key = key,
                    Label = currentItems.FirstOrDefault()?.ContractTypeLabel
                        ?? compareItems.FirstOrDefault()?.ContractTypeLabel
                        ?? "Sin contrato",
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    SharePercent = verticalTotal == 0m ? 0m : RoundCurrency((currentTotal / verticalTotal) * 100m)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<BillingClientSummaryDto> BuildClientSummaries(
        IReadOnlyList<BillingRecordRow> currentEmission,
        IReadOnlyList<BillingRecordRow> compareEmission)
    {
        var currentGroups = currentEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.ClientName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var compareGroups = compareEmission
            .GroupBy(static record => NormalizeBillingGroupKey(record.ClientName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var totalCurrent = SumCurrency(currentEmission, static record => record.TotalInvoice);

        return currentGroups.Keys
            .Concat(compareGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                currentGroups.TryGetValue(key, out var currentRows);
                compareGroups.TryGetValue(key, out var compareRows);
                currentRows ??= new List<BillingRecordRow>();
                compareRows ??= new List<BillingRecordRow>();

                var currentTotal = SumCurrency(currentRows, static row => row.TotalInvoice);
                var compareTotal = SumCurrency(compareRows, static row => row.TotalInvoice);

                return new BillingClientSummaryDto
                {
                    Key = key,
                    ClientName = currentRows.FirstOrDefault()?.ClientName
                        ?? compareRows.FirstOrDefault()?.ClientName
                        ?? "Cliente sin nombre",
                    InvoicesCount = currentRows.Count,
                    TotalBilling = currentTotal,
                    PreviousTotalBilling = compareTotal,
                    GrowthPercent = CalculateGrowthPercent(currentTotal, compareTotal),
                    SharePercent = totalCurrent == 0m ? 0m : RoundCurrency((currentTotal / totalCurrent) * 100m)
                };
            })
            .OrderByDescending(static item => item.TotalBilling)
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private IReadOnlyList<BillingRetentionItemDto> BuildRetentionSummaries(
        IReadOnlyList<BillingRecordRow> currentPayments,
        IReadOnlyList<BillingRecordRow> comparePayments)
    {
        return new[]
        {
            BuildRetentionSummary("reteica", "ReteICA", SumCurrency(currentPayments, static row => row.ReteIcaValue), SumCurrency(comparePayments, static row => row.ReteIcaValue)),
            BuildRetentionSummary("rteiva", "ReteIVA", SumCurrency(currentPayments, static row => row.RteIvaValue), SumCurrency(comparePayments, static row => row.RteIvaValue)),
            BuildRetentionSummary("rtefte", "ReteFuente", SumCurrency(currentPayments, static row => row.RteFteValue), SumCurrency(comparePayments, static row => row.RteFteValue))
        };
    }

    private IReadOnlyList<TaxExpenseDetailDto> BuildTaxExpenseDetails(IReadOnlyList<TaxExpenseRow> currentExpenses)
    {
        return currentExpenses
            .Where(static row => row.ReteFuenteValue > 0m)
            .OrderByDescending(row => row.PaymentDate)
            .ThenByDescending(static row => row.ReteFuenteValue)
            .ThenBy(static row => row.RecipientName, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TaxExpenseDetailDto
            {
                PaymentDateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                PaymentValue = row.PaymentValue,
                ReteFuenteValue = row.ReteFuenteValue,
                RecipientName = string.IsNullOrWhiteSpace(row.RecipientName) ? "Sin receptor" : row.RecipientName,
                RecipientNit = string.IsNullOrWhiteSpace(row.RecipientNit) ? "Sin NIT" : row.RecipientNit,
                CloudValue = row.CloudValue,
                CopiersValue = row.CopiersValue
            })
            .ToList();
    }

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) CalculateExpenseRetentionByVertical(IEnumerable<TaxExpenseRow> rows)
    {
        decimal cloud = 0m;
        decimal copiers = 0m;
        decimal unassigned = 0m;

        foreach (var row in rows)
        {
            if (row.ReteFuenteValue <= 0m)
                continue;

            var cloudBase = Math.Max(row.CloudValue, 0m);
            var copiersBase = Math.Max(row.CopiersValue, 0m);

            if (cloudBase > 0m && copiersBase <= 0m)
            {
                cloud += row.ReteFuenteValue;
                continue;
            }

            if (copiersBase > 0m && cloudBase <= 0m)
            {
                copiers += row.ReteFuenteValue;
                continue;
            }

            var totalBase = cloudBase + copiersBase;
            if (totalBase <= 0m)
            {
                unassigned += row.ReteFuenteValue;
                continue;
            }

            cloud += row.ReteFuenteValue * (cloudBase / totalBase);
            copiers += row.ReteFuenteValue * (copiersBase / totalBase);
        }

        return (RoundCurrency(cloud), RoundCurrency(copiers), RoundCurrency(unassigned));
    }

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) SumBillingCurrencyByVertical(
        IEnumerable<BillingRecordRow> rows,
        Func<BillingRecordRow, decimal> selector)
    {
        decimal cloud = 0m;
        decimal copiers = 0m;
        decimal unassigned = 0m;

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value == 0m)
                continue;

            switch (row.VerticalOptionValue)
            {
                case DashboardVerticalCloudOption:
                    cloud += value;
                    break;
                case DashboardVerticalCopiersOption:
                    copiers += value;
                    break;
                default:
                    unassigned += value;
                    break;
            }
        }

        return (RoundCurrency(cloud), RoundCurrency(copiers), RoundCurrency(unassigned));
    }

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) SumTaxVerticalAmounts(
        (decimal Cloud, decimal Copiers, decimal Unassigned) left,
        (decimal Cloud, decimal Copiers, decimal Unassigned) right) =>
        (
            RoundCurrency(left.Cloud + right.Cloud),
            RoundCurrency(left.Copiers + right.Copiers),
            RoundCurrency(left.Unassigned + right.Unassigned)
        );

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) SubtractTaxVerticalAmounts(
        (decimal Cloud, decimal Copiers, decimal Unassigned) left,
        (decimal Cloud, decimal Copiers, decimal Unassigned) right) =>
        (
            RoundCurrency(left.Cloud - right.Cloud),
            RoundCurrency(left.Copiers - right.Copiers),
            RoundCurrency(left.Unassigned - right.Unassigned)
        );

    private static decimal CalculateAutoFuente(IEnumerable<BillingRecordRow> rows) =>
        SumCurrency(rows, row => CalculateInvoiceTaxBase(row) * DashboardAutoFuenteRate);

    private static decimal CalculateIcaGenerated(IEnumerable<BillingRecordRow> rows) =>
        SumCurrency(rows, row => CalculateInvoiceTaxBase(row) * DashboardIcaRate);

    private static decimal CalculateInvoiceTaxBase(BillingRecordRow row) =>
        RoundCurrency(Math.Max(row.TotalInvoice - row.VatValue, 0m));

    private BillingRetentionItemDto BuildRetentionSummary(string key, string label, decimal current, decimal previous)
    {
        return new BillingRetentionItemDto
        {
            Key = key,
            Label = label,
            Total = current,
            PreviousTotal = previous,
            GrowthPercent = CalculateGrowthPercent(current, previous)
        };
    }

    private IReadOnlyList<BillingUnpaidInvoiceDto> BuildUnpaidInvoices(
        IReadOnlyList<BillingRecordRow> currentEmission,
        DateOnly today)
    {
        return currentEmission
            .Where(record => record.IsOverdue(today))
            .Select(record => new BillingUnpaidInvoiceDto
            {
                InvoiceNumber = record.InvoiceNumber,
                ClientName = record.ClientName,
                VerticalLabel = record.VerticalLabel,
                ContractTypeLabel = record.ContractTypeLabel,
                DueDateDisplay = record.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                TotalInvoice = record.TotalInvoice,
                AgeDays = record.GetOverdueDays(today)
            })
            .OrderByDescending(static record => record.AgeDays)
            .ThenByDescending(static record => record.TotalInvoice)
            .ToList();
    }

    private IReadOnlyList<BillingDifferenceInvoiceDto> BuildDifferenceInvoices(IReadOnlyList<BillingRecordRow> currentEmission)
    {
        return currentEmission
            .Where(static record => record.HasPayment && Math.Abs(record.DifferenceValue) >= 0.01m)
            .Select(record => new BillingDifferenceInvoiceDto
            {
                InvoiceNumber = record.InvoiceNumber,
                ClientName = record.ClientName,
                VerticalLabel = record.VerticalLabel,
                PaymentDateDisplay = record.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                TotalInvoice = record.TotalInvoice,
                PaymentValue = record.PaymentValue,
                RetentionsTotal = record.RetentionsTotal,
                Difference = record.DifferenceValue,
                IsBalanced = Math.Abs(record.DifferenceValue) < 0.01m
            })
            .OrderByDescending(item => Math.Abs(item.Difference))
            .ThenBy(static item => item.ClientName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static BillingPeriodDefinition BuildBillingPeriodDefinition(
        int year,
        BillingPeriodKind periodKind,
        int? periodValue,
        DateOnly today)
    {
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var compareYear = resolvedYear - 1;

        return periodKind switch
        {
            BillingPeriodKind.Quarter => BuildQuarterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? ((today.Month - 1) / 3) + 1 : 1)),
            BillingPeriodKind.Semester => BuildSemesterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? (today.Month <= 6 ? 1 : 2) : 1)),
            BillingPeriodKind.Year => BuildYearPeriod(resolvedYear, compareYear),
            _ => BuildMonthPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? today.Month : 1))
        };
    }

    private static BillingPeriodDefinition BuildMonthPeriod(int year, int compareYear, int month)
    {
        var resolvedMonth = Math.Clamp(month, 1, 12);
        var currentStart = new DateOnly(year, resolvedMonth, 1);
        var currentEnd = currentStart.AddMonths(1);
        var compareStart = new DateOnly(compareYear, resolvedMonth, 1);
        var compareEnd = compareStart.AddMonths(1);
        var totalDays = Math.Max(DateTime.DaysInMonth(year, resolvedMonth), DateTime.DaysInMonth(compareYear, resolvedMonth));
        var categories = Enumerable.Range(1, totalDays)
            .Select(day => new BillingCategory(day.ToString("00", CultureInfo.InvariantCulture), day.ToString("00", CultureInfo.InvariantCulture)))
            .ToList();

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Month,
            PeriodValue = resolvedMonth,
            PeriodLabel = ToTitleCase(currentStart.ToString("MMMM", DashboardCulture)),
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs {ToTitleCase(compareStart.ToString("MMMM", DashboardCulture))} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Day,
            GranularityLabel = "Diaria",
            Categories = categories
        };
    }

    private static BillingPeriodDefinition BuildQuarterPeriod(int year, int compareYear, int quarter)
    {
        var resolvedQuarter = Math.Clamp(quarter, 1, 4);
        var startMonth = ((resolvedQuarter - 1) * 3) + 1;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(3);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(3);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Quarter,
            PeriodValue = resolvedQuarter,
            PeriodLabel = $"T{resolvedQuarter}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs T{resolvedQuarter} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 3)
        };
    }

    private static BillingPeriodDefinition BuildSemesterPeriod(int year, int compareYear, int semester)
    {
        var resolvedSemester = Math.Clamp(semester, 1, 2);
        var startMonth = resolvedSemester == 1 ? 1 : 7;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(6);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(6);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Semester,
            PeriodValue = resolvedSemester,
            PeriodLabel = $"S{resolvedSemester}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs S{resolvedSemester} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 6)
        };
    }

    private static BillingPeriodDefinition BuildYearPeriod(int year, int compareYear)
    {
        var currentStart = new DateOnly(year, 1, 1);
        var currentEnd = currentStart.AddYears(1);
        var compareStart = new DateOnly(compareYear, 1, 1);
        var compareEnd = compareStart.AddYears(1);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Year,
            PeriodValue = 1,
            PeriodLabel = year.ToString(CultureInfo.InvariantCulture),
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 12)
        };
    }

    private static IReadOnlyList<BillingCategory> BuildMonthCategories(DateOnly startInclusive, int monthCount)
    {
        return Enumerable.Range(0, monthCount)
            .Select(offset =>
            {
                var date = startInclusive.AddMonths(offset);
                return new BillingCategory(
                    (offset + 1).ToString(CultureInfo.InvariantCulture),
                    ToTitleCase(date.ToString("MMM", DashboardCulture)));
            })
            .ToList();
    }

    private static string BuildDateRangeLabel(DateOnly startInclusive, DateOnly endExclusive)
    {
        var endInclusive = endExclusive.AddDays(-1);
        return $"{startInclusive.ToString("dd MMM yyyy", DashboardCulture)} - {endInclusive.ToString("dd MMM yyyy", DashboardCulture)}";
    }

    private static string ToTitleCase(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : DashboardCulture.TextInfo.ToTitleCase(value.Trim().ToLower(DashboardCulture));
    }

    private static string BuildBillingDateFilter(string fieldName, string fieldKind, DateOnly startInclusive, DateOnly endExclusive)
    {
        if (string.Equals(fieldKind, "date-time", StringComparison.OrdinalIgnoreCase))
        {
            var startDateTime = new DateTimeOffset(startInclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var endDateTime = new DateTimeOffset(endExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return $"{fieldName} ge {startDateTime:yyyy-MM-ddTHH:mm:ssZ} and {fieldName} lt {endDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        }

        return $"{fieldName} ge {startInclusive:yyyy-MM-dd} and {fieldName} lt {endExclusive:yyyy-MM-dd}";
    }

    private static string ResolveDashboardVerticalLabel(int optionValue)
    {
        return optionValue switch
        {
            DashboardVerticalCloudOption => "Cloud",
            DashboardVerticalCopiersOption => "Copiers",
            _ => "Sin vertical"
        };
    }

    private static string ResolveDashboardContractTypeLabel(int optionValue)
    {
        return optionValue switch
        {
            DashboardContractTypeMonthlyOption => "Mensual",
            DashboardContractTypeOneTimeOption => "OneTime",
            _ => "Sin contrato"
        };
    }

    private static string NormalizeBillingGroupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty";

        return value.Trim().ToLowerInvariant();
    }

    private string ReadDashboardClientName(JsonElement item)
    {
        var configuredLookupProperty = BuildDashboardLookupValuePropertyName(_dashboardBillingClientField);
        var lookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                configuredLookupProperty,
                "_cr07a_clientenit_value",
                "_cr07a_clientenitid_value",
                "_cr07a_cliente_value",
                "_cr07a_clienteid_value",
                "_cr07a_clientelookup_value"
            },
            "cliente");

        var scannedClientValue = item.EnumerateObject()
            .Where(property =>
                property.Value.ValueKind == JsonValueKind.String
                && (property.Name.Contains("cliente", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("client", StringComparison.OrdinalIgnoreCase))
                && !property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase)
                && !property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Value.GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return FirstNonEmpty(
            ReadLookupFormattedValue(item, lookupProperty),
            ReadLookupFormattedValue(item, configuredLookupProperty),
            ReadString(item, $"{_dashboardBillingClientField}{FormattedValueAnnotationSuffix}"),
            ReadString(item, $"{_dashboardBillingClientField}_name"),
            ReadString(item, _dashboardBillingClientField),
            scannedClientValue,
            "Cliente sin nombre");
    }

    private static string BuildDashboardLookupValuePropertyName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return "";

        var trimmed = fieldName.Trim();
        return trimmed.StartsWith("_", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("_value", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"_{trimmed}_value";
    }

    private static decimal SumCurrency(IEnumerable<BillingRecordRow> rows, Func<BillingRecordRow, decimal> selector) =>
        RoundCurrency(rows.Sum(selector));

    private static decimal SumExpenseCurrency(IEnumerable<TaxExpenseRow> rows, Func<TaxExpenseRow, decimal> selector) =>
        RoundCurrency(rows.Sum(selector));

    private static decimal? CalculateGrowthPercent(decimal current, decimal previous)
    {
        if (previous == 0m)
            return current == 0m ? 0m : null;

        return RoundCurrency(((current - previous) / Math.Abs(previous)) * 100m);
    }

    private static string ResolveTrendTone(decimal current, decimal previous, bool lowerIsBetter)
    {
        if (current == previous)
            return "neutral";

        var improved = lowerIsBetter
            ? current < previous
            : current > previous;

        return improved ? "positive" : "negative";
    }

    private static decimal CalculateAverageDaysToPay(IEnumerable<BillingRecordRow> rows)
    {
        var paidRows = rows
            .Where(static row => row.EmissionDate is not null && row.PaymentDate is not null && row.PaymentDate.Value >= row.EmissionDate.Value)
            .ToList();

        if (paidRows.Count == 0)
            return 0m;

        var totalDays = paidRows.Sum(row => row.PaymentDate!.Value.DayNumber - row.EmissionDate!.Value.DayNumber);
        return RoundCurrency(totalDays / (decimal)paidRows.Count);
    }

    private static decimal CalculatePaymentCoverage(IReadOnlyList<BillingRecordRow> rows)
    {
        if (rows.Count == 0)
            return 0m;

        return RoundCurrency((rows.Count(static row => row.HasPayment) / (decimal)rows.Count) * 100m);
    }

    private static string FormatPercentValue(decimal value) =>
        $"{RoundCurrency(value).ToString("N2", DashboardCulture)}%";

    private static string FormatCurrencyValue(decimal value) =>
        RoundCurrency(value).ToString("C0", DashboardCulture);

    private static string GetBillingCategoryKey(DateOnly date, DateOnly periodStart, BillingTrendGranularity granularity)
    {
        return granularity switch
        {
            BillingTrendGranularity.Month => (((date.Year - periodStart.Year) * 12) + (date.Month - periodStart.Month) + 1).ToString(CultureInfo.InvariantCulture),
            _ => date.Day.ToString("00", CultureInfo.InvariantCulture)
        };
    }

    private enum BillingTrendGranularity
    {
        Day = 0,
        Month = 1
    }

    private sealed class BillingRecordRow
    {
        public string RecordId { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public string CompanyTaxId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string VerticalLabel { get; set; } = "";
        public string ContractTypeLabel { get; set; } = "";
        public int VerticalOptionValue { get; set; }
        public int ContractTypeOptionValue { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateOnly? EmissionDate { get; set; }
        public DateOnly? PaymentDate { get; set; }
        public decimal TotalInvoice { get; set; }
        public decimal VatValue { get; set; }
        public decimal PaymentValue { get; set; }
        public decimal ReteIcaValue { get; set; }
        public decimal RteIvaValue { get; set; }
        public decimal RteFteValue { get; set; }
        public decimal DifferenceValue { get; set; }
        public decimal RetentionsTotal => RoundCurrency(ReteIcaValue + RteIvaValue + RteFteValue);
        public bool HasPayment => PaymentDate.HasValue || PaymentValue > 0m;
        public bool IsOverdue(DateOnly today) => !HasPayment && DueDate is not null && DueDate.Value < today;
        public int GetOverdueDays(DateOnly today) => !IsOverdue(today) ? 0 : today.DayNumber - DueDate!.Value.DayNumber;
    }

    private sealed class TaxExpenseRow
    {
        public string RecordId { get; set; } = "";
        public DateOnly? PaymentDate { get; set; }
        public decimal PaymentValue { get; set; }
        public decimal ReteFuenteValue { get; set; }
        public string RecipientName { get; set; } = "";
        public string RecipientNit { get; set; } = "";
        public decimal CloudValue { get; set; }
        public decimal CopiersValue { get; set; }
    }

    private sealed record TaxVerticalComponentSet(
        string Key,
        string Label,
        (decimal Cloud, decimal Copiers, decimal Unassigned) Current,
        (decimal Cloud, decimal Copiers, decimal Unassigned) Previous);

    private sealed class CopiersBillingRecordRow
    {
        public string RecordId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal IncludedOperations { get; set; }
        public decimal AdditionalOperation { get; set; }
        public decimal UnitValueBeforeVat { get; set; }
        public decimal UnitValueWithVat { get; set; }
        public decimal TotalWithVat { get; set; }
        public int BillingDay { get; set; }
    }

    private sealed record BillingCategory(string Key, string Label);

    private sealed class BillingPeriodDefinition
    {
        public int Year { get; init; }
        public int CompareYear { get; init; }
        public BillingPeriodKind PeriodKind { get; init; }
        public int PeriodValue { get; init; }
        public string PeriodLabel { get; init; } = "";
        public string DateRangeLabel { get; init; } = "";
        public string CompareLabel { get; init; } = "";
        public DateOnly CurrentStartInclusive { get; init; }
        public DateOnly CurrentEndExclusive { get; init; }
        public DateOnly CompareStartInclusive { get; init; }
        public DateOnly CompareEndExclusive { get; init; }
        public BillingTrendGranularity TrendGranularity { get; init; }
        public string GranularityLabel { get; init; } = "";
        public IReadOnlyList<BillingCategory> Categories { get; init; } = Array.Empty<BillingCategory>();
    }
}
