using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ExpenseAccountCodeField = "cr07a_cuentacontablecodigo";
    private const string ExpenseAccountNameField = "cr07a_cuentacontablenombre";
    private const string ExpenseAutomationStateField = "cr07a_estadoautomatizacion";
    private const string ExpenseAutomationConfidenceField = "cr07a_confianzaautomatizacion";
    private const string ExpenseAccountingRuleIdField = "cr07a_reglacontableid";
    private const string ExpenseReviewReasonField = "cr07a_motivorevision";

    private const string AccountingRuleLogicalName = "cr07a_reglacuentacontable";
    private const string AccountingRuleSetName = "cr07a_reglacuentacontables";
    private const string AccountingRuleIdField = "cr07a_reglacuentacontableid";
    private const string AccountingRulePrimaryNameField = "cr07a_name";
    private const string AccountingRulePriorityField = "cr07a_prioridad";
    private const string AccountingRuleCategoryValueField = "cr07a_categoriavalor";
    private const string AccountingRuleCategoryNameField = "cr07a_categorianombre";
    private const string AccountingRuleIssuerNitField = "cr07a_nitemisor";
    private const string AccountingRuleTextContainsField = "cr07a_textocontiene";
    private const string AccountingRuleMovementTypeField = "cr07a_tipomovimiento";
    private const string AccountingRuleDebitCodeField = "cr07a_cuentadebitocodigo";
    private const string AccountingRuleDebitNameField = "cr07a_cuentadebitonombre";
    private const string AccountingRuleCreditCodeField = "cr07a_cuentacreditocodigo";
    private const string AccountingRuleCreditNameField = "cr07a_cuentacreditonombre";
    private const string AccountingRuleActiveField = "cr07a_activa";

    private static readonly string[] ExpenseAccountingTextFieldCandidates =
    {
        "cr07a_descripcion",
        "cr07a_concepto",
        "cr07a_detalle",
        "cr07a_observaciones",
        "cr07a_nombreemisor",
        "cr07a_nombrereceptor"
    };

    public async Task<ExpenseAccountingRuleApplyResultDto> ApplyExpenseAccountingRulesAsync(
        DateOnly startDate,
        DateOnly endDate,
        string movementType = "Compra",
        bool overwrite = false,
        CancellationToken ct = default,
        IReadOnlySet<string>? externalKeys = null)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo para aplicar reglas contables no es valido.");

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
        var expenses = await GetExpenseAccountingRowsAsync(
            expenseMetadata,
            expenseAttributes,
            expenseFields,
            issuerNitField,
            startDate,
            endDate.AddDays(1),
            externalKeys,
            ct);
        var rules = await GetExpenseAccountingRulesAsync(ct);
        var catalog = await GetExpenseAccountingAccountCatalogAsync(ct);
        var historicalAccounts = _dianHistoricalAccountFallbackEnabled
            ? await BuildDianHistoricalSupplierAccountIndexAsync(
                expenseMetadata,
                expenseAttributes,
                expenseFields,
                issuerNitField,
                expenses,
                endDate,
                catalog,
                ct)
            : new Dictionary<string, ExpenseAccountingFallbackMatch>(StringComparer.OrdinalIgnoreCase);

        var resultRows = new List<ExpenseAccountingRuleAppliedRowDto>();
        var updated = 0;
        var alreadyAssigned = 0;
        var noRule = 0;
        var invalidRule = 0;

        foreach (var expense in expenses)
        {
            ct.ThrowIfCancellationRequested();

            if (IsExpenseAccountingSiigoWriteProtected(expense))
            {
                alreadyAssigned++;
                resultRows.Add(BuildExpenseAccountingResultRow(
                    expense,
                    null,
                    expense.AccountCode,
                    expense.AccountName,
                    "ProtegidaSiigo",
                    "La cuenta no se modifico porque existe una creacion Siigo en curso o pendiente de confirmacion."));
                continue;
            }

            if (!overwrite && !string.IsNullOrWhiteSpace(expense.AccountCode))
            {
                alreadyAssigned++;
                continue;
            }

            var match = FindBestExpenseAccountingRule(expense, rules, movementType);
            if (match is null)
            {
                var fallback = ResolveDianAccountingFallback(expense, historicalAccounts, catalog);
                if (fallback is not null)
                {
                    var fallbackPayload = BuildExpenseAccountingFallbackAssignmentPayload(
                        expenseAttributes,
                        expense,
                        fallback);
                    if (fallbackPayload.Count > 0
                        && await TryPatchExpenseAccountingRowAsync(
                            expenseMetadata,
                            expense.RecordId,
                            expense.ConcurrencyToken,
                            fallbackPayload,
                            ct))
                    {
                        updated++;
                    }

                    resultRows.Add(BuildExpenseAccountingResultRow(
                        expense,
                        null,
                        fallback.AccountCode,
                        fallback.AccountName,
                        fallback.Status,
                        fallback.Reason));
                    continue;
                }

                noRule++;
                var notes = BuildNoAccountingRuleReason(expense, movementType);
                if (await UpdateExpenseAccountingReviewStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, expense.ConcurrencyToken, notes, ct))
                    updated++;

                resultRows.Add(BuildExpenseAccountingResultRow(expense, null, "", "", "SinRegla", notes));
                continue;
            }

            if (!catalog.TryGetValue(match.Rule.DebitAccountCode, out var account) || !account.Active)
            {
                invalidRule++;
                var notes = $"La regla {match.Rule.Name} usa la cuenta {match.Rule.DebitAccountCode}, pero no esta activa en el catalogo contable.";
                if (await UpdateExpenseAccountingReviewStateAsync(expenseMetadata, expenseAttributes, expense.RecordId, expense.ConcurrencyToken, notes, ct))
                    updated++;

                resultRows.Add(BuildExpenseAccountingResultRow(expense, match.Rule, match.Rule.DebitAccountCode, match.Rule.DebitAccountName, "ReglaInvalida", notes));
                continue;
            }

            var accountName = FirstNonEmpty(match.Rule.DebitAccountName, account.Name, match.Rule.DebitAccountCode);
            var payload = BuildExpenseAccountingAssignmentPayload(expenseAttributes, expense, match, accountName, overwrite);
            if (payload.Count > 0)
            {
                if (await TryPatchExpenseAccountingRowAsync(
                        expenseMetadata,
                        expense.RecordId,
                        expense.ConcurrencyToken,
                        payload,
                        ct))
                {
                    updated++;
                }
            }

            resultRows.Add(BuildExpenseAccountingResultRow(expense, match.Rule, match.Rule.DebitAccountCode, accountName, "Asignada", $"Confianza {match.Confidence.ToString("0", CultureInfo.InvariantCulture)}."));
        }

        return new ExpenseAccountingRuleApplyResultDto
        {
            StartDate = startDate,
            EndDate = endDate,
            MovementType = movementType,
            Reviewed = expenses.Count,
            Updated = updated,
            AlreadyAssigned = alreadyAssigned,
            NoRule = noRule,
            InvalidRule = invalidRule,
            Rows = resultRows
        };
    }

    private async Task<IReadOnlyList<ExpenseAccountingExpenseRow>> GetExpenseAccountingRowsAsync(
        RhEntityMetadata metadata,
        IReadOnlySet<string> attributes,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        DateOnly startInclusive,
        DateOnly endExclusive,
        IReadOnlySet<string>? externalKeys,
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
            fields.IssuerNameField,
            issuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            DashboardExpenseCategoryField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            ExpenseAccountingRuleIdField,
            ConciliacionDianExcelKeyField
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
            .Select(row => ParseExpenseAccountingExpenseRow(row, metadata, fields, issuerNitField, textFields))
            .Where(static row => row is not null)
            .Cast<ExpenseAccountingExpenseRow>()
            .Where(row => externalKeys is null
                || externalKeys.Count == 0
                || externalKeys.Contains(row.ExternalKey))
            .ToList();
    }

    private static ExpenseAccountingExpenseRow? ParseExpenseAccountingExpenseRow(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields,
        string issuerNitField,
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

        return new ExpenseAccountingExpenseRow
        {
            RecordId = recordId,
            ConcurrencyToken = ReadString(item, "@odata.etag").Trim(),
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), ReadString(item, fields.InvoiceNumberField), recordId),
            ExternalKey = ReadString(item, ConciliacionDianExcelKeyField).Trim(),
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
            AutomationState = ReadString(item, ExpenseAutomationStateField).Trim(),
            ReviewReason = ReadString(item, ExpenseReviewReasonField).Trim(),
            SearchText = NormalizeAccountingRuleText(string.Join(" ", textValues))
        };
    }

    private async Task<IReadOnlyList<ExpenseAccountingRuleRow>> GetExpenseAccountingRulesAsync(CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountingRuleLogicalName,
            AccountingRuleSetName,
            AccountingRuleIdField,
            AccountingRulePrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildExpenseAccountingRuleAttributeSet(metadata, attributes);
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AccountingRulePriorityField,
            AccountingRuleCategoryValueField,
            AccountingRuleCategoryNameField,
            AccountingRuleIssuerNitField,
            AccountingRuleTextContainsField,
            AccountingRuleMovementTypeField,
            AccountingRuleDebitCodeField,
            AccountingRuleDebitNameField,
            AccountingRuleCreditCodeField,
            AccountingRuleCreditNameField,
            AccountingRuleActiveField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field)
            && (string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                || attributes.Contains(field)))
        .Distinct(StringComparer.OrdinalIgnoreCase));

        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(row => ParseExpenseAccountingRuleRow(row, metadata, attributes))
            .Where(static row => row is not null && row.Active && !string.IsNullOrWhiteSpace(row.DebitAccountCode))
            .Cast<ExpenseAccountingRuleRow>()
            .OrderBy(static row => row.Priority <= 0 ? int.MaxValue : row.Priority)
            .ThenBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExpenseAccountingRuleRow? ParseExpenseAccountingRuleRow(
        JsonElement item,
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new ExpenseAccountingRuleRow
        {
            RecordId = recordId,
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), recordId),
            Priority = ReadIntFlexible(item, AccountingRulePriorityField),
            CategoryValue = ReadIntFlexible(item, AccountingRuleCategoryValueField),
            CategoryName = FirstNonEmpty(
                ReadString(item, $"{AccountingRuleCategoryNameField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, AccountingRuleCategoryNameField)),
            IssuerNit = ReadString(item, AccountingRuleIssuerNitField).Trim(),
            TextContains = ReadString(item, AccountingRuleTextContainsField).Trim(),
            MovementType = ReadString(item, AccountingRuleMovementTypeField).Trim(),
            DebitAccountCode = ReadString(item, AccountingRuleDebitCodeField).Trim(),
            DebitAccountName = ReadString(item, AccountingRuleDebitNameField).Trim(),
            CreditAccountCode = ReadString(item, AccountingRuleCreditCodeField).Trim(),
            CreditAccountName = ReadString(item, AccountingRuleCreditNameField).Trim(),
            Active = !attributes.Contains(AccountingRuleActiveField)
                || !item.TryGetProperty(AccountingRuleActiveField, out _)
                || ReadBool(item, AccountingRuleActiveField)
        };
    }

    private async Task<IReadOnlyDictionary<string, ExpenseAccountingAccountRow>> GetExpenseAccountingAccountCatalogAsync(CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            AccountCatalogLogicalName,
            AccountCatalogSetName,
            AccountCatalogIdField,
            AccountCatalogPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildAccountCatalogAttributeSet(metadata, attributes);
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AccountCatalogCodeField,
            AccountCatalogNameField,
            AccountCatalogActiveField
        }
        .Where(field => !string.IsNullOrWhiteSpace(field)
            && (string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                || attributes.Contains(field)))
        .Distinct(StringComparer.OrdinalIgnoreCase));
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);

        return rows
            .Select(row => new ExpenseAccountingAccountRow
            {
                Code = ReadString(row, AccountCatalogCodeField).Trim(),
                Name = ResolveAccountCatalogName(ReadString(row, AccountCatalogCodeField), FirstNonEmpty(ReadString(row, AccountCatalogNameField), ReadString(row, metadata.PrimaryNameField))),
                Active = !attributes.Contains(AccountCatalogActiveField)
                    || !row.TryGetProperty(AccountCatalogActiveField, out _)
                    || ReadBool(row, AccountCatalogActiveField)
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildExpenseAccountingRuleAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AccountingRulePriorityField,
            AccountingRuleCategoryValueField,
            AccountingRuleCategoryNameField,
            AccountingRuleIssuerNitField,
            AccountingRuleTextContainsField,
            AccountingRuleMovementTypeField,
            AccountingRuleDebitCodeField,
            AccountingRuleDebitNameField,
            AccountingRuleCreditCodeField,
            AccountingRuleCreditNameField,
            AccountingRuleActiveField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static ExpenseAccountingRuleMatch? FindBestExpenseAccountingRule(
        ExpenseAccountingExpenseRow expense,
        IReadOnlyList<ExpenseAccountingRuleRow> rules,
        string movementType)
    {
        return rules
            .Select(rule => BuildExpenseAccountingRuleMatch(expense, rule, movementType))
            .Where(static match => match is not null)
            .Cast<ExpenseAccountingRuleMatch>()
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Rule.Priority <= 0 ? int.MaxValue : match.Rule.Priority)
            .ThenBy(static match => match.Rule.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static ExpenseAccountingRuleMatch? BuildExpenseAccountingRuleMatch(
        ExpenseAccountingExpenseRow expense,
        ExpenseAccountingRuleRow rule,
        string movementType)
    {
        var score = 0;
        var confidence = 70m;
        var hasCriterion = false;

        if (!string.IsNullOrWhiteSpace(rule.MovementType))
        {
            if (!string.Equals(rule.MovementType.Trim(), movementType?.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            score += 5;
            confidence += 5m;
        }

        if (!string.IsNullOrWhiteSpace(rule.IssuerNit))
        {
            hasCriterion = true;
            var ruleNit = ExtractDigits(rule.IssuerNit);
            if (string.IsNullOrWhiteSpace(ruleNit)
                || (!string.Equals(ruleNit, ExtractDigits(expense.ProviderNit), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ruleNit, ExtractDigits(expense.RecipientNit), StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            score += 40;
            confidence += 20m;
        }

        if (rule.CategoryValue > 0 || !string.IsNullOrWhiteSpace(rule.CategoryName))
        {
            hasCriterion = true;
            var categoryMatches = rule.CategoryValue > 0
                ? rule.CategoryValue == expense.CategoryValue
                : string.Equals(
                    NormalizeAccountingRuleText(rule.CategoryName),
                    NormalizeAccountingRuleText(expense.CategoryName),
                    StringComparison.OrdinalIgnoreCase);
            if (!categoryMatches)
                return null;

            score += 25;
            confidence += 15m;
        }

        if (!string.IsNullOrWhiteSpace(rule.TextContains))
        {
            hasCriterion = true;
            if (!expense.SearchText.Contains(NormalizeAccountingRuleText(rule.TextContains), StringComparison.OrdinalIgnoreCase))
                return null;

            score += 20;
            confidence += 10m;
        }

        if (!hasCriterion)
            return null;

        return new ExpenseAccountingRuleMatch
        {
            Rule = rule,
            Score = score,
            Confidence = Math.Min(confidence, 100m)
        };
    }

    private static Dictionary<string, object?> BuildExpenseAccountingAssignmentPayload(
        ISet<string> attributes,
        ExpenseAccountingExpenseRow expense,
        ExpenseAccountingRuleMatch match,
        string accountName,
        bool overwrite)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var force = overwrite || string.IsNullOrWhiteSpace(expense.AccountCode);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, expense.AccountCode, match.Rule.DebitAccountCode, force);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, expense.AccountName, accountName, force);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountingRuleIdField, null, match.Rule.RecordId, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "Clasificado", force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, match.Confidence, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            $"Cuenta contable asignada por regla {match.Rule.Name}.",
            force: true);
        return payload;
    }

    private async Task<IReadOnlyDictionary<string, ExpenseAccountingFallbackMatch>> BuildDianHistoricalSupplierAccountIndexAsync(
        RhEntityMetadata metadata,
        IReadOnlySet<string> attributes,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        IReadOnlyList<ExpenseAccountingExpenseRow> currentExpenses,
        DateOnly endDate,
        IReadOnlyDictionary<string, ExpenseAccountingAccountRow> catalog,
        CancellationToken ct)
    {
        var dianExpenses = currentExpenses
            .Where(IsDianExpenseAccountingRow)
            .Where(static expense => string.IsNullOrWhiteSpace(expense.AccountCode))
            .ToArray();
        if (dianExpenses.Length == 0)
            return new Dictionary<string, ExpenseAccountingFallbackMatch>(StringComparer.OrdinalIgnoreCase);

        var supplierNits = dianExpenses
            .Select(static expense => ExtractDigits(expense.ProviderNit))
            .Where(static nit => nit.Length >= 5)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (supplierNits.Count == 0)
            return new Dictionary<string, ExpenseAccountingFallbackMatch>(StringComparer.OrdinalIgnoreCase);

        var currentRecordIds = dianExpenses
            .Select(static expense => expense.RecordId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var history = await GetExpenseAccountingRowsAsync(
            metadata,
            attributes,
            fields,
            issuerNitField,
            new DateOnly(2000, 1, 1),
            endDate.AddDays(1),
            externalKeys: null,
            ct);

        return history
            .Where(expense => !currentRecordIds.Contains(expense.RecordId))
            .Where(static expense => !expense.ReviewReason.Contains(
                "Cuenta DIAN provisional",
                StringComparison.OrdinalIgnoreCase))
            .Select(expense => new
            {
                Expense = expense,
                SupplierNit = ExtractDigits(expense.ProviderNit),
                AccountCode = expense.AccountCode.Trim()
            })
            .Where(item => supplierNits.Contains(item.SupplierNit)
                && !string.IsNullOrWhiteSpace(item.AccountCode)
                && catalog.TryGetValue(item.AccountCode, out var account)
                && account.Active)
            .GroupBy(static item => item.SupplierNit, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var selected = group
                        .GroupBy(static item => item.AccountCode, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(static accountGroup => accountGroup.Count())
                        .ThenBy(static accountGroup => accountGroup.Key, StringComparer.OrdinalIgnoreCase)
                        .First();
                    var catalogAccount = catalog[selected.Key];
                    return new ExpenseAccountingFallbackMatch
                    {
                        AccountCode = catalogAccount.Code,
                        AccountName = FirstNonEmpty(catalogAccount.Name, selected.First().Expense.AccountName, catalogAccount.Code),
                        Confidence = 85m,
                        Status = "AsignadaHistorico",
                        Reason = $"Cuenta contable asignada automaticamente por historial del proveedor ({selected.Count():N0} registro(s)): {catalogAccount.Code} {catalogAccount.Name}."
                    };
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private ExpenseAccountingFallbackMatch? ResolveDianAccountingFallback(
        ExpenseAccountingExpenseRow expense,
        IReadOnlyDictionary<string, ExpenseAccountingFallbackMatch> historicalAccounts,
        IReadOnlyDictionary<string, ExpenseAccountingAccountRow> catalog)
    {
        if (!IsDianExpenseAccountingRow(expense))
            return null;

        var supplierNit = ExtractDigits(expense.ProviderNit);
        if (supplierNit.Length >= 5 && historicalAccounts.TryGetValue(supplierNit, out var historical))
            return historical;

        if (string.IsNullOrWhiteSpace(_dianDefaultAccountCode)
            || !catalog.TryGetValue(_dianDefaultAccountCode, out var defaultAccount)
            || !defaultAccount.Active)
        {
            return null;
        }

        var accountName = FirstNonEmpty(defaultAccount.Name, _dianDefaultAccountName, defaultAccount.Code);
        return new ExpenseAccountingFallbackMatch
        {
            AccountCode = defaultAccount.Code,
            AccountName = accountName,
            Confidence = 50m,
            Status = "AsignadaPorDefecto",
            Reason = $"Cuenta DIAN provisional asignada automaticamente porque el proveedor no tiene historial ni regla: {defaultAccount.Code} {accountName}."
        };
    }

    private static bool IsDianExpenseAccountingRow(ExpenseAccountingExpenseRow expense) =>
        expense.ExternalKey.StartsWith("dian-cufe:", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?> BuildExpenseAccountingFallbackAssignmentPayload(
        ISet<string> attributes,
        ExpenseAccountingExpenseRow expense,
        ExpenseAccountingFallbackMatch fallback)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, expense.AccountCode, fallback.AccountCode, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, expense.AccountName, fallback.AccountName, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "Clasificado", force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, fallback.Confidence, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, fallback.Reason, force: true);
        return payload;
    }

    private static bool IsExpenseAccountingSiigoWriteProtected(ExpenseAccountingExpenseRow expense) =>
        expense.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase)
        || expense.AutomationState.Equals("VerificacionSiigoPendiente", StringComparison.OrdinalIgnoreCase)
        || expense.AutomationState.Equals("ProcesandoProveedorSiigo", StringComparison.OrdinalIgnoreCase)
        || expense.AutomationState.Equals("VerificacionProveedorSiigoPendiente", StringComparison.OrdinalIgnoreCase)
        || expense.ReviewReason.Contains("[SIIGO_SUPPLIER_WRITE_AMBIGUOUS]", StringComparison.OrdinalIgnoreCase)
        || expense.ReviewReason.Contains("[SIIGO_WRITE_AMBIGUOUS]", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> UpdateExpenseAccountingReviewStateAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string recordId,
        string concurrencyToken,
        string reason,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "PendienteRevision", force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, 0m, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, reason, force: true);
        if (payload.Count == 0)
            return false;

        return await TryPatchExpenseAccountingRowAsync(metadata, recordId, concurrencyToken, payload, ct);
    }

    private async Task<bool> TryPatchExpenseAccountingRowAsync(
        RhEntityMetadata metadata,
        string recordId,
        string concurrencyToken,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken ct)
    {
        var etag = (concurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(etag))
            return false;

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            ct,
            content,
            request => request.Headers.TryAddWithoutValidation("If-Match", etag));
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Dataverse rechazo la actualizacion contable concurrente ({(int)response.StatusCode}): {body}");
        }

        return true;
    }

    private static ExpenseAccountingRuleAppliedRowDto BuildExpenseAccountingResultRow(
        ExpenseAccountingExpenseRow expense,
        ExpenseAccountingRuleRow? rule,
        string accountCode,
        string accountName,
        string status,
        string notes)
    {
        return new ExpenseAccountingRuleAppliedRowDto
        {
            ExpenseId = expense.RecordId,
            ExpenseName = expense.Name,
            ProviderNit = FirstNonEmpty(expense.ProviderNit, expense.RecipientNit),
            ProviderName = FirstNonEmpty(expense.ProviderName, expense.RecipientName),
            Category = FirstNonEmpty(expense.CategoryName, expense.CategoryValue > 0 ? expense.CategoryValue.ToString(CultureInfo.InvariantCulture) : ""),
            RuleId = rule?.RecordId ?? "",
            RuleName = rule?.Name ?? "",
            AccountCode = accountCode,
            AccountName = accountName,
            Status = status,
            Notes = notes
        };
    }

    private static string BuildNoAccountingRuleReason(ExpenseAccountingExpenseRow expense, string movementType) =>
        $"Sin regla contable para movimiento {movementType}, categoria {FirstNonEmpty(expense.CategoryName, expense.CategoryValue.ToString(CultureInfo.InvariantCulture))}, proveedor {FirstNonEmpty(expense.ProviderNit, expense.ProviderName, "sin proveedor")}.";

    private static string NormalizeAccountingRuleText(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class ExpenseAccountingExpenseRow
    {
        public string RecordId { get; init; } = "";
        public string ConcurrencyToken { get; init; } = "";
        public string Name { get; init; } = "";
        public string ExternalKey { get; init; } = "";
        public string InvoiceNumber { get; init; } = "";
        public string ProviderNit { get; init; } = "";
        public string ProviderName { get; init; } = "";
        public string RecipientNit { get; init; } = "";
        public string RecipientName { get; init; } = "";
        public int CategoryValue { get; init; }
        public string CategoryName { get; init; } = "";
        public string AccountCode { get; init; } = "";
        public string AccountName { get; init; } = "";
        public string AutomationState { get; init; } = "";
        public string ReviewReason { get; init; } = "";
        public string SearchText { get; init; } = "";
    }

    private sealed class ExpenseAccountingRuleRow
    {
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public int Priority { get; init; }
        public int CategoryValue { get; init; }
        public string CategoryName { get; init; } = "";
        public string IssuerNit { get; init; } = "";
        public string TextContains { get; init; } = "";
        public string MovementType { get; init; } = "";
        public string DebitAccountCode { get; init; } = "";
        public string DebitAccountName { get; init; } = "";
        public string CreditAccountCode { get; init; } = "";
        public string CreditAccountName { get; init; } = "";
        public bool Active { get; init; }
    }

    private sealed class ExpenseAccountingAccountRow
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Active { get; init; }
    }

    private sealed class ExpenseAccountingRuleMatch
    {
        public ExpenseAccountingRuleRow Rule { get; init; } = new();
        public int Score { get; init; }
        public decimal Confidence { get; init; }
    }

    private sealed class ExpenseAccountingFallbackMatch
    {
        public string AccountCode { get; init; } = "";
        public string AccountName { get; init; } = "";
        public decimal Confidence { get; init; }
        public string Status { get; init; } = "";
        public string Reason { get; init; } = "";
    }
}
