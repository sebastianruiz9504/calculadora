using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>
/// Dedicated adapter for the parallel MTO V2 tables. It never writes to the
/// legacy maintenance table and it only publishes ReadyToSend after every file
/// has been uploaded and verified by read-back.
/// </summary>
public sealed class CopiersMaintenanceV2DataverseRepository : ICopiersMaintenanceV2DataverseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan FinalizationLeaseTimeout = TimeSpan.FromMinutes(15);

    private readonly ICopiersMtoV2ApplicationDataverseClient _dataverseClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CopiersMaintenanceV2DataverseOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CopiersMaintenanceV2DataverseRepository> _logger;

    public CopiersMaintenanceV2DataverseRepository(
        ICopiersMtoV2ApplicationDataverseClient dataverseClient,
        IHttpContextAccessor httpContextAccessor,
        IOptions<CopiersMaintenanceV2DataverseOptions> options,
        TimeProvider timeProvider,
        ILogger<CopiersMaintenanceV2DataverseRepository> logger)
    {
        _dataverseClient = dataverseClient;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CopiersMaintenanceV2DraftRecord> CreateOrGetDraftAsync(
        CopiersMaintenanceV2CreateDraftCommand command,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var user = RequireUser();
        var existing = await FindByOperationKeyAsync(command.SubmissionKey, user, ct);
        if (existing is not null)
            return existing;

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [_options.MainNameField] = BuildRecordName(command),
            [_options.OperationKeyField] = command.SubmissionKey,
            [_options.WorkflowStateField] = _options.DraftStateValue,
            [_options.EmailStateField] = _options.EmailNotReadyStateValue,
            [_options.TechnicianUserIdField] = command.TechnicianSystemUserId,
            [_options.TechnicianNameField] = command.TechnicianName,
            [_options.TechnicianEmailField] = command.TechnicianEmail,
            [_options.ClientNameField] = command.ClientName,
            [_options.ClientContactNameField] = command.CustomerContactName,
            [_options.ClientEmailField] = command.CustomerEmail,
            [_options.EquipmentSerialField] = command.EquipmentSerial,
            [_options.TitleField] = command.Title,
            [_options.ServiceDateField] = command.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [_options.MaintenanceTypeField] = command.MaintenanceTypeValue,
            [$"{_options.ClientNavigationProperty}@odata.bind"] =
                $"/{_options.ClientEntitySetName}({NormalizeGuid(command.ClientId, nameof(command.ClientId))})"
        };
        if (!string.IsNullOrWhiteSpace(command.EquipmentId))
        {
            payload[$"{_options.EquipmentNavigationProperty}@odata.bind"] =
                $"/{_options.EquipmentEntitySetName}({NormalizeGuid(command.EquipmentId, nameof(command.EquipmentId))})";
        }
        else
        {
            payload[$"{_options.EquipmentNavigationProperty}@odata.bind"] = null;
        }

        using var response = await SendJsonAsync(
            $"/api/data/v9.2/{_options.MainEntitySetName}",
            HttpMethod.Post,
            payload,
            user,
            ct,
            request => request.Headers.TryAddWithoutValidation("Prefer", "return=representation"));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                var raced = await FindByOperationKeyAsync(command.SubmissionKey, user, ct);
                if (raced is not null)
                    return raced;
            }

            throw BuildDataverseException("crear el borrador MTO V2", response, body);
        }

        var recordId = ExtractRecordId(response, body, _options.MainIdField);
        if (string.IsNullOrWhiteSpace(recordId))
        {
            var reread = await FindByOperationKeyAsync(command.SubmissionKey, user, ct);
            if (reread is not null)
                return reread;
            throw new InvalidOperationException("Dataverse creó el borrador MTO V2, pero no devolvió su identificador.");
        }

        var created = await GetByIdAsync(recordId, user, ct)
            ?? throw new InvalidOperationException("No fue posible releer el borrador MTO V2 recién creado.");
        created.WasCreated = true;
        return created;
    }

    public async Task<CopiersMaintenanceV2DraftRecord> SaveDraftAsync(
        CopiersMaintenanceV2SaveDraftCommand command,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var user = RequireUser();
        var current = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
        EnsureOperationKey(current, command.SubmissionKey);
        if (current.State is not CopiersMaintenanceV2WorkflowState.Draft and not CopiersMaintenanceV2WorkflowState.Failed)
            throw new CopiersMaintenanceV2ConcurrencyException("El reporte ya no está editable.");
        EnsureVersion(current, command.ExpectedVersion);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [_options.MainNameField] = BuildRecordName(command),
            [_options.ClientNameField] = command.ClientName,
            [_options.ClientContactNameField] = command.CustomerContactName,
            [_options.ClientEmailField] = command.CustomerEmail,
            [_options.EquipmentSerialField] = command.EquipmentSerial,
            [_options.TitleField] = command.Title,
            [_options.ServiceDateField] = command.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [_options.MaintenanceTypeField] = command.MaintenanceTypeValue,
            [$"{_options.ClientNavigationProperty}@odata.bind"] =
                $"/{_options.ClientEntitySetName}({NormalizeGuid(command.ClientId, nameof(command.ClientId))})"
        };
        if (!string.IsNullOrWhiteSpace(command.EquipmentId))
        {
            payload[$"{_options.EquipmentNavigationProperty}@odata.bind"] =
                $"/{_options.EquipmentEntitySetName}({NormalizeGuid(command.EquipmentId, nameof(command.EquipmentId))})";
        }
        else
        {
            payload[$"{_options.EquipmentNavigationProperty}@odata.bind"] = null;
        }

        await PatchWithVersionAsync(current.RecordId, current.Version, payload, user, ct);
        return await GetByIdAsync(current.RecordId, user, ct)
            ?? throw new InvalidOperationException("No fue posible releer el borrador MTO V2 actualizado.");
    }

    public async Task<CopiersMaintenanceV2BeginFinalizationResult> TryBeginFinalizationAsync(
        CopiersMaintenanceV2BeginFinalizationCommand command,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var user = RequireUser();
        var current = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
        EnsureOperationKey(current, command.SubmissionKey);

        if (current.State == CopiersMaintenanceV2WorkflowState.ReadyToSend)
            return Begin(CopiersMaintenanceV2BeginDisposition.AlreadyReady, current, current.Version, "El reporte ya está listo para envío.");
        if (current.State == CopiersMaintenanceV2WorkflowState.Finalizing
            && current.UpdatedAtUtc > command.StartedAtUtc - FinalizationLeaseTimeout)
        {
            return Begin(CopiersMaintenanceV2BeginDisposition.InProgress, current, "", "Otro proceso está finalizando este reporte.");
        }
        if (current.State is not CopiersMaintenanceV2WorkflowState.Draft
            and not CopiersMaintenanceV2WorkflowState.Failed
            and not CopiersMaintenanceV2WorkflowState.Finalizing)
        {
            return Begin(CopiersMaintenanceV2BeginDisposition.Conflict, current, "", "El estado actual no permite finalizar.");
        }
        if (!string.Equals(current.Version, command.ExpectedVersion, StringComparison.Ordinal))
            return Begin(CopiersMaintenanceV2BeginDisposition.Conflict, current, "", "La fila cambió desde la última lectura.");

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [_options.WorkflowStateField] = _options.FinalizingStateValue,
            [_options.EmailStateField] = _options.EmailNotReadyStateValue,
            [_options.FinalizationLeaseIdField] = command.FinalizationLeaseId,
            [_options.ReadyAtUtcField] = null,
            [_options.LastErrorCodeField] = null,
            [_options.LastErrorMessageField] = null
        };

        try
        {
            await PatchWithVersionAsync(current.RecordId, current.Version, payload, user, ct);
        }
        catch (CopiersMaintenanceV2ConcurrencyException)
        {
            var reread = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
            return Begin(CopiersMaintenanceV2BeginDisposition.Conflict, reread, "", "La fila fue modificada por otro proceso.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                var reconciled = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
                await EnsureLeaseAsync(reconciled, command.FinalizationLeaseId, user, ct);
                _logger.LogWarning(ex, "Se reconcilió una respuesta ambigua al adquirir el lease MTO V2 {RecordId}.", command.RecordId);
                return Begin(CopiersMaintenanceV2BeginDisposition.Acquired, reconciled, command.FinalizationLeaseId, "Finalización adquirida y reconciliada.");
            }
            catch (Exception reconcileException)
            {
                _logger.LogWarning(reconcileException, "No fue posible reconciliar el lease MTO V2 {RecordId}.", command.RecordId);
            }
            throw;
        }

        var acquired = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
        return Begin(CopiersMaintenanceV2BeginDisposition.Acquired, acquired, command.FinalizationLeaseId, "Finalización adquirida.");
    }

    public async Task<CopiersMaintenanceV2DraftRecord> CompleteFinalizationAsync(
        CopiersMaintenanceV2CompleteFinalizationCommand command,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var user = RequireUser();
        var current = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
        EnsureOperationKey(current, command.SubmissionKey);
        await EnsureLeaseAsync(current, command.FinalizationLeaseId, user, ct);

        var signatureEvidenceKey = await UpsertEvidenceAsync(
            current.RecordId,
            command.SubmissionKey,
            "signature",
            _options.EvidenceSignaturePurposeValue,
            0,
            command.Signature,
            "",
            _options.EvidenceSecurityNotApplicableValue,
            null,
            "not-applicable",
            user,
            ct);
        var reportEvidenceKey = await UpsertEvidenceAsync(
            current.RecordId,
            command.SubmissionKey,
            "signed-report",
            _options.EvidenceSignedReportPurposeValue,
            0,
            command.SignedReport,
            "",
            _options.EvidenceSecurityNotApplicableValue,
            null,
            "not-applicable",
            user,
            ct);
        var attachmentSecurityCheckedAtUtc = _timeProvider.GetUtcNow();
        var originalEvidenceKeys = new List<string>(command.OriginalAttachments.Count);
        for (var index = 0; index < command.OriginalAttachments.Count; index++)
        {
            originalEvidenceKeys.Add(await UpsertEvidenceAsync(
                current.RecordId,
                command.SubmissionKey,
                "attachment-original",
                _options.EvidenceOriginalAttachmentPurposeValue,
                index + 1,
                command.OriginalAttachments[index],
                "",
                _options.EvidenceSecurityScanPassedValue,
                attachmentSecurityCheckedAtUtc,
                "server-image-cdr-v1",
                user,
                ct));
        }
        var attachmentEvidenceKeys = new List<string>(command.CustomerAttachments.Count);
        for (var index = 0; index < command.CustomerAttachments.Count; index++)
        {
            attachmentEvidenceKeys.Add(await UpsertEvidenceAsync(
                current.RecordId,
                command.SubmissionKey,
                "attachment-customer",
                _options.EvidenceCustomerAttachmentPurposeValue,
                index + 1,
                command.CustomerAttachments[index],
                originalEvidenceKeys[index],
                _options.EvidenceSecurityScanPassedValue,
                attachmentSecurityCheckedAtUtc,
                "server-image-cdr-v1",
                user,
                ct));
        }

        // Evidence writes must never authorize adopting a newer parent ETag. If a
        // plugin or actor changed the ticket while files were uploaded, the staging
        // PATCH below fails with 412 instead of publishing a row different from the PDF.
        await EnsureLeaseAsync(current, command.FinalizationLeaseId, user, ct);

        var answersJson = JsonSerializer.Serialize(command.Answers, JsonOptions);
        var manifestJson = JsonSerializer.Serialize(
            command.CustomerAttachments.Select((file, index) => new
            {
                sequence = index + 1,
                evidenceKey = attachmentEvidenceKeys[index],
                file.FileName,
                file.ContentType,
                file.Size,
                sha256 = file.Sha256.ToLowerInvariant(),
                purpose = "CustomerAttachment",
                securityState = "ScanPassed"
            }),
            JsonOptions);
        var location = command.InternalLocation;
        var stagingPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [_options.WorkflowStateField] = _options.FinalizingStateValue,
            [_options.EmailStateField] = _options.EmailNotReadyStateValue,
            [_options.FormVersionField] = command.FormVersion,
            [_options.AnswersJsonField] = answersJson,
            [_options.WorkPerformedField] = command.WorkPerformed,
            [_options.CustomerObservationsField] = command.CustomerObservations,
            [_options.ServiceAddressInternalField] = command.ServiceAddressInternal,
            [_options.InternalNotesField] = command.InternalNotes,
            [_options.SignerNameField] = command.SignerName,
            [_options.SignerRoleField] = command.SignerRole,
            [_options.CustomerAcceptedField] = command.CustomerAccepted,
            [_options.SignaturePointCountField] = command.SignaturePointCount,
            [_options.DeviceSignedAtUtcField] = command.DeviceSignedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [_options.ServerFinalizedAtUtcField] = command.ServerFinalizedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [_options.LatitudeField] = location?.Latitude,
            [_options.LongitudeField] = location?.Longitude,
            [_options.AccuracyMetersField] = location?.AccuracyMeters,
            [_options.LocationCapturedAtUtcField] = location?.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [_options.LocationSourceField] = location?.Source ?? "not-captured",
            [_options.SignatureSha256Field] = command.Signature.Sha256.ToLowerInvariant(),
            [_options.SignatureEvidenceKeyField] = signatureEvidenceKey,
            [_options.SignedReportEvidenceKeyField] = reportEvidenceKey,
            [_options.SignedReportSha256Field] = command.SignedReport.Sha256.ToLowerInvariant(),
            [_options.SignedReportFileNameField] = command.SignedReport.FileName,
            [_options.AttachmentCountField] = command.CustomerAttachments.Count,
            [_options.AttachmentManifestJsonField] = manifestJson,
            [_options.FinalizationFingerprintField] = command.FinalizationFingerprint,
            [_options.EmailOutboxKeyField] = command.EmailOutbox.OutboxKey,
            [_options.EmailToField] = string.Join(";", command.EmailOutbox.To),
            [_options.EmailSubjectField] = command.EmailOutbox.Subject,
            [_options.EmailHtmlBodyField] = command.EmailOutbox.HtmlBody,
            [_options.ReadyAtUtcField] = null,
            [_options.LastErrorCodeField] = null,
            [_options.LastErrorMessageField] = null
        };

        await PatchWithVersionAsync(current.RecordId, current.Version, stagingPayload, user, ct);
        var staged = await GetFinalizationSnapshotAsync(current.RecordId, user, ct);
        VerifyFinalizationSnapshot(
            staged,
            command,
            answersJson,
            manifestJson,
            signatureEvidenceKey,
            reportEvidenceKey,
            expectedWorkflowState: _options.FinalizingStateValue,
            expectedEmailStates: new HashSet<int> { _options.EmailNotReadyStateValue },
            expectedReadyAtUtc: null);

        // The publication patch is deliberately minimal and guarded by the ETag
        // returned by the complete staging read-back. This is the only transition
        // that the dedicated Power Automate trigger is allowed to claim.
        var readyAtUtc = _timeProvider.GetUtcNow();
        await PatchWithVersionAsync(current.RecordId, staged.Version, new Dictionary<string, object?>
        {
            [_options.ReadyAtUtcField] = readyAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [_options.EmailStateField] = _options.EmailPendingStateValue,
            [_options.WorkflowStateField] = _options.ReadyToSendStateValue
        }, user, ct);

        var published = await GetFinalizationSnapshotAsync(current.RecordId, user, ct);
        VerifyFinalizationSnapshot(
            published,
            command,
            answersJson,
            manifestJson,
            signatureEvidenceKey,
            reportEvidenceKey,
            expectedWorkflowState: _options.ReadyToSendStateValue,
            expectedEmailStates: new HashSet<int>
            {
                _options.EmailPendingStateValue,
                _options.EmailProcessingStateValue,
                _options.EmailSentStateValue,
                _options.EmailFailedStateValue
            },
            expectedReadyAtUtc: readyAtUtc);

        var completed = await GetOwnedAsync(current.RecordId, command.TechnicianSystemUserId, user, ct);
        if (completed.State != CopiersMaintenanceV2WorkflowState.ReadyToSend
            || completed.EmailState == CopiersMaintenanceV2EmailState.NotReady
            || !string.Equals(completed.SignatureEvidenceKey, signatureEvidenceKey, StringComparison.Ordinal)
            || !string.Equals(completed.ReportEvidenceKey, reportEvidenceKey, StringComparison.Ordinal)
            || !string.Equals(completed.ReportSha256, command.SignedReport.Sha256, StringComparison.OrdinalIgnoreCase)
            || completed.AttachmentCount != command.CustomerAttachments.Count
            || !string.Equals(completed.FinalizationFingerprint, command.FinalizationFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El read-back de Dataverse no confirmó todos los artefactos publicados del MTO V2.");
        }
        return completed;
    }

    public async Task<CopiersMaintenanceV2DraftRecord> MarkFinalizationFailedAsync(
        CopiersMaintenanceV2FinalizationFailedCommand command,
        CancellationToken ct = default)
    {
        EnsureConfigured();
        var user = RequireUser();
        var current = await GetOwnedAsync(command.RecordId, command.TechnicianSystemUserId, user, ct);
        EnsureOperationKey(current, command.SubmissionKey);
        if (current.State == CopiersMaintenanceV2WorkflowState.ReadyToSend)
            return current;

        await EnsureLeaseAsync(current, command.FinalizationLeaseId, user, ct);
        await PatchWithVersionAsync(current.RecordId, current.Version, new Dictionary<string, object?>
        {
            [_options.WorkflowStateField] = _options.FailedStateValue,
            [_options.EmailStateField] = _options.EmailNotReadyStateValue,
            [_options.ReadyAtUtcField] = null,
            [_options.LastErrorCodeField] = Truncate(command.ErrorCode, 80),
            [_options.LastErrorMessageField] = Truncate(command.ErrorMessage, 1500)
        }, user, ct);
        return await GetOwnedAsync(current.RecordId, command.TechnicianSystemUserId, user, ct);
    }

    private async Task<CopiersMaintenanceV2DraftRecord?> FindByOperationKeyAsync(
        string operationKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{_options.OperationKeyField} eq '{EscapeODataLiteral(operationKey)}'";
        var relativeUrl =
            $"/api/data/v9.2/{_options.MainEntitySetName}?$select={BuildMainSelect()}" +
            $"&$filter={Uri.EscapeDataString(filter)}&$top=2";
        using var response = await SendAsync(relativeUrl, HttpMethod.Get, user, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess("consultar el MTO V2 por clave", response, body);
        using var document = JsonDocument.Parse(body);
        var rows = document.RootElement.GetProperty("value");
        if (rows.GetArrayLength() == 0)
            return null;
        if (rows.GetArrayLength() > 1)
            throw new InvalidOperationException("La clave idempotente del MTO V2 está duplicada en Dataverse.");
        return ParseRecord(rows[0]);
    }

    private async Task<CopiersMaintenanceV2DraftRecord?> GetByIdAsync(
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{_options.MainEntitySetName}({NormalizeGuid(recordId, nameof(recordId))})" +
            $"?$select={BuildMainSelect()}";
        using var response = await SendAsync(relativeUrl, HttpMethod.Get, user, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess("releer el MTO V2", response, body);
        using var document = JsonDocument.Parse(body);
        return ParseRecord(document.RootElement);
    }

    private async Task<CopiersMaintenanceV2DraftRecord> GetOwnedAsync(
        string recordId,
        string technicianSystemUserId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var record = await GetByIdAsync(recordId, user, ct)
            ?? throw new InvalidOperationException("El MTO V2 no existe o ya no está disponible.");
        if (!string.Equals(record.TechnicianSystemUserId, technicianSystemUserId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("El MTO V2 no pertenece al técnico autenticado.");
        return record;
    }

    private async Task EnsureLeaseAsync(
        CopiersMaintenanceV2DraftRecord current,
        string expectedLeaseId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var relativeUrl =
            $"/api/data/v9.2/{_options.MainEntitySetName}({NormalizeGuid(current.RecordId, nameof(current.RecordId))})" +
            $"?$select={_options.FinalizationLeaseIdField},{_options.WorkflowStateField}";
        using var response = await SendAsync(relativeUrl, HttpMethod.Get, user, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess("verificar el lease de finalización", response, body);
        using var document = JsonDocument.Parse(body);
        var actual = ReadString(document.RootElement, _options.FinalizationLeaseIdField);
        var workflowState = ReadInt(document.RootElement, _options.WorkflowStateField);
        if (!string.Equals(actual, expectedLeaseId, StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ConcurrencyException("La finalización pertenece a otro proceso.");
        if (workflowState != _options.FinalizingStateValue)
            throw new CopiersMaintenanceV2ConcurrencyException("El MTO V2 ya no está en estado de finalización.");
    }

    private async Task<CopiersMaintenanceV2FinalizationSnapshot> GetFinalizationSnapshotAsync(
        string recordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var fields = string.Join(",", new[]
        {
            _options.TechnicianUserIdField,
            _options.TechnicianNameField,
            _options.TechnicianEmailField,
            _options.ClientNameField,
            _options.ClientContactNameField,
            _options.ClientEmailField,
            _options.EquipmentSerialField,
            _options.TitleField,
            _options.ServiceDateField,
            _options.MaintenanceTypeField,
            $"_{_options.ClientLookupLogicalName}_value",
            $"_{_options.EquipmentLookupLogicalName}_value",
            _options.WorkflowStateField,
            _options.EmailStateField,
            _options.FinalizationLeaseIdField,
            _options.FormVersionField,
            _options.AnswersJsonField,
            _options.WorkPerformedField,
            _options.CustomerObservationsField,
            _options.ServiceAddressInternalField,
            _options.InternalNotesField,
            _options.SignerNameField,
            _options.SignerRoleField,
            _options.CustomerAcceptedField,
            _options.SignaturePointCountField,
            _options.DeviceSignedAtUtcField,
            _options.ServerFinalizedAtUtcField,
            _options.LatitudeField,
            _options.LongitudeField,
            _options.AccuracyMetersField,
            _options.LocationCapturedAtUtcField,
            _options.LocationSourceField,
            _options.SignatureSha256Field,
            _options.SignatureEvidenceKeyField,
            _options.SignedReportEvidenceKeyField,
            _options.SignedReportFileNameField,
            _options.SignedReportSha256Field,
            _options.AttachmentCountField,
            _options.AttachmentManifestJsonField,
            _options.FinalizationFingerprintField,
            _options.EmailOutboxKeyField,
            _options.EmailToField,
            _options.EmailSubjectField,
            _options.EmailHtmlBodyField,
            _options.ReadyAtUtcField
        }.Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl =
            $"/api/data/v9.2/{_options.MainEntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={fields}";
        using var response = await SendAsync(relativeUrl, HttpMethod.Get, user, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess("releer el staging completo del MTO V2", response, body);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement;
        return new CopiersMaintenanceV2FinalizationSnapshot
        {
            Version = ReadString(item, "@odata.etag"),
            TechnicianSystemUserId = ReadString(item, _options.TechnicianUserIdField),
            TechnicianName = ReadString(item, _options.TechnicianNameField),
            TechnicianEmail = ReadString(item, _options.TechnicianEmailField),
            ClientId = ReadString(item, $"_{_options.ClientLookupLogicalName}_value"),
            ClientName = ReadString(item, _options.ClientNameField),
            CustomerContactName = ReadString(item, _options.ClientContactNameField),
            CustomerEmail = ReadString(item, _options.ClientEmailField),
            EquipmentId = ReadString(item, $"_{_options.EquipmentLookupLogicalName}_value"),
            EquipmentSerial = ReadString(item, _options.EquipmentSerialField),
            Title = ReadString(item, _options.TitleField),
            ServiceDate = ReadDateOnly(item, _options.ServiceDateField),
            MaintenanceTypeValue = ReadNullableInt(item, _options.MaintenanceTypeField),
            WorkflowState = ReadInt(item, _options.WorkflowStateField),
            EmailState = ReadInt(item, _options.EmailStateField),
            FinalizationLeaseId = ReadString(item, _options.FinalizationLeaseIdField),
            FormVersion = ReadString(item, _options.FormVersionField),
            AnswersJson = ReadString(item, _options.AnswersJsonField),
            WorkPerformed = ReadString(item, _options.WorkPerformedField),
            CustomerObservations = ReadString(item, _options.CustomerObservationsField),
            ServiceAddressInternal = ReadString(item, _options.ServiceAddressInternalField),
            InternalNotes = ReadString(item, _options.InternalNotesField),
            SignerName = ReadString(item, _options.SignerNameField),
            SignerRole = ReadString(item, _options.SignerRoleField),
            CustomerAccepted = ReadNullableBool(item, _options.CustomerAcceptedField),
            SignaturePointCount = ReadNullableInt(item, _options.SignaturePointCountField),
            DeviceSignedAtUtc = ReadDateTimeOffset(item, _options.DeviceSignedAtUtcField),
            ServerFinalizedAtUtc = ReadDateTimeOffset(item, _options.ServerFinalizedAtUtcField),
            Latitude = ReadNullableDouble(item, _options.LatitudeField),
            Longitude = ReadNullableDouble(item, _options.LongitudeField),
            AccuracyMeters = ReadNullableDouble(item, _options.AccuracyMetersField),
            LocationCapturedAtUtc = ReadDateTimeOffset(item, _options.LocationCapturedAtUtcField),
            LocationSource = ReadString(item, _options.LocationSourceField),
            SignatureSha256 = ReadString(item, _options.SignatureSha256Field),
            SignatureEvidenceKey = ReadString(item, _options.SignatureEvidenceKeyField),
            SignedReportEvidenceKey = ReadString(item, _options.SignedReportEvidenceKeyField),
            SignedReportFileName = ReadString(item, _options.SignedReportFileNameField),
            SignedReportSha256 = ReadString(item, _options.SignedReportSha256Field),
            AttachmentCount = ReadNullableInt(item, _options.AttachmentCountField),
            AttachmentManifestJson = ReadString(item, _options.AttachmentManifestJsonField),
            FinalizationFingerprint = ReadString(item, _options.FinalizationFingerprintField),
            EmailOutboxKey = ReadString(item, _options.EmailOutboxKeyField),
            EmailTo = ReadString(item, _options.EmailToField),
            EmailSubject = ReadString(item, _options.EmailSubjectField),
            EmailHtmlBody = ReadString(item, _options.EmailHtmlBodyField),
            ReadyAtUtc = ReadDateTimeOffset(item, _options.ReadyAtUtcField)
        };
    }

    private static void VerifyFinalizationSnapshot(
        CopiersMaintenanceV2FinalizationSnapshot actual,
        CopiersMaintenanceV2CompleteFinalizationCommand expected,
        string answersJson,
        string manifestJson,
        string signatureEvidenceKey,
        string reportEvidenceKey,
        int expectedWorkflowState,
        IReadOnlySet<int> expectedEmailStates,
        DateTimeOffset? expectedReadyAtUtc)
    {
        var location = expected.InternalLocation;
        var baseSnapshot = expected.BaseSnapshot;
        var failures = new List<string>();
        Check(string.Equals(actual.TechnicianSystemUserId, baseSnapshot.TechnicianSystemUserId, StringComparison.OrdinalIgnoreCase), "technicianSystemUserId", failures);
        Check(string.Equals(actual.TechnicianName, baseSnapshot.TechnicianName, StringComparison.Ordinal), "technicianName", failures);
        Check(string.Equals(actual.TechnicianEmail, baseSnapshot.TechnicianEmail, StringComparison.OrdinalIgnoreCase), "technicianEmail", failures);
        Check(string.Equals(actual.ClientId, baseSnapshot.ClientId, StringComparison.OrdinalIgnoreCase), "clientId", failures);
        Check(string.Equals(actual.ClientName, baseSnapshot.ClientName, StringComparison.Ordinal), "clientName", failures);
        Check(string.Equals(actual.CustomerContactName, baseSnapshot.CustomerContactName, StringComparison.Ordinal), "customerContactName", failures);
        Check(string.Equals(actual.CustomerEmail, baseSnapshot.CustomerEmail, StringComparison.OrdinalIgnoreCase), "customerEmail", failures);
        Check(string.Equals(actual.EquipmentId, baseSnapshot.EquipmentId, StringComparison.OrdinalIgnoreCase), "equipmentId", failures);
        Check(string.Equals(actual.EquipmentSerial, baseSnapshot.EquipmentSerial, StringComparison.Ordinal), "equipmentSerial", failures);
        Check(string.Equals(actual.Title, baseSnapshot.Title, StringComparison.Ordinal), "title", failures);
        Check(actual.ServiceDate == baseSnapshot.ServiceDate, "serviceDate", failures);
        Check(actual.MaintenanceTypeValue == baseSnapshot.MaintenanceTypeValue, "maintenanceType", failures);
        Check(actual.WorkflowState == expectedWorkflowState, "workflowState", failures);
        Check(expectedEmailStates.Contains(actual.EmailState), "emailState", failures);
        Check(string.Equals(actual.FinalizationLeaseId, expected.FinalizationLeaseId, StringComparison.Ordinal), "finalizationLease", failures);
        Check(string.Equals(actual.FormVersion, expected.FormVersion, StringComparison.Ordinal), "formVersion", failures);
        Check(string.Equals(actual.AnswersJson, answersJson, StringComparison.Ordinal), "answersJson", failures);
        Check(string.Equals(actual.WorkPerformed, expected.WorkPerformed, StringComparison.Ordinal), "workPerformed", failures);
        Check(string.Equals(actual.CustomerObservations, expected.CustomerObservations, StringComparison.Ordinal), "customerObservations", failures);
        Check(string.Equals(actual.ServiceAddressInternal, expected.ServiceAddressInternal, StringComparison.Ordinal), "serviceAddressInternal", failures);
        Check(string.Equals(actual.InternalNotes, expected.InternalNotes, StringComparison.Ordinal), "internalNotes", failures);
        Check(string.Equals(actual.SignerName, expected.SignerName, StringComparison.Ordinal), "signerName", failures);
        Check(string.Equals(actual.SignerRole, expected.SignerRole, StringComparison.Ordinal), "signerRole", failures);
        Check(actual.CustomerAccepted == expected.CustomerAccepted, "customerAccepted", failures);
        Check(actual.SignaturePointCount == expected.SignaturePointCount, "signaturePointCount", failures);
        Check(SameInstant(actual.DeviceSignedAtUtc, expected.DeviceSignedAtUtc), "deviceSignedAt", failures);
        Check(SameInstant(actual.ServerFinalizedAtUtc, expected.ServerFinalizedAtUtc), "serverFinalizedAt", failures);
        Check(SameDouble(actual.Latitude, location?.Latitude), "latitude", failures);
        Check(SameDouble(actual.Longitude, location?.Longitude), "longitude", failures);
        Check(SameDouble(actual.AccuracyMeters, location?.AccuracyMeters), "accuracyMeters", failures);
        Check(SameInstant(actual.LocationCapturedAtUtc, location?.CapturedAtUtc), "locationCapturedAt", failures);
        Check(string.Equals(actual.LocationSource, location?.Source ?? "not-captured", StringComparison.Ordinal), "locationSource", failures);
        Check(string.Equals(actual.SignatureSha256, expected.Signature.Sha256, StringComparison.OrdinalIgnoreCase), "signatureSha256", failures);
        Check(string.Equals(actual.SignatureEvidenceKey, signatureEvidenceKey, StringComparison.Ordinal), "signatureEvidenceKey", failures);
        Check(string.Equals(actual.SignedReportEvidenceKey, reportEvidenceKey, StringComparison.Ordinal), "signedReportEvidenceKey", failures);
        Check(string.Equals(actual.SignedReportFileName, expected.SignedReport.FileName, StringComparison.Ordinal), "signedReportFileName", failures);
        Check(string.Equals(actual.SignedReportSha256, expected.SignedReport.Sha256, StringComparison.OrdinalIgnoreCase), "signedReportSha256", failures);
        Check(actual.AttachmentCount == expected.CustomerAttachments.Count, "attachmentCount", failures);
        Check(string.Equals(actual.AttachmentManifestJson, manifestJson, StringComparison.Ordinal), "attachmentManifest", failures);
        Check(string.Equals(actual.FinalizationFingerprint, expected.FinalizationFingerprint, StringComparison.OrdinalIgnoreCase), "finalizationFingerprint", failures);
        Check(string.Equals(actual.EmailOutboxKey, expected.EmailOutbox.OutboxKey, StringComparison.Ordinal), "emailOutboxKey", failures);
        Check(string.Equals(actual.EmailTo, string.Join(";", expected.EmailOutbox.To), StringComparison.OrdinalIgnoreCase), "emailTo", failures);
        Check(string.Equals(actual.EmailSubject, expected.EmailOutbox.Subject, StringComparison.Ordinal), "emailSubject", failures);
        Check(string.Equals(actual.EmailHtmlBody, expected.EmailOutbox.HtmlBody, StringComparison.Ordinal), "emailHtmlBody", failures);
        Check(SameInstant(actual.ReadyAtUtc, expectedReadyAtUtc), "readyAt", failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"El read-back de staging MTO V2 no coincidió en: {string.Join(", ", failures)}.");
        }
    }

    private static void Check(bool condition, string field, ICollection<string> failures)
    {
        if (!condition)
            failures.Add(field);
    }

    private static bool SameInstant(DateTimeOffset? actual, DateTimeOffset? expected) =>
        actual.HasValue == expected.HasValue
        && (!actual.HasValue || Math.Abs((actual.Value - expected!.Value).TotalMilliseconds) <= 10d);

    private static bool SameDouble(double? actual, double? expected) =>
        actual.HasValue == expected.HasValue
        && (!actual.HasValue || actual.Value.Equals(expected!.Value));

    private async Task PatchWithVersionAsync(
        string recordId,
        string version,
        object payload,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        using var response = await SendJsonAsync(
            $"/api/data/v9.2/{_options.MainEntitySetName}({NormalizeGuid(recordId, nameof(recordId))})",
            HttpMethod.Patch,
            payload,
            user,
            ct,
            request => request.Headers.TryAddWithoutValidation("If-Match", version));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            throw new CopiersMaintenanceV2ConcurrencyException("El MTO V2 fue modificado por otro proceso.");
        EnsureSuccess("actualizar el MTO V2", response, body);
    }

    private async Task<string> UpsertEvidenceAsync(
        string parentRecordId,
        string operationKey,
        string slot,
        int purposeValue,
        int sequence,
        CopiersMaintenanceV2StoredFile file,
        string derivedFromEvidenceKey,
        int securityStateValue,
        DateTimeOffset? securityCheckedAtUtc,
        string securityProvider,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var evidenceKey = BuildEvidenceKey(operationKey, slot, sequence, file.Sha256);
        var existing = await FindEvidenceByKeyAsync(evidenceKey, user, ct);

        string evidenceId;
        if (existing is not null)
        {
            evidenceId = existing.Id;
            VerifyEvidenceMetadata(existing, parentRecordId, purposeValue, sequence, file, derivedFromEvidenceKey, securityStateValue, securityCheckedAtUtc, securityProvider);
        }
        else
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [_options.EvidenceNameField] = $"{slot} · {sequence:00} · {Truncate(file.FileName, 110)}",
                [_options.EvidenceKeyField] = evidenceKey,
                [_options.EvidencePurposeField] = purposeValue,
                [_options.EvidenceSequenceField] = sequence,
                [_options.EvidenceOriginalFileNameField] = file.FileName,
                [_options.EvidenceContentTypeField] = file.ContentType,
                [_options.EvidenceSizeField] = file.Size,
                [_options.EvidenceSha256Field] = file.Sha256.ToLowerInvariant(),
                [_options.EvidenceDerivedFromKeyField] = string.IsNullOrWhiteSpace(derivedFromEvidenceKey) ? null : derivedFromEvidenceKey,
                [_options.EvidenceSecurityStateField] = securityStateValue,
                [_options.EvidenceSecurityCheckedAtUtcField] = securityCheckedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                [_options.EvidenceSecurityProviderField] = securityProvider,
                [$"{_options.EvidenceParentNavigationProperty}@odata.bind"] =
                    $"/{_options.MainEntitySetName}({NormalizeGuid(parentRecordId, nameof(parentRecordId))})"
            };
            using var createResponse = await SendJsonAsync(
                $"/api/data/v9.2/{_options.EvidenceEntitySetName}",
                HttpMethod.Post,
                payload,
                user,
                ct,
                request => request.Headers.TryAddWithoutValidation("Prefer", "return=representation"));
            var createBody = await createResponse.Content.ReadAsStringAsync(ct);
            if (!createResponse.IsSuccessStatusCode)
            {
                if (createResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                {
                    // Reconcile the single racing create by key; never retry the POST blindly.
                    var raced = await FindEvidenceByKeyAsync(evidenceKey, user, ct);
                    if (raced is null)
                        throw BuildDataverseException("conciliar una evidencia MTO V2", createResponse, createBody);
                    VerifyEvidenceMetadata(raced, parentRecordId, purposeValue, sequence, file, derivedFromEvidenceKey, securityStateValue, securityCheckedAtUtc, securityProvider);
                    evidenceId = raced.Id;
                }
                else
                {
                    throw BuildDataverseException("crear una evidencia MTO V2", createResponse, createBody);
                }
            }
            else
            {
                evidenceId = ExtractRecordId(createResponse, createBody, _options.EvidenceIdField);
                if (string.IsNullOrWhiteSpace(evidenceId))
                    throw new InvalidOperationException("Dataverse creó la evidencia, pero no devolvió su identificador.");
            }
        }

        var metadataReadBack = await FindEvidenceByKeyAsync(evidenceKey, user, ct)
            ?? throw new InvalidOperationException("No fue posible releer la metadata de evidencia recién creada.");
        VerifyEvidenceMetadata(metadataReadBack, parentRecordId, purposeValue, sequence, file, derivedFromEvidenceKey, securityStateValue, securityCheckedAtUtc, securityProvider);
        var hasExpectedFile = await VerifyFileAsync(
            _options.EvidenceEntitySetName,
            evidenceId,
            _options.EvidenceFileField,
            file,
            user,
            ct,
            allowMissing: true);
        if (!hasExpectedFile)
        {
            await UploadAndVerifyFileAsync(
                _options.EvidenceEntitySetName,
                evidenceId,
                _options.EvidenceFileField,
                file,
                user,
                ct);
        }
        return evidenceKey;
    }

    private async Task<CopiersMaintenanceV2EvidenceMetadata?> FindEvidenceByKeyAsync(
        string evidenceKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{_options.EvidenceKeyField} eq '{evidenceKey}'";
        var fields = string.Join(",", new[]
        {
            _options.EvidenceIdField,
            _options.EvidenceSha256Field,
            $"_{_options.EvidenceParentLookupLogicalName}_value",
            _options.EvidencePurposeField,
            _options.EvidenceSequenceField,
            _options.EvidenceOriginalFileNameField,
            _options.EvidenceContentTypeField,
            _options.EvidenceSizeField,
            _options.EvidenceDerivedFromKeyField,
            _options.EvidenceSecurityStateField,
            _options.EvidenceSecurityCheckedAtUtcField,
            _options.EvidenceSecurityProviderField
        });
        var relativeUrl =
            $"/api/data/v9.2/{_options.EvidenceEntitySetName}?$select={fields}" +
            $"&$filter={Uri.EscapeDataString(filter)}&$top=2";
        using var response = await SendAsync(relativeUrl, HttpMethod.Get, user, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess("consultar la evidencia MTO V2", response, body);
        using var document = JsonDocument.Parse(body);
        var rows = document.RootElement.GetProperty("value");
        if (rows.GetArrayLength() > 1)
            throw new InvalidOperationException("La clave de evidencia MTO V2 está duplicada.");
        if (rows.GetArrayLength() == 0)
            return null;
        return new CopiersMaintenanceV2EvidenceMetadata
        {
            Id = ReadString(rows[0], _options.EvidenceIdField),
            ParentRecordId = ReadString(rows[0], $"_{_options.EvidenceParentLookupLogicalName}_value"),
            PurposeValue = ReadNullableInt(rows[0], _options.EvidencePurposeField),
            Sequence = ReadNullableInt(rows[0], _options.EvidenceSequenceField),
            OriginalFileName = ReadString(rows[0], _options.EvidenceOriginalFileNameField),
            ContentType = ReadString(rows[0], _options.EvidenceContentTypeField),
            Size = ReadNullableLong(rows[0], _options.EvidenceSizeField),
            Sha256 = ReadString(rows[0], _options.EvidenceSha256Field),
            DerivedFromEvidenceKey = ReadString(rows[0], _options.EvidenceDerivedFromKeyField),
            SecurityStateValue = ReadNullableInt(rows[0], _options.EvidenceSecurityStateField),
            SecurityCheckedAtUtc = ReadDateTimeOffset(rows[0], _options.EvidenceSecurityCheckedAtUtcField),
            SecurityProvider = ReadString(rows[0], _options.EvidenceSecurityProviderField)
        };
    }

    private static void VerifyEvidenceMetadata(
        CopiersMaintenanceV2EvidenceMetadata actual,
        string parentRecordId,
        int purposeValue,
        int sequence,
        CopiersMaintenanceV2StoredFile file,
        string derivedFromEvidenceKey,
        int securityStateValue,
        DateTimeOffset? securityCheckedAtUtc,
        string securityProvider)
    {
        if (!string.Equals(NormalizeGuid(actual.ParentRecordId, nameof(actual.ParentRecordId)), NormalizeGuid(parentRecordId, nameof(parentRecordId)), StringComparison.OrdinalIgnoreCase)
            || actual.PurposeValue != purposeValue
            || actual.Sequence != sequence
            || !string.Equals(actual.OriginalFileName, file.FileName, StringComparison.Ordinal)
            || !string.Equals(actual.ContentType, file.ContentType, StringComparison.OrdinalIgnoreCase)
            || actual.Size != file.Size
            || !string.Equals(actual.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actual.DerivedFromEvidenceKey, derivedFromEvidenceKey, StringComparison.Ordinal)
            || actual.SecurityStateValue != securityStateValue
            || actual.SecurityCheckedAtUtc.HasValue != securityCheckedAtUtc.HasValue
            || !string.Equals(actual.SecurityProvider, securityProvider, StringComparison.Ordinal))
        {
            throw new CopiersMaintenanceV2ConcurrencyException("La evidencia idempotente existe con metadata diferente.");
        }
    }

    private async Task UploadAndVerifyFileAsync(
        string entitySetName,
        string recordId,
        string fileField,
        CopiersMaintenanceV2StoredFile file,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        using var content = new ByteArrayContent(file.Content);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        using var response = await SendAsync(
            $"/api/data/v9.2/{entitySetName}({NormalizeGuid(recordId, nameof(recordId))})/{fileField}",
            HttpMethod.Patch,
            user,
            ct,
            content,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", ToSafeHeaderFileName(file.FileName));
            });
        var body = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess($"cargar {file.FileName}", response, body);

        _ = await VerifyFileAsync(entitySetName, recordId, fileField, file, user, ct, allowMissing: false);
    }

    private async Task<bool> VerifyFileAsync(
        string entitySetName,
        string recordId,
        string fileField,
        CopiersMaintenanceV2StoredFile file,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool allowMissing)
    {
        using var readBack = await SendAsync(
            $"/api/data/v9.2/{entitySetName}({NormalizeGuid(recordId, nameof(recordId))})/{fileField}/$value",
            HttpMethod.Get,
            user,
            ct);
        var bytes = await readBack.Content.ReadAsByteArrayAsync(ct);
        if (allowMissing && (readBack.StatusCode == HttpStatusCode.NotFound || readBack.StatusCode == HttpStatusCode.NoContent || bytes.Length == 0))
            return false;
        if (!readBack.IsSuccessStatusCode)
        {
            var error = bytes.Length == 0 ? "" : Encoding.UTF8.GetString(bytes);
            throw BuildDataverseException($"verificar {file.FileName}", readBack, error);
        }
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.LongLength != file.Size || !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El read-back de {file.FileName} no coincide con el archivo recibido.");
        return true;
    }

    private string BuildMainSelect() => string.Join(",", new[]
    {
        _options.MainIdField,
        _options.OperationKeyField,
        _options.WorkflowStateField,
        _options.EmailStateField,
        _options.TechnicianUserIdField,
        _options.TechnicianNameField,
        _options.TechnicianEmailField,
        _options.ClientNameField,
        _options.ClientContactNameField,
        _options.ClientEmailField,
        _options.EquipmentSerialField,
        _options.TitleField,
        _options.ServiceDateField,
        _options.MaintenanceTypeField,
        _options.SignatureEvidenceKeyField,
        _options.SignedReportEvidenceKeyField,
        _options.SignedReportFileNameField,
        _options.SignedReportSha256Field,
        _options.AttachmentCountField,
        _options.FinalizationFingerprintField,
        _options.ServerFinalizedAtUtcField,
        $"_{_options.ClientLookupLogicalName}_value",
        $"_{_options.EquipmentLookupLogicalName}_value",
        "modifiedon"
    }.Distinct(StringComparer.OrdinalIgnoreCase));

    private CopiersMaintenanceV2DraftRecord ParseRecord(JsonElement item)
    {
        var stateValue = ReadInt(item, _options.WorkflowStateField);
        var emailValue = ReadInt(item, _options.EmailStateField);
        return new CopiersMaintenanceV2DraftRecord
        {
            RecordId = ReadString(item, _options.MainIdField),
            SubmissionKey = ReadString(item, _options.OperationKeyField),
            Version = ReadString(item, "@odata.etag"),
            State = MapWorkflowState(stateValue),
            EmailState = MapEmailState(emailValue),
            TechnicianSystemUserId = ReadString(item, _options.TechnicianUserIdField),
            TechnicianName = ReadString(item, _options.TechnicianNameField),
            TechnicianEmail = ReadString(item, _options.TechnicianEmailField),
            ClientId = ReadString(item, $"_{_options.ClientLookupLogicalName}_value"),
            ClientName = ReadString(item, _options.ClientNameField),
            CustomerContactName = ReadString(item, _options.ClientContactNameField),
            CustomerEmail = ReadString(item, _options.ClientEmailField),
            EquipmentId = ReadString(item, $"_{_options.EquipmentLookupLogicalName}_value"),
            EquipmentSerial = ReadString(item, _options.EquipmentSerialField),
            Title = ReadString(item, _options.TitleField),
            ServiceDate = ReadDateOnly(item, _options.ServiceDateField),
            MaintenanceTypeValue = ReadNullableInt(item, _options.MaintenanceTypeField),
            SignatureEvidenceKey = ReadString(item, _options.SignatureEvidenceKeyField),
            ReportEvidenceKey = ReadString(item, _options.SignedReportEvidenceKeyField),
            ReportFileName = ReadString(item, _options.SignedReportFileNameField),
            ReportSha256 = ReadString(item, _options.SignedReportSha256Field),
            AttachmentCount = ReadInt(item, _options.AttachmentCountField),
            FinalizationFingerprint = ReadString(item, _options.FinalizationFingerprintField),
            UpdatedAtUtc = ReadDateTimeOffset(item, "modifiedon") ?? DateTimeOffset.MinValue,
            ServerFinalizedAtUtc = ReadDateTimeOffset(item, _options.ServerFinalizedAtUtcField)
        };
    }

    private CopiersMaintenanceV2WorkflowState MapWorkflowState(int value)
    {
        if (value == _options.DraftStateValue) return CopiersMaintenanceV2WorkflowState.Draft;
        if (value == _options.FinalizingStateValue) return CopiersMaintenanceV2WorkflowState.Finalizing;
        if (value == _options.ReadyToSendStateValue) return CopiersMaintenanceV2WorkflowState.ReadyToSend;
        if (value == _options.FailedStateValue) return CopiersMaintenanceV2WorkflowState.Failed;
        throw new InvalidOperationException($"Dataverse devolvió un estado MTO V2 no configurado: {value}.");
    }

    private CopiersMaintenanceV2EmailState MapEmailState(int value)
    {
        if (value == _options.EmailNotReadyStateValue) return CopiersMaintenanceV2EmailState.NotReady;
        if (value == _options.EmailPendingStateValue) return CopiersMaintenanceV2EmailState.Pending;
        if (value == _options.EmailProcessingStateValue) return CopiersMaintenanceV2EmailState.Processing;
        if (value == _options.EmailSentStateValue) return CopiersMaintenanceV2EmailState.Sent;
        if (value == _options.EmailFailedStateValue) return CopiersMaintenanceV2EmailState.Failed;
        throw new InvalidOperationException($"Dataverse devolvió un estado de correo MTO V2 no configurado: {value}.");
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        string relativeUrl,
        HttpMethod method,
        object payload,
        ClaimsPrincipal user,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return await SendAsync(relativeUrl, method, user, ct, content, customizeRequest);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string relativeUrl,
        HttpMethod method,
        ClaimsPrincipal user,
        CancellationToken ct,
        HttpContent? content = null,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        _ = user; // Authentication/ownership is enforced before reaching the app-only transport.
        return await _dataverseClient.SendAsync(relativeUrl, method, content, customizeRequest, ct);
    }

    private void EnsureConfigured()
    {
        var missing = _options.FindMissingBindings();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "El esquema aislado de MTO Firmado V2 aún no está aprovisionado/configurado. " +
                $"Bindings pendientes: {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? "…" : "")}.");
        }
    }

    private ClaimsPrincipal RequireUser() =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext.User
            : throw new InvalidOperationException("No hay un usuario autenticado para guardar el MTO V2.");

    private static CopiersMaintenanceV2BeginFinalizationResult Begin(
        CopiersMaintenanceV2BeginDisposition disposition,
        CopiersMaintenanceV2DraftRecord record,
        string leaseId,
        string message) => new()
        {
            Disposition = disposition,
            Record = record,
            FinalizationLeaseId = leaseId,
            Message = message
        };

    private static string BuildRecordName(CopiersMaintenanceV2CreateDraftCommand command) =>
        Truncate($"MTO {command.ServiceDate:yyyy-MM-dd} · {command.ClientName} · {command.EquipmentSerial}", 160);

    private static void EnsureOperationKey(CopiersMaintenanceV2DraftRecord record, string operationKey)
    {
        if (!string.Equals(record.SubmissionKey, operationKey, StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ConcurrencyException("La clave idempotente no corresponde al MTO V2.");
    }

    private static void EnsureVersion(CopiersMaintenanceV2DraftRecord record, string expectedVersion)
    {
        if (!string.Equals(record.Version, expectedVersion, StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ConcurrencyException("El MTO V2 cambió desde la última lectura.");
    }

    private static void EnsureSuccess(string action, HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
            throw BuildDataverseException(action, response, body);
    }

    private static CopiersMaintenanceV2PersistenceException BuildDataverseException(
        string action,
        HttpResponseMessage response,
        string body) =>
        new($"Dataverse no pudo {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 1200)}");

    private static string ExtractRecordId(HttpResponseMessage response, string body, string idField)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var inline = ReadString(document.RootElement, idField);
                if (!string.IsNullOrWhiteSpace(inline))
                    return inline;
            }
            catch (JsonException)
            {
                // Fall through to OData-EntityId.
            }
        }
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
        {
            var value = values.FirstOrDefault() ?? "";
            var start = value.LastIndexOf('(');
            var end = value.LastIndexOf(')');
            if (start >= 0 && end > start)
                return value[(start + 1)..end];
        }
        return "";
    }

    private static string ReadString(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static int ReadInt(JsonElement item, string field) => ReadNullableInt(item, field) ?? 0;

    private static int? ReadNullableInt(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
            return numeric;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric) ? numeric : null;
    }

    private static long? ReadNullableLong(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
            return numeric;
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric) ? numeric : null;
    }

    private static bool? ReadNullableBool(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static double? ReadNullableDouble(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
            return numeric;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numeric) ? numeric : null;
    }

    private static DateOnly ReadDateOnly(JsonElement item, string field)
    {
        var raw = ReadString(item, field);
        return raw.Length >= 10 && DateOnly.TryParseExact(raw[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : default;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement item, string field)
    {
        var raw = ReadString(item, field);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string NormalizeGuid(string value, string parameterName) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : throw new ArgumentException("El identificador no es un GUID válido.", parameterName);

    private static string EscapeODataLiteral(string value) => (value ?? "").Replace("'", "''", StringComparison.Ordinal);

    private static string BuildEvidenceKey(string operationKey, string slot, int sequence, string sha256) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{operationKey}|{slot}|{sequence}|{sha256.Trim().ToLowerInvariant()}"))).ToLowerInvariant();

    private static string ToSafeHeaderFileName(string fileName)
    {
        var normalized = (Path.GetFileName(fileName) ?? "archivo.bin").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(character is >= ' ' and <= '~' && character is not '"' and not '\\' ? character : '-');
        }
        var safe = builder.ToString().Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "archivo.bin" : Truncate(safe, 180);
    }

    private static string Truncate(string? value, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed class CopiersMaintenanceV2FinalizationSnapshot
    {
        public string Version { get; init; } = "";
        public string TechnicianSystemUserId { get; init; } = "";
        public string TechnicianName { get; init; } = "";
        public string TechnicianEmail { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string CustomerContactName { get; init; } = "";
        public string CustomerEmail { get; init; } = "";
        public string EquipmentId { get; init; } = "";
        public string EquipmentSerial { get; init; } = "";
        public string Title { get; init; } = "";
        public DateOnly ServiceDate { get; init; }
        public int? MaintenanceTypeValue { get; init; }
        public int WorkflowState { get; init; }
        public int EmailState { get; init; }
        public string FinalizationLeaseId { get; init; } = "";
        public string FormVersion { get; init; } = "";
        public string AnswersJson { get; init; } = "";
        public string WorkPerformed { get; init; } = "";
        public string CustomerObservations { get; init; } = "";
        public string ServiceAddressInternal { get; init; } = "";
        public string InternalNotes { get; init; } = "";
        public string SignerName { get; init; } = "";
        public string SignerRole { get; init; } = "";
        public bool? CustomerAccepted { get; init; }
        public int? SignaturePointCount { get; init; }
        public DateTimeOffset? DeviceSignedAtUtc { get; init; }
        public DateTimeOffset? ServerFinalizedAtUtc { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public double? AccuracyMeters { get; init; }
        public DateTimeOffset? LocationCapturedAtUtc { get; init; }
        public string LocationSource { get; init; } = "";
        public string SignatureSha256 { get; init; } = "";
        public string SignatureEvidenceKey { get; init; } = "";
        public string SignedReportEvidenceKey { get; init; } = "";
        public string SignedReportFileName { get; init; } = "";
        public string SignedReportSha256 { get; init; } = "";
        public int? AttachmentCount { get; init; }
        public string AttachmentManifestJson { get; init; } = "";
        public string FinalizationFingerprint { get; init; } = "";
        public string EmailOutboxKey { get; init; } = "";
        public string EmailTo { get; init; } = "";
        public string EmailSubject { get; init; } = "";
        public string EmailHtmlBody { get; init; } = "";
        public DateTimeOffset? ReadyAtUtc { get; init; }
    }

    private sealed class CopiersMaintenanceV2EvidenceMetadata
    {
        public string Id { get; init; } = "";
        public string ParentRecordId { get; init; } = "";
        public int? PurposeValue { get; init; }
        public int? Sequence { get; init; }
        public string OriginalFileName { get; init; } = "";
        public string ContentType { get; init; } = "";
        public long? Size { get; init; }
        public string Sha256 { get; init; } = "";
        public string DerivedFromEvidenceKey { get; init; } = "";
        public int? SecurityStateValue { get; init; }
        public DateTimeOffset? SecurityCheckedAtUtc { get; init; }
        public string SecurityProvider { get; init; } = "";
    }
}

