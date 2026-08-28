using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<DashboardAgentExpensesDto> GetDashboardAgentExpensesAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (startInclusive >= endExclusive)
            throw new InvalidOperationException("El periodo de gastos para el agente no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        if (string.IsNullOrWhiteSpace(fields.EmissionDateField.FieldName))
            throw new InvalidOperationException("No encontramos un campo de fecha valido en la tabla de gastos.");

        var issuerNitField = ResolveTaxExpenseField(
            attributes,
            "cr07a_nitemisor",
            "cr07a_nitproveedor",
            "cr07a_identificacionemisor",
            "cr07a_identificacionproveedor",
            "cr07a_nit");
        var baseAmountField = ResolveTaxExpenseField(
            attributes,
            DashboardExpenseTotalBeforeVatField,
            "cr07a_base",
            "cr07a_baseiva",
            "cr07a_totalantesdeimpuestos");
        var textFields = ExpenseAccountingTextFieldCandidates
            .Where(field => IsDashboardDataverseFieldAvailable(field, attributes))
            .ToArray();

        var select = BuildDashboardAgentExpenseSelect(metadata, attributes, fields, issuerNitField, baseAmountField, textFields);
        var filter = BuildBillingDateFilter(
            fields.EmissionDateField.FieldName,
            fields.EmissionDateField.FieldKind,
            startInclusive,
            endExclusive);
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={fields.EmissionDateField.FieldName} desc";
        var items = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        var rows = items
            .Select(item => ParseDashboardAgentExpenseRow(item, metadata, fields, issuerNitField, baseAmountField, textFields))
            .Where(static row => row is not null)
            .Cast<DashboardAgentExpenseRowDto>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var endInclusive = endExclusive.AddDays(-1);
        return new DashboardAgentExpensesDto
        {
            StartDateValue = startInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateValue = endInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodLabel = $"{startInclusive:yyyy-MM-dd} a {endInclusive:yyyy-MM-dd}",
            RecordsCount = rows.Length,
            Rows = rows
        };
    }

    private static string BuildDashboardAgentExpenseSelect(
        RhEntityMetadata metadata,
        IReadOnlySet<string> attributes,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        string baseAmountField,
        IReadOnlyList<string> textFields)
    {
        return string.Join(",", new[]
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
            issuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            fields.CloudField,
            fields.CopiersField,
            baseAmountField,
            DashboardExpenseCategoryField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField
        }
        .Concat(textFields)
        .Where(field => !string.IsNullOrWhiteSpace(field)
            && (string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, metadata.PrimaryNameField, StringComparison.OrdinalIgnoreCase)
                || IsDashboardDataverseFieldAvailable(field, attributes)))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private DashboardAgentExpenseRowDto? ParseDashboardAgentExpenseRow(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields,
        string issuerNitField,
        string baseAmountField,
        IReadOnlyList<string> textFields)
    {
        var taxRow = ParseTaxExpenseRow(item, metadata.PrimaryIdField, fields);
        if (taxRow is null)
            return null;

        var categoryLabel = RepairSpanishMojibakeText(FirstNonEmpty(
            ReadString(item, $"{DashboardExpenseCategoryField}{FormattedValueAnnotationSuffix}"),
            ReadString(item, DashboardExpenseCategoryField),
            "Sin categoria")).Trim();
        var accountName = RepairSpanishMojibakeText(ReadString(item, ExpenseAccountNameField)).Trim();
        var reviewReason = RepairSpanishMojibakeText(ReadString(item, ExpenseReviewReasonField)).Trim();
        var details = BuildDashboardAgentExpenseDetails(item, metadata, fields, textFields, categoryLabel, accountName, reviewReason);
        var totalBeforeVat = RoundCurrency(ReadDecimal(item, baseAmountField) ?? Math.Max(0m, taxRow.TotalValue - taxRow.VatValue));
        var supplierNit = ReadString(item, issuerNitField).Trim();

        var searchText = string.Join(" ", new[]
        {
            ReadString(item, metadata.PrimaryNameField),
            taxRow.InvoiceNumber,
            taxRow.IssuerName,
            supplierNit,
            taxRow.RecipientName,
            taxRow.RecipientNit,
            categoryLabel,
            ReadString(item, ExpenseAccountCodeField),
            accountName,
            reviewReason,
            details
        });

        return new DashboardAgentExpenseRowDto
        {
            RecordId = taxRow.RecordId,
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), taxRow.InvoiceNumber, taxRow.RecordId),
            InvoiceNumber = taxRow.InvoiceNumber,
            SupplierName = RepairSpanishMojibakeText(taxRow.IssuerName).Trim(),
            SupplierNit = supplierNit,
            RecipientName = RepairSpanishMojibakeText(taxRow.RecipientName).Trim(),
            RecipientNit = taxRow.RecipientNit,
            EmissionDateValue = taxRow.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PaymentDateValue = taxRow.PaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            TotalValue = taxRow.TotalValue,
            TotalBeforeVatValue = totalBeforeVat,
            VatValue = taxRow.VatValue,
            PaymentValue = taxRow.PaymentValue,
            ReteFuenteValue = taxRow.ReteFuenteValue,
            ReteIcaValue = taxRow.ReteIcaValue,
            CloudValue = taxRow.CloudValue,
            CopiersValue = taxRow.CopiersValue,
            CategoryLabel = categoryLabel,
            AccountCode = ReadString(item, ExpenseAccountCodeField).Trim(),
            AccountName = accountName,
            AutomationState = ReadString(item, ExpenseAutomationStateField).Trim(),
            ReviewReason = reviewReason,
            SourceLabel = FirstNonEmpty(ReadString(item, ConciliacionDianSourceField), ReadString(item, ConciliacionDianExcelKeyField), "Dataverse").Trim(),
            Details = details,
            SearchText = NormalizeDashboardAgentExpenseSearchText(searchText)
        };
    }

    private static string BuildDashboardAgentExpenseDetails(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields,
        IReadOnlyList<string> textFields,
        string categoryLabel,
        string accountName,
        string reviewReason)
    {
        var values = textFields
            .Select(field => RepairSpanishMojibakeText(FirstNonEmpty(
                ReadString(item, $"{field}{FormattedValueAnnotationSuffix}"),
                ReadString(item, field))).Trim())
            .Concat(new[]
            {
                RepairSpanishMojibakeText(ReadString(item, metadata.PrimaryNameField)).Trim(),
                RepairSpanishMojibakeText(ReadString(item, fields.InvoiceNumberField)).Trim(),
                categoryLabel,
                accountName,
                reviewReason
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6);

        return string.Join(" | ", values);
    }

    private static string NormalizeDashboardAgentExpenseSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(" ", builder.ToString().Normalize(System.Text.NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
