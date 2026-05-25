using System.Globalization;
using CotizadorInterno.Web.Models.Automation;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CashFlowMovementLogicalName = "cr07a_movimientobancario";
    private const string CashFlowMovementSetName = "cr07a_movimientobancarios";
    private const string CashFlowMovementIdField = "cr07a_movimientobancarioid";
    private const string CashFlowMovementPrimaryNameField = "cr07a_name";
    private const string CashFlowDateField = "cr07a_fecha";
    private const string CashFlowBankField = "cr07a_banco";
    private const string CashFlowDescriptionField = "cr07a_descripcion";
    private const string CashFlowEntryField = "cr07a_valorentrada";
    private const string CashFlowExitField = "cr07a_valorsalida";
    private const string CashFlowReferenceField = "cr07a_referencia";
    private const string CashFlowMovementTypeField = "cr07a_tipomovimiento";
    private const string CashFlowStatusField = "cr07a_estado";
    private const string CashFlowSiigoDocumentIdField = "cr07a_siigodocumentid";
    private const string CashFlowReviewReasonField = "cr07a_motivorevision";
    private const string CashFlowSourceFlowField = "cr07a_origenflujo";
    private const string CashFlowBankAccountCodeField = "cr07a_bancocuentacodigo";
    private const string CashFlowBankAccountNameField = "cr07a_bancocuentanombre";
    private const string CashFlowRecipientField = "cr07a_destinatario";
    private const string CashFlowDestinationBankField = "cr07a_bancodestino";
    private const string CashFlowDocumentTypeField = "cr07a_tipodocumento";
    private const string CashFlowObservationsField = "cr07a_observaciones";
    private const string CashFlowSiigoStatusField = "cr07a_siigoestado";
    private const string CashFlowExternalKeyField = "cr07a_claveexterna";
    private const string CashFlowSourceFileField = "cr07a_archivoorigen";
    private const string CashFlowSourceTableField = "cr07a_tablaorigen";
    private const string CashFlowSourceRowField = "cr07a_filaorigen";
    private const string CashFlowSourceHashField = "cr07a_hashorigen";

    private const string CashFlowTransferLogicalName = "cr07a_trasladointernoflujocaja";
    private const string CashFlowTransferSetName = "cr07a_trasladointernoflujocajas";
    private const string CashFlowTransferIdField = "cr07a_trasladointernoflujocajaid";
    private const string CashFlowTransferPrimaryNameField = "cr07a_name";
    private const string CashFlowTransferDateField = "cr07a_fecha";
    private const string CashFlowTransferSourceFlowField = "cr07a_origenflujo";
    private const string CashFlowTransferFromField = "cr07a_flujodesde";
    private const string CashFlowTransferToField = "cr07a_flujohacia";
    private const string CashFlowTransferEntryField = "cr07a_entrada";
    private const string CashFlowTransferExitField = "cr07a_salida";
    private const string CashFlowTransferValueField = "cr07a_valor";
    private const string CashFlowTransferDescriptionField = "cr07a_descripcion";
    private const string CashFlowTransferRecipientField = "cr07a_destinatario";
    private const string CashFlowTransferDestinationBankField = "cr07a_bancodestino";
    private const string CashFlowTransferDocumentTypeField = "cr07a_tipodocumento";
    private const string CashFlowTransferObservationsField = "cr07a_observaciones";
    private const string CashFlowTransferStatusField = "cr07a_estado";
    private const string CashFlowTransferExternalKeyField = "cr07a_claveexterna";
    private const string CashFlowTransferSourceFileField = "cr07a_archivoorigen";
    private const string CashFlowTransferSourceTableField = "cr07a_tablaorigen";
    private const string CashFlowTransferSourceRowField = "cr07a_filaorigen";
    private const string CashFlowTransferSourceHashField = "cr07a_hashorigen";
    private const int CashFlowUpsertMaxConcurrency = 8;

    public async Task<CashFlowDataverseUpsertResultDto> UpsertCashFlowRowsAsync(
        IReadOnlyList<CashFlowImportRowDto> rows,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        rows ??= Array.Empty<CashFlowImportRowDto>();

        var movementMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowMovementLogicalName,
            CashFlowMovementSetName,
            CashFlowMovementIdField,
            CashFlowMovementPrimaryNameField,
            ct);
        var movementAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(movementMetadata.LogicalName, ct);
        movementAttributes = BuildCashFlowMovementAttributeSet(movementMetadata, movementAttributes);

        var transferMetadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            CashFlowTransferLogicalName,
            CashFlowTransferSetName,
            CashFlowTransferIdField,
            CashFlowTransferPrimaryNameField,
            ct);
        var transferAttributes = await GetFinancialReconciliationAttributeNamesAppAsync(transferMetadata.LogicalName, ct);
        transferAttributes = BuildCashFlowTransferAttributeSet(transferMetadata, transferAttributes);

        var movementIndex = await GetCashFlowExistingIndexAsync(
            movementMetadata,
            movementAttributes,
            CashFlowExternalKeyField,
            ct);
        var transferIndex = await GetCashFlowExistingIndexAsync(
            transferMetadata,
            transferAttributes,
            CashFlowTransferExternalKeyField,
            ct);
        var lockedClientPaymentMovementKeys = await GetCashFlowLockedClientPaymentMovementKeysAsync(ct);

        using var throttler = new SemaphoreSlim(CashFlowUpsertMaxConcurrency);
        var tasks = rows.Select(async row =>
        {
            ct.ThrowIfCancellationRequested();
            await throttler.WaitAsync(ct);
            try
            {
                if (string.IsNullOrWhiteSpace(row.ExternalKey) || row.Date is null)
                    return CashFlowUpsertOutcome.Skipped;

                if (row.IsTransfer)
                    return await UpsertCashFlowTransferRowAsync(transferMetadata, transferAttributes, transferIndex, row, dryRun, ct);

                return await UpsertCashFlowMovementRowAsync(
                    movementMetadata,
                    movementAttributes,
                    movementIndex,
                    lockedClientPaymentMovementKeys,
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
        var result = new CashFlowDataverseUpsertResultDto();
        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case CashFlowUpsertOutcome.Created:
                    result.Created++;
                    break;
                case CashFlowUpsertOutcome.Updated:
                    result.Updated++;
                    break;
                case CashFlowUpsertOutcome.Unchanged:
                    result.Unchanged++;
                    break;
                case CashFlowUpsertOutcome.Skipped:
                    result.Skipped++;
                    break;
            }
        }

        return result;
    }

    private async Task<CashFlowUpsertOutcome> UpsertCashFlowMovementRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyDictionary<string, CashFlowExistingRecord> existingIndex,
        IReadOnlySet<string> lockedClientPaymentMovementKeys,
        CashFlowImportRowDto row,
        bool dryRun,
        CancellationToken ct)
    {
        var payload = BuildCashFlowMovementPayload(metadata, attributes, row);
        if (payload.Count == 0)
            return CashFlowUpsertOutcome.Unchanged;

        if (existingIndex.TryGetValue(row.ExternalKey, out var existing))
        {
            if (attributes.Contains(CashFlowSourceHashField)
                && !string.IsNullOrWhiteSpace(existing.SourceHash)
                && string.Equals(existing.SourceHash, row.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return CashFlowUpsertOutcome.Unchanged;
            }

            if (IsCashFlowPostSiigoLocked(existing, row.ExternalKey, lockedClientPaymentMovementKeys))
            {
                var lockedPayload = BuildCashFlowPostSiigoChangePayload(attributes, row, existing);
                if (lockedPayload.Count == 0)
                    return CashFlowUpsertOutcome.Unchanged;

                if (!dryRun)
                {
                    await CallDataverseAppSendAsync(
                        $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                        "PATCH",
                        lockedPayload,
                        ct);
                }

                return CashFlowUpsertOutcome.Updated;
            }

            if (!dryRun)
            {
                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                    "PATCH",
                    payload,
                    ct);
            }

            return CashFlowUpsertOutcome.Updated;
        }

        if (!dryRun)
        {
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}",
                "POST",
                payload,
                ct);
        }

        return CashFlowUpsertOutcome.Created;
    }

    private async Task<CashFlowUpsertOutcome> UpsertCashFlowTransferRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyDictionary<string, CashFlowExistingRecord> existingIndex,
        CashFlowImportRowDto row,
        bool dryRun,
        CancellationToken ct)
    {
        var payload = BuildCashFlowTransferPayload(metadata, attributes, row);
        if (payload.Count == 0)
            return CashFlowUpsertOutcome.Unchanged;

        if (existingIndex.TryGetValue(row.ExternalKey, out var existing))
        {
            if (attributes.Contains(CashFlowTransferSourceHashField)
                && !string.IsNullOrWhiteSpace(existing.SourceHash)
                && string.Equals(existing.SourceHash, row.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return CashFlowUpsertOutcome.Unchanged;
            }

            if (!dryRun)
            {
                await CallDataverseAppSendAsync(
                    $"/api/data/v9.2/{metadata.EntitySetName}({existing.Id})",
                    "PATCH",
                    payload,
                    ct);
            }

            return CashFlowUpsertOutcome.Updated;
        }

        if (!dryRun)
        {
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}",
                "POST",
                payload,
                ct);
        }

        return CashFlowUpsertOutcome.Created;
    }

    private async Task<IReadOnlyDictionary<string, CashFlowExistingRecord>> GetCashFlowExistingIndexAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        string externalKeyField,
        CancellationToken ct)
    {
        if (!attributes.Contains(externalKeyField))
            return new Dictionary<string, CashFlowExistingRecord>(StringComparer.OrdinalIgnoreCase);

        var hashField = attributes.Contains(CashFlowSourceHashField)
            ? CashFlowSourceHashField
            : attributes.Contains(CashFlowTransferSourceHashField)
                ? CashFlowTransferSourceHashField
                : "";
        var statusField = attributes.Contains(CashFlowStatusField)
            ? CashFlowStatusField
            : attributes.Contains(CashFlowTransferStatusField)
                ? CashFlowTransferStatusField
                : "";
        var siigoDocumentIdField = attributes.Contains(CashFlowSiigoDocumentIdField)
            ? CashFlowSiigoDocumentIdField
            : "";
        var siigoStatusField = attributes.Contains(CashFlowSiigoStatusField)
            ? CashFlowSiigoStatusField
            : "";
        var select = string.Join(",", new[] { metadata.PrimaryIdField, externalKeyField, hashField, statusField, siigoDocumentIdField, siigoStatusField }
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);

        return rows
            .Select(row => new
            {
                Id = ReadString(row, metadata.PrimaryIdField).Trim(),
                Key = ReadString(row, externalKeyField).Trim(),
                Hash = string.IsNullOrWhiteSpace(hashField) ? "" : ReadString(row, hashField).Trim(),
                Status = string.IsNullOrWhiteSpace(statusField) ? "" : ReadString(row, statusField).Trim(),
                SiigoDocumentId = string.IsNullOrWhiteSpace(siigoDocumentIdField) ? "" : ReadString(row, siigoDocumentIdField).Trim(),
                SiigoStatus = string.IsNullOrWhiteSpace(siigoStatusField) ? "" : ReadString(row, siigoStatusField).Trim()
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row.Id) && !string.IsNullOrWhiteSpace(row.Key))
            .GroupBy(static row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var first = group.First();
                    return new CashFlowExistingRecord(
                        first.Id,
                        first.Hash,
                        first.Status,
                        first.SiigoDocumentId,
                        first.SiigoStatus);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlySet<string>> GetCashFlowLockedClientPaymentMovementKeysAsync(CancellationToken ct)
    {
        var metadata = await ResolveFinancialReconciliationEntityMetadataAppAsync(
            ClientPaymentMatchLogicalName,
            ClientPaymentMatchSetName,
            ClientPaymentMatchIdField,
            ClientPaymentMatchPrimaryNameField,
            ct);
        var attributes = await GetFinancialReconciliationAttributeNamesAppAsync(metadata.LogicalName, ct);
        if (!attributes.Contains(ClientPaymentMatchMovementExternalKeyField)
            || !attributes.Contains(ClientPaymentMatchStatusField))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var select = string.Join(",", new[] { ClientPaymentMatchMovementExternalKeyField, ClientPaymentMatchStatusField });
        var filter = $"{ClientPaymentMatchStatusField} eq 'EnviadoSiigo' or {ClientPaymentMatchStatusField} eq 'Conciliado'";
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=5000";
        var rows = await GetDataverseAppEntitiesAsync(url, ct);

        return rows
            .Select(static row => ReadString(row, ClientPaymentMatchMovementExternalKeyField).Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> BuildCashFlowMovementPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CashFlowImportRowDto row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var name = BuildCashFlowPrimaryName(row);
        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, name, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowDateField, null, row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowBankField, null, row.BankAccountName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowDescriptionField, null, row.Description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowEntryField, (decimal?)null, row.Entry, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowExitField, (decimal?)null, row.Exit, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReferenceField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowMovementTypeField, null, row.MovementType, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "Importado", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, "", force: false);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, "Importado desde flujo de caja SharePoint. Descripcion usada para cruce con factura/documento.", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSourceFlowField, null, row.SourceFlow, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowBankAccountCodeField, null, row.BankAccountCode, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowBankAccountNameField, null, row.BankAccountName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowRecipientField, null, row.Recipient, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowDestinationBankField, null, row.DestinationBank, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowDocumentTypeField, null, row.DocumentType, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowObservationsField, null, row.Observations, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, row.SiigoStatus, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowExternalKeyField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSourceFileField, null, string.IsNullOrWhiteSpace(row.SourceFileName) ? "Pagos de facturas copiers y cloud.xlsx" : row.SourceFileName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSourceTableField, null, row.TableName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSourceRowField, (int?)null, row.RowNumber, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSourceHashField, null, row.SourceHash, force: true);
        return payload;
    }

    private static Dictionary<string, object?> BuildCashFlowTransferPayload(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CashFlowImportRowDto row)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var value = Math.Max(row.Entry, row.Exit);
        var name = BuildCashFlowPrimaryName(row);
        SetAccountCatalogValue(payload, attributes, metadata.PrimaryNameField, null, name, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDateField, null, row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferSourceFlowField, null, row.SourceFlow, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferFromField, null, row.TransferFrom, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferToField, null, row.TransferTo, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferEntryField, (decimal?)null, row.Entry, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferExitField, (decimal?)null, row.Exit, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferValueField, (decimal?)null, value, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDescriptionField, null, row.Description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferRecipientField, null, row.Recipient, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDestinationBankField, null, row.DestinationBank, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferDocumentTypeField, null, row.DocumentType, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferObservationsField, null, row.Observations, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, "InternoNoSiigo", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferExternalKeyField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferSourceFileField, null, string.IsNullOrWhiteSpace(row.SourceFileName) ? "Pagos de facturas copiers y cloud.xlsx" : row.SourceFileName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferSourceTableField, null, row.TableName, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferSourceRowField, (int?)null, row.RowNumber, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferSourceHashField, null, row.SourceHash, force: true);
        return payload;
    }

    private static Dictionary<string, object?> BuildCashFlowPostSiigoChangePayload(
        ISet<string> attributes,
        CashFlowImportRowDto row,
        CashFlowExistingRecord existing)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var message = BuildCashFlowPostSiigoChangeMessage(row, existing);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "CambioPostEnvio", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, message, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoStatusField, null, "CambioPostEnvio", force: true);
        return payload;
    }

    private static string BuildCashFlowPostSiigoChangeMessage(CashFlowImportRowDto row, CashFlowExistingRecord existing)
    {
        var oldHash = string.IsNullOrWhiteSpace(existing.SourceHash) ? "sin hash anterior" : existing.SourceHash;
        var newHash = string.IsNullOrWhiteSpace(row.SourceHash) ? "sin hash nuevo" : row.SourceHash;
        var siigo = string.IsNullOrWhiteSpace(existing.SiigoDocumentId)
            ? FirstNonEmpty(existing.SiigoStatus, existing.Status, "registro enviado")
            : existing.SiigoDocumentId;

        return TruncateAccountCatalogText(
            $"Cambio posterior detectado en el Excel para una fila ya enviada/conciliada en Siigo ({siigo}). No se sobreescribio fecha, descripcion ni valores. Hash anterior: {oldHash}. Hash Excel actual: {newHash}. Revisa si requiere ajuste manual.",
            1000);
    }

    private static bool IsCashFlowPostSiigoLocked(
        CashFlowExistingRecord existing,
        string externalKey,
        IReadOnlySet<string> lockedClientPaymentMovementKeys)
    {
        if (lockedClientPaymentMovementKeys.Contains(externalKey))
            return true;

        if (!string.IsNullOrWhiteSpace(existing.SiigoDocumentId))
            return true;

        return IsCashFlowPostSiigoStatus(existing.Status)
            || IsCashFlowPostSiigoStatus(existing.SiigoStatus);
    }

    private static bool IsCashFlowPostSiigoStatus(string? value)
    {
        var status = (value ?? "").Trim();
        return status.Equals("EnviadoSiigo", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Conciliado", StringComparison.OrdinalIgnoreCase)
            || status.Equals("CambioPostEnvio", StringComparison.OrdinalIgnoreCase)
            || status.Contains("ENVIAD", StringComparison.OrdinalIgnoreCase)
            || status.Contains("CONCILI", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildCashFlowMovementAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            CashFlowDateField,
            CashFlowBankField,
            CashFlowDescriptionField,
            CashFlowEntryField,
            CashFlowExitField,
            CashFlowReferenceField,
            CashFlowMovementTypeField,
            CashFlowStatusField,
            CashFlowSiigoDocumentIdField,
            CashFlowReviewReasonField,
            CashFlowSourceFlowField,
            CashFlowBankAccountCodeField,
            CashFlowBankAccountNameField,
            CashFlowRecipientField,
            CashFlowDestinationBankField,
            CashFlowDocumentTypeField,
            CashFlowObservationsField,
            CashFlowSiigoStatusField,
            CashFlowExternalKeyField,
            CashFlowSourceFileField,
            CashFlowSourceTableField,
            CashFlowSourceRowField,
            CashFlowSourceHashField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildCashFlowTransferAttributeSet(RhEntityMetadata metadata, ISet<string> attributes)
    {
        if (attributes.Count > 0)
            return new HashSet<string>(attributes.Concat(new[] { metadata.PrimaryIdField, metadata.PrimaryNameField }), StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            CashFlowTransferDateField,
            CashFlowTransferSourceFlowField,
            CashFlowTransferFromField,
            CashFlowTransferToField,
            CashFlowTransferEntryField,
            CashFlowTransferExitField,
            CashFlowTransferValueField,
            CashFlowTransferDescriptionField,
            CashFlowTransferRecipientField,
            CashFlowTransferDestinationBankField,
            CashFlowTransferDocumentTypeField,
            CashFlowTransferObservationsField,
            CashFlowTransferStatusField,
            CashFlowTransferExternalKeyField,
            CashFlowTransferSourceFileField,
            CashFlowTransferSourceTableField,
            CashFlowTransferSourceRowField,
            CashFlowTransferSourceHashField
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildCashFlowPrimaryName(CashFlowImportRowDto row)
    {
        var date = row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Sin fecha";
        var amount = Math.Max(row.Entry, row.Exit).ToString("0.##", CultureInfo.InvariantCulture);
        return TruncateAccountCatalogText($"{row.SourceFlow} {date} {row.MovementType} {amount} {row.Description}".Trim(), 100);
    }

    private enum CashFlowUpsertOutcome
    {
        Created,
        Updated,
        Unchanged,
        Skipped
    }

    private sealed record CashFlowExistingRecord(
        string Id,
        string SourceHash,
        string Status,
        string SiigoDocumentId,
        string SiigoStatus);
}
