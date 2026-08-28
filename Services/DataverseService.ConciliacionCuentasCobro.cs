using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.CuentasCobro;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CuentaCobroSiigoDocumentIdField = "cr07a_siigodocumentid";
    private const string CuentaCobroSiigoDocumentNameField = "cr07a_siigodocumentname";
    private const string CuentaCobroSiigoPaymentIdField = "cr07a_siigopaymentid";
    private const string CuentaCobroSiigoPaymentNameField = "cr07a_siigopaymentname";
    private const string CuentaCobroSiigoPaymentResponseField = "cr07a_siigopaymentresponse";
    private const string CuentaCobroSiigoResponseField = "cr07a_siigorespuesta";
    private const string CuentaCobroExpenseRecordSource = "expense";
    private const string CuentaCobroLegacyRecordSource = "cuenta-cobro";
    private const string CuentaCobroExpenseAutomationSource = "ConciliacionCuentaCobro";
    private const string CuentaCobroSupportDocumentProcessingState = "ProcesandoDocumentoSoporteSiigo";
    private const string CuentaCobroSupportDocumentVerificationState = "VerificacionDocumentoSoporteSiigoPendiente";
    private const string CuentaCobroSupportDocumentAmbiguousMarker = "[SIIGO_SUPPORT_DOCUMENT_WRITE_AMBIGUOUS]";

    public async Task<ConciliacionCuentaCobroRowDto> GetConciliacionCuentaCobroDocumentAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        CancellationToken ct = default)
    {
        if (request is null
            || (string.IsNullOrWhiteSpace(request.RecordId)
                && string.IsNullOrWhiteSpace(request.CashFlowRecordId)
                && string.IsNullOrWhiteSpace(request.CashFlowExternalKey)))
        {
            throw new InvalidOperationException("Debes indicar la cuenta de cobro o su salida bancaria.");
        }

        CuentaCobroAutomationRow? cuenta = null;
        if (!string.IsNullOrWhiteSpace(request.RecordId))
        {
            cuenta = IsConciliacionCuentaCobroExpenseSource(request.RecordSource)
                ? await GetConciliacionCuentaCobroExpenseRowByIdAsync(request.RecordId, ct)
                : await GetConciliacionCuentaCobroAutomationRowByIdAsync(request.RecordId, ct);
            if (cuenta is null)
                throw new InvalidOperationException("No encontramos la cuenta de cobro seleccionada.");
        }

        ConciliacionCashFlowRowDto? cashFlow = null;
        if (!string.IsNullOrWhiteSpace(request.CashFlowRecordId)
            || !string.IsNullOrWhiteSpace(request.CashFlowExternalKey))
        {
            cashFlow = await GetConciliacionCashFlowMovementForCuentaCobroAsync(
                request.CashFlowRecordId,
                request.CashFlowExternalKey,
                ct);
        }
        if (cuenta is null && cashFlow is null)
            throw new InvalidOperationException("No encontramos la salida bancaria seleccionada.");
        if (cuenta is not null && cashFlow is not null)
            EnsureConciliacionCuentaCobroCashFlowLink(cuenta, cashFlow);

        return BuildConciliacionCuentaCobroRow(cashFlow, cuenta, score: cashFlow is null ? 0 : 100);
    }

    public async Task<ConciliacionCuentaCobroActionResultDto> SaveConciliacionCuentaCobroExpenseAsync(
        ConciliacionCuentaCobroExpenseSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new InvalidOperationException("Debes indicar la cuenta de cobro que se registrara como gasto.");

        var cashFlow = await GetConciliacionCashFlowMovementForCuentaCobroAsync(
            request.CashFlowRecordId,
            request.CashFlowExternalKey,
            ct)
            ?? throw new InvalidOperationException("No encontramos la salida bancaria que origina la cuenta de cobro.");
        if (cashFlow.ExitValue <= 0m)
            throw new InvalidOperationException("La fila seleccionada no contiene una salida bancaria positiva.");
        if (!string.Equals(cashFlow.DetectedTypeKey, "cuenta-cobro", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La salida bancaria ya no esta clasificada como cuenta de cobro. "
                + "Recarga Conciliacion antes de registrar el gasto.");
        }

        var cashFlowRecordId = NormalizeGuid(cashFlow.RecordId, nameof(request.CashFlowRecordId));
        var recordSource = FirstNonEmpty(cashFlow.ExternalKey, request.CashFlowExternalKey).Trim();
        if (string.IsNullOrWhiteSpace(recordSource))
            recordSource = $"cashflow-record:{cashFlowRecordId}";
        if (recordSource.Length > 200)
            throw new InvalidOperationException("La clave externa del movimiento supera los 200 caracteres permitidos por Dataverse.");

        var receptor = TruncateAccountCatalogText((request.Receptor ?? "").Trim(), 100);
        var nit = TruncateAccountCatalogText((request.NitOCedula ?? "").Trim(), 100);
        if (string.IsNullOrWhiteSpace(receptor) || string.IsNullOrWhiteSpace(nit))
            throw new InvalidOperationException("Debes indicar el nombre y la identificacion del proveedor.");
        if (string.IsNullOrWhiteSpace((request.SiigoSupplierId ?? "").Trim())
            || string.IsNullOrWhiteSpace((request.SiigoSupplierName ?? "").Trim()))
        {
            throw new InvalidOperationException("Selecciona un proveedor activo de Siigo antes de guardar la cuenta de cobro.");
        }
        if (string.IsNullOrWhiteSpace(_rhCompanyName) || string.IsNullOrWhiteSpace(_rhCompanyNit))
            throw new InvalidOperationException("Falta configurar el nombre o NIT de Digital Tech para registrar el gasto.");

        if (!DateOnly.TryParseExact(
                request.FechaEmisionValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var emissionDate))
        {
            throw new InvalidOperationException("La fecha de emision de la cuenta de cobro no es valida.");
        }
        if (!DateOnly.TryParseExact(
                cashFlow.MovementDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var paymentDate))
        {
            throw new InvalidOperationException("El movimiento bancario no tiene una fecha de pago valida.");
        }
        if (emissionDate > paymentDate)
        {
            throw new InvalidOperationException(
                "La fecha de emision de la cuenta de cobro no puede ser posterior a la fecha del pago bancario.");
        }

        var total = RoundCurrency(request.ValorTotal);
        var vat = RoundCurrency(request.ValorIva);
        var payment = RoundCurrency(request.ValorPago);
        if (total <= 0m || payment <= 0m)
            throw new InvalidOperationException("El total y el valor pagado deben ser mayores a cero.");
        if (vat < 0m || vat > total)
            throw new InvalidOperationException("El IVA no puede ser negativo ni superior al total de la cuenta de cobro.");
        var allocationBase = RoundCurrency(total - vat);
        var cloudValue = RoundCurrency(request.CloudValue);
        var copiersValue = RoundCurrency(request.CopiersValue);
        if (cloudValue < 0m || copiersValue < 0m)
            throw new InvalidOperationException("Cloud y Copiers no pueden ser negativos.");
        if (Math.Abs(RoundCurrency(cloudValue + copiersValue) - allocationBase) > 0.01m)
        {
            throw new InvalidOperationException(
                "Cloud y Copiers deben sumar la base del gasto sin IVA. "
                + $"Base esperada: {allocationBase:N2}.");
        }
        if (!int.TryParse(request.CategoryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryValue)
            || categoryValue <= 0)
        {
            throw new InvalidOperationException("Selecciona una categoria para la cuenta de cobro.");
        }
        if (Math.Abs(payment - RoundCurrency(cashFlow.ExitValue)) > 0.01m)
            throw new InvalidOperationException("El valor pagado no coincide con la salida bancaria.");

        var normalizedRetentions = NormalizeCuentaCobroRetentions(
            (request.Retentions ?? Array.Empty<ConciliacionCuentaCobroRetentionDto>())
                .Select(static retention => new CuentaCobroRetentionDto
                {
                    Kind = retention.Kind,
                    Label = retention.Label,
                    TaxId = retention.TaxId > 0
                        ? retention.TaxId.ToString(CultureInfo.InvariantCulture)
                        : "",
                    AccountCode = retention.AccountCode,
                    BaseValue = retention.BaseValue,
                    Rate = retention.Rate,
                    Value = retention.Value
                })
                .ToArray(),
            total,
            legacyRate: 0m,
            legacyValue: 0m);
        var duplicateRetentionKind = normalizedRetentions
            .GroupBy(static retention => retention.Kind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateRetentionKind is not null)
            throw new InvalidOperationException($"Solo se permite una tarifa de {duplicateRetentionKind.Key}.");

        var totalRetentions = RoundCurrency(normalizedRetentions.Sum(static retention => retention.Value));
        if (Math.Abs(total - (payment + totalRetentions)) > 0.01m)
            throw new InvalidOperationException("El total debe ser igual al pago bancario mas las retenciones.");

        var accountCode = (request.AccountCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new InvalidOperationException("Selecciona una cuenta contable para el documento soporte.");
        var accounts = await GetConciliacionDianExpenseAccountCatalogAsync(ct);
        if (!accounts.TryGetValue(accountCode, out var account) || !account.Active)
            throw new InvalidOperationException("La cuenta contable seleccionada no existe o no esta activa en el catalogo Siigo.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        EnsureConciliacionCuentaCobroExpenseSchema(metadata, attributes, normalizedRetentions);
        if (!await HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct))
        {
            throw new InvalidOperationException(
                $"Dataverse no tiene activa la clave unica sobre {ConciliacionDianExcelKeyField}; "
                + "no se registrara el gasto para evitar duplicados.");
        }

        var current = await GetConciliacionCuentaCobroExpenseRowByExternalKeyAsync(
            metadata,
            attributes,
            recordSource,
            ct);
        var requestedExpenseId = IsConciliacionCuentaCobroExpenseSource(request.RecordSource)
            && !string.IsNullOrWhiteSpace(request.RecordId)
                ? NormalizeGuid(request.RecordId, nameof(request.RecordId))
                : "";
        if (!string.IsNullOrWhiteSpace(requestedExpenseId)
            && (current is null
                || !string.Equals(requestedExpenseId, current.RecordId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("La cuenta de cobro y la salida bancaria no corresponden al mismo gasto.");
        }
        if (current is not null)
        {
            EnsureConciliacionCuentaCobroEditableBeforeSiigo(current);
            var requestedEtag = (request.ConcurrencyToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(requestedEtag)
                || string.IsNullOrWhiteSpace(current.ConcurrencyToken)
                || !string.Equals(requestedEtag, current.ConcurrencyToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "El gasto cambio desde que abriste el editor. Recarga Conciliacion; "
                    + "no se sobrescribieron los cambios mas recientes.");
            }
        }
        else if (string.Equals(request.RecordSource, CuentaCobroLegacyRecordSource, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.RecordId))
        {
            var legacy = await GetConciliacionCuentaCobroAutomationRowByIdAsync(request.RecordId, ct)
                ?? throw new InvalidOperationException("No encontramos la cuenta de cobro heredada seleccionada.");
            EnsureConciliacionCuentaCobroEditableBeforeSiigo(legacy);
            EnsureConciliacionCuentaCobroCashFlowLink(legacy, cashFlow);
        }

        var payload = BuildConciliacionCuentaCobroExpensePayload(
            metadata,
            attributes,
            request,
            recordSource,
            receptor,
            nit,
            emissionDate,
            paymentDate,
            total,
            vat,
            payment,
            normalizedRetentions,
            account.Code,
            account.Name,
            isNew: current is null);
        await UpsertConciliacionCuentaCobroExpenseAsync(
            metadata,
            recordSource,
            current,
            request.ConcurrencyToken ?? "",
            payload,
            ct);

        var saved = await GetConciliacionCuentaCobroExpenseRowByExternalKeyAsync(
            metadata,
            attributes,
            recordSource,
            ct)
            ?? throw new InvalidOperationException("Dataverse acepto el guardado, pero no devolvio el gasto creado.");

        return new ConciliacionCuentaCobroActionResultDto
        {
            Message = current is null
                ? "Cuenta de cobro, distribucion y retenciones registradas en Dataverse. Pendiente de envio a Siigo."
                : "Gasto de cuenta de cobro actualizado sin crear duplicados.",
            IsSuccess = true,
            Row = BuildConciliacionCuentaCobroRow(cashFlow, saved, 100)
        };
    }

    public async Task<ConciliacionCuentaCobroActionResultDto> UpdateConciliacionCuentaCobroClassificationAsync(
        ConciliacionCuentaCobroClassificationRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar la cuenta de cobro a actualizar.");

        var accountCode = (request.AccountCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new InvalidOperationException("Selecciona una cuenta contable para el documento soporte.");

        var accounts = await GetConciliacionDianExpenseAccountCatalogAsync(ct);
        if (!accounts.TryGetValue(accountCode, out var account) || !account.Active)
            throw new InvalidOperationException("La cuenta contable seleccionada no existe o no esta activa en el catalogo Siigo.");

        var isExpense = IsConciliacionCuentaCobroExpenseSource(request.RecordSource);
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            isExpense ? _supplierExpensesTableName : CuentaCobroLogicalName,
            isExpense ? _supplierExpensesTableSetName : CuentaCobroFallbackEntitySetName,
            isExpense ? _supplierExpensesIdField : CuentaCobroFallbackIdField,
            isExpense ? "" : CuentaCobroFallbackPrimaryNameField,
            ct);
        var rawAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(
            metadata.LogicalName,
            ct);
        EnsureConciliacionCuentaCobroDocumentWorkflowSchema(rawAttributes, isExpense);
        var attributes = rawAttributes;
        attributes = isExpense
            ? BuildConciliacionCuentaCobroExpenseAttributeSet(metadata, attributes)
            : BuildCuentaCobroAutomationAttributeSet(metadata, attributes);

        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = isExpense
            ? await GetConciliacionCuentaCobroExpenseRowByIdAsync(normalizedRecordId, ct)
            : await GetConciliacionCuentaCobroAutomationRowByIdAsync(normalizedRecordId, ct);
        if (current is null)
            throw new InvalidOperationException("No encontramos la cuenta de cobro seleccionada.");
        EnsureConciliacionCuentaCobroEditableBeforeSiigo(current);
        var expectedEtag = (request.ConcurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(expectedEtag)
            || string.IsNullOrWhiteSpace(current.ConcurrencyToken)
            || !string.Equals(expectedEtag, current.ConcurrencyToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro cambio desde que abriste el formulario. "
                + "Recarga Conciliacion antes de asignar la cuenta contable.");
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, null, account.Code, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, null, account.Name, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "CuentaAsignada", force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            TruncateAccountCatalogText($"Cuenta contable asignada desde Conciliacion: {account.Code} - {account.Name}. Falta validar pre-Siigo.", 1000),
            force: true);

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos columnas disponibles para guardar la cuenta contable.");

        if (!await TryPatchExpenseAccountingRowAsync(
                metadata,
                normalizedRecordId,
                expectedEtag,
                payload,
                ct))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro cambio mientras se asignaba la cuenta contable. "
                + "Recarga Conciliacion.");
        }

        return new ConciliacionCuentaCobroActionResultDto
        {
            Message = "Cuenta contable guardada en Dataverse. Valida pre-Siigo para dejarla lista.",
            IsSuccess = true,
            Row = BuildConciliacionCuentaCobroRow(
                null,
                isExpense
                    ? await GetConciliacionCuentaCobroExpenseRowByIdAsync(normalizedRecordId, ct)
                    : await GetConciliacionCuentaCobroAutomationRowByIdAsync(normalizedRecordId, ct),
                0)
        };
    }

    public async Task<bool> TryClaimConciliacionCuentaCobroSupportDocumentForSiigoAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar la cuenta de cobro que se reservara para Siigo.");
        if (string.IsNullOrWhiteSpace(request.CashFlowRecordId)
            && string.IsNullOrWhiteSpace(request.CashFlowExternalKey))
        {
            throw new InvalidOperationException(
                "Debes indicar la salida bancaria vinculada antes de reservar el documento soporte.");
        }
        if (!string.IsNullOrWhiteSpace(request.RecordSource)
            && !IsConciliacionCuentaCobroExpenseSource(request.RecordSource)
            && !string.Equals(
                request.RecordSource.Trim(),
                CuentaCobroLegacyRecordSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La fuente de la cuenta de cobro no es valida.");
        }

        var isExpense = IsConciliacionCuentaCobroExpenseSource(request.RecordSource);
        if (!isExpense)
        {
            throw new InvalidOperationException(
                "El envio real exige el gasto canonico en cr07a_gastodelaempresa. "
                + "No se adquirio un claim sobre la tabla historica de cuentas de cobro.");
        }
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            isExpense ? _supplierExpensesTableName : CuentaCobroLogicalName,
            isExpense ? _supplierExpensesTableSetName : CuentaCobroFallbackEntitySetName,
            isExpense ? _supplierExpensesIdField : CuentaCobroFallbackIdField,
            isExpense ? "" : CuentaCobroFallbackPrimaryNameField,
            ct);
        var rawAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(
            metadata.LogicalName,
            ct);
        EnsureConciliacionCuentaCobroDocumentWorkflowSchema(rawAttributes, isExpense);
        var attributes = isExpense
            ? BuildConciliacionCuentaCobroExpenseAttributeSet(metadata, rawAttributes)
            : BuildCuentaCobroAutomationAttributeSet(metadata, rawAttributes);
        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = isExpense
            ? await GetConciliacionCuentaCobroExpenseRowByIdAsync(normalizedRecordId, ct)
            : await GetConciliacionCuentaCobroAutomationRowByIdAsync(normalizedRecordId, ct);
        if (current is null)
            return false;
        var cashFlow = await GetConciliacionCashFlowMovementForCuentaCobroAsync(
            request.CashFlowRecordId,
            request.CashFlowExternalKey,
            ct);
        if (cashFlow is null
            || !string.Equals(
                cashFlow.DetectedTypeKey,
                "cuenta-cobro",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        EnsureConciliacionCuentaCobroCashFlowLink(current, cashFlow);

        var etag = (request.ConcurrencyToken ?? "").Trim();
        if (string.IsNullOrWhiteSpace(etag)
            || string.IsNullOrWhiteSpace(current.ConcurrencyToken)
            || !string.Equals(etag, current.ConcurrencyToken, StringComparison.Ordinal))
        {
            return false;
        }
        if (HasConciliacionCuentaCobroSiigoCheckpoint(current)
            || HasConciliacionCuentaCobroSupportDocumentWriteHold(current))
        {
            return false;
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseAutomationStateField,
            null,
            CuentaCobroSupportDocumentProcessingState,
            force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            "Cuenta de cobro reservada atomicamente antes de crear el documento soporte en Siigo.",
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
            httpRequest => httpRequest.Headers.TryAddWithoutValidation("If-Match", etag));
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;
        if (response.IsSuccessStatusCode)
            return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning(
            "Dataverse rechazo el claim de documento soporte {RecordId} ({RecordSource}) con {StatusCode}: {Body}",
            normalizedRecordId,
            isExpense ? CuentaCobroExpenseRecordSource : CuentaCobroLegacyRecordSource,
            (int)response.StatusCode,
            body);
        throw new InvalidOperationException(
            $"Dataverse no permitio reservar atomicamente el documento soporte ({(int)response.StatusCode}).");
    }

    public async Task<ConciliacionCuentaCobroActionResultDto> MarkConciliacionCuentaCobroPreflightAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        bool ready,
        string message,
        IReadOnlyList<string> issues,
        string payloadJson = "",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.ConcurrencyToken))
        {
            throw new InvalidOperationException(
                "Falta la version de la cuenta de cobro que se valido. "
                + "Recarga Conciliacion antes de ejecutar la prevalidacion.");
        }
        var row = await UpdateConciliacionCuentaCobroAutomationStateAsync(
            request,
            ready ? "ListoSiigo" : "BloqueadoSiigo",
            message,
            siigoId: "",
            siigoName: "",
            siigoPaymentId: "",
            siigoPaymentName: "",
            responseJson: "",
            expectedConcurrencyToken: request.ConcurrencyToken,
            ct: ct);

        return new ConciliacionCuentaCobroActionResultDto
        {
            Message = message,
            IsSuccess = ready,
            IsReadyForSiigo = ready,
            TargetEndpoint = "DRY-RUN /v1/purchase-support-documents",
            PayloadJson = payloadJson,
            Issues = issues,
            Row = row
        };
    }

    public async Task<ConciliacionCuentaCobroActionResultDto> MarkConciliacionCuentaCobroSiigoResultAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string siigoPaymentId = "",
        string siigoPaymentName = "",
        string responseJson = "",
        string payloadJson = "",
        string stateOverride = "",
        string targetEndpoint = "/v1/purchase-support-documents",
        CancellationToken ct = default)
    {
        var row = await UpdateConciliacionCuentaCobroAutomationStateAsync(
            request,
            string.IsNullOrWhiteSpace(stateOverride) ? success ? "EnviadoSiigo" : "ErrorSiigo" : stateOverride,
            message,
            siigoId,
            siigoName,
            siigoPaymentId,
            siigoPaymentName,
            responseJson,
            expectedConcurrencyToken: "",
            ct: ct);

        return new ConciliacionCuentaCobroActionResultDto
        {
            Message = message,
            IsSuccess = success,
            IsReadyForSiigo = success,
            TargetEndpoint = targetEndpoint,
            PayloadJson = payloadJson,
            ResponseJson = responseJson,
            SiigoId = FirstNonEmpty(siigoPaymentId, siigoId),
            SiigoName = FirstNonEmpty(siigoPaymentName, siigoName),
            Issues = success || string.IsNullOrWhiteSpace(responseJson) ? Array.Empty<string>() : new[] { responseJson },
            Row = row
        };
    }

    private async Task<ConciliacionCuentaCobroSummaryDto> GetConciliacionCuentaCobroSummaryAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        IReadOnlyList<ConciliacionCashFlowRowDto> cashFlowRows,
        CancellationToken ct)
    {
        var cashRows = cashFlowRows
            .Where(static row => string.Equals(row.DetectedTypeKey, "cuenta-cobro", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var legacyTask = GetConciliacionCuentaCobroAutomationRowsAsync(startInclusive, endExclusive, ct);
        var expenseTask = GetConciliacionCuentaCobroExpenseRowsAsync(startInclusive, endExclusive, ct);
        await Task.WhenAll(legacyTask, expenseTask);
        var cuentaRows = expenseTask.Result
            .Concat(legacyTask.Result)
            .ToArray();
        var matchedRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactExpenseMatches = new CuentaCobroAutomationRow?[cashRows.Length];
        var rows = new List<ConciliacionCuentaCobroRowDto>();

        // Reserve durable expense links before legacy fuzzy matching. An expense
        // must never be consumed by a similar-looking movement that appears first.
        for (var index = 0; index < cashRows.Length; index++)
        {
            var cashRow = cashRows[index];
            var exactExpense = cuentaRows
                .Where(row => IsConciliacionCuentaCobroExpenseSource(row.RecordSource))
                .Where(row => !matchedRows.Contains(BuildConciliacionCuentaCobroMatchIdentity(row)))
                .FirstOrDefault(row => IsExactConciliacionCuentaCobroExpenseCashFlowLink(row, cashRow));
            if (exactExpense is null)
                continue;

            exactExpenseMatches[index] = exactExpense;
            matchedRows.Add(BuildConciliacionCuentaCobroMatchIdentity(exactExpense));
        }

        for (var index = 0; index < cashRows.Length; index++)
        {
            var cashRow = cashRows[index];
            var exactExpense = exactExpenseMatches[index];
            if (exactExpense is not null)
            {
                rows.Add(BuildConciliacionCuentaCobroRow(cashRow, exactExpense, 100));
                continue;
            }

            // Canonical expenses require their durable key. Only unlinked
            // legacy rows are eligible for heuristic matching.
            var match = cuentaRows
                .Where(row => !IsConciliacionCuentaCobroExpenseSource(row.RecordSource))
                .Where(row => !matchedRows.Contains(BuildConciliacionCuentaCobroMatchIdentity(row)))
                .Select(row => new
                {
                    Cuenta = row,
                    Score = ScoreConciliacionCuentaCobroMatch(cashRow, row)
                })
                .Where(static item => item.Score >= 75)
                .OrderByDescending(static item => item.Score)
                .ThenBy(item => Math.Abs(item.Cuenta.ValorPago - cashRow.ExitValue))
                .FirstOrDefault();

            if (match is not null)
            {
                matchedRows.Add(BuildConciliacionCuentaCobroMatchIdentity(match.Cuenta));
                rows.Add(BuildConciliacionCuentaCobroRow(cashRow, match.Cuenta, match.Score));
                continue;
            }

            rows.Add(BuildConciliacionCuentaCobroRow(cashRow, null, 0));
        }

        foreach (var cuenta in cuentaRows.Where(
                     row => !matchedRows.Contains(BuildConciliacionCuentaCobroMatchIdentity(row))))
            rows.Add(BuildConciliacionCuentaCobroRow(null, cuenta, 0));

        var lastRun = rows
            .Select(static row => ParseConciliacionDateTimeOffset(row.ModifiedOnDisplay))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Max();

        return new ConciliacionCuentaCobroSummaryDto
        {
            TotalRows = rows.Count,
            DetectedCashFlowRows = cashRows.Length,
            MatchedRows = rows.Count(static row => !string.IsNullOrWhiteSpace(row.RecordId) && !string.IsNullOrWhiteSpace(row.CashFlowRecordId)),
            PendingRows = rows.Count(static row => string.Equals(row.Stage, "detectadas", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Stage, "pendientes", StringComparison.OrdinalIgnoreCase)),
            ReadyForSiigo = rows.Count(static row => string.Equals(row.Stage, "prevalidacion", StringComparison.OrdinalIgnoreCase)),
            SentToSiigo = rows.Count(static row => string.Equals(row.Stage, "enviadas", StringComparison.OrdinalIgnoreCase) && !string.Equals(row.AutomationState, "ErrorSiigo", StringComparison.OrdinalIgnoreCase)),
            WithErrors = rows.Count(static row => string.Equals(row.AutomationState, "ErrorSiigo", StringComparison.OrdinalIgnoreCase)),
            TotalPaidValue = RoundCurrency(rows.Where(static row => !string.IsNullOrWhiteSpace(row.RecordId)).Sum(static row => row.ValorPago)),
            TotalGrossValue = RoundCurrency(rows.Where(static row => !string.IsNullOrWhiteSpace(row.RecordId)).Sum(static row => row.ValorTotal)),
            TotalReteFuenteValue = RoundCurrency(rows.Where(static row => !string.IsNullOrWhiteSpace(row.RecordId)).Sum(static row => row.ReteFuenteValor)),
            TotalRetentionsValue = RoundCurrency(rows
                .Where(static row => !string.IsNullOrWhiteSpace(row.RecordId))
                .Sum(static row => row.Retentions.Sum(static retention => retention.Value))),
            LastRunLabel = FormatConciliacionDateTimeDisplay(lastRun),
            Rows = rows
                .OrderBy(static row => row.Stage, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(static row => row.MovementDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Receptor, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private async Task<IReadOnlyList<CuentaCobroAutomationRow>> GetConciliacionCuentaCobroExpenseRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = BuildConciliacionCuentaCobroExpenseAttributeSet(
            metadata,
            await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct));
        if (!attributes.Contains(ConciliacionDianSourceField)
            || !attributes.Contains(ConciliacionDianExcelKeyField))
        {
            return Array.Empty<CuentaCobroAutomationRow>();
        }

        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var dateFilters = new[]
        {
            !string.IsNullOrWhiteSpace(fields.PaymentDateField.FieldName)
                ? BuildBillingDateFilter(
                    fields.PaymentDateField.FieldName,
                    fields.PaymentDateField.FieldKind,
                    startInclusive,
                    endExclusive)
                : "",
            !string.IsNullOrWhiteSpace(fields.EmissionDateField.FieldName)
                ? BuildBillingDateFilter(
                    fields.EmissionDateField.FieldName,
                    fields.EmissionDateField.FieldKind,
                    startInclusive,
                    endExclusive)
                : ""
        }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static value => $"({value})")
            .ToArray();
        if (dateFilters.Length == 0)
            return Array.Empty<CuentaCobroAutomationRow>();

        var sourceFilter =
            $"{ConciliacionDianSourceField} eq '{EscapeOdataLiteral(CuentaCobroExpenseAutomationSource)}'";
        var filter = $"{sourceFilter} and ({string.Join(" or ", dateFilters)})";
        var select = BuildConciliacionCuentaCobroExpenseSelect(metadata, attributes);
        var orderField = FirstNonEmpty(
            fields.PaymentDateField.FieldName,
            fields.EmissionDateField.FieldName);
        var url =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}"
            + $"&$filter={Uri.EscapeDataString(filter)}&$orderby={orderField} desc";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(row => ParseConciliacionCuentaCobroExpenseRow(row, metadata, attributes))
            .Where(static row => row is not null)
            .Cast<CuentaCobroAutomationRow>()
            .ToArray();
    }

    private async Task<CuentaCobroAutomationRow?> GetConciliacionCuentaCobroExpenseRowByIdAsync(
        string recordId,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            _supplierExpensesTableName,
            _supplierExpensesTableSetName,
            _supplierExpensesIdField,
            "",
            ct);
        var attributes = BuildConciliacionCuentaCobroExpenseAttributeSet(
            metadata,
            await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct));
        var select = BuildConciliacionCuentaCobroExpenseSelect(metadata, attributes);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);
        using var document = JsonDocument.Parse(json);
        var row = ParseConciliacionCuentaCobroExpenseRow(document.RootElement, metadata, attributes);
        if (row is null
            || !string.Equals(
                row.AutomationSource,
                CuentaCobroExpenseAutomationSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El gasto seleccionado no fue creado por el flujo de cuentas de cobro.");
        }

        return row;
    }

    private async Task<CuentaCobroAutomationRow?> GetConciliacionCuentaCobroExpenseRowByExternalKeyAsync(
        RhEntityMetadata metadata,
        HashSet<string> attributes,
        string externalKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalKey))
            return null;

        var select = BuildConciliacionCuentaCobroExpenseSelect(metadata, attributes);
        var filter =
            $"{ConciliacionDianExcelKeyField} eq '{EscapeOdataLiteral(externalKey.Trim())}'"
            + $" and {ConciliacionDianSourceField} eq '{EscapeOdataLiteral(CuentaCobroExpenseAutomationSource)}'";
        var url =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}"
            + $"&$filter={Uri.EscapeDataString(filter)}&$top=2";
        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        if (rows.Count > 1)
        {
            throw new InvalidOperationException(
                "Dataverse devolvio mas de un gasto para la misma salida bancaria. "
                + "Se detuvo el proceso para no actualizar un registro ambiguo.");
        }

        return rows.Count == 0
            ? null
            : ParseConciliacionCuentaCobroExpenseRow(rows[0], metadata, attributes);
    }

    private string BuildConciliacionCuentaCobroExpenseSelect(
        RhEntityMetadata metadata,
        HashSet<string> attributes)
    {
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        return BuildConciliacionSelectClause(
            metadata,
            attributes,
            BuildConciliacionCuentaCobroExpenseFields(metadata, fields));
    }

    private static IEnumerable<string> BuildConciliacionCuentaCobroExpenseFields(
        RhEntityMetadata metadata,
        TaxExpenseFieldMap fields) =>
        new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            fields.IssuerNameField,
            ConciliacionDianIssuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            CuentaCobroObservacionesField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.TotalField,
            fields.PaymentValueField,
            DashboardExpenseTotalBeforeVatField,
            fields.VatField,
            fields.ReteFuenteField,
            fields.ReteIcaField,
            DianSupplierDocumentReteIvaField,
            fields.CloudField,
            fields.CopiersField,
            DashboardExpenseCategoryField,
            CuentaCobroRetentionsJsonField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            ConciliacionDianSourceField,
            ConciliacionDianExcelKeyField,
            ConciliacionDianDocumentTypeField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField,
            CuentaCobroSiigoDocumentIdField,
            CuentaCobroSiigoDocumentNameField,
            CuentaCobroSiigoPaymentIdField,
            CuentaCobroSiigoPaymentNameField,
            CuentaCobroSiigoPaymentResponseField,
            CuentaCobroSiigoResponseField,
            ConciliacionModifiedOnField
        };

    private static HashSet<string> BuildConciliacionCuentaCobroExpenseAttributeSet(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var values = new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase)
        {
            metadata.PrimaryIdField
        };
        if (!string.IsNullOrWhiteSpace(metadata.PrimaryNameField))
            values.Add(metadata.PrimaryNameField);
        return values;
    }

    private CuentaCobroAutomationRow? ParseConciliacionCuentaCobroExpenseRow(
        JsonElement item,
        RhEntityMetadata metadata,
        HashSet<string> attributes)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var total = RoundCurrency(ReadDecimal(item, fields.TotalField) ?? 0m);
        var vat = RoundCurrency(ReadDecimal(item, fields.VatField) ?? 0m);
        var payment = RoundCurrency(ReadDecimal(item, fields.PaymentValueField) ?? 0m);
        var retentions = ParseConciliacionCuentaCobroExpenseRetentions(item, attributes, fields, total);
        var totalRetentions = RoundCurrency(retentions.Sum(static retention => retention.Value));
        var reteFuente = retentions.FirstOrDefault(
            static retention => string.Equals(retention.Kind, "ReteFuente", StringComparison.OrdinalIgnoreCase));
        var emissionDate = ReadDateOnly(item, fields.EmissionDateField.FieldName);
        var paymentDate = ReadDateOnly(item, fields.PaymentDateField.FieldName);
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, ConciliacionModifiedOnField));

        return new CuentaCobroAutomationRow
        {
            RecordId = recordId,
            RecordSource = CuentaCobroExpenseRecordSource,
            AutomationSource = ReadString(item, ConciliacionDianSourceField).Trim(),
            CashFlowRecordId = ParseConciliacionCuentaCobroExpenseCashFlowRecordId(
                ReadString(item, ConciliacionDianExcelKeyField)),
            CashFlowExternalKey = ReadString(item, ConciliacionDianExcelKeyField).Trim(),
            ConcurrencyToken = ReadString(item, "@odata.etag").Trim(),
            Receptor = ReadString(item, fields.IssuerNameField).Trim(),
            NitOCedula = ReadString(item, ConciliacionDianIssuerNitField).Trim(),
            SiigoSupplierId = ReadString(item, DianSupplierDocumentSiigoSupplierIdField).Trim(),
            SiigoSupplierName = ReadString(item, DianSupplierDocumentSiigoSupplierNameField).Trim(),
            Observaciones = RepairSpanishMojibakeText(ReadString(item, CuentaCobroObservacionesField)).Trim(),
            FechaEmisionValue = emissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            FechaEmisionDisplay = emissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            FechaPagoValue = paymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            FechaPagoDisplay = paymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ValorTotal = total,
            ValorIva = vat,
            ValorPago = payment,
            CloudValue = RoundCurrency(ReadDecimal(item, fields.CloudField) ?? 0m),
            CopiersValue = RoundCurrency(ReadDecimal(item, fields.CopiersField) ?? 0m),
            CategoryValue = ReadString(item, DashboardExpenseCategoryField).Trim(),
            CategoryLabel = RepairSpanishMojibakeText(FirstNonEmpty(
                ReadString(item, $"{DashboardExpenseCategoryField}{FormattedValueAnnotationSuffix}"),
                ReadString(item, DashboardExpenseCategoryField))).Trim(),
            ReteFuentePorcentaje = reteFuente?.Rate ?? 0m,
            ReteFuenteValor = reteFuente?.Value ?? 0m,
            Retentions = retentions,
            TotalesCuadran = Math.Abs(total - (payment + totalRetentions)) <= 0.01m,
            AccountCode = ReadString(item, ExpenseAccountCodeField).Trim(),
            AccountName = ResolveAccountCatalogName(
                ReadString(item, ExpenseAccountCodeField),
                ReadString(item, ExpenseAccountNameField)),
            AutomationState = ReadString(item, ExpenseAutomationStateField).Trim(),
            ReviewReason = RepairSpanishMojibakeText(ReadString(item, ExpenseReviewReasonField)).Trim(),
            SiigoDocumentId = ReadString(item, CuentaCobroSiigoDocumentIdField).Trim(),
            SiigoDocumentName = ReadString(item, CuentaCobroSiigoDocumentNameField).Trim(),
            SiigoPaymentId = ReadString(item, CuentaCobroSiigoPaymentIdField).Trim(),
            SiigoPaymentName = ReadString(item, CuentaCobroSiigoPaymentNameField).Trim(),
            ModifiedOnDisplay = FormatConciliacionDateTimeDisplay(modifiedOn),
            CashFlowAmountHint = payment
        };
    }

    private static IReadOnlyList<CuentaCobroRetentionDto> ParseConciliacionCuentaCobroExpenseRetentions(
        JsonElement item,
        ISet<string> attributes,
        TaxExpenseFieldMap fields,
        decimal total)
    {
        if (attributes.Contains(CuentaCobroRetentionsJsonField))
        {
            var rawJson = ReadString(item, CuentaCobroRetentionsJsonField);
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<CuentaCobroRetentionDto>>(
                        rawJson,
                        CuentaCobroRetentionJsonOptions);
                    if (parsed is not null)
                        return NormalizeCuentaCobroRetentions(parsed, total, 0m, 0m);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    // Un JSON historico invalido no debe ocultar los valores escalares.
                }
            }
        }

        var retentions = new List<CuentaCobroRetentionDto>();
        AddConciliacionCuentaCobroExpenseScalarRetention(
            retentions,
            "ReteFuente",
            ReadDecimal(item, fields.ReteFuenteField) ?? 0m,
            total);
        AddConciliacionCuentaCobroExpenseScalarRetention(
            retentions,
            "ReteICA",
            ReadDecimal(item, fields.ReteIcaField) ?? 0m,
            total);
        AddConciliacionCuentaCobroExpenseScalarRetention(
            retentions,
            "RteIVA",
            ReadDecimal(item, DianSupplierDocumentReteIvaField) ?? 0m,
            total);
        return retentions;
    }

    private static void AddConciliacionCuentaCobroExpenseScalarRetention(
        ICollection<CuentaCobroRetentionDto> target,
        string kind,
        decimal rawValue,
        decimal total)
    {
        var value = RoundCurrency(rawValue);
        if (value <= 0m || total <= 0m)
            return;

        var divisor = string.Equals(kind, "ReteICA", StringComparison.OrdinalIgnoreCase) ? 1000m : 100m;
        target.Add(new CuentaCobroRetentionDto
        {
            Kind = kind,
            Label = ResolveCuentaCobroRetentionLabel(kind),
            BaseValue = total,
            Rate = RoundRetentionRate(value * divisor / total),
            Value = value
        });
    }

    private void EnsureConciliacionCuentaCobroExpenseSchema(
        RhEntityMetadata metadata,
        HashSet<string> attributes,
        IReadOnlyList<CuentaCobroRetentionDto> retentions)
    {
        EnsureConciliacionCuentaCobroExpenseWorkflowSchema(attributes);
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var required = new[]
        {
            metadata.PrimaryNameField,
            fields.InvoiceNumberField,
            ConciliacionDianExcelKeyField,
            ConciliacionDianSourceField,
            ConciliacionDianDocumentTypeField,
            fields.IssuerNameField,
            ConciliacionDianIssuerNitField,
            fields.RecipientNameField,
            fields.RecipientNitField,
            fields.EmissionDateField.FieldName,
            fields.PaymentDateField.FieldName,
            fields.TotalField,
            fields.PaymentValueField,
            DashboardExpenseTotalBeforeVatField,
            fields.VatField,
            fields.CloudField,
            fields.CopiersField,
            DashboardExpenseCategoryField,
            DianSupplierDocumentSiigoSupplierIdField,
            DianSupplierDocumentSiigoSupplierNameField
        };
        var missing = required
            .Where(field => string.IsNullOrWhiteSpace(field) || !attributes.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var retention in retentions)
        {
            var field = retention.Kind switch
            {
                "ReteFuente" => fields.ReteFuenteField,
                "ReteICA" => fields.ReteIcaField,
                "RteIVA" => DianSupplierDocumentReteIvaField,
                _ => CuentaCobroRetentionsJsonField
            };
            if (!attributes.Contains(field))
                missing.Add(field);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "La tabla de gastos no tiene todas las columnas requeridas para guardar una cuenta de cobro: "
                + string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase))
                + ". No se guardo un registro parcial.");
        }
        if (!string.Equals(fields.TotalField, fields.PaymentValueField, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(
                    fields.EmissionDateField.FieldName,
                    fields.PaymentDateField.FieldName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException(
                "La tabla de gastos no tiene campos independientes para fecha de emision y fecha de pago. "
                + "No se guardo la cuenta de cobro.");
        }

        throw new InvalidOperationException(
            "La tabla de gastos no tiene campos independientes para total bruto y valor pagado. "
            + "No se guardo la cuenta de cobro porque las retenciones podrian sobrescribir el total.");
    }

    private static void EnsureConciliacionCuentaCobroExpenseWorkflowSchema(ISet<string> attributes)
    {
        var required = new[]
        {
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            CuentaCobroRetentionsJsonField,
            CuentaCobroSiigoDocumentIdField,
            CuentaCobroSiigoDocumentNameField,
            CuentaCobroSiigoPaymentIdField,
            CuentaCobroSiigoPaymentNameField,
            CuentaCobroSiigoResponseField,
            CuentaCobroSiigoPaymentResponseField
        };
        var missing = required
            .Where(field => !attributes.Contains(field))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "La tabla de gastos no tiene las columnas tecnicas del flujo de cuentas de cobro: "
                + string.Join(", ", missing)
                + ".");
        }
    }

    private static void EnsureConciliacionCuentaCobroDocumentWorkflowSchema(
        ISet<string> attributes,
        bool isExpense)
    {
        if (isExpense)
        {
            EnsureConciliacionCuentaCobroExpenseWorkflowSchema(attributes);
            return;
        }

        var required = new[]
        {
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            CuentaCobroSiigoDocumentIdField,
            CuentaCobroSiigoDocumentNameField,
            CuentaCobroSiigoPaymentIdField,
            CuentaCobroSiigoPaymentNameField,
            CuentaCobroSiigoResponseField,
            CuentaCobroSiigoPaymentResponseField
        };
        var missing = required
            .Where(field => !attributes.Contains(field))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "La tabla heredada de cuentas de cobro no tiene los checkpoints requeridos para Siigo: "
                + string.Join(", ", missing)
                + ". No se adquirio el claim.");
        }
    }

    private static bool HasConciliacionCuentaCobroSiigoCheckpoint(CuentaCobroAutomationRow row) =>
        !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
        || !string.IsNullOrWhiteSpace(row.SiigoDocumentName)
        || !string.IsNullOrWhiteSpace(row.SiigoPaymentId)
        || !string.IsNullOrWhiteSpace(row.SiigoPaymentName)
        || row.AutomationState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals("DocumentoSoporteSiigo", StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals("Conciliado", StringComparison.OrdinalIgnoreCase);

    private static bool HasConciliacionCuentaCobroSupportDocumentIdentity(CuentaCobroAutomationRow row) =>
        !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
        || !string.IsNullOrWhiteSpace(row.SiigoDocumentName);

    private static bool HasConciliacionCuentaCobroSupportDocumentWriteHold(CuentaCobroAutomationRow row) =>
        row.AutomationState.Equals(
            CuentaCobroSupportDocumentProcessingState,
            StringComparison.OrdinalIgnoreCase)
        || row.AutomationState.Equals(
            CuentaCobroSupportDocumentVerificationState,
            StringComparison.OrdinalIgnoreCase)
        || row.ReviewReason.Contains(
            CuentaCobroSupportDocumentAmbiguousMarker,
            StringComparison.OrdinalIgnoreCase);

    private static void EnsureConciliacionCuentaCobroEditableBeforeSiigo(CuentaCobroAutomationRow row)
    {
        if (HasConciliacionCuentaCobroSiigoCheckpoint(row))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro ya tiene documento o pago Siigo y no puede editarse desde este formulario.");
        }
        if (HasConciliacionCuentaCobroSupportDocumentWriteHold(row))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro conserva una reserva o verificacion pendiente de Siigo y no puede editarse.");
        }
    }

    private static void EnsureConciliacionCuentaCobroSupportDocumentTransition(
        CuentaCobroAutomationRow current,
        string nextState,
        string siigoDocumentId,
        string siigoDocumentName)
    {
        var targetState = (nextState ?? "").Trim();
        var incomingDocumentId = (siigoDocumentId ?? "").Trim();
        var incomingDocumentName = (siigoDocumentName ?? "").Trim();
        var hasIncomingDocument = !string.IsNullOrWhiteSpace(incomingDocumentId)
            || !string.IsNullOrWhiteSpace(incomingDocumentName);
        var currentIsProcessing = current.AutomationState.Equals(
            CuentaCobroSupportDocumentProcessingState,
            StringComparison.OrdinalIgnoreCase);
        var currentNeedsVerification = current.AutomationState.Equals(
                CuentaCobroSupportDocumentVerificationState,
                StringComparison.OrdinalIgnoreCase)
            || current.ReviewReason.Contains(
                CuentaCobroSupportDocumentAmbiguousMarker,
                StringComparison.OrdinalIgnoreCase);
        var targetNeedsVerification = targetState.Equals(
            CuentaCobroSupportDocumentVerificationState,
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(current.SiigoDocumentId)
            && !string.IsNullOrWhiteSpace(incomingDocumentId)
            && !string.Equals(
                current.SiigoDocumentId,
                incomingDocumentId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro ya esta vinculada a otro documento soporte Siigo.");
        }
        if (currentNeedsVerification && !targetNeedsVerification && !hasIncomingDocument)
        {
            throw new InvalidOperationException(
                "El resultado del documento soporte sigue pendiente de verificar en Siigo. "
                + "El hold no se libero.");
        }
        if (targetNeedsVerification && !currentIsProcessing && !currentNeedsVerification)
        {
            throw new InvalidOperationException(
                "Solo una ejecucion que conserva el claim puede marcar una escritura Siigo como ambigua.");
        }
        if (currentIsProcessing
            && !targetNeedsVerification
            && !hasIncomingDocument
            && !targetState.Equals("ErrorSiigo", StringComparison.OrdinalIgnoreCase)
            && !targetState.Equals(
                CuentaCobroSupportDocumentProcessingState,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro conserva un claim de documento soporte. "
                + "Solo puede cerrarse con un documento confirmado, un error definitivo o verificacion pendiente.");
        }
        if (hasIncomingDocument
            && !HasConciliacionCuentaCobroSupportDocumentIdentity(current)
            && !currentIsProcessing
            && !currentNeedsVerification)
        {
            throw new InvalidOperationException(
                "La cuenta de cobro no conserva el claim atomico requerido para asociar el documento soporte Siigo.");
        }
        if (targetState.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            && !hasIncomingDocument
            && !HasConciliacionCuentaCobroSupportDocumentIdentity(current))
        {
            throw new InvalidOperationException(
                "No se marcara la cuenta de cobro como enviada sin un identificador durable del documento soporte.");
        }
    }

    private Dictionary<string, object?> BuildConciliacionCuentaCobroExpensePayload(
        RhEntityMetadata metadata,
        HashSet<string> attributes,
        ConciliacionCuentaCobroExpenseSaveRequest request,
        string recordSource,
        string receptor,
        string nit,
        DateOnly emissionDate,
        DateOnly paymentDate,
        decimal total,
        decimal vat,
        decimal payment,
        IReadOnlyList<CuentaCobroRetentionDto> retentions,
        string accountCode,
        string accountName,
        bool isNew)
    {
        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(recordSource)))
            .ToLowerInvariant();
        var documentNumber = $"CC-{emissionDate:yyyyMMdd}-{hash[..12]}";
        var reteFuente = RoundCurrency(retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteFuente", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value));
        var reteIca = RoundCurrency(retentions
            .Where(static retention => string.Equals(retention.Kind, "ReteICA", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value));
        var reteIva = RoundCurrency(retentions
            .Where(static retention => string.Equals(retention.Kind, "RteIVA", StringComparison.OrdinalIgnoreCase))
            .Sum(static retention => retention.Value));
        var fields = ResolveTaxExpenseFieldMap(metadata, attributes);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, documentNumber, force: true);
        SetAccountCatalogValue(payload, attributes, fields.InvoiceNumberField, null, documentNumber, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianDocumentTypeField, null, "Documento soporte", force: true);
        SetAccountCatalogValue(payload, attributes, fields.IssuerNameField, null, receptor, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianIssuerNitField, null, nit, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierIdField, null, request.SiigoSupplierId.Trim(), force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentSiigoSupplierNameField, null, request.SiigoSupplierName.Trim(), force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            fields.RecipientNameField,
            null,
            TruncateAccountCatalogText(_rhCompanyName, 100),
            force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            fields.RecipientNitField,
            null,
            TruncateAccountCatalogText(_rhCompanyNit, 100),
            force: true);
        SetAccountCatalogValue(payload, attributes, fields.EmissionDateField.FieldName, null, emissionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, fields.PaymentDateField.FieldName, null, paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, fields.TotalField, (decimal?)null, total, force: true);
        // cr07a_valorpago is a legacy text column in the canonical expense table.
        // Keep its numeric representation invariant so Dataverse does not reject the
        // idempotent expense before the Siigo document/payment saga begins.
        if (string.Equals(
                fields.PaymentValueField,
                DashboardExpensePaymentValueField,
                StringComparison.OrdinalIgnoreCase))
        {
            SetAccountCatalogValue(
                payload,
                attributes,
                fields.PaymentValueField,
                null,
                payment.ToString("0.00", CultureInfo.InvariantCulture),
                force: true);
        }
        else
        {
            SetAccountCatalogValue(payload, attributes, fields.PaymentValueField, (decimal?)null, payment, force: true);
        }
        SetAccountCatalogValue(
            payload,
            attributes,
            DashboardExpenseTotalBeforeVatField,
            (decimal?)null,
            RoundCurrency(total - vat),
            force: true);
        SetAccountCatalogValue(payload, attributes, fields.VatField, (decimal?)null, vat, force: true);
        SetAccountCatalogValue(payload, attributes, fields.CloudField, (decimal?)null, RoundCurrency(request.CloudValue), force: true);
        SetAccountCatalogValue(payload, attributes, fields.CopiersField, (decimal?)null, RoundCurrency(request.CopiersValue), force: true);
        _ = int.TryParse(request.CategoryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryValue);
        SetAccountCatalogValue<int?>(payload, attributes, DashboardExpenseCategoryField, null, categoryValue, force: true);
        SetAccountCatalogValue(payload, attributes, fields.ReteFuenteField, (decimal?)null, reteFuente, force: true);
        SetAccountCatalogValue(payload, attributes, fields.ReteIcaField, (decimal?)null, reteIca, force: true);
        SetAccountCatalogValue(payload, attributes, DianSupplierDocumentReteIvaField, (decimal?)null, reteIva, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountCodeField, null, accountCode, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseAccountNameField, null, accountName, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianSourceField, null, CuentaCobroExpenseAutomationSource, force: true);
        SetAccountCatalogValue(payload, attributes, ConciliacionDianExcelKeyField, null, recordSource, force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            CuentaCobroObservacionesField,
            null,
            TruncateAccountCatalogText(request.Observaciones, 1000),
            force: true);
        if (attributes.Contains(CuentaCobroRetentionsJsonField))
        {
            payload[CuentaCobroRetentionsJsonField] = JsonSerializer.Serialize(
                retentions,
                CuentaCobroRetentionJsonOptions);
        }
        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, "CuentaAsignada", force: true);
        SetAccountCatalogValue(
            payload,
            attributes,
            ExpenseReviewReasonField,
            null,
            isNew
                ? "Gasto creado desde una salida bancaria de cuenta de cobro. Pendiente de prevalidacion Siigo."
                : "Gasto editado antes de Siigo. Requiere una nueva prevalidacion del documento soporte.",
            force: true);

        return payload;
    }

    private async Task UpsertConciliacionCuentaCobroExpenseAsync(
        RhEntityMetadata metadata,
        string recordSource,
        CuentaCobroAutomationRow? current,
        string concurrencyToken,
        IReadOnlyDictionary<string, object?> sourcePayload,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        payload.Remove(ConciliacionDianExcelKeyField);
        if (current is not null)
        {
            var etag = (concurrencyToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(etag)
                || !string.Equals(etag, current.ConcurrencyToken, StringComparison.Ordinal)
                || !await TryPatchExpenseAccountingRowAsync(
                    metadata,
                    current.RecordId,
                    etag,
                    payload,
                    ct))
            {
                throw new InvalidOperationException(
                    "El gasto cambio mientras se guardaba. Recarga Conciliacion; "
                    + "no se aplico una actualizacion last-write-wins.");
            }
            return;
        }

        var alternateKey = Uri.EscapeDataString(
            recordSource.Replace("'", "''", StringComparison.Ordinal));
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({ConciliacionDianExcelKeyField}='{alternateKey}')",
            "PATCH",
            ct,
            content,
            request => request.Headers.TryAddWithoutValidation("If-None-Match", "*"));
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new InvalidOperationException(
                "Otra solicitud creo el gasto al mismo tiempo. Recarga Conciliacion; "
                + "no se sobrescribio el registro concurrente.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Dataverse rechazo el registro idempotente del gasto ({(int)response.StatusCode}): {body}");
    }

    private static bool IsConciliacionCuentaCobroExpenseSource(string? recordSource) =>
        string.Equals(recordSource?.Trim(), CuentaCobroExpenseRecordSource, StringComparison.OrdinalIgnoreCase);

    private static void EnsureConciliacionCuentaCobroCashFlowLink(
        CuentaCobroAutomationRow cuenta,
        ConciliacionCashFlowRowDto cashFlow)
    {
        if (IsConciliacionCuentaCobroExpenseSource(cuenta.RecordSource))
        {
            if (IsExactConciliacionCuentaCobroExpenseCashFlowLink(cuenta, cashFlow))
                return;

            throw new InvalidOperationException(
                "El gasto seleccionado no pertenece a la salida bancaria indicada. "
                + "Se bloqueo el ensamblado cruzado antes de Siigo.");
        }

        if (!string.Equals(
                cuenta.RecordSource,
                CuentaCobroLegacyRecordSource,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La fuente de la cuenta de cobro no permite validar su movimiento.");
        }

        var amountMatches = Math.Abs(cashFlow.ExitValue - cuenta.ValorPago) <= 0.01m;
        var dateMatches = DateOnly.TryParseExact(
                cashFlow.MovementDateValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var movementDate)
            && DateOnly.TryParseExact(
                cuenta.FechaPagoValue,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var paymentDate)
            && Math.Abs(movementDate.DayNumber - paymentDate.DayNumber) <= 10;
        var haystack = NormalizeConciliacionLookupText(
            $"{cashFlow.Description} {cashFlow.Recipient} {cashFlow.Observations} "
            + $"{cashFlow.ThirdPartyName} {cashFlow.ThirdPartyIdentification}");
        var nit = NormalizeConciliacionDigits(cuenta.NitOCedula);
        var hasNitEvidence = !string.IsNullOrWhiteSpace(nit)
            && NormalizeConciliacionDigits(haystack).Contains(nit, StringComparison.OrdinalIgnoreCase);
        var nameTokens = NormalizeConciliacionLookupText(cuenta.Receptor)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiredNameTokens = nameTokens.Length <= 1 ? nameTokens.Length : 2;
        var matchingNameTokens = nameTokens.Count(
            token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
        var hasNameEvidence = requiredNameTokens > 0
            && matchingNameTokens >= requiredNameTokens;

        if (amountMatches && dateMatches && (hasNitEvidence || hasNameEvidence))
            return;

        throw new InvalidOperationException(
            "La cuenta de cobro heredada no tiene un vinculo fuerte e inequivoco con esa salida bancaria. "
            + "Se exige coincidencia de pago, fecha y NIT o nombre antes de continuar.");
    }

    private static bool IsExactConciliacionCuentaCobroExpenseCashFlowLink(
        CuentaCobroAutomationRow cuenta,
        ConciliacionCashFlowRowDto cashFlow)
    {
        if (!IsConciliacionCuentaCobroExpenseSource(cuenta.RecordSource))
            return false;

        var storedKey = (cuenta.CashFlowExternalKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(storedKey))
            return false;

        var externalKey = (cashFlow.ExternalKey ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(externalKey)
            && string.Equals(storedKey, externalKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Guid.TryParse(cashFlow.RecordId, out var movementId)
            && string.Equals(
                storedKey,
                $"cashflow-record:{movementId:D}",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConciliacionCuentaCobroMatchIdentity(CuentaCobroAutomationRow row)
        => $"{row.RecordSource.Trim()}:{row.RecordId.Trim()}";

    private static string ParseConciliacionCuentaCobroExpenseCashFlowRecordId(string? recordSource)
    {
        const string prefix = "cashflow-record:";
        var value = (recordSource ?? "").Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(value[prefix.Length..], out var recordId))
        {
            return "";
        }

        return recordId.ToString("D");
    }

    private async Task<IReadOnlyList<CuentaCobroAutomationRow>> GetConciliacionCuentaCobroAutomationRowsAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CuentaCobroLogicalName,
            CuentaCobroFallbackEntitySetName,
            CuentaCobroFallbackIdField,
            CuentaCobroFallbackPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCuentaCobroAutomationAttributeSet(metadata, attributes);
        var select = BuildCuentaCobroAutomationSelect(metadata, attributes);
        var filter = BuildCuentaCobroAutomationPeriodFilter(attributes, startInclusive, endExclusive);
        var orderField = attributes.Contains(CuentaCobroFechaEmisionField) ? CuentaCobroFechaEmisionField : CuentaCobroPeriodFallbackField;
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}";
        if (!string.IsNullOrWhiteSpace(filter))
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        url += $"&$orderby={orderField} desc";

        var rows = await GetDataverseAppEntitiesAsync(url, ct, AddFormattedValueHeaders);
        return rows
            .Select(row => ParseCuentaCobroAutomationRow(row, metadata, attributes))
            .Where(static row => row is not null)
            .Cast<CuentaCobroAutomationRow>()
            .ToArray();
    }

    private async Task<CuentaCobroAutomationRow?> GetConciliacionCuentaCobroAutomationRowByIdAsync(
        string recordId,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CuentaCobroLogicalName,
            CuentaCobroFallbackEntitySetName,
            CuentaCobroFallbackIdField,
            CuentaCobroFallbackPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCuentaCobroAutomationAttributeSet(metadata, attributes);
        var select = BuildCuentaCobroAutomationSelect(metadata, attributes);
        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})?$select={select}",
            ct,
            AddFormattedValueHeaders);
        using var document = JsonDocument.Parse(json);
        return ParseCuentaCobroAutomationRow(document.RootElement, metadata, attributes);
    }

    private async Task<ConciliacionCuentaCobroRowDto> UpdateConciliacionCuentaCobroAutomationStateAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        string state,
        string message,
        string siigoId,
        string siigoName,
        string siigoPaymentId,
        string siigoPaymentName,
        string responseJson,
        string expectedConcurrencyToken,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar la cuenta de cobro.");

        var isExpense = IsConciliacionCuentaCobroExpenseSource(request.RecordSource);
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            isExpense ? _supplierExpensesTableName : CuentaCobroLogicalName,
            isExpense ? _supplierExpensesTableSetName : CuentaCobroFallbackEntitySetName,
            isExpense ? _supplierExpensesIdField : CuentaCobroFallbackIdField,
            isExpense ? "" : CuentaCobroFallbackPrimaryNameField,
            ct);
        var rawAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(
            metadata.LogicalName,
            ct);
        EnsureConciliacionCuentaCobroDocumentWorkflowSchema(rawAttributes, isExpense);
        var attributes = rawAttributes;
        attributes = isExpense
            ? BuildConciliacionCuentaCobroExpenseAttributeSet(metadata, attributes)
            : BuildCuentaCobroAutomationAttributeSet(metadata, attributes);
        var normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var current = isExpense
            ? await GetConciliacionCuentaCobroExpenseRowByIdAsync(normalizedRecordId, ct)
            : await GetConciliacionCuentaCobroAutomationRowByIdAsync(normalizedRecordId, ct);
        if (current is null)
            throw new InvalidOperationException("No encontramos la cuenta de cobro seleccionada.");
        var expectedEtag = (expectedConcurrencyToken ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(expectedEtag)
            && (string.IsNullOrWhiteSpace(current.ConcurrencyToken)
                || !string.Equals(expectedEtag, current.ConcurrencyToken, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "La cuenta de cobro cambio durante la validacion. "
                + "No se guardo un estado pre-Siigo obsoleto; recarga Conciliacion.");
        }
        if (!string.IsNullOrWhiteSpace(request.CashFlowRecordId)
            || !string.IsNullOrWhiteSpace(request.CashFlowExternalKey))
        {
            var cashFlow = await GetConciliacionCashFlowMovementForCuentaCobroAsync(
                request.CashFlowRecordId,
                request.CashFlowExternalKey,
                ct)
                ?? throw new InvalidOperationException("No encontramos la salida bancaria asociada.");
            EnsureConciliacionCuentaCobroCashFlowLink(current, cashFlow);
        }
        EnsureConciliacionCuentaCobroSupportDocumentTransition(
            current,
            state,
            siigoId,
            siigoName);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var isVerificationHold = state.Equals(
            CuentaCobroSupportDocumentVerificationState,
            StringComparison.OrdinalIgnoreCase);
        var detailParts = new List<string>();
        if (isVerificationHold)
        {
            detailParts.Add(CuentaCobroSupportDocumentAmbiguousMarker);
            if (HasConciliacionCuentaCobroSupportDocumentWriteHold(current))
                detailParts.Add(current.ReviewReason);
        }
        detailParts.Add(message);
        detailParts.Add(responseJson);
        var detail = TruncateAccountCatalogText(
            string.Join(" ", detailParts.Where(static value => !string.IsNullOrWhiteSpace(value))),
            1000);
        var isPaymentCheckpoint = !string.IsNullOrWhiteSpace(siigoPaymentId)
            || !string.IsNullOrWhiteSpace(siigoPaymentName)
            || message.Contains("pago", StringComparison.OrdinalIgnoreCase);

        SetAccountCatalogValue(payload, attributes, ExpenseAutomationStateField, null, state, force: true);
        SetAccountCatalogValue(payload, attributes, ExpenseReviewReasonField, null, detail, force: true);
        SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoResponseField, null, TruncateAccountCatalogText(responseJson, 4000), force: true);
        if (isPaymentCheckpoint)
            SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoPaymentResponseField, null, TruncateAccountCatalogText(responseJson, 4000), force: true);
        if (!string.IsNullOrWhiteSpace(siigoId) || !string.IsNullOrWhiteSpace(siigoName))
        {
            SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoDocumentIdField, null, siigoId, force: true);
            SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoDocumentNameField, null, siigoName, force: true);
        }
        if (!string.IsNullOrWhiteSpace(siigoPaymentId) || !string.IsNullOrWhiteSpace(siigoPaymentName))
        {
            SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoPaymentIdField, null, siigoPaymentId, force: true);
            SetAccountCatalogValue(payload, attributes, CuentaCobroSiigoPaymentNameField, null, siigoPaymentName, force: true);
        }

        if (payload.Count > 0)
        {
            var patchEtag = string.IsNullOrWhiteSpace(expectedEtag)
                ? current.ConcurrencyToken
                : expectedEtag;
            if (string.IsNullOrWhiteSpace(patchEtag)
                || !await TryPatchExpenseAccountingRowAsync(
                    metadata,
                    normalizedRecordId,
                    patchEtag,
                    payload,
                    ct))
            {
                throw new InvalidOperationException(
                    "La cuenta de cobro cambio mientras se guardaba el resultado Siigo. "
                    + "Se conservo el estado mas reciente y no se aplico last-write-wins.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.CashFlowRecordId) || !string.IsNullOrWhiteSpace(request.CashFlowExternalKey))
        {
            await MarkConciliacionCuentaCobroCashFlowSiigoResultAsync(
                request,
                state,
                detail,
                FirstNonEmpty(siigoPaymentId, siigoId),
                ct);
        }

        return await GetConciliacionCuentaCobroDocumentAsync(request, ct);
    }

    private async Task MarkConciliacionCuentaCobroCashFlowSiigoResultAsync(
        ConciliacionCuentaCobroDocumentRequest request,
        string state,
        string detail,
        string siigoId,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var movementId = Guid.TryParse(request.CashFlowRecordId, out var parsedId)
            ? parsedId.ToString("D")
            : await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, request.CashFlowExternalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            return;

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, state, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, state, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, siigoId, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, detail, force: true);
        if (payload.Count == 0)
            return;

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
            "PATCH",
            payload,
            ct);
    }

    private async Task<ConciliacionCashFlowRowDto?> GetConciliacionCashFlowMovementForCuentaCobroAsync(
        string recordId,
        string externalKey,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var select = BuildConciliacionSelectClause(metadata, attributes, new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
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

        var movementId = Guid.TryParse(recordId, out var parsedId)
            ? parsedId.ToString("D")
            : await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, externalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            return null;

        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})?$select={select}",
            ct);
        using var document = JsonDocument.Parse(json);
        return ParseConciliacionCashFlowMovementRow(document.RootElement, metadata);
    }

    private static ConciliacionCuentaCobroRowDto BuildConciliacionCuentaCobroRow(
        ConciliacionCashFlowRowDto? cashFlow,
        CuentaCobroAutomationRow? cuenta,
        int score)
    {
        var difference = RoundCurrency((cashFlow?.ExitValue ?? cuenta?.ValorPago ?? 0m) - (cuenta?.ValorPago ?? 0m));
        var row = new ConciliacionCuentaCobroRowDto
        {
            RecordId = cuenta?.RecordId ?? "",
            RecordSource = cuenta?.RecordSource ?? "",
            ConcurrencyToken = cuenta?.ConcurrencyToken ?? "",
            CashFlowRecordId = cashFlow?.RecordId ?? cuenta?.CashFlowRecordId ?? "",
            CashFlowExternalKey = cashFlow?.ExternalKey ?? cuenta?.CashFlowExternalKey ?? "",
            SourceRowNumber = cashFlow?.SourceRowNumber ?? ParseConciliacionCashFlowSourceRowNumber(cashFlow?.ExternalKey),
            SourceFlow = cashFlow?.SourceFlow ?? "",
            BankAccountCode = cashFlow?.BankAccountCode ?? "",
            BankAccountName = cashFlow?.BankAccountName ?? "",
            MovementDateValue = cashFlow?.MovementDateValue ?? "",
            MovementDateDisplay = cashFlow?.MovementDateDisplay ?? "Sin flujo",
            CashFlowDescription = cashFlow?.Description ?? "",
            CashFlowRecipient = cashFlow?.Recipient ?? "",
            CashFlowDocumentType = cashFlow?.DocumentType ?? "",
            CashFlowObservations = cashFlow?.Observations ?? "",
            CashFlowExitValue = cashFlow?.ExitValue ?? 0m,
            Receptor = cuenta?.Receptor ?? FirstNonEmpty(cashFlow?.ThirdPartyName, cashFlow?.Recipient, cashFlow?.Description),
            NitOCedula = cuenta?.NitOCedula ?? cashFlow?.ThirdPartyIdentification ?? "",
            Observaciones = cuenta?.Observaciones ?? cashFlow?.Observations ?? "",
            FechaEmisionValue = cuenta?.FechaEmisionValue ?? cashFlow?.MovementDateValue ?? "",
            FechaEmisionDisplay = cuenta?.FechaEmisionDisplay ?? cashFlow?.MovementDateDisplay ?? "",
            FechaPagoValue = cuenta?.FechaPagoValue ?? cashFlow?.MovementDateValue ?? "",
            FechaPagoDisplay = cuenta?.FechaPagoDisplay ?? cashFlow?.MovementDateDisplay ?? "",
            ValorTotal = cuenta?.ValorTotal ?? cashFlow?.ExitValue ?? 0m,
            ValorIva = cuenta?.ValorIva ?? 0m,
            ValorPago = cuenta?.ValorPago ?? cashFlow?.ExitValue ?? 0m,
            CloudValue = cuenta?.CloudValue ?? 0m,
            CopiersValue = cuenta?.CopiersValue ?? 0m,
            CategoryValue = cuenta?.CategoryValue ?? "",
            CategoryLabel = cuenta?.CategoryLabel ?? "",
            ReteFuentePorcentaje = cuenta?.ReteFuentePorcentaje ?? 0m,
            ReteFuenteValor = cuenta?.ReteFuenteValor ?? 0m,
            Retentions = cuenta?.Retentions.Select(static retention =>
            {
                _ = int.TryParse(retention.TaxId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taxId);
                return new ConciliacionCuentaCobroRetentionDto
                {
                    Kind = retention.Kind,
                    Label = retention.Label,
                    TaxId = taxId,
                    AccountCode = retention.AccountCode,
                    BaseValue = retention.BaseValue,
                    Rate = retention.Rate,
                    Value = retention.Value
                };
            }).ToArray() ?? Array.Empty<ConciliacionCuentaCobroRetentionDto>(),
            DifferenceValue = difference,
            MatchScore = score,
            MatchLabel = cuenta is null ? "Sin cuenta de cobro app" : cashFlow is null ? "Sin flujo de caja" : $"Cruce {score:N0}%",
            MatchTone = cuenta is null || cashFlow is null ? "warning" : score >= 90 ? "success" : "info",
            TotalesCuadran = cuenta?.TotalesCuadran ?? cashFlow is not null,
            AccountCode = cuenta?.AccountCode ?? "",
            AccountName = ResolveAccountCatalogName(cuenta?.AccountCode ?? "", cuenta?.AccountName),
            AutomationState = cuenta?.AutomationState ?? "",
            ReviewReason = cuenta?.ReviewReason ?? "",
            SiigoSupplierId = cuenta?.SiigoSupplierId ?? "",
            SiigoSupplierName = cuenta?.SiigoSupplierName ?? "",
            SiigoDocumentId = cuenta?.SiigoDocumentId ?? "",
            SiigoDocumentName = cuenta?.SiigoDocumentName ?? "",
            SiigoPaymentId = cuenta?.SiigoPaymentId ?? "",
            SiigoPaymentName = cuenta?.SiigoPaymentName ?? "",
            ModifiedOnDisplay = cuenta?.ModifiedOnDisplay ?? cashFlow?.ModifiedOnValue ?? ""
        };

        CompleteConciliacionCuentaCobroRow(row);
        return row;
    }

    private static void CompleteConciliacionCuentaCobroRow(ConciliacionCuentaCobroRowDto row)
    {
        var hasAppRecord = !string.IsNullOrWhiteSpace(row.RecordId);
        var hasCashFlow = !string.IsNullOrWhiteSpace(row.CashFlowRecordId) || !string.IsNullOrWhiteSpace(row.CashFlowExternalKey);
        var hasAccount = !string.IsNullOrWhiteSpace(row.AccountCode);
        var hasSupportDocument = !string.IsNullOrWhiteSpace(row.SiigoDocumentId)
            || !string.IsNullOrWhiteSpace(row.SiigoDocumentName);
        var hasPayment = !string.IsNullOrWhiteSpace(row.SiigoPaymentId)
            || !string.IsNullOrWhiteSpace(row.SiigoPaymentName);
        var isError = string.Equals(row.AutomationState, "ErrorSiigo", StringComparison.OrdinalIgnoreCase);
        var isProcessingDocument = string.Equals(
            row.AutomationState,
            CuentaCobroSupportDocumentProcessingState,
            StringComparison.OrdinalIgnoreCase);
        var needsDocumentVerification = string.Equals(
                row.AutomationState,
                CuentaCobroSupportDocumentVerificationState,
                StringComparison.OrdinalIgnoreCase)
            || row.ReviewReason.Contains(
                CuentaCobroSupportDocumentAmbiguousMarker,
                StringComparison.OrdinalIgnoreCase);
        var sentOrAttempted = hasSupportDocument
            || hasPayment
            || isError
            || isProcessingDocument
            || needsDocumentVerification
            || string.Equals(row.AutomationState, "EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.AutomationState, "Conciliado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.AutomationState, "DocumentoSoporteSiigo", StringComparison.OrdinalIgnoreCase);

        row.SiigoStatusLabel = needsDocumentVerification
            ? "Verificar en Siigo"
            : isProcessingDocument
                ? "Documento en proceso"
            : isError && !hasSupportDocument
                ? "Error documento"
            : hasSupportDocument
                ? "Documento soporte OK"
                : "Documento pendiente";
        row.SiigoStatusTone = needsDocumentVerification || isError && !hasSupportDocument
            ? "danger"
            : hasSupportDocument ? "success" : "warning";
        row.SiigoPaymentStatusLabel = isError && hasSupportDocument && !hasPayment
            ? "Pago con error"
            : hasPayment
                ? "Pago OK"
                : "Pago pendiente";
        row.SiigoPaymentStatusTone = isError && hasSupportDocument && !hasPayment
            ? "danger"
            : hasPayment ? "success" : "warning";

        if (sentOrAttempted)
        {
            row.Stage = "enviadas";
            row.StageLabel = needsDocumentVerification
                ? "Verificacion Siigo"
                : isProcessingDocument
                    ? "Procesando en Siigo"
                : isError
                    ? "Error Siigo"
                : string.Equals(row.AutomationState, "Conciliado", StringComparison.OrdinalIgnoreCase)
                    ? "Conciliado manual"
                : hasSupportDocument && !hasPayment
                    ? "Documento sin pago"
                    : "Enviado a Siigo";
            row.StageTone = needsDocumentVerification || isError
                ? "danger"
                : hasPayment ? "success" : "warning";
            return;
        }

        if (!hasAppRecord)
        {
            row.Stage = "detectadas";
            row.StageLabel = "Falta cuenta app";
            row.StageTone = "warning";
            return;
        }

        if (string.Equals(row.AutomationState, "BloqueadoSiigo", StringComparison.OrdinalIgnoreCase)
            || !hasCashFlow
            || !row.TotalesCuadran
            || Math.Abs(row.DifferenceValue) > 1m
            || !hasAccount)
        {
            row.Stage = "pendientes";
            row.StageLabel = !hasCashFlow ? "Falta flujo caja" : !hasAccount ? "Falta cuenta" : "Pendiente";
            row.StageTone = "warning";
            return;
        }

        row.Stage = "prevalidacion";
        row.StageLabel = string.Equals(row.AutomationState, "ListoSiigo", StringComparison.OrdinalIgnoreCase)
            ? "Listo Siigo"
            : "Listo para enviar";
        row.StageTone = "info";
    }

    private static int ScoreConciliacionCuentaCobroMatch(
        ConciliacionCashFlowRowDto cashFlow,
        CuentaCobroAutomationRow cuenta)
    {
        var score = 0;
        var difference = Math.Abs(cashFlow.ExitValue - cuenta.ValorPago);
        if (difference <= 1m)
            score += 70;
        else if (difference <= 5000m)
            score += 45;
        else if (difference <= 50000m)
            score += 20;

        var haystack = NormalizeConciliacionLookupText($"{cashFlow.Description} {cashFlow.Recipient} {cashFlow.Observations}");
        var nit = NormalizeConciliacionDigits(cuenta.NitOCedula);
        if (!string.IsNullOrWhiteSpace(nit) && NormalizeConciliacionDigits(haystack).Contains(nit, StringComparison.OrdinalIgnoreCase))
            score += 20;

        var nameTokens = NormalizeConciliacionLookupText(cuenta.Receptor)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (nameTokens.Length > 0)
        {
            var matched = nameTokens.Count(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (matched > 0)
                score += Math.Min(20, matched * 8);
        }

        if (DateOnly.TryParseExact(cashFlow.MovementDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var movementDate)
            && DateOnly.TryParseExact(cuenta.FechaPagoValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var paymentDate))
        {
            var days = Math.Abs(movementDate.DayNumber - paymentDate.DayNumber);
            if (days <= 2)
                score += 10;
            else if (days <= 10)
                score += 5;
        }

        return Math.Min(100, score);
    }

    private static string BuildCuentaCobroAutomationSelect(RhEntityMetadata metadata, ISet<string> attributes) =>
        BuildConciliacionSelectClause(metadata, attributes, BuildCuentaCobroAutomationFields(metadata));

    private static IEnumerable<string> BuildCuentaCobroAutomationFields(RhEntityMetadata metadata) =>
        new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            CuentaCobroReceptorField,
            CuentaCobroNitField,
            CuentaCobroObservacionesField,
            CuentaCobroValorTotalField,
            CuentaCobroReteFuentePorcentajeField,
            CuentaCobroValorPagoField,
            CuentaCobroReteFuenteValorField,
            CuentaCobroRetentionsJsonField,
            CuentaCobroFechaEmisionField,
            CuentaCobroFechaPagoField,
            CuentaCobroModifiedOnField,
            ExpenseAccountCodeField,
            ExpenseAccountNameField,
            ExpenseAutomationStateField,
            ExpenseReviewReasonField,
            CuentaCobroSiigoDocumentIdField,
            CuentaCobroSiigoDocumentNameField,
            CuentaCobroSiigoPaymentIdField,
            CuentaCobroSiigoPaymentNameField,
            CuentaCobroSiigoPaymentResponseField,
            CuentaCobroSiigoResponseField
        };

    private static HashSet<string> BuildCuentaCobroAutomationAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        var values = attributes.Count > 0
            ? new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(BuildCuentaCobroAutomationFields(metadata), StringComparer.OrdinalIgnoreCase);
        values.Add(metadata.PrimaryIdField);
        if (!string.IsNullOrWhiteSpace(metadata.PrimaryNameField))
            values.Add(metadata.PrimaryNameField);

        return values;
    }

    private static string BuildCuentaCobroAutomationPeriodFilter(
        ISet<string> attributes,
        DateOnly startInclusive,
        DateOnly endExclusive)
    {
        var filters = new[]
        {
            attributes.Contains(CuentaCobroFechaPagoField) ? BuildBillingDateFilter(CuentaCobroFechaPagoField, "date-only", startInclusive, endExclusive) : "",
            attributes.Contains(CuentaCobroFechaEmisionField) ? BuildBillingDateFilter(CuentaCobroFechaEmisionField, "date-only", startInclusive, endExclusive) : ""
        }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => $"({value})")
            .ToArray();

        return filters.Length == 0 ? "" : string.Join(" or ", filters);
    }

    private static CuentaCobroAutomationRow? ParseCuentaCobroAutomationRow(
        JsonElement item,
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var valorTotal = RoundCurrency(ReadDecimal(item, CuentaCobroValorTotalField) ?? 0m);
        var valorPago = RoundCurrency(ReadDecimal(item, CuentaCobroValorPagoField) ?? 0m);
        var reteFuentePorcentaje = RoundCurrency(ReadDecimal(item, CuentaCobroReteFuentePorcentajeField) ?? 0m);
        var reteFuenteValor = RoundCurrency(ReadDecimal(item, CuentaCobroReteFuenteValorField)
            ?? CalculateCuentaCobroReteFuenteValue(valorTotal, reteFuentePorcentaje));
        var retentions = ParseCuentaCobroAutomationRetentions(
            item,
            attributes,
            valorTotal,
            reteFuentePorcentaje,
            reteFuenteValor);
        var totalRetentionsValue = RoundCurrency(retentions.Sum(static retention => retention.Value));
        var fechaEmision = ReadDateOnly(item, CuentaCobroFechaEmisionField);
        var fechaPago = ReadDateOnly(item, CuentaCobroFechaPagoField);
        var modifiedOn = ParseConciliacionDateTimeOffset(ReadString(item, CuentaCobroModifiedOnField));

        return new CuentaCobroAutomationRow
        {
            RecordId = recordId,
            RecordSource = CuentaCobroLegacyRecordSource,
            ConcurrencyToken = ReadString(item, "@odata.etag").Trim(),
            Receptor = ReadString(item, CuentaCobroReceptorField).Trim(),
            NitOCedula = ReadString(item, CuentaCobroNitField).Trim(),
            Observaciones = RepairSpanishMojibakeText(ReadString(item, CuentaCobroObservacionesField)).Trim(),
            FechaEmisionValue = fechaEmision?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            FechaEmisionDisplay = fechaEmision?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            FechaPagoValue = fechaPago?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            FechaPagoDisplay = fechaPago?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ValorTotal = valorTotal,
            ValorPago = valorPago,
            ReteFuentePorcentaje = reteFuentePorcentaje,
            ReteFuenteValor = reteFuenteValor,
            Retentions = retentions,
            TotalesCuadran = Math.Abs(valorTotal - (valorPago + totalRetentionsValue)) <= 0.01m,
            AccountCode = ReadString(item, ExpenseAccountCodeField).Trim(),
            AccountName = ResolveAccountCatalogName(ReadString(item, ExpenseAccountCodeField), ReadString(item, ExpenseAccountNameField)),
            AutomationState = ReadString(item, ExpenseAutomationStateField).Trim(),
            ReviewReason = RepairSpanishMojibakeText(ReadString(item, ExpenseReviewReasonField)).Trim(),
            SiigoDocumentId = ReadString(item, CuentaCobroSiigoDocumentIdField).Trim(),
            SiigoDocumentName = ReadString(item, CuentaCobroSiigoDocumentNameField).Trim(),
            SiigoPaymentId = ReadString(item, CuentaCobroSiigoPaymentIdField).Trim(),
            SiigoPaymentName = ReadString(item, CuentaCobroSiigoPaymentNameField).Trim(),
            ModifiedOnDisplay = FormatConciliacionDateTimeDisplay(modifiedOn),
            CashFlowAmountHint = valorPago
        };
    }

    private static IReadOnlyList<CuentaCobroRetentionDto> ParseCuentaCobroAutomationRetentions(
        JsonElement item,
        ISet<string> attributes,
        decimal valorTotal,
        decimal legacyRate,
        decimal legacyValue)
    {
        if (attributes.Contains(CuentaCobroRetentionsJsonField))
        {
            var rawJson = ReadString(item, CuentaCobroRetentionsJsonField);
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<CuentaCobroRetentionDto>>(
                        rawJson,
                        CuentaCobroRetentionJsonOptions);
                    if (parsed is not null)
                        return NormalizeCuentaCobroRetentions(parsed, valorTotal, legacyRate, legacyValue);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    return NormalizeCuentaCobroRetentions(null, valorTotal, legacyRate, legacyValue);
                }
            }
        }

        return NormalizeCuentaCobroRetentions(null, valorTotal, legacyRate, legacyValue);
    }

    private sealed class CuentaCobroAutomationRow
    {
        public string RecordId { get; init; } = "";
        public string RecordSource { get; init; } = "";
        public string AutomationSource { get; init; } = "";
        public string CashFlowRecordId { get; init; } = "";
        public string CashFlowExternalKey { get; init; } = "";
        public string ConcurrencyToken { get; init; } = "";
        public string Receptor { get; init; } = "";
        public string NitOCedula { get; init; } = "";
        public string Observaciones { get; init; } = "";
        public string FechaEmisionValue { get; init; } = "";
        public string FechaEmisionDisplay { get; init; } = "";
        public string FechaPagoValue { get; init; } = "";
        public string FechaPagoDisplay { get; init; } = "";
        public decimal ValorTotal { get; init; }
        public decimal ValorIva { get; init; }
        public decimal ValorPago { get; init; }
        public decimal CloudValue { get; init; }
        public decimal CopiersValue { get; init; }
        public string CategoryValue { get; init; } = "";
        public string CategoryLabel { get; init; } = "";
        public decimal ReteFuentePorcentaje { get; init; }
        public decimal ReteFuenteValor { get; init; }
        public IReadOnlyList<CuentaCobroRetentionDto> Retentions { get; init; } = Array.Empty<CuentaCobroRetentionDto>();
        public bool TotalesCuadran { get; init; }
        public string AccountCode { get; init; } = "";
        public string AccountName { get; init; } = "";
        public string AutomationState { get; init; } = "";
        public string ReviewReason { get; init; } = "";
        public string SiigoSupplierId { get; init; } = "";
        public string SiigoSupplierName { get; init; } = "";
        public string SiigoDocumentId { get; init; } = "";
        public string SiigoDocumentName { get; init; } = "";
        public string SiigoPaymentId { get; init; } = "";
        public string SiigoPaymentName { get; init; } = "";
        public string ModifiedOnDisplay { get; init; } = "";
        public decimal CashFlowAmountHint { get; init; }
    }
}
