using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.PublicDataExport;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public PublicDataExportCatalogDto GetPublicDataExportCatalog()
    {
        var billingClientLookupField = BuildDashboardLookupValuePropertyName(_dashboardBillingClientField);

        return new PublicDataExportCatalogDto
        {
            Datasets = new[]
            {
                new PublicDataExportDatasetDefinition
                {
                    Key = PublicDataExportDatasetKeys.Expenses,
                    Label = "Gastos Digital Tech",
                    Description = "Tabla de gastos de la empresa con columnas aprobadas para descarga publica.",
                    EntityLogicalName = _supplierExpensesTableName,
                    EntitySetName = _supplierExpensesTableSetName,
                    PrimaryIdField = _supplierExpensesIdField,
                    PrimaryNameField = "cr07a_name",
                    OrderBy = $"{_supplierExpensesDateField} desc",
                    Columns = BuildPublicExpenseColumns()
                },
                new PublicDataExportDatasetDefinition
                {
                    Key = PublicDataExportDatasetKeys.Billing,
                    Label = "Facturacion Digital Tech",
                    Description = "Tabla de facturacion con columnas aprobadas para descarga publica.",
                    EntityLogicalName = _dashboardBillingTableLogicalName,
                    EntitySetName = _dashboardBillingTableSetName,
                    PrimaryIdField = _dashboardBillingIdField,
                    PrimaryNameField = _dashboardBillingPrimaryNameField,
                    OrderBy = $"{_dashboardBillingEmissionDateField} desc",
                    Columns = BuildPublicBillingColumns(billingClientLookupField)
                }
            }
        };
    }

    public async Task<PublicDataExportTableDto> GetPublicDataExportTableAsync(
        string datasetKey,
        IReadOnlyList<string> columnKeys,
        int? top = null,
        CancellationToken ct = default)
    {
        var catalog = GetPublicDataExportCatalog();
        var dataset = catalog.FindDataset(datasetKey)
            ?? throw new InvalidOperationException("La tabla solicitada no existe.");
        var columns = ResolvePublicExportColumns(dataset, columnKeys);
        if (columns.Count == 0)
            throw new InvalidOperationException("La tabla seleccionada no tiene columnas aprobadas.");

        var selectFields = columns
            .SelectMany(GetPublicExportSelectFields)
            .Append(dataset.PrimaryIdField)
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Select(static field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var query = new List<string>
        {
            $"$select={string.Join(",", selectFields)}"
        };

        if (!string.IsNullOrWhiteSpace(dataset.OrderBy))
            query.Add($"$orderby={Uri.EscapeDataString(dataset.OrderBy)}");

        if (top.HasValue && top.Value > 0)
            query.Add($"$top={Math.Clamp(top.Value, 1, 5000).ToString(CultureInfo.InvariantCulture)}");

        var relativeUrl = $"/api/data/v9.2/{dataset.EntitySetName}?{string.Join("&", query)}";
        var items = await GetDataverseAppEntitiesAsync(relativeUrl, ct, AddFormattedValueHeaders);
        var rows = items
            .Select(item => BuildPublicExportRow(item, columns))
            .ToList();

        return new PublicDataExportTableDto
        {
            DatasetKey = dataset.Key,
            DatasetLabel = dataset.Label,
            Columns = columns,
            Rows = rows,
            RecordsCount = rows.Count,
            IsPreview = top.HasValue,
            PreviewLimit = top,
            Message = rows.Count == 0
                ? "No encontramos registros para esta tabla."
                : top.HasValue
                    ? $"Vista previa de {rows.Count:N0} registro(s)."
                    : $"Se prepararon {rows.Count:N0} registro(s)."
        };
    }

    private IReadOnlyList<PublicDataExportColumnDefinition> BuildPublicExpenseColumns()
    {
        return new[]
        {
            PublicDateColumn("createdOn", "Fecha creacion", "createdon"),
            PublicDateColumn("emissionDate", "Fecha emision", "cr07a_fechadeemision", "cr07a_fechaemision", "cr07a_fecha"),
            PublicDateColumn("paymentDate", "Fecha pago", DashboardExpensePaymentDateField),
            PublicTextColumn("issuerName", "Emisor", DashboardExpenseIssuerNameField),
            PublicTextColumn("issuerNit", "NIT emisor", "cr07a_nitemisor"),
            PublicTextColumn("recipientName", "Receptor", DashboardExpenseRecipientNameField, "cr07a_nombrereceptor"),
            PublicTextColumn("recipientNit", "NIT receptor", DashboardExpenseRecipientNitField, "cr07a_nitreceptor"),
            PublicFormattedTextColumn("category", "Categoria", DashboardExpenseCategoryField),
            PublicCurrencyColumn("total", "Total", DashboardExpenseTotalField, "cr07a_totalfactura"),
            PublicCurrencyColumn("vat", "IVA", DashboardExpenseVatField, "cr07a_ivavalor"),
            PublicCurrencyColumn("totalBeforeVat", "Total antes de IVA", DashboardExpenseTotalBeforeVatField, "cr07a_base"),
            PublicCurrencyColumn("paymentValue", "Valor pago", DashboardExpensePaymentValueField),
            PublicCurrencyColumn("reteFuente", "Rete fuente", DashboardExpenseReteFuenteField),
            PublicCurrencyColumn("reteIca", "Rete ICA", "cr07a_reteica"),
            PublicCurrencyColumn("cloud", "Cloud", DashboardExpenseCloudField),
            PublicCurrencyColumn("copiers", "Copiers", DashboardExpenseCopiersField)
        };
    }

    private IReadOnlyList<PublicDataExportColumnDefinition> BuildPublicBillingColumns(string billingClientLookupField)
    {
        return new[]
        {
            PublicTextColumn("invoiceNumber", "Factura", _dashboardBillingInvoiceNumberField, _dashboardBillingPrimaryNameField),
            PublicFormattedTextColumn("clientName", "Cliente", billingClientLookupField, _dashboardBillingClientField),
            PublicTextColumn("companyTaxId", "NIT empresa", _dashboardBillingCompanyTaxIdField),
            PublicFormattedTextColumn("vertical", "Vertical", _dashboardBillingVerticalField),
            PublicFormattedTextColumn("contractType", "Tipo contrato", _dashboardBillingContractTypeField),
            PublicDateColumn("emissionDate", "Fecha emision", _dashboardBillingEmissionDateField),
            PublicDateColumn("dueDate", "Fecha vencimiento", _dashboardBillingDueDateField),
            PublicCurrencyColumn("totalInvoice", "Total factura", _dashboardBillingTotalField),
            PublicNumberColumn("vatPercent", "% IVA", _dashboardBillingVatPercentField),
            PublicCurrencyColumn("vatValue", "Valor IVA", _dashboardBillingVatField),
            PublicDateColumn("paymentDate", "Fecha pago", _dashboardBillingPaymentDateField),
            PublicCurrencyColumn("paymentValue", "Valor pago", _dashboardBillingPaymentValueField),
            PublicCurrencyColumn("reteIca", "Rete ICA", _dashboardBillingReteIcaField),
            PublicCurrencyColumn("rteIva", "Rte IVA", _dashboardBillingRteIvaField),
            PublicCurrencyColumn("rteFte", "Rte Fte", _dashboardBillingRteFteField),
            PublicCurrencyColumn("difference", "Diferencia", _dashboardBillingDifferenceField),
            PublicUrlColumn("publicUrl", "URL factura", _dashboardBillingPublicUrlField)
        };
    }

    private static PublicDataExportColumnDefinition PublicTextColumn(
        string key,
        string label,
        string valueField,
        params string[] fallbackFields) =>
        BuildPublicTextColumn(key, label, valueField, fallbackFields, preferFormatted: false);

    private static PublicDataExportColumnDefinition PublicFormattedTextColumn(
        string key,
        string label,
        string valueField,
        params string[] fallbackFields) =>
        BuildPublicTextColumn(key, label, valueField, fallbackFields, preferFormatted: true);

    private static PublicDataExportColumnDefinition BuildPublicTextColumn(
        string key,
        string label,
        string valueField,
        string[] fallbackFields,
        bool preferFormatted) =>
        new()
        {
            Key = key,
            Label = label,
            ValueField = valueField,
            SelectFields = BuildPublicExportFieldList(valueField, fallbackFields),
            FallbackFields = fallbackFields,
            ValueType = "text",
            PreferFormattedValue = preferFormatted
        };

    private static PublicDataExportColumnDefinition PublicNumberColumn(string key, string label, string valueField) =>
        new()
        {
            Key = key,
            Label = label,
            ValueField = valueField,
            SelectFields = BuildPublicExportFieldList(valueField),
            ValueType = "number"
        };

    private static PublicDataExportColumnDefinition PublicCurrencyColumn(
        string key,
        string label,
        string valueField,
        params string[] fallbackFields) =>
        new()
        {
            Key = key,
            Label = label,
            ValueField = valueField,
            SelectFields = BuildPublicExportFieldList(valueField, fallbackFields),
            FallbackFields = fallbackFields,
            ValueType = "currency"
        };

    private static PublicDataExportColumnDefinition PublicDateColumn(
        string key,
        string label,
        string valueField,
        params string[] fallbackFields) =>
        new()
        {
            Key = key,
            Label = label,
            ValueField = valueField,
            SelectFields = BuildPublicExportFieldList(valueField, fallbackFields),
            FallbackFields = fallbackFields,
            ValueType = "date"
        };

    private static PublicDataExportColumnDefinition PublicUrlColumn(string key, string label, string valueField) =>
        new()
        {
            Key = key,
            Label = label,
            ValueField = valueField,
            SelectFields = BuildPublicExportFieldList(valueField),
            ValueType = "url"
        };

    private static IReadOnlyList<string> BuildPublicExportFieldList(string valueField, params string[] fallbackFields)
    {
        return new[] { valueField }
            .Concat(fallbackFields ?? Array.Empty<string>())
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Select(static field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PublicDataExportColumnDefinition> ResolvePublicExportColumns(
        PublicDataExportDatasetDefinition dataset,
        IReadOnlyList<string> columnKeys)
    {
        var requested = (columnKeys ?? Array.Empty<string>())
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return dataset.Columns
            .Where(column => requested.Contains(column.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static IEnumerable<string> GetPublicExportSelectFields(PublicDataExportColumnDefinition column)
    {
        return column.SelectFields.Count > 0
            ? column.SelectFields
            : new[] { column.ValueField };
    }

    private PublicDataExportRowDto BuildPublicExportRow(
        JsonElement item,
        IReadOnlyList<PublicDataExportColumnDefinition> columns)
    {
        var row = new PublicDataExportRowDto();
        foreach (var column in columns)
        {
            row.Cells[column.Key] = BuildPublicExportCell(item, column);
        }

        return row;
    }

    private PublicDataExportCellDto BuildPublicExportCell(JsonElement item, PublicDataExportColumnDefinition column)
    {
        var fields = GetPublicExportValueFields(column).ToList();
        var formattedValue = ReadFirstPublicFormattedValue(item, fields);
        var rawValue = ReadFirstPublicRawValue(item, fields);
        var cell = new PublicDataExportCellDto
        {
            RawValue = rawValue,
            ValueType = column.ValueType
        };

        if (string.Equals(column.ValueType, "date", StringComparison.OrdinalIgnoreCase))
        {
            var date = ReadFirstPublicDateValue(item, fields);
            cell.DateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
            cell.DisplayValue = date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                ?? FirstNonEmpty(formattedValue, rawValue);
            return cell;
        }

        if (string.Equals(column.ValueType, "currency", StringComparison.OrdinalIgnoreCase)
            || string.Equals(column.ValueType, "number", StringComparison.OrdinalIgnoreCase))
        {
            var number = ReadFirstPublicDecimalValue(item, fields);
            cell.NumberValue = number;
            cell.DisplayValue = number.HasValue
                ? number.Value.ToString(string.Equals(column.ValueType, "currency", StringComparison.OrdinalIgnoreCase) ? "C2" : "N2", DashboardCulture)
                : FirstNonEmpty(formattedValue, rawValue);
            return cell;
        }

        cell.DisplayValue = column.PreferFormattedValue
            ? FirstNonEmpty(formattedValue, rawValue)
            : FirstNonEmpty(rawValue, formattedValue);
        return cell;
    }

    private static IEnumerable<string> GetPublicExportValueFields(PublicDataExportColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.ValueField))
            yield return column.ValueField;

        foreach (var field in column.FallbackFields ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(field))
                yield return field;
        }

        foreach (var field in column.SelectFields ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(field))
                yield return field;
        }
    }

    private static string ReadFirstPublicFormattedValue(JsonElement item, IEnumerable<string> fields)
    {
        foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var formatted = ReadString(item, $"{field}{FormattedValueAnnotationSuffix}");
            if (!string.IsNullOrWhiteSpace(formatted))
                return formatted.Trim();
        }

        return "";
    }

    private static string ReadFirstPublicRawValue(JsonElement item, IEnumerable<string> fields)
    {
        foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = ReadString(item, field);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static decimal? ReadFirstPublicDecimalValue(JsonElement item, IEnumerable<string> fields)
    {
        foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = ReadDecimal(item, field);
            if (value.HasValue)
                return RoundCurrency(value.Value);
        }

        return null;
    }

    private static DateOnly? ReadFirstPublicDateValue(JsonElement item, IEnumerable<string> fields)
    {
        foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = ReadDateOnly(item, field);
            if (value.HasValue)
                return value;
        }

        return null;
    }
}
