using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<ConciliacionCashFlowRowDto> GetConciliacionCashFlowMovementAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct = default)
    {
        var rows = await GetConciliacionCashFlowMovementsAsync(request, ct);
        return rows.FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos la fila del flujo de caja.");
    }

    public async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowMovementsAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.Equals(request.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
            return await GetConciliacionCashFlowTransferMovementsAsync(request, ct);

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var hasRequestedThirdParty = !string.IsNullOrWhiteSpace(request.ThirdPartyId)
            || !string.IsNullOrWhiteSpace(request.ThirdPartyIdentification)
            || !string.IsNullOrWhiteSpace(request.ThirdPartyName);
        if (hasRequestedThirdParty
            && (string.IsNullOrWhiteSpace(request.ThirdPartyId)
                || string.IsNullOrWhiteSpace(request.ThirdPartyIdentification)
                || string.IsNullOrWhiteSpace(request.ThirdPartyName)))
        {
            throw new InvalidOperationException("El tercero de Siigo debe incluir ID, identificacion y nombre.");
        }
        if (hasRequestedThirdParty
            && (!attributes.Contains(CashFlowThirdPartyKeyField)
                || !attributes.Contains(CashFlowThirdPartyIdentificationField)
                || !attributes.Contains(CashFlowThirdPartyNameField)
                || !attributes.Contains(CashFlowThirdPartyBranchOfficeField)))
        {
            throw new InvalidOperationException(
                "El ambiente de Dataverse aun no tiene todos los campos requeridos para guardar el tercero real del comprobante.");
        }

        var targets = BuildConciliacionAccountingVoucherTargets(
            request.RecordId,
            request.RecordIds,
            request.MovementExternalKey,
            request.MovementExternalKeys);
        if (targets.Count == 0)
            throw new InvalidOperationException("No encontramos la fila del flujo de caja.");

        var rows = new List<ConciliacionCashFlowRowDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var movementId = await ResolveConciliacionCashFlowMovementIdAsync(metadata, target.RecordId, target.ExternalKey, ct);
            if (!seen.Add(movementId))
                continue;

            var row = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct)
                ?? throw new InvalidOperationException("No encontramos la fila del flujo de caja.");
            rows.Add(row);
        }

        return rows;
    }

    public async Task<ConciliacionCashFlowActionResultDto> UpdateConciliacionCashFlowAccountingAccountAsync(
        ConciliacionCashFlowAccountingAccountRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.Equals(request.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
        {
            return new ConciliacionCashFlowActionResultDto
            {
                Message = "La cuenta contable del traslado se toma automaticamente de las cuentas bancarias origen y destino.",
                IsSuccess = true,
                IsReadyForSiigo = true
            };
        }

        var accountCode = (request.AccountCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountCode))
            throw new InvalidOperationException("Selecciona la cuenta contable para el comprobante.");

        var catalog = await GetConciliacionAccountCatalogAsync(ct);
        if (!catalog.TryGetValue(accountCode, out var account) || !account.Active)
            throw new InvalidOperationException("La cuenta contable seleccionada no existe o no esta activa en el catalogo Siigo.");

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var hasRequestedThirdParty = !string.IsNullOrWhiteSpace(request.ThirdPartyId)
            || !string.IsNullOrWhiteSpace(request.ThirdPartyIdentification)
            || !string.IsNullOrWhiteSpace(request.ThirdPartyName);
        if (hasRequestedThirdParty
            && (string.IsNullOrWhiteSpace(request.ThirdPartyId)
                || string.IsNullOrWhiteSpace(request.ThirdPartyIdentification)
                || string.IsNullOrWhiteSpace(request.ThirdPartyName)))
        {
            throw new InvalidOperationException("El tercero de Siigo debe incluir ID, identificacion y nombre.");
        }
        if (hasRequestedThirdParty
            && (!attributes.Contains(CashFlowThirdPartyKeyField)
                || !attributes.Contains(CashFlowThirdPartyIdentificationField)
                || !attributes.Contains(CashFlowThirdPartyNameField)
                || !attributes.Contains(CashFlowThirdPartyBranchOfficeField)))
        {
            throw new InvalidOperationException(
                "El ambiente de Dataverse aun no tiene todos los campos requeridos para guardar el tercero real del comprobante.");
        }
        var targets = BuildConciliacionAccountingVoucherTargets(
            request.RecordId,
            request.RecordIds,
            request.MovementExternalKey,
            request.MovementExternalKeys);
        if (targets.Count == 0)
            throw new InvalidOperationException("No encontramos la fila del flujo de caja.");

        var updatedRows = new List<ConciliacionCashFlowRowDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var movementId = await ResolveConciliacionCashFlowMovementIdAsync(metadata, target.RecordId, target.ExternalKey, ct);
            if (!seen.Add(movementId))
                continue;

            var current = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct)
                ?? throw new InvalidOperationException("No encontramos la fila del flujo de caja.");

            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            SetAccountCatalogValue(payload, attributes, CashFlowAccountingAccountCodeField, null, account.Code, force: true);
            SetAccountCatalogValue(payload, attributes, CashFlowAccountingAccountNameField, null, TruncateAccountCatalogText(account.Name, 250), force: true);
            SetAccountCatalogValue(payload, attributes, CashFlowThirdPartyKeyField, null, request.ThirdPartyId?.Trim() ?? "", force: false);
            SetAccountCatalogValue(payload, attributes, CashFlowThirdPartyIdentificationField, null, request.ThirdPartyIdentification?.Trim() ?? "", force: false);
            SetAccountCatalogValue(payload, attributes, CashFlowThirdPartyNameField, null, TruncateAccountCatalogText(request.ThirdPartyName, 250), force: false);
            SetAccountCatalogValue(payload, attributes, CashFlowThirdPartyBranchOfficeField, (int?)null, Math.Max(0, request.ThirdPartyBranchOffice), force: false);
            SetAccountCatalogValue(
                payload,
                attributes,
                CashFlowReviewReasonField,
                null,
                TruncateAccountCatalogText($"Cuenta contable asignada desde Conciliacion: {account.Code} - {account.Name}.", 1000),
                force: true);

            if (!IsConciliacionSiigoRegistered(current.SiigoDocumentId, current.SiigoStatus)
                && !string.Equals(current.SiigoStatus, "Conciliado", StringComparison.OrdinalIgnoreCase))
            {
                SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "ListoSiigo", force: true);
                SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, "ListoSiigo", force: true);
            }

            if (payload.Count == 0)
                throw new InvalidOperationException("No encontramos campos disponibles para guardar la cuenta contable.");

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
                "PATCH",
                payload,
                ct);

            var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
            if (updated is null)
                throw new InvalidOperationException("El movimiento se actualizo, pero Dataverse no permitio releerlo para verificar el resultado.");
            if (!string.Equals(updated.AccountCode, account.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dataverse no confirmo la cuenta contable guardada.");
            if (hasRequestedThirdParty
                && (!string.Equals(updated.ThirdPartyId, (request.ThirdPartyId ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        ExtractDigits(updated.ThirdPartyIdentification),
                        ExtractDigits(request.ThirdPartyIdentification),
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(updated.ThirdPartyName, TruncateAccountCatalogText(request.ThirdPartyName, 250), StringComparison.OrdinalIgnoreCase)
                    || updated.ThirdPartyBranchOffice != Math.Max(0, request.ThirdPartyBranchOffice)))
            {
                throw new InvalidOperationException("Dataverse no confirmo exactamente el tercero real guardado para el comprobante.");
            }

            updatedRows.Add(updated);
        }

        return new ConciliacionCashFlowActionResultDto
        {
            Message = updatedRows.Count > 1
                ? $"Cuenta contable guardada en {updatedRows.Count:N0} movimientos: {account.Code} - {account.Name}."
                : $"Cuenta contable guardada: {account.Code} - {account.Name}.",
            IsSuccess = true,
            IsReadyForSiigo = true,
            Row = updatedRows.FirstOrDefault()
        };
    }

    public async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowAccountingVoucherSiigoResultAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        bool success,
        string message,
        string siigoId = "",
        string siigoName = "",
        string responseJson = "",
        string payloadJson = "",
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.Equals(request.SourceKind, "Traslado", StringComparison.OrdinalIgnoreCase))
        {
            return await MarkConciliacionCashFlowTransferAccountingVoucherSiigoResultAsync(
                request,
                success,
                message,
                siigoId,
                siigoName,
                responseJson,
                payloadJson,
                ct);
        }

        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowMovementAttributeSet(metadata, attributes);
        var targets = BuildConciliacionAccountingVoucherTargets(
            request.RecordId,
            request.RecordIds,
            request.MovementExternalKey,
            request.MovementExternalKeys);
        if (targets.Count == 0)
            throw new InvalidOperationException("No encontramos la fila del flujo de caja.");

        var status = ResolveConciliacionAccountingVoucherCompletionStatus(success);
        var detail = TruncateAccountCatalogText(
            string.Join(" ", new[]
            {
                FirstNonEmpty(message, success ? "Comprobante contable enviado a Siigo." : "Error al enviar comprobante contable a Siigo."),
                string.IsNullOrWhiteSpace(siigoName) ? "" : $"Documento Siigo: {siigoName}.",
                string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id Siigo: {siigoId}.",
                string.IsNullOrWhiteSpace(responseJson) ? "" : $"Detalle: {responseJson}"
            }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            1000);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, status, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, detail, force: true);
        if (success)
        {
            SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, siigoId, force: true);
            SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentNameField, null, siigoName, force: true);
        }

        if (payload.Count == 0)
            throw new InvalidOperationException("No encontramos campos disponibles para guardar el resultado Siigo.");

        var updatedRows = new List<ConciliacionCashFlowRowDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var movementId = await ResolveConciliacionCashFlowMovementIdAsync(metadata, target.RecordId, target.ExternalKey, ct);
            if (!seen.Add(movementId))
                continue;

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({movementId})",
                "PATCH",
                payload,
                ct);

            var updated = await GetConciliacionCashFlowMovementByIdAsync(metadata, attributes, movementId, ct);
            if (updated is not null)
                updatedRows.Add(updated);
        }

        return new ConciliacionCashFlowActionResultDto
        {
            Message = detail,
            IsSuccess = success,
            SiigoId = siigoId,
            SiigoName = siigoName,
            PayloadJson = payloadJson,
            ResponseJson = responseJson,
            Row = updatedRows.FirstOrDefault()
        };
    }

    private async Task<IReadOnlyList<ConciliacionCashFlowRowDto>> GetConciliacionCashFlowTransferMovementsAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);

        var targets = BuildConciliacionAccountingVoucherTargets(
            request.RecordId,
            request.RecordIds,
            request.MovementExternalKey,
            request.MovementExternalKeys);
        if (targets.Count == 0)
            throw new InvalidOperationException("No encontramos el traslado del flujo de caja.");

        var rows = new List<ConciliacionCashFlowRowDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var transferId = await ResolveConciliacionCashFlowTransferIdAsync(metadata, target.RecordId, target.ExternalKey, ct);
            if (!seen.Add(transferId))
                continue;

            var row = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct)
                ?? throw new InvalidOperationException("No encontramos el traslado del flujo de caja.");
            rows.Add(row);
        }

        return rows;
    }

    private async Task<ConciliacionCashFlowActionResultDto> MarkConciliacionCashFlowTransferAccountingVoucherSiigoResultAsync(
        ConciliacionCashFlowAccountingVoucherRequest request,
        bool success,
        string message,
        string siigoId,
        string siigoName,
        string responseJson,
        string payloadJson,
        CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        attributes = BuildCashFlowTransferAttributeSet(metadata, attributes);
        var targets = BuildConciliacionAccountingVoucherTargets(
            request.RecordId,
            request.RecordIds,
            request.MovementExternalKey,
            request.MovementExternalKeys);
        if (targets.Count == 0)
            throw new InvalidOperationException("No encontramos el traslado del flujo de caja.");

        var status = ResolveConciliacionAccountingVoucherCompletionStatus(success);
        var detail = TruncateAccountCatalogText(
            string.Join(" ", new[]
            {
                FirstNonEmpty(message, success ? "Comprobante contable de traslado enviado a Siigo." : "Error al enviar comprobante contable de traslado a Siigo."),
                string.IsNullOrWhiteSpace(siigoName) ? "" : $"Documento Siigo: {siigoName}.",
                string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id Siigo: {siigoId}."
            }.Where(static value => !string.IsNullOrWhiteSpace(value))),
            100);

        var updatedRows = new List<ConciliacionCashFlowRowDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var transferId = await ResolveConciliacionCashFlowTransferIdAsync(metadata, target.RecordId, target.ExternalKey, ct);
            if (!seen.Add(transferId))
                continue;

            var current = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct)
                ?? throw new InvalidOperationException("No encontramos el traslado del flujo de caja.");
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, status, force: true);
            if (attributes.Contains(CashFlowTransferObservationsField))
            {
                var auditDetail = string.Join(" ", new[]
                {
                    success ? "[SIIGO TRASLADO OK]" : "[SIIGO TRASLADO ERROR]",
                    string.IsNullOrWhiteSpace(siigoName) ? "" : $"Documento {siigoName}.",
                    string.IsNullOrWhiteSpace(siigoId) ? "" : $"Id {siigoId}.",
                    detail
                }.Where(static value => !string.IsNullOrWhiteSpace(value)));
                SetAccountCatalogValue(
                    payload,
                    attributes,
                    CashFlowTransferObservationsField,
                    null,
                    AppendConciliacionPendingReason(current.Observations, auditDetail),
                    force: true);
            }
            if (payload.Count == 0)
                throw new InvalidOperationException("No encontramos campos disponibles para guardar el resultado Siigo del traslado.");

            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({transferId})",
                "PATCH",
                payload,
                ct);

            var updated = await GetConciliacionCashFlowTransferByIdAsync(metadata, attributes, transferId, ct);
            if (updated is not null)
                updatedRows.Add(updated);
        }

        return new ConciliacionCashFlowActionResultDto
        {
            Message = detail,
            IsSuccess = success,
            SiigoId = siigoId,
            SiigoName = siigoName,
            PayloadJson = payloadJson,
            ResponseJson = responseJson,
            Row = updatedRows.FirstOrDefault()
        };
    }

    internal static string ResolveConciliacionAccountingVoucherCompletionStatus(bool success) =>
        success ? "Conciliado" : "ErrorSiigo";

    private static IReadOnlyList<(string RecordId, string ExternalKey)> BuildConciliacionAccountingVoucherTargets(
        string recordId,
        IReadOnlyList<string>? recordIds,
        string externalKey,
        IReadOnlyList<string>? externalKeys)
    {
        var targets = new List<(string RecordId, string ExternalKey)>();
        void Add(string candidateRecordId, string candidateExternalKey)
        {
            candidateRecordId = (candidateRecordId ?? "").Trim();
            candidateExternalKey = (candidateExternalKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(candidateRecordId) && string.IsNullOrWhiteSpace(candidateExternalKey))
                return;
            targets.Add((candidateRecordId, candidateExternalKey));
        }

        Add(recordId, externalKey);
        if (recordIds is not null)
        {
            foreach (var item in recordIds)
                Add(item, "");
        }

        if (targets.Count == 0 && externalKeys is not null)
        {
            foreach (var item in externalKeys)
                Add("", item);
        }

        return targets
            .GroupBy(static target => string.IsNullOrWhiteSpace(target.RecordId) ? $"ext:{target.ExternalKey}" : $"id:{target.RecordId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private async Task<string> ResolveConciliacionCashFlowMovementIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        string externalKey,
        CancellationToken ct)
    {
        var movementId = Guid.TryParse(recordId, out var parsedRecordId)
            ? parsedRecordId.ToString("D")
            : "";
        if (string.IsNullOrWhiteSpace(movementId))
            movementId = await FindConciliacionCashFlowMovementIdByExternalKeyAsync(metadata, externalKey, ct);
        if (string.IsNullOrWhiteSpace(movementId))
            throw new InvalidOperationException("No encontramos la fila del flujo de caja.");

        return movementId;
    }

    private async Task<string> ResolveConciliacionCashFlowTransferIdAsync(
        RhEntityMetadata metadata,
        string recordId,
        string externalKey,
        CancellationToken ct)
    {
        var transferId = Guid.TryParse(recordId, out var parsedRecordId)
            ? parsedRecordId.ToString("D")
            : "";
        if (string.IsNullOrWhiteSpace(transferId))
            transferId = await FindConciliacionCashFlowRecordIdByExternalKeyAsync(metadata, CashFlowTransferExternalKeyField, externalKey, ct);
        if (string.IsNullOrWhiteSpace(transferId))
            throw new InvalidOperationException("No encontramos el traslado del flujo de caja.");

        return transferId;
    }

    private async Task<ConciliacionCashFlowRowDto?> GetConciliacionCashFlowMovementByIdAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string movementId,
        CancellationToken ct)
    {
        var select = BuildConciliacionCashFlowMovementSelect(metadata, attributes);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({movementId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseConciliacionCashFlowMovementRow(doc.RootElement, metadata);
    }

    private async Task<ConciliacionCashFlowRowDto?> GetConciliacionCashFlowTransferByIdAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string transferId,
        CancellationToken ct)
    {
        var select = BuildConciliacionCashFlowTransferSelect(metadata, attributes);
        var json = await CallDataverseAppGetJsonAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({transferId})?$select={select}",
            ct,
            AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return ParseConciliacionCashFlowTransferRow(doc.RootElement, metadata);
    }

    private static string BuildConciliacionCashFlowMovementSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return BuildConciliacionSelectClause(metadata, attributes, new[]
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
            CashFlowSiigoDocumentNameField,
            CashFlowAccountingAccountCodeField,
            CashFlowAccountingAccountNameField,
            CashFlowThirdPartyKeyField,
            CashFlowThirdPartyIdentificationField,
            CashFlowThirdPartyNameField,
            CashFlowThirdPartyBranchOfficeField,
            CashFlowSiigoStatusField,
            CashFlowExternalKeyField,
            CashFlowSourceRowField,
            CashFlowReviewReasonField,
            ConciliacionModifiedOnField
        });
    }

    private static string BuildConciliacionCashFlowTransferSelect(
        RhEntityMetadata metadata,
        ISet<string> attributes)
    {
        return BuildConciliacionSelectClause(metadata, attributes, new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            CashFlowTransferDateField,
            CashFlowTransferValueField,
            CashFlowTransferSourceFlowField,
            CashFlowTransferFromField,
            CashFlowTransferToField,
            CashFlowTransferEntryField,
            CashFlowTransferExitField,
            CashFlowTransferDescriptionField,
            CashFlowTransferRecipientField,
            CashFlowTransferDestinationBankField,
            CashFlowTransferDocumentTypeField,
            CashFlowTransferObservationsField,
            CashFlowTransferStatusField,
            CashFlowTransferExternalKeyField,
            CashFlowTransferSourceRowField,
            ConciliacionModifiedOnField
        });
    }
}
