using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DianSiigoAmbiguousWriteMarker = "[SIIGO_WRITE_AMBIGUOUS]";
    private const string DianSiigoAmbiguousWriteState = "VerificacionSiigoPendiente";
    private const string DianSiigoSupplierAmbiguousWriteMarker = "[SIIGO_SUPPLIER_WRITE_AMBIGUOUS]";
    private const string DianSiigoSupplierAmbiguousWriteState = "VerificacionProveedorSiigoPendiente";
    private const string DianSiigoSupplierProcessingState = "ProcesandoProveedorSiigo";

    public async Task<ConciliacionActionResultDto> UpdateConciliacionDianSupplierDocumentClassificationAsync(
        ConciliacionDianClassificationRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar el documento DIAN a actualizar.");

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN seleccionado.");
        var preserveAmbiguousWrite = HasDianSiigoAmbiguousWriteHold(current);
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

        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, null, account.Code, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, null, account.Name, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseAutomationStateField,
            current.AutomationState,
            preserveAmbiguousWrite ? ResolveDianSiigoProtectedState(current) : "Clasificado",
            force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, 100m, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            current.ReviewReason,
            TruncateAccountCatalogText(
                preserveAmbiguousWrite
                    ? $"{current.ReviewReason} Cuenta gasto ajustada manualmente: {account.Code} - {account.Name}."
                    : $"Cuenta gasto ajustada manualmente desde Conciliacion: {account.Code} - {account.Name}.",
                1000),
            force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar la clasificacion.");

        if (!await TryPatchExpenseAccountingRowAsync(metadata, normalizedRecordId, current.ConcurrencyToken, payload, ct))
            throw new InvalidOperationException("La factura cambio mientras se guardaba la cuenta; recarga y vuelve a intentarlo.");

        return new ConciliacionActionResultDto
        {
            Message = "Cuenta gasto guardada en Dataverse.",
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

    public Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForAutomationAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (endExclusive <= startInclusive)
            throw new InvalidOperationException("El periodo DIAN para automatizacion no es valido.");

        return GetConciliacionDianSupplierInvoiceRowsAsync(startInclusive, endExclusive, ct);
    }

    public Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForHistoryAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (endExclusive <= startInclusive)
            throw new InvalidOperationException("El periodo DIAN para historico no es valido.");

        return GetConciliacionDianSupplierInvoiceRowsAsync(
            startInclusive,
            endExclusive,
            ct,
            includeDataverseOnlyDocuments: true);
    }

    public async Task<bool> TryClaimConciliacionDianSupplierDocumentForSiigoAsync(
        string recordId,
        string concurrencyToken,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var etag = (concurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(etag))
            return false;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        if (!await HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica ExcelKey de documentos DIAN no esta activa; no se reservara la factura para Siigo.");
        if (!await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoDocumentId de documentos DIAN no esta activa; no se reservara la factura para Siigo.");
        if (!await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoBusinessKey de documentos DIAN no esta activa; no se reservara la factura para Siigo.");

        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct);
        if (current is null
            || !string.Equals(current.ConcurrencyToken, etag, StringComparison.Ordinal)
            || IsDianSiigoSuccessfulResult(current)
            || HasDianSiigoSupplierWriteHold(current))
        {
            return false;
        }
        EnsureDianSupplierDocumentDurableRow(current);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "ProcesandoSiigo", force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            "Factura reservada atomicamente para validar/crear la compra en Siigo.",
            force: true);

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            ct,
            content,
            request => request.Headers.TryAddWithoutValidation("If-Match", etag));
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Dataverse rechazo la reserva DIAN {RecordId} con {StatusCode}: {Body}",
                normalizedRecordId,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Dataverse no permitio reservar la factura para Siigo ({(int)response.StatusCode}).");
        }

        return true;
    }

    public async Task<bool> TryClaimConciliacionDianSupplierCreationAsync(
        string recordId,
        string concurrencyToken,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var etag = (concurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(etag))
            return false;

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        if (!await HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica ExcelKey de documentos DIAN no esta activa; no se reservara el proveedor para Siigo.");
        if (!await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoDocumentId de documentos DIAN no esta activa; no se reservara el proveedor para Siigo.");
        if (!await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoBusinessKey de documentos DIAN no esta activa; no se reservara el proveedor para Siigo.");

        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct);
        if (current is null
            || !string.Equals(current.ConcurrencyToken, etag, StringComparison.Ordinal)
            || IsDianSiigoSuccessfulResult(current)
            || HasDianSiigoAmbiguousWriteHold(current)
            || !string.IsNullOrWhiteSpace(current.SiigoSupplierId))
        {
            return false;
        }
        EnsureDianSupplierDocumentDurableRow(current);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, DianSiigoSupplierProcessingState, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            "Proveedor reservado atomicamente antes de validar/crear el tercero en Siigo.",
            force: true);

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
            "PATCH",
            ct,
            content,
            request => request.Headers.TryAddWithoutValidation("If-Match", etag));
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Dataverse no permitio reservar el proveedor para Siigo ({(int)response.StatusCode}): {body}");
        }

        return true;
    }

    public async Task<IReadOnlyList<ConciliacionDianSupplierInvoiceRowDto>> GetConciliacionDianSupplierDocumentsForPaymentAsync(
        string supplierIdentification,
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct = default)
    {
        if (endExclusive <= startInclusive)
            throw new InvalidOperationException("El rango de fechas para buscar facturas DIAN no es valido.");

        var rows = await GetConciliacionDianSupplierInvoiceRowsAsync(startInclusive, endExclusive, ct);
        var supplierDigits = ExtractDigits(supplierIdentification);
        if (string.IsNullOrWhiteSpace(supplierDigits))
            return rows;

        return rows
            .Where(row => AreConciliacionSupplierTaxIdsEquivalent(supplierDigits, ExtractDigits(row.SupplierNit)))
            .ToArray();
    }

    public async Task<ConciliacionDianActionResultDto> UpdateConciliacionSupplierExpenseAllocationAsync(
        ConciliacionSupplierExpenseAllocationRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar la factura DIAN que se va a distribuir.");

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos la factura DIAN seleccionada en Dataverse.");
        var expectedCufe = NormalizeConciliacionCufeCude(request.CufeCude);
        var currentCufe = NormalizeConciliacionCufeCude(current.Cufe);
        var expectedInvoiceNumber = NormalizeConciliacionSupplierInvoiceNumber(request.InvoiceNumber);
        var matchesSelectedDocument = !string.IsNullOrWhiteSpace(expectedCufe)
            ? !string.IsNullOrWhiteSpace(currentCufe)
                && string.Equals(expectedCufe, currentCufe, StringComparison.OrdinalIgnoreCase)
            : ConciliacionSupplierInvoiceNumberMatches(expectedInvoiceNumber, current);
        if (!matchesSelectedDocument)
        {
            throw new InvalidOperationException("El CUFE/CUDE o numero de factura ya no coincide con el registro seleccionado en Dataverse. Vuelve a buscar el proveedor.");
        }

        var cloudValue = RoundCurrency(request.CloudValue);
        var copiersValue = RoundCurrency(request.CopiersValue);
        var paymentValue = RoundCurrency(request.PaymentValue);
        if (cloudValue < 0m || copiersValue < 0m)
            throw new InvalidOperationException("Los valores de Cloud y Copiers no pueden ser negativos.");
        if (paymentValue <= 0m)
            throw new InvalidOperationException("El valor pagado debe ser mayor a cero.");
        if (Math.Abs(RoundCurrency(cloudValue + copiersValue) - paymentValue) > 1m)
        {
            throw new InvalidOperationException(
                $"Cloud y Copiers deben sumar el valor pagado de {paymentValue:N2}.");
        }

        if (!int.TryParse(request.CategoryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryValue)
            || !BuildPnlCategoryOptions().Any(option => option.Value == categoryValue))
        {
            throw new InvalidOperationException("Selecciona una categoria valida para el gasto.");
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        if (string.IsNullOrWhiteSpace(fields.CloudField)
            || string.IsNullOrWhiteSpace(fields.CopiersField)
            || !attributes.Contains(fields.CloudField)
            || !attributes.Contains(fields.CopiersField)
            || !attributes.Contains(DashboardExpenseCategoryField))
        {
            throw new InvalidOperationException("Dataverse no tiene disponibles los campos Cloud, Copiers y Categoria para esta factura.");
        }

        int? currentCategory = int.TryParse(current.CategoryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCategory)
            ? parsedCategory
            : null;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, fields.CloudField, current.CloudValue, cloudValue, force: true);
        SetAccountCatalogValue(payload, attributes, fields.CopiersField, current.CopiersValue, copiersValue, force: true);
        SetAccountCatalogValue<int?>(payload, attributes, DashboardExpenseCategoryField, currentCategory, categoryValue, force: true);
        var preserveAmbiguousWrite = HasDianSiigoAmbiguousWriteHold(current);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseAutomationStateField,
            current.AutomationState,
            preserveAmbiguousWrite ? ResolveDianSiigoProtectedState(current) : "Clasificado",
            force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationConfidenceField, (decimal?)null, 100m, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            current.ReviewReason,
            TruncateAccountCatalogText(
                preserveAmbiguousWrite
                    ? $"{current.ReviewReason} Distribucion confirmada. Cloud {cloudValue:N2}; Copiers {copiersValue:N2}; categoria {categoryValue}."
                    : $"Distribucion confirmada desde Conciliacion. Cloud {cloudValue:N2}; Copiers {copiersValue:N2}; categoria {categoryValue}.",
                1000),
            force: true);

        if (!await TryPatchExpenseAccountingRowAsync(metadata, normalizedRecordId, current.ConcurrencyToken, payload, ct))
            throw new InvalidOperationException("La factura cambio mientras se guardaba la distribucion; recarga y vuelve a intentarlo.");

        var updated = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse guardo la solicitud, pero no devolvio la factura actualizada.");
        var updatedDocumentMatches = !string.IsNullOrWhiteSpace(expectedCufe)
            ? string.Equals(NormalizeConciliacionCufeCude(updated.Cufe), expectedCufe, StringComparison.OrdinalIgnoreCase)
            : ConciliacionSupplierInvoiceNumberMatches(expectedInvoiceNumber, updated);
        if (!updatedDocumentMatches
            || Math.Abs(updated.CloudValue - cloudValue) > 0.01m
            || Math.Abs(updated.CopiersValue - copiersValue) > 0.01m
            || !string.Equals(updated.CategoryValue, categoryValue.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dataverse no confirmo la distribucion completa de Cloud, Copiers y Categoria.");
        }

        return new ConciliacionDianActionResultDto
        {
            Message = "Distribucion de la factura guardada en Dataverse.",
            IsSuccess = true,
            Row = updated
        };
    }

    public async Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierAsync(
        string recordId,
        string siigoSupplierId,
        string siigoSupplierName,
        string message,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var requestedSupplierId = (siigoSupplierId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requestedSupplierId))
            throw new InvalidOperationException("No se asociara el proveedor sin un SiigoProveedorId durable.");
        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN seleccionado.");
        var supplierWriteProtected = HasDianSiigoSupplierWriteHold(current);
        var preserveAmbiguousWrite = HasDianSiigoAmbiguousWriteHold(current) && !supplierWriteProtected;
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, null, requestedSupplierId, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, null, siigoSupplierName, force: true);
        if (supplierWriteProtected)
        {
            SetAccountCatalogValue(
                payload,
                attributes,
                ExpenseAutomationStateField,
                current.AutomationState,
                "ProveedorSiigoAsociado",
                force: true);
        }
        else if (preserveAmbiguousWrite)
        {
            SetAccountCatalogValue(
                payload,
                attributes,
                ExpenseAutomationStateField,
                current.AutomationState,
                ResolveDianSiigoProtectedState(current),
                force: true);
        }
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            TruncateAccountCatalogText(
                preserveAmbiguousWrite
                    ? $"{current.ReviewReason} {FirstNonEmpty(message, "Proveedor Siigo asociado desde Conciliacion.")}"
                    : FirstNonEmpty(message, "Proveedor Siigo asociado desde Conciliacion."),
                1000),
            force: true);

        if (!await TryPatchExpenseAccountingRowAsync(metadata, normalizedRecordId, current.ConcurrencyToken, payload, ct))
            throw new InvalidOperationException("La factura cambio mientras se asociaba el proveedor; se omitio la actualizacion concurrente.");

        var updated = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse acepto la asociacion, pero no devolvio el documento DIAN actualizado.");
        if (!string.Equals(updated.SiigoSupplierId, requestedSupplierId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dataverse no confirmo de forma durable el SiigoProveedorId.");

        return new ConciliacionDianActionResultDto
        {
            Message = FirstNonEmpty(message, "Proveedor Siigo asociado."),
            IsSuccess = true,
            SiigoId = requestedSupplierId,
            SiigoName = siigoSupplierName,
            Row = updated
        };
    }

    public async Task<ConciliacionDianActionResultDto> ClearConciliacionDianSupplierAsync(
        string recordId,
        string message,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN seleccionado.");
        if (IsDianSiigoSuccessfulResult(current))
            throw new InvalidOperationException("La factura ya tiene una compra Siigo vinculada; no se retirara el proveedor.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        SetAccountCatalogValue<string>(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, current.SiigoSupplierId, null, force: true);
        SetAccountCatalogValue<string>(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, current.SiigoSupplierName, null, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, current.AutomationState, "ProveedorPendienteSiigo", force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            current.ReviewReason,
            TruncateAccountCatalogText(
                FirstNonEmpty(message, "Siigo confirmo que el proveedor asociado no existe; carga el RUT para crearlo."),
                1000),
            force: true);

        if (!await TryPatchExpenseAccountingRowAsync(metadata, normalizedRecordId, current.ConcurrencyToken, payload, ct))
            throw new InvalidOperationException("La factura cambio mientras se retiraba el proveedor obsoleto.");

        var updated = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse acepto el cambio, pero no devolvio el documento DIAN actualizado.");
        if (!string.IsNullOrWhiteSpace(updated.SiigoSupplierId)
            || !updated.AutomationState.Equals("ProveedorPendienteSiigo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dataverse no confirmo que la factura quedara pendiente de proveedor.");
        }

        return new ConciliacionDianActionResultDto
        {
            Message = FirstNonEmpty(message, "Proveedor inexistente confirmado; factura enviada a la bandeja de RUT."),
            IsSuccess = true,
            Row = updated
        };
    }

    public async Task<ConciliacionDianActionResultDto> MarkConciliacionDianSupplierDocumentSiigoResultAsync(
        string recordId,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        bool ownsProcessingClaim = false,
        bool releaseProcessingClaim = false,
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN seleccionado para guardar el resultado Siigo.");
        EnsureDianSupplierDocumentDurableRow(current);
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        if (!await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoDocumentId de documentos DIAN no esta activa; no se guardara el resultado Siigo.");
        if (!await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct))
            throw new InvalidOperationException("La clave unica SiigoBusinessKey de documentos DIAN no esta activa; no se guardara el resultado Siigo.");

        var requestedSiigoId = (siigoId ?? "").Trim();
        var requestedSiigoName = (siigoName ?? "").Trim();
        if (success && string.IsNullOrWhiteSpace(requestedSiigoId))
            throw new InvalidOperationException("Dataverse no marcara la factura como enviada sin un SiigoDocumentId durable.");

        if (success && IsDianSiigoSuccessfulResult(current))
        {
            if (string.IsNullOrWhiteSpace(current.SiigoDocumentId))
                throw new InvalidOperationException("La factura figura enviada a Siigo, pero no tiene SiigoDocumentId; requiere reparacion manual.");
            if (!string.Equals(current.SiigoDocumentId, requestedSiigoId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La factura ya esta vinculada al documento Siigo {current.SiigoDocumentId}; no se reemplazara por {requestedSiigoId}.");
            }

            if (!current.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
                || IsDianSiigoInvoiceVerificationHold(current))
            {
                var verificationPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                SetAccountCatalogValue(verificationPayload, attributes, ExpenseAutomationStateField, null, "EnviadoSiigo", force: true);
                SetAccountCatalogValue(
                    verificationPayload,
                    attributes,
                    ExpenseReviewReasonField,
                    null,
                    TruncateAccountCatalogText(FirstNonEmpty(message, "Compra Siigo verificada nuevamente."), 1000),
                    force: true);
                SetAccountCatalogValue(
                    verificationPayload,
                    attributes,
                    ConciliacionDianSiigoDocumentNameField,
                    current.SiigoDocumentName,
                    FirstNonEmpty(requestedSiigoName, current.SiigoDocumentName),
                    force: true);
                if (!await TryPatchExpenseAccountingRowAsync(
                        metadata,
                        normalizedRecordId,
                        current.ConcurrencyToken,
                        verificationPayload,
                        ct))
                {
                    throw new InvalidOperationException("La factura cambio mientras se confirmaba nuevamente el vinculo Siigo.");
                }

                current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
                    ?? throw new InvalidOperationException("Dataverse no devolvio la factura despues de verificar el vinculo Siigo.");
                if (!current.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(current.SiigoDocumentId, requestedSiigoId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Dataverse no confirmo nuevamente el vinculo Siigo de forma durable.");
                }
            }

            return new ConciliacionDianActionResultDto
            {
                Message = message,
                IsSuccess = true,
                SiigoId = current.SiigoDocumentId,
                SiigoName = current.SiigoDocumentName,
                ResponseJson = responseJson,
                Row = current
            };
        }

        if (success && HasDianSiigoSupplierWriteHold(current))
        {
            throw new InvalidOperationException(
                "El proveedor conserva una escritura Siigo pendiente de verificar; no se asociara una compra hasta resolver ese estado.");
        }
        if (success
            && !current.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La factura no conserva una reserva atomica ProcesandoSiigo; se rechazo el resultado para evitar una escritura fuera del claim.");
        }

        var incomingReason = TruncateAccountCatalogText(
            string.Join(" ", new[] { message, responseJson }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            1000);
        var incomingSupplierVerification = incomingReason.Contains(
            DianSiigoSupplierAmbiguousWriteMarker,
            StringComparison.OrdinalIgnoreCase);
        var incomingInvoiceVerification = incomingReason.Contains(
            DianSiigoAmbiguousWriteMarker,
            StringComparison.OrdinalIgnoreCase);
        var currentIsProcessing = current.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase)
            || current.AutomationState.Equals(DianSiigoSupplierProcessingState, StringComparison.OrdinalIgnoreCase);
        if (releaseProcessingClaim && !ownsProcessingClaim)
            throw new InvalidOperationException("Solo el proceso que adquirio el claim puede liberarlo.");
        if (!success
            && currentIsProcessing
            && !ownsProcessingClaim)
        {
            return new ConciliacionDianActionResultDto
            {
                Message = "La fila esta reservada por otra ejecucion; no se sobrescribio su claim con un fallo externo al POST.",
                IsSuccess = false,
                SiigoId = current.SiigoDocumentId,
                SiigoName = current.SiigoDocumentName,
                ResponseJson = responseJson,
                Row = current
            };
        }
        var preserveSupplierVerification = !success
            && (IsDianSiigoSupplierVerificationHold(current)
                || incomingSupplierVerification);
        var preserveInvoiceVerification = !success
            && (IsDianSiigoInvoiceVerificationHold(current)
                || incomingInvoiceVerification);
        var state = success
            ? "EnviadoSiigo"
            : preserveSupplierVerification
                ? DianSiigoSupplierAmbiguousWriteState
                : preserveInvoiceVerification
                    ? DianSiigoAmbiguousWriteState
                    : incomingReason.Contains(DianSiigoSupplierAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase)
                        ? DianSiigoSupplierAmbiguousWriteState
                        : incomingReason.Contains(DianSiigoAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase)
                            ? DianSiigoAmbiguousWriteState
                            : "ErrorSiigo";
        var protectedIncomingReason = preserveSupplierVerification
            && !incomingReason.Contains(DianSiigoSupplierAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase)
                ? $"{DianSiigoSupplierAmbiguousWriteMarker} {incomingReason}".Trim()
                : preserveInvoiceVerification
                    && !incomingReason.Contains(DianSiigoAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase)
                        ? $"{DianSiigoAmbiguousWriteMarker} {incomingReason}".Trim()
                        : incomingReason;
        var reason = preserveSupplierVerification || preserveInvoiceVerification
            ? TruncateAccountCatalogText(
                string.Join(" ", new[] { current.ReviewReason, protectedIncomingReason }.Where(static value => !string.IsNullOrWhiteSpace(value))),
                1000)
            : protectedIncomingReason;

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, state, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, reason, force: true);
        if (success)
        {
            SetAccountCatalogValue(payload, attributes, ConciliacionDianSiigoDocumentIdField, null, requestedSiigoId, force: true);
            SetAccountCatalogValue(payload, attributes, ConciliacionDianSiigoDocumentNameField, null, requestedSiigoName, force: true);
        }

        if (!await TryPatchExpenseAccountingRowAsync(metadata, normalizedRecordId, current.ConcurrencyToken, payload, ct))
        {
            throw new InvalidOperationException(
                "La factura cambio mientras se guardaba el resultado Siigo; se conservo el estado mas reciente y no se aplico last-write-wins.");
        }

        var updated = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse acepto el PATCH, pero no devolvio la factura DIAN actualizada.");
        EnsureDianSupplierDocumentDurableRow(updated);
        if (success
            && (!updated.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(updated.SiigoDocumentId, requestedSiigoId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Dataverse no confirmo de forma durable el SiigoDocumentId y el estado EnviadoSiigo.");
        }
        if (!success && !updated.AutomationState.Equals(state, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dataverse no confirmo de forma durable el estado final de la automatizacion Siigo.");

        return new ConciliacionDianActionResultDto
        {
            Message = message,
            IsSuccess = success,
            SiigoId = success ? updated.SiigoDocumentId : requestedSiigoId,
            SiigoName = success ? updated.SiigoDocumentName : requestedSiigoName,
            ResponseJson = responseJson,
            Row = updated
        };
    }

    public async Task<ConciliacionDianActionResultDto> ConfirmConciliacionDianSupplierDocumentAmbiguousWriteAsync(
        string recordId,
        string siigoId,
        string siigoName,
        string message,
        string responseJson = "",
        CancellationToken ct = default)
    {
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var requestedSiigoId = (siigoId ?? "").Trim();
        var requestedSiigoName = (siigoName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requestedSiigoId))
            throw new InvalidOperationException("No se confirmara la escritura ambigua sin un SiigoDocumentId durable.");

        var current = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("No encontramos el documento DIAN pendiente de verificar.");
        EnsureDianSupplierDocumentDurableRow(current);
        if (IsDianSiigoSuccessfulResult(current))
        {
            if (!string.Equals(current.SiigoDocumentId, requestedSiigoId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"El documento DIAN ya esta vinculado a {current.SiigoDocumentId}; no se reemplazara por {requestedSiigoId}.");
            }

            return new ConciliacionDianActionResultDto
            {
                Message = message,
                IsSuccess = true,
                SiigoId = current.SiigoDocumentId,
                SiigoName = current.SiigoDocumentName,
                ResponseJson = responseJson,
                Row = current
            };
        }
        if (!IsDianSiigoInvoiceVerificationHold(current))
        {
            throw new InvalidOperationException(
                "El documento DIAN no conserva una escritura Siigo ambigua que pueda confirmarse por esta ruta.");
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
        if (!await HasActiveDianSupplierDocumentSiigoDocumentIdKeyAsync(metadata.LogicalName, ct)
            || !await HasActiveDianSupplierDocumentSiigoBusinessKeyAsync(metadata.LogicalName, ct))
        {
            throw new InvalidOperationException(
                "Dataverse no tiene activas las claves durables necesarias para confirmar el documento Siigo.");
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "EnviadoSiigo", force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            TruncateAccountCatalogText(
                string.Join(" ", new[] { message, responseJson }.Where(static value => !string.IsNullOrWhiteSpace(value))),
                1000),
            force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ConciliacionDianSiigoDocumentIdField,
            null,
            requestedSiigoId,
            force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ConciliacionDianSiigoDocumentNameField,
            null,
            requestedSiigoName,
            force: true);

        if (!await TryPatchExpenseAccountingRowAsync(
                metadata,
                normalizedRecordId,
                current.ConcurrencyToken,
                payload,
                ct))
        {
            throw new InvalidOperationException(
                "El documento DIAN cambio mientras se confirmaba la escritura Siigo; vuelve a ejecutar la verificacion.");
        }

        var updated = await GetConciliacionDianSupplierDocumentByIdAsync(normalizedRecordId, ct)
            ?? throw new InvalidOperationException("Dataverse acepto el cambio, pero no devolvio el documento confirmado.");
        if (!updated.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(updated.SiigoDocumentId, requestedSiigoId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dataverse no confirmo de forma durable la escritura Siigo verificada.");
        }

        return new ConciliacionDianActionResultDto
        {
            Message = message,
            IsSuccess = true,
            SiigoId = updated.SiigoDocumentId,
            SiigoName = updated.SiigoDocumentName,
            ResponseJson = responseJson,
            Row = updated
        };
    }

    private static bool IsDianSiigoSuccessfulResult(ConciliacionDianSupplierInvoiceRowDto row) =>
        !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
        || row.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase);

    private static bool IsDianSiigoInvoiceVerificationHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals(DianSiigoAmbiguousWriteState, StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains(DianSiigoAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsDianSiigoSupplierVerificationHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals(DianSiigoSupplierAmbiguousWriteState, StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains(DianSiigoSupplierAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase);

    private static void EnsureDianSupplierDocumentDurableRow(ConciliacionDianSupplierInvoiceRowDto row)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(row.Cufe)) missing.Add("CUFE");
        if (string.IsNullOrWhiteSpace(row.ExcelKey)) missing.Add("ExcelKey");
        if (string.IsNullOrWhiteSpace(row.SiigoBusinessKey)) missing.Add("SiigoBusinessKey");
        if (string.IsNullOrWhiteSpace(row.ReceptionDateValue)) missing.Add("FechaRecepcion");
        if (string.IsNullOrWhiteSpace(row.AutomationSource)) missing.Add("FuenteAutomatizacion");
        if (string.IsNullOrWhiteSpace(row.ConcurrencyToken)) missing.Add("ETag");
        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"El documento DIAN {FirstNonEmpty(row.RecordId, row.InvoiceNumber, "sin identificador")} "
            + $"no tiene valores durables para {string.Join(", ", missing)}. Se detuvo la escritura Siigo.");
    }

    private static bool HasDianSiigoAmbiguousWriteHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals(DianSiigoAmbiguousWriteState, StringComparison.OrdinalIgnoreCase)
        || HasDianSiigoSupplierWriteHold(row)
        || row.ReviewReason.Contains(DianSiigoAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase);

    private static string ResolveDianSiigoProtectedState(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals("ProcesandoSiigo", StringComparison.OrdinalIgnoreCase)
            ? "ProcesandoSiigo"
            : row.AutomationState.Equals(DianSiigoSupplierProcessingState, StringComparison.OrdinalIgnoreCase)
                ? DianSiigoSupplierProcessingState
                : HasDianSiigoSupplierWriteHold(row)
                    ? DianSiigoSupplierAmbiguousWriteState
                    : DianSiigoAmbiguousWriteState;

    private static bool HasDianSiigoSupplierWriteHold(ConciliacionDianSupplierInvoiceRowDto row) =>
        row.AutomationState.Equals(DianSiigoSupplierProcessingState, StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals(DianSiigoSupplierAmbiguousWriteState, StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains(DianSiigoSupplierAmbiguousWriteMarker, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<ConciliacionOptionDto>> GetConciliacionDianExpenseAccountOptionsAsync(
        CancellationToken ct)
    {
        var accounts = await GetConciliacionDianExpenseAccountCatalogAsync(ct);
        return accounts.Values
            .OrderBy(static account => account.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static account => new ConciliacionOptionDto
            {
                Value = account.Code,
                Label = RepairSpanishMojibakeText($"{account.Code} - {account.Name}")
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<ConciliacionOptionDto>> GetConciliacionAccountingAccountOptionsAsync(
        CancellationToken ct = default)
    {
        var accounts = await GetConciliacionAccountCatalogAsync(ct);
        return accounts.Values
            .Where(static account => account.Active)
            .OrderBy(static account => account.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static account => new ConciliacionOptionDto
            {
                Value = account.Code,
                Label = RepairSpanishMojibakeText($"{account.Code} - {account.Name}")
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

        var accounts = rows
            .Where(static row => row.Active && IsConciliacionDianExpenseAccount(row.Code, row.Type))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code))
            .GroupBy(static row => row.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var row = group.First();
                    return new ConciliacionAccountCatalogItem(
                        row.Code.Trim(),
                        ResolveAccountCatalogName(row.Code, row.Name),
                        row.Active);
                },
                StringComparer.OrdinalIgnoreCase);
        AddConciliacionRequiredExpenseAccounts(accounts);
        return accounts;
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
        EnsureDianSupplierDocumentDurabilitySchema(attributes);
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
        return IsConciliacionDianSupplierImportableDocument(row) ? row : null;
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
            DianSupplierDocumentSiigoBusinessKeyField,
            ConciliacionDianSiigoDocumentIdField,
            ConciliacionDianSiigoDocumentNameField,
            ConciliacionCreatedOnField,
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

    private static string NormalizeConciliacionCufeCude(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).ToArray());

    private static string NormalizeConciliacionSupplierInvoiceNumber(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool ConciliacionSupplierInvoiceNumberMatches(
        string expectedInvoiceNumber,
        ConciliacionDianSupplierInvoiceRowDto document)
    {
        if (string.IsNullOrWhiteSpace(expectedInvoiceNumber))
            return false;

        return new[]
            {
                document.InvoiceNumber,
                $"{document.Prefix}{document.Folio}",
                document.Folio
            }
            .Select(NormalizeConciliacionSupplierInvoiceNumber)
            .Any(candidate => !string.IsNullOrWhiteSpace(candidate)
                && string.Equals(candidate, expectedInvoiceNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreConciliacionSupplierTaxIdsEquivalent(string leftDigits, string rightDigits)
    {
        if (string.IsNullOrWhiteSpace(leftDigits) || string.IsNullOrWhiteSpace(rightDigits))
            return false;
        if (string.Equals(leftDigits, rightDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return (leftDigits.Length >= 9 && leftDigits.Length == rightDigits.Length + 1 && leftDigits.StartsWith(rightDigits, StringComparison.OrdinalIgnoreCase))
            || (rightDigits.Length >= 9 && rightDigits.Length == leftDigits.Length + 1 && rightDigits.StartsWith(leftDigits, StringComparison.OrdinalIgnoreCase));
    }
}
