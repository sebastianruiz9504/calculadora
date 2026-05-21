using System.Globalization;
using System.Net;
using System.Text.Json;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string AccountingTemplateLogicalName = "cr07a_plantillacontablegasto";
    private const string AccountingTemplateSetName = "cr07a_plantillacontablegastos";
    private const string AccountingTemplateIdField = "cr07a_plantillacontablegastoid";
    private const string AccountingTemplatePrimaryNameField = "cr07a_name";
    private const string AccountingTemplatePriorityField = "cr07a_prioridad";
    private const string AccountingTemplateCategoryValueField = "cr07a_categoriavalor";
    private const string AccountingTemplateCategoryNameField = "cr07a_categorianombre";
    private const string AccountingTemplateIssuerNitField = "cr07a_nitemisor";
    private const string AccountingTemplateTextContainsField = "cr07a_textocontiene";
    private const string AccountingTemplateMovementTypeField = "cr07a_tipomovimiento";
    private const string AccountingTemplateActiveField = "cr07a_activa";
    private const string AccountingTemplateRequiresApprovalField = "cr07a_requiereaprobacion";
    private const string AccountingTemplateDescriptionField = "cr07a_descripcion";

    private const string AccountingTemplateLineLogicalName = "cr07a_lineaplantillacontablegasto";
    private const string AccountingTemplateLineSetName = "cr07a_lineaplantillacontablegastos";
    private const string AccountingTemplateLineIdField = "cr07a_lineaplantillacontablegastoid";
    private const string AccountingTemplateLinePrimaryNameField = "cr07a_name";
    private const string AccountingTemplateLineTemplateIdField = "cr07a_plantillaid";
    private const string AccountingTemplateLineTemplateNameField = "cr07a_plantillanombre";
    private const string AccountingTemplateLineOrderField = "cr07a_orden";
    private const string AccountingTemplateLineSideField = "cr07a_lado";
    private const string AccountingTemplateLineAccountCodeField = "cr07a_cuentacodigo";
    private const string AccountingTemplateLineAccountNameField = "cr07a_cuentanombre";
    private const string AccountingTemplateLineFormulaField = "cr07a_formula";
    private const string AccountingTemplateLinePercentageField = "cr07a_porcentaje";
    private const string AccountingTemplateLineConstantValueField = "cr07a_valorconstante";
    private const string AccountingTemplateLineDescriptionField = "cr07a_descripcion";
    private const string AccountingTemplateLineActiveField = "cr07a_activa";

    private const string GeneratedAccountingLineLogicalName = "cr07a_lineacontablegasto";
    private const string GeneratedAccountingLineSetName = "cr07a_lineacontablegastos";
    private const string GeneratedAccountingLineIdField = "cr07a_lineacontablegastoid";
    private const string GeneratedAccountingLinePrimaryNameField = "cr07a_name";
    private const string GeneratedAccountingLineExpenseIdField = "cr07a_gastoid";
    private const string GeneratedAccountingLineExpenseNameField = "cr07a_gastonombre";
    private const string GeneratedAccountingLineTemplateIdField = "cr07a_plantillaid";
    private const string GeneratedAccountingLineTemplateNameField = "cr07a_plantillanombre";
    private const string GeneratedAccountingLineOrderField = "cr07a_orden";
    private const string GeneratedAccountingLineSideField = "cr07a_lado";
    private const string GeneratedAccountingLineAccountCodeField = "cr07a_cuentacodigo";
    private const string GeneratedAccountingLineAccountNameField = "cr07a_cuentanombre";
    private const string GeneratedAccountingLineFormulaField = "cr07a_formula";
    private const string GeneratedAccountingLineValueField = "cr07a_valor";
    private const string GeneratedAccountingLineStatusField = "cr07a_estado";
    private const string GeneratedAccountingLineReasonField = "cr07a_motivo";
    private const string GeneratedAccountingLineGenerationDateField = "cr07a_fechageneracion";
    private const string GeneratedAccountingLineSentToSiigoField = "cr07a_enviadoasiigo";

    public async Task<ExpenseAccountingTemplateApplyResultDto> ApplyExpenseAccountingTemplatesAsync(
        DateOnly startDate,
        DateOnly endDate,
        string movementType = "Compra",
        bool overwrite = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo para aplicar plantillas contables no es valido.");

        var expenseMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var expenseAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(expenseMetadata.LogicalName, ct);
        var expenseFields = ResolveTaxExpenseFieldMap(expenseMetadata, expenseAttributes);
        if (string.IsNullOrWhiteSpace(expenseFields.EmissionDateField.FieldName))
            throw new InvalidOperationException("No encontramos un campo de fecha para filtrar gastos.");

        var issuerNitField = ResolveTaxExpenseField(
            expenseAttributes,
            "cr07a_nitemisor",
            "cr07a_nitproveedor",
            "cr07a_identificacionemisor",
            "cr07a_identificacionproveedor",
            "cr07a_nit");
        var baseAmountField = ResolveTaxExpenseField(
            expenseAttributes,
            "cr07a_totalantesdeiva",
            "cr07a_subtotal",
            "cr07a_base",
            "cr07a_baseiva");

        var expenses = await GetExpenseAccountingTemplateExpenseRowsAsync(
            expenseMetadata,
            expenseAttributes,
            expenseFields,
            issuerNitField,
            baseAmountField,
            startDate,
            endDate.AddDays(1),
            ct);
        var templates = await GetExpenseAccountingTemplatesAsync(ct);
        var templateLines = await GetExpenseAccountingTemplateLinesAsync(ct);
        var linesByTemplate = templateLines
            .GroupBy(static line => line.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static line => line.Order).ToList(), StringComparer.OrdinalIgnoreCase);

        var generatedMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            GeneratedAccountingLineLogicalName,
            GeneratedAccountingLineSetName,
            GeneratedAccountingLineIdField,
            GeneratedAccountingLinePrimaryNameField,
            ct);
        var generatedAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(generatedMetadata.LogicalName, ct);
        generatedAttributes = BuildGeneratedAccountingLineAttributeSet(generatedMetadata, generatedAttributes);
        var existingLines = await GetExistingGeneratedAccountingLinesAsync(generatedMetadata, generatedAttributes, ct);
        var catalog = await GetExpenseAccountingAccountCatalogAsync(ct);

        var resultRows = new List<ExpenseAccountingTemplateAppliedRowDto>();
        var updated = 0;
        var alreadyHandled = 0;
        var noTemplate = 0;
        var invalidTemplate = 0;
        var generatedLineCount = 0;

        foreach (var expense in expenses)
        {
            ct.ThrowIfCancellationRequested();

            if (!overwrite && (!string.IsNullOrWhiteSpace(expense.AccountCode) || existingLines.ContainsKey(expense.RecordId)))
            {
                alreadyHandled++;
                continue;
            }

            if (overwrite && !dryRun && existingLines.TryGetValue(expense.RecordId, out var existingForExpense))
                await DeleteGeneratedAccountingLinesAsync(generatedMetadata, existingForExpense, ct);

            var match = FindBestExpenseAccountingTemplate(expense, templates, movementType);
            if (match is null)
            {
                noTemplate++;
                var noTemplateNotes = BuildNoAccountingTemplateReason(expense, movementType);
                if (!dryRun && await UpdateExpenseAccountingReviewStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, noTemplateNotes, ct))
                    updated++;

                resultRows.Add(BuildExpenseAccountingTemplateResultRow(expense, null, "SinPlantilla", noTemplateNotes, Array.Empty<ExpenseAccountingTemplateGeneratedLineDto>()));
                continue;
            }

            if (!linesByTemplate.TryGetValue(match.Template.RecordId, out var lines) || lines.Count == 0)
            {
                invalidTemplate++;
                var invalidTemplateNotes = $"La plantilla {match.Template.Name} no tiene lineas contables activas.";
                if (!dryRun && await UpdateExpenseAccountingReviewStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, invalidTemplateNotes, ct))
                    updated++;

                resultRows.Add(BuildExpenseAccountingTemplateResultRow(expense, match.Template, "PlantillaInvalida", invalidTemplateNotes, Array.Empty<ExpenseAccountingTemplateGeneratedLineDto>()));
                continue;
            }

            var evaluatedLines = EvaluateExpenseAccountingTemplateLines(expense, match.Template, lines, catalog, out var invalidReason);
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                invalidTemplate++;
                if (!dryRun && await UpdateExpenseAccountingReviewStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, invalidReason, ct))
                    updated++;

                resultRows.Add(BuildExpenseAccountingTemplateResultRow(expense, match.Template, "PlantillaInvalida", invalidReason, evaluatedLines));
                continue;
            }

            var debitTotal = RoundCurrency(evaluatedLines
                .Where(static line => IsDebitSide(line.Side))
                .Sum(static line => line.Value));
            var creditTotal = RoundCurrency(evaluatedLines
                .Where(static line => IsCreditSide(line.Side))
                .Sum(static line => line.Value));
            var isBalanced = Math.Abs(debitTotal - creditTotal) <= 1m;
            var status = isBalanced && !match.Template.RequiresApproval
                ? "PlantillaContable"
                : "PendienteRevision";
            var notes = isBalanced
                ? $"Plantilla {match.Template.Name} generada. Debito {debitTotal.ToString("N2", CultureInfo.InvariantCulture)} / credito {creditTotal.ToString("N2", CultureInfo.InvariantCulture)}."
                : $"Plantilla {match.Template.Name} no cuadra. Debito {debitTotal.ToString("N2", CultureInfo.InvariantCulture)} / credito {creditTotal.ToString("N2", CultureInfo.InvariantCulture)}.";
            if (match.Template.RequiresApproval)
                notes += " Requiere aprobacion antes de enviar a Siigo.";

            if (!dryRun)
            {
                foreach (var line in evaluatedLines)
                {
                    await CreateGeneratedAccountingLineAsync(
                        generatedMetadata,
                        generatedAttributes,
                        expense,
                        match.Template,
                        line,
                        status,
                        notes,
                        ct);
                    generatedLineCount++;
                }

                if (await UpdateExpenseAccountingTemplateStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, status, match.Confidence, notes, ct))
                    updated++;
            }
            else
            {
                generatedLineCount += evaluatedLines.Count;
            }

            resultRows.Add(BuildExpenseAccountingTemplateResultRow(expense, match.Template, status, notes, evaluatedLines));
        }

        return new ExpenseAccountingTemplateApplyResultDto
        {
            StartDate = startDate,
            EndDate = endDate,
            MovementType = movementType,
            DryRun = dryRun,
            Reviewed = expenses.Count,
            Updated = updated,
            AlreadyHandled = alreadyHandled,
            NoTemplate = noTemplate,
            InvalidTemplate = invalidTemplate,
            GeneratedLineCount = generatedLineCount,
            Rows = resultRows
        };
    }

    private async Task<IReadOnlyList<ExpenseAccountingTemplateExpenseRow>> GetExpenseAccountingTemplateExpenseRowsAsync(
        RhEntityMetadata metadata,
        IReadOnlySet<string> attributes,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        string baseAmountField,
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var textFields = ExpenseAccountingTextFieldCandidates
            .Where(field => IsDashboardDataverseFieldAvailable(field, attributes))
            .ToArray();
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.TotalField,
            baseAmountField,
            fields.VatField,
            fields.PaymentValueField,
            fields.ReteFuenteField,
            fields.ReteIcaField,
            fields.IssuerNameField,
            issuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            DashboardExpenseCategoryField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField
        }
        .Concat(textFields)
        .Where(field => !string.IsNullOrWhiteSpace(field)
            && (string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                || IsDashboardDataverseFieldAvailable(field, attributes)))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = BuildBillingDateFilter(
            fields.EmissionDateField.FieldName,
            fields.EmissionDateField.FieldKind,
            startInclusive,
            endExclusive);
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={fields.EmissionDateField.FieldName} asc";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        return rows
            .Select(row => ParseExpenseAccountingTemplateExpenseRow(row, metadata, fields, issuerNitField, baseAmountField, textFields))
            .Where(static row => row is not null)
            .Cast<ExpenseAccountingTemplateExpenseRow>()
            .ToList();
    }

    private static ExpenseAccountingTemplateExpenseRow? ParseExpenseAccountingTemplateExpenseRow(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        string baseAmountField,
        IReadOnlyList<string> textFields)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var categoryLabel = FirstNonEmpty(
            ReadString(item, $"{DashboardExpenseCategoryField}{FormattedValueAnnotationSuffix}"),
            ReadString(item, DashboardExpenseCategoryField));
        var textValues = textFields
            .Select(field => FirstNonEmpty(
                ReadString(item, $"{field}{FormattedValueAnnotationSuffix}"),
                ReadString(item, field)))
            .Concat(new[]
            {
                ReadString(item, metadata.PrimaryNameField),
                ReadString(item, fields.InvoiceNumberField),
                ReadString(item, fields.IssuerNameField),
                ReadString(item, issuerNitField),
                ReadString(item, fields.RecipientNameField),
                ReadString(item, fields.RecipientNitField),
                categoryLabel
            });

        return new ExpenseAccountingTemplateExpenseRow
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), ReadString(item, fields.InvoiceNumberField), recordId),
            InvoiceNumber = FirstNonEmpty(
                ReadString(item, $"{fields.InvoiceNumberField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, fields.InvoiceNumberField)),
            ProviderNit = ReadString(item, issuerNitField).Trim(),
            ProviderName = ReadString(item, fields.IssuerNameField).Trim(),
            RecipientNit = ReadString(item, fields.RecipientNitField).Trim(),
            RecipientName = ReadString(item, fields.RecipientNameField).Trim(),
            CategoryValue = ReadIntFlexible(item, DashboardExpenseCategoryField),
            CategoryName = categoryLabel,
            AccountCode = ReadString(item, ExpenseAccountCodeField).Trim(),
            AccountName = ReadString(item, ExpenseAccountNameField).Trim(),
            Total = RoundCurrency(ReadDecimal(item, fields.TotalField) ?? 0m),
            BaseAmount = RoundCurrency(ReadDecimal(item, baseAmountField) ?? 0m),
            Vat = RoundCurrency(ReadDecimal(item, fields.VatField) ?? 0m),
            PaymentValue = RoundCurrency(ReadDecimal(item, fields.PaymentValueField) ?? 0m),
            ReteFuente = RoundCurrency(ReadDecimal(item, fields.ReteFuenteField) ?? 0m),
            ReteIca = RoundCurrency(ReadDecimal(item, fields.ReteIcaField) ?? 0m),
            SearchText = NormalizeAccountingRuleText(string.Join(" ", textValues))
        };
    }

    private async Task<IReadOnlyList<ExpenseAccountingTemplateRow>> GetExpenseAccountingTemplatesAsync(CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountingTemplateLogicalName,
            AccountingTemplateSetName,
            AccountingTemplateIdField,
            AccountingTemplatePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildExpenseAccountingTemplateAttributeSet(metadata, attributes);
        var select = string.Join(",", new[] { metadata.PrimaryIdField }
            .Concat(BuildOptionalSelect(
                    attributes,
                    metadata.PrimaryNameField,
                    AccountingTemplatePriorityField,
                    AccountingTemplateCategoryValueField,
                    AccountingTemplateCategoryNameField,
                    AccountingTemplateIssuerNitField,
                    AccountingTemplateTextContainsField,
                    AccountingTemplateMovementTypeField,
                    AccountingTemplateActiveField,
                    AccountingTemplateRequiresApprovalField,
                    AccountingTemplateDescriptionField)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(row => ParseExpenseAccountingTemplateRow(row, metadata, attributes))
            .Where(static row => row is not null && row.Active)
            .Cast<ExpenseAccountingTemplateRow>()
            .OrderBy(static row => row.Priority <= 0 ? int.MaxValue : row.Priority)
            .ThenBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExpenseAccountingTemplateRow? ParseExpenseAccountingTemplateRow(
        JsonElement item,
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new ExpenseAccountingTemplateRow
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), recordId),
            Priority = ReadIntFlexible(item, AccountingTemplatePriorityField),
            CategoryValue = ReadIntFlexible(item, AccountingTemplateCategoryValueField),
            CategoryName = FirstNonEmpty(
                ReadString(item, $"{AccountingTemplateCategoryNameField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, AccountingTemplateCategoryNameField)),
            IssuerNit = ReadString(item, AccountingTemplateIssuerNitField).Trim(),
            TextContains = ReadString(item, AccountingTemplateTextContainsField).Trim(),
            MovementType = ReadString(item, AccountingTemplateMovementTypeField).Trim(),
            RequiresApproval = attributes.Contains(AccountingTemplateRequiresApprovalField)
                && item.TryGetProperty(AccountingTemplateRequiresApprovalField, out _)
                && ReadBool(item, AccountingTemplateRequiresApprovalField),
            Description = ReadString(item, AccountingTemplateDescriptionField).Trim(),
            Active = !attributes.Contains(AccountingTemplateActiveField)
                || !item.TryGetProperty(AccountingTemplateActiveField, out _)
                || ReadBool(item, AccountingTemplateActiveField)
        };
    }

    private async Task<IReadOnlyList<ExpenseAccountingTemplateLineRow>> GetExpenseAccountingTemplateLinesAsync(CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountingTemplateLineLogicalName,
            AccountingTemplateLineSetName,
            AccountingTemplateLineIdField,
            AccountingTemplateLinePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildExpenseAccountingTemplateLineAttributeSet(metadata, attributes);
        var select = string.Join(",", new[] { metadata.PrimaryIdField }
            .Concat(BuildOptionalSelect(
                    attributes,
                    metadata.PrimaryNameField,
                    AccountingTemplateLineTemplateIdField,
                    AccountingTemplateLineTemplateNameField,
                    AccountingTemplateLineOrderField,
                    AccountingTemplateLineSideField,
                    AccountingTemplateLineAccountCodeField,
                    AccountingTemplateLineAccountNameField,
                    AccountingTemplateLineFormulaField,
                    AccountingTemplateLinePercentageField,
                    AccountingTemplateLineConstantValueField,
                    AccountingTemplateLineDescriptionField,
                    AccountingTemplateLineActiveField)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(row => ParseExpenseAccountingTemplateLineRow(row, metadata, attributes))
            .Where(static row => row is not null && row.Active && !string.IsNullOrWhiteSpace(row.TemplateId))
            .Cast<ExpenseAccountingTemplateLineRow>()
            .OrderBy(static row => row.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Order)
            .ToList();
    }

    private static ExpenseAccountingTemplateLineRow? ParseExpenseAccountingTemplateLineRow(
        JsonElement item,
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new ExpenseAccountingTemplateLineRow
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), recordId),
            TemplateId = ReadString(item, AccountingTemplateLineTemplateIdField).Trim(),
            TemplateName = ReadString(item, AccountingTemplateLineTemplateNameField).Trim(),
            Order = ReadIntFlexible(item, AccountingTemplateLineOrderField),
            Side = FirstNonEmpty(
                ReadString(item, $"{AccountingTemplateLineSideField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, AccountingTemplateLineSideField)).Trim(),
            AccountCode = ReadString(item, AccountingTemplateLineAccountCodeField).Trim(),
            AccountName = ReadString(item, AccountingTemplateLineAccountNameField).Trim(),
            Formula = ReadString(item, AccountingTemplateLineFormulaField).Trim(),
            Percentage = ReadDecimal(item, AccountingTemplateLinePercentageField) ?? 0m,
            ConstantValue = ReadDecimal(item, AccountingTemplateLineConstantValueField) ?? 0m,
            Description = ReadString(item, AccountingTemplateLineDescriptionField).Trim(),
            Active = !attributes.Contains(AccountingTemplateLineActiveField)
                || !item.TryGetProperty(AccountingTemplateLineActiveField, out _)
                || ReadBool(item, AccountingTemplateLineActiveField)
        };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<ExistingGeneratedAccountingLineRow>>> GetExistingGeneratedAccountingLinesAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CancellationToken ct)
    {
        var select = string.Join(",", new[] { metadata.PrimaryIdField }
            .Concat(BuildOptionalSelect(attributes, GeneratedAccountingLineExpenseIdField)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);

        return rows
            .Select(row => new ExistingGeneratedAccountingLineRow
            {
                RecordId = ReadString(row, metadata.PrimaryIdField).Trim(),
                ExpenseId = ReadString(row, GeneratedAccountingLineExpenseIdField).Trim()
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.RecordId) && !string.IsNullOrWhiteSpace(row.ExpenseId))
            .GroupBy(static row => row.ExpenseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ExistingGeneratedAccountingLineRow>)group.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task DeleteGeneratedAccountingLinesAsync(
        RhEntityMetadata metadata,
        IReadOnlyList<ExistingGeneratedAccountingLineRow> rows,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            using var response = await CallDataverseAppResponseAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({row.RecordId})",
                "DELETE",
                ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                continue;

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Dataverse app error {StatusCode} {ReasonPhrase}. Body: {Body}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                body);
            throw new InvalidOperationException(BuildDataverseAppFailureMessage(response.StatusCode));
        }
    }

    private async Task CreateGeneratedAccountingLineAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        ExpenseAccountingTemplateExpenseRow expense,
        ExpenseAccountingTemplateRow template,
        ExpenseAccountingTemplateGeneratedLineDto line,
        string status,
        string reason,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var primaryName = TruncateAccountCatalogText($"{expense.InvoiceNumber} {line.Order} {line.AccountCode}".Trim(), 100);
        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, primaryName, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineExpenseIdField, null, expense.RecordId, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineExpenseNameField, null, TruncateAccountCatalogText(expense.Name, 100), force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineTemplateIdField, null, template.RecordId, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineTemplateNameField, null, TruncateAccountCatalogText(template.Name, 100), force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineOrderField, (int?)null, line.Order, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineSideField, null, line.Side, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineAccountCodeField, null, line.AccountCode, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineAccountNameField, null, line.AccountName, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineFormulaField, null, line.Formula, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineValueField, (decimal?)null, line.Value, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineReasonField, null, reason, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineGenerationDateField, (DateTimeOffset?)null, DateTimeOffset.UtcNow, force: true);
        SetAccountCatalogValue(payload, attributes, GeneratedAccountingLineSentToSiigoField, (bool?)null, false, force: true);

        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}",
            "POST",
            payload,
            ct);
    }

    private async Task<bool> UpdateExpenseAccountingTemplateStateAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string recordId,
        string status,
        decimal confidence,
        string reason,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, confidence, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, reason, force: true);
        if (payload.Count == 0)
            return false;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            payload,
            ct);
        return true;
    }

    private static IReadOnlyList<ExpenseAccountingTemplateGeneratedLineDto> EvaluateExpenseAccountingTemplateLines(
        ExpenseAccountingTemplateExpenseRow expense,
        ExpenseAccountingTemplateRow template,
        IReadOnlyList<ExpenseAccountingTemplateLineRow> lines,
        IReadOnlyDictionary<string, ExpenseAccountingAccountRow> catalog,
        out string invalidReason)
    {
        var evaluated = new List<ExpenseAccountingTemplateGeneratedLineDto>();
        foreach (var line in lines)
        {
            var value = EvaluateExpenseAccountingTemplateFormula(expense, line);
            if (value == 0m)
                continue;

            var side = NormalizeAccountingLineSide(line.Side);
            if (string.IsNullOrWhiteSpace(side))
            {
                invalidReason = $"La plantilla {template.Name} tiene una linea sin lado debito/credito valido.";
                return evaluated;
            }

            if (string.IsNullOrWhiteSpace(line.AccountCode)
                || !catalog.TryGetValue(line.AccountCode, out var account)
                || !account.Active)
            {
                invalidReason = $"La plantilla {template.Name} usa la cuenta {line.AccountCode}, pero no esta activa en el catalogo contable.";
                return evaluated;
            }

            evaluated.Add(new ExpenseAccountingTemplateGeneratedLineDto
            {
                Order = line.Order <= 0 ? evaluated.Count + 1 : line.Order,
                Side = side,
                AccountCode = line.AccountCode,
                AccountName = FirstNonEmpty(line.AccountName, account.Name, line.AccountCode),
                Formula = line.Formula,
                Value = value,
                Description = line.Description
            });
        }

        if (evaluated.Count == 0)
        {
            invalidReason = $"La plantilla {template.Name} no genero lineas con valor para este gasto.";
            return evaluated;
        }

        invalidReason = "";
        return evaluated;
    }

    private static decimal EvaluateExpenseAccountingTemplateFormula(
        ExpenseAccountingTemplateExpenseRow expense,
        ExpenseAccountingTemplateLineRow line)
    {
        var formula = NormalizeFormula(line.Formula);
        var value = formula switch
        {
            "total" => expense.Total,
            "base" => expense.BaseAmount,
            "subtotal" => expense.BaseAmount,
            "iva" => expense.Vat,
            "retefuente" => expense.ReteFuente,
            "retencionfuente" => expense.ReteFuente,
            "reteica" => expense.ReteIca,
            "valorpago" => expense.PaymentValue,
            "pago" => expense.PaymentValue,
            "constante" => line.ConstantValue,
            "totalporcentaje" => expense.Total * line.Percentage / 100m,
            "baseporcentaje" => expense.BaseAmount * line.Percentage / 100m,
            "ivaporcentaje" => expense.Vat * line.Percentage / 100m,
            "cero" => 0m,
            "pendienterevision" => 0m,
            _ => 0m
        };

        return RoundCurrency(Math.Abs(value));
    }

    private static ExpenseAccountingTemplateMatch? FindBestExpenseAccountingTemplate(
        ExpenseAccountingTemplateExpenseRow expense,
        IReadOnlyList<ExpenseAccountingTemplateRow> templates,
        string movementType)
    {
        return templates
            .Select(template => BuildExpenseAccountingTemplateMatch(expense, template, movementType))
            .Where(static match => match is not null)
            .Cast<ExpenseAccountingTemplateMatch>()
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Template.Priority <= 0 ? int.MaxValue : match.Template.Priority)
            .ThenBy(static match => match.Template.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static ExpenseAccountingTemplateMatch? BuildExpenseAccountingTemplateMatch(
        ExpenseAccountingTemplateExpenseRow expense,
        ExpenseAccountingTemplateRow template,
        string movementType)
    {
        var score = 0;
        var confidence = 70m;
        var hasCriterion = false;

        if (!string.IsNullOrWhiteSpace(template.MovementType))
        {
            if (!string.Equals(template.MovementType.Trim(), movementType?.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            score += 5;
            confidence += 5m;
        }

        if (!string.IsNullOrWhiteSpace(template.IssuerNit))
        {
            hasCriterion = true;
            var templateNit = ExtractDigits(template.IssuerNit);
            if (string.IsNullOrWhiteSpace(templateNit)
                || (!string.Equals(templateNit, ExtractDigits(expense.ProviderNit), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(templateNit, ExtractDigits(expense.RecipientNit), StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            score += 40;
            confidence += 20m;
        }

        if (template.CategoryValue > 0 || !string.IsNullOrWhiteSpace(template.CategoryName))
        {
            hasCriterion = true;
            var categoryMatches = template.CategoryValue > 0
                ? template.CategoryValue == expense.CategoryValue
                : string.Equals(
                    NormalizeAccountingRuleText(template.CategoryName),
                    NormalizeAccountingRuleText(expense.CategoryName),
                    StringComparison.OrdinalIgnoreCase);
            if (!categoryMatches)
                return null;

            score += 25;
            confidence += 15m;
        }

        if (!string.IsNullOrWhiteSpace(template.TextContains))
        {
            hasCriterion = true;
            if (!expense.SearchText.Contains(NormalizeAccountingRuleText(template.TextContains), StringComparison.OrdinalIgnoreCase))
                return null;

            score += 20;
            confidence += 10m;
        }

        if (!hasCriterion)
            return null;

        return new ExpenseAccountingTemplateMatch
        {
            Template = template,
            Score = score,
            Confidence = Math.Min(confidence, 100m)
        };
    }

    private static ExpenseAccountingTemplateAppliedRowDto BuildExpenseAccountingTemplateResultRow(
        ExpenseAccountingTemplateExpenseRow expense,
        ExpenseAccountingTemplateRow? template,
        string status,
        string notes,
        IReadOnlyList<ExpenseAccountingTemplateGeneratedLineDto> lines)
    {
        return new ExpenseAccountingTemplateAppliedRowDto
        {
            ExpenseId = expense.RecordId,
            ExpenseName = expense.Name,
            ProviderNit = FirstNonEmpty(expense.ProviderNit, expense.RecipientNit),
            ProviderName = FirstNonEmpty(expense.ProviderName, expense.RecipientName),
            Category = FirstNonEmpty(expense.CategoryName, expense.CategoryValue > 0 ? expense.CategoryValue.ToString(CultureInfo.InvariantCulture) : ""),
            TemplateId = template?.RecordId ?? "",
            TemplateName = template?.Name ?? "",
            Status = status,
            Notes = notes,
            DebitTotal = RoundCurrency(lines.Where(static line => IsDebitSide(line.Side)).Sum(static line => line.Value)),
            CreditTotal = RoundCurrency(lines.Where(static line => IsCreditSide(line.Side)).Sum(static line => line.Value)),
            Lines = lines
        };
    }

    private static string BuildNoAccountingTemplateReason(ExpenseAccountingTemplateExpenseRow expense, string movementType) =>
        $"Sin plantilla contable para movimiento {movementType}, categoria {FirstNonEmpty(expense.CategoryName, expense.CategoryValue.ToString(CultureInfo.InvariantCulture))}, proveedor {FirstNonEmpty(expense.ProviderNit, expense.ProviderName, "sin proveedor")}.";

    private static HashSet<string> BuildExpenseAccountingTemplateAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AccountingTemplatePriorityField,
            AccountingTemplateCategoryValueField,
            AccountingTemplateCategoryNameField,
            AccountingTemplateIssuerNitField,
            AccountingTemplateTextContainsField,
            AccountingTemplateMovementTypeField,
            AccountingTemplateActiveField,
            AccountingTemplateRequiresApprovalField,
            AccountingTemplateDescriptionField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildExpenseAccountingTemplateLineAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AccountingTemplateLineTemplateIdField,
            AccountingTemplateLineTemplateNameField,
            AccountingTemplateLineOrderField,
            AccountingTemplateLineSideField,
            AccountingTemplateLineAccountCodeField,
            AccountingTemplateLineAccountNameField,
            AccountingTemplateLineFormulaField,
            AccountingTemplateLinePercentageField,
            AccountingTemplateLineConstantValueField,
            AccountingTemplateLineDescriptionField,
            AccountingTemplateLineActiveField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildGeneratedAccountingLineAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            GeneratedAccountingLineExpenseIdField,
            GeneratedAccountingLineExpenseNameField,
            GeneratedAccountingLineTemplateIdField,
            GeneratedAccountingLineTemplateNameField,
            GeneratedAccountingLineOrderField,
            GeneratedAccountingLineSideField,
            GeneratedAccountingLineAccountCodeField,
            GeneratedAccountingLineAccountNameField,
            GeneratedAccountingLineFormulaField,
            GeneratedAccountingLineValueField,
            GeneratedAccountingLineStatusField,
            GeneratedAccountingLineReasonField,
            GeneratedAccountingLineGenerationDateField,
            GeneratedAccountingLineSentToSiigoField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFormula(string value) =>
        NormalizeAccountingRuleText(value)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);

    private static string NormalizeAccountingLineSide(string side)
    {
        var normalized = NormalizeFormula(side);
        if (normalized is "debito" or "debe" or "debit")
            return "Debito";

        if (normalized is "credito" or "haber" or "credit")
            return "Credito";

        return "";
    }

    private static bool IsDebitSide(string side) =>
        string.Equals(NormalizeAccountingLineSide(side), "Debito", StringComparison.OrdinalIgnoreCase);

    private static bool IsCreditSide(string side) =>
        string.Equals(NormalizeAccountingLineSide(side), "Credito", StringComparison.OrdinalIgnoreCase);

    private sealed class ExpenseAccountingTemplateExpenseRow
    {
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public string InvoiceNumber { get; init; } = "";
        public string ProviderNit { get; init; } = "";
        public string ProviderName { get; init; } = "";
        public string RecipientNit { get; init; } = "";
        public string RecipientName { get; init; } = "";
        public int CategoryValue { get; init; }
        public string CategoryName { get; init; } = "";
        public string AccountCode { get; init; } = "";
        public string AccountName { get; init; } = "";
        public decimal Total { get; init; }
        public decimal BaseAmount { get; init; }
        public decimal Vat { get; init; }
        public decimal PaymentValue { get; init; }
        public decimal ReteFuente { get; init; }
        public decimal ReteIca { get; init; }
        public string SearchText { get; init; } = "";
    }

    private sealed class ExpenseAccountingTemplateRow
    {
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public int Priority { get; init; }
        public int CategoryValue { get; init; }
        public string CategoryName { get; init; } = "";
        public string IssuerNit { get; init; } = "";
        public string TextContains { get; init; } = "";
        public string MovementType { get; init; } = "";
        public bool RequiresApproval { get; init; }
        public string Description { get; init; } = "";
        public bool Active { get; init; }
    }

    private sealed class ExpenseAccountingTemplateLineRow
    {
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public string TemplateId { get; init; } = "";
        public string TemplateName { get; init; } = "";
        public int Order { get; init; }
        public string Side { get; init; } = "";
        public string AccountCode { get; init; } = "";
        public string AccountName { get; init; } = "";
        public string Formula { get; init; } = "";
        public decimal Percentage { get; init; }
        public decimal ConstantValue { get; init; }
        public string Description { get; init; } = "";
        public bool Active { get; init; }
    }

    private sealed class ExpenseAccountingTemplateMatch
    {
        public ExpenseAccountingTemplateRow Template { get; init; } = new();
        public int Score { get; init; }
        public decimal Confidence { get; init; }
    }

    private sealed class ExistingGeneratedAccountingLineRow
    {
        public string RecordId { get; init; } = "";
        public string ExpenseId { get; init; } = "";
    }
}
