using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DashboardExpenseIcaField = "cr07a_ica";
    private const string DianSupplierDocumentCufeField = "cr07a_cufecude";
    private const string DianSupplierDocumentReceptionDateField = "cr07a_fecharecepcion";
    private const string DianSupplierDocumentStatusField = "cr07a_estadodian";
    private const string DianSupplierDocumentGroupField = "cr07a_grupodian";
    private const string DianSupplierDocumentPaymentFormField = "cr07a_formapago";
    private const string DianSupplierDocumentPaymentMethodField = "cr07a_mediopago";
    private const string DianSupplierDocumentCurrencyField = "cr07a_divisa";
    private const string DianSupplierDocumentReteIvaField = "cr07a_reteiva";
    private const string DianSupplierDocumentSiigoSupplierIdField = "cr07a_siigoproveedorid";
    private const string DianSupplierDocumentSiigoSupplierNameField = "cr07a_siigoproveedornombre";
    private const int DianSupplierDocumentUpsertMaxConcurrency = 6;

    public async Task<DianSupplierDocumentDataverseUpsertResultDto> UpsertDianSupplierDocumentRowsAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        rows ??= Array.Empty<DianSupplierDocumentImportRowDto>();

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var effectiveAttributes = BuildDianSupplierDocumentAttributeSet(metadata, attributes);
        var existingIndex = await GetDianSupplierDocumentExistingIndexAsync(metadata, effectiveAttributes, ct);

        using var throttler = new SemaphoreSlim(DianSupplierDocumentUpsertMaxConcurrency);
        var tasks = rows.Select(async row =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                return await UpsertDianSupplierDocumentRowAsync(
                    metadata,
                    effectiveAttributes,
                    fields,
                    existingIndex,
                    row,
                    dryRun,
                    ct);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        var outcomes = await Task.WhenAll(tasks);
        var result = new DianSupplierDocumentDataverseUpsertResultDto();
        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case DianSupplierDocumentUpsertOutcome.Created:
                    result.Created++;
                    break;
                case DianSupplierDocumentUpsertOutcome.Updated:
                    result.Updated++;
                    break;
                case DianSupplierDocumentUpsertOutcome.Unchanged:
                    result.Unchanged++;
                    break;
                case DianSupplierDocumentUpsertOutcome.Skipped:
                    result.Skipped++;
                    break;
            }
        }

        return result;
    }

    public async Task<DianSupplierDocumentSiigoSupplierResolutionResultDto> ResolveDianSupplierDocumentSiigoSuppliersAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        IReadOnlyList<DianSupplierDocumentResolvedSupplierDto> suppliers,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        rows ??= Array.Empty<DianSupplierDocumentImportRowDto>();
        suppliers ??= Array.Empty<DianSupplierDocumentResolvedSupplierDto>();

        var supplierIndex = suppliers
            .Where(static supplier => !string.IsNullOrWhiteSpace(supplier.SiigoSupplierId))
            .Select(static supplier => new
            {
                Key = ExtractDigits(supplier.SupplierNit),
                Supplier = supplier
            })
            .Where(static item => item.Key.Length >= 5)
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Supplier, StringComparer.OrdinalIgnoreCase);

        var rowSupplierKeys = rows
            .Select(static row => ExtractDigits(row.SupplierNit))
            .Where(static key => key.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new DianSupplierDocumentSiigoSupplierResolutionResultDto
        {
            Reviewed = rowSupplierKeys.Length,
            Found = rowSupplierKeys.Count(supplierIndex.ContainsKey)
        };
        result.Missing = Math.Max(0, result.Reviewed - result.Found);

        if (dryRun || result.Found == 0)
            return result;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var effectiveAttributes = BuildDianSupplierDocumentAttributeSet(metadata, attributes);
        var existingIndex = await GetDianSupplierDocumentExistingIndexAsync(metadata, effectiveAttributes, ct);
        var updatedRecordIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var supplierKey = ExtractDigits(row.SupplierNit);
            if (!supplierIndex.TryGetValue(supplierKey, out var supplier))
                continue;

            result.MatchedRows++;
            var existing = FindDianSupplierDocumentExistingRecord(existingIndex, row);
            if (existing is null || !updatedRecordIds.Add(existing.Id))
                continue;

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, null, supplier.SiigoSupplierId, force: true);
            SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, null, supplier.SiigoSupplierName, force: true);
            SetAccountCatalogValue(
                payload,
                attributes,
                ExpenseReviewReasonField,
                null,
                TruncateAccountCatalogText(
                    $"Proveedor Siigo encontrado automaticamente por NIT {row.SupplierNit}: {supplier.SiigoSupplierName}.",
                    1000),
                force: true);

            if (payload.Count == 0)
                continue;

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                "PATCH",
                payload,
                ct);
            result.Updated++;
        }

        return result;
    }

    private async Task<DianSupplierDocumentUpsertOutcome> UpsertDianSupplierDocumentRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        TaxExpenseFieldMap fields,
        IReadOnlyDictionary<string, DianSupplierDocumentExistingRecord> existingIndex,
        DianSupplierDocumentImportRowDto row,
        bool dryRun,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalKey) || row.EmissionDate is null)
            return DianSupplierDocumentUpsertOutcome.Skipped;

        var payload = BuildDianSupplierDocumentPayload(metadata, attributes, fields, row);
        if (payload.Count == 0)
            return DianSupplierDocumentUpsertOutcome.Unchanged;

        var existing = FindDianSupplierDocumentExistingRecord(existingIndex, row);
        if (existing is not null)
        {
            if (!dryRun)
            {
                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                    "PATCH",
                    payload,
                    ct);
            }

            return DianSupplierDocumentUpsertOutcome.Updated;
        }

        if (!dryRun)
        {
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}",
                "POST",
                payload,
                ct);
        }

        return DianSupplierDocumentUpsertOutcome.Created;
    }

    private async Task<IReadOnlyDictionary<string, DianSupplierDocumentExistingRecord>> GetDianSupplierDocumentExistingIndexAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CancellationToken ct)
    {
        var select = BuildConciliacionSelectClause(metadata, attributes, new[]
        {
            metadata.PrimaryIdField,
            DianSupplierDocumentCufeField,
            ConciliacionDianExcelKeyField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            ConciliacionDianIssuerNitField,
            DashboardExpenseTotalField
        });
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        var index = new Dictionary<string, DianSupplierDocumentExistingRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in rows)
        {
            var id = ReadString(item, metadata.PrimaryIdField).Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var record = new DianSupplierDocumentExistingRecord(
                id,
                ReadString(item, DianSupplierDocumentCufeField).Trim(),
                ReadString(item, ConciliacionDianExcelKeyField).Trim(),
                ReadString(item, ConciliacionDianPrefixField).Trim(),
                ReadString(item, ConciliacionDianFolioField).Trim(),
                ReadString(item, ConciliacionDianIssuerNitField).Trim(),
                RoundCurrency(ReadDecimal(item, DashboardExpenseTotalField) ?? 0m));

            AddDianSupplierDocumentIndex(index, record.ExcelKey, record);
            if (!string.IsNullOrWhiteSpace(record.CufeCude))
            {
                AddDianSupplierDocumentIndex(index, BuildDianSupplierDocumentCufeKey(record.CufeCude), record);
                AddDianSupplierDocumentIndex(index, $"dian:{record.CufeCude.Trim().ToLowerInvariant()}", record);
            }
            AddDianSupplierDocumentIndex(index, BuildDianSupplierDocumentFallbackKey(record.Prefix, record.Folio, record.SupplierNit, record.TotalValue), record);
        }

        return index;
    }

    private static Dictionary<string, object?> BuildDianSupplierDocumentPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        TaxExpenseFieldMap fields,
        DianSupplierDocumentImportRowDto row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var primaryName = TruncateAccountCatalogText(
            $"{row.InvoiceNumber} {row.SupplierName} {row.TotalValue.ToString("0.##", CultureInfo.InvariantCulture)}".Trim(),
            100);

        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, primaryName, force: true);
        SetAccountCatalogValue(payload, attributes, fields.InvoiceNumberField, null, row.InvoiceNumber, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianDocumentTypeField, null, row.DocumentType, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianPrefixField, null, row.Prefix, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianFolioField, null, row.Folio, force: true);
        SetAccountCatalogValue(payload, attributes, fields.EmissionDateField.FieldName, null, row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, fields.IssuerNameField, null, row.SupplierName, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianIssuerNitField, null, row.SupplierNit, force: true);
        SetAccountCatalogValue(payload, attributes, fields.RecipientNameField, null, row.CompanyName, force: true);
        SetAccountCatalogValue(payload, attributes, fields.RecipientNitField, null, row.CompanyNit, force: true);
        SetAccountCatalogValue(payload, attributes, fields.TotalField, (decimal?)null, row.TotalValue, force: true);
        SetAccountCatalogValue(payload, attributes, fields.VatField, (decimal?)null, row.VatValue, force: true);
        SetAccountCatalogValue(payload, attributes, fields.ReteFuenteField, (decimal?)null, row.ReteFuenteValue, force: true);
        SetAccountCatalogValue(payload, attributes, fields.ReteIcaField, (decimal?)null, row.ReteIcaValue, force: true);
        SetAccountCatalogValue(payload, attributes, DashboardExpenseTotalBeforeVatField, (decimal?)null, row.BaseAmount, force: true);
        SetAccountCatalogValue(payload, attributes, DashboardExpenseIcaField, (decimal?)null, row.IcaValue, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentReteIvaField, (decimal?)null, row.ReteIvaValue, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentCufeField, null, row.CufeCude, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentReceptionDateField, null, row.ReceptionDate?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentStatusField, null, row.DianStatus, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentGroupField, null, row.DianGroup, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentPaymentFormField, null, row.PaymentForm, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentPaymentMethodField, null, row.PaymentMethod, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentCurrencyField, null, row.Currency, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianSourceField, null, "DIAN Excel", force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianExcelKeyField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "ImportadoDian", force: false);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, $"Importado desde Excel DIAN {row.SourceFileName}, hoja {row.SheetName}, fila {row.RowNumber}. Hash {row.SourceHash}.", force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, null, "", force: false);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, null, "", force: false);
        return payload;
    }

    private static DianSupplierDocumentExistingRecord? FindDianSupplierDocumentExistingRecord(
        IReadOnlyDictionary<string, DianSupplierDocumentExistingRecord> existingIndex,
        DianSupplierDocumentImportRowDto row)
    {
        if (existingIndex.TryGetValue(row.ExternalKey, out var byExternalKey))
            return byExternalKey;

        if (!string.IsNullOrWhiteSpace(row.CufeCude)
            && existingIndex.TryGetValue(BuildDianSupplierDocumentCufeKey(row.CufeCude), out var byCufe))
        {
            return byCufe;
        }

        if (!string.IsNullOrWhiteSpace(row.CufeCude)
            && existingIndex.TryGetValue($"dian:{row.CufeCude.Trim().ToLowerInvariant()}", out var byLegacyCufe))
        {
            return byLegacyCufe;
        }

        return existingIndex.TryGetValue(
            BuildDianSupplierDocumentFallbackKey(row.Prefix, row.Folio, row.SupplierNit, row.TotalValue),
            out var byFallback)
            ? byFallback
            : null;
    }

    private static HashSet<string> BuildDianSupplierDocumentAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            DashboardExpenseTotalBeforeVatField,
            DashboardExpenseIcaField,
            DashboardExpenseTotalField,
            DashboardExpenseVatField,
            DashboardExpenseReteFuenteField,
            DashboardExpenseReteIcaField,
            DashboardExpenseIssuerNameField,
            ConciliacionDianIssuerNitField,
            DashboardExpenseRecipientNameField,
            DashboardExpenseRecipientNitField,
            DashboardExpenseEmissionDateField,
            ConciliacionDianDocumentTypeField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            DianSupplierDocumentCufeField,
            DianSupplierDocumentReceptionDateField,
            DianSupplierDocumentStatusField,
            DianSupplierDocumentGroupField,
            DianSupplierDocumentPaymentFormField,
            DianSupplierDocumentPaymentMethodField,
            DianSupplierDocumentCurrencyField,
            DianSupplierDocumentReteIvaField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddDianSupplierDocumentIndex(
        IDictionary<string, DianSupplierDocumentExistingRecord> index,
        string key,
        DianSupplierDocumentExistingRecord record)
    {
        if (!string.IsNullOrWhiteSpace(key) && !index.ContainsKey(key))
            index[key] = record;
    }

    private static string BuildDianSupplierDocumentFallbackKey(string prefix, string folio, string supplierNit, decimal total) =>
        string.Join(
            ":",
            new[]
            {
                "dian-fallback",
                (prefix ?? "").Trim().ToLowerInvariant(),
                (folio ?? "").Trim().ToLowerInvariant(),
                (supplierNit ?? "").Trim().ToLowerInvariant(),
                RoundCurrency(total).ToString("0.##", CultureInfo.InvariantCulture)
            });

    private static string BuildDianSupplierDocumentCufeKey(string cufeCude)
    {
        var normalized = (cufeCude ?? "").Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"dian-cufe:{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
    }

    private enum DianSupplierDocumentUpsertOutcome
    {
        Created,
        Updated,
        Unchanged,
        Skipped
    }

    private sealed record DianSupplierDocumentExistingRecord(
        string Id,
        string CufeCude,
        string ExcelKey,
        string Prefix,
        string Folio,
        string SupplierNit,
        decimal TotalValue);
}
