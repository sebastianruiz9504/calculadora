using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string DianSupplierDocumentSiigoBusinessKeyField = "cr07a_siigobusinesskey";
    private const int DianSupplierDocumentUpsertMaxConcurrency = 6;
    private static readonly SemaphoreSlim DianSupplierDocumentImportGate = new(1, 1);
    private static readonly string[] DianSupplierDocumentDurableFields =
    {
        DianSupplierDocumentReceptionDateField,
        ExpenseAutomationStateField,
        ExpenseReviewReasonField,
        ConciliacionDianSiigoDocumentIdField,
        ConciliacionDianExcelKeyField,
        DianSupplierDocumentSiigoBusinessKeyField,
        DianSupplierDocumentCufeField,
        ConciliacionDianSourceField,
        DianSupplierDocumentSiigoSupplierIdField
    };

    public async Task<DianSupplierDocumentDataverseUpsertResultDto> UpsertDianSupplierDocumentRowsAsync(
        IReadOnlyList<DianSupplierDocumentImportRowDto> rows,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        rows ??= Array.Empty<DianSupplierDocumentImportRowDto>();

        var uniqueRows = rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.ExternalKey))
            .GroupBy(static row => row.ExternalKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var duplicateRows = Math.Max(0, rows.Count - uniqueRows.Length);
        if (uniqueRows.Length == 0)
        {
            return new DianSupplierDocumentDataverseUpsertResultDto
            {
                Skipped = duplicateRows
            };
        }

        var conflictingBusinessIdentities = uniqueRows
            .Where(static row => IsDianSupplierDocumentSiigoEligible(row)
                && !string.IsNullOrWhiteSpace(row.CufeCude))
            .Select(static row => new
            {
                Row = row,
                Key = BuildDianSupplierDocumentBusinessIdentityKey(row.Prefix, row.Folio, row.SupplierNit)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group
                .Select(item => item.Row.CufeCude.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .Select(static group => group.Key)
            .Take(5)
            .ToArray();
        if (conflictingBusinessIdentities.Length > 0)
        {
            throw new InvalidOperationException(
                "El archivo contiene CUFE distintos para la misma identidad de factura "
                + "(proveedor, prefijo, folio y total). No se importo ninguna fila para evitar vincular "
                + $"dos documentos DIAN a una sola compra Siigo. Conflictos: {string.Join(", ", conflictingBusinessIdentities)}.");
        }

        await DianSupplierDocumentImportGate.WaitAsync(ct);
        try
        {

            var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
                _supplierExpensesTableName,
                _supplierExpensesTableSetName,
                _supplierExpensesIdField,
                "",
                ct);
            var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
            if (!dryRun)
                EnsureDianSupplierDocumentDurabilitySchema(attributes);
            var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
            var effectiveAttributes = BuildDianSupplierDocumentAttributeSet(metadata, attributes);
            var existingIndex = await GetDianSupplierDocumentExistingIndexAsync(metadata, effectiveAttributes, ct);
            var hasExcelKeyAlternateKey = await HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct);
            var hasSiigoBusinessKey = await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct);
            var hasSiigoDocumentIdKey = await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct);

            if (!dryRun && (!hasExcelKeyAlternateKey || !hasSiigoBusinessKey || !hasSiigoDocumentIdKey))
            {
                throw new InvalidOperationException(
                    "Dataverse no tiene activas todas las claves unicas DIAN requeridas "
                    + $"({ConciliacionDianExcelKeyField}, {DianSupplierDocumentSiigoBusinessKeyField}, {ConciliacionDianSiigoDocumentIdField}). "
                    + "No se importara el archivo para evitar facturas duplicadas entre ejecuciones o instancias. "
                    + "Ejecuta scripts/Provision-CashFlowImportDataverse.ps1 y espera a que la clave quede activa.");
            }

            using var throttler = new SemaphoreSlim(DianSupplierDocumentUpsertMaxConcurrency);
            var tasks = uniqueRows.Select(async row =>
            {
                ct.ThrowIfCancellationRequested();
                await throttler.WaitAsync(ct);
                try
                {
                    var outcome = await UpsertDianSupplierDocumentRowAsync(
                        metadata,
                        effectiveAttributes,
                        fields,
                        existingIndex,
                        hasExcelKeyAlternateKey,
                        row,
                        dryRun,
                        ct);
                    return new DianSupplierDocumentUpsertRowResultDto
                    {
                        ExternalKey = row.ExternalKey,
                        RowNumber = row.RowNumber,
                        InvoiceNumber = row.InvoiceNumber,
                        SupplierNit = row.SupplierNit,
                        SupplierName = row.SupplierName,
                        TotalValue = row.TotalValue,
                        Outcome = outcome.ToString()
                    };
                }
                finally
                {
                    throttler.Release();
                }
            }).ToArray();

            var outcomes = await Task.WhenAll(tasks);
            var result = new DianSupplierDocumentDataverseUpsertResultDto
            {
                Skipped = duplicateRows
            };
            foreach (var rowResult in outcomes)
            {
                switch (rowResult.Outcome)
                {
                    case nameof(DianSupplierDocumentUpsertOutcome.Created):
                        result.Created++;
                        break;
                    case nameof(DianSupplierDocumentUpsertOutcome.Updated):
                        result.Updated++;
                        break;
                    case nameof(DianSupplierDocumentUpsertOutcome.Unchanged):
                        result.Unchanged++;
                        break;
                    case nameof(DianSupplierDocumentUpsertOutcome.Skipped):
                        result.Skipped++;
                        break;
                }
            }
            result.Rows = outcomes;

            return result;
        }
        finally
        {
            DianSupplierDocumentImportGate.Release();
        }
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

    public async Task<IReadOnlyList<DianSupplierDocumentImportRowDto>> GetDianSupplierDocumentRowsForSupplierLookupAsync(
        DateOnly startDate,
        DateOnly endDate,
        bool onlyPending = true,
        CancellationToken ct = default)
    {
        if (startDate > endDate)
            throw new InvalidOperationException("El periodo para validar proveedores DIAN no es valido.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        if (string.IsNullOrWhiteSpace(fields.EmissionDateField.FieldName))
            return Array.Empty<DianSupplierDocumentImportRowDto>();

        var effectiveAttributes = BuildDianSupplierDocumentAttributeSet(metadata, attributes);
        var select = BuildConciliacionSelectClause(metadata, effectiveAttributes, new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            fields.EmissionDateField.FieldName,
            fields.IssuerNameField,
            ConciliacionDianIssuerNitField,
            fields.TotalField,
            ConciliacionDianDocumentTypeField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField,
            DianSupplierDocumentCufeField,
            DianSupplierDocumentReceptionDateField,
            DianSupplierDocumentGroupField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField
        });
        if (!effectiveAttributes.Contains(DianSupplierDocumentReceptionDateField))
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene activa la columna {DianSupplierDocumentReceptionDateField}; "
                + "no es seguro buscar facturas por mes de emision en lugar del mes de recepcion.");
        }

        var periodField = DianSupplierDocumentReceptionDateField;
        var filter = BuildConciliacionReceptionDateFilter(periodField, startDate, endDate.AddDays(1));
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={periodField} desc&$top=5000";
        var items = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildDianSupplierDocumentLookupRow(item, metadata, fields))
            .Where(row => row is not null)
            .Cast<DianSupplierDocumentLookupRow>()
            .Where(row => !onlyPending || string.IsNullOrWhiteSpace(row.SiigoSupplierId))
            .Where(static row => IsDianSupplierDocumentLookupCandidate(row))
            .Select(static row => row.ImportRow)
            .ToArray();
    }

    private async Task<DianSupplierDocumentUpsertOutcome> UpsertDianSupplierDocumentRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        TaxExpenseFieldMap fields,
        IReadOnlyDictionary<string, DianSupplierDocumentExistingRecord> existingIndex,
        bool hasExcelKeyAlternateKey,
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
            if (string.IsNullOrWhiteSpace(existing.ConcurrencyToken))
                throw new InvalidOperationException("Dataverse no devolvio ETag para reconciliar el documento DIAN existente; se detuvo la importacion concurrente.");

            if (!dryRun)
            {
                // La importacion DIAN es la fuente autoritativa de identidad, fechas e importes.
                // Este payload no contiene clasificacion, proveedor Siigo ni estado de automatizacion,
                // por lo que una reimportacion repara datos obsoletos sin reabrir el workflow.
                if (!await TryPatchExpenseAccountingRowAsync(
                        metadata,
                        existing.Id,
                        existing.ConcurrencyToken,
                        payload,
                        ct))
                {
                    throw new InvalidOperationException(
                        "El documento DIAN cambio mientras se reimportaba. Se detuvo el job antes de Siigo para evitar publicar con una identidad obsoleta.");
                }
            }

            return DianSupplierDocumentUpsertOutcome.Updated;
        }

        if (!dryRun)
        {
            if (!hasExcelKeyAlternateKey)
                throw new InvalidOperationException("La importacion DIAN exige la clave unica de Dataverse; no se permite POST sin proteccion contra duplicados.");

            var alternateKey = Uri.EscapeDataString(row.ExternalKey.Replace("'", "''", StringComparison.Ordinal));
            payload.Remove(ConciliacionDianExcelKeyField);
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({ConciliacionDianExcelKeyField}='{alternateKey}')",
                "PATCH",
                payload,
                ct);
        }

        return DianSupplierDocumentUpsertOutcome.Created;
    }

    private Task<bool> HasActiveDianSupplierDocumentExcelKeyAsync(
        string entityLogicalName,
        CancellationToken ct) =>
        HasActiveDianSupplierDocumentAlternateKeyAsync(
            entityLogicalName,
            ConciliacionDianExcelKeyField,
            ct);

    private Task<bool> HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(
        string entityLogicalName,
        CancellationToken ct) =>
        HasActiveDianSupplierDocumentAlternateKeyAsync(
            entityLogicalName,
            ConciliacionDianSiigoDocumentIdField,
            ct);

    private Task<bool> HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(
        string entityLogicalName,
        CancellationToken ct) =>
        HasActiveDianSupplierDocumentAlternateKeyAsync(
            entityLogicalName,
            DianSupplierDocumentSiigoBusinessKeyField,
            ct);

    private async Task<bool> HasActiveDianSupplierDocumentAlternateKeyAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct)
    {
        try
        {
            var json = await CallDataverseAppGetJsonAsync(
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')/Keys?$select=KeyAttributes,EntityKeyIndexStatus",
                ct);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("value", out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in values.EnumerateArray())
            {
                if (!item.TryGetProperty("KeyAttributes", out var keyAttributes)
                    || keyAttributes.ValueKind != JsonValueKind.Array
                    || keyAttributes.GetArrayLength() != 1
                    || !keyAttributes.EnumerateArray().Any(attribute =>
                        string.Equals(attribute.GetString(), attributeLogicalName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!item.TryGetProperty("EntityKeyIndexStatus", out var status))
                    return false;

                return status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var numericStatus) && numericStatus == 2
                    || status.ValueKind == JsonValueKind.String
                    && (status.GetString()?.Equals("2", StringComparison.OrdinalIgnoreCase) == true
                        || status.GetString()?.Equals("Active", StringComparison.OrdinalIgnoreCase) == true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible verificar la clave alterna DIAN sobre {Field}; la operacion real se detendra por seguridad.", attributeLogicalName);
        }

        return false;
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
            DianSupplierDocumentReceptionDateField,
            ConciliacionDianDocumentTypeField,
            DianSupplierDocumentGroupField,
            ConciliacionDianPrefixField,
            ConciliacionDianFolioField,
            ConciliacionDianIssuerNitField,
            DashboardExpenseTotalField
        });
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);
        var index = new Dictionary<string, DianSupplierDocumentExistingRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in rows)
        {
            var id = ReadString(item, metadata.PrimaryIdField).Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var record = new DianSupplierDocumentExistingRecord(
                id,
                ReadString(item, "@odata.etag").Trim(),
                ReadString(item, DianSupplierDocumentCufeField).Trim(),
                ReadString(item, ConciliacionDianExcelKeyField).Trim(),
                ReadString(item, DianSupplierDocumentReceptionDateField).Trim(),
                ReadString(item, ConciliacionDianDocumentTypeField).Trim(),
                ReadString(item, DianSupplierDocumentGroupField).Trim(),
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
            if (IsDianSupplierDocumentReceivedElectronicInvoice(record))
            {
                AddDianSupplierDocumentBusinessIdentityIndex(
                    index,
                    BuildDianSupplierDocumentBusinessIdentityKey(record.Prefix, record.Folio, record.SupplierNit),
                    record);
            }
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
        var invoiceNumber = TruncateAccountCatalogText(row.InvoiceNumber, 100);
        var documentType = TruncateAccountCatalogText(row.DocumentType, 100);
        var prefix = TruncateAccountCatalogText(row.Prefix, 100);
        var folio = TruncateAccountCatalogText(row.Folio, 100);
        var supplierName = TruncateAccountCatalogText(row.SupplierName, 100);
        var supplierNit = TruncateAccountCatalogText(row.SupplierNit, 100);
        var companyName = TruncateAccountCatalogText(row.CompanyName, 100);
        var companyNit = TruncateAccountCatalogText(row.CompanyNit, 100);
        var primaryName = TruncateAccountCatalogText(
            $"{invoiceNumber} {supplierName} {row.TotalValue.ToString("0.##", CultureInfo.InvariantCulture)}".Trim(),
            100);

        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, primaryName, force: true);
        SetAccountCatalogValue(payload, attributes, fields.InvoiceNumberField, null, invoiceNumber, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianDocumentTypeField, null, documentType, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianPrefixField, null, prefix, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianFolioField, null, folio, force: true);
        SetAccountCatalogValue(payload, attributes, fields.EmissionDateField.FieldName, null, row.EmissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, fields.IssuerNameField, null, supplierName, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianIssuerNitField, null, supplierNit, force: true);
        SetAccountCatalogValue(payload, attributes, fields.RecipientNameField, null, companyName, force: true);
        SetAccountCatalogValue(payload, attributes, fields.RecipientNitField, null, companyNit, force: true);
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
        if (IsDianSupplierDocumentSiigoEligible(row))
        {
            SetAccountCatalogValue(
                payload,
                attributes,
                DianSupplierDocumentSiigoBusinessKeyField,
                null,
                BuildDianSupplierDocumentBusinessIdentityKey(row.Prefix, row.Folio, row.SupplierNit),
                force: true);
        }
        return payload;
    }

    private static DianSupplierDocumentLookupRow? BuildDianSupplierDocumentLookupRow(
        JsonElement item,
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var prefix = ReadString(item, ConciliacionDianPrefixField).Trim();
        var folio = ReadString(item, ConciliacionDianFolioField).Trim();
        var fallbackInvoice = ReadString(item, fields.InvoiceNumberField).Trim();
        var invoiceNumber = string.Join("-", new[] { prefix, folio }.Where(static value => !string.IsNullOrWhiteSpace(value))).Trim();

        var importRow = new DianSupplierDocumentImportRowDto
        {
            ExternalKey = ReadString(item, ConciliacionDianExcelKeyField).Trim(),
            CufeCude = ReadString(item, DianSupplierDocumentCufeField).Trim(),
            DocumentType = FirstNonEmpty(
                ReadString(item, $"{ConciliacionDianDocumentTypeField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, ConciliacionDianDocumentTypeField)).Trim(),
            DianGroup = ReadString(item, DianSupplierDocumentGroupField).Trim(),
            Prefix = prefix,
            Folio = folio,
            InvoiceNumber = FirstNonEmpty(invoiceNumber, fallbackInvoice),
            EmissionDate = ReadDateOnly(item, fields.EmissionDateField.FieldName),
            SupplierNit = ReadString(item, ConciliacionDianIssuerNitField).Trim(),
            SupplierName = ReadString(item, fields.IssuerNameField).Trim(),
            TotalValue = RoundCurrency(ReadDecimal(item, fields.TotalField) ?? 0m)
        };

        return new DianSupplierDocumentLookupRow(
            importRow,
            ReadString(item, ConciliacionDianSourceField).Trim(),
            ReadString(item, DianSupplierDocumentSiigoSupplierIdField).Trim(),
            ReadString(item, DianSupplierDocumentSiigoSupplierNameField).Trim());
    }

    private static bool IsDianSupplierDocumentLookupCandidate(DianSupplierDocumentLookupRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ImportRow.SupplierNit))
            return false;

        var type = NormalizeConciliacionLookupText(row.ImportRow.DocumentType);
        var group = NormalizeConciliacionLookupText(row.ImportRow.DianGroup);
        var isInvoice = type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase);
        var isSupplierCreditNote = type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase);
        return (isInvoice || isSupplierCreditNote)
            && !type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDianSupplierDocumentSiigoEligible(DianSupplierDocumentImportRowDto row)
    {
        var type = NormalizeConciliacionLookupText(row.DocumentType);
        var group = NormalizeConciliacionLookupText(row.DianGroup);
        var isInvoice = type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase);
        var isSupplierCreditNote = type.Contains("NOTA DE CREDITO", StringComparison.OrdinalIgnoreCase)
            || type.Contains("CREDIT NOTE", StringComparison.OrdinalIgnoreCase);
        return (isInvoice || isSupplierCreditNote)
            && !type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static DianSupplierDocumentExistingRecord? FindDianSupplierDocumentExistingRecord(
        IReadOnlyDictionary<string, DianSupplierDocumentExistingRecord> existingIndex,
        DianSupplierDocumentImportRowDto row)
    {
        var businessIdentityKey = BuildDianSupplierDocumentBusinessIdentityKey(row.Prefix, row.Folio, row.SupplierNit);
        if (IsDianSupplierDocumentSiigoEligible(row)
            && !string.IsNullOrWhiteSpace(row.CufeCude)
            && existingIndex.TryGetValue(businessIdentityKey, out var businessMatch)
            && !string.IsNullOrWhiteSpace(businessMatch.CufeCude)
            && !string.Equals(businessMatch.CufeCude.Trim(), row.CufeCude.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La factura {row.Prefix}{row.Folio} del proveedor {row.SupplierNit} ya existe en Dataverse "
                + $"con un CUFE diferente ({businessMatch.CufeCude}). Se bloqueo la importacion para evitar duplicar la compra Siigo.");
        }

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

        // Con CUFE presente nunca se reconcilia por prefijo/folio/NIT/total: dos facturas
        // distintas pueden compartir esos valores y no deben heredar el vinculo Siigo.
        if (!string.IsNullOrWhiteSpace(row.CufeCude))
            return null;

        var fallbackKey = BuildDianSupplierDocumentFallbackKey(row.Prefix, row.Folio, row.SupplierNit, row.TotalValue);
        return existingIndex.TryGetValue(fallbackKey, out var byFallback)
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
            DianSupplierDocumentSiigoBusinessKeyField,
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

    private static void EnsureDianSupplierDocumentDurabilitySchema(ISet<string> attributes)
    {
        var missing = DianSupplierDocumentDurableFields
            .Where(field => !attributes.Contains(field))
            .ToArray();
        if (missing.Length == 0)
            return;

        throw new InvalidOperationException(
            "Dataverse no tiene completo el esquema durable requerido para la importacion DIAN/Siigo. "
            + $"Faltan: {string.Join(", ", missing)}. "
            + "No se importara ni publicara en Siigo hasta provisionar estas columnas.");
    }

    private static void AddDianSupplierDocumentIndex(
        IDictionary<string, DianSupplierDocumentExistingRecord> index,
        string key,
        DianSupplierDocumentExistingRecord record)
    {
        if (!string.IsNullOrWhiteSpace(key) && !index.ContainsKey(key))
            index[key] = record;
    }

    private static void AddDianSupplierDocumentBusinessIdentityIndex(
        IDictionary<string, DianSupplierDocumentExistingRecord> index,
        string key,
        DianSupplierDocumentExistingRecord record)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!index.TryGetValue(key, out var existing))
        {
            index[key] = record;
            return;
        }

        if (!string.IsNullOrWhiteSpace(existing.CufeCude)
            && !string.IsNullOrWhiteSpace(record.CufeCude)
            && !string.Equals(existing.CufeCude.Trim(), record.CufeCude.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Dataverse ya contiene CUFE distintos para la misma identidad de factura ({key}). "
                + "La importacion se bloqueo antes de cualquier escritura o publicacion en Siigo.");
        }

        if (string.IsNullOrWhiteSpace(existing.CufeCude) && !string.IsNullOrWhiteSpace(record.CufeCude))
            index[key] = record;
    }

    private static bool IsDianSupplierDocumentReceivedElectronicInvoice(DianSupplierDocumentExistingRecord record)
    {
        var type = NormalizeConciliacionLookupText(record.DocumentType);
        var group = NormalizeConciliacionLookupText(record.DianGroup);
        return type.Contains("FACTURA ELECTRONICA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("NOTA", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("APPLICATION RESPONSE", StringComparison.OrdinalIgnoreCase)
            && group.Contains("RECIBID", StringComparison.OrdinalIgnoreCase)
            && !group.Contains("EMITID", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDianSupplierDocumentFallbackKey(string prefix, string folio, string supplierNit, decimal total) =>
        string.Join(
            ":",
            new[]
            {
                "dian-fallback",
                NormalizeDianSupplierDocumentIdentityPart(prefix),
                NormalizeDianSupplierDocumentFolio(folio),
                ExtractDigits(supplierNit),
                RoundCurrency(total).ToString("0.##", CultureInfo.InvariantCulture)
            });

    private static string BuildDianSupplierDocumentBusinessIdentityKey(
        string prefix,
        string folio,
        string supplierNit)
    {
        var supplier = CanonicalizeDianSupplierDocumentTaxId(supplierNit);
        var normalizedPrefix = NormalizeDianSupplierDocumentIdentityPart(prefix);
        var normalizedFolio = NormalizeDianSupplierDocumentFolio(folio);
        if (string.IsNullOrWhiteSpace(supplier)
            || string.IsNullOrWhiteSpace(normalizedFolio))
        {
            return "";
        }

        var canonicalIdentity = string.Join(
            "|",
            supplier,
            string.IsNullOrWhiteSpace(normalizedPrefix) ? "SIN-PREFIJO" : normalizedPrefix,
            normalizedFolio);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        return $"dian-siigo:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string CanonicalizeDianSupplierDocumentTaxId(string? supplierNit)
    {
        var digits = ExtractDigits(supplierNit);
        if (digits.Length != 10)
            return digits;

        var baseNit = digits[..^1];
        return CalculateDianSupplierDocumentCheckDigit(baseNit) == digits[^1] - '0'
            ? baseNit
            : digits;
    }

    private static int CalculateDianSupplierDocumentCheckDigit(string identification)
    {
        var digits = ExtractDigits(identification);
        var weights = new[] { 71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3 };
        var offset = Math.Max(0, weights.Length - digits.Length);
        var sum = 0;
        for (var index = 0; index < digits.Length && index + offset < weights.Length; index++)
            sum += (digits[index] - '0') * weights[index + offset];

        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    private static string NormalizeDianSupplierDocumentIdentityPart(string? value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeDianSupplierDocumentFolio(string? value)
    {
        var digits = ExtractDigits(value);
        if (digits.Length == 0)
            return NormalizeDianSupplierDocumentIdentityPart(value);

        var normalized = digits.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

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
        string ConcurrencyToken,
        string CufeCude,
        string ExcelKey,
        string ReceptionDateValue,
        string DocumentType,
        string DianGroup,
        string Prefix,
        string Folio,
        string SupplierNit,
        decimal TotalValue);

    private sealed record DianSupplierDocumentLookupRow(
        DianSupplierDocumentImportRowDto ImportRow,
        string SourceLabel,
        string SiigoSupplierId,
        string SiigoSupplierName);
}
