using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed class CopiersMaintenanceV2Service : ICopiersMaintenanceV2Service
{
    private readonly ICopiersMaintenanceV2DataverseRepository _repository;
    private readonly ICopiersMtoV2PdfBuilder _pdfBuilder;
    private readonly CopiersMaintenanceV2Options _options;
    private readonly CopiersMaintenanceV2DataverseOptions _dataverseOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CopiersMaintenanceV2Service> _logger;

    public CopiersMaintenanceV2Service(
        ICopiersMaintenanceV2DataverseRepository repository,
        ICopiersMtoV2PdfBuilder pdfBuilder,
        IOptions<CopiersMaintenanceV2Options> options,
        IOptions<CopiersMaintenanceV2DataverseOptions> dataverseOptions,
        TimeProvider timeProvider,
        ILogger<CopiersMaintenanceV2Service> logger)
    {
        _repository = repository;
        _pdfBuilder = pdfBuilder;
        _options = options.Value;
        _dataverseOptions = dataverseOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CopiersMaintenanceV2DraftResultDto> CreateOrGetDraftAsync(
        CopiersMaintenanceV2DraftRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedActor = CopiersMaintenanceV2Validation.Actor(actor);
        var command = BuildCreateCommand(request, normalizedActor, _timeProvider.GetUtcNow());
        var record = await _repository.CreateOrGetDraftAsync(command, ct);
        EnsureRecordIdentity(record, command.SubmissionKey, normalizedActor.SystemUserId);
        if (record.WasCreated
            || record.State is not CopiersMaintenanceV2WorkflowState.Draft
                and not CopiersMaintenanceV2WorkflowState.Failed)
        {
            EnsureImmutableBaseSnapshotMatches(record, command);
        }
        else
        {
            EnsureReplayScopeMatches(record, command.ClientId, command.EquipmentId, command.EquipmentSerial);
        }
        return ToDraftResult(record, reusedExisting: !record.WasCreated);
    }

    public async Task<CopiersMaintenanceV2DraftResultDto> SaveDraftAsync(
        CopiersMaintenanceV2DraftUpdateRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedActor = CopiersMaintenanceV2Validation.Actor(actor);
        var nowUtc = _timeProvider.GetUtcNow();
        var create = BuildCreateCommand(new CopiersMaintenanceV2DraftRequestDto
        {
            SubmissionKey = request.SubmissionKey,
            ClientId = request.ClientId,
            ClientName = request.ClientName,
            CustomerContactName = request.CustomerContactName,
            CustomerEmail = request.CustomerEmail,
            EquipmentId = request.EquipmentId,
            EquipmentSerial = request.EquipmentSerial,
            Title = request.Title,
            ServiceDate = request.ServiceDate,
            MaintenanceTypeValue = request.MaintenanceTypeValue
        }, normalizedActor, nowUtc);

        var command = new CopiersMaintenanceV2SaveDraftCommand
        {
            RecordId = CopiersMaintenanceV2Validation.RequiredGuid(request.RecordId, "record_id_invalid", "El mantenimiento"),
            ExpectedVersion = CopiersMaintenanceV2Validation.Required(request.ExpectedVersion, "version_required", "la version del borrador", 200),
            SubmissionKey = create.SubmissionKey,
            TechnicianSystemUserId = create.TechnicianSystemUserId,
            TechnicianName = create.TechnicianName,
            TechnicianEmail = create.TechnicianEmail,
            ClientId = create.ClientId,
            ClientName = create.ClientName,
            CustomerContactName = create.CustomerContactName,
            CustomerEmail = create.CustomerEmail,
            EquipmentId = create.EquipmentId,
            EquipmentSerial = create.EquipmentSerial,
            Title = create.Title,
            ServiceDate = create.ServiceDate,
            MaintenanceTypeValue = create.MaintenanceTypeValue,
            RequestedAtUtc = nowUtc
        };

        var record = await _repository.SaveDraftAsync(command, ct);
        EnsureRecordIdentity(record, command.SubmissionKey, normalizedActor.SystemUserId);
        return ToDraftResult(record, reusedExisting: false);
    }

    public async Task<CopiersMaintenanceV2FinalizeResultDto> FinalizeMultipartAsync(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedActor = CopiersMaintenanceV2Validation.Actor(actor);
        var nowUtc = _timeProvider.GetUtcNow();
        var recordId = CopiersMaintenanceV2Validation.RequiredGuid(request.RecordId, "record_id_invalid", "El mantenimiento");
        var submissionKey = CopiersMaintenanceV2Validation.SubmissionKey(request.SubmissionKey, _options);
        var expectedVersion = CopiersMaintenanceV2Validation.Required(request.ExpectedVersion, "version_required", "la version del borrador", 200);

        var leaseId = Guid.NewGuid().ToString("N");
        var begin = await _repository.TryBeginFinalizationAsync(new CopiersMaintenanceV2BeginFinalizationCommand
        {
            RecordId = recordId,
            SubmissionKey = submissionKey,
            ExpectedVersion = expectedVersion,
            TechnicianSystemUserId = normalizedActor.SystemUserId,
            FinalizationLeaseId = leaseId,
            StartedAtUtc = nowUtc
        }, ct);

        EnsureRecordIdentity(begin.Record, submissionKey, normalizedActor.SystemUserId);
        if (begin.Disposition == CopiersMaintenanceV2BeginDisposition.InProgress)
            throw new CopiersMaintenanceV2ConcurrencyException("La finalización ya está en proceso. Espera y consulta el resultado antes de reintentar.");
        if (begin.Disposition == CopiersMaintenanceV2BeginDisposition.Conflict)
            throw new CopiersMaintenanceV2ConcurrencyException(FirstNonEmpty(begin.Message, "El borrador cambio; vuelve a cargarlo antes de finalizar."));
        var isReadyReplay = begin.Disposition == CopiersMaintenanceV2BeginDisposition.AlreadyReady;
        if (!isReadyReplay && begin.Disposition != CopiersMaintenanceV2BeginDisposition.Acquired)
            throw new InvalidOperationException("Dataverse devolvio un resultado de finalizacion no reconocido.");

        var acquiredLeaseId = FirstNonEmpty(begin.FinalizationLeaseId, leaseId);
        try
        {
            CopiersMaintenanceV2Validation.ValidateCustomerEmail(begin.Record.CustomerEmail);
            var formVersion = CopiersMaintenanceV2Validation.FormVersion(request.FormVersion, _options);
            var answers = CanonicalizeRecordAnswers(
                CopiersMaintenanceV2Validation.ParseAnswers(request.AnswersJson, _options),
                begin.Record);
            var workPerformed = CopiersMaintenanceV2Validation.Required(
                request.WorkPerformed,
                "work_performed_required",
                "el trabajo realizado",
                _options.WorkPerformedMaxLength);
            var customerObservations = CopiersMaintenanceV2Validation.Optional(
                request.CustomerObservations,
                "customer_observations_too_long",
                "Las observaciones del cliente",
                _options.CustomerObservationsMaxLength);
            var serviceAddressInternal = CopiersMaintenanceV2Validation.Optional(
                request.ServiceAddress,
                "service_address_too_long",
                "La dirección o sede",
                _options.ServiceAddressMaxLength);
            var internalNotes = CopiersMaintenanceV2Validation.Optional(
                request.InternalNotes,
                "internal_notes_too_long",
                "Las observaciones internas",
                _options.InternalNotesMaxLength);
            var signerName = CopiersMaintenanceV2Validation.Required(
                request.SignerName,
                "signer_name_required",
                "el nombre de quien firma",
                _options.SignerNameMaxLength);
            var signerRole = CopiersMaintenanceV2Validation.Required(
                request.SignerRole,
                "signer_role_required",
                "el cargo de quien firma",
                _options.SignerRoleMaxLength);
            if (!request.CustomerAccepted)
                throw new CopiersMaintenanceV2ValidationException("customer_acceptance_required", "El cliente debe aceptar el reporte antes de firmar.");

            var signedAtUtc = CopiersMaintenanceV2Validation.DeviceSignedAt(request.DeviceSignedAtUtc, nowUtc, _options);
            var internalLocation = CopiersMaintenanceV2Validation.Location(request, nowUtc, _options);
            if (request.SignaturePointCount < _options.MinSignaturePointCount)
                throw new CopiersMaintenanceV2ValidationException("signature_ink_required", "La firma no contiene suficientes trazos; solicita al cliente firmar nuevamente.");
            var signature = await CopiersMaintenanceV2Validation.ReadSignatureAsync(request.Signature, _options, ct);
            var originalAttachments = await CopiersMaintenanceV2Validation.ReadAttachmentsAsync(request.Attachments, _options, ct);
            var customerAttachments = CopiersMaintenanceV2Validation.BuildCustomerSafeAttachments(originalAttachments, _options);
            var finalizationFingerprint = BuildFinalizationFingerprint(
                begin.Record,
                formVersion,
                answers,
                workPerformed,
                customerObservations,
                serviceAddressInternal,
                internalNotes,
                signerName,
                signerRole,
                request.CustomerAccepted,
                request.SignaturePointCount,
                signedAtUtc,
                internalLocation,
                signature,
                originalAttachments);

            if (isReadyReplay)
            {
                if (string.IsNullOrWhiteSpace(begin.Record.FinalizationFingerprint)
                    || !string.Equals(begin.Record.FinalizationFingerprint, finalizationFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CopiersMaintenanceV2ConcurrencyException(
                        "La clave idempotente ya finalizó un reporte con contenido diferente.");
                }
                return ToFinalizeResult(begin.Record, idempotentReplay: true, "El reporte ya estaba finalizado; no se creó otra fila ni otro envío.");
            }

            var finalizedAtUtc = _timeProvider.GetUtcNow();
            var pdfModel = new CopiersMaintenanceV2PdfModel
            {
                RecordId = begin.Record.RecordId,
                ClientName = begin.Record.ClientName,
                CustomerContactName = begin.Record.CustomerContactName,
                EquipmentSerial = begin.Record.EquipmentSerial,
                Title = begin.Record.Title,
                ServiceDate = begin.Record.ServiceDate,
                TechnicianName = begin.Record.TechnicianName,
                FormVersion = formVersion,
                Answers = answers,
                WorkPerformed = workPerformed,
                CustomerObservations = customerObservations,
                SignerName = signerName,
                SignerRole = signerRole,
                DeviceSignedAtUtc = signedAtUtc,
                ServerFinalizedAtUtc = finalizedAtUtc,
                SignatureContent = signature.Content,
                SignatureContentType = signature.ContentType,
                Attachments = customerAttachments.Select(item => new CopiersMaintenanceV2PdfAttachmentManifestItem
                {
                    FileName = item.FileName,
                    Size = item.Size,
                    Sha256 = item.Sha256
                }).ToList()
            };

            // The builder only receives CopiersMaintenanceV2PdfModel, which has no location fields.
            var rendered = await _pdfBuilder.BuildAsync(pdfModel, ct);
            var signedReport = CopiersMaintenanceV2Validation.ValidatePdf(rendered, _options);
            var emailOutbox = BuildEmailOutbox(begin.Record, signedReport, signerName, finalizedAtUtc);
            CopiersMaintenanceV2Validation.ValidateEmailPackageSize(
                signedReport,
                customerAttachments,
                emailOutbox,
                _options);
            var completed = await _repository.CompleteFinalizationAsync(new CopiersMaintenanceV2CompleteFinalizationCommand
            {
                RecordId = begin.Record.RecordId,
                SubmissionKey = submissionKey,
                FinalizationLeaseId = acquiredLeaseId,
                TechnicianSystemUserId = normalizedActor.SystemUserId,
                BaseSnapshot = CopiersMaintenanceV2BaseSnapshot.From(begin.Record),
                FinalizationFingerprint = finalizationFingerprint,
                FormVersion = formVersion,
                Answers = answers,
                WorkPerformed = workPerformed,
                CustomerObservations = customerObservations,
                ServiceAddressInternal = serviceAddressInternal,
                InternalNotes = internalNotes,
                SignerName = signerName,
                SignerRole = signerRole,
                CustomerAccepted = request.CustomerAccepted,
                SignaturePointCount = request.SignaturePointCount,
                DeviceSignedAtUtc = signedAtUtc,
                ServerFinalizedAtUtc = finalizedAtUtc,
                InternalLocation = internalLocation,
                Signature = signature,
                OriginalAttachments = originalAttachments,
                CustomerAttachments = customerAttachments,
                SignedReport = signedReport,
                EmailOutbox = emailOutbox
            }, ct);

            EnsureRecordIdentity(completed, submissionKey, normalizedActor.SystemUserId);
            if (completed.State != CopiersMaintenanceV2WorkflowState.ReadyToSend)
                throw new InvalidOperationException("Dataverse no confirmo el estado ReadyToSend despues de guardar los artefactos.");
            var completionMessage = completed.EmailState switch
            {
                CopiersMaintenanceV2EmailState.Sent => "Reporte firmado, guardado y enviado al cliente.",
                CopiersMaintenanceV2EmailState.Processing => "Reporte firmado y guardado; el correo está en proceso.",
                CopiersMaintenanceV2EmailState.Failed => "Reporte firmado y guardado; el correo requiere revisión interna.",
                _ => "Reporte firmado y listo para envío."
            };
            return ToFinalizeResult(completed, idempotentReplay: false, completionMessage);
        }
        catch (Exception ex)
        {
            if (!isReadyReplay)
            {
                await TryMarkFinalizationFailedAsync(
                    begin.Record.RecordId,
                    submissionKey,
                    acquiredLeaseId,
                    normalizedActor.SystemUserId,
                    ex);
            }
            throw;
        }
    }

    private CopiersMaintenanceV2CreateDraftCommand BuildCreateCommand(
        CopiersMaintenanceV2DraftRequestDto request,
        CopiersMaintenanceV2ActorContext actor,
        DateTimeOffset nowUtc)
    {
        CopiersMaintenanceV2Validation.ValidateServiceDate(request.ServiceDate);
        CopiersMaintenanceV2Validation.ValidateCustomerEmail(request.CustomerEmail);
        var equipmentId = CopiersMaintenanceV2Validation.OptionalGuid(request.EquipmentId, "equipment_id_invalid", "El equipo");
        var equipmentSerial = CopiersMaintenanceV2Validation.Optional(request.EquipmentSerial, "equipment_serial_too_long", "El serial del equipo", 200);
        if (string.IsNullOrWhiteSpace(equipmentId) && string.IsNullOrWhiteSpace(equipmentSerial))
            throw new CopiersMaintenanceV2ValidationException("equipment_required", "Debes indicar el equipo o el serial externo.");

        return new CopiersMaintenanceV2CreateDraftCommand
        {
            SubmissionKey = CopiersMaintenanceV2Validation.SubmissionKey(request.SubmissionKey, _options),
            TechnicianSystemUserId = actor.SystemUserId,
            TechnicianName = actor.DisplayName,
            TechnicianEmail = actor.Email,
            ClientId = CopiersMaintenanceV2Validation.RequiredGuid(request.ClientId, "client_id_invalid", "El cliente"),
            ClientName = CopiersMaintenanceV2Validation.Required(request.ClientName, "client_name_required", "el nombre del cliente", 250),
            CustomerContactName = CopiersMaintenanceV2Validation.Optional(request.CustomerContactName, "customer_contact_too_long", "El contacto del cliente", 200),
            CustomerEmail = CopiersMaintenanceV2Validation.Required(request.CustomerEmail, "customer_email_required", "el correo del cliente", 320),
            EquipmentId = equipmentId,
            EquipmentSerial = equipmentSerial,
            Title = CopiersMaintenanceV2Validation.Required(request.Title, "title_required", "el titulo", _options.TitleMaxLength),
            ServiceDate = request.ServiceDate,
            MaintenanceTypeValue = CopiersMaintenanceV2Validation.MaintenanceType(request.MaintenanceTypeValue, _dataverseOptions),
            RequestedAtUtc = nowUtc
        };
    }

    private CopiersMaintenanceV2EmailOutboxSnapshot BuildEmailOutbox(
        CopiersMaintenanceV2DraftRecord record,
        CopiersMaintenanceV2StoredFile signedReport,
        string signerName,
        DateTimeOffset nowUtc)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cliente"] = record.ClientName,
            ["Contacto"] = FirstNonEmpty(signerName, record.CustomerContactName, record.ClientName, "cliente"),
            ["Fecha"] = record.ServiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["Reporte"] = signedReport.FileName
        };

        return new CopiersMaintenanceV2EmailOutboxSnapshot
        {
            OutboxKey = $"copiers-mto-v2:{record.RecordId}:customer-report:v1",
            To = new[] { record.CustomerEmail.Trim() },
            Subject = ApplyTemplate(_options.EmailSubjectTemplate, tokens, htmlEncodeValues: false),
            HtmlBody = ApplyTemplate(_options.EmailBodyTemplate, tokens, htmlEncodeValues: true),
            CreatedAtUtc = nowUtc
        };
    }

    private IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> CanonicalizeRecordAnswers(
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers,
        CopiersMaintenanceV2DraftRecord record) =>
        answers.Select(answer => new CopiersMaintenanceV2FormAnswerSnapshot
        {
            Key = answer.Key,
            Label = answer.Label,
            SortOrder = answer.SortOrder,
            Value = answer.Key switch
            {
                "onsite_contact" => record.CustomerContactName,
                "onsite_email" => record.CustomerEmail,
                "maintenance_type" => CopiersMaintenanceV2Validation.MaintenanceTypeLabel(record.MaintenanceTypeValue, _dataverseOptions),
                _ => answer.Value
            }
        }).ToList();

    private static string BuildFinalizationFingerprint(
        CopiersMaintenanceV2DraftRecord record,
        string formVersion,
        IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> answers,
        string workPerformed,
        string customerObservations,
        string serviceAddressInternal,
        string internalNotes,
        string signerName,
        string signerRole,
        bool customerAccepted,
        int signaturePointCount,
        DateTimeOffset deviceSignedAtUtc,
        CopiersMaintenanceV2InternalLocationData? internalLocation,
        CopiersMaintenanceV2StoredFile signature,
        IReadOnlyList<CopiersMaintenanceV2StoredFile> originalAttachments)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            record.SubmissionKey,
            record.TechnicianSystemUserId,
            record.TechnicianName,
            record.TechnicianEmail,
            record.ClientId,
            record.ClientName,
            record.CustomerContactName,
            record.CustomerEmail,
            record.EquipmentId,
            record.EquipmentSerial,
            record.Title,
            serviceDate = record.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            record.MaintenanceTypeValue,
            formVersion,
            answers,
            workPerformed,
            customerObservations,
            serviceAddressInternal,
            internalNotes,
            signerName,
            signerRole,
            customerAccepted,
            signaturePointCount,
            deviceSignedAtUtc = deviceSignedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            internalLocation = internalLocation is null ? null : new
            {
                internalLocation.Latitude,
                internalLocation.Longitude,
                internalLocation.AccuracyMeters,
                capturedAtUtc = internalLocation.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                internalLocation.Source
            },
            signatureSha256 = signature.Sha256.ToLowerInvariant(),
            attachments = originalAttachments.Select((file, index) => new
            {
                sequence = index + 1,
                file.FileName,
                file.Size,
                sha256 = file.Sha256.ToLowerInvariant()
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private async Task TryMarkFinalizationFailedAsync(
        string recordId,
        string submissionKey,
        string leaseId,
        string technicianSystemUserId,
        Exception exception)
    {
        try
        {
            await _repository.MarkFinalizationFailedAsync(new CopiersMaintenanceV2FinalizationFailedCommand
            {
                RecordId = recordId,
                SubmissionKey = submissionKey,
                FinalizationLeaseId = leaseId,
                TechnicianSystemUserId = technicianSystemUserId,
                ErrorCode = exception is CopiersMaintenanceV2ValidationException validation ? validation.Code : "finalization_failed",
                ErrorMessage = SafeFailureMessage(exception),
                FailedAtUtc = _timeProvider.GetUtcNow()
            }, CancellationToken.None);
        }
        catch (Exception persistenceException)
        {
            _logger.LogError(
                persistenceException,
                "No fue posible marcar como fallida la finalizacion V2 {RecordId}; lease {LeaseId}.",
                recordId,
                leaseId);
        }
    }

    private static CopiersMaintenanceV2DraftResultDto ToDraftResult(CopiersMaintenanceV2DraftRecord record, bool reusedExisting) =>
        new()
        {
            RecordId = record.RecordId,
            SubmissionKey = record.SubmissionKey,
            Version = record.Version,
            State = record.State,
            EmailState = record.EmailState,
            UpdatedAtUtc = record.UpdatedAtUtc,
            ReusedExisting = reusedExisting
        };

    private static CopiersMaintenanceV2FinalizeResultDto ToFinalizeResult(
        CopiersMaintenanceV2DraftRecord record,
        bool idempotentReplay,
        string message) =>
        new()
        {
            RecordId = record.RecordId,
            SubmissionKey = record.SubmissionKey,
            Version = record.Version,
            State = record.State,
            EmailState = record.EmailState,
            ReportFileName = record.ReportFileName,
            ReportSha256 = record.ReportSha256,
            AttachmentCount = record.AttachmentCount,
            ServerFinalizedAtUtc = record.ServerFinalizedAtUtc ?? default,
            IdempotentReplay = idempotentReplay,
            Message = message
        };

    private static void EnsureRecordIdentity(
        CopiersMaintenanceV2DraftRecord record,
        string submissionKey,
        string technicianSystemUserId)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.RecordId))
            throw new InvalidOperationException("Dataverse no devolvio el mantenimiento V2 guardado.");
        if (!string.Equals(record.SubmissionKey, submissionKey, StringComparison.Ordinal))
            throw new CopiersMaintenanceV2ConcurrencyException("La clave de envio pertenece a otro mantenimiento.");
        if (!string.Equals(record.TechnicianSystemUserId, technicianSystemUserId, StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ConcurrencyException("El mantenimiento no pertenece al tecnico autenticado.");
    }

    private static void EnsureReplayScopeMatches(
        CopiersMaintenanceV2DraftRecord record,
        string clientId,
        string equipmentId,
        string equipmentSerial)
    {
        if (!string.Equals(record.ClientId, clientId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(equipmentId)
                && !string.Equals(record.EquipmentId, equipmentId, StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(equipmentId)
                && !string.Equals(record.EquipmentSerial, equipmentSerial, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CopiersMaintenanceV2ConcurrencyException("La clave de envio ya fue usada con otro cliente o equipo.");
        }
    }

    private static void EnsureImmutableBaseSnapshotMatches(
        CopiersMaintenanceV2DraftRecord record,
        CopiersMaintenanceV2CreateDraftCommand expected)
    {
        EnsureReplayScopeMatches(record, expected.ClientId, expected.EquipmentId, expected.EquipmentSerial);
        if (!string.Equals(record.TechnicianName, expected.TechnicianName, StringComparison.Ordinal)
            || !string.Equals(record.TechnicianEmail, expected.TechnicianEmail, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.ClientName, expected.ClientName, StringComparison.Ordinal)
            || !string.Equals(record.CustomerContactName, expected.CustomerContactName, StringComparison.Ordinal)
            || !string.Equals(record.CustomerEmail, expected.CustomerEmail, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.EquipmentId, expected.EquipmentId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.EquipmentSerial, expected.EquipmentSerial, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.Title, expected.Title, StringComparison.Ordinal)
            || record.ServiceDate != expected.ServiceDate
            || record.MaintenanceTypeValue != expected.MaintenanceTypeValue)
        {
            throw new CopiersMaintenanceV2ConcurrencyException(
                "La clave de envio ya finalizó o está finalizando con datos base diferentes.");
        }
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens, bool htmlEncodeValues)
    {
        var result = template ?? "";
        foreach (var token in tokens)
        {
            var value = htmlEncodeValues ? HtmlEncoder.Default.Encode(token.Value ?? "") : token.Value ?? "";
            result = result.Replace($"{{{token.Key}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))?.Trim() ?? "";

    private static string Truncate(string? value, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string SafeFailureMessage(Exception exception) => exception switch
    {
        CopiersMaintenanceV2ValidationException validation => Truncate(validation.Message, 500),
        CopiersMaintenanceV2ConcurrencyException => "La fila cambió durante la finalización.",
        CopiersMaintenanceV2PersistenceException => "Dataverse no completó una operación de persistencia.",
        _ => "La finalización no pudo completarse. Consulta el trace del servidor."
    };
}

