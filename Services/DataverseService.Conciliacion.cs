using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ConciliacionCreatedOnField = "createdon";
    private const string ConciliacionModifiedOnField = "modifiedon";
    private const string ClientPaymentMatchPreflightStatusField = "cr07a_preflightestado";
    private const string ClientPaymentMatchPreflightMessageField = "cr07a_preflightmensaje";
    private const string ClientPaymentMatchPreflightValidatedOnField = "cr07a_preflightfecha";
    private const string ClientPaymentMatchPreflightDebitField = "cr07a_preflightdebito";
    private const string ClientPaymentMatchPreflightCreditField = "cr07a_preflightcredito";
    private const int ConciliacionSiigoIncomeJournalDocumentFallbackId = 31321;
    private const string ConciliacionSiigoIncomeJournalDocumentFallbackName = "Comprobante de ingreso";
    private static readonly CultureInfo ConciliacionCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly Regex ConciliacionInvoiceTokenRegex = new(
        @"\b(?:FV|FVE|FEV|FEM|FE|FEDT|FEKT)[-\s]*\d+(?:[-\s]*\d+)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<ConciliacionBoardDto> GetConciliacionBoardAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        if (year < 2020 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de conciliacion no es valido.");

        var start = new DateOnly(year, month, 1);
        var endExclusive = start.AddMonths(1);
        var cashFlowRowsTask = GetConciliacionCashFlowRowsAsync(start, endExclusive, ct);
        var clientPaymentsTask = GetConciliacionClientPaymentsAsync(start, endExclusive, ct);
        await Task.WhenAll(cashFlowRowsTask, clientPaymentsTask);

        var clientPayments = BuildConciliacionClientPaymentSummary(clientPaymentsTask.Result);
        var cashFlow = BuildConciliacionCashFlowSummary(cashFlowRowsTask.Result, clientPayments.Rows);
        var phases = BuildConciliacionPhases(cashFlow, clientPayments);
        var pending = clientPayments.PendingReview + cashFlow.PendingValidationRows;
        var suggested = clientPayments.Suggested;
        var approved = clientPayments.Approved;

        return new ConciliacionBoardDto
        {
            Year = year,
            Month = month,
            PeriodLabel = start.ToString("MMMM yyyy", ConciliacionCulture),
            StatusLabel = pending > 0 ? "Con pendientes" : suggested > 0 ? "Listo para aprobacion" : "En preparacion",
            StatusTone = pending > 0 ? "warning" : suggested > 0 ? "info" : "neutral",
            TotalPendingReview = pending,
            TotalSuggested = suggested,
            TotalApproved = approved,
            ClientPaymentEntries = clientPayments.TotalEntries,
            Phases = phases,
            CashFlow = cashFlow,
            ClientPayments = clientPayments
        };
    }

    public async Task<ConciliacionPreflightResultDto> ValidateConciliacionClientPaymentPreflightAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a validar.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var isTechnicallyReady = preflight.Issues.Count == 0;
        var isApprovedForSiigo = IsConciliacionApprovedForSiigo(row.Status);
        var isReadyForSiigo = isTechnicallyReady && isApprovedForSiigo;
        var preflightStatus = isReadyForSiigo
            ? "ListoSiigo"
            : isTechnicallyReady
                ? "ValidadoPendienteAprobacion"
                : "BloqueadoSiigo";
        var nextStatus = row.Status;
        if (isReadyForSiigo)
        {
            nextStatus = "ListoSiigo";
        }
        else if (!isTechnicallyReady && IsConciliacionSiigoCandidateStatus(row.Status))
        {
            nextStatus = "BloqueadoSiigo";
        }

        var message = isReadyForSiigo
            ? "Prevalidacion correcta. El cruce queda listo para enviar el comprobante de ingreso a Siigo."
            : isTechnicallyReady
                ? "Prevalidacion contable correcta. Falta aprobar el cruce antes de dejarlo listo para Siigo."
                : "Prevalidacion bloqueada: corrige los puntos indicados antes de enviar a Siigo.";
        var detailMessage = preflight.Issues.Count == 0
            ? message
            : $"{message} {string.Join(" ", preflight.Issues)}";

        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, nextStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, preflightStatus, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, preflight.DebitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, preflight.CreditTotal, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la prevalidacion.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        var updatedRow = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct);
        return new ConciliacionPreflightResultDto
        {
            Message = message,
            IsReadyForSiigo = isReadyForSiigo,
            Issues = preflight.Issues,
            Row = updatedRow
        };
    }

    public async Task<ConciliacionSiigoDryRunResultDto> SimulateConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a simular.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var issues = new List<string>(preflight.Issues);
        if (!string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(string.Equals(row.Status, "Aprobado", StringComparison.OrdinalIgnoreCase)
                ? "Haz clic en Validar pre-Siigo. Si la prevalidacion no encuentra errores, el cruce pasa a Listo Siigo."
                : "El cruce debe estar aprobado y luego validado pre-Siigo antes de habilitar el envio real.");
        }

        var payloadJson = "";
        var lineCount = 0;
        try
        {
            var payload = BuildConciliacionClientPaymentSiigoDryRunPayload(row, preflight, out lineCount);
            payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(ex.Message);
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido y no se puede simular.");
        }

        var ready = issues.Count == 0;
        return new ConciliacionSiigoDryRunResultDto
        {
            Message = ready
                ? "Simulacion correcta. El payload esta completo y aun no se envio nada a Siigo."
                : "Simulacion con pendientes. Corrige los puntos indicados antes del envio real.",
            IsReadyForSiigo = ready,
            TargetEndpoint = "DRY-RUN /v1/journals",
            PayloadJson = payloadJson,
            LineCount = lineCount,
            DebitTotal = preflight.DebitTotal,
            CreditTotal = preflight.CreditTotal,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionActionResultDto> UpdateConciliacionClientPaymentStatusAsync(
        ConciliacionClientPaymentStatusRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var status = NormalizeConciliacionClientPaymentStatus(request.Status);
        var reason = (request.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = status switch
            {
                "Aprobado" => "Aprobado desde modulo Conciliacion.",
                "Rechazado" => "Rechazado desde modulo Conciliacion.",
                "RevisionManual" => "Marcado para revision manual desde modulo Conciliacion.",
                _ => "Estado actualizado desde modulo Conciliacion."
            };
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, reason, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para actualizar el cruce.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);

        var row = await GetConciliacionClientPaymentByIdAsync(metadata, recordId, ct);
        return new ConciliacionActionResultDto
        {
            Message = $"Cruce marcado como {ResolveConciliacionStatusLabel(status)}.",
            Row = row
        };
    }

    public async Task<ConciliacionInvoiceSearchResultDto> SearchConciliacionDataverseInvoicesAsync(
        ConciliacionInvoiceSearchRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var query = (request.Query ?? "").Trim();
        var top = Math.Clamp(request.Top <= 0 ? 20 : request.Top, 1, 50);
        var value = request.Value is > 0m ? RoundCurrency(request.Value.Value) : (decimal?)null;
        if (string.IsNullOrWhiteSpace(query) && value is null)
            throw new InvalidOperationException("Busca por cliente, numero de factura o valor de factura.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var rows = await GetCashFlowClientPaymentBillingRowsAsync(metadata, ct);
        var queryKey = NormalizeConciliacionLookupKey(query);
        var queryText = NormalizeConciliacionLookupText(query);
        var queryDigits = NormalizeConciliacionDigits(query);

        var items = rows
            .Select(row => new
            {
                Row = row,
                Score = ScoreConciliacionInvoiceLookup(row, queryKey, queryText, queryDigits, value)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenByDescending(static item => item.Row.EmissionDate)
            .ThenBy(static item => item.Row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .Select(item => BuildConciliacionInvoiceLookupDto(item.Row, value))
            .ToArray();

        return new ConciliacionInvoiceSearchResultDto
        {
            Message = items.Length == 0
                ? "No encontramos facturas con esos criterios."
                : $"Encontramos {items.Length:N0} factura{(items.Length == 1 ? "" : "s")} en Dataverse.",
            Items = items
        };
    }

    public async Task<ConciliacionActionResultDto> AssignConciliacionClientPaymentInvoiceAsync(
        ConciliacionAssignInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var invoiceRecordId = NormalizeGuid(request.InvoiceRecordId, nameof(request.InvoiceRecordId));
        var matchMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var matchAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(matchMetadata.LogicalName, ct);
        matchAttributes = BuildCashFlowClientPaymentMatchAttributeSet(matchMetadata, matchAttributes);

        var current = await GetConciliacionClientPaymentByIdAsync(matchMetadata, recordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a editar.");

        var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var invoice = await GetConciliacionBillingRecordByIdAppAsync(billingMetadata, invoiceRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos la factura seleccionada en Dataverse.");

        var matchRow = BuildConciliacionManualClientPaymentMatchRow(current, invoice);
        var payload = BuildCashFlowClientPaymentMatchPayload(matchMetadata, matchAttributes, matchRow);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightStatusField, null, "", force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightMessageField, null, "Factura reasignada. Falta validar pre-Siigo.", force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, null, force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, 0m, force: true);
        SetAccountCatalogValue(payload, matchAttributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, 0m, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para asignar la factura.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{matchMetadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);

        var updated = await GetConciliacionClientPaymentByIdAsync(matchMetadata, recordId, ct);
        return new ConciliacionActionResultDto
        {
            Message = $"Factura {invoice.InvoiceNumber} asignada al cruce. Revisa y aprueba la sugerencia para pasar a prevalidacion.",
            Row = updated
        };
    }

    public async Task<ConciliacionSiigoSendPreparedDto> PrepareConciliacionClientPaymentSiigoSendAsync(
        string recordId,
        CancellationToken ct = default,
        IReadOnlyList<SiigoTaxLookupDto>? siigoTaxes = null,
        SiigoDocumentTypeLookupDto? journalDocument = null,
        IReadOnlyList<SiigoInvoiceRowDto>? siigoInvoices = null)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el cruce de pago a enviar.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        var issues = new List<string>(preflight.Issues);
        if (!IsConciliacionReadyForRealSendStatus(row.Status))
            issues.Add("El cruce debe estar en estado Listo Siigo o Error Siigo antes de habilitar el envio real.");
        if (journalDocument is null || journalDocument.Id <= 0)
            issues.Add("No se encontro en Siigo el tipo de comprobante Comprobante de ingreso.");
        else if (!journalDocument.Active)
            issues.Add($"El tipo de comprobante Siigo {journalDocument.Name} ({journalDocument.Id}) no esta activo.");

        var payloadJson = "";
        var customerIdentification = "";
        var invoiceNumbers = Array.Empty<string>();
        object? payload = null;
        try
        {
            var invoiceRecordIds = ExtractConciliacionInvoiceRecordIds(row);
            if (invoiceRecordIds.Count == 0)
            {
                issues.Add("No hay identificador de factura Dataverse asociado al cruce.");
            }

            var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
                _dashboardBillingTableLogicalName,
                _dashboardBillingTableSetName,
                _dashboardBillingIdField,
                _dashboardBillingPrimaryNameField,
                ct);
            var invoices = invoiceRecordIds.Count == 0
                ? Array.Empty<BillingRecordRow>()
                : await GetConciliacionBillingRecordsByIdsAppAsync(billingMetadata, invoiceRecordIds, ct);
            if (invoiceRecordIds.Count > 0 && invoices.Count != invoiceRecordIds.Count)
                issues.Add("Una o mas facturas asociadas ya no se encontraron en Dataverse.");
            if (invoices.Count == 0)
                issues.Add("No hay facturas Dataverse disponibles para armar el comprobante de ingreso.");

            var customerIdentifications = invoices
                .Select(static invoice => NormalizeConciliacionIdentificationDigits(invoice.CompanyTaxId))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            customerIdentification = customerIdentifications.Length == 1 ? customerIdentifications[0] : "";
            invoiceNumbers = invoices
                .Select(static invoice => FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber).Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (customerIdentifications.Length == 0)
                issues.Add("La factura Dataverse no tiene NIT del cliente para buscarlo en Siigo.");
            else if (customerIdentifications.Length > 1)
                issues.Add("Las facturas asociadas tienen NIT de cliente diferentes; envia un comprobante por cliente.");

            var siigoInvoiceLookup = BuildConciliacionSiigoInvoiceLookup(siigoInvoices ?? Array.Empty<SiigoInvoiceRowDto>());
            var requireLiveSiigoInvoice = siigoInvoices is not null;
            var invoiceValues = invoices
                .Select(invoice => new
                {
                    Invoice = invoice,
                    Value = ResolveConciliacionSiigoInvoiceAccountingValue(
                        invoice,
                        siigoInvoiceLookup,
                        requireLiveSiigoInvoice,
                        issues)
                })
                .ToArray();
            var invoiceTotal = RoundCurrency(invoiceValues.Sum(static item => item.Value));
            var dataverseInvoiceTotal = RoundCurrency(invoices.Sum(static invoice => invoice.TotalInvoice));
            var actualRetentions = RoundCurrency(invoices.Sum(ResolveConciliacionInvoiceRetentionsTotal));
            if (invoices.Count > 0 && Math.Abs(dataverseInvoiceTotal - row.InvoiceTotal) > 1m)
                issues.Add($"El total de facturas Dataverse ({dataverseInvoiceTotal:N2}) no coincide con el total del cruce ({row.InvoiceTotal:N2}).");
            if (invoices.Count > 0 && Math.Abs(actualRetentions - row.RetentionsTotal) > 1m)
                issues.Add($"Las retenciones calculadas desde la factura ({actualRetentions:N2}) no coinciden con las del cruce ({row.RetentionsTotal:N2}).");
            var siigoAdjustment = RoundCurrency(invoiceTotal - row.EntryValue - actualRetentions);
            var accountingDifference = RoundCurrency(siigoAdjustment - row.DifferenceValue);
            if (invoices.Count > 0 && Math.Abs(accountingDifference) > 1m)
                issues.Add($"El ajuste requerido contra Siigo ({siigoAdjustment:N2}) no coincide con el ajuste del cruce ({row.DifferenceValue:N2}). Diferencia residual {accountingDifference:N2}.");

            var movementDate = row.MovementDateValue.Trim();
            if (!DateOnly.TryParseExact(movementDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                issues.Add("La fecha del movimiento no tiene formato valido para Siigo.");

            var invoiceDues = new List<ConciliacionSiigoInvoiceDueItem>();
            foreach (var invoice in invoices)
            {
                if (!TryBuildConciliacionSiigoDue(invoice, out var due, out var dueIssue))
                {
                    issues.Add(dueIssue);
                    continue;
                }

                var retentionTaxes = ResolveConciliacionInvoiceRetentionTaxes(invoice, siigoTaxes ?? Array.Empty<SiigoTaxLookupDto>(), issues);
                var invoiceValue = invoiceValues
                    .FirstOrDefault(value => string.Equals(value.Invoice.RecordId, invoice.RecordId, StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? invoice.TotalInvoice;
                invoiceDues.Add(new ConciliacionSiigoInvoiceDueItem(invoice, due, RoundCurrency(invoiceValue), retentionTaxes));
            }

            if (issues.Count == 0)
            {
                payload = BuildConciliacionClientPaymentSiigoSendPayload(
                    row,
                    invoiceDues,
                    movementDate,
                    customerIdentifications[0],
                    journalDocument!,
                    siigoAdjustment);
                payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            }
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido y no se pudo preparar el envio real.");
        }

        var canSend = issues.Count == 0 && payload is not null;
        return new ConciliacionSiigoSendPreparedDto
        {
            Message = canSend
                ? "Listo para envio real a Siigo."
                : "Envio real bloqueado. Corrige los pendientes visibles antes de enviar.",
            CanSend = canSend,
            TargetEndpoint = "/v1/journals",
            CustomerIdentification = customerIdentification,
            InvoiceNumbers = invoiceNumbers,
            Payload = payload,
            PayloadJson = payloadJson,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionActionResultDto> MarkConciliacionClientPaymentSiigoSendResultAsync(
        string recordId,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var status = success ? "EnviadoSiigo" : "ErrorSiigo";
        var detailParts = new[]
            {
                message,
                string.IsNullOrWhiteSpace(siigoName) ? "" : $"Documento Siigo: {siigoName}.",
                string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id Siigo: {siigoId}.",
                success || string.IsNullOrWhiteSpace(responseJson) ? "" : $"Detalle: {responseJson}"
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        var detailMessage = TruncateAccountCatalogText(string.Join(" ", detailParts), 1000);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, detailMessage, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para marcar el resultado del envio a Siigo.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        var row = await GetConciliacionClientPaymentByIdAsync(metadata, normalizedRecordId, ct);
        if (success && row is not null)
            await MarkConciliacionCashFlowMovementSiigoResultAsync(row, siigoId, siigoName, detailMessage, ct);

        return new ConciliacionActionResultDto
        {
            Message = detailMessage,
            Row = row
        };
    }

    private async Task MarkConciliacionCashFlowMovementSiigoResultAsync(
        ConciliacionClientPaymentRowDto row,
        string siigoId,
        string siigoName,
        string detailMessage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.MovementExternalKey)
            && string.IsNullOrWhiteSpace(row.MovementId))
        {
            return;
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);

        var movementId = Guid.TryParse(row.MovementId, out var parsedMovementId)
            ? parsedMovementId.ToString("D")
            : await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, row.MovementExternalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            return;

        var siigoReference = FirstNonEmpty(siigoName, siigoId, "Comprobante enviado a Siigo");
        var message = TruncateAccountCatalogText(
            $"Pago cliente enviado a Siigo: {siigoReference}. {detailMessage}".Trim(),
            1000);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "EnviadoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, "EnviadoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, siigoId, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, message, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);
    }

    private async Task<string> FindConciliacionCashFlowMovementIdByExternalKeyAsync(
        RhEntityMetadata metadata,
        string externalKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalKey))
            return "";

        var filter = $"{CashFlowExternalKeyField} eq '{EscapeOdataLiteral(externalKey.Trim())}'";
        var select = Uri.EscapeDataString(metadata.PrimaryIdField);
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        return rows
            .Select(row => ReadString(row, metadata.PrimaryIdField).Trim())
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private async Task<IReadOnlyList<ConciliacionClientPaymentRowDto>> GetConciliacionClientPaymentsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var filter = BuildBillingDateFilter(ClientPaymentMatchMovementDateField, "date-only", startInclusive, endExclusive);
        var orderBy = Uri.EscapeDataString($"{ClientPaymentMatchMovementDateField} desc");
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        var parsedRows = rows
            .Select(item => ParseConciliacionClientPaymentRow(item, metadata))
            .Where(static row => row is not null)
            .Cast<ConciliacionClientPaymentRowDto>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ClientNames, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await AutoValidateConciliacionClientPaymentRowsAsync(metadata, attributes, parsedRows, ct);
        return parsedRows;
    }

    private async Task<ConciliacionClientPaymentRowDto?> GetConciliacionClientPaymentByIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        CancellationToken ct)
    {
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildConciliacionClientPaymentAttributeSet(metadata, attributes);
        var select = BuildConciliacionClientPaymentSelect(metadata, attributes);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseConciliacionClientPaymentRow(doc.RootElement, metadata);
    }

    private async Task AutoValidateConciliacionClientPaymentRowsAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows,
        CancellationToken ct)
    {
        var candidates = rows
            .Where(IsAutoReadyConciliacionClientPaymentCandidate)
            .ToArray();
        if (candidates.Length == 0)
            return;

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        using var throttler = new SemaphoreSlim(4);
        var tasks = candidates.Select(async row =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                await TryAutoValidateConciliacionClientPaymentRowAsync(metadata, attributes, row, catalog, ct);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task TryAutoValidateConciliacionClientPaymentRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        ConciliacionClientPaymentRowDto row,
        IReadOnlyDictionary<string, ConciliacionAccountCatalogItem> catalog,
        CancellationToken ct)
    {
        var preflight = ValidateConciliacionClientPaymentDraft(row, catalog);
        if (preflight.Issues.Count > 0)
            return;

        var message = BuildConciliacionAutoReadyMessage(row);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchReasonField, null, message, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightStatusField, null, "ListoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightMessageField, null, message, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightValidatedOnField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightDebitField, (decimal?)null, preflight.DebitTotal, force: true);
        SetAccountCatalogValue(payload, attributes, ClientPaymentMatchPreflightCreditField, (decimal?)null, preflight.CreditTotal, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({row.RecordId})",
            "PATCH",
            payload,
            ct);

        row.Status = "ListoSiigo";
        row.StatusLabel = ResolveConciliacionStatusLabel(row.Status);
        row.StatusTone = ResolveConciliacionStatusTone(row.Status);
        row.Reason = message;
        row.PreflightStatus = "ListoSiigo";
        row.PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(row.PreflightStatus);
        row.PreflightStatusTone = ResolveConciliacionPreflightStatusTone(row.PreflightStatus);
        row.PreflightMessage = message;
        row.PreflightDebitTotal = preflight.DebitTotal;
        row.PreflightCreditTotal = preflight.CreditTotal;
        row.PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(DateTimeOffset.UtcNow);
    }

    private static bool IsAutoReadyConciliacionClientPaymentCandidate(ConciliacionClientPaymentRowDto row)
    {
        if (!string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(row.PreflightStatus, "ListoSiigo", StringComparison.OrdinalIgnoreCase))
            return false;
        if (row.Confidence < 90)
            return false;
        if (row.EntryValue <= 0m || row.InvoiceTotal <= 0m)
            return false;
        if (Math.Abs(row.DifferenceValue) > 5m)
            return false;
        if (string.IsNullOrWhiteSpace(row.RecordId)
            || string.IsNullOrWhiteSpace(row.InvoiceRecordIds)
            || string.IsNullOrWhiteSpace(row.InvoiceNumbers)
            || string.IsNullOrWhiteSpace(row.ClientNames)
            || string.IsNullOrWhiteSpace(row.BankAccountCode)
            || string.IsNullOrWhiteSpace(row.DraftJson))
        {
            return false;
        }

        return true;
    }

    private static string BuildConciliacionAutoReadyMessage(ConciliacionClientPaymentRowDto row)
    {
        var adjustment = Math.Abs(row.DifferenceValue) > 0m
            ? $" Ajuste al peso: {row.DifferenceValue:N2}."
            : "";
        return TruncateAccountCatalogText(
            $"Auto-validado: factura, cliente, banco, retenciones y comprobante contable completos.{adjustment}",
            1000);
    }

    private async Task<BillingRecordRow?> GetConciliacionBillingRecordByIdAppAsync(
        RhEntityMetadata metadata,
        string recordId,
        CancellationToken ct)
    {
        var select = BuildBillingSelectClause(metadata);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseBillingRecord(doc.RootElement, metadata.PrimaryIdField, metadata.PrimaryNameField);
    }

    private async Task<IReadOnlyList<BillingRecordRow>> GetConciliacionBillingRecordsByIdsAppAsync(
        RhEntityMetadata metadata,
        IReadOnlyList<string> recordIds,
        CancellationToken ct)
    {
        var rows = new List<BillingRecordRow>();
        foreach (var recordId in recordIds)
        {
            var row = await GetConciliacionBillingRecordByIdAppAsync(metadata, recordId, ct);
            if (row is not null)
                rows.Add(row);
        }

        return rows
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private CashFlowClientPaymentMatchRowDto BuildConciliacionManualClientPaymentMatchRow(
        ConciliacionClientPaymentRowDto current,
        BillingRecordRow invoice)
    {
        var tokens = ExtractCashFlowClientPaymentInvoiceTokens(current.Description).ToList();
        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            tokens.Add(NormalizeDocumentToken(invoice.InvoiceNumber));

        var reteFteValue = ResolveCashFlowClientPaymentReteFteValue(invoice);
        var reteIcaValue = ResolveCashFlowClientPaymentReteIcaValue(invoice);
        var rteIvaValue = ResolveCashFlowClientPaymentRteIvaValue(invoice);
        var retentions = RoundCurrency(reteFteValue + reteIcaValue + rteIvaValue);
        var difference = RoundCurrency(invoice.TotalInvoice - current.EntryValue - retentions);
        var inTolerance = Math.Abs(difference) <= RegistroPagosClientesBalancedTolerance;
        var movementDate = DateOnly.TryParseExact(
            current.MovementDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedMovementDate)
            ? parsedMovementDate
            : (DateOnly?)null;

        var row = new CashFlowClientPaymentMatchRowDto
        {
            MovementId = current.MovementId,
            MovementExternalKey = current.MovementExternalKey,
            MovementDate = movementDate,
            SourceFlow = current.SourceFlow,
            BankAccountCode = current.BankAccountCode,
            BankAccountName = current.BankAccountName,
            Description = current.Description,
            EntryValue = current.EntryValue,
            InvoiceTokens = tokens,
            InvoiceTotal = invoice.TotalInvoice,
            ReteFteValue = reteFteValue,
            ReteIcaValue = reteIcaValue,
            RteIvaValue = rteIvaValue,
            RetentionsTotal = retentions,
            DifferenceValue = difference,
            Confidence = inTolerance ? 90 : 70,
            Status = inTolerance ? "Sugerido" : "DiferenciaFueraRango",
            Reason = inTolerance
                ? "Factura asignada manualmente y diferencia dentro del rango."
                : $"Factura asignada manualmente, pero la diferencia supera {RegistroPagosClientesBalancedTolerance:N0}."
        };

        return FinalizeCashFlowClientPaymentMatchRow(row, new[] { invoice });
    }

    private static int ScoreConciliacionInvoiceLookup(
        BillingRecordRow row,
        string queryKey,
        string queryText,
        string queryDigits,
        decimal? value)
    {
        var score = 0;
        var invoiceKey = NormalizeConciliacionLookupKey(row.InvoiceNumber);
        var clientKey = NormalizeConciliacionLookupKey(row.ClientName);
        var taxIdDigits = NormalizeConciliacionDigits(row.CompanyTaxId);
        var clientText = NormalizeConciliacionLookupText(row.ClientName);

        if (!string.IsNullOrWhiteSpace(queryKey))
        {
            if (string.Equals(invoiceKey, queryKey, StringComparison.OrdinalIgnoreCase))
                score += 120;
            else if (invoiceKey.Contains(queryKey, StringComparison.OrdinalIgnoreCase) || queryKey.Contains(invoiceKey, StringComparison.OrdinalIgnoreCase))
                score += 85;

            if (clientKey.Contains(queryKey, StringComparison.OrdinalIgnoreCase))
                score += 45;
        }

        if (!string.IsNullOrWhiteSpace(queryText) && clientText.Contains(queryText, StringComparison.OrdinalIgnoreCase))
            score += 35;

        if (!string.IsNullOrWhiteSpace(queryDigits))
        {
            if (NormalizeConciliacionDigits(row.InvoiceNumber).Contains(queryDigits, StringComparison.OrdinalIgnoreCase))
                score += 55;
            if (!string.IsNullOrWhiteSpace(taxIdDigits) && taxIdDigits.Contains(queryDigits, StringComparison.OrdinalIgnoreCase))
                score += 20;
        }

        if (value.HasValue)
        {
            var difference = Math.Abs(row.TotalInvoice - value.Value);
            if (difference <= 1m)
                score += 80;
            else if (difference <= RegistroPagosClientesBalancedTolerance)
                score += 55;
            else if (difference <= 50000m)
                score += 25;
        }

        return score;
    }

    private static ConciliacionInvoiceLookupDto BuildConciliacionInvoiceLookupDto(
        BillingRecordRow row,
        decimal? searchedValue) =>
        new()
        {
            RecordId = row.RecordId,
            InvoiceNumber = row.InvoiceNumber,
            ClientName = row.ClientName,
            EmissionDateDisplay = row.EmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            TotalInvoice = row.TotalInvoice,
            PaymentValue = row.PaymentValue,
            ReteFteValue = ResolveCashFlowClientPaymentReteFteValue(row),
            ReteIcaValue = ResolveCashFlowClientPaymentReteIcaValue(row),
            RteIvaValue = ResolveCashFlowClientPaymentRteIvaValue(row),
            DifferenceWithEntry = searchedValue.HasValue
                ? RoundCurrency(row.TotalInvoice - searchedValue.Value)
                : 0m
        };

    private static string NormalizeConciliacionLookupText(string? value) =>
        Regex.Replace((value ?? "").Trim().ToUpperInvariant(), @"\s+", " ", RegexOptions.CultureInvariant);

    private static string NormalizeConciliacionLookupKey(string? value) =>
        Regex.Replace((value ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "", RegexOptions.CultureInvariant);

    private static string NormalizeConciliacionDigits(string? value) =>
        Regex.Replace(value ?? "", @"\D", "", RegexOptions.CultureInvariant);

    private async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var movementMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var movementAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(movementMetadata.LogicalName, ct);
        var movementSelect = BuildConciliacionSelectClause(movementMetadata, movementAttributes, new[]
        {
            movementMetadata.PrimaryIdField,
            movementMetadata.PrimaryNameField,
            CashFlowDateField,
            CashFlowBankField,
            CashFlowDescriptionField,
            CashFlowEntryField,
            CashFlowExitField,
            CashFlowSourceFlowField,
            CashFlowBankAccountCodeField,
            CashFlowBankAccountNameField,
            CashFlowRecipientField,
            CashFlowDestinationBankField,
            CashFlowDocumentTypeField,
            CashFlowObservationsField,
            CashFlowMovementTypeField,
            CashFlowStatusField,
            CashFlowSiigoDocumentIdField,
            CashFlowSiigoStatusField,
            CashFlowExternalKeyField,
            CashFlowReviewReasonField,
            ConciliacionModifiedOnField
        });
        var movementFilter = BuildBillingDateFilter(CashFlowDateField, "date-only", startInclusive, endExclusive);
        var movementUrl = $"/api/data/v9.2/{movementMetadata.EntitySetName}?$select={movementSelect}&$filter={Uri.EscapeDataString(movementFilter)}&$orderby={CashFlowDateField} desc";
        var movementRows = await GetDataverseAppEntitiesAsync(movementUrl, ct);

        var transferMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var transferAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(transferMetadata.LogicalName, ct);
        var transferSelect = BuildConciliacionSelectClause(transferMetadata, transferAttributes, new[]
        {
            transferMetadata.PrimaryIdField,
            transferMetadata.PrimaryNameField,
            CashFlowTransferDateField,
            CashFlowTransferValueField,
            CashFlowTransferSourceFlowField,
            CashFlowTransferFromField,
            CashFlowTransferToField,
            CashFlowTransferEntryField,
            CashFlowTransferExitField,
            CashFlowTransferDescriptionField,
            CashFlowTransferRecipientField,
            CashFlowTransferDestinationBankField,
            CashFlowTransferDocumentTypeField,
            CashFlowTransferObservationsField,
            CashFlowTransferStatusField,
            CashFlowTransferExternalKeyField,
            ConciliacionModifiedOnField
        });
        var transferFilter = BuildBillingDateFilter(CashFlowTransferDateField, "date-only", startInclusive, endExclusive);
        var transferUrl = $"/api/data/v9.2/{transferMetadata.EntitySetName}?$select={transferSelect}&$filter={Uri.EscapeDataString(transferFilter)}&$orderby={CashFlowTransferDateField} desc";
        var transferRows = await GetDataverseAppEntitiesAsync(transferUrl, ct);

        return movementRows
            .Select(item => ParseConciliacionCashFlowMovementRow(item, movementMetadata))
            .Concat(transferRows.Select(item => ParseConciliacionCashFlowTransferRow(item, transferMetadata)))
            .Where(static row => row is not null && !IsConciliacionPocketTransfer(row))
            .Cast<ConciliacionCashFlowRowDto>()
            .OrderByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceFlow, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConciliacionCashFlowSummaryDto BuildConciliacionCashFlowSummary(
        IReadOnlyList<ConciliacionCashFlowRowDto> rows,
        IReadOnlyList<ConciliacionClientPaymentRowDto> clientPayments)
    {
        var matchByExternalKey = clientPayments
            .Where(static row => !string.IsNullOrWhiteSpace(row.MovementExternalKey))
            .GroupBy(static row => row.MovementExternalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.ExternalKey)
                && matchByExternalKey.TryGetValue(row.ExternalKey.Trim(), out var match))
            {
                ApplyConciliacionClientPaymentMatch(row, match);
            }
        }

        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnValue))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionCashFlowSummaryDto
        {
            TotalRows = rows.Count,
            MovementRows = rows.Count(static row => string.Equals(row.SourceKind, "Movimiento", StringComparison.OrdinalIgnoreCase)),
            TransferRows = rows.Count(static row => string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase)),
            EntryRows = rows.Count(static row => string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase)),
            ExitRows = rows.Count(static row => string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase)),
            OutgoingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)),
            IncomingInvoiceRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "entrada-fe", StringComparison.OrdinalIgnoreCase)),
            CollectionAccountRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "cuenta-cobro", StringComparison.OrdinalIgnoreCase)),
            AccountingVoucherRows = rows.Count(static row =>
                string.Equals(row.DetectedTypeKey, "comprobante-contable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.DetectedTypeKey, "entrada-comprobante", StringComparison.OrdinalIgnoreCase)),
            OrphanRows = rows.Count(static row => string.Equals(row.DetectedTypeKey, "huerfano", StringComparison.OrdinalIgnoreCase)),
            PendingValidationRows = rows.Count(static row => string.Equals(row.ValidationStatus, "Pendiente validar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.ValidationStatus, "Revisar", StringComparison.OrdinalIgnoreCase)),
            PendingSiigoRows = rows.Count(static row => row.RegistrationStatus.Contains("Siigo pendiente", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            TotalExits = RoundCurrency(rows.Sum(static row => row.ExitValue)),
            TotalTransfers = RoundCurrency(rows.Where(static row => string.Equals(row.Direction, "Traslado", StringComparison.OrdinalIgnoreCase)).Sum(static row => row.Amount)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static string BuildConciliacionSelectClause(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IEnumerable<string> fields)
    {
        var selected = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Where(field => attributes.Count == 0
                || attributes.Contains(field)
                || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length > 0 ? string.Join(",", selected) : metadata.PrimaryIdField;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowMovementRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowMovementIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowExitField) ?? 0m);
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Movimiento",
            SourceKindLabel = "Movimiento",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, CashFlowSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, CashFlowBankAccountCodeField).Trim(),
            BankAccountName = FirstNonEmpty(
                ReadString(item, CashFlowBankAccountNameField),
                ReadString(item, CashFlowBankField)).Trim(),
            EntryValue = entry,
            ExitValue = exit,
            Amount = RoundCurrency(Math.Max(entry, exit)),
            Description = ReadString(item, CashFlowDescriptionField).Trim(),
            Recipient = ReadString(item, CashFlowRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowObservationsField).Trim(),
            ExcelMovementType = ReadString(item, CashFlowMovementTypeField).Trim(),
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowStatusField), "Importado").Trim(),
            SiigoStatus = ReadString(item, CashFlowSiigoStatusField).Trim(),
            ExternalKey = ReadString(item, CashFlowExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        row.Direction = entry > 0m ? "Entrada" : exit > 0m ? "Salida" : "Sin valor";
        row.DirectionTone = entry > 0m ? "success" : exit > 0m ? "danger" : "neutral";
        CompleteConciliacionCashFlowRow(
            row,
            ReadString(item, CashFlowSiigoDocumentIdField).Trim(),
            row.SiigoStatus);
        return row;
    }

    private static ConciliacionCashFlowRowDto? ParseConciliacionCashFlowTransferRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, CashFlowTransferIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, CashFlowTransferDateField);
        var entry = RoundCurrency(ReadDecimal(item, CashFlowTransferEntryField) ?? 0m);
        var exit = RoundCurrency(ReadDecimal(item, CashFlowTransferExitField) ?? 0m);
        var value = RoundCurrency(ReadDecimal(item, CashFlowTransferValueField) ?? Math.Max(entry, exit));
        var transferFrom = ReadString(item, CashFlowTransferFromField).Trim();
        var transferTo = ReadString(item, CashFlowTransferToField).Trim();
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = recordId,
            SourceKind = "Traslado",
            SourceKindLabel = "Traslado interno",
            MovementDateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, CashFlowTransferSourceFlowField).Trim(),
            BankAccountName = string.Join(" => ", new[] { transferFrom, transferTo }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            Direction = "Traslado",
            DirectionTone = "neutral",
            EntryValue = entry,
            ExitValue = exit,
            Amount = value,
            Description = ReadString(item, CashFlowTransferDescriptionField).Trim(),
            Recipient = ReadString(item, CashFlowTransferRecipientField).Trim(),
            DestinationBank = ReadString(item, CashFlowTransferDestinationBankField).Trim(),
            DocumentType = ReadString(item, CashFlowTransferDocumentTypeField).Trim(),
            Observations = ReadString(item, CashFlowTransferObservationsField).Trim(),
            ExcelMovementType = "TRASLADO",
            DataverseStatus = FirstNonEmpty(ReadString(item, CashFlowTransferStatusField), "InternoNoSiigo").Trim(),
            ExternalKey = ReadString(item, CashFlowTransferExternalKeyField).Trim(),
            ModifiedOnValue = ReadString(item, ConciliacionModifiedOnField).Trim()
        };

        CompleteConciliacionCashFlowRow(row, "", "");
        return row;
    }

    private static void CompleteConciliacionCashFlowRow(
        ConciliacionCashFlowRowDto row,
        string siigoDocumentId,
        string siigoStatus)
    {
        var detection = ResolveConciliacionCashFlowDetectedType(row);
        row.DetectedTypeKey = detection.Key;
        row.DetectedTypeLabel = detection.Label;
        row.DetectedTypeTone = detection.Tone;
        row.ActionTargetKey = detection.TargetKey;
        row.CanValidate = !string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase);

        if (IsConciliacionCashFlowPostSendChange(row.DataverseStatus, siigoStatus))
        {
            ApplyConciliacionCashFlowPostSendChange(row);
            return;
        }

        if (string.Equals(detection.Key, "traslado-interno", StringComparison.OrdinalIgnoreCase))
        {
            row.ValidationStatus = "Interno";
            row.ValidationTone = "neutral";
            row.RegistrationStatus = "Dataverse OK / no aplica Siigo";
            row.RegistrationTone = "neutral";
            row.InvoiceStatus = "No aplica";
            row.InvoiceStatusTone = "neutral";
            row.SiigoDocumentStatus = "No aplica";
            row.SiigoDocumentTone = "neutral";
            row.SiigoPaymentStatus = "No aplica";
            row.SiigoPaymentTone = "neutral";
            row.InvoiceBalanceStatus = "No aplica";
            row.DataversePaymentStatus = "No aplica";
            row.DataversePaymentTone = "neutral";
            return;
        }

        var siigoRegistered = IsConciliacionSiigoRegistered(siigoDocumentId, siigoStatus);
        row.ValidationStatus = "Pendiente validar";
        row.ValidationTone = "warning";
        row.RegistrationStatus = siigoRegistered
            ? "Dataverse OK / Siigo OK"
            : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = siigoRegistered ? "success" : "warning";
        row.InvoiceStatus = ResolveDefaultInvoiceStatus(row.DetectedTypeKey);
        row.InvoiceStatusTone = row.InvoiceStatus.Contains("OK", StringComparison.OrdinalIgnoreCase) ? "success" : "warning";
        row.SiigoDocumentStatus = siigoRegistered ? "Siigo OK" : "Pendiente Siigo";
        row.SiigoDocumentTone = siigoRegistered ? "success" : "warning";
        row.SiigoPaymentStatus = siigoRegistered ? "Pago/registro Siigo detectado" : "Pendiente envio Siigo";
        row.SiigoPaymentTone = siigoRegistered ? "success" : "warning";
        row.InvoiceBalanceStatus = string.Equals(row.DetectedTypeKey, "salida-fe", StringComparison.OrdinalIgnoreCase)
            ? "Saldo sin calcular"
            : "No aplica";
        row.DataversePaymentStatus = "Flujo Dataverse OK";
        row.DataversePaymentTone = "success";
    }

    private static void ApplyConciliacionClientPaymentMatch(
        ConciliacionCashFlowRowDto row,
        ConciliacionClientPaymentRowDto match)
    {
        row.MatchRecordId = match.RecordId;
        row.MatchStatus = match.Status;
        row.ActionTargetKey = "entradas-fe";
        row.CanValidate = true;

        if (IsConciliacionCashFlowPostSendChange(row.DataverseStatus, row.SiigoStatus))
        {
            ApplyConciliacionCashFlowPostSendChange(row);
            return;
        }

        if (!string.IsNullOrWhiteSpace(match.InvoiceNumbers)
            || string.Equals(row.DetectedTypeKey, "huerfano", StringComparison.OrdinalIgnoreCase))
        {
            row.DetectedTypeKey = "entrada-fe";
            row.DetectedTypeLabel = "Entrada FE - pago cliente";
            row.DetectedTypeTone = "success";
        }

        row.ValidationStatus = match.Status switch
        {
            "Aprobado" or "ListoSiigo" or "EnviadoSiigo" or "Conciliado" => "Validada",
            "Sugerido" => "Pendiente validar",
            "Rechazado" => "Rechazada",
            _ => "Revisar"
        };
        row.ValidationTone = match.Status switch
        {
            "Aprobado" or "ListoSiigo" or "EnviadoSiigo" or "Conciliado" => "success",
            "Sugerido" => "info",
            "Rechazado" => "danger",
            _ => "warning"
        };
        row.InvoiceStatus = string.IsNullOrWhiteSpace(match.InvoiceNumbers)
            ? "Factura no encontrada"
            : "Factura Dataverse OK";
        row.InvoiceStatusTone = string.IsNullOrWhiteSpace(match.InvoiceNumbers) ? "danger" : "success";
        row.DataversePaymentStatus = match.RetentionsTotal > 0m
            ? $"Pago Dataverse OK con retenciones {match.RetentionsTotal:N0}"
            : "Pago Dataverse OK sin retenciones";
        row.DataversePaymentTone = "success";
        var sentToSiigo = string.Equals(match.Status, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(match.Status, "Conciliado", StringComparison.OrdinalIgnoreCase);
        var readyForSiigo = string.Equals(match.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase);
        row.SiigoPaymentStatus = sentToSiigo
            ? "Enviado Siigo"
            : readyForSiigo
                ? "Listo para envio Siigo"
                : "Pendiente envio Siigo";
        row.SiigoPaymentTone = sentToSiigo ? "success" : readyForSiigo ? "info" : "warning";
        row.RegistrationStatus = sentToSiigo
            ? "Dataverse OK / Siigo OK"
            : readyForSiigo
                ? "Dataverse OK / listo Siigo"
                : "Dataverse OK / Siigo pendiente";
        row.RegistrationTone = sentToSiigo ? "success" : readyForSiigo ? "info" : "warning";
    }

    private static void ApplyConciliacionCashFlowPostSendChange(ConciliacionCashFlowRowDto row)
    {
        row.ValidationStatus = "Cambio posterior";
        row.ValidationTone = "danger";
        row.RegistrationStatus = "Cambio en Excel despues de Siigo";
        row.RegistrationTone = "danger";
        row.InvoiceStatus = "Revisar cambio";
        row.InvoiceStatusTone = "danger";
        row.SiigoDocumentStatus = "Siigo ya tenia registro";
        row.SiigoDocumentTone = "warning";
        row.SiigoPaymentStatus = "Bloqueado por cambio";
        row.SiigoPaymentTone = "danger";
        row.InvoiceBalanceStatus = "Revisar manual";
        row.DataversePaymentStatus = "No sobreescrito";
        row.DataversePaymentTone = "warning";
    }

    private static bool IsConciliacionCashFlowPostSendChange(string status, string siigoStatus)
    {
        return string.Equals(status, "CambioPostEnvio", StringComparison.OrdinalIgnoreCase)
            || string.Equals(siigoStatus, "CambioPostEnvio", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Key, string Label, string Tone, string TargetKey) ResolveConciliacionCashFlowDetectedType(
        ConciliacionCashFlowRowDto row)
    {
        if (string.Equals(row.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
            return ("traslado-interno", "Traslado interno entre cuentas", "neutral", "flujo-caja");

        var text = BuildConciliacionCashFlowSearchText(row);
        if (string.Equals(row.Direction, "Entrada", StringComparison.OrdinalIgnoreCase))
        {
            if (ConciliacionInvoiceTokenRegex.IsMatch(text))
                return ("entrada-fe", "Entrada FE - pago cliente", "success", "entradas-fe");

            if (ContainsConciliacionAny(text, "ABONO INTERES", "APERTURA INVERSION", "INTERES", "RENDIMIENTO", "CANCELACION INVERSION", "CANCELACION INVERCION"))
                return ("entrada-comprobante", "Entrada - comprobante contable", "info", "comprobantes");

            return ("huerfano", "Entrada sin clasificar", "warning", "huerfanos");
        }

        if (string.Equals(row.Direction, "Salida", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsConciliacionAny(text, "CUENTA DE COBRO", "CUENTAS DE COBRO", "DOCUMENTO SOPORTE", "DOC SOPORTE", "DS "))
                return ("cuenta-cobro", "Documento soporte / cuenta de cobro", "info", "cuentas-cobro");

            if (ContainsConciliacionAny(text, "FACTURA ELECTRONICA", "FACTURA ELECTR", "FACTURA", "FEV", "FVE", "FE "))
                return ("salida-fe", "Salida FE - factura electronica", "success", "salidas-fe");

            if (ContainsConciliacionAny(
                text,
                "MI PLANILLA",
                "MIPLANILLA",
                "PLANILLA",
                "ETB",
                "ENEL",
                "CANCELACION INVERSION",
                "CANCELACION INVERCION",
                "GRAVAMEN",
                "GMF",
                "4X1000",
                "4 X 1000",
                "COMISION",
                "GASTO BANCARIO",
                "INTERES",
                "DIAN",
                "IMPUESTO"))
            {
                return ("comprobante-contable", "Salida - comprobante contable", "info", "comprobantes");
            }

            return ("huerfano", "Salida sin clasificar", "warning", "huerfanos");
        }

        return ("huerfano", "Sin clasificar", "warning", "huerfanos");
    }

    private static string ResolveDefaultInvoiceStatus(string detectedTypeKey)
    {
        return detectedTypeKey switch
        {
            "salida-fe" => "Pendiente cruce Dataverse",
            "cuenta-cobro" => "Se creara desde flujo",
            "comprobante-contable" => "No requiere factura",
            "entrada-comprobante" => "No requiere factura",
            "entrada-fe" => "Pendiente cruce factura",
            _ => "Pendiente clasificar"
        };
    }

    private static bool IsConciliacionPocketTransfer(ConciliacionCashFlowRowDto? row)
    {
        if (row is null)
            return false;

        return ContainsConciliacionAny(BuildConciliacionCashFlowSearchText(row), "BOLSILLO");
    }

    private static bool IsConciliacionSiigoRegistered(string siigoDocumentId, string siigoStatus)
    {
        if (!string.IsNullOrWhiteSpace(siigoDocumentId))
            return true;

        var status = (siigoStatus ?? "").Trim();
        if (status.Equals("si", StringComparison.OrdinalIgnoreCase)
            || status.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = status.ToUpperInvariant();
        return normalized.Contains("REGISTR", StringComparison.Ordinal)
            || normalized.Contains("SUBID", StringComparison.Ordinal)
            || normalized.Contains("ENVIAD", StringComparison.Ordinal)
            || normalized.Contains("CREAD", StringComparison.Ordinal);
    }

    private static string BuildConciliacionCashFlowSearchText(ConciliacionCashFlowRowDto row) =>
        string.Join(" ", new[]
        {
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations,
            row.ExcelMovementType,
            row.BankAccountName,
            row.SourceFlow
        }).ToUpperInvariant();

    private static bool ContainsConciliacionAny(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static ConciliacionClientPaymentSummaryDto BuildConciliacionClientPaymentSummary(
        IReadOnlyList<ConciliacionClientPaymentRowDto> rows)
    {
        var pendingRows = rows.Where(static row => IsConciliacionPendingReviewStatus(row.Status)).ToArray();
        var suggestedRows = rows.Where(static row => string.Equals(row.Status, "Sugerido", StringComparison.OrdinalIgnoreCase)).ToArray();
        var readyRows = rows.Where(static row => string.Equals(row.Status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)).ToArray();
        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnDisplay))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionClientPaymentSummaryDto
        {
            TotalRows = rows.Count,
            Suggested = suggestedRows.Length,
            Approved = rows.Count(static row => string.Equals(row.Status, "Aprobado", StringComparison.OrdinalIgnoreCase)),
            ReadyForSiigo = readyRows.Length,
            PreflightOk = rows.Count(static row => string.Equals(row.PreflightStatus, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.PreflightStatus, "ValidadoPendienteAprobacion", StringComparison.OrdinalIgnoreCase)),
            PreflightBlocked = rows.Count(static row => string.Equals(row.PreflightStatus, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)),
            Rejected = rows.Count(static row => string.Equals(row.Status, "Rechazado", StringComparison.OrdinalIgnoreCase)),
            PendingReview = pendingRows.Length,
            DifferenceOutOfTolerance = rows.Count(static row => string.Equals(row.Status, "DiferenciaFueraRango", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceToken = rows.Count(static row => string.Equals(row.Status, "SinFacturaDescripcion", StringComparison.OrdinalIgnoreCase)),
            NoInvoiceMatch = rows.Count(static row => string.Equals(row.Status, "FacturaNoEncontrada", StringComparison.OrdinalIgnoreCase)),
            AmbiguousInvoice = rows.Count(static row => string.Equals(row.Status, "FacturaAmbigua", StringComparison.OrdinalIgnoreCase)),
            TotalEntries = RoundCurrency(rows.Sum(static row => row.EntryValue)),
            SuggestedEntries = RoundCurrency(suggestedRows.Sum(static row => row.EntryValue)),
            ReadyForSiigoEntries = RoundCurrency(readyRows.Sum(static row => row.EntryValue)),
            PendingReviewEntries = RoundCurrency(pendingRows.Sum(static row => row.EntryValue)),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
        };
    }

    private static IReadOnlyList<ConciliacionPhaseDto> BuildConciliacionPhases(
        ConciliacionCashFlowSummaryDto cashFlow,
        ConciliacionClientPaymentSummaryDto clientPayments)
    {
        return new[]
        {
            BuildStaticConciliacionPhase(
                "flujo-caja",
                "Flujo de caja por banco",
                cashFlow.TotalRows > 0 ? "Activo" : "Sin datos",
                cashFlow.TotalRows > 0 ? "success" : "neutral",
                "Semanal y cierre mensual",
                cashFlow.LastRunLabel,
                "Validar cada fila antes de enviarla a Siigo y cruzar el extracto bancario al cierre.",
                new[]
                {
                    Step("Filas importadas", "Listo", "success", $"{cashFlow.TotalRows:N0} filas del periodo."),
                    Step("Tipo detectado", "Parcial", "info", "Clasificacion inicial por entrada/salida y texto."),
                    Step("Validacion", cashFlow.PendingValidationRows > 0 ? "Pendiente" : "Lista", cashFlow.PendingValidationRows > 0 ? "warning" : "success", $"{cashFlow.PendingValidationRows:N0} filas por validar."),
                    Step("Extracto mensual", "Falta", "warning", "Cruce banco vs flujo y tabla de cierre.")
                },
                $"Entradas {cashFlow.TotalEntries:N0}; salidas {cashFlow.TotalExits:N0}; traslados {cashFlow.TotalTransfers:N0}.",
                new[]
                {
                    "Importacion de flujo de caja Cloud/Copiers a Dataverse.",
                    "Separacion de traslados internos y omision de traslados de bolsillos.",
                    "Columna visual de tipo de comprobante detectado."
                },
                new[]
                {
                    "Persistir la categoria reasignada desde el popup.",
                    "Cruzar mensualmente contra extractos bancarios y saldos finales por banco.",
                    "Bloquear envio a Siigo hasta que la fila este validada y completa."
                }),
            BuildStaticConciliacionPhase(
                "salidas-fe",
                "Registro de Salidas FE",
                cashFlow.OutgoingInvoiceRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.OutgoingInvoiceRows > 0 ? "info" : "neutral",
                "Por periodo",
                cashFlow.LastRunLabel,
                "Cruzar salidas con factura electronica contra Dataverse, Siigo y saldo de factura.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.OutgoingInvoiceRows:N0} salidas FE."),
                    Step("Factura Dataverse", "Falta", "warning", "Cruce DIAN/Dataverse pendiente."),
                    Step("Factura Siigo", "Falta", "warning", "Consulta de compras/egresos pendiente."),
                    Step("Pago Siigo", "Falta", "warning", "Registro de pago pendiente.")
                },
                "",
                new[]
                {
                    "Filtro lateral y tabla de salidas con factura electronica.",
                    "Estado visual para factura Dataverse, factura Siigo, pago Siigo y saldo."
                },
                new[]
                {
                    "Conectar cruce real contra gastos DIAN/Dataverse.",
                    "Consultar saldo de factura y pago en Siigo.",
                    "Crear prevalidacion completa antes del envio a Siigo."
                }),
            BuildStaticConciliacionPhase(
                "entradas-fe",
                "Registro de Entradas FE",
                clientPayments.PendingReview > 0 ? "Con pendientes" : clientPayments.Suggested > 0 ? "Listo para aprobar" : "Sin pendientes",
                clientPayments.PendingReview > 0 ? "warning" : clientPayments.Suggested > 0 ? "info" : "success",
                "Semanal",
                clientPayments.LastRunLabel,
                "Validar pagos de clientes, retenciones y borrador contable antes del envio a Siigo.",
                new[]
                {
                    Step("Entradas", "Importadas", "success", $"{clientPayments.TotalRows:N0} cruces."),
                    Step("Factura Dataverse", "Parcial", "info", $"{clientPayments.Suggested:N0} sugeridos."),
                    Step("Pago Dataverse", "Activo", "success", "Cruce guarda retenciones calculadas."),
                    Step("Subida Siigo", "Falta", "warning", $"{clientPayments.ReadyForSiigo:N0} listos para envio futuro.")
                },
                $"Valor revisado {clientPayments.TotalEntries:N0}; sugerido {clientPayments.SuggestedEntries:N0}; listo Siigo {clientPayments.ReadyForSiigoEntries:N0}.",
                new[]
                {
                    "Cruce de entradas contra facturacion Dataverse.",
                    "Aprobacion, revision, rechazo y prevalidacion pre-Siigo.",
                    "Borrador contable con retenciones y balance debito/credito."
                },
                new[]
                {
                    "Envio real a Siigo de los registros `ListoSiigo`.",
                    "Confirmar marca de pago registrado en Dataverse cuando el comprobante quede creado.",
                    "Reflejar cambios posteriores de Siigo hacia Dataverse."
                }),
            BuildStaticConciliacionPhase(
                "cuentas-cobro",
                "Registro de cuentas de cobro",
                cashFlow.CollectionAccountRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.CollectionAccountRows > 0 ? "info" : "neutral",
                "Por actualizacion de flujo",
                cashFlow.LastRunLabel,
                "Crear automaticamente la cuenta de cobro en el modulo y completar retenciones alli.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.CollectionAccountRows:N0} cuentas de cobro."),
                    Step("Creacion app", "Falta", "warning", "Crear registro automaticamente desde flujo."),
                    Step("Retenciones", "Actual", "info", "Formulario existente en modulo cuentas de cobro."),
                    Step("Dataverse DIAN", "Falta", "warning", "Se confirma en importacion DIAN siguiente.")
                },
                "",
                new[]
                {
                    "Filtro y deteccion inicial desde flujo de caja.",
                    "Modulo de cuentas de cobro ya permite capturar retenciones."
                },
                new[]
                {
                    "Crear registros automaticamente en el modulo al actualizar flujo.",
                    "Subir a Siigo cuando retenciones esten completas y aprobadas.",
                    "Marcar subida a Dataverse en la siguiente importacion DIAN."
                }),
            BuildStaticConciliacionPhase(
                "comprobantes",
                "Registro de comprobantes contables",
                cashFlow.AccountingVoucherRows > 0 ? "Detectado" : "Sin filas",
                cashFlow.AccountingVoucherRows > 0 ? "info" : "neutral",
                "Semanal",
                cashFlow.LastRunLabel,
                "Validar comprobantes sin factura/documento soporte y preparar asiento completo.",
                new[]
                {
                    Step("Filas candidatas", "Detectadas", "info", $"{cashFlow.AccountingVoucherRows:N0} comprobantes."),
                    Step("Dataverse", "Flujo OK", "success", "Registro bancario ya existe."),
                    Step("Plantillas", "Parcial", "info", "Hay plantillas piloto para algunos casos."),
                    Step("Siigo", "Falta", "warning", "Crear journals/egresos automaticos.")
                },
                "",
                new[]
                {
                    "Deteccion de MI PLANILLA, ENEL, ETB, intereses, inversiones, gravamen y gastos bancarios.",
                    "Catalogo de cuentas Siigo y plantillas multi-linea ya existen como base."
                },
                new[]
                {
                    "Consolidar gravamen mensual por banco en un solo comprobante.",
                    "Partir MI PLANILLA por salud, pension, ARL y caja con cuentas contables separadas.",
                    "Validar que cada asiento tenga todas sus lineas antes de crear Siigo/Dataverse."
                }),
            BuildStaticConciliacionPhase(
                "huerfanos",
                "Registros huerfanos",
                cashFlow.OrphanRows > 0 ? "Con pendientes" : "Sin pendientes",
                cashFlow.OrphanRows > 0 ? "warning" : "success",
                "Continuo",
                cashFlow.LastRunLabel,
                "Reasignar categoria con popup y convertir correcciones frecuentes en reglas.",
                new[]
                {
                    Step("Filas sin tipo", "Pendiente", cashFlow.OrphanRows > 0 ? "warning" : "success", $"{cashFlow.OrphanRows:N0} registros."),
                    Step("Popup categoria", "Visual", "info", "Opciones restringidas por entrada/salida."),
                    Step("Guardado Dataverse", "Falta", "warning", "Campo/endpoint pendiente.")
                },
                "",
                new[]
                {
                    "Vista dedicada de huerfanos.",
                    "Popup visual para reasignar categoria segun entrada o salida."
                },
                new[]
                {
                    "Guardar reasignacion en Dataverse.",
                    "Crear reglas desde correcciones repetidas.",
                    "Reprocesar las filas despues de reasignarlas."
                })
        };
    }

    private static ConciliacionPhaseDto BuildStaticConciliacionPhase(
        string key,
        string label,
        string status,
        string tone,
        string cadence,
        string lastRun,
        string nextStep,
        IReadOnlyList<ConciliacionFlowStepDto> steps,
        string runSummary = "",
        IReadOnlyList<string>? readyItems = null,
        IReadOnlyList<string>? missingItems = null) =>
        new()
        {
            Key = key,
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            CadenceLabel = cadence,
            LastRunLabel = string.IsNullOrWhiteSpace(lastRun) ? "Sin log" : lastRun,
            RunSummary = string.IsNullOrWhiteSpace(runSummary) ? "Resumen pendiente de conectar a logs historicos." : runSummary,
            NextStep = nextStep,
            ReadyItems = readyItems ?? Array.Empty<string>(),
            MissingItems = missingItems ?? Array.Empty<string>(),
            Steps = steps
        };

    private static ConciliacionFlowStepDto Step(string label, string status, string tone, string summary) =>
        new()
        {
            Label = label,
            StatusLabel = status,
            StatusTone = tone,
            Summary = summary
        };

    private static ConciliacionClientPaymentRowDto? ParseConciliacionClientPaymentRow(
        JsonElement item,
        RhEntityMetadata metadata)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, ClientPaymentMatchIdField)).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var status = FirstNonEmpty(ReadString(item, ClientPaymentMatchStatusField), "Sin estado");
        var movementDate = ReadDateOnly(item, ClientPaymentMatchMovementDateField);
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField));
        var preflightStatus = ReadString(item, ClientPaymentMatchPreflightStatusField).Trim();
        var preflightValidatedOn = ParseConciliacionDateTimeOffset(ReadString(item, ClientPaymentMatchPreflightValidatedOnField));

        return new ConciliacionClientPaymentRowDto
        {
            RecordId = recordId,
            Status = status,
            StatusLabel = ResolveConciliacionStatusLabel(status),
            StatusTone = ResolveConciliacionStatusTone(status),
            Confidence = ReadInt(item, ClientPaymentMatchConfidenceField),
            Reason = ReadString(item, ClientPaymentMatchReasonField).Trim(),
            MovementId = ReadString(item, ClientPaymentMatchMovementIdField).Trim(),
            MovementExternalKey = ReadString(item, ClientPaymentMatchMovementExternalKeyField).Trim(),
            MovementDateValue = movementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            MovementDateDisplay = movementDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            SourceFlow = ReadString(item, ClientPaymentMatchSourceFlowField).Trim(),
            BankAccountCode = ReadString(item, ClientPaymentMatchBankCodeField).Trim(),
            BankAccountName = ReadString(item, ClientPaymentMatchBankNameField).Trim(),
            Description = ReadString(item, ClientPaymentMatchDescriptionField).Trim(),
            InvoiceRecordIds = ReadString(item, ClientPaymentMatchInvoiceIdsField).Trim(),
            InvoiceNumbers = ReadString(item, ClientPaymentMatchInvoiceNumbersField).Trim(),
            ClientNames = ReadString(item, ClientPaymentMatchClientField).Trim(),
            EntryValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchEntryField) ?? 0m),
            InvoiceTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchInvoiceTotalField) ?? 0m),
            RetentionsTotal = RoundCurrency((ReadDecimal(item, ClientPaymentMatchReteFteField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchReteIcaField) ?? 0m)
                + (ReadDecimal(item, ClientPaymentMatchRteIvaField) ?? 0m)),
            DifferenceValue = RoundCurrency(ReadDecimal(item, ClientPaymentMatchDifferenceField) ?? 0m),
            DraftJson = ReadString(item, ClientPaymentMatchDraftJsonField).Trim(),
            PreflightStatus = preflightStatus,
            PreflightStatusLabel = ResolveConciliacionPreflightStatusLabel(preflightStatus),
            PreflightStatusTone = ResolveConciliacionPreflightStatusTone(preflightStatus),
            PreflightMessage = ReadString(item, ClientPaymentMatchPreflightMessageField).Trim(),
            PreflightDebitTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightDebitField) ?? 0m),
            PreflightCreditTotal = RoundCurrency(ReadDecimal(item, ClientPaymentMatchPreflightCreditField) ?? 0m),
            PreflightValidatedOnDisplay = FormatConciliacionDateTimeDisplay(preflightValidatedOn),
            ModifiedOnDisplay = modifiedOn?.ToString("O", CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static HashSet<string> BuildConciliacionClientPaymentAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var values = attributes.Count > 0
            ? new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        })
        {
            if (!string.IsNullOrWhiteSpace(field))
                values.Add(field);
        }

        return values;
    }

    private static string BuildConciliacionClientPaymentSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ClientPaymentMatchIdField,
            ClientPaymentMatchStatusField,
            ClientPaymentMatchConfidenceField,
            ClientPaymentMatchReasonField,
            ClientPaymentMatchMovementIdField,
            ClientPaymentMatchMovementExternalKeyField,
            ClientPaymentMatchMovementDateField,
            ClientPaymentMatchSourceFlowField,
            ClientPaymentMatchBankCodeField,
            ClientPaymentMatchBankNameField,
            ClientPaymentMatchDescriptionField,
            ClientPaymentMatchEntryField,
            ClientPaymentMatchInvoiceIdsField,
            ClientPaymentMatchInvoiceNumbersField,
            ClientPaymentMatchClientField,
            ClientPaymentMatchInvoiceTotalField,
            ClientPaymentMatchReteFteField,
            ClientPaymentMatchReteIcaField,
            ClientPaymentMatchRteIvaField,
            ClientPaymentMatchDifferenceField,
            ClientPaymentMatchDraftJsonField,
            ClientPaymentMatchPreflightStatusField,
            ClientPaymentMatchPreflightMessageField,
            ClientPaymentMatchPreflightValidatedOnField,
            ClientPaymentMatchPreflightDebitField,
            ClientPaymentMatchPreflightCreditField,
            ConciliacionCreatedOnField,
            ConciliacionModifiedOnField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field) && attributes.Contains(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyDictionary<string, ConciliacionAccountCatalogItem>> GetConciliacionAccountCatalogAsync(
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountCatalogLogicalName,
            AccountCatalogSetName,
            AccountCatalogIdField,
            AccountCatalogPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildAccountCatalogAttributeSet(metadata, attributes);
        var rows = await GetAccountCatalogRowsAsync(metadata, attributes, ct);

        return rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var row = group.First();
                    return new ConciliacionAccountCatalogItem(row.Code.Trim(), row.Name.Trim(), row.Active);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static ConciliacionPreflightValidation ValidateConciliacionClientPaymentDraft(
        ConciliacionClientPaymentRowDto row,
        IReadOnlyDictionary<string, ConciliacionAccountCatalogItem> accountCatalog)
    {
        var issues = new List<string>();
        var debitTotal = 0m;
        var creditTotal = 0m;

        if (!IsConciliacionSiigoCandidateStatus(row.Status))
            issues.Add("El estado actual debe resolverse antes de preparar envio a Siigo.");
        if (row.EntryValue <= 0m)
            issues.Add("El movimiento no tiene valor de entrada.");
        if (row.InvoiceTotal <= 0m)
            issues.Add("No hay total de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.InvoiceNumbers))
            issues.Add("No hay numero de factura asociado.");
        if (string.IsNullOrWhiteSpace(row.ClientNames))
            issues.Add("No hay cliente asociado.");
        if (string.IsNullOrWhiteSpace(row.BankAccountCode))
            issues.Add("No hay cuenta bancaria contable.");
        if (Math.Abs(row.DifferenceValue) > RegistroPagosClientesBalancedTolerance)
            issues.Add($"La diferencia supera la tolerancia de {RegistroPagosClientesBalancedTolerance:N0}.");

        if (string.IsNullOrWhiteSpace(row.DraftJson))
        {
            issues.Add("No existe JSON de borrador Siigo.");
            return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
        }

        try
        {
            using var doc = JsonDocument.Parse(row.DraftJson);
            var root = doc.RootElement;
            var type = ReadString(root, "type");
            if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
                issues.Add("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");

            if (!root.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() == 0)
            {
                issues.Add("El borrador no tiene lineas contables.");
                return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
            }

            var lineNumber = 0;
            foreach (var line in lines.EnumerateArray())
            {
                lineNumber++;
                var accountCode = ReadString(line, "accountCode").Trim();
                var accountName = ReadString(line, "accountName").Trim();
                var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
                var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
                debitTotal = RoundCurrency(debitTotal + debit);
                creditTotal = RoundCurrency(creditTotal + credit);

                if (ReadBool(line, "requiresAccountMapping"))
                    issues.Add($"Linea {lineNumber}: falta mapear cuenta contable para {FirstNonEmpty(accountName, "la linea")}.");
                if (debit < 0m || credit < 0m)
                    issues.Add($"Linea {lineNumber}: debito/credito no puede ser negativo.");
                if (debit > 0m && credit > 0m)
                    issues.Add($"Linea {lineNumber}: no puede tener debito y credito al mismo tiempo.");
                if (debit == 0m && credit == 0m)
                    continue;
                if (string.IsNullOrWhiteSpace(accountCode))
                {
                    issues.Add($"Linea {lineNumber}: falta codigo de cuenta.");
                    continue;
                }
                if (!accountCatalog.TryGetValue(accountCode, out var account))
                {
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} no esta en el catalogo contable Siigo de Dataverse.");
                    continue;
                }
                if (!account.Active)
                    issues.Add($"Linea {lineNumber}: la cuenta {accountCode} esta inactiva.");
            }

            if (Math.Abs(debitTotal - creditTotal) > 1m)
                issues.Add($"El asiento no cuadra: debito {debitTotal:N2} vs credito {creditTotal:N2}.");
        }
        catch (JsonException)
        {
            issues.Add("El JSON de borrador Siigo no es valido.");
        }

        return new ConciliacionPreflightValidation(RoundCurrency(debitTotal), RoundCurrency(creditTotal), issues);
    }

    private static object BuildConciliacionClientPaymentSiigoDryRunPayload(
        ConciliacionClientPaymentRowDto row,
        ConciliacionPreflightValidation preflight,
        out int lineCount)
    {
        if (string.IsNullOrWhiteSpace(row.DraftJson))
            throw new InvalidOperationException("No existe JSON de borrador Siigo para armar la simulacion.");

        using var doc = JsonDocument.Parse(row.DraftJson);
        var root = doc.RootElement;
        var type = ReadString(root, "type");
        if (!string.Equals(type, "ComprobanteIngresoSiigoBorrador", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El borrador no corresponde al tipo ComprobanteIngresoSiigoBorrador.");
        if (!root.TryGetProperty("lines", out var linesElement)
            || linesElement.ValueKind != JsonValueKind.Array
            || linesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("El borrador no tiene lineas contables para simular.");
        }

        var lines = new List<object>();
        foreach (var line in linesElement.EnumerateArray())
        {
            var debit = RoundCurrency(ReadDecimal(line, "debit") ?? 0m);
            var credit = RoundCurrency(ReadDecimal(line, "credit") ?? 0m);
            if (debit == 0m && credit == 0m)
                continue;

            var accountCode = ReadString(line, "accountCode").Trim();
            lines.Add(new
            {
                account = new
                {
                    code = accountCode,
                    name = ReadString(line, "accountName").Trim()
                },
                description = FirstNonEmpty(
                    ReadString(line, "description"),
                    ReadString(line, "detail"),
                    row.InvoiceNumbers,
                    row.Description).Trim(),
                thirdParty = FirstNonEmpty(ReadString(line, "thirdParty"), row.ClientNames).Trim(),
                detail = FirstNonEmpty(ReadString(line, "detail"), row.InvoiceNumbers).Trim(),
                debit,
                credit
            });
        }

        lineCount = lines.Count;
        if (lineCount == 0)
            throw new InvalidOperationException("El borrador no tiene lineas con debito o credito.");

        var movementDate = FirstNonEmpty(row.MovementDateValue, ReadString(root, "movement.date")).Trim();
        var invoices = ReadConciliacionDraftInvoices(root);

        return new
        {
            dryRun = true,
            targetEndpoint = "/v1/journals",
            note = "Payload de prueba generado por Conciliacion. No fue enviado a Siigo.",
            document = new
            {
                type = "CC",
                id = ConciliacionSiigoIncomeJournalDocumentFallbackId,
                code = "17",
                name = ConciliacionSiigoIncomeJournalDocumentFallbackName
            },
            date = movementDate,
            customer = new
            {
                name = row.ClientNames,
                invoices = row.InvoiceNumbers
            },
            movement = new
            {
                id = row.MovementId,
                externalKey = row.MovementExternalKey,
                sourceFlow = row.SourceFlow,
                bankAccountCode = row.BankAccountCode,
                bankAccountName = row.BankAccountName,
                description = row.Description,
                entry = row.EntryValue
            },
            totals = new
            {
                invoiceTotal = row.InvoiceTotal,
                payment = row.EntryValue,
                retentions = row.RetentionsTotal,
                difference = row.DifferenceValue,
                debit = preflight.DebitTotal,
                credit = preflight.CreditTotal
            },
            invoices,
            items = lines
        };
    }

    private static object BuildConciliacionClientPaymentSiigoSendPayload(
        ConciliacionClientPaymentRowDto row,
        IReadOnlyList<ConciliacionSiigoInvoiceDueItem> invoiceDues,
        string movementDate,
        string customerIdentification,
        SiigoDocumentTypeLookupDto journalDocument,
        decimal siigoAdjustment)
    {
        var items = new List<Dictionary<string, object?>>();
        var customer = new
        {
            identification = customerIdentification,
            branch_office = 0
        };
        if (row.EntryValue > 0m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = row.BankAccountCode,
                    movement = "Debit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText(
                    FirstNonEmpty($"Pago {row.InvoiceNumbers} {row.BankAccountName}", row.BankAccountName, "Banco"),
                    200),
                ["value"] = row.EntryValue
            });
        }

        foreach (var item in invoiceDues)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "13050501",
                    movement = "Credit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText($"Clientes nacionales {item.Invoice.InvoiceNumber}".Trim(), 200),
                ["due"] = new
                {
                    prefix = item.Due.Prefix,
                    consecutive = item.Due.Consecutive,
                    quote = 1,
                    date = movementDate
                },
                ["value"] = RoundCurrency(item.Value)
            });

            foreach (var retention in item.RetentionTaxes)
            {
                items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account"] = new
                    {
                        code = retention.AccountCode,
                        movement = "Debit"
                    },
                    ["customer"] = customer,
                    ["tax"] = new
                    {
                        id = retention.TaxId
                    },
                    ["description"] = TruncateAccountCatalogText($"{retention.Kind} {item.Invoice.InvoiceNumber}".Trim(), 200),
                    ["value"] = retention.Value
                });
            }
        }

        var adjustment = RoundCurrency(siigoAdjustment);
        if (Math.Abs(adjustment) > 0.009m)
        {
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = new
                {
                    code = "42958101",
                    movement = adjustment > 0m ? "Debit" : "Credit"
                },
                ["customer"] = customer,
                ["description"] = TruncateAccountCatalogText($"Ajuste al peso {row.InvoiceNumbers}".Trim(), 200),
                ["value"] = Math.Abs(adjustment)
            });
        }

        return new
        {
            document = new
            {
                id = journalDocument.Id
            },
            date = movementDate,
            items,
            observations = TruncateAccountCatalogText(
                $"{journalDocument.Name} - Conciliacion flujo caja {row.SourceFlow} {row.MovementExternalKey} {row.Description}".Trim(),
                500)
        };
    }

    private static IReadOnlyDictionary<string, SiigoInvoiceRowDto> BuildConciliacionSiigoInvoiceLookup(
        IReadOnlyList<SiigoInvoiceRowDto> invoices)
    {
        var lookup = new Dictionary<string, SiigoInvoiceRowDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices ?? Array.Empty<SiigoInvoiceRowDto>())
        {
            AddConciliacionSiigoInvoiceLookupKey(lookup, invoice.Id, invoice);
            AddConciliacionSiigoInvoiceLookupKey(lookup, invoice.Name, invoice);
            if (!string.IsNullOrWhiteSpace(invoice.Prefix) && invoice.Number is > 0)
                AddConciliacionSiigoInvoiceLookupKey(lookup, $"{invoice.Prefix}-{invoice.Number}", invoice);
        }

        return lookup;
    }

    private static void AddConciliacionSiigoInvoiceLookupKey(
        IDictionary<string, SiigoInvoiceRowDto> lookup,
        string? key,
        SiigoInvoiceRowDto invoice)
    {
        var normalized = NormalizeDocumentKey(key);
        if (!string.IsNullOrWhiteSpace(normalized) && !lookup.ContainsKey(normalized))
            lookup[normalized] = invoice;
    }

    private static decimal ResolveConciliacionSiigoInvoiceAccountingValue(
        BillingRecordRow invoice,
        IReadOnlyDictionary<string, SiigoInvoiceRowDto> siigoInvoiceLookup,
        bool requireLiveSiigoInvoice,
        ICollection<string> issues)
    {
        var siigoInvoice = FindConciliacionSiigoInvoice(invoice, siigoInvoiceLookup);
        if (siigoInvoice is null)
        {
            if (requireLiveSiigoInvoice)
            {
                issues.Add($"No encontre en Siigo la factura {FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber)} para confirmar saldo actual.");
            }

            return RoundCurrency(invoice.TotalInvoice);
        }

        var balance = RoundCurrency(siigoInvoice.Balance);
        if (balance <= 0m)
        {
            issues.Add($"La factura {siigoInvoice.Name} aparece sin saldo pendiente en Siigo.");
            return 0m;
        }

        return balance;
    }

    private static SiigoInvoiceRowDto? FindConciliacionSiigoInvoice(
        BillingRecordRow invoice,
        IReadOnlyDictionary<string, SiigoInvoiceRowDto> siigoInvoiceLookup)
    {
        foreach (var key in new[]
        {
            invoice.SiigoInvoiceId,
            invoice.SiigoInvoiceName,
            invoice.InvoiceNumber,
            !string.IsNullOrWhiteSpace(invoice.InvoicePrefix) && !string.IsNullOrWhiteSpace(invoice.InvoiceCode)
                ? $"{invoice.InvoicePrefix}-{invoice.InvoiceCode}"
                : ""
        })
        {
            if (siigoInvoiceLookup.TryGetValue(NormalizeDocumentKey(key), out var siigoInvoice))
                return siigoInvoice;
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractConciliacionInvoiceRecordIds(ConciliacionClientPaymentRowDto row)
    {
        var values = SplitConciliacionRecordIdList(row.InvoiceRecordIds)
            .Concat(ExtractConciliacionDraftInvoiceRecordIds(row.DraftJson))
            .Select(static value => Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : "")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values;
    }

    private static IEnumerable<string> SplitConciliacionRecordIdList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(new[] { '|', ';', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static IReadOnlyList<string> ExtractConciliacionDraftInvoiceRecordIds(string? draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
            return Array.Empty<string>();

        using var doc = JsonDocument.Parse(draftJson);
        if (!doc.RootElement.TryGetProperty("invoices", out var invoices)
            || invoices.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return invoices
            .EnumerateArray()
            .Select(static invoice => ReadString(invoice, "recordId").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static bool TryBuildConciliacionSiigoDue(
        BillingRecordRow invoice,
        out ConciliacionSiigoDue due,
        out string issue)
    {
        due = new ConciliacionSiigoDue("", 0);
        issue = "";

        var label = FirstNonEmpty(invoice.SiigoInvoiceName, invoice.InvoiceNumber);
        if (!TryParseConciliacionDueLabel(label, out due)
            && IsConciliacionInvoiceDuePrefix(invoice.InvoicePrefix)
            && int.TryParse(invoice.InvoiceCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var siigoCode))
        {
            due = new ConciliacionSiigoDue(invoice.InvoicePrefix.Trim(), siigoCode);
        }

        if (!string.IsNullOrWhiteSpace(due.Prefix) && due.Consecutive > 0)
            return true;

        issue = $"No se pudo separar prefijo y consecutivo Siigo para la factura {FirstNonEmpty(label, invoice.InvoicePrefix, invoice.InvoiceCode)}.";
        return false;
    }

    private static bool TryParseConciliacionDueLabel(string label, out ConciliacionSiigoDue due)
    {
        due = new ConciliacionSiigoDue("", 0);
        var normalized = Regex.Replace((label ?? "").Trim().ToUpperInvariant(), @"\s+", "-", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"-+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var match = Regex.Match(normalized, @"^(?<prefix>.*?)[-]?(?<consecutive>\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["consecutive"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var consecutive))
            return false;

        var prefix = match.Groups["prefix"].Value.Trim('-');
        if (string.IsNullOrWhiteSpace(prefix) || !IsConciliacionInvoiceDuePrefix(prefix))
            return false;

        due = new ConciliacionSiigoDue(prefix, consecutive);
        return true;
    }

    private static bool IsConciliacionInvoiceDuePrefix(string? value)
    {
        var prefix = (value ?? "").Trim().ToUpperInvariant();
        return Regex.IsMatch(prefix, @"^(?:FV|FVE|FEV|FEM|FE|FEDT|FEKT)(?:-\d+)?$", RegexOptions.CultureInvariant);
    }

    private static string NormalizeConciliacionIdentificationDigits(string? value) =>
        NormalizeConciliacionDigits(value);

    private static decimal ResolveConciliacionInvoiceRetentionsTotal(BillingRecordRow invoice) =>
        RoundCurrency(
            ResolveCashFlowClientPaymentReteFteValue(invoice)
            + ResolveCashFlowClientPaymentReteIcaValue(invoice)
            + ResolveCashFlowClientPaymentRteIvaValue(invoice));

    private static IReadOnlyList<ConciliacionRetentionTax> ResolveConciliacionInvoiceRetentionTaxes(
        BillingRecordRow invoice,
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        ICollection<string> issues)
    {
        var result = new List<ConciliacionRetentionTax>();
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "ReteFte",
            label: "retefuente",
            value: ResolveCashFlowClientPaymentReteFteValue(invoice),
            baseValue: ResolveConciliacionRetentionBase(invoice));
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "ReteIca",
            label: "ReteICA",
            value: ResolveCashFlowClientPaymentReteIcaValue(invoice),
            baseValue: ResolveConciliacionRetentionBase(invoice));
        AddConciliacionRetentionTax(
            result,
            issues,
            siigoTaxes,
            invoice.InvoiceNumber,
            kind: "RteIva",
            label: "RteIVA",
            value: ResolveCashFlowClientPaymentRteIvaValue(invoice),
            baseValue: invoice.VatValue);

        return result;
    }

    private static void AddConciliacionRetentionTax(
        ICollection<ConciliacionRetentionTax> result,
        ICollection<string> issues,
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        string invoiceNumber,
        string kind,
        string label,
        decimal value,
        decimal baseValue)
    {
        value = RoundCurrency(value);
        if (value <= 0m)
            return;

        var accountCode = ResolveConciliacionRetentionAccountCode(kind);
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            issues.Add($"La factura {invoiceNumber} tiene {label}, pero falta mapear la cuenta contable para enviarla a Siigo.");
            return;
        }

        if (baseValue <= 0m)
        {
            issues.Add($"La factura {invoiceNumber} tiene {label}, pero no hay base para calcular el porcentaje.");
            return;
        }

        var percentage = Math.Round(value / baseValue * 100m, 4, MidpointRounding.AwayFromZero);
        var tax = FindConciliacionRetentionTax(siigoTaxes, kind, percentage);
        if (tax is null)
        {
            issues.Add($"No encontre impuesto Siigo activo para {label} {percentage:N4}% de la factura {invoiceNumber}.");
            return;
        }

        result.Add(new ConciliacionRetentionTax(kind, tax.Id, accountCode, value, percentage));
    }

    private static string ResolveConciliacionRetentionAccountCode(string kind) =>
        kind switch
        {
            "ReteIca" => "13551805",
            "RteIva" => "13551701",
            _ => "13551513"
        };

    private static SiigoTaxLookupDto? FindConciliacionRetentionTax(
        IReadOnlyList<SiigoTaxLookupDto> siigoTaxes,
        string kind,
        decimal percentage)
    {
        return siigoTaxes
            .Where(tax => tax.Active
                && tax.Id > 0
                && MatchesConciliacionRetentionTaxKind(tax, kind))
            .Select(tax => new
            {
                Tax = tax,
                Difference = Math.Abs(tax.Percentage - percentage)
            })
            .Where(static item => item.Difference <= 0.1m)
            .OrderBy(static item => item.Difference)
            .ThenBy(static item => item.Tax.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Tax)
            .FirstOrDefault();
    }

    private static bool MatchesConciliacionRetentionTaxKind(SiigoTaxLookupDto tax, string kind)
    {
        var text = NormalizeConciliacionTaxText($"{tax.Type} {tax.Name}");
        return kind switch
        {
            "ReteIca" => text.Contains("ICA", StringComparison.OrdinalIgnoreCase),
            "RteIva" => text.Contains("IVA", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("RETE", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("RETENCION", StringComparison.OrdinalIgnoreCase)),
            _ => text.Contains("FUENTE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("RETEFTE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("RETEFUENTE", StringComparison.OrdinalIgnoreCase)
                || (text.Contains("RETENCION", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("ICA", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("IVA", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static decimal ResolveConciliacionRetentionBase(BillingRecordRow invoice)
    {
        var baseValue = RoundCurrency(invoice.TotalInvoice - invoice.VatValue);
        return baseValue > 0m ? baseValue : invoice.TotalInvoice;
    }

    private static string NormalizeConciliacionTaxText(string value)
    {
        var text = (value ?? "").Trim().ToUpperInvariant();
        return text
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("Ü", "U", StringComparison.Ordinal)
            .Replace("Ñ", "N", StringComparison.Ordinal);
    }

    private static object ResolveConciliacionClientPaymentType(string sourceFlow, string bankAccountCode)
    {
        var id = ResolveConciliacionClientPaymentTypeId(sourceFlow, bankAccountCode);
        return new
        {
            documentType = "RC",
            id,
            name = id == 13568 ? "Bancolombia Copiers Ventas" : "Bancolombia Cloud Ventas"
        };
    }

    private static int ResolveConciliacionClientPaymentTypeId(string sourceFlow, string bankAccountCode)
    {
        var isCopiers = sourceFlow.Contains("Copiers", StringComparison.OrdinalIgnoreCase)
            || bankAccountCode.Contains("11100505", StringComparison.OrdinalIgnoreCase);

        return isCopiers ? 13568 : 13566;
    }

    private static IReadOnlyList<object> ReadConciliacionDraftInvoices(JsonElement root)
    {
        if (!root.TryGetProperty("invoices", out var invoicesElement)
            || invoicesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object>();
        }

        return invoicesElement
            .EnumerateArray()
            .Select(invoice => new
            {
                recordId = ReadString(invoice, "recordId").Trim(),
                number = ReadString(invoice, "number").Trim(),
                client = ReadString(invoice, "client").Trim(),
                total = RoundCurrency(ReadDecimal(invoice, "total") ?? 0m),
                vat = RoundCurrency(ReadDecimal(invoice, "vat") ?? 0m)
            })
            .Cast<object>()
            .ToArray();
    }

    private static string NormalizeConciliacionClientPaymentStatus(string? rawStatus)
    {
        var status = (rawStatus ?? "").Trim();
        var allowed = new HashSet<string>(new[]
        {
            "Sugerido",
            "Aprobado",
            "Rechazado",
            "RevisionManual"
        }, StringComparer.OrdinalIgnoreCase);

        if (!allowed.Contains(status))
            throw new InvalidOperationException("El estado solicitado no es valido.");

        return allowed.First(value => string.Equals(value, status, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConciliacionPendingReviewStatus(string status)
    {
        return status switch
        {
            "DiferenciaFueraRango" => true,
            "SinFacturaDescripcion" => true,
            "FacturaNoEncontrada" => true,
            "FacturaAmbigua" => true,
            "RevisionManual" => true,
            "BloqueadoSiigo" => true,
            _ => false
        };
    }

    private static bool IsConciliacionApprovedForSiigo(string status) =>
        string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionReadyForRealSendStatus(string status) =>
        string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ErrorSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsConciliacionSiigoCandidateStatus(string status) =>
        string.Equals(status, "Sugerido", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Aprobado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "ErrorSiigo", StringComparison.OrdinalIgnoreCase);

    private static string ResolveConciliacionStatusLabel(string status)
    {
        return status switch
        {
            "Sugerido" => "Sugerido",
            "Aprobado" => "Aprobado",
            "Rechazado" => "Rechazado",
            "RevisionManual" => "Revision manual",
            "ListoSiigo" => "Listo Siigo",
            "EnviadoSiigo" => "Enviado Siigo",
            "ErrorSiigo" => "Error Siigo",
            "Conciliado" => "Conciliado",
            "BloqueadoSiigo" => "Bloqueado pre-Siigo",
            "DiferenciaFueraRango" => "Diferencia fuera de rango",
            "SinFacturaDescripcion" => "Sin factura en descripcion",
            "FacturaNoEncontrada" => "Factura no encontrada",
            "FacturaAmbigua" => "Factura ambigua",
            _ => status
        };
    }

    private static string ResolveConciliacionStatusTone(string status)
    {
        return status switch
        {
            "Sugerido" => "info",
            "Aprobado" => "success",
            "Rechazado" => "danger",
            "RevisionManual" => "warning",
            "ListoSiigo" => "success",
            "EnviadoSiigo" => "success",
            "ErrorSiigo" => "danger",
            "Conciliado" => "success",
            "BloqueadoSiigo" => "danger",
            "DiferenciaFueraRango" => "warning",
            "SinFacturaDescripcion" => "neutral",
            "FacturaNoEncontrada" => "danger",
            "FacturaAmbigua" => "warning",
            _ => "neutral"
        };
    }

    private static string ResolveConciliacionPreflightStatusLabel(string status)
    {
        return status switch
        {
            "ListoSiigo" => "Listo Siigo",
            "EnviadoSiigo" => "Enviado Siigo",
            "ErrorSiigo" => "Error Siigo",
            "ValidadoPendienteAprobacion" => "OK, falta aprobar",
            "BloqueadoSiigo" => "Bloqueado",
            _ => string.IsNullOrWhiteSpace(status) ? "Sin validar" : status
        };
    }

    private static string ResolveConciliacionPreflightStatusTone(string status)
    {
        return status switch
        {
            "ListoSiigo" => "success",
            "EnviadoSiigo" => "success",
            "ErrorSiigo" => "danger",
            "ValidadoPendienteAprobacion" => "info",
            "BloqueadoSiigo" => "danger",
            _ => "neutral"
        };
    }

    private static DateTimeOffset? ParseConciliacionDateTimeOffset(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            || DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return value;
        }

        return null;
    }

    private static string FormatConciliacionDateTimeDisplay(DateTimeOffset? value)
    {
        if (!value.HasValue || value.Value == default)
            return "Sin log";

        var bogota = TimeZoneInfo.ConvertTime(value.Value, MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time"));
        return bogota.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private sealed record ConciliacionPreflightValidation(
        decimal DebitTotal,
        decimal CreditTotal,
        IReadOnlyList<string> Issues);

    private sealed record ConciliacionSiigoDue(string Prefix, int Consecutive);

    private sealed record ConciliacionSiigoInvoiceDueItem(
        BillingRecordRow Invoice,
        ConciliacionSiigoDue Due,
        decimal Value,
        IReadOnlyList<ConciliacionRetentionTax> RetentionTaxes);

    private sealed record ConciliacionRetentionTax(
        string Kind,
        int TaxId,
        string AccountCode,
        decimal Value,
        decimal Percentage);

    private sealed record ConciliacionAccountCatalogItem(
        string Code,
        string Name,
        bool Active);
}
