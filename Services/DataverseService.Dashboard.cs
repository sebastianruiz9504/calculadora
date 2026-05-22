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
    private const string DashboardExpenseReteIcaField = "cr07a_reteica";
    private const string DashboardExpenseRecipientNameField = "cr07a_nombrereceptor";
    private const string DashboardExpenseRecipientNitField = "cr07a_nitreceptor";
    private const string DashboardExpenseCloudField = "cr07a_cloud";
    private const string DashboardExpenseCopiersField = "cr07a_copiers";
    private const string DashboardCopiersAdditionalOperationField = "cr07a_operacionadicional";
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] TaxLegalEntityTokens =
    {
        " SAS",
        "SAS ",
        "S.A.S",
        " S.A",
        " LTDA",
        "LIMITADA",
        "EMPRESA",
        "FUNDACION",
        "CORPORACION",
        "INVERSIONES",
        "UNION TEMPORAL"
    };
    private readonly ConcurrentDictionary<string, string> _dashboardAttributeTypeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string[]> _dashboardEntityAttributeNamesCache = new(StringComparer.OrdinalIgnoreCase);

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
            OverdueInvoices = BuildUnpaidInvoices(overdueInvoices, today),
            Invoices = BuildBillingInvoiceRows(portfolioCandidates, today)
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
        var counterPeriodEnd = today.AddDays(1);
        var counterPeriodStart = counterPeriodEnd.AddDays(-35);
        var counterPeriodLabel = "Ultimos 35 dias";
        var equipmentRows = await BuildCopiersBillingEquipmentRowsAsync(
            rows,
            httpContext.User,
            counterPeriodStart,
            counterPeriodEnd,
            counterPeriodLabel,
            ct);
        var assignmentRows = await TryLoadCopiersLineEquipmentAssignmentRecordsForLinesAsync(
            rows.Select(static row => row.RecordId),
            httpContext.User,
            ct);
        var billingRows = BuildCopiersRows(rows, equipmentRows, assignmentRows);
        var groups = BuildCopiersBillingGroups(billingRows, equipmentRows);

        return new CopiersDashboardDto
        {
            AsOfDateLabel = today.ToString("dd MMM yyyy", DashboardCulture),
            FocusLabel = $"Agrupado por cliente y dia de facturacion. Contadores: {counterPeriodLabel}",
            CounterPeriodValue = counterPeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CounterPeriodLabel = counterPeriodLabel,
            HasData = rows.Count > 0,
            RecordsCount = groups.Count,
            EmptyStateTitle = "No encontramos registros de facturacion copiers.",
            EmptyStateMessage = "Cuando Dataverse tenga filas en cr07a_productoscopiers las veras aqui.",
            Kpis = BuildCopiersKpis(rows),
            Groups = groups,
            Rows = billingRows
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

        var today = GetBogotaToday();
        var invoices = await GetBillingRecordsByClientAsync(metadata, normalizedClientId, httpContext.User, ct, copiersOnly: true);
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
            EmptyStateTitle = "No encontramos facturas Copiers para este cliente.",
            EmptyStateMessage = "Cuando existan registros emitidos en cr07a_facturacion con vertical Copiers para este cliente los veras aqui.",
            Invoices = invoices
                .Select(row =>
                {
                    var isPaymentOverdue = IsCopiersClientInvoicePaymentOverdue(row, today);

                    return new CopiersClientInvoiceRowDto
                    {
                        RecordId = row.RecordId,
                        InvoiceNumber = row.InvoiceNumber,
                        PublicUrl = row.PublicUrl,
                        TotalInvoice = row.TotalInvoice,
                        EmissionDateValue = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                        EmissionDateDisplay = row.EmissionDate?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha",
                        PaymentDateValue = row.PaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                        PaymentDateDisplay = isPaymentOverdue
                            ? "Vencida"
                            : row.PaymentDate?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha",
                        PaymentValue = row.PaymentValue,
                        IsPaymentOverdue = isPaymentOverdue
                    };
                })
                .ToList()
        };
    }

    public async Task<BillingClientReportDto> GetBillingClientReportAsync(
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
            throw new InvalidOperationException("Selecciona un cliente valido para consultar sus facturas.");

        var invoices = await GetBillingRecordsByClientAsync(metadata, normalizedClientId, httpContext.User, ct, copiersOnly: false);
        var resolvedClientName = FirstNonEmpty(
            invoices.Select(static row => row.ClientName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            clientName?.Trim(),
            "Cliente");

        return new BillingClientReportDto
        {
            ClientId = normalizedClientId,
            ClientName = resolvedClientName,
            HasData = invoices.Count > 0,
            RecordsCount = invoices.Count,
            EmptyStateTitle = "No encontramos facturas para este cliente.",
            EmptyStateMessage = "Cuando existan registros en cr07a_facturacion para el cliente seleccionado apareceran aqui.",
            Invoices = BuildBillingClientReportInvoices(invoices)
        };
    }

    public async Task<BillingInvoicesTableDto> GetBillingInvoicesAsync(CancellationToken ct = default)
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

        var today = GetBogotaToday();
        var rows = await GetAllBillingRecordsAsync(metadata, httpContext.User, ct);

        return new BillingInvoicesTableDto
        {
            HasData = rows.Count > 0,
            RecordsCount = rows.Count,
            EmptyStateTitle = "No encontramos facturas registradas.",
            EmptyStateMessage = "Cuando existan registros en cr07a_facturacion apareceran aqui.",
            VerticalOptions = BuildBillingVerticalOptions(),
            ContractTypeOptions = BuildBillingContractTypeOptions(),
            Invoices = BuildBillingInvoiceRows(rows, today)
        };
    }

    public async Task<BillingInvoiceSaveResultDto> SaveBillingInvoiceAsync(BillingInvoiceSaveRequestDto request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetBillingRecordByIdAsync(metadata, recordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos la factura que quieres editar.");

        var payload = await BuildBillingInvoiceSavePayloadAsync(metadata, request, current, httpContext.User, ct);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";
        await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);

        var updated = await GetBillingRecordByIdAsync(metadata, recordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("La factura se actualizo, pero no pudimos reconstruirla desde Dataverse.");
        var invoice = BuildBillingInvoiceRows(new[] { updated }, GetBogotaToday()).First();

        return new BillingInvoiceSaveResultDto
        {
            Message = $"Factura {invoice.InvoiceNumber} actualizada correctamente.",
            Invoice = invoice
        };
    }

    public async Task<BillingInvoicesDeleteResultDto> DeleteBillingInvoicesAsync(BillingInvoicesDeleteRequestDto request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordIds = NormalizeBillingRecordIds(request.RecordIds);
        if (recordIds.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para eliminar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        foreach (var recordId in recordIds)
        {
            var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";
            await CallDataverseDeleteAsync(relativeUrl, httpContext.User, ct);
        }

        return new BillingInvoicesDeleteResultDto
        {
            DeletedCount = recordIds.Count,
            Message = recordIds.Count == 1
                ? "Factura eliminada correctamente."
                : $"{recordIds.Count:N0} facturas eliminadas correctamente."
        };
    }

    public async Task<BillingInvoicesContractTypeUpdateResultDto> UpdateBillingInvoicesContractTypeAsync(
        BillingInvoicesContractTypeUpdateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordIds = NormalizeBillingRecordIds(request.RecordIds);
        if (recordIds.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una factura para cambiar el tipo de contrato.");

        var contractType = NormalizeRequiredBillingOptionValue(
            request.ContractTypeOptionValue,
            BuildBillingContractTypeOptions(),
            "tipo de contrato");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var payload = new Dictionary<string, object?>
        {
            [_dashboardBillingContractTypeField] = contractType
        };

        foreach (var recordId in recordIds)
        {
            var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({recordId})";
            await CallDataverseSendAsync(relativeUrl, "PATCH", payload, httpContext.User, ct);
        }

        var label = BuildBillingContractTypeOptions()
            .FirstOrDefault(option => option.Value == contractType)?.Label
            ?? "seleccionado";

        return new BillingInvoicesContractTypeUpdateResultDto
        {
            UpdatedCount = recordIds.Count,
            Message = recordIds.Count == 1
                ? $"Tipo de contrato actualizado a {label}."
                : $"{recordIds.Count:N0} facturas actualizadas a {label}."
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
        var ytdEndExclusive = ResolveBillingYtdEndExclusive(period.Year, today);
        var billingFetchStart = new DateOnly(period.CompareYear, 1, 1);
        var billingFetchEnd = MaxDateOnly(period.CurrentEndExclusive, ytdEndExclusive);

        var emissionRecords = await GetBillingRecordsAsync(
            metadata,
            billingFetchStart,
            billingFetchEnd,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var paymentRecords = await GetBillingRecordsAsync(
            metadata,
            billingFetchStart,
            billingFetchEnd,
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
            Trend = BuildBillingYtdTrend(period.Year, period.CompareYear, ytdEndExclusive, emissionRecords, paymentRecords),
            Verticals = BuildVerticalSummaries(currentEmission, compareEmission),
            TopClients = BuildClientSummaries(currentEmission, compareEmission),
            Retentions = BuildRetentionSummaries(currentPayments, comparePayments),
            UnpaidInvoices = unpaidInvoices,
            DifferenceInvoices = differenceInvoices
        };
    }

    public async Task<TaxesDashboardDto> GetTaxesDashboardAsync(
        TaxesDashboardRequestDto request,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        request ??= new TaxesDashboardRequestDto();
        var legacyPeriodKind = BillingPeriodKindExtensions.ParseOrDefault(request.Period);
        var legacyYear = NormalizeTaxYear(request.Year, today.Year, 2000);
        var legacyMonth = ResolveTaxReferenceMonth(legacyYear, legacyPeriodKind, request.Value, today);
        var currentBimonthly = ((today.Month - 1) / 2) + 1;
        var currentFourMonthly = ((today.Month - 1) / 4) + 1;

        var reteFuenteYear = NormalizeTaxYear(request.ReteFuenteYear ?? request.Year, today.Year, 2000);
        var reteFuenteMonth = Math.Clamp(request.ReteFuenteMonth ?? (legacyPeriodKind == BillingPeriodKind.Month ? request.Value ?? legacyMonth : today.Month), 1, 12);
        var reteIcaYear = NormalizeTaxYear(request.ReteIcaYear ?? (legacyYear >= 2026 ? legacyYear : today.Year), Math.Max(today.Year, 2026), 2026);
        var reteIcaValue = Math.Clamp(request.ReteIcaPeriod ?? (legacyPeriodKind == BillingPeriodKind.Bimonthly ? request.Value ?? currentBimonthly : currentBimonthly), 1, 6);
        var ivaYear = NormalizeTaxYear(request.IvaYear ?? (legacyYear >= 2026 ? legacyYear : today.Year), Math.Max(today.Year, 2026), 2026);
        var ivaValue = Math.Clamp(request.IvaPeriod ?? currentFourMonthly, 1, 3);
        var incomeTaxYear = NormalizeTaxYear(request.IncomeTaxYear ?? request.Year, today.Year, 2025);

        var reteFuentePeriod = BuildMonthPeriod(reteFuenteYear, reteFuenteYear - 1, reteFuenteMonth);
        var reteIcaPeriod = BuildBimonthlyPeriod(reteIcaYear, reteIcaYear - 1, reteIcaValue);
        var incomeTaxPeriod = BuildYearPeriod(incomeTaxYear, incomeTaxYear - 1);
        var vatPeriod = BuildFourMonthlyPeriod(ivaYear, ivaYear - 1, ivaValue);
        var queryStart = new[]
        {
            new DateOnly(2025, 1, 1),
            reteFuentePeriod.CurrentStartInclusive,
            reteIcaPeriod.CurrentStartInclusive,
            incomeTaxPeriod.CurrentStartInclusive,
            vatPeriod.CurrentStartInclusive
        }.Min();
        var queryEnd = new[]
        {
            new DateOnly(Math.Max(Math.Max(today.Year, incomeTaxYear), Math.Max(reteIcaYear, ivaYear)) + 2, 1, 1),
            reteFuentePeriod.CurrentEndExclusive,
            reteIcaPeriod.CurrentEndExclusive,
            incomeTaxPeriod.CurrentEndExclusive,
            vatPeriod.CurrentEndExclusive
        }.Max();
        var metadata = await ResolveRhEntityMetadataAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            httpContext.User,
            ct);

        var emissionRecords = await GetBillingRecordsAsync(
            metadata,
            queryStart,
            queryEnd,
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            httpContext.User,
            ct);

        var paymentRecords = await GetBillingRecordsAsync(
            metadata,
            queryStart,
            queryEnd,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentDateFieldKind,
            httpContext.User,
            ct);

        var expenseRecords = await GetTaxExpenseRowsAsync(
            queryStart,
            queryEnd,
            httpContext.User,
            ct);

        var reteFuenteEmission = FilterBillingEmissionByPeriod(emissionRecords, reteFuentePeriod);
        var reteFuenteExpenses = FilterTaxExpensesByPeriod(expenseRecords, reteFuentePeriod);
        var reteIcaEmission = FilterBillingEmissionByPeriod(emissionRecords, reteIcaPeriod);
        var reteIcaPaymentRows = FilterBillingPaymentByPeriod(paymentRecords, reteIcaPeriod)
            .Where(static row => row.ReteIcaValue > 0m)
            .ToList();
        var incomeTaxPayments = FilterBillingPaymentByPeriod(paymentRecords, incomeTaxPeriod);
        var vatEmission = FilterBillingEmissionByPeriod(emissionRecords, vatPeriod);
        var vatExpenses = FilterTaxExpensesByEmissionPeriod(expenseRecords, vatPeriod);
        var vatGeneratedRows = vatEmission
            .Where(static row => row.VatValue > 0m)
            .ToList();
        var reteIvaFavorRows = FilterBillingPaymentByPeriod(paymentRecords, vatPeriod)
            .Where(static row => row.RteIvaValue > 0m)
            .ToList();
        var vatExpensesWithVat = vatExpenses
            .Where(static row => row.VatValue > 0m)
            .ToList();

        var hasData = reteFuenteEmission.Count > 0
            || reteFuenteExpenses.Count > 0
            || reteIcaEmission.Count > 0
            || reteIcaPaymentRows.Count > 0
            || incomeTaxPayments.Count > 0
            || vatGeneratedRows.Count > 0
            || reteIvaFavorRows.Count > 0
            || vatExpensesWithVat.Count > 0;

        var reteFuenteDetails = BuildTaxExpenseDetails(reteFuenteExpenses);
        var reteFuenteLegalRows = reteFuenteExpenses
            .Where(static row => string.Equals(ResolveTaxPersonTypeKey(row), "legal", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var reteFuenteNaturalRows = reteFuenteExpenses
            .Where(static row => string.Equals(ResolveTaxPersonTypeKey(row), "natural", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var reteFuenteUnknownRows = reteFuenteExpenses
            .Where(static row => string.Equals(ResolveTaxPersonTypeKey(row), "unknown", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reteFuenteBase = SumCurrency(reteFuenteEmission, static row => CalculateInvoiceTaxBase(row));
        var reteFuenteAutoFuente = CalculateAutoFuente(reteFuenteEmission);
        var reteFuenteLegal = SumExpenseCurrency(reteFuenteLegalRows, static row => row.ReteFuenteValue);
        var reteFuenteNatural = SumExpenseCurrency(reteFuenteNaturalRows, static row => row.ReteFuenteValue);
        var reteFuenteUnknown = SumExpenseCurrency(reteFuenteUnknownRows, static row => row.ReteFuenteValue);
        var reteFuenteExpensesTotal = SumExpenseCurrency(reteFuenteExpenses, static row => row.ReteFuenteValue);
        var reteFuentePayable = RoundCurrency(reteFuenteAutoFuente + reteFuenteExpensesTotal);
        var reteFuentePayableVerticals = SumTaxVerticalAmounts(
            SumBillingCurrencyByVertical(reteFuenteEmission, static row => CalculateInvoiceTaxBase(row) * DashboardAutoFuenteRate),
            CalculateExpenseRetentionByVertical(reteFuenteExpenses));
        var reteFuenteReportDetails = BuildReteFuenteReportDetails(reteFuenteEmission, reteFuenteExpenses);

        var reteIcaBase = SumCurrency(reteIcaEmission, static row => CalculateInvoiceTaxBase(row));
        var reteIcaInvoiceTotal = SumCurrency(reteIcaEmission, static row => row.TotalInvoice);
        var reteIcaTotal = CalculateIcaGenerated(reteIcaEmission);
        var reteIcaFavor = SumCurrency(reteIcaPaymentRows, static row => row.ReteIcaValue);
        var reteIcaPayable = RoundCurrency(reteIcaTotal - reteIcaFavor);
        var reteIcaGeneratedVerticals = SumBillingCurrencyByVertical(reteIcaEmission, static row => CalculateInvoiceTaxBase(row) * DashboardIcaRate);
        var reteIcaFavorVerticals = SumBillingCurrencyByVertical(reteIcaPaymentRows, static row => row.ReteIcaValue);
        var reteIcaPayableVerticals = SubtractTaxVerticalAmounts(reteIcaGeneratedVerticals, reteIcaFavorVerticals);
        var reteIcaReportDetails = BuildReteIcaReportDetails(reteIcaEmission, reteIcaPaymentRows);

        var incomeTaxRetentionRows = incomeTaxPayments
            .Where(static row => row.RteFteValue > 0m)
            .ToList();
        var incomeTaxWithheld = SumCurrency(incomeTaxRetentionRows, static row => row.RteFteValue);
        var incomeTaxInvoiceTotal = SumCurrency(incomeTaxRetentionRows, static row => row.TotalInvoice);
        var incomeTaxPaymentTotal = SumCurrency(incomeTaxRetentionRows, static row => row.PaymentValue);
        var incomeTaxVerticals = SumBillingCurrencyByVertical(incomeTaxRetentionRows, static row => row.RteFteValue);

        var vatBase = SumCurrency(vatEmission, static row => CalculateInvoiceTaxBase(row));
        var vatGeneratedInvoiceTotal = SumCurrency(vatGeneratedRows, static row => row.TotalInvoice);
        var generatedVat = SumCurrency(vatGeneratedRows, static row => row.VatValue);
        var reteIvaFavor = SumCurrency(reteIvaFavorRows, static row => row.RteIvaValue);
        var vatExpensesTotal = SumExpenseCurrency(vatExpensesWithVat, static row => row.TotalValue);
        var vatExpenseVat = SumExpenseCurrency(vatExpensesWithVat, static row => row.VatValue);
        var vatPayable = RoundCurrency(generatedVat - (vatExpenseVat + reteIvaFavor));
        var vatPayableVerticals = SubtractTaxVerticalAmounts(
            SubtractTaxVerticalAmounts(
                SumBillingCurrencyByVertical(vatGeneratedRows, static row => row.VatValue),
                CalculateExpenseCurrencyByVertical(vatExpensesWithVat, static row => row.VatValue)),
            SumBillingCurrencyByVertical(reteIvaFavorRows, static row => row.RteIvaValue));
        var vatDetails = BuildVatDetails(vatGeneratedRows, vatExpensesWithVat, reteIvaFavorRows);

        var incomeTaxYearOptions = BuildIncomeTaxYearOptions(paymentRecords, incomeTaxYear, today.Year);
        var compareYear = Math.Min(Math.Min(reteFuenteYear, reteIcaYear), Math.Min(ivaYear, incomeTaxYear)) - 1;

        var reteIcaCalculationDetails = new[]
        {
            BuildTaxCalculationDetail(
                "reteica-total",
                "Detalle Rete ICA bimensual",
                "Base antes de IVA x 0,69% - ReteICA a favor",
                reteIcaBase,
                reteIcaInvoiceTotal,
                reteIcaEmission.Count,
                "Total ICA a pagar",
                reteIcaPayable,
                BuildTaxCalculationLine("Base antes de IVA", reteIcaBase),
                BuildTaxCalculationLine("ICA generado", reteIcaTotal),
                BuildTaxCalculationLine("ReteICA a favor", reteIcaFavor),
                BuildTaxCalculationLine("Tarifa", DashboardIcaRate * 100m, "percent"))
        };

        var incomeTaxCalculationDetails = new[]
        {
            BuildTaxCalculationDetail(
                "income-tax-total",
                "Detalle declaracion de renta",
                "Retenciones hechas en los pagos de nuestros clientes a nuestro favor",
                incomeTaxPaymentTotal,
                incomeTaxInvoiceTotal,
                incomeTaxRetentionRows.Count,
                "Retenciones a favor",
                incomeTaxWithheld,
                BuildTaxCalculationLine("Total pagos relacionados", incomeTaxPaymentTotal),
                BuildTaxCalculationLine("Total facturas relacionadas", incomeTaxInvoiceTotal))
        };

        return new TaxesDashboardDto
        {
            Year = Math.Max(Math.Max(reteFuenteYear, reteIcaYear), Math.Max(ivaYear, incomeTaxYear)),
            CompareYear = compareYear,
            PeriodKind = "custom",
            PeriodKindLabel = "Filtros independientes",
            PeriodValue = 0,
            PeriodLabel = "Impuestos",
            DateRangeLabel = "Cada tarjeta tiene su propio periodo fiscal.",
            CompareLabel = "Periodos por impuesto",
            GranularityLabel = "Mensual / bimensual / cuatrimestral / anual",
            EmptyStateTitle = "No encontramos movimientos de impuestos para este periodo.",
            EmptyStateMessage = "Cambia el filtro de cada tarjeta para recalcular los cortes fiscales.",
            HasData = hasData,
            RecordsCount = reteFuenteEmission.Count + reteFuenteExpenses.Count + reteIcaEmission.Count + reteIcaPaymentRows.Count + incomeTaxRetentionRows.Count + vatGeneratedRows.Count + reteIvaFavorRows.Count + vatExpensesWithVat.Count,
            ReteFuente = BuildTaxesSection(
                "retefuente",
                "Retefuente",
                "Autofuente sobre base antes de IVA mas retefuente registrada en gastos pagados del mes.",
                "Total retefuente a pagar",
                reteFuentePayable,
                BuildTaxesSectionFilter("month", reteFuenteYear, reteFuenteMonth, 2000, today.Year, incomeTaxYearOptions),
                new[]
                {
                    BuildTaxKpi("rtefte-payable", "Total retefuente a pagar", "Autofuente + retenciones practicadas a juridicas, naturales y pendientes de clasificacion.", reteFuentePayable),
                    BuildTaxKpi("autofuente", "Autofuente", "Total facturado antes de IVA x 0.00414.", reteFuenteAutoFuente, "currency", "Base", FormatCurrencyValue(reteFuenteBase)),
                    BuildTaxKpi("legal-rtefte", "Personas juridicas", "Retenciones realizadas a personas juridicas durante el mes.", reteFuenteLegal, "currency", "Registros", reteFuenteLegalRows.Count.ToString("N0", DashboardCulture)),
                    BuildTaxKpi("natural-rtefte", "Personas naturales", "Retenciones realizadas a personas naturales durante el mes.", reteFuenteNatural, "currency", "Registros", reteFuenteNaturalRows.Count.ToString("N0", DashboardCulture)),
                    BuildTaxKpi("unknown-rtefte", "Sin clasificar", "Retenciones sin clasificacion automatica por NIT o nombre.", reteFuenteUnknown, "currency", "Registros", reteFuenteUnknownRows.Count.ToString("N0", DashboardCulture))
                },
                BuildTaxVerticalSummaries(
                    "Total retefuente",
                    reteFuentePayableVerticals,
                    (0m, 0m, 0m),
                    new TaxVerticalComponentSet("autofuente", "Autofuente", SumBillingCurrencyByVertical(reteFuenteEmission, static row => CalculateInvoiceTaxBase(row) * DashboardAutoFuenteRate), (0m, 0m, 0m)),
                    new TaxVerticalComponentSet("retentions", "Retenciones", CalculateExpenseRetentionByVertical(reteFuenteExpenses), (0m, 0m, 0m))),
                Array.Empty<TaxCalculationDetailDto>(),
                reteFuenteDetails,
                reteFuentePeriod.PeriodLabel,
                reteFuentePeriod.DateRangeLabel,
                null,
                reteFuenteReportDetails,
                calculationBaseLabel: "Valor facturado antes de IVA",
                calculationBaseValue: reteFuenteBase),
            ReteIca = BuildTaxesSection(
                "reteica",
                "Rete ICA",
                "ICA generado sobre base antes de IVA menos ReteICA practicada a favor en pagos del bimestre.",
                "Total ICA a pagar",
                reteIcaPayable,
                BuildTaxesSectionFilter("bimonthly", reteIcaYear, reteIcaValue, 2026, today.Year, incomeTaxYearOptions),
                new[]
                {
                    BuildTaxKpi("ica-payable", "Total ICA a pagar", "ICA generado - ReteICA a favor.", reteIcaPayable),
                    BuildTaxKpi("ica-total", "ICA generado", "Base antes de IVA x 0.0069.", reteIcaTotal),
                    BuildTaxKpi("ica-favor", "ReteICA a favor", "ReteICA que clientes practicaron en pagos del bimestre.", reteIcaFavor),
                    BuildTaxKpi("ica-base", "Total antes de IVA", "Base usada para el impuesto.", reteIcaBase),
                    BuildTaxKpi("ica-invoices", "Total facturas", "Valor total de las facturas del bimestre.", reteIcaInvoiceTotal, "currency", "Facturas", reteIcaEmission.Count.ToString("N0", DashboardCulture))
                },
                BuildTaxVerticalSummaries(
                    "Total ICA a pagar",
                    reteIcaPayableVerticals,
                    (0m, 0m, 0m),
                    new TaxVerticalComponentSet("generated-ica", "ICA generado", reteIcaGeneratedVerticals, (0m, 0m, 0m)),
                    new TaxVerticalComponentSet("reteica-favor", "ReteICA a favor", reteIcaFavorVerticals, (0m, 0m, 0m))),
                reteIcaCalculationDetails,
                Array.Empty<TaxExpenseDetailDto>(),
                reteIcaPeriod.PeriodLabel,
                reteIcaPeriod.DateRangeLabel,
                reportDetails: reteIcaReportDetails,
                calculationBaseLabel: "Valor facturado antes de IVA",
                calculationBaseValue: reteIcaBase),
            IncomeTax = BuildTaxesSection(
                "income-tax",
                "Retenciones a favor declaracion de renta",
                "Retenciones hechas en los pagos de nuestros clientes a nuestro favor.",
                "Retenciones a favor",
                incomeTaxWithheld,
                BuildTaxesSectionFilter("year", incomeTaxYear, 1, 2025, today.Year, incomeTaxYearOptions),
                new[]
                {
                    BuildTaxKpi("income-tax-retentions", "Retenciones a favor", "ReteFuente que clientes nos practicaron en pagos del ano.", incomeTaxWithheld),
                    BuildTaxKpi("income-tax-invoices", "Total facturas", "Facturas relacionadas con pagos que tuvieron retencion.", incomeTaxInvoiceTotal, "currency", "Facturas", incomeTaxRetentionRows.Count.ToString("N0", DashboardCulture)),
                    BuildTaxKpi("income-tax-payments", "Total pagos", "Pagos relacionados con esas retenciones.", incomeTaxPaymentTotal)
                },
                BuildTaxVerticalSummaries(
                    "Retenciones a favor",
                    incomeTaxVerticals,
                    (0m, 0m, 0m),
                    new TaxVerticalComponentSet("client-rtefte", "Clientes", incomeTaxVerticals, (0m, 0m, 0m))),
                incomeTaxCalculationDetails,
                Array.Empty<TaxExpenseDetailDto>(),
                incomeTaxYear.ToString(CultureInfo.InvariantCulture),
                incomeTaxPeriod.DateRangeLabel),
            ReteIva = BuildTaxesSection(
                "reteiva",
                "IVA",
                "IVA generado del periodo menos IVA gastado y ReteIVA a favor.",
                "IVA total a pagar",
                vatPayable,
                BuildTaxesSectionFilter("fourmonthly", ivaYear, ivaValue, 2026, today.Year, incomeTaxYearOptions),
                new[]
                {
                    BuildTaxKpi("vat-net", "IVA total a pagar", "IVA generado - (IVA gastado + ReteIVA a favor).", vatPayable),
                    BuildTaxKpi("generated-vat", "IVA generado", "IVA generado por facturas emitidas en el cuatrimestre.", generatedVat, "currency", "Base", FormatCurrencyValue(vatBase)),
                    BuildTaxKpi("expense-vat", "IVA gastado", "IVA registrado en gastos de la empresa del cuatrimestre.", vatExpenseVat, "currency", "Gastos", FormatCurrencyValue(vatExpensesTotal)),
                    BuildTaxKpi("client-rteiva", "ReteIVA a favor", "ReteIVA que clientes nos practicaron en pagos del cuatrimestre.", reteIvaFavor)
                },
                BuildTaxVerticalSummaries(
                    "IVA total a pagar",
                    vatPayableVerticals,
                    (0m, 0m, 0m),
                    new TaxVerticalComponentSet("generated-vat", "IVA generado", SumBillingCurrencyByVertical(vatGeneratedRows, static row => row.VatValue), (0m, 0m, 0m)),
                    new TaxVerticalComponentSet("expense-vat", "IVA gastado", CalculateExpenseCurrencyByVertical(vatExpensesWithVat, static row => row.VatValue), (0m, 0m, 0m)),
                    new TaxVerticalComponentSet("client-rteiva", "ReteIVA a favor", SumBillingCurrencyByVertical(reteIvaFavorRows, static row => row.RteIvaValue), (0m, 0m, 0m))),
                Array.Empty<TaxCalculationDetailDto>(),
                Array.Empty<TaxExpenseDetailDto>(),
                vatPeriod.PeriodLabel,
                vatPeriod.DateRangeLabel,
                vatDetails,
                calculationBaseLabel: "Facturado con IVA",
                calculationBaseValue: vatGeneratedInvoiceTotal),
            ExpenseDetails = reteFuenteDetails
        };
    }

    private async Task<List<CopiersBillingRecordRow>> GetCopiersRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            return await GetCopiersRecordsCoreWithGroupRetryAsync(metadata, user, ct, preferProductLookup: false);
        }
        catch (InvalidOperationException ex)
        {
            if (!await ShouldRetryCopiersQueryWithProductLookupAsync(ex, user, ct))
                throw;

            return await GetCopiersRecordsCoreWithGroupRetryAsync(metadata, user, ct, preferProductLookup: true);
        }
    }

    private async Task<List<CopiersBillingRecordRow>> GetCopiersRecordsCoreWithGroupRetryAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool preferProductLookup)
    {
        try
        {
            return await GetCopiersRecordsCoreAsync(metadata, user, ct, preferProductLookup, includeGroupField: true);
        }
        catch (InvalidOperationException ex) when (ShouldRetryCopiersQueryWithoutGroupField(ex))
        {
            _logger.LogWarning(
                ex,
                "No fue posible leer la columna de agrupacion Copiers {Field}. Se usara agrupacion por defecto.",
                _dashboardCopiersGroupField);
            return await GetCopiersRecordsCoreAsync(metadata, user, ct, preferProductLookup, includeGroupField: false);
        }
    }

    private async Task<List<CopiersBillingRecordRow>> GetCopiersRecordsCoreAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool preferProductLookup,
        bool includeGroupField)
    {
        var select = BuildCopiersSelectClause(metadata, preferProductLookup, includeGroupField);

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

    private string BuildCopiersSelectClause(RhEntityMetadata metadata, bool preferProductLookup, bool includeGroupField = true)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            _dashboardCopiersQuantityField,
            preferProductLookup
                ? BuildDashboardLookupValuePropertyName(NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField))
                : NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField),
            _dashboardCopiersUnitValueBeforeVatField,
            _dashboardCopiersBillingDayField,
            _dashboardCopiersIncludedOperationsField,
            includeGroupField ? _dashboardCopiersGroupField : "",
            DashboardCopiersAdditionalOperationField,
            BuildDashboardLookupValuePropertyName(_dashboardCopiersClientField),
            _dashboardCopiersUnitValueWithVatField,
            _dashboardCopiersTotalWithVatField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> ShouldRetryCopiersQueryWithProductLookupAsync(
        InvalidOperationException exception,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var productField = NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField);
        if (string.IsNullOrWhiteSpace(productField))
            return false;

        var lookupField = BuildDashboardLookupValuePropertyName(productField);
        if (exception.Message.Contains(lookupField, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!exception.Message.Contains(productField, StringComparison.OrdinalIgnoreCase))
            return false;

        return await IsCopiersLookupFieldAsync(productField, user, ct);
    }

    private bool ShouldRetryCopiersQueryWithoutGroupField(InvalidOperationException exception)
    {
        return !string.IsNullOrWhiteSpace(_dashboardCopiersGroupField)
            && exception.Message.Contains(_dashboardCopiersGroupField, StringComparison.OrdinalIgnoreCase)
            && (exception.Message.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
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
        catch (InvalidOperationException ex)
        {
            if (!await ShouldRetryCopiersQueryWithProductLookupAsync(ex, user, ct))
                throw;

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
        try
        {
            return await GetCopiersRecordByIdCoreAsync(metadata, recordId, user, ct, preferProductLookup, includeGroupField: true);
        }
        catch (InvalidOperationException ex) when (ShouldRetryCopiersQueryWithoutGroupField(ex))
        {
            _logger.LogWarning(
                ex,
                "No fue posible leer la columna de agrupacion Copiers {Field}. Se usara agrupacion por defecto.",
                _dashboardCopiersGroupField);
            return await GetCopiersRecordByIdCoreAsync(metadata, recordId, user, ct, preferProductLookup, includeGroupField: false);
        }
    }

    private async Task<CopiersBillingRecordRow?> GetCopiersRecordByIdCoreAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool preferProductLookup,
        bool includeGroupField)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={BuildCopiersSelectClause(metadata, preferProductLookup, includeGroupField)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return ParseCopiersRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private CopiersBillingRecordRow? ParseCopiersRecord(JsonElement item, string primaryIdField, string primaryNameField)
    {
        var productField = NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField);
        var productName = ReadCopiersFieldDisplayValue(
            item,
            productField,
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
            ProductId = ReadCopiersLookupId(item, productField, "producto"),
            ClientName = clientName,
            ProductName = productName,
            Quantity = RoundCurrency(ReadDecimal(item, _dashboardCopiersQuantityField) ?? 0m),
            IncludedOperations = RoundCurrency(ReadDecimal(item, _dashboardCopiersIncludedOperationsField) ?? 0m),
            GroupIncludedOperations = ReadCopiersGroupIncludedOperations(item),
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
        var totalOperations = RoundCurrency(rows.Sum(static row => CalculateCopiersLineIncludedOperations(row.Quantity, row.IncludedOperations)));
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

    private IReadOnlyList<CopiersBillingRowDto> BuildCopiersRows(
        IReadOnlyList<CopiersBillingRecordRow> rows,
        IReadOnlyList<CopiersBillingEquipmentDto> equipmentRows,
        IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow> assignmentRows)
    {
        return rows
            .Select(row =>
            {
                var equipment = FindCopiersBillingEquipment(row, equipmentRows);
                var capacity = NormalizeCopiersLineEquipmentAssignmentCapacity(row.Quantity);
                var assignedCount = assignmentRows
                    .Where(assignment => string.Equals(assignment.LineId, row.RecordId, StringComparison.OrdinalIgnoreCase))
                    .Select(static assignment => assignment.EquipmentId)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var assignedAnyCount = assignmentRows
                    .Where(assignment => CopiersBillingAssignmentClientMatches(assignment, row))
                    .Select(static assignment => assignment.EquipmentId)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var availableCount = Math.Max(equipment.Count - assignedAnyCount, 0);

                return new CopiersBillingRowDto
                {
                    RecordId = row.RecordId,
                    ClientId = row.ClientId,
                    ProductId = row.ProductId,
                    ClientName = row.ClientName,
                    ProductName = row.ProductName,
                    Quantity = row.Quantity,
                    IncludedOperations = row.IncludedOperations,
                    GroupIncludedOperations = row.GroupIncludedOperations,
                    AdditionalOperation = row.AdditionalOperation,
                    UnitValueBeforeVat = row.UnitValueBeforeVat,
                    UnitValueWithVat = row.UnitValueWithVat,
                    TotalWithVat = row.TotalWithVat,
                    BillingDay = row.BillingDay,
                    BillingDayDisplay = row.BillingDay is >= 1 and <= 31 ? $"Dia {row.BillingDay}" : "Sin dia",
                    EquipmentAssignmentCapacity = capacity,
                    AssignedEquipmentCount = assignedCount,
                    AvailableEquipmentCount = availableCount,
                    EquipmentAssignmentSummary = BuildCopiersLineEquipmentAssignmentSummary(assignedCount, capacity, availableCount),
                    HasAssignmentOverflow = assignedCount > capacity
                };
            })
            .ToList();
    }

    private static IReadOnlyList<BillingClientReportInvoiceDto> BuildBillingClientReportInvoices(IReadOnlyList<BillingRecordRow> rows)
    {
        return rows
            .OrderByDescending(static row => row.EmissionDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(static row => new BillingClientReportInvoiceDto
            {
                RecordId = row.RecordId,
                InvoiceNumber = row.InvoiceNumber,
                ClientId = row.ClientId,
                ClientName = row.ClientName,
                CompanyTaxId = row.CompanyTaxId,
                VatPercent = row.VatPercent,
                VatValue = row.VatValue,
                TotalInvoice = row.TotalInvoice,
                PublicUrl = row.PublicUrl,
                EmissionDateValue = row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                EmissionDateDisplay = row.EmissionDate?.ToString("dd MMM yyyy", DashboardCulture) ?? "Sin fecha",
                VerticalLabel = row.VerticalLabel,
                ContractTypeLabel = row.ContractTypeLabel
            })
            .ToList();
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsByClientAsync(
        RhEntityMetadata metadata,
        string clientId,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool copiersOnly)
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
                var rows = await GetBillingRecordsByClientCoreAsync(metadata, normalizedClientId, lookupField, user, ct, copiersOnly);
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

    private async Task<List<BillingRecordRow>> GetAllBillingRecordsAsync(
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = BuildBillingSelectClause(metadata);
        var orderBy = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} desc");
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(static item => item is not null)
            .Cast<BillingRecordRow>()
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(static item => item.EmissionDate)
            .ThenBy(static item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<BillingRecordRow?> GetBillingRecordByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var select = BuildBillingSelectClause(metadata);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseBillingRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private string BuildBillingSelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
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
            _dashboardBillingVatPercentField,
            _dashboardBillingVatField,
            _dashboardBillingPublicUrlField,
            _dashboardBillingPaymentDateField,
            _dashboardBillingPaymentValueField,
            _dashboardBillingReteIcaField,
            _dashboardBillingRteIvaField,
            _dashboardBillingRteFteField,
            _dashboardBillingDifferenceField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<List<BillingRecordRow>> GetBillingRecordsByClientCoreAsync(
        RhEntityMetadata metadata,
        string clientId,
        string lookupField,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool copiersOnly)
    {
        var select = BuildBillingSelectClause(metadata);

        var filter = copiersOnly
            ? $"{lookupField} eq {clientId} and {_dashboardBillingEmissionDateField} ne null"
            : $"{lookupField} eq {clientId}";
        var orderBy = Uri.EscapeDataString($"{_dashboardBillingEmissionDateField} desc");
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField))
            .Where(item => item is not null
                && (!copiersOnly || item.EmissionDate is not null))
            .Cast<BillingRecordRow>()
            .Where(item => !copiersOnly || IsDashboardCopiersVertical(item))
            .GroupBy(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(static item => item.EmissionDate)
            .ThenBy(static item => item.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, object?>> BuildBillingInvoiceSavePayloadAsync(
        RhEntityMetadata metadata,
        BillingInvoiceSaveRequestDto request,
        BillingRecordRow current,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var invoiceNumber = (request.InvoiceNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new InvalidOperationException("El numero de factura es obligatorio.");

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [_dashboardBillingInvoiceNumberField] = invoiceNumber,
            [_dashboardBillingCompanyTaxIdField] = NormalizeBillingTextValue(request.CompanyTaxId),
            [_dashboardBillingVerticalField] = NormalizeBillingOptionValue(request.VerticalOptionValue, BuildBillingVerticalOptions(), "vertical"),
            [_dashboardBillingContractTypeField] = NormalizeBillingOptionValue(request.ContractTypeOptionValue, BuildBillingContractTypeOptions(), "tipo de contrato"),
            [_dashboardBillingEmissionDateField] = NormalizeBillingDateValue(request.EmissionDateValue, "fecha de emision"),
            [_dashboardBillingDueDateField] = NormalizeBillingDateValue(request.DueDateValue, "fecha de vencimiento"),
            [_dashboardBillingPaymentDateField] = NormalizeBillingDateValue(request.PaymentDateValue, "fecha de pago"),
            [_dashboardBillingTotalField] = NormalizeBillingAmount(request.TotalInvoice, "total factura"),
            [_dashboardBillingVatPercentField] = NormalizeBillingAmount(request.VatPercent, "porcentaje de IVA"),
            [_dashboardBillingVatField] = NormalizeBillingAmount(request.VatValue, "valor IVA"),
            [_dashboardBillingPaymentValueField] = NormalizeBillingAmount(request.PaymentValue, "valor pago"),
            [_dashboardBillingReteIcaField] = NormalizeBillingAmount(request.ReteIcaValue, "ReteICA"),
            [_dashboardBillingRteIvaField] = NormalizeBillingAmount(request.RteIvaValue, "RteIVA"),
            [_dashboardBillingRteFteField] = NormalizeBillingAmount(request.RteFteValue, "RteFte"),
            [_dashboardBillingPublicUrlField] = NormalizeBillingTextValue(request.PublicUrl)
        };

        if (!string.IsNullOrWhiteSpace(metadata.PrimaryNameField)
            && !payload.ContainsKey(metadata.PrimaryNameField))
        {
            payload[metadata.PrimaryNameField] = invoiceNumber;
        }

        await ApplyBillingClientPayloadAsync(payload, request.ClientName, request.ClientId, current, user, ct);
        return payload;
    }

    private async Task ApplyBillingClientPayloadAsync(
        IDictionary<string, object?> payload,
        string? rawClientName,
        string? rawClientId,
        BillingRecordRow current,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientName = (rawClientName ?? "").Trim();
        if (await IsBillingLookupFieldAsync(_dashboardBillingClientField, user, ct))
        {
            var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
                _dashboardBillingTableLogicalName,
                _dashboardBillingClientField,
                _dashboardBillingClientField,
                user,
                ct);

            if (string.IsNullOrWhiteSpace(clientName))
            {
                payload[$"{navigationProperty}@odata.bind"] = null;
                return;
            }

            var requestedClientId = NormalizeOptionalGuid(rawClientId);
            var currentClientId = NormalizeOptionalGuid(current.ClientId);
            var resolvedClientId = !string.IsNullOrWhiteSpace(requestedClientId)
                ? requestedClientId
                : string.Equals(
                    NormalizeCopiersComparableValue(clientName),
                    NormalizeCopiersComparableValue(current.ClientName),
                    StringComparison.Ordinal)
                    ? currentClientId
                    : await ResolveBillingClientIdAsync(clientName, ct);

            if (string.IsNullOrWhiteSpace(resolvedClientId))
                throw new InvalidOperationException("No encontramos un cliente valido para la factura. Selecciona una sugerencia o escribe el nombre exacto del cliente.");

            payload[$"{navigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({resolvedClientId})";
            return;
        }

        payload[_dashboardBillingClientField] = NormalizeBillingTextValue(clientName);
    }

    private async Task<string> ResolveBillingClientIdAsync(string clientName, CancellationToken ct)
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

    private async Task<bool> IsBillingLookupFieldAsync(string fieldName, ClaimsPrincipal user, CancellationToken ct)
    {
        var attributeType = await ResolveDashboardAttributeTypeAsync(_dashboardBillingTableLogicalName, fieldName, user, ct);
        return string.Equals(attributeType, "Lookup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributeType, "Customer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributeType, "Owner", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveDashboardAttributeTypeAsync(
        string entityLogicalName,
        string fieldName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var cacheKey = $"{entityLogicalName}|{fieldName}";
        if (_dashboardAttributeTypeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
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
                entityLogicalName);
        }

        var fallback = string.Equals(entityLogicalName, _dashboardBillingTableLogicalName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(fieldName, _dashboardBillingClientField, StringComparison.OrdinalIgnoreCase)
                ? "Lookup"
                : "";
        _dashboardAttributeTypeCache[cacheKey] = fallback;
        return fallback;
    }

    private static List<string> NormalizeBillingRecordIds(IEnumerable<string>? recordIds)
    {
        return (recordIds ?? Array.Empty<string>())
            .Select(NormalizeOptionalGuid)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static object? NormalizeBillingTextValue(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeBillingDateValue(string? rawValue, string label)
    {
        var trimmed = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (!TryParseDateOnly(trimmed, out var parsedDate))
            throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");

        return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static decimal NormalizeBillingAmount(decimal value, string label)
    {
        if (value < 0m)
            throw new InvalidOperationException($"El valor de {label} no puede ser negativo.");

        return RoundCurrency(value);
    }

    private static int? NormalizeBillingOptionValue(int? value, IReadOnlyList<BillingOptionDto> options, string label)
    {
        if (!value.HasValue)
            return null;

        if (!options.Any(option => option.Value == value.Value))
            throw new InvalidOperationException($"El valor seleccionado para {label} no es valido.");

        return value.Value;
    }

    private static int NormalizeRequiredBillingOptionValue(int? value, IReadOnlyList<BillingOptionDto> options, string label)
    {
        var normalizedValue = NormalizeBillingOptionValue(value, options, label);
        if (!normalizedValue.HasValue)
            throw new InvalidOperationException($"Selecciona un valor valido para {label}.");

        return normalizedValue.Value;
    }

    private static bool IsDashboardCopiersVertical(BillingRecordRow row)
    {
        return row.VerticalOptionValue == DashboardVerticalCopiersOption
            || row.VerticalLabel.Contains("Copiers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCopiersClientInvoicePaymentOverdue(BillingRecordRow row, DateOnly today)
    {
        if (row.DueDate is null || row.PaymentValue > 0m)
            return false;

        return today.DayNumber - row.DueDate.Value.DayNumber > 30;
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
        var productField = NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField);
        if (!string.IsNullOrWhiteSpace(primaryNameField)
            && !payload.ContainsKey(primaryNameField)
            && !string.Equals(primaryNameField, _dashboardCopiersClientField, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(primaryNameField, productField, StringComparison.OrdinalIgnoreCase))
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
        var productField = NormalizeDashboardLookupLogicalName(_dashboardCopiersProductField);
        var requestedProductId = NormalizeOptionalGuid(rawProductId);
        var currentProductId = NormalizeOptionalGuid(current?.ProductId);
        var useLookup = !string.IsNullOrWhiteSpace(requestedProductId)
            || !string.IsNullOrWhiteSpace(currentProductId)
            || await IsCopiersLookupFieldAsync(productField, user, ct);

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
                productField,
                productField,
                user,
                ct);

            payload[$"{navigationProperty}@odata.bind"] = $"/{ProductsEntitySetName}({resolvedProductId})";
            return;
        }

        payload[productField] = productName;
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
        fieldName = NormalizeDashboardLookupLogicalName(fieldName);
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
        var select = BuildBillingSelectClause(metadata);

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

        var clientLookupProperty = DetectLookupValueProperty(
            item,
            new[]
            {
                BuildDashboardLookupValuePropertyName(_dashboardBillingClientField),
                "_cr07a_clientenit_value",
                "_cr07a_clientenitid_value",
                "_cr07a_cliente_value",
                "_cr07a_clienteid_value",
                "_cr07a_clientelookup_value"
            },
            "cliente");
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
            ClientId = ReadString(item, clientLookupProperty).Trim(),
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
            VatPercent = RoundCurrency(ReadDecimal(item, _dashboardBillingVatPercentField) ?? 0m),
            VatValue = RoundCurrency(ReadDecimal(item, _dashboardBillingVatField) ?? 0m),
            PublicUrl = ReadString(item, _dashboardBillingPublicUrlField).Trim(),
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
        string totalLabel,
        decimal totalValue,
        TaxesSectionFilterDto filter,
        IReadOnlyList<BillingKpiDto> metrics,
        IReadOnlyList<TaxVerticalSummaryDto>? verticalSummaries = null,
        IReadOnlyList<TaxCalculationDetailDto>? calculationDetails = null,
        IReadOnlyList<TaxExpenseDetailDto>? retentionDetails = null,
        string periodLabel = "",
        string dateRangeLabel = "",
        TaxVatDetailsDto? vatDetails = null,
        TaxReportDetailsDto? reportDetails = null,
        string calculationBaseLabel = "",
        decimal calculationBaseValue = 0m)
    {
        return new TaxesSectionDto
        {
            Key = key,
            Label = label,
            Description = description,
            PeriodLabel = periodLabel,
            DateRangeLabel = dateRangeLabel,
            TotalLabel = totalLabel,
            TotalValue = RoundCurrency(totalValue),
            CalculationBaseLabel = calculationBaseLabel,
            CalculationBaseValue = RoundCurrency(calculationBaseValue),
            Filter = filter,
            Metrics = metrics,
            CalculationDetails = calculationDetails ?? Array.Empty<TaxCalculationDetailDto>(),
            VerticalSummaries = verticalSummaries ?? Array.Empty<TaxVerticalSummaryDto>(),
            RetentionDetails = retentionDetails ?? Array.Empty<TaxExpenseDetailDto>(),
            VatDetails = vatDetails ?? new TaxVatDetailsDto(),
            ReportDetails = reportDetails ?? new TaxReportDetailsDto()
        };
    }

    private static TaxesSectionFilterDto BuildTaxesSectionFilter(
        string kind,
        int year,
        int value,
        int minYear,
        int currentYear,
        IReadOnlyList<int>? incomeTaxYearOptions = null)
    {
        var yearOptions = string.Equals(kind, "year", StringComparison.OrdinalIgnoreCase)
            ? BuildTaxYearOptionsFromValues(incomeTaxYearOptions, year, minYear, currentYear)
            : BuildRollingTaxYearOptions(year, minYear, currentYear);
        var valueOptions = BuildTaxPeriodValueOptions(kind);
        var valueLabel = valueOptions.FirstOrDefault(option => option.Value == value)?.Label
            ?? year.ToString(CultureInfo.InvariantCulture);

        return new TaxesSectionFilterDto
        {
            Kind = kind,
            Year = year,
            Value = value,
            ValueLabel = valueLabel,
            YearOptions = yearOptions,
            ValueOptions = valueOptions
        };
    }

    private static IReadOnlyList<TaxesFilterOptionDto> BuildRollingTaxYearOptions(int selectedYear, int minYear, int currentYear)
    {
        var maxYear = Math.Max(currentYear + 1, selectedYear);
        var startYear = Math.Max(minYear, maxYear - 6);
        return Enumerable.Range(startYear, maxYear - startYear + 1)
            .Append(selectedYear)
            .Where(year => year >= minYear)
            .Distinct()
            .OrderByDescending(static year => year)
            .Select(static year => new TaxesFilterOptionDto
            {
                Value = year,
                Label = year.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();
    }

    private static IReadOnlyList<TaxesFilterOptionDto> BuildTaxYearOptionsFromValues(
        IReadOnlyList<int>? years,
        int selectedYear,
        int minYear,
        int currentYear)
    {
        var values = (years ?? Array.Empty<int>())
            .Append(2025)
            .Append(currentYear)
            .Append(selectedYear)
            .Where(year => year >= minYear)
            .Distinct()
            .OrderByDescending(static year => year)
            .ToList();

        return values.Select(static year => new TaxesFilterOptionDto
        {
            Value = year,
            Label = year.ToString(CultureInfo.InvariantCulture)
        }).ToList();
    }

    private static IReadOnlyList<TaxesFilterOptionDto> BuildTaxPeriodValueOptions(string kind)
    {
        if (string.Equals(kind, "bimonthly", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new TaxesFilterOptionDto { Value = 1, Label = "B1 Ene-Feb" },
                new TaxesFilterOptionDto { Value = 2, Label = "B2 Mar-Abr" },
                new TaxesFilterOptionDto { Value = 3, Label = "B3 May-Jun" },
                new TaxesFilterOptionDto { Value = 4, Label = "B4 Jul-Ago" },
                new TaxesFilterOptionDto { Value = 5, Label = "B5 Sep-Oct" },
                new TaxesFilterOptionDto { Value = 6, Label = "B6 Nov-Dic" }
            };
        }

        if (string.Equals(kind, "fourmonthly", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new TaxesFilterOptionDto { Value = 1, Label = "C1 Ene-Abr" },
                new TaxesFilterOptionDto { Value = 2, Label = "C2 May-Ago" },
                new TaxesFilterOptionDto { Value = 3, Label = "C3 Sep-Dic" }
            };
        }

        if (string.Equals(kind, "year", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<TaxesFilterOptionDto>();
        }

        return Enumerable.Range(1, 12)
            .Select(month => new TaxesFilterOptionDto
            {
                Value = month,
                Label = ToTitleCase(new DateOnly(2024, month, 1).ToString("MMMM", DashboardCulture))
            })
            .ToList();
    }

    private static IReadOnlyList<int> BuildIncomeTaxYearOptions(
        IEnumerable<BillingRecordRow> paymentRecords,
        int selectedYear,
        int currentYear)
    {
        return paymentRecords
            .Where(static row => row.PaymentDate is not null && row.RteFteValue > 0m)
            .Select(static row => row.PaymentDate!.Value.Year)
            .Append(2025)
            .Append(currentYear)
            .Append(selectedYear)
            .Where(static year => year >= 2025)
            .Distinct()
            .OrderByDescending(static year => year)
            .ToList();
    }

    private TaxVatDetailsDto BuildVatDetails(
        IReadOnlyList<BillingRecordRow> generatedRows,
        IReadOnlyList<TaxExpenseRow> expenseRows,
        IReadOnlyList<BillingRecordRow> reteIvaRows)
    {
        return new TaxVatDetailsDto
        {
            Tables = new[]
            {
                new TaxVatTableDto
                {
                    Key = "generated",
                    Label = "IVA generado",
                    DateColumnLabel = "Fecha emision",
                    NameColumnLabel = "Cliente",
                    ValueLabel = "IVA",
                    TotalValue = SumCurrency(generatedRows, static row => row.VatValue),
                    Rows = BuildVatBillingRows(
                        generatedRows,
                        static row => row.EmissionDate,
                        static row => row.VatValue)
                },
                new TaxVatTableDto
                {
                    Key = "spent",
                    Label = "IVA gastado",
                    DateColumnLabel = "Fecha emision",
                    NameColumnLabel = "Nombre emisor",
                    ValueLabel = "IVA",
                    ShowRetentionRateColumns = true,
                    TotalValue = SumExpenseCurrency(expenseRows, static row => row.VatValue),
                    Rows = BuildVatExpenseRows(expenseRows)
                },
                new TaxVatTableDto
                {
                    Key = "reteiva",
                    Label = "ReteIVA a favor",
                    DateColumnLabel = "Fecha pago",
                    NameColumnLabel = "Cliente",
                    ValueLabel = "Valor reteiva",
                    TotalValue = SumCurrency(reteIvaRows, static row => row.RteIvaValue),
                    Rows = BuildVatBillingRows(
                        reteIvaRows,
                        static row => row.PaymentDate,
                        static row => row.RteIvaValue)
                }
            }
        };
    }

    private IReadOnlyList<TaxVatRowDto> BuildVatBillingRows(
        IEnumerable<BillingRecordRow> rows,
        Func<BillingRecordRow, DateOnly?> dateSelector,
        Func<BillingRecordRow, decimal> taxSelector)
    {
        return rows
            .OrderBy(dateSelector)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var taxValue = RoundCurrency(taxSelector(row));
                var totalValue = RoundCurrency(row.TotalInvoice);
                var verticalKey = ResolveTaxVerticalKey(row.VerticalOptionValue);
                var rowDate = dateSelector(row);

                return new TaxVatRowDto
                {
                    DateDisplay = rowDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    InvoiceNumber = row.InvoiceNumber,
                    Name = FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Sin cliente"),
                    VerticalKey = verticalKey,
                    VerticalLabel = ResolveDashboardVerticalLabel(row.VerticalOptionValue),
                    TotalValue = totalValue,
                    TaxValue = taxValue,
                    CloudTotalValue = verticalKey == "cloud" ? totalValue : 0m,
                    CloudTaxValue = verticalKey == "cloud" ? taxValue : 0m,
                    CopiersTotalValue = verticalKey == "copiers" ? totalValue : 0m,
                    CopiersTaxValue = verticalKey == "copiers" ? taxValue : 0m,
                    UnassignedTotalValue = verticalKey == "unassigned" ? totalValue : 0m,
                    UnassignedTaxValue = verticalKey == "unassigned" ? taxValue : 0m
                };
            })
            .ToList();
    }

    private IReadOnlyList<TaxVatRowDto> BuildVatExpenseRows(IEnumerable<TaxExpenseRow> rows)
    {
        return rows
            .OrderBy(static row => row.EmissionDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var taxSplit = CalculateExpenseRowVerticalSplit(row, row.VatValue);
                var totalSplit = CalculateExpenseRowVerticalSplit(row, row.TotalValue);

                return new TaxVatRowDto
                {
                    DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    InvoiceNumber = row.InvoiceNumber,
                    Name = FirstNonEmpty(row.IssuerName, row.RecipientName, "Sin emisor"),
                    VerticalKey = ResolveDominantExpenseVerticalKey(row),
                    VerticalLabel = ResolveDominantExpenseVerticalLabel(row),
                    TotalValue = RoundCurrency(row.TotalValue),
                    TaxValue = RoundCurrency(row.VatValue),
                    ReteFuentePercent = CalculateExpenseRetentionPercent(row.ReteFuenteValue, row),
                    ReteIcaPercent = CalculateExpenseRetentionPercent(row.ReteIcaValue, row),
                    CloudTotalValue = totalSplit.Cloud,
                    CloudTaxValue = taxSplit.Cloud,
                    CopiersTotalValue = totalSplit.Copiers,
                    CopiersTaxValue = taxSplit.Copiers,
                    UnassignedTotalValue = totalSplit.Unassigned,
                    UnassignedTaxValue = taxSplit.Unassigned
                };
            })
            .ToList();
    }

    private TaxReportDetailsDto BuildReteFuenteReportDetails(
        IReadOnlyList<BillingRecordRow> emissionRows,
        IReadOnlyList<TaxExpenseRow> expenseRows)
    {
        var autoRows = BuildReteFuenteAutoRows(emissionRows);
        var expenseReportRows = BuildReteFuenteExpenseRows(expenseRows);

        return new TaxReportDetailsDto
        {
            Tables = new[]
            {
                new TaxReportTableDto
                {
                    Key = "autofuente",
                    Label = "Autofuente",
                    DateColumnLabel = "Fecha emision",
                    NameColumnLabel = "Cliente",
                    TotalColumnLabel = "Total factura",
                    BaseColumnLabel = "Base antes de IVA",
                    AmountColumnLabel = "Autofuente",
                    ShowBaseColumn = true,
                    TotalBaseValue = SumCurrency(emissionRows, static row => CalculateInvoiceTaxBase(row)),
                    TotalValue = SumCurrency(emissionRows, static row => row.TotalInvoice),
                    TotalAmountValue = CalculateAutoFuente(emissionRows),
                    Rows = autoRows
                },
                new TaxReportTableDto
                {
                    Key = "retefuente-gastos",
                    Label = "ReteFuente gastos",
                    DateColumnLabel = "Fecha pago",
                    NameColumnLabel = "Receptor",
                    TotalColumnLabel = "Total factura",
                    BaseColumnLabel = "Base antes de IVA",
                    AmountColumnLabel = "ReteFuente",
                    CategoryColumnLabel = "Tipo persona",
                    ShowBaseColumn = true,
                    ShowCategoryColumn = true,
                    ShowReteFuentePercentColumn = true,
                    ShowReteIcaPercentColumn = true,
                    TotalBaseValue = SumExpenseCurrency(expenseRows.Where(static row => row.ReteFuenteValue > 0m), CalculateExpenseTaxBase),
                    TotalValue = SumExpenseCurrency(expenseRows.Where(static row => row.ReteFuenteValue > 0m), static row => row.TotalValue),
                    TotalAmountValue = SumExpenseCurrency(expenseRows.Where(static row => row.ReteFuenteValue > 0m), static row => row.ReteFuenteValue),
                    Rows = expenseReportRows
                }
            }
        };
    }

    private TaxReportDetailsDto BuildReteIcaReportDetails(
        IReadOnlyList<BillingRecordRow> generatedRows,
        IReadOnlyList<BillingRecordRow> favorRows)
    {
        var generatedReportRows = BuildReteIcaGeneratedRows(generatedRows);
        var favorReportRows = BuildReteIcaFavorRows(favorRows);

        return new TaxReportDetailsDto
        {
            Tables = new[]
            {
                new TaxReportTableDto
                {
                    Key = "reteica-generado",
                    Label = "Rete ICA generado",
                    DateColumnLabel = "Fecha emision",
                    NameColumnLabel = "Cliente",
                    TotalColumnLabel = "Total factura",
                    BaseColumnLabel = "Base antes de IVA",
                    AmountColumnLabel = "Rete ICA generado",
                    ShowBaseColumn = true,
                    ShowReteIcaPercentColumn = true,
                    TotalBaseValue = RoundCurrency(generatedReportRows.Sum(static row => row.BaseValue)),
                    TotalValue = RoundCurrency(generatedReportRows.Sum(static row => row.TotalValue)),
                    TotalAmountValue = RoundCurrency(generatedReportRows.Sum(static row => row.AmountValue)),
                    Rows = generatedReportRows
                },
                new TaxReportTableDto
                {
                    Key = "reteica-favor",
                    Label = "Rete ICA a favor",
                    DateColumnLabel = "Fecha pago",
                    NameColumnLabel = "Cliente",
                    TotalColumnLabel = "Valor pago",
                    BaseColumnLabel = "Total factura",
                    AmountColumnLabel = "Rete ICA a favor",
                    ShowBaseColumn = true,
                    ShowReteIcaPercentColumn = true,
                    TotalBaseValue = RoundCurrency(favorReportRows.Sum(static row => row.BaseValue)),
                    TotalValue = RoundCurrency(favorReportRows.Sum(static row => row.TotalValue)),
                    TotalAmountValue = RoundCurrency(favorReportRows.Sum(static row => row.AmountValue)),
                    Rows = favorReportRows
                }
            }
        };
    }

    private IReadOnlyList<TaxReportRowDto> BuildReteFuenteAutoRows(IEnumerable<BillingRecordRow> rows)
    {
        return rows
            .OrderBy(static row => row.EmissionDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var baseValue = CalculateInvoiceTaxBase(row);
                return new TaxReportRowDto
                {
                    DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    InvoiceNumber = row.InvoiceNumber,
                    Name = FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Sin cliente"),
                    BaseValue = baseValue,
                    TotalValue = RoundCurrency(row.TotalInvoice),
                    AmountValue = RoundCurrency(baseValue * DashboardAutoFuenteRate)
                };
            })
            .Where(static row => row.BaseValue > 0m)
            .ToList();
    }

    private IReadOnlyList<TaxReportRowDto> BuildReteFuenteExpenseRows(IEnumerable<TaxExpenseRow> rows)
    {
        return rows
            .Where(static row => row.ReteFuenteValue > 0m)
            .OrderBy(static row => row.PaymentDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TaxReportRowDto
            {
                DateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                InvoiceNumber = row.InvoiceNumber,
                Name = FirstNonEmpty(row.RecipientName, row.IssuerName, "Sin receptor"),
                Category = ResolveTaxPersonTypeLabel(row),
                BaseValue = CalculateExpenseTaxBase(row),
                TotalValue = RoundCurrency(row.TotalValue),
                AmountValue = RoundCurrency(row.ReteFuenteValue),
                ReteFuentePercent = CalculateExpenseRetentionPercent(row.ReteFuenteValue, row),
                ReteIcaPercent = CalculateExpenseRetentionPercent(row.ReteIcaValue, row)
            })
            .ToList();
    }

    private IReadOnlyList<TaxReportRowDto> BuildReteIcaGeneratedRows(IEnumerable<BillingRecordRow> rows)
    {
        return rows
            .OrderBy(static row => row.EmissionDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var baseValue = CalculateInvoiceTaxBase(row);
                return new TaxReportRowDto
                {
                    DateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    InvoiceNumber = row.InvoiceNumber,
                    Name = FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Sin cliente"),
                    BaseValue = baseValue,
                    TotalValue = RoundCurrency(row.TotalInvoice),
                    AmountValue = RoundCurrency(baseValue * DashboardIcaRate),
                    ReteIcaPercent = DashboardIcaRate * 100m
                };
            })
            .Where(static row => row.BaseValue > 0m)
            .ToList();
    }

    private IReadOnlyList<TaxReportRowDto> BuildReteIcaFavorRows(IEnumerable<BillingRecordRow> rows)
    {
        return rows
            .Where(static row => row.ReteIcaValue > 0m)
            .OrderBy(static row => row.PaymentDate)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TaxReportRowDto
            {
                DateDisplay = row.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                InvoiceNumber = row.InvoiceNumber,
                Name = FirstNonEmpty(row.ClientName, row.CompanyTaxId, "Sin cliente"),
                BaseValue = RoundCurrency(row.TotalInvoice),
                TotalValue = RoundCurrency(row.PaymentValue),
                AmountValue = RoundCurrency(row.ReteIcaValue),
                ReteIcaPercent = CalculateBillingRetentionPercent(row.ReteIcaValue, row)
            })
            .ToList();
    }

    private static TaxCalculationDetailDto BuildTaxCalculationDetail(
        string key,
        string label,
        string formula,
        decimal baseTotal,
        decimal invoiceTotal,
        int invoiceCount,
        string resultLabel,
        decimal resultValue,
        params TaxCalculationDetailLineDto[] lines)
    {
        return new TaxCalculationDetailDto
        {
            Key = key,
            Label = label,
            Formula = formula,
            BaseTotal = RoundCurrency(baseTotal),
            InvoiceTotal = RoundCurrency(invoiceTotal),
            InvoiceCount = Math.Max(invoiceCount, 0),
            ResultLabel = resultLabel,
            ResultValue = RoundCurrency(resultValue),
            Lines = lines
        };
    }

    private static TaxCalculationDetailLineDto BuildTaxCalculationLine(
        string label,
        decimal value,
        string valueFormat = "currency")
    {
        return new TaxCalculationDetailLineDto
        {
            Label = label,
            Value = RoundCurrency(value),
            ValueFormat = valueFormat
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
            ShowComparison = false,
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
        bool lowerIsBetter = false,
        bool showComparison = true)
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
            ShowComparison = showComparison,
            SecondaryLabel = secondaryLabel,
            SecondaryValue = secondaryValue,
            Breakdowns = breakdowns ?? Array.Empty<BillingKpiBreakdownDto>()
        };
    }

    private BillingKpiDto BuildTaxKpi(
        string key,
        string label,
        string hint,
        decimal value,
        string valueFormat = "currency",
        string secondaryLabel = "",
        string secondaryValue = "")
    {
        return BuildBillingKpi(
            key,
            label,
            hint,
            value,
            0m,
            valueFormat,
            secondaryLabel,
            secondaryValue,
            showComparison: false);
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
        var metadata = await ResolveRhEntityMetadataAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            user,
            ct);
        var attributes = await GetDashboardEntityAttributeNamesAsync(metadata.LogicalName, user, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);

        var dateFilters = new[]
        {
            BuildBillingDateFilter(
                fields.PaymentDateField.FieldName,
                fields.PaymentDateField.FieldKind,
                startInclusive,
                endExclusive),
            BuildBillingDateFilter(
                fields.EmissionDateField.FieldName,
                fields.EmissionDateField.FieldKind,
                startInclusive,
                endExclusive)
        }
        .Where(static filterValue => !string.IsNullOrWhiteSpace(filterValue))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
        if (dateFilters.Count == 0)
            throw new InvalidOperationException("No encontramos un campo de fecha valido en la tabla de gastos para calcular impuestos.");

        var filter = string.Join(" or ", dateFilters.Select(static filterValue => $"({filterValue})"));
        var orderByField = FirstNonEmpty(fields.PaymentDateField.FieldName, fields.EmissionDateField.FieldName, metadata.PrimaryIdField);
        var effectiveInvoiceNumberField = fields.InvoiceNumberField;

        string BuildSelect(string currentInvoiceNumberField) => string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            currentInvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.PaymentValueField,
            fields.ReteFuenteField,
            fields.ReteIcaField,
            fields.TotalField,
            fields.VatField,
            fields.IssuerNameField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            fields.CloudField,
            fields.CopiersField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        string BuildRelativeUrl(string selectFields) =>
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={selectFields}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderByField} asc";

        List<JsonElement> items;
        try
        {
            items = await GetDataverseEntitiesAsync(
                BuildRelativeUrl(BuildSelect(effectiveInvoiceNumberField)),
                user,
                ct,
                AddFormattedValueHeaders);
        }
        catch (InvalidOperationException ex) when (
            !ct.IsCancellationRequested
            && IsMissingDataversePropertyError(ex, effectiveInvoiceNumberField))
        {
            _logger.LogWarning(
                ex,
                "El campo configurado como numero de factura de gastos ({FieldName}) no existe en Dataverse. Se consulta sin ese campo.",
                effectiveInvoiceNumberField);
            effectiveInvoiceNumberField = "";
            items = await GetDataverseEntitiesAsync(
                BuildRelativeUrl(BuildSelect(effectiveInvoiceNumberField)),
                user,
                ct,
                AddFormattedValueHeaders);
        }

        return items
            .Select(item => ParseTaxExpenseRow(item, metadata.PrimaryIdField, fields with { InvoiceNumberField = effectiveInvoiceNumberField }))
            .Where(static row => row is not null)
            .Cast<TaxExpenseRow>()
            .ToList();
    }

    private static bool IsMissingDataversePropertyError(Exception ex, string fieldName) =>
        !string.IsNullOrWhiteSpace(fieldName)
        && ex.Message.Contains($"property named '{fieldName}'", StringComparison.OrdinalIgnoreCase);

    private async Task<HashSet<string>> GetDashboardEntityAttributeNamesAsync(
        string entityLogicalName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var cacheKey = entityLogicalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cacheKey))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_dashboardEntityAttributeNamesCache.TryGetValue(cacheKey, out var cached))
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(cacheKey)}')" +
                "/Attributes?$select=LogicalName";

            try
            {
                var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
                cached = items
                    .Select(static item => ReadString(item, "LogicalName").Trim())
                    .Where(static field => !string.IsNullOrWhiteSpace(field))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception userEx) when (!ct.IsCancellationRequested)
            {
                try
                {
                    var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct);
                    cached = items
                        .Select(static item => ReadString(item, "LogicalName").Trim())
                        .Where(static field => !string.IsNullOrWhiteSpace(field))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception appEx) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        appEx,
                        "No fue posible consultar la metadata de atributos de {EntityLogicalName}. Se usaran los campos configurados. Error usuario: {UserMetadataError}",
                        cacheKey,
                        userEx.Message);
                    cached = Array.Empty<string>();
                }
            }

            _dashboardEntityAttributeNamesCache[cacheKey] = cached;
        }

        return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
    }

    private TaxExpenseFieldMap ResolveTaxExpenseFieldMap(
        RhEntityMetadata metadata,
        IReadOnlySet<string> attributes)
    {
        var emissionDateField = ResolveTaxExpenseDateField(
            BuildTaxExpenseEmissionDateCandidates(),
            attributes);
        var paymentDateField = ResolveTaxExpenseDateField(
            BuildTaxExpensePaymentDateCandidates(),
            attributes);

        return new TaxExpenseFieldMap(
            InvoiceNumberField: ResolveTaxExpenseField(
                attributes,
                _supplierExpensesInvoiceNumberField,
                metadata.PrimaryNameField,
                "cr07a_numerofactura",
                "cr07a_numfactura",
                "cr07a_factura",
                "cr07a_numero"),
            EmissionDateField: emissionDateField,
            PaymentDateField: paymentDateField,
            PaymentValueField: ResolveTaxExpenseField(
                attributes,
                DashboardExpensePaymentValueField,
                DashboardExpenseTotalField,
                "cr07a_totalfactura"),
            ReteFuenteField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseReteFuenteField,
                "cr07a_retefuentevalor",
                "cr07a_rteftevalor",
                "cr07a_retencionfuente"),
            ReteIcaField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseReteIcaField,
                "cr07a_reteicavalor"),
            TotalField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseTotalField,
                "cr07a_totalfactura",
                DashboardExpensePaymentValueField),
            VatField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseVatField,
                "cr07a_ivavalor"),
            IssuerNameField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseIssuerNameField,
                "cr07a_emisor",
                "cr07a_nombreproveedor",
                "cr07a_proveedor"),
            RecipientNameField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseRecipientNameField,
                "cr07a_receptor"),
            RecipientNitField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseRecipientNitField,
                "cr07a_nit"),
            CloudField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseCloudField),
            CopiersField: ResolveTaxExpenseField(
                attributes,
                DashboardExpenseCopiersField));
    }

    private IEnumerable<PnlExpenseDateFieldCandidate> BuildTaxExpenseEmissionDateCandidates()
    {
        foreach (var candidate in DashboardExpenseEmissionDateFieldCandidates)
            yield return candidate;

        yield return new PnlExpenseDateFieldCandidate(_supplierExpensesDateField, _supplierExpensesDateFieldKind);
        yield return new PnlExpenseDateFieldCandidate("createdon", "date-time");
    }

    private IEnumerable<PnlExpenseDateFieldCandidate> BuildTaxExpensePaymentDateCandidates()
    {
        yield return new PnlExpenseDateFieldCandidate(DashboardExpensePaymentDateField, DashboardExpensePaymentDateFieldKind);
        yield return new PnlExpenseDateFieldCandidate(_supplierExpensesDateField, _supplierExpensesDateFieldKind);
        yield return new PnlExpenseDateFieldCandidate("createdon", "date-time");
    }

    private static PnlExpenseDateFieldCandidate ResolveTaxExpenseDateField(
        IEnumerable<PnlExpenseDateFieldCandidate> candidates,
        IReadOnlySet<string> attributes)
    {
        return candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.FieldName))
            .DistinctBy(static candidate => candidate.FieldName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(candidate => IsDashboardDataverseFieldAvailable(candidate.FieldName, attributes))
            ?? new PnlExpenseDateFieldCandidate("", "date-only");
    }

    private static string ResolveTaxExpenseField(
        IReadOnlySet<string> attributes,
        params string[] candidates)
    {
        return candidates
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Select(static field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(field => IsDashboardDataverseFieldAvailable(field, attributes))
            ?? "";
    }

    private static bool IsDashboardDataverseFieldAvailable(string? field, IReadOnlySet<string> attributes)
    {
        if (string.IsNullOrWhiteSpace(field))
            return false;

        if (attributes.Count == 0)
            return true;

        var normalizedField = NormalizeDashboardDataverseAttributeName(field);
        return !string.IsNullOrWhiteSpace(normalizedField)
            && attributes.Contains(normalizedField);
    }

    private static string NormalizeDashboardDataverseAttributeName(string field)
    {
        var trimmed = field.Trim();
        if (trimmed.StartsWith('_') && trimmed.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
            return trimmed[1..^6];

        return trimmed;
    }

    private TaxExpenseRow? ParseTaxExpenseRow(
        JsonElement item,
        string idField,
        TaxExpenseFieldMap fields)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, idField),
            $"{ReadString(item, fields.RecipientNitField)}|{ReadString(item, fields.RecipientNameField)}|{ReadString(item, fields.PaymentDateField.FieldName)}");

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var invoiceNumber = string.IsNullOrWhiteSpace(fields.InvoiceNumberField)
            ? ""
            : FirstNonEmpty(
                ReadString(item, $"{fields.InvoiceNumberField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, fields.InvoiceNumberField));

        return new TaxExpenseRow
        {
            RecordId = recordId.Trim(),
            InvoiceNumber = invoiceNumber.Trim(),
            EmissionDate = ReadDateOnly(item, fields.EmissionDateField.FieldName),
            PaymentDate = ReadDateOnly(item, fields.PaymentDateField.FieldName),
            PaymentValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.PaymentValueField)),
            ReteFuenteValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.ReteFuenteField)),
            ReteIcaValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.ReteIcaField)),
            TotalValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.TotalField)),
            VatValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.VatField)),
            IssuerName = ReadString(item, fields.IssuerNameField).Trim(),
            RecipientName = ReadString(item, fields.RecipientNameField).Trim(),
            RecipientNit = ReadString(item, fields.RecipientNitField).Trim(),
            CloudValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.CloudField)),
            CopiersValue = RoundCurrency(ReadTaxExpenseDecimal(item, fields.CopiersField))
        };
    }

    private static decimal ReadTaxExpenseDecimal(JsonElement item, string fieldName) =>
        string.IsNullOrWhiteSpace(fieldName)
            ? 0m
            : ReadDecimal(item, fieldName) ?? 0m;

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

    private async Task<IReadOnlyList<CopiersBillingEquipmentDto>> BuildCopiersBillingEquipmentRowsAsync(
        IReadOnlyList<CopiersBillingRecordRow> billingRows,
        ClaimsPrincipal user,
        DateOnly periodStart,
        DateOnly periodEnd,
        string counterPeriodLabel,
        CancellationToken ct)
    {
        if (billingRows.Count == 0)
            return Array.Empty<CopiersBillingEquipmentDto>();

        var clientIds = billingRows
            .Select(static row => NormalizeOptionalGuid(row.ClientId))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clientNames = billingRows
            .Select(static row => NormalizeCopiersComparableValue(row.ClientName))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var equipmentMetadata = await ResolveRhEntityMetadataAsync(
                DashboardEquipmentTableLogicalName,
                DashboardEquipmentTableSetName,
                DashboardEquipmentIdField,
                DashboardEquipmentPrimaryNameField,
                user,
                ct);
            var equipmentRows = (await GetEquipmentRecordsAsync(equipmentMetadata, user, ct))
                .Where(static row => !row.InStock)
                .Where(row => CopiersBillingClientMatches(row, clientIds, clientNames))
                .ToList();

            if (equipmentRows.Count == 0)
                return Array.Empty<CopiersBillingEquipmentDto>();

            var semaphore = new SemaphoreSlim(8);
            var tasks = equipmentRows.Select(async equipment =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var counter = await GetCopiersLastCounterReadingAsync(
                        equipment.RecordId,
                        equipment.Serial,
                        periodStart,
                        periodEnd,
                        user,
                        ct);
                    var hasCurrentCounter = counter.Date.HasValue;
                    var counterDateDisplay = FormatCopiersCounterDateDisplay(counter.Date);

                    return new CopiersBillingEquipmentDto
                    {
                        RecordId = equipment.RecordId,
                        Serial = equipment.Serial,
                        ClientId = NormalizeOptionalGuid(equipment.ClientId),
                        ClientName = FirstNonEmpty(equipment.ClientName, "Sin cliente"),
                        CategoryLabel = equipment.CategoryLabel,
                        Reference = equipment.Reference,
                        Area = equipment.Area,
                        Site = equipment.Site,
                        HasCurrentCounter = hasCurrentCounter,
                        CounterDateValue = FormatCopiersCounterDateValue(counter.Date),
                        CounterDateDisplay = counterDateDisplay,
                        CounterCopies = counter.Copies,
                        CounterScans = counter.Scans,
                        CounterStatusLabel = hasCurrentCounter
                            ? $"Contador registrado el {counterDateDisplay}"
                            : $"Pendiente de contador - {counterPeriodLabel}",
                        CounterStatusTone = hasCurrentCounter ? "ok" : "pending"
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            return (await Task.WhenAll(tasks))
                .OrderBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Serial, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "No fue posible enriquecer la facturacion copiers con equipos y contadores de los ultimos 35 dias.");
            return Array.Empty<CopiersBillingEquipmentDto>();
        }
    }

    private static bool CopiersBillingClientMatches(
        CopiersEquipmentRecordRow equipment,
        IReadOnlySet<string> clientIds,
        IReadOnlySet<string> clientNames)
    {
        var equipmentClientId = NormalizeOptionalGuid(equipment.ClientId);
        if (!string.IsNullOrWhiteSpace(equipmentClientId) && clientIds.Contains(equipmentClientId))
            return true;

        var equipmentClientName = NormalizeCopiersComparableValue(equipment.ClientName);
        return !string.IsNullOrWhiteSpace(equipmentClientName) && clientNames.Contains(equipmentClientName);
    }

    private IReadOnlyList<CopiersBillingGroupDto> BuildCopiersBillingGroups(
        IReadOnlyList<CopiersBillingRowDto> rows,
        IReadOnlyList<CopiersBillingEquipmentDto> equipmentRows)
    {
        return rows
            .GroupBy(
                row => $"{BuildDashboardGroupKey(row.ClientId, row.ClientName)}|day:{row.BillingDay}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var lines = group
                    .OrderBy(static row => row.ProductName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var first = lines[0];
                var equipment = FindCopiersBillingEquipment(first, equipmentRows);
                var countersRegistered = equipment.Count(static row => row.HasCurrentCounter);
                var pendingCounters = equipment.Count - countersRegistered;

                return new CopiersBillingGroupDto
                {
                    GroupId = group.Key,
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    BillingDay = first.BillingDay,
                    BillingDayDisplay = first.BillingDayDisplay,
                    ProductLinesCount = lines.Count,
                    EquipmentCount = equipment.Count,
                    CountersRegisteredCount = countersRegistered,
                    PendingCountersCount = pendingCounters,
                    Quantity = RoundCurrency(lines.Sum(static row => row.Quantity)),
                    IncludedOperations = RoundCurrency(lines.Sum(static row => CalculateCopiersLineIncludedOperations(row.Quantity, row.IncludedOperations))),
                    AdditionalOperation = RoundCurrency(lines.Sum(static row => row.AdditionalOperation)),
                    TotalWithVat = RoundCurrency(lines.Sum(static row => row.TotalWithVat)),
                    CounterSummary = equipment.Count == 0
                        ? "Sin equipos asignados"
                        : pendingCounters == 0
                            ? "Contadores al dia"
                            : $"{pendingCounters.ToString("N0", DashboardCulture)} pendiente(s)",
                    EquipmentAssignedToLinesCount = lines.Sum(static row => row.AssignedEquipmentCount),
                    EquipmentAvailableForLinesCount = Math.Max(equipment.Count - lines.Sum(static row => row.AssignedEquipmentCount), 0),
                    EquipmentAssignmentSummary = BuildCopiersLineEquipmentAssignmentSummary(
                        lines.Sum(static row => row.AssignedEquipmentCount),
                        lines.Sum(static row => row.EquipmentAssignmentCapacity),
                        Math.Max(equipment.Count - lines.Sum(static row => row.AssignedEquipmentCount), 0)),
                    Lines = lines,
                    Equipment = equipment
                };
            })
            .OrderBy(static group => group.BillingDay is >= 1 and <= 31 ? group.BillingDay : int.MaxValue)
            .ThenBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CopiersBillingEquipmentDto> FindCopiersBillingEquipment(
        CopiersBillingRowDto row,
        IReadOnlyList<CopiersBillingEquipmentDto> equipmentRows)
    {
        return FindCopiersBillingEquipment(row.ClientId, row.ClientName, equipmentRows);
    }

    private static IReadOnlyList<CopiersBillingEquipmentDto> FindCopiersBillingEquipment(
        CopiersBillingRecordRow row,
        IReadOnlyList<CopiersBillingEquipmentDto> equipmentRows)
    {
        return FindCopiersBillingEquipment(row.ClientId, row.ClientName, equipmentRows);
    }

    private static IReadOnlyList<CopiersBillingEquipmentDto> FindCopiersBillingEquipment(
        string clientIdValue,
        string clientNameValue,
        IReadOnlyList<CopiersBillingEquipmentDto> equipmentRows)
    {
        var clientId = NormalizeOptionalGuid(clientIdValue);
        var clientName = NormalizeCopiersComparableValue(clientNameValue);

        return equipmentRows
            .Where(equipment =>
            {
                var equipmentClientId = NormalizeOptionalGuid(equipment.ClientId);
                if (!string.IsNullOrWhiteSpace(clientId)
                    && string.Equals(equipmentClientId, clientId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var equipmentClientName = NormalizeCopiersComparableValue(equipment.ClientName);
                return !string.IsNullOrWhiteSpace(clientName)
                    && string.Equals(equipmentClientName, clientName, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(static equipment => equipment.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool CopiersBillingAssignmentClientMatches(
        CopiersLineEquipmentAssignmentRecordRow assignment,
        CopiersBillingRecordRow row)
    {
        var assignmentClientId = NormalizeOptionalGuid(assignment.ClientId);
        var rowClientId = NormalizeOptionalGuid(row.ClientId);
        if (!string.IsNullOrWhiteSpace(assignmentClientId)
            && !string.IsNullOrWhiteSpace(rowClientId)
            && string.Equals(assignmentClientId, rowClientId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var assignmentClientName = NormalizeCopiersComparableValue(assignment.ClientName);
        var rowClientName = NormalizeCopiersComparableValue(row.ClientName);
        return !string.IsNullOrWhiteSpace(assignmentClientName)
            && !string.IsNullOrWhiteSpace(rowClientName)
            && string.Equals(assignmentClientName, rowClientName, StringComparison.OrdinalIgnoreCase);
    }

    private static decimal CalculateCopiersLineIncludedOperations(decimal quantity, decimal includedOperations) =>
        RoundCurrency(Math.Max(quantity, 0m) * Math.Max(includedOperations, 0m));

    private IReadOnlyList<BillingTrendPointDto> BuildBillingYtdTrend(
        int year,
        int compareYear,
        DateOnly ytdEndExclusive,
        IReadOnlyList<BillingRecordRow> emissionRecords,
        IReadOnlyList<BillingRecordRow> paymentRecords)
    {
        var maxMonth = Math.Clamp(ytdEndExclusive.AddDays(-1).Month, 1, 12);
        var currentStart = new DateOnly(year, 1, 1);
        var currentEnd = new DateOnly(year, maxMonth, 1).AddMonths(1);
        var compareStart = new DateOnly(compareYear, 1, 1);
        var compareEnd = new DateOnly(compareYear, maxMonth, 1).AddMonths(1);

        return Enumerable.Range(1, maxMonth)
            .Select(month =>
            {
                var currentEmission = emissionRecords
                    .Where(record => record.EmissionDate is not null
                        && record.EmissionDate.Value >= currentStart
                        && record.EmissionDate.Value < currentEnd
                        && record.EmissionDate.Value.Year == year
                        && record.EmissionDate.Value.Month == month)
                    .ToList();
                var compareEmission = emissionRecords
                    .Where(record => record.EmissionDate is not null
                        && record.EmissionDate.Value >= compareStart
                        && record.EmissionDate.Value < compareEnd
                        && record.EmissionDate.Value.Year == compareYear
                        && record.EmissionDate.Value.Month == month)
                    .ToList();
                var currentPayments = paymentRecords
                    .Where(record => record.PaymentDate is not null
                        && record.PaymentDate.Value >= currentStart
                        && record.PaymentDate.Value < currentEnd
                        && record.PaymentDate.Value.Year == year
                        && record.PaymentDate.Value.Month == month)
                    .ToList();
                var comparePayments = paymentRecords
                    .Where(record => record.PaymentDate is not null
                        && record.PaymentDate.Value >= compareStart
                        && record.PaymentDate.Value < compareEnd
                        && record.PaymentDate.Value.Year == compareYear
                        && record.PaymentDate.Value.Month == month)
                    .ToList();

                var billingCurrent = SumCurrency(currentEmission, static record => record.TotalInvoice);
                var billingPrevious = SumCurrency(compareEmission, static record => record.TotalInvoice);
                var collectionsCurrent = SumCurrency(currentPayments, static record => record.PaymentValue);
                var collectionsPrevious = SumCurrency(comparePayments, static record => record.PaymentValue);
                var retentionsCurrent = SumCurrency(currentPayments, static record => record.RetentionsTotal);
                var retentionsPrevious = SumCurrency(comparePayments, static record => record.RetentionsTotal);

                return new BillingTrendPointDto
                {
                    Key = month.ToString(CultureInfo.InvariantCulture),
                    Label = ToTitleCase(new DateOnly(year, month, 1).ToString("MMM", DashboardCulture)),
                    BillingCurrent = billingCurrent,
                    BillingPrevious = billingPrevious,
                    BillingGrowthPercent = CalculateGrowthPercent(billingCurrent, billingPrevious),
                    CollectionsCurrent = collectionsCurrent,
                    CollectionsPrevious = collectionsPrevious,
                    CollectionsGrowthPercent = CalculateGrowthPercent(collectionsCurrent, collectionsPrevious),
                    RetentionsCurrent = retentionsCurrent,
                    RetentionsPrevious = retentionsPrevious,
                    RetentionsGrowthPercent = CalculateGrowthPercent(retentionsCurrent, retentionsPrevious)
                };
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
                PersonTypeLabel = ResolveTaxPersonTypeLabel(row),
                RecipientName = string.IsNullOrWhiteSpace(row.RecipientName) ? "Sin receptor" : row.RecipientName,
                RecipientNit = string.IsNullOrWhiteSpace(row.RecipientNit) ? "Sin NIT" : row.RecipientNit,
                CloudValue = row.CloudValue,
                CopiersValue = row.CopiersValue
            })
            .ToList();
    }

    private static string ResolveTaxPersonTypeLabel(TaxExpenseRow row)
    {
        return ResolveTaxPersonTypeKey(row) switch
        {
            "legal" => "Persona juridica",
            "natural" => "Persona natural",
            _ => "Sin clasificar"
        };
    }

    private static string ResolveTaxPersonTypeKey(TaxExpenseRow row)
    {
        var name = NormalizeTaxClassifierText(row.RecipientName);
        if (!string.IsNullOrWhiteSpace(name)
            && TaxLegalEntityTokens.Any(token => name.Contains(token, StringComparison.Ordinal)))
        {
            return "legal";
        }

        var digits = new string((row.RecipientNit ?? "")
            .Where(char.IsDigit)
            .ToArray());

        return digits.Length switch
        {
            >= 10 => "natural",
            9 => "legal",
            _ => "unknown"
        };
    }

    private static string NormalizeTaxClassifierText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) CalculateExpenseRetentionByVertical(IEnumerable<TaxExpenseRow> rows)
    {
        return CalculateExpenseCurrencyByVertical(rows, static row => row.ReteFuenteValue);
    }

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) CalculateExpenseRowVerticalSplit(
        TaxExpenseRow row,
        decimal value)
    {
        if (value <= 0m)
            return (0m, 0m, 0m);

        var cloudBase = Math.Max(row.CloudValue, 0m);
        var copiersBase = Math.Max(row.CopiersValue, 0m);

        if (cloudBase > 0m && copiersBase <= 0m)
            return (RoundCurrency(value), 0m, 0m);

        if (copiersBase > 0m && cloudBase <= 0m)
            return (0m, RoundCurrency(value), 0m);

        var totalBase = cloudBase + copiersBase;
        if (totalBase <= 0m)
            return (0m, 0m, RoundCurrency(value));

        return (
            RoundCurrency(value * (cloudBase / totalBase)),
            RoundCurrency(value * (copiersBase / totalBase)),
            0m);
    }

    private static string ResolveDominantExpenseVerticalKey(TaxExpenseRow row)
    {
        var cloudBase = Math.Max(row.CloudValue, 0m);
        var copiersBase = Math.Max(row.CopiersValue, 0m);

        if (cloudBase <= 0m && copiersBase <= 0m)
            return "unassigned";

        if (cloudBase >= copiersBase)
            return "cloud";

        return "copiers";
    }

    private static string ResolveDominantExpenseVerticalLabel(TaxExpenseRow row) =>
        ResolveDominantExpenseVerticalKey(row) switch
        {
            "cloud" => "Cloud",
            "copiers" => "Copiers",
            _ => "Sin vertical"
        };

    private static (decimal Cloud, decimal Copiers, decimal Unassigned) CalculateExpenseCurrencyByVertical(
        IEnumerable<TaxExpenseRow> rows,
        Func<TaxExpenseRow, decimal> selector)
    {
        decimal cloud = 0m;
        decimal copiers = 0m;
        decimal unassigned = 0m;

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value <= 0m)
                continue;

            var cloudBase = Math.Max(row.CloudValue, 0m);
            var copiersBase = Math.Max(row.CopiersValue, 0m);

            if (cloudBase > 0m && copiersBase <= 0m)
            {
                cloud += value;
                continue;
            }

            if (copiersBase > 0m && cloudBase <= 0m)
            {
                copiers += value;
                continue;
            }

            var totalBase = cloudBase + copiersBase;
            if (totalBase <= 0m)
            {
                unassigned += value;
                continue;
            }

            cloud += value * (cloudBase / totalBase);
            copiers += value * (copiersBase / totalBase);
        }

        return (RoundCurrency(cloud), RoundCurrency(copiers), RoundCurrency(unassigned));
    }

    private static string ResolveTaxVerticalKey(int optionValue) =>
        optionValue switch
        {
            DashboardVerticalCloudOption => "cloud",
            DashboardVerticalCopiersOption => "copiers",
            _ => "unassigned"
        };

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

    private static decimal CalculateExpenseTaxBase(TaxExpenseRow row) =>
        RoundCurrency(Math.Max(row.TotalValue - row.VatValue, 0m));

    private static decimal CalculateExpenseRetentionPercent(decimal retentionValue, TaxExpenseRow row)
    {
        var baseBeforeVat = CalculateExpenseTaxBase(row);
        return baseBeforeVat <= 0m
            ? 0m
            : RoundCurrency((retentionValue / baseBeforeVat) * 100m);
    }

    private static decimal CalculateBillingRetentionPercent(decimal retentionValue, BillingRecordRow row)
    {
        var baseBeforeVat = CalculateInvoiceTaxBase(row);
        return baseBeforeVat <= 0m
            ? 0m
            : RoundCurrency((retentionValue / baseBeforeVat) * 100m);
    }

    private static List<BillingRecordRow> FilterBillingEmissionByPeriod(
        IEnumerable<BillingRecordRow> rows,
        BillingPeriodDefinition period)
    {
        return rows
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CurrentStartInclusive
                && record.EmissionDate.Value < period.CurrentEndExclusive)
            .ToList();
    }

    private static List<BillingRecordRow> FilterBillingPaymentByPeriod(
        IEnumerable<BillingRecordRow> rows,
        BillingPeriodDefinition period)
    {
        return rows
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();
    }

    private static List<TaxExpenseRow> FilterTaxExpensesByPeriod(
        IEnumerable<TaxExpenseRow> rows,
        BillingPeriodDefinition period)
    {
        return rows
            .Where(record => record.PaymentDate is not null
                && record.PaymentDate.Value >= period.CurrentStartInclusive
                && record.PaymentDate.Value < period.CurrentEndExclusive)
            .ToList();
    }

    private static List<TaxExpenseRow> FilterTaxExpensesByEmissionPeriod(
        IEnumerable<TaxExpenseRow> rows,
        BillingPeriodDefinition period)
    {
        return rows
            .Where(record => record.EmissionDate is not null
                && record.EmissionDate.Value >= period.CurrentStartInclusive
                && record.EmissionDate.Value < period.CurrentEndExclusive)
            .ToList();
    }

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

    private IReadOnlyList<BillingInvoiceRowDto> BuildBillingInvoiceRows(
        IReadOnlyList<BillingRecordRow> rows,
        DateOnly today)
    {
        return rows
            .Select(record =>
            {
                var isOverdue = record.IsOverdue(today);

                return new BillingInvoiceRowDto
                {
                    RecordId = record.RecordId,
                    InvoiceNumber = record.InvoiceNumber,
                    ClientId = record.ClientId,
                    ClientName = record.ClientName,
                    CompanyTaxId = record.CompanyTaxId,
                    VerticalOptionValue = record.VerticalOptionValue > 0 ? record.VerticalOptionValue : null,
                    VerticalLabel = record.VerticalLabel,
                    ContractTypeOptionValue = record.ContractTypeOptionValue > 0 ? record.ContractTypeOptionValue : null,
                    ContractTypeLabel = record.ContractTypeLabel,
                    EmissionDateValue = record.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    EmissionDateDisplay = record.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    DueDateValue = record.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    DueDateDisplay = record.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
                    PaymentDateValue = record.PaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    PaymentDateDisplay = record.PaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin pago",
                    TotalInvoice = record.TotalInvoice,
                    VatPercent = record.VatPercent,
                    VatValue = record.VatValue,
                    PaymentValue = record.PaymentValue,
                    ReteIcaValue = record.ReteIcaValue,
                    RteIvaValue = record.RteIvaValue,
                    RteFteValue = record.RteFteValue,
                    RetentionsTotal = record.RetentionsTotal,
                    DifferenceValue = record.DifferenceValue,
                    PaymentStatusLabel = record.HasPayment ? "Con pago" : isOverdue ? "Vencida" : "Pendiente",
                    AgeDays = record.GetOverdueDays(today),
                    PublicUrl = record.PublicUrl
                };
            })
            .OrderByDescending(static row => row.EmissionDateValue)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
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
            BillingPeriodKind.Bimonthly => BuildBimonthlyPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? ((today.Month - 1) / 2) + 1 : 1)),
            BillingPeriodKind.Quarter => BuildQuarterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? ((today.Month - 1) / 3) + 1 : 1)),
            BillingPeriodKind.Semester => BuildSemesterPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? (today.Month <= 6 ? 1 : 2) : 1)),
            BillingPeriodKind.Year => BuildYearPeriod(resolvedYear, compareYear),
            _ => BuildMonthPeriod(resolvedYear, compareYear, periodValue ?? (resolvedYear == today.Year ? today.Month : 1))
        };
    }

    private bool ReadCopiersGroupIncludedOperations(JsonElement item)
    {
        if (string.IsNullOrWhiteSpace(_dashboardCopiersGroupField))
            return true;

        var formatted = ReadString(item, $"{_dashboardCopiersGroupField}{FormattedValueAnnotationSuffix}").Trim();
        if (TryParseCopiersGroupFlag(formatted, out var formattedValue))
            return formattedValue;

        if (!item.TryGetProperty(_dashboardCopiersGroupField, out var property))
            return true;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.TryGetInt32(out var intValue) ? intValue != 0 : true,
            JsonValueKind.String => TryParseCopiersGroupFlag(property.GetString(), out var stringValue) ? stringValue : true,
            _ => true
        };
    }

    private static bool TryParseCopiersGroupFlag(string? rawValue, out bool value)
    {
        value = true;
        var normalized = NormalizeCopiersComparableValue(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized is "si" or "sí" or "yes" or "true" or "1" or "agrupar" or "agrupado")
        {
            value = true;
            return true;
        }

        if (normalized is "no" or "false" or "0" or "individual" or "no agrupar")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static DateOnly ResolveBillingYtdEndExclusive(int year, DateOnly today)
    {
        var lastVisibleMonth = year == today.Year ? today.Month : 12;
        return new DateOnly(year, Math.Clamp(lastVisibleMonth, 1, 12), 1).AddMonths(1);
    }

    private static DateOnly MaxDateOnly(DateOnly left, DateOnly right) =>
        left.DayNumber >= right.DayNumber ? left : right;

    private static int ResolveTaxReferenceMonth(
        int resolvedYear,
        BillingPeriodKind periodKind,
        int? periodValue,
        DateOnly today)
    {
        return periodKind switch
        {
            BillingPeriodKind.Bimonthly => Math.Clamp(periodValue ?? ((resolvedYear == today.Year ? ((today.Month - 1) / 2) + 1 : 1)), 1, 6) * 2,
            BillingPeriodKind.Quarter => Math.Clamp(periodValue ?? ((resolvedYear == today.Year ? ((today.Month - 1) / 3) + 1 : 1)), 1, 4) * 3,
            BillingPeriodKind.Semester => Math.Clamp(periodValue ?? (resolvedYear == today.Year ? (today.Month <= 6 ? 1 : 2) : 1), 1, 2) * 6,
            BillingPeriodKind.Year => resolvedYear == today.Year ? today.Month : 12,
            _ => Math.Clamp(periodValue ?? (resolvedYear == today.Year ? today.Month : 1), 1, 12)
        };
    }

    private static int NormalizeTaxYear(int? rawYear, int fallbackYear, int minYear)
    {
        var year = rawYear is >= 2000 and <= 2100
            ? rawYear.Value
            : fallbackYear;

        return Math.Clamp(year, minYear, 2100);
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

    private static BillingPeriodDefinition BuildBimonthlyPeriod(int year, int compareYear, int bimonthly)
    {
        var resolvedBimonthly = Math.Clamp(bimonthly, 1, 6);
        var startMonth = ((resolvedBimonthly - 1) * 2) + 1;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(2);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(2);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Bimonthly,
            PeriodValue = resolvedBimonthly,
            PeriodLabel = $"B{resolvedBimonthly}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs B{resolvedBimonthly} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 2)
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

    private static BillingPeriodDefinition BuildFourMonthlyPeriod(int year, int compareYear, int fourMonthly)
    {
        var resolvedFourMonthly = Math.Clamp(fourMonthly, 1, 3);
        var startMonth = ((resolvedFourMonthly - 1) * 4) + 1;
        var currentStart = new DateOnly(year, startMonth, 1);
        var currentEnd = currentStart.AddMonths(4);
        var compareStart = new DateOnly(compareYear, startMonth, 1);
        var compareEnd = compareStart.AddMonths(4);

        return new BillingPeriodDefinition
        {
            Year = year,
            CompareYear = compareYear,
            PeriodKind = BillingPeriodKind.Quarter,
            PeriodValue = resolvedFourMonthly,
            PeriodLabel = $"C{resolvedFourMonthly}",
            DateRangeLabel = BuildDateRangeLabel(currentStart, currentEnd),
            CompareLabel = $"Vs C{resolvedFourMonthly} {compareYear}",
            CurrentStartInclusive = currentStart,
            CurrentEndExclusive = currentEnd,
            CompareStartInclusive = compareStart,
            CompareEndExclusive = compareEnd,
            TrendGranularity = BillingTrendGranularity.Month,
            GranularityLabel = "Mensual",
            Categories = BuildMonthCategories(currentStart, 4)
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
        if (string.IsNullOrWhiteSpace(fieldName))
            return "";

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

    private static IReadOnlyList<BillingOptionDto> BuildBillingVerticalOptions()
    {
        return new[]
        {
            new BillingOptionDto { Value = DashboardVerticalCloudOption, Label = "Cloud" },
            new BillingOptionDto { Value = DashboardVerticalCopiersOption, Label = "Copiers" }
        };
    }

    private static IReadOnlyList<BillingOptionDto> BuildBillingContractTypeOptions()
    {
        return new[]
        {
            new BillingOptionDto { Value = DashboardContractTypeMonthlyOption, Label = "Mensual" },
            new BillingOptionDto { Value = DashboardContractTypeOneTimeOption, Label = "OneTime" }
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

    private static string NormalizeDashboardLookupLogicalName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return "";

        var trimmed = fieldName.Trim();
        const string lookupSuffix = "_value";
        return trimmed.StartsWith("_", StringComparison.OrdinalIgnoreCase)
            && trimmed.EndsWith(lookupSuffix, StringComparison.OrdinalIgnoreCase)
            && trimmed.Length > lookupSuffix.Length + 1
                ? trimmed.Substring(1, trimmed.Length - lookupSuffix.Length - 1)
                : trimmed;
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
        public string ClientId { get; set; } = "";
        public string CompanyTaxId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string BusinessGroupId { get; set; } = "";
        public string BusinessGroupName { get; set; } = "";
        public string VerticalLabel { get; set; } = "";
        public string ContractTypeLabel { get; set; } = "";
        public int VerticalOptionValue { get; set; }
        public int ContractTypeOptionValue { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateOnly? EmissionDate { get; set; }
        public DateOnly? PaymentDate { get; set; }
        public decimal TotalInvoice { get; set; }
        public decimal VatPercent { get; set; }
        public decimal VatValue { get; set; }
        public string PublicUrl { get; set; } = "";
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
        public string InvoiceNumber { get; set; } = "";
        public DateOnly? EmissionDate { get; set; }
        public DateOnly? PaymentDate { get; set; }
        public decimal PaymentValue { get; set; }
        public decimal ReteFuenteValue { get; set; }
        public decimal ReteIcaValue { get; set; }
        public decimal TotalValue { get; set; }
        public decimal VatValue { get; set; }
        public string IssuerName { get; set; } = "";
        public string RecipientName { get; set; } = "";
        public string RecipientNit { get; set; } = "";
        public decimal CloudValue { get; set; }
        public decimal CopiersValue { get; set; }
    }

    private sealed record TaxExpenseFieldMap(
        string InvoiceNumberField,
        PnlExpenseDateFieldCandidate EmissionDateField,
        PnlExpenseDateFieldCandidate PaymentDateField,
        string PaymentValueField,
        string ReteFuenteField,
        string ReteIcaField,
        string TotalField,
        string VatField,
        string IssuerNameField,
        string RecipientNameField,
        string RecipientNitField,
        string CloudField,
        string CopiersField);

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
        public bool GroupIncludedOperations { get; set; } = true;
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
