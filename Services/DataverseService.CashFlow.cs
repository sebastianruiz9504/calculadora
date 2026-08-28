using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private const string CashFlowSiigoDocumentNameField = "cr07a_siigodocumentname";
    private const string CashFlowAccountingAccountCodeField = "cr07a_cuentacontablecodigo";
    private const string CashFlowAccountingAccountNameField = "cr07a_cuentacontablenombre";
    private const string CashFlowThirdPartyKeyField = "cr07a_siigoterceroclave";
    private const string CashFlowThirdPartyIdentificationField = "cr07a_siigoterceroidentificacion";
    private const string CashFlowThirdPartyNameField = "cr07a_siigoterceronombre";
    private const string CashFlowThirdPartyBranchOfficeField = "cr07a_siigotercerosucursal";
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
        var movementIdentityIndex = BuildCashFlowExistingIdentityIndex(movementIndex.Values);
        var transferIndex = await GetCashFlowExistingIndexAsync(
            transferMetadata,
            transferAttributes,
            CashFlowTransferExternalKeyField,
            ct);
        var transferIdentityIndex = BuildCashFlowExistingIdentityIndex(transferIndex.Values);
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
                    return await UpsertCashFlowTransferRowAsync(transferMetadata, transferAttributes, transferIndex, transferIdentityIndex, row, dryRun, ct);

                return await UpsertCashFlowMovementRowAsync(
                    movementMetadata,
                    movementAttributes,
                    movementIndex,
                    movementIdentityIndex,
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
        IReadOnlyDictionary<string, CashFlowExistingRecord> existingIdentityIndex,
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
            if (IsBancolombiaStatementRow(row))
                return CashFlowUpsertOutcome.Unchanged;

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

        if (existingIdentityIndex.TryGetValue(BuildCashFlowImportIdentityKey(row), out var existingByIdentity))
            return await UpdateCashFlowExistingByIdentityAsync(metadata, attributes, row, existingByIdentity, dryRun, ct);

        if (!dryRun)
        {
            return await CreateCashFlowRecordAsync(metadata, payload, row, ct);
        }

        return CashFlowUpsertOutcome.Created;
    }

    private async Task<CashFlowUpsertOutcome> UpsertCashFlowTransferRowAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        IReadOnlyDictionary<string, CashFlowExistingRecord> existingIndex,
        IReadOnlyDictionary<string, CashFlowExistingRecord> existingIdentityIndex,
        CashFlowImportRowDto row,
        bool dryRun,
        CancellationToken ct)
    {
        var payload = BuildCashFlowTransferPayload(metadata, attributes, row);
        if (payload.Count == 0)
            return CashFlowUpsertOutcome.Unchanged;

        if (existingIndex.TryGetValue(row.ExternalKey, out var existing))
        {
            if (IsBancolombiaStatementRow(row))
                return CashFlowUpsertOutcome.Unchanged;

            if (attributes.Contains(CashFlowTransferSourceHashField)
                && !string.IsNullOrWhiteSpace(existing.SourceHash)
                && string.Equals(existing.SourceHash, row.SourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return CashFlowUpsertOutcome.Unchanged;
            }

            if (IsCashFlowExistingPostSiigoLocked(existing))
            {
                var lockedPayload = BuildCashFlowTransferPostSiigoChangePayload(attributes);
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

        if (existingIdentityIndex.TryGetValue(BuildCashFlowImportIdentityKey(row), out var existingByIdentity))
            return await UpdateCashFlowExistingByIdentityAsync(metadata, attributes, row, existingByIdentity, dryRun, ct);

        if (!dryRun)
        {
            return await CreateCashFlowRecordAsync(metadata, payload, row, ct);
        }

        return CashFlowUpsertOutcome.Created;
    }

    private async Task<CashFlowUpsertOutcome> CreateCashFlowRecordAsync(
        RhEntityMetadata metadata,
        IReadOnlyDictionary<string, object?> payload,
        CashFlowImportRowDto row,
        CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}",
            "POST",
            ct,
            content);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return CashFlowUpsertOutcome.Created;

        if (IsCashFlowDuplicateCreate(response.StatusCode, body))
        {
            _logger.LogInformation(
                "Dataverse omitio un movimiento ya existente durante una importacion concurrente. ExternalKey={ExternalKey}.",
                row.ExternalKey);
            return CashFlowUpsertOutcome.Unchanged;
        }

        _logger.LogWarning(
            "Dataverse app error {StatusCode} {ReasonPhrase} creando flujo de caja. Body: {Body}",
            (int)response.StatusCode,
            response.ReasonPhrase,
            body);
        throw new InvalidOperationException(BuildDataverseAppFailureMessage(response.StatusCode));
    }

    private static bool IsCashFlowDuplicateCreate(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.PreconditionFailed)
            return true;

        return responseBody.Contains("0x80040237", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("matching key values already exists", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBancolombiaStatementRow(CashFlowImportRowDto row)
    {
        return row.SourceSystem.Equals("Bancolombia", StringComparison.OrdinalIgnoreCase)
            && row.ExternalKey.StartsWith("bancolombia:", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CashFlowUpsertOutcome> UpdateCashFlowExistingByIdentityAsync(
        RhEntityMetadata metadata,
        ISet<string> attributes,
        CashFlowImportRowDto row,
        CashFlowExistingRecord existing,
        bool dryRun,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(existing.SourceHash)
            && string.Equals(existing.SourceHash, row.SourceHash, StringComparison.OrdinalIgnoreCase))
        {
            return CashFlowUpsertOutcome.Unchanged;
        }

        if (IsCashFlowExistingPostSiigoLocked(existing))
        {
            var lockedPayload = row.IsTransfer
                ? BuildCashFlowTransferPostSiigoChangePayload(attributes)
                : BuildCashFlowPostSiigoChangePayload(attributes, row, existing);
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

        var payload = row.IsTransfer
            ? BuildCashFlowTransferPayload(metadata, attributes, row)
            : BuildCashFlowMovementPayload(metadata, attributes, row);
        RemoveCashFlowExternalKeyPayloadValues(payload);
        if (payload.Count == 0)
            return CashFlowUpsertOutcome.Unchanged;

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

    private static void RemoveCashFlowExternalKeyPayloadValues(IDictionary<string, object?> payload)
    {
        payload.Remove(CashFlowReferenceField);
        payload.Remove(CashFlowExternalKeyField);
        payload.Remove(CashFlowTransferExternalKeyField);
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
        var select = string.Join(",", new[]
            {
                metadata.PrimaryIdField,
                externalKeyField,
                hashField,
                statusField,
                siigoDocumentIdField,
                siigoStatusField,
                CashFlowSourceFlowField,
                CashFlowSourceTableField,
                CashFlowDateField,
                CashFlowEntryField,
                CashFlowExitField,
                CashFlowDescriptionField,
                CashFlowRecipientField,
                CashFlowDestinationBankField,
                CashFlowDocumentTypeField,
                CashFlowObservationsField,
                CashFlowTransferSourceFlowField,
                CashFlowTransferSourceTableField,
                CashFlowTransferDateField,
                CashFlowTransferEntryField,
                CashFlowTransferExitField,
                CashFlowTransferDescriptionField,
                CashFlowTransferRecipientField,
                CashFlowTransferDestinationBankField,
                CashFlowTransferDocumentTypeField,
                CashFlowTransferObservationsField
            }
            .Where(field => !string.IsNullOrWhiteSpace(field)
                && (attributes.Contains(field) || string.Equals(field, metadata.PrimaryIdField, StringComparison.OrdinalIgnoreCase)))
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
                SiigoStatus = string.IsNullOrWhiteSpace(siigoStatusField) ? "" : ReadString(row, siigoStatusField).Trim(),
                IdentityKey = BuildCashFlowExistingIdentityKey(row)
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
                        first.Key,
                        first.Hash,
                        first.Status,
                        first.SiigoDocumentId,
                        first.SiigoStatus,
                        first.IdentityKey);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, CashFlowExistingRecord> BuildCashFlowExistingIdentityIndex(
        IEnumerable<CashFlowExistingRecord> records)
    {
        return records
            .Where(static record => !string.IsNullOrWhiteSpace(record.IdentityKey))
            .GroupBy(static record => record.IdentityKey, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
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
        if (!row.PreserveExistingDescription || !string.IsNullOrWhiteSpace(row.Description))
            SetAccountCatalogValue(payload, attributes, CashFlowDescriptionField, null, row.Description, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowEntryField, (decimal?)null, row.Entry, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowExitField, (decimal?)null, row.Exit, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowReferenceField, null, row.ExternalKey, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowMovementTypeField, null, row.MovementType, force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowStatusField, null, "Importado", force: true);
        SetAccountCatalogValue(payload, attributes, CashFlowSiigoDocumentIdField, null, "", force: false);
        SetAccountCatalogValue(payload, attributes, CashFlowReviewReasonField, null, BuildCashFlowImportReviewReason(row), force: true);
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

    private static Dictionary<string, object?> BuildCashFlowTransferPostSiigoChangePayload(ISet<string> attributes)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        SetAccountCatalogValue(payload, attributes, CashFlowTransferStatusField, null, "CambioPostEnvio", force: true);
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

    private static bool IsCashFlowExistingPostSiigoLocked(CashFlowExistingRecord existing)
    {
        if (!string.IsNullOrWhiteSpace(existing.SiigoDocumentId))
            return true;

        return IsCashFlowPostSiigoStatus(existing.Status)
            || IsCashFlowPostSiigoStatus(existing.SiigoStatus);
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
            CashFlowSiigoDocumentNameField,
            CashFlowAccountingAccountCodeField,
            CashFlowAccountingAccountNameField,
            CashFlowThirdPartyKeyField,
            CashFlowThirdPartyIdentificationField,
            CashFlowThirdPartyNameField,
            CashFlowThirdPartyBranchOfficeField,
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
        var detail = FirstNonEmpty(row.Description, row.Observations, row.Recipient, row.DocumentType);
        return TruncateAccountCatalogText($"{row.SourceFlow} {date} {row.MovementType} {amount} {detail}".Trim(), 100);
    }

    private static string BuildCashFlowImportReviewReason(CashFlowImportRowDto row)
    {
        if (row.SourceSystem.Equals("Bancolombia", StringComparison.OrdinalIgnoreCase))
        {
            return "Importado desde extracto Bancolombia. La descripcion queda como nota interna para digitacion manual; el detalle bancario queda en observaciones.";
        }

        return "Importado desde flujo de caja SharePoint. Descripcion usada para cruce con factura/documento.";
    }

    private static string BuildCashFlowImportIdentityKey(CashFlowImportRowDto row)
    {
        return BuildCashFlowIdentityKey(
            row.SourceFlow,
            row.TableName,
            row.Date,
            row.Entry,
            row.Exit,
            row.Description,
            row.Recipient,
            row.DestinationBank,
            row.DocumentType,
            row.Observations);
    }

    private static string BuildCashFlowExistingIdentityKey(JsonElement item)
    {
        var date = ReadDateOnly(item, CashFlowDateField)
            ?? ReadDateOnly(item, CashFlowTransferDateField);
        var entry = ReadDecimal(item, CashFlowEntryField)
            ?? ReadDecimal(item, CashFlowTransferEntryField)
            ?? 0m;
        var exit = ReadDecimal(item, CashFlowExitField)
            ?? ReadDecimal(item, CashFlowTransferExitField)
            ?? 0m;

        return BuildCashFlowIdentityKey(
            FirstNonEmpty(ReadString(item, CashFlowSourceFlowField), ReadString(item, CashFlowTransferSourceFlowField)),
            FirstNonEmpty(ReadString(item, CashFlowSourceTableField), ReadString(item, CashFlowTransferSourceTableField)),
            date,
            entry,
            exit,
            FirstNonEmpty(ReadString(item, CashFlowDescriptionField), ReadString(item, CashFlowTransferDescriptionField)),
            FirstNonEmpty(ReadString(item, CashFlowRecipientField), ReadString(item, CashFlowTransferRecipientField)),
            FirstNonEmpty(ReadString(item, CashFlowDestinationBankField), ReadString(item, CashFlowTransferDestinationBankField)),
            FirstNonEmpty(ReadString(item, CashFlowDocumentTypeField), ReadString(item, CashFlowTransferDocumentTypeField)),
            FirstNonEmpty(ReadString(item, CashFlowObservationsField), ReadString(item, CashFlowTransferObservationsField)));
    }

    private static string BuildCashFlowIdentityKey(
        string sourceFlow,
        string tableName,
        DateOnly? date,
        decimal entry,
        decimal exit,
        string description,
        string recipient,
        string destinationBank,
        string documentType,
        string observations)
    {
        if (date is null || (entry == 0m && exit == 0m))
            return "";

        return string.Join("|", new[]
        {
            NormalizeCashFlowIdentityText(sourceFlow),
            NormalizeCashFlowIdentityText(tableName),
            date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.ToString("0.##", CultureInfo.InvariantCulture),
            exit.ToString("0.##", CultureInfo.InvariantCulture),
            NormalizeCashFlowIdentityText(description),
            NormalizeCashFlowIdentityText(recipient),
            NormalizeCashFlowIdentityText(destinationBank),
            NormalizeCashFlowIdentityText(documentType),
            NormalizeCashFlowIdentityText(observations)
        });
    }

    private static string NormalizeCashFlowIdentityText(string? value)
    {
        var normalized = (value ?? "").Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
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
        string ExternalKey,
        string SourceHash,
        string Status,
        string SiigoDocumentId,
        string SiigoStatus,
        string IdentityKey);
}
