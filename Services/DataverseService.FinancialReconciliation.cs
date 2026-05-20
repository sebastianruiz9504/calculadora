using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Reconciliation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ReconciliationCreditNoteLogicalName = "cr07a_siigonotacredito";
    private const string ReconciliationCreditNoteSetName = "cr07a_siigonotacreditos";
    private const string ReconciliationCreditNoteIdField = "cr07a_siigonotacreditoid";
    private const string ReconciliationCreditNotePrimaryNameField = "cr07a_name";
    private const string ReconciliationCreditNoteSiigoIdField = "cr07a_siigocreditnoteid";
    private const string ReconciliationCreditNoteSiigoNameField = "cr07a_siigocreditnotename";
    private const string ReconciliationCreditNoteSiigoNumberField = "cr07a_siigocreditnotenumber";
    private const string ReconciliationCreditNoteInvoiceIdField = "cr07a_siigoinvoiceid";
    private const string ReconciliationCreditNoteInvoiceNameField = "cr07a_siigoinvoicename";
    private const string ReconciliationCreditNoteInvoiceNumberField = "cr07a_siigoinvoicenumber";
    private const string ReconciliationCreditNoteInvoicePrefixField = "cr07a_siigoinvoiceprefix";
    private const string ReconciliationCreditNoteDateField = "cr07a_fechanotacredito";
    private const string ReconciliationCreditNoteCreatedField = "cr07a_fechacreacionsiigo";
    private const string ReconciliationCreditNoteTotalField = "cr07a_totalnotacredito";
    private const string ReconciliationCreditNoteVatField = "cr07a_valorivanotacredito";
    private const string ReconciliationCreditNoteCustomerIdentificationField = "cr07a_clienteidentificacion";
    private const string ReconciliationCreditNoteCustomerIdField = "cr07a_clientesiigoid";
    private const string ReconciliationCreditNoteStampStatusField = "cr07a_stampstatus";
    private const string ReconciliationCreditNoteCudeField = "cr07a_cude";
    private const string ReconciliationCreditNoteFacturacionIdField = "cr07a_facturaciondataverseid";
    private const string ReconciliationCreditNoteMatchByField = "cr07a_matchfacturacionpor";
    private const string ReconciliationCreditNoteProcessedField = "cr07a_procesada";
    private const string ReconciliationCreditNoteRawJsonField = "cr07a_rawjson";
    private const string ReconciliationCreditNoteLogField = "cr07a_logprocesamiento";
    private const string ReconciliationBillingSiigoInvoiceIdField = "cr07a_siigoinvoiceid";
    private const string ReconciliationBillingSiigoInvoiceNameField = "cr07a_siigoinvoicename";
    private const string ReconciliationBillingInvoiceCodeField = "cr07a_codigo";
    private const string ReconciliationBillingInvoicePrefixField = "cr07a_prefijo";
    private const string ReconciliationBillingOriginalValueField = "cr07a_valororiginalfactura";
    private const string ReconciliationBillingCreditNotesValueField = "cr07a_valornotascredito";
    private const string ReconciliationBillingAdjustedValueField = "cr07a_valorajustadofactura";
    private const string ReconciliationBillingCreditNotesCountField = "cr07a_cantidadnotascredito";
    private const string ReconciliationBillingLastCreditNoteIdField = "cr07a_ultimancsiigoid";
    private const string ReconciliationBillingLastCreditNoteDateField = "cr07a_fechaultimanc";
    private const string ReconciliationBillingCreditNoteLogField = "cr07a_ncsynclog";
    private const string ReconciliationBillingBeforeVatField = "cr07a_facturaantesdeiva";
    private const string ReconciliationBillingTaxValueField = "cr07a_impuestovalor";
    private const string ReconciliationBillingRequiredTaxField = "cr07a_impuesto";
    private const string ReconciliationBillingLegacyNitField = "cr07a_nit";

    public async Task<IReadOnlyList<ReconciliationDataverseBillingRow>> GetFinancialReconciliationBillingRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("El periodo de conciliacion de facturacion no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var select = BuildFinancialReconciliationBillingSelectClause(metadata, attributes);
        var filter = BuildBillingDateFilter(
            _dashboardBillingEmissionDateField,
            _dashboardBillingEmissionDateFieldKind,
            startInclusive,
            endExclusive);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={_dashboardBillingEmissionDateField} asc";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);

        return items
            .Select(item => new
            {
                Item = item,
                Row = ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField)
            })
            .Where(static value => value.Row is not null)
            .GroupBy(static value => value.Row!.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Select(value =>
            {
                var row = value.Row!;
                return BuildFinancialReconciliationBillingRow(value.Item, row, attributes);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ReconciliationDataverseCreditNoteRow>> GetFinancialReconciliationCreditNoteRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("El periodo de conciliacion de notas credito no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ReconciliationCreditNoteLogicalName,
            ReconciliationCreditNoteSetName,
            ReconciliationCreditNoteIdField,
            ReconciliationCreditNotePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var select = BuildCreditNoteSelectClause(metadata, attributes);
        var startText = startInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endText = endExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var filter = $"{ReconciliationCreditNoteDateField} ge '{EscapeOdataLiteral(startText)}' and {ReconciliationCreditNoteDateField} lt '{EscapeOdataLiteral(endText)}'";
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={ReconciliationCreditNoteDateField} asc";

        List<JsonElement> items;
        try
        {
            items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible filtrar notas credito por fecha en Dataverse. Se consultaran sin filtro y se filtraran en memoria.");
            var fallbackUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$orderby={ReconciliationCreditNoteDateField} asc";
            items = await GetDataverseAppEntitiesAsync(fallbackUrl, ct, AddFormattedValueHeaders);
        }

        return items
            .Select(item => ParseCreditNoteRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, attributes))
            .Where(row => row is not null
                && row.Date.HasValue
                && row.Date.Value >= startInclusive
                && row.Date.Value < endExclusive)
            .Cast<ReconciliationDataverseCreditNoteRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    public async Task<IReadOnlyList<ReconciliationDataverseExpenseRow>> GetFinancialReconciliationExpenseRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("El periodo de conciliacion de gastos no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        if (string.IsNullOrWhiteSpace(fields.EmissionDateField.FieldName))
            throw new InvalidOperationException("No encontramos un campo de fecha de emision valido en la tabla de gastos.");

        var issuerNitField = ResolveTaxExpenseField(
            attributes,
            "cr07a_nitemisor",
            "cr07a_nitproveedor",
            "cr07a_identificacionemisor",
            "cr07a_identificacionproveedor",
            "cr07a_nit");
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            fields.InvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.PaymentValueField,
            fields.TotalField,
            fields.VatField,
            fields.IssuerNameField,
            issuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = BuildBillingDateFilter(
            fields.EmissionDateField.FieldName,
            fields.EmissionDateField.FieldKind,
            startInclusive,
            endExclusive);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={fields.EmissionDateField.FieldName} asc";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);

        return items
            .Select(item =>
            {
                var row = ParseTaxExpenseRow(item, metadata.PrimaryIdField, fields);
                if (row is null)
                    return null;

                return new ReconciliationDataverseExpenseRow
                {
                    RecordId = row.RecordId,
                    InvoiceNumber = row.InvoiceNumber,
                    IssuerName = row.IssuerName,
                    IssuerNit = ReadString(item, issuerNitField).Trim(),
                    RecipientName = row.RecipientName,
                    RecipientNit = row.RecipientNit,
                    EmissionDate = row.EmissionDate,
                    PaymentDate = row.PaymentDate,
                    Total = row.TotalValue,
                    Vat = row.VatValue,
                    PaymentValue = row.PaymentValue
                };
            })
            .Where(static row => row is not null)
            .Cast<ReconciliationDataverseExpenseRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    public async Task<FinancialReconciliationCorrectionResult> ApplyFinancialReconciliationBillingCorrectionsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        IReadOnlyList<ReconciliationDataverseBillingRow> dataverseBilling,
        IReadOnlyList<ReconciliationDataverseCreditNoteRow> dataverseCreditNotes,
        SiigoFinancialReconciliationData siigo,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("El periodo de conciliacion de facturacion no es valido.");

        var actions = new List<FinancialReconciliationCorrectionAction>();
        var billingMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _dashboardBillingTableLogicalName,
            _dashboardBillingTableSetName,
            _dashboardBillingIdField,
            _dashboardBillingPrimaryNameField,
            ct);
        var billingAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(billingMetadata.LogicalName, ct);
        var creditNoteMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ReconciliationCreditNoteLogicalName,
            ReconciliationCreditNoteSetName,
            ReconciliationCreditNoteIdField,
            ReconciliationCreditNotePrimaryNameField,
            ct);
        var creditNoteAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(creditNoteMetadata.LogicalName, ct);

        var activeInvoices = siigo.Invoices
            .Where(static invoice => !invoice.Annulled)
            .GroupBy(static invoice => FirstNonEmpty(invoice.Id, invoice.Name), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
        var billingIndex = BuildFinancialBillingIndex(dataverseBilling);
        var creditNotesBySiigoId = dataverseCreditNotes
            .Where(static row => !string.IsNullOrWhiteSpace(row.CreditNoteId))
            .GroupBy(static row => row.CreditNoteId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var siigoCreditNotesByInvoiceKey = BuildSiigoCreditNotesByInvoiceKey(siigo.CreditNotes, activeInvoices);

        foreach (var invoice in activeInvoices)
        {
            try
            {
                var match = FindBillingMatch(invoice, billingIndex);
                var relatedCreditNotes = GetRelatedSiigoCreditNotes(invoice, siigoCreditNotesByInvoiceKey);
                var action = match is null
                    ? await CreateBillingInvoiceFromSiigoAsync(
                        billingMetadata,
                        billingAttributes,
                        invoice,
                        relatedCreditNotes,
                        ct)
                    : await UpdateBillingInvoiceFromSiigoAsync(
                        billingMetadata,
                        billingAttributes,
                        match,
                        invoice,
                        relatedCreditNotes,
                        ct);

                if (action is not null)
                {
                    actions.Add(action);
                    if (string.Equals(action.Action, "Creada", StringComparison.OrdinalIgnoreCase))
                    {
                        var created = await FindBillingRecordForSiigoInvoiceAsync(
                            billingMetadata,
                            billingAttributes,
                            invoice.Id,
                            invoice.Name,
                            invoice.Prefix,
                            invoice.Number?.ToString(CultureInfo.InvariantCulture),
                            ct);
                        if (created is not null)
                            AddBillingIndexRecord(billingIndex, created);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "No fue posible corregir la factura {InvoiceName} en Dataverse.", invoice.Name);
                actions.Add(new FinancialReconciliationCorrectionAction
                {
                    Entity = "Factura",
                    Action = "Error",
                    Document = FirstNonEmpty(invoice.Name, invoice.Id),
                    NewTotal = invoice.Total,
                    NewVat = invoice.Vat,
                    Notes = ex.Message
                });
            }
        }

        foreach (var creditNote in siigo.CreditNotes)
        {
            try
            {
                creditNotesBySiigoId.TryGetValue(creditNote.Id, out var existing);
                existing ??= await FindCreditNoteBySiigoIdAsync(creditNoteMetadata, creditNoteAttributes, creditNote.Id, ct);
                var billingMatch = FindBillingMatchForCreditNote(creditNote, billingIndex)
                    ?? await FindBillingRecordForSiigoInvoiceAsync(
                        billingMetadata,
                        billingAttributes,
                        creditNote.InvoiceId,
                        creditNote.InvoiceName,
                        creditNote.InvoicePrefix,
                        creditNote.InvoiceNumber?.ToString(CultureInfo.InvariantCulture),
                        ct);
                var action = existing is null
                    ? await CreateCreditNoteFromSiigoAsync(
                        creditNoteMetadata,
                        creditNoteAttributes,
                        creditNote,
                        billingMatch,
                        ct)
                    : await UpdateCreditNoteFromSiigoAsync(
                        creditNoteMetadata,
                        creditNoteAttributes,
                        existing,
                        creditNote,
                        billingMatch,
                        ct);

                if (action is not null)
                    actions.Add(action);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "No fue posible corregir la nota credito {CreditNoteName} en Dataverse.", creditNote.Name);
                actions.Add(new FinancialReconciliationCorrectionAction
                {
                    Entity = "NC",
                    Action = "Error",
                    Document = FirstNonEmpty(creditNote.Name, creditNote.Id),
                    NewTotal = creditNote.Total,
                    NewVat = creditNote.Vat,
                    Notes = ex.Message
                });
            }
        }

        return new FinancialReconciliationCorrectionResult
        {
            Actions = actions
        };
    }

    private string BuildFinancialReconciliationBillingSelectClause(RhEntityMetadata metadata, ISet<string> attributes)
    {
        var fields = BuildBillingSelectClause(metadata)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(BuildOptionalSelect(attributes,
                    ReconciliationBillingSiigoInvoiceIdField,
                    ReconciliationBillingSiigoInvoiceNameField,
                    ReconciliationBillingInvoiceCodeField,
                    ReconciliationBillingInvoicePrefixField,
                    ReconciliationBillingOriginalValueField,
                    ReconciliationBillingCreditNotesValueField,
                    ReconciliationBillingAdjustedValueField,
                    ReconciliationBillingBeforeVatField,
                    ReconciliationBillingTaxValueField,
                    ReconciliationBillingRequiredTaxField,
                    ReconciliationBillingLegacyNitField)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.Join(",", fields.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildCreditNoteSelectClause(RhEntityMetadata metadata, ISet<string> attributes)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            ReconciliationCreditNoteSiigoIdField,
            ReconciliationCreditNoteSiigoNameField,
            ReconciliationCreditNoteSiigoNumberField,
            ReconciliationCreditNoteInvoiceIdField,
            ReconciliationCreditNoteInvoiceNameField,
            ReconciliationCreditNoteInvoiceNumberField,
            ReconciliationCreditNoteInvoicePrefixField,
            ReconciliationCreditNoteDateField,
            ReconciliationCreditNoteTotalField,
            ReconciliationCreditNoteVatField,
            ReconciliationCreditNoteCustomerIdentificationField,
            ReconciliationCreditNoteFacturacionIdField,
            ReconciliationCreditNoteMatchByField,
            ReconciliationCreditNoteProcessedField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field)
            && (string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                || attributes.Contains(field)))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildOptionalSelect(ISet<string> attributes, params string[] fields)
    {
        return string.Join(",", fields
            .Where(field => !string.IsNullOrWhiteSpace(field) && attributes.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static ReconciliationDataverseCreditNoteRow? ParseCreditNoteRecord(
        JsonElement item,
        string primaryIdField,
        string primaryNameField,
        ISet<string> attributes)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, primaryIdField),
            ReadString(item, ReconciliationCreditNoteIdField),
            ReadString(item, ReconciliationCreditNoteSiigoIdField));

        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new ReconciliationDataverseCreditNoteRow
        {
            RecordId = recordId.Trim(),
            CreditNoteId = ReadString(item, ReconciliationCreditNoteSiigoIdField).Trim(),
            CreditNoteName = FirstNonEmpty(
                ReadString(item, ReconciliationCreditNoteSiigoNameField),
                ReadString(item, primaryNameField),
                ReadString(item, ReconciliationCreditNotePrimaryNameField),
                recordId),
            CreditNoteNumber = ReadLong(item, ReconciliationCreditNoteSiigoNumberField),
            Date = ReadDateOnlyFromString(item, ReconciliationCreditNoteDateField),
            InvoiceId = ReadString(item, ReconciliationCreditNoteInvoiceIdField).Trim(),
            InvoiceName = ReadString(item, ReconciliationCreditNoteInvoiceNameField).Trim(),
            InvoicePrefix = ReadString(item, ReconciliationCreditNoteInvoicePrefixField).Trim(),
            InvoiceNumber = ReadLong(item, ReconciliationCreditNoteInvoiceNumberField),
            CustomerIdentification = ReadString(item, ReconciliationCreditNoteCustomerIdentificationField).Trim(),
            Total = RoundCurrency(ReadDecimal(item, ReconciliationCreditNoteTotalField) ?? 0m),
            Vat = attributes.Contains(ReconciliationCreditNoteVatField)
                ? RoundCurrency(ReadDecimal(item, ReconciliationCreditNoteVatField) ?? 0m)
                : 0m,
            FacturacionDataverseId = ReadString(item, ReconciliationCreditNoteFacturacionIdField).Trim(),
            MatchBy = ReadString(item, ReconciliationCreditNoteMatchByField).Trim(),
            Processed = ReadBool(item, ReconciliationCreditNoteProcessedField)
        };
    }

    private async Task<FinancialReconciliationCorrectionAction?> CreateBillingInvoiceFromSiigoAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        SiigoReconciliationInvoice invoice,
        IReadOnlyList<SiigoReconciliationCreditNote> relatedCreditNotes,
        CancellationToken ct)
    {
        var payload = BuildBillingInvoiceCorrectionPayload(metadata, attributes, invoice, relatedCreditNotes, current: null);
        var body = await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}",
            "POST",
            payload,
            ct,
            AddFinancialReturnRepresentationHeaders);
        var recordId = TryReadRecordId(body, metadata.PrimaryIdField);

        return new FinancialReconciliationCorrectionAction
        {
            Entity = "Factura",
            Action = "Creada",
            Document = FirstNonEmpty(invoice.Name, invoice.Id),
            RecordId = recordId,
            NewTotal = invoice.Total,
            NewVat = invoice.Vat,
            Notes = "No existia en Dataverse; se creo con base Siigo."
        };
    }

    private async Task<FinancialReconciliationCorrectionAction?> UpdateBillingInvoiceFromSiigoAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        ReconciliationDataverseBillingRow current,
        SiigoReconciliationInvoice invoice,
        IReadOnlyList<SiigoReconciliationCreditNote> relatedCreditNotes,
        CancellationToken ct)
    {
        var payload = BuildBillingInvoiceCorrectionPayload(metadata, attributes, invoice, relatedCreditNotes, current);
        if (payload.Count == 0)
            return null;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({current.RecordId})",
            "PATCH",
            payload,
            ct);

        return new FinancialReconciliationCorrectionAction
        {
            Entity = "Factura",
            Action = "Actualizada",
            Document = FirstNonEmpty(invoice.Name, current.InvoiceNumber, invoice.Id),
            RecordId = current.RecordId,
            PreviousTotal = current.Total,
            NewTotal = invoice.Total,
            PreviousVat = current.Vat,
            NewVat = invoice.Vat,
            Notes = BuildUpdateNotes(payload.Keys)
        };
    }

    private Dictionary<string, object?> BuildBillingInvoiceCorrectionPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        SiigoReconciliationInvoice invoice,
        IReadOnlyList<SiigoReconciliationCreditNote> relatedCreditNotes,
        ReconciliationDataverseBillingRow? current)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var invoiceName = FirstNonEmpty(invoice.Name, BuildInvoiceName(invoice.Prefix, invoice.Number), invoice.Id);
        var vatPercent = GuessVatPercent(invoice.Total, invoice.Vat);
        var creditNoteTotal = RoundCurrency(relatedCreditNotes.Sum(static row => row.Total));
        var adjustedTotal = Math.Max(0m, RoundCurrency(invoice.Total - creditNoteTotal));
        var shouldCreate = current is null;
        var hasVatDifference = shouldCreate || HasVatDifference(current?.Vat ?? 0m, invoice.Vat);

        SetIfDifferent(payload, attributes, metadata.PrimaryNameField, current?.InvoiceNumber, invoiceName, force: shouldCreate);
        SetIfDifferent(payload, attributes, _dashboardBillingInvoiceNumberField, current?.InvoiceNumber, invoiceName, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationBillingInvoicePrefixField, current?.InvoicePrefix, invoice.Prefix, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationBillingInvoiceCodeField, current?.InvoiceCode, invoice.Number?.ToString(CultureInfo.InvariantCulture), force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationBillingSiigoInvoiceIdField, current?.SiigoInvoiceId, invoice.Id, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationBillingSiigoInvoiceNameField, current?.SiigoInvoiceName, invoice.Name, force: shouldCreate);
        SetIfDifferent(payload, attributes, _dashboardBillingCompanyTaxIdField, current?.CompanyTaxId, invoice.CustomerIdentification, force: shouldCreate);
        SetIfDifferent(payload, attributes, _dashboardBillingEmissionDateField, current?.EmissionDate, invoice.Date, force: shouldCreate);
        SetCurrencyIfDifferent(payload, attributes, _dashboardBillingTotalField, current?.Total, invoice.Total, force: shouldCreate);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationBillingBeforeVatField, null, RoundCurrency(invoice.Total - invoice.Vat), force: hasVatDifference);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationBillingTaxValueField, current?.Vat, invoice.Vat, force: hasVatDifference);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationBillingOriginalValueField, null, invoice.Total, force: shouldCreate);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationBillingCreditNotesValueField, null, creditNoteTotal, force: shouldCreate || creditNoteTotal > 0m);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationBillingAdjustedValueField, null, adjustedTotal, force: shouldCreate || creditNoteTotal > 0m);
        SetIfDifferent(payload, attributes, ReconciliationBillingCreditNotesCountField, (int?)null, relatedCreditNotes.Count, force: shouldCreate || relatedCreditNotes.Count > 0);
        SetIfDifferent(payload, attributes, ReconciliationBillingLastCreditNoteIdField, null, relatedCreditNotes.LastOrDefault()?.Id, force: relatedCreditNotes.Count > 0);
        SetIfDifferent(payload, attributes, ReconciliationBillingLastCreditNoteDateField, null, relatedCreditNotes.LastOrDefault()?.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: relatedCreditNotes.Count > 0);
        SetIfDifferent(payload, attributes, ReconciliationBillingCreditNoteLogField, null, BuildCreditNoteSyncLog(invoice, creditNoteTotal, adjustedTotal), force: shouldCreate || creditNoteTotal > 0m);
        SetIfDifferent(payload, attributes, _dashboardBillingVatPercentField, current?.VatPercent, vatPercent, force: hasVatDifference);
        SetIfDifferent(payload, attributes, ReconciliationBillingRequiredTaxField, (int?)null, Convert.ToInt32(vatPercent, CultureInfo.InvariantCulture), force: shouldCreate);

        var customerNit = TryParsePositiveInt(ExtractDigits(invoice.CustomerIdentification));
        if (customerNit.HasValue)
            SetIfDifferent(payload, attributes, ReconciliationBillingLegacyNitField, (int?)null, customerNit.Value, force: shouldCreate);

        return payload;
    }

    private async Task<FinancialReconciliationCorrectionAction?> CreateCreditNoteFromSiigoAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        SiigoReconciliationCreditNote creditNote,
        ReconciliationDataverseBillingRow? billingMatch,
        CancellationToken ct)
    {
        var payload = BuildCreditNoteCorrectionPayload(metadata, attributes, creditNote, billingMatch, current: null);
        var body = await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}",
            "POST",
            payload,
            ct,
            AddFinancialReturnRepresentationHeaders);
        var recordId = TryReadRecordId(body, metadata.PrimaryIdField);

        return new FinancialReconciliationCorrectionAction
        {
            Entity = "NC",
            Action = "Creada",
            Document = FirstNonEmpty(creditNote.Name, creditNote.Id),
            RecordId = recordId,
            NewTotal = creditNote.Total,
            NewVat = creditNote.Vat,
            Notes = billingMatch is null
                ? "Se creo la NC, pero no se encontro la factura afectada en Dataverse."
                : $"Se creo la NC y se cruzo con {billingMatch.InvoiceNumber}."
        };
    }

    private async Task<FinancialReconciliationCorrectionAction?> UpdateCreditNoteFromSiigoAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        ReconciliationDataverseCreditNoteRow current,
        SiigoReconciliationCreditNote creditNote,
        ReconciliationDataverseBillingRow? billingMatch,
        CancellationToken ct)
    {
        var payload = BuildCreditNoteCorrectionPayload(metadata, attributes, creditNote, billingMatch, current);
        if (payload.Count == 0)
            return null;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({current.RecordId})",
            "PATCH",
            payload,
            ct);

        return new FinancialReconciliationCorrectionAction
        {
            Entity = "NC",
            Action = "Actualizada",
            Document = FirstNonEmpty(creditNote.Name, current.CreditNoteName, creditNote.Id),
            RecordId = current.RecordId,
            PreviousTotal = current.Total,
            NewTotal = creditNote.Total,
            PreviousVat = current.Vat,
            NewVat = creditNote.Vat,
            Notes = BuildUpdateNotes(payload.Keys)
        };
    }

    private Dictionary<string, object?> BuildCreditNoteCorrectionPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        SiigoReconciliationCreditNote creditNote,
        ReconciliationDataverseBillingRow? billingMatch,
        ReconciliationDataverseCreditNoteRow? current)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var shouldCreate = current is null;
        var creditNoteName = FirstNonEmpty(creditNote.Name, creditNote.Id);

        SetIfDifferent(payload, attributes, metadata.PrimaryNameField, current?.CreditNoteName, creditNoteName, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteSiigoIdField, current?.CreditNoteId, creditNote.Id, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteSiigoNameField, current?.CreditNoteName, creditNote.Name, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteSiigoNumberField, current?.CreditNoteNumber, creditNote.Number, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteInvoiceIdField, current?.InvoiceId, creditNote.InvoiceId, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteInvoiceNameField, current?.InvoiceName, creditNote.InvoiceName, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteInvoicePrefixField, current?.InvoicePrefix, creditNote.InvoicePrefix, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteInvoiceNumberField, current?.InvoiceNumber, creditNote.InvoiceNumber, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteDateField, current?.Date, creditNote.Date, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteCreatedField, null, creditNote.CreatedAt?.ToString("O", CultureInfo.InvariantCulture), force: shouldCreate);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationCreditNoteTotalField, current?.Total, creditNote.Total, force: shouldCreate);
        SetCurrencyIfDifferent(payload, attributes, ReconciliationCreditNoteVatField, current?.Vat, creditNote.Vat, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteCustomerIdentificationField, current?.CustomerIdentification, creditNote.CustomerIdentification, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteCustomerIdField, null, creditNote.CustomerId, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteStampStatusField, null, creditNote.StampStatus, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteCudeField, null, creditNote.Cude, force: shouldCreate);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteFacturacionIdField, current?.FacturacionDataverseId, billingMatch?.RecordId, force: billingMatch is not null);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteMatchByField, current?.MatchBy, ResolveCreditNoteMatchBy(creditNote, billingMatch), force: billingMatch is not null);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteProcessedField, current?.Processed, billingMatch is not null, force: shouldCreate || current?.Processed != (billingMatch is not null));
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteLogField, null, BuildCreditNoteProcessingLog(creditNote, billingMatch), force: true);
        SetIfDifferent(payload, attributes, ReconciliationCreditNoteRawJsonField, null, Truncate(creditNote.RawJson, 100000), force: shouldCreate);

        return payload;
    }

    private async Task<ReconciliationDataverseCreditNoteRow?> FindCreditNoteBySiigoIdAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string siigoCreditNoteId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siigoCreditNoteId) || !attributes.Contains(ReconciliationCreditNoteSiigoIdField))
            return null;

        var select = BuildCreditNoteSelectClause(metadata, attributes);
        var filter = $"{ReconciliationCreditNoteSiigoIdField} eq '{EscapeOdataLiteral(siigoCreditNoteId.Trim())}'";
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(item => ParseCreditNoteRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField, attributes))
            .FirstOrDefault(static row => row is not null);
    }

    private async Task<ReconciliationDataverseBillingRow?> FindBillingRecordForSiigoInvoiceAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string? siigoInvoiceId,
        string? invoiceName,
        string? invoicePrefix,
        string? invoiceNumber,
        CancellationToken ct)
    {
        var select = BuildFinancialReconciliationBillingSelectClause(metadata, attributes);
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(siigoInvoiceId) && attributes.Contains(ReconciliationBillingSiigoInvoiceIdField))
            filters.Add($"{ReconciliationBillingSiigoInvoiceIdField} eq '{EscapeOdataLiteral(siigoInvoiceId.Trim())}'");
        if (!string.IsNullOrWhiteSpace(invoiceName))
            filters.Add($"{_dashboardBillingInvoiceNumberField} eq '{EscapeOdataLiteral(invoiceName.Trim())}'");
        if (!string.IsNullOrWhiteSpace(invoicePrefix)
            && !string.IsNullOrWhiteSpace(invoiceNumber)
            && attributes.Contains(ReconciliationBillingInvoicePrefixField)
            && attributes.Contains(ReconciliationBillingInvoiceCodeField))
        {
            filters.Add($"{ReconciliationBillingInvoicePrefixField} eq '{EscapeOdataLiteral(invoicePrefix.Trim())}' and {ReconciliationBillingInvoiceCodeField} eq '{EscapeOdataLiteral(invoiceNumber.Trim())}'");
        }

        foreach (var filter in filters)
        {
            var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
            var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
            var parsed = rows
                .Select(item =>
                {
                    var row = ParseBillingRecord(item, metadata.PrimaryIdField, metadata.PrimaryNameField);
                    if (row is null)
                        return null;

                    return BuildFinancialReconciliationBillingRow(item, row, attributes);
                })
                .FirstOrDefault(static row => row is not null);

            if (parsed is not null)
                return parsed;
        }

        return null;
    }

    private static ReconciliationDataverseBillingRow BuildFinancialReconciliationBillingRow(
        JsonElement item,
        BillingRecordRow row,
        ISet<string> attributes)
    {
        return new ReconciliationDataverseBillingRow
        {
            RecordId = row.RecordId,
            InvoiceNumber = row.InvoiceNumber,
            InvoicePrefix = ReadString(item, ReconciliationBillingInvoicePrefixField).Trim(),
            InvoiceCode = ReadString(item, ReconciliationBillingInvoiceCodeField).Trim(),
            SiigoInvoiceId = ReadString(item, ReconciliationBillingSiigoInvoiceIdField).Trim(),
            SiigoInvoiceName = ReadString(item, ReconciliationBillingSiigoInvoiceNameField).Trim(),
            ClientId = row.ClientId,
            ClientName = row.ClientName,
            CompanyTaxId = row.CompanyTaxId,
            EmissionDate = row.EmissionDate,
            Total = row.TotalInvoice,
            Vat = ResolveFinancialReconciliationBillingVat(item, attributes, row.VatValue),
            VatPercent = row.VatPercent == 0 ? null : row.VatPercent
        };
    }

    private static decimal ResolveFinancialReconciliationBillingVat(
        JsonElement item,
        ISet<string> attributes,
        decimal dashboardVat)
    {
        if (attributes.Contains(ReconciliationBillingTaxValueField))
        {
            var taxValue = ReadDecimal(item, ReconciliationBillingTaxValueField);
            if (taxValue.HasValue && Math.Abs(RoundCurrency(taxValue.Value)) > 0.01m)
                return RoundCurrency(taxValue.Value);
        }

        return dashboardVat;
    }

    private static FinancialBillingIndex BuildFinancialBillingIndex(IEnumerable<ReconciliationDataverseBillingRow> rows)
    {
        var index = new FinancialBillingIndex();
        foreach (var row in rows)
            AddBillingIndexRecord(index, row);

        return index;
    }

    private static void AddBillingIndexRecord(FinancialBillingIndex index, ReconciliationDataverseBillingRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.SiigoInvoiceId))
            index.BySiigoId[row.SiigoInvoiceId.Trim()] = row;
        if (!string.IsNullOrWhiteSpace(row.InvoiceNumber))
            index.ByName[NormalizeDocumentKey(row.InvoiceNumber)] = row;
        if (!string.IsNullOrWhiteSpace(row.SiigoInvoiceName))
            index.ByName[NormalizeDocumentKey(row.SiigoInvoiceName)] = row;
        if (!string.IsNullOrWhiteSpace(row.InvoicePrefix) && !string.IsNullOrWhiteSpace(row.InvoiceCode))
            index.ByPrefixAndNumber[$"{NormalizeDocumentKey(row.InvoicePrefix)}:{NormalizeDocumentKey(row.InvoiceCode)}"] = row;
    }

    private static ReconciliationDataverseBillingRow? FindBillingMatch(SiigoReconciliationInvoice invoice, FinancialBillingIndex index)
    {
        if (!string.IsNullOrWhiteSpace(invoice.Id) && index.BySiigoId.TryGetValue(invoice.Id.Trim(), out var byId))
            return byId;

        var nameKey = NormalizeDocumentKey(invoice.Name);
        if (!string.IsNullOrWhiteSpace(nameKey) && index.ByName.TryGetValue(nameKey, out var byName))
            return byName;

        var prefixKey = BuildPrefixNumberKey(invoice.Prefix, invoice.Number?.ToString(CultureInfo.InvariantCulture));
        return !string.IsNullOrWhiteSpace(prefixKey) && index.ByPrefixAndNumber.TryGetValue(prefixKey, out var byPrefix)
            ? byPrefix
            : null;
    }

    private static ReconciliationDataverseBillingRow? FindBillingMatchForCreditNote(SiigoReconciliationCreditNote creditNote, FinancialBillingIndex index)
    {
        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceId) && index.BySiigoId.TryGetValue(creditNote.InvoiceId.Trim(), out var byId))
            return byId;

        var nameKey = NormalizeDocumentKey(creditNote.InvoiceName);
        if (!string.IsNullOrWhiteSpace(nameKey) && index.ByName.TryGetValue(nameKey, out var byName))
            return byName;

        var prefixKey = BuildPrefixNumberKey(creditNote.InvoicePrefix, creditNote.InvoiceNumber?.ToString(CultureInfo.InvariantCulture));
        return !string.IsNullOrWhiteSpace(prefixKey) && index.ByPrefixAndNumber.TryGetValue(prefixKey, out var byPrefix)
            ? byPrefix
            : null;
    }

    private static Dictionary<string, List<SiigoReconciliationCreditNote>> BuildSiigoCreditNotesByInvoiceKey(
        IEnumerable<SiigoReconciliationCreditNote> creditNotes,
        IEnumerable<SiigoReconciliationInvoice> invoices)
    {
        var invoiceKeyById = invoices
            .Where(static invoice => !string.IsNullOrWhiteSpace(invoice.Id))
            .GroupBy(static invoice => invoice.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => BuildSiigoInvoiceKey(group.First()), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<SiigoReconciliationCreditNote>>(StringComparer.OrdinalIgnoreCase);

        foreach (var creditNote in creditNotes)
        {
            var key = "";
            if (!string.IsNullOrWhiteSpace(creditNote.InvoiceId)
                && invoiceKeyById.TryGetValue(creditNote.InvoiceId.Trim(), out var invoiceKey))
            {
                key = invoiceKey;
            }
            else
            {
                key = BuildDocumentKey(creditNote.InvoiceName, "credit-note-invoice", creditNote.InvoiceId);
            }

            if (!result.TryGetValue(key, out var rows))
            {
                rows = new List<SiigoReconciliationCreditNote>();
                result[key] = rows;
            }

            rows.Add(creditNote);
        }

        return result;
    }

    private static IReadOnlyList<SiigoReconciliationCreditNote> GetRelatedSiigoCreditNotes(
        SiigoReconciliationInvoice invoice,
        IReadOnlyDictionary<string, List<SiigoReconciliationCreditNote>> creditNotesByInvoiceKey)
    {
        var key = BuildSiigoInvoiceKey(invoice);
        return creditNotesByInvoiceKey.TryGetValue(key, out var rows)
            ? rows
            : Array.Empty<SiigoReconciliationCreditNote>();
    }

    private static string BuildSiigoInvoiceKey(SiigoReconciliationInvoice invoice) =>
        BuildDocumentKey(invoice.Name, "siigo", invoice.Id);

    private static string BuildDocumentKey(string? documentNumber, string fallbackPrefix, string? fallbackValue)
    {
        var normalized = NormalizeDocumentKey(documentNumber);
        if (!string.IsNullOrWhiteSpace(normalized))
            return $"DOC:{normalized}";

        var fallback = NormalizeDocumentKey(fallbackValue);
        return string.IsNullOrWhiteSpace(fallback)
            ? $"{fallbackPrefix}:empty"
            : $"{fallbackPrefix}:{fallback}";
    }

    private static string BuildPrefixNumberKey(string? prefix, string? number)
    {
        var normalizedPrefix = NormalizeDocumentKey(prefix);
        var normalizedNumber = NormalizeDocumentKey(number);
        return string.IsNullOrWhiteSpace(normalizedPrefix) || string.IsNullOrWhiteSpace(normalizedNumber)
            ? ""
            : $"{normalizedPrefix}:{normalizedNumber}";
    }

    private static string BuildInvoiceName(string? prefix, long? number)
    {
        if (!number.HasValue)
            return "";

        return string.IsNullOrWhiteSpace(prefix)
            ? number.Value.ToString(CultureInfo.InvariantCulture)
            : $"{prefix.Trim()}-{number.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string NormalizeDocumentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    private static void SetIfDifferent<T>(
        IDictionary<string, object?> payload,
        ISet<string> attributes,
        string field,
        T? current,
        T? value,
        bool force = false)
    {
        if (string.IsNullOrWhiteSpace(field) || !attributes.Contains(field))
            return;

        if (value is null)
        {
            if (force)
                payload[field] = null;
            return;
        }

        if (!force && current is null)
            return;

        if (!force && ValuesEqual(current, value))
            return;

        payload[field] = value is DateOnly date
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;
    }

    private static void SetCurrencyIfDifferent(
        IDictionary<string, object?> payload,
        ISet<string> attributes,
        string field,
        decimal? current,
        decimal value,
        bool force = false)
    {
        if (string.IsNullOrWhiteSpace(field) || !attributes.Contains(field))
            return;

        var roundedValue = RoundCurrency(value);
        if (!force && !current.HasValue)
            return;

        if (!force && current.HasValue && !HasVatDifference(current.Value, roundedValue))
            return;

        payload[field] = roundedValue;
    }

    private static bool ValuesEqual<T>(T? left, T? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;

        if (left is decimal leftDecimal && right is decimal rightDecimal)
            return !HasVatDifference(leftDecimal, rightDecimal);

        return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVatDifference(decimal left, decimal right) =>
        Math.Abs(RoundCurrency(left) - RoundCurrency(right)) > 1m;

    private static decimal GuessVatPercent(decimal total, decimal vat)
    {
        if (vat <= 0m || total <= 0m)
            return 0m;

        var baseAmount = total - vat;
        if (baseAmount <= 0m)
            return 19m;

        var percent = Math.Round((vat / baseAmount) * 100m, 0, MidpointRounding.AwayFromZero);
        return percent is > 0m and <= 100m ? percent : 19m;
    }

    private static string BuildCreditNoteSyncLog(SiigoReconciliationInvoice invoice, decimal creditNoteTotal, decimal adjustedTotal) =>
        $"Conciliacion mensual {DateTimeOffset.Now:O}. Siigo={invoice.Total:0.00} NC={creditNoteTotal:0.00} Neto={adjustedTotal:0.00}.";

    private static string BuildCreditNoteProcessingLog(SiigoReconciliationCreditNote creditNote, ReconciliationDataverseBillingRow? billingMatch) =>
        billingMatch is null
            ? $"No se encontro factura Dataverse para {FirstNonEmpty(creditNote.InvoiceName, creditNote.InvoiceId)}."
            : $"NC {creditNote.Name} cruzada con factura {billingMatch.InvoiceNumber}.";

    private static string ResolveCreditNoteMatchBy(SiigoReconciliationCreditNote creditNote, ReconciliationDataverseBillingRow? billingMatch)
    {
        if (billingMatch is null)
            return "";

        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceId)
            && string.Equals(creditNote.InvoiceId, billingMatch.SiigoInvoiceId, StringComparison.OrdinalIgnoreCase))
            return "siigo_invoice_id";

        if (!string.IsNullOrWhiteSpace(creditNote.InvoiceName)
            && string.Equals(NormalizeDocumentKey(creditNote.InvoiceName), NormalizeDocumentKey(billingMatch.InvoiceNumber), StringComparison.OrdinalIgnoreCase))
            return "invoice_name";

        return "prefix_codigo";
    }

    private static string BuildUpdateNotes(IEnumerable<string> fields) =>
        $"Campos actualizados: {string.Join(", ", fields.OrderBy(static field => field, StringComparer.OrdinalIgnoreCase))}.";

    private static void AddFinancialReturnRepresentationHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "Prefer",
            $"return=representation, odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private static string TryReadRecordId(string json, string idField)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadString(doc.RootElement, idField).Trim();
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static long? ReadLong(JsonElement item, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName) || !item.TryGetProperty(fieldName, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static DateOnly? ReadDateOnlyFromString(JsonElement item, string fieldName)
    {
        var text = ReadString(item, fieldName).Trim();
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return null;
    }

    private static int? TryParsePositiveInt(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;

        return null;
    }

    private static string ExtractDigits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private sealed class FinancialBillingIndex
    {
        public Dictionary<string, ReconciliationDataverseBillingRow> BySiigoId { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ReconciliationDataverseBillingRow> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ReconciliationDataverseBillingRow> ByPrefixAndNumber { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<RhEntityMetadata> ResolveFinancialReconciliationEntityMetadataAppAsync(
        string logicalName,
        string fallbackEntitySetName,
        string fallbackPrimaryIdField,
        string fallbackPrimaryNameField,
        CancellationToken ct)
    {
        var normalizedLogicalName = logicalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedLogicalName))
            throw new InvalidOperationException("La entidad de conciliacion financiera no esta configurada.");

        if (_rhEntityMetadataCache.TryGetValue(normalizedLogicalName, out var cached))
            return cached;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(normalizedLogicalName)}')" +
                "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);

            using var doc = JsonDocument.Parse(json);
            var metadata = new RhEntityMetadata
            {
                LogicalName = normalizedLogicalName,
                EntitySetName = FirstNonEmpty(ReadString(doc.RootElement, "EntitySetName"), fallbackEntitySetName),
                PrimaryIdField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryIdAttribute"), fallbackPrimaryIdField),
                PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), fallbackPrimaryNameField)
            };

            if (string.IsNullOrWhiteSpace(metadata.EntitySetName) || string.IsNullOrWhiteSpace(metadata.PrimaryIdField))
                throw new InvalidOperationException($"No fue posible resolver la metadata base de la entidad {normalizedLogicalName}.");

            _entityPrimaryNameFieldCache[normalizedLogicalName] = metadata.PrimaryNameField;
            _rhEntityMetadataCache[normalizedLogicalName] = metadata;
            return metadata;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver metadata app-only para {LogicalName}. Se usaran valores de respaldo.", normalizedLogicalName);

            if (string.IsNullOrWhiteSpace(fallbackEntitySetName) || string.IsNullOrWhiteSpace(fallbackPrimaryIdField))
                throw;

            var fallback = new RhEntityMetadata
            {
                LogicalName = normalizedLogicalName,
                EntitySetName = fallbackEntitySetName,
                PrimaryIdField = fallbackPrimaryIdField,
                PrimaryNameField = fallbackPrimaryNameField
            };

            _rhEntityMetadataCache[normalizedLogicalName] = fallback;
            return fallback;
        }
    }

    private async Task<HashSet<string>> GetFinancialReconciliationAttributeNamesAppAsync(
        string entityLogicalName,
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
                var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct);
                cached = items
                    .Select(static item => ReadString(item, "LogicalName").Trim())
                    .Where(static field => !string.IsNullOrWhiteSpace(field))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "No fue posible consultar atributos app-only de {EntityLogicalName}. Se usaran los campos configurados.",
                    cacheKey);
                cached = Array.Empty<string>();
            }

            _dashboardEntityAttributeNamesCache[cacheKey] = cached;
        }

        return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
    }
}
