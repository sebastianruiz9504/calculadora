using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<ConciliacionActionResultDto> UpdateConciliacionDianSupplierDocumentClassificationAsync(
        ConciliacionDianClassificationRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar el documento DIAN a actualizar.");

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var categoryValue = request.CategoryValue
            ?? throw new InvalidOperationException("Selecciona una categoria para el documento.");
        var category = BuildPnlCategoryOptions()
            .FirstOrDefault(option => option.Value == categoryValue)
            ?? throw new InvalidOperationException("La categoria seleccionada no existe en Dataverse.");
        var accountCode = (request.AccountCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new InvalidOperationException("Selecciona una cuenta gasto para el documento.");

        var accounts = await GetConciliacionDianExpenseAccountCatalogAsync(ct);
        if (!accounts.TryGetValue(accountCode, out var account) || !account.Active)
            throw new InvalidOperationException("La cuenta gasto seleccionada no existe o no esta activa en el catalogo Siigo.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        SetAccountCatalogValue(payload, attributes, DashboardExpenseCategoryField, (int?)null, categoryValue, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, null, account.Code, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, null, account.Name, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "Clasificado", force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, 100m, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            TruncateAccountCatalogText($"Clasificado manualmente desde Conciliacion: {category.Label}, cuenta {account.Code} - {account.Name}.", 1000),
            force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la clasificacion.");

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        return new ConciliacionActionResultDto
        {
            Message = "Clasificacion guardada en Dataverse.",
            Row = null
        };
    }

    public async Task<ConciliacionDianSupplierInvoiceRowDto> GetConciliacionDianSupplierDocumentAsync(
        string recordId,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var row = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN seleccionado.");

        return row;
    }

    public async Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierAsync(
        string recordId,
        string siigoSupplierId,
        string siigoSupplierName,
        string message,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, null, siigoSupplierId, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, null, siigoSupplierName, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            TruncateAccountCatalogText(FirstNonEmpty(message, "Proveedor Siigo asociado desde Conciliacion."), 1000),
            force: true);

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        return new ConciliacionDianActionResultDto
        {
            Message = FirstNonEmpty(message, "Proveedor Siigo asociado."),
            IsSuccess = true,
            SiigoId = siigoSupplierId,
            SiigoName = siigoSupplierName,
            Row = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
        };
    }

    public async Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierDocumentSiigoResultAsync(
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
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var state = success ? "EnviadoSiigo" : "ErrorSiigo";
        var reason = TruncateAccountCatalogText(
            string.Join(" ", new[] { message, responseJson }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            1000);

        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, state, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, reason, force: true);
        if (success)
        {
            SetAccountCatalogValue(payload, attributes, ConciliacionDianSiigoDocumentIdField, null, siigoId, force: true);
            SetAccountCatalogValue(payload, attributes, ConciliacionDianSiigoDocumentNameField, null, siigoName, force: true);
        }

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            payload,
            ct);

        return new ConciliacionDianActionResultDto
        {
            Message = message,
            IsSuccess = success,
            SiigoId = siigoId,
            SiigoName = siigoName,
            ResponseJson = responseJson,
            Row = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
        };
    }

    private async Task<IReadOnlyList<ConciliacionOptionDto>> GetConciliacionDianExpenseAccountOptionsAsync(
        CancellationToken ct)
    {
        var accounts = await GetConciliacionDianExpenseAccountCatalogAsync(ct);
        return accounts.Values
            .OrderBy(static account => account.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static account => new ConciliacionOptionDto
            {
                Value = account.Code,
                Label = $"{account.Code} - {account.Name}"
            })
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, ConciliacionAccountCatalogItem>> GetConciliacionDianExpenseAccountCatalogAsync(
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
            .Where(static row => row.Active && IsConciliacionDianExpenseAccount(row.Code, row.Type))
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

    private async Task<ConciliacionDianSupplierInvoiceRowDto?> GetConciliacionDianSupplierDocumentByIdAsync(
        string recordId,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var cufeField = ResolveTaxExpenseField(attributes, DianSupplierDocumentCufeField, "cr07a_cufecude", "cr07a_cufe", "cr07a_cude");
        var baseAmountField = ResolveTaxExpenseField(attributes, DashboardExpenseTotalBeforeVatField, "cr07a_base", "cr07a_baseiva", "cr07a_totalantesdeimpuestos");
        var select = BuildConciliacionDianSupplierDocumentSelect(metadata, attributes, fields, cufeField, baseAmountField);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);
        using var document = JsonDocument.Parse(json);
        var row = ParseConciliacionDianSupplierInvoiceRow(document.RootElement, metadata, fields, cufeField, baseAmountField);
        return IsConciliacionDianSupplierInvoice(row) ? row : null;
    }

    private static string BuildConciliacionDianSupplierDocumentSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        TaxExpenseFieldMap fields,
        string cufeField,
        string baseAmountField)
    {
        return BuildConciliacionSelectClause(metadata, attributes, new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.PaymentValueField,
            fields.TotalField,
            fields.VatField,
            fields.ReteFuenteField,
            fields.ReteIcaField,
            fields.IssuerNameField,
            ConciliacionDianIssuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            fields.CloudField,
            fields.CopiersField,
            ConciliacionDianDocumentTypeField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            cufeField,
            baseAmountField,
            DianSupplierDocumentReceptionDateField,
            DianSupplierDocumentStatusField,
            DianSupplierDocumentGroupField,
            DianSupplierDocumentPaymentFormField,
            DianSupplierDocumentPaymentMethodField,
            DianSupplierDocumentCurrencyField,
            DianSupplierDocumentReteIvaField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField,
            DashboardExpenseCategoryField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseAutomationConfidenceField,
            ExpenseReviewReasonField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField,
            ConciliacionDianSiigoDocumentIdField,
            ConciliacionDianSiigoDocumentNameField,
            ConciliacionModifiedOnField
        });
    }

    private static bool IsConciliacionDianExpenseAccount(string code, string type)
    {
        var normalizedCode = (code ?? "").Trim();
        var normalizedType = NormalizeConciliacionLookupText(type);
        return normalizedType.Contains("GASTO", StringComparison.OrdinalIgnoreCase)
            || normalizedType.Contains("COSTO", StringComparison.OrdinalIgnoreCase)
            || normalizedCode.StartsWith('5')
            || normalizedCode.StartsWith('6');
    }
}
