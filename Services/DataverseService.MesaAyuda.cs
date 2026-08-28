using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.MesaAyuda;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string MesaAyudaInteractionEntitySetName = "hd_supportinteractions";
    private const string MesaAyudaInteractionIdField = "hd_supportinteractionid";
    private const string MesaAyudaInteractionNameField = "hd_name";
    private const string MesaAyudaInteractionKeyField = "hd_interactionkey";
    private const string MesaAyudaInteractionEventAtField = "hd_eventat";
    private const string MesaAyudaInteractionTypeField = "hd_interactiontype";
    private const string MesaAyudaInteractionDirectionField = "hd_direction";
    private const string MesaAyudaInteractionActorTypeField = "hd_actortype";
    private const string MesaAyudaInteractionActorNameField = "hd_actorname";
    private const string MesaAyudaInteractionActorAddressField = "hd_actoraddress";
    private const string MesaAyudaInteractionSubjectField = "hd_subject";
    private const string MesaAyudaInteractionContentField = "hd_content";
    private const string MesaAyudaInteractionStructuredJsonField = "hd_structuredjson";
    private const string MesaAyudaInteractionModelResponseIdField = "hd_modelresponsekey";
    private const string MesaAyudaInteractionClassificationField = "hd_classification";
    private const string MesaAyudaInteractionConfidenceField = "hd_confidence";
    private const string MesaAyudaInteractionVisibleCustomerField = "hd_visiblecustomer";
    private const string MesaAyudaInteractionIdempotencyKeyField = "hd_idempotencykey";
    private const string MesaAyudaInteractionSequenceField = "hd_sequence";
    private const string MesaAyudaInteractionTicketLookupField = "hd_ticketid";
    private const string MesaAyudaInteractionTicketNavigationProperty = "hd_TicketId";

    private const string MesaAyudaCaseNumberField = "hd_casenumber";
    private const string MesaAyudaSourceChannelField = "hd_sourcechannel";
    private const string MesaAyudaReceiveMailboxField = "hd_receivemailbox";
    private const string MesaAyudaExternalConversationField = "hd_externalconversation";
    private const string MesaAyudaExternalCaseKeyField = "hd_externalcasekey";
    private const string MesaAyudaAiClassificationField = "hd_aiclassification";
    private const string MesaAyudaAiConfidenceField = "hd_aiconfidence";
    private const string MesaAyudaAiSeverityField = "hd_aiseverity";
    private const string MesaAyudaAiSummaryField = "hd_aisummary";
    private const string MesaAyudaAutomationStatusField = "hd_automationstatus";
    private const string MesaAyudaLastActivityAtField = "hd_lastactivityat";
    private const string MesaAyudaCustomerTenantLookupField = "hd_customertenantid";
    private const string MesaAyudaCustomerTenantNavigationProperty = "hd_CustomerTenantId";
    private const string MesaAyudaCustomerTenantNameField = "hd_name";
    private const string MesaAyudaCustomerTenantGuidField = "hd_tenantguid";
    private const string MesaAyudaOwnerLookupField = "ownerid";

    private static readonly JsonSerializerOptions MesaAyudaJsonOptions = new(JsonOptions)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<MesaAyudaDataverseTicketDto>> GetMesaAyudaTicketsAsync(
        CancellationToken ct = default)
    {
        var user = GetMesaAyudaUser();
        var metadata = await ResolveSoporteCloudMetadataAsync(user, ct);
        var select = BuildMesaAyudaTicketSelectClause(metadata);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={select}" +
            $"&$expand={MesaAyudaCustomerTenantNavigationProperty}" +
            $"($select={MesaAyudaCustomerTenantNameField},{MesaAyudaCustomerTenantGuidField})" +
            $"&$orderby={MesaAyudaLastActivityAtField} desc,{SoporteCloudModifiedOnField} desc";
        var items = await GetDataverseEntitiesAsync(
            relativeUrl,
            user,
            ct,
            AddFormattedValueHeaders);

        return items
            .Select(item => BuildMesaAyudaTicketDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task<MesaAyudaDataverseTicketDto?> GetMesaAyudaTicketAsync(
        string ticketId,
        CancellationToken ct = default)
    {
        var normalizedTicketId = NormalizeGuid(ticketId, nameof(ticketId));
        var user = GetMesaAyudaUser();
        var metadata = await ResolveSoporteCloudMetadataAsync(user, ct);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}" +
            $"?$select={BuildMesaAyudaTicketSelectClause(metadata)}" +
            $"&$expand={MesaAyudaCustomerTenantNavigationProperty}" +
            $"($select={MesaAyudaCustomerTenantNameField},{MesaAyudaCustomerTenantGuidField})" +
            $"&$filter={metadata.BaseMetadata.PrimaryIdField} eq {normalizedTicketId}&$top=1";
        var items = await GetDataverseEntitiesAsync(
            relativeUrl,
            user,
            ct,
            AddFormattedValueHeaders);
        return items.Count == 0
            ? null
            : BuildMesaAyudaTicketDto(metadata, items[0]);
    }

    public async Task<IReadOnlyList<MesaAyudaInteractionDto>>
        GetMesaAyudaInteractionsAsync(CancellationToken ct = default)
    {
        var relativeUrl =
            $"/api/data/v9.2/{MesaAyudaInteractionEntitySetName}" +
            $"?$select={BuildMesaAyudaInteractionSelectClause()}" +
            $"&$orderby={MesaAyudaInteractionEventAtField} asc,{MesaAyudaInteractionSequenceField} asc,createdon asc";
        var items = await GetDataverseEntitiesAsync(
            relativeUrl,
            GetMesaAyudaUser(),
            ct,
            AddFormattedValueHeaders);

        return items
            .Select(BuildMesaAyudaInteractionDto)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task<IReadOnlyList<MesaAyudaInteractionDto>> GetMesaAyudaInteractionsAsync(
        string ticketId,
        CancellationToken ct = default)
    {
        var normalizedTicketId = NormalizeGuid(ticketId, nameof(ticketId));
        var lookupValueField = BuildDashboardLookupValuePropertyName(
            MesaAyudaInteractionTicketLookupField);
        var relativeUrl =
            $"/api/data/v9.2/{MesaAyudaInteractionEntitySetName}" +
            $"?$select={BuildMesaAyudaInteractionSelectClause()}" +
            $"&$filter={lookupValueField} eq {normalizedTicketId}" +
            $"&$orderby={MesaAyudaInteractionEventAtField} asc,{MesaAyudaInteractionSequenceField} asc,createdon asc";
        var items = await GetDataverseEntitiesAsync(
            relativeUrl,
            GetMesaAyudaUser(),
            ct,
            AddFormattedValueHeaders);

        return items
            .Select(BuildMesaAyudaInteractionDto)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task<MesaAyudaInteractionDto?> GetMesaAyudaInteractionByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var normalizedKey = NormalizeMesaAyudaIdempotencyKey(idempotencyKey);
        return await GetMesaAyudaInteractionByIdempotencyKeyCoreAsync(
            normalizedKey,
            GetMesaAyudaUser(),
            ct);
    }

    public async Task<MesaAyudaInteractionDto> CreateMesaAyudaInternalMessageAsync(
        MesaAyudaInternalMessageCreate request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ticketId = NormalizeGuid(request.TicketId, nameof(request.TicketId));
        var content = NormalizeRequiredMesaAyudaText(
            request.Content,
            4000,
            "El mensaje interno esta vacio.");
        var idempotencyKey = NormalizeMesaAyudaIdempotencyKey(request.IdempotencyKey);
        var eventAt = request.EventAtUtc.ToUniversalTime();
        var actorName = LimitMesaAyudaText(request.ActorName, 200);
        var user = GetMesaAyudaUser();
        var sequence = await GetNextMesaAyudaInteractionSequenceAsync(
            ticketId,
            user,
            ct);

        var payload = BuildMesaAyudaInteractionBasePayload(
            ticketId,
            idempotencyKey,
            sequence,
            eventAt,
            FirstNonEmpty(request.Subject, "Mensaje interno"),
            content,
            actorName,
            request.ActorAddress);
        payload[MesaAyudaInteractionStructuredJsonField] = JsonSerializer.Serialize(
            new
            {
                kind = "agent_instruction",
                actorObjectId = LimitMesaAyudaText(request.ActorObjectId, 64)
            },
            MesaAyudaJsonOptions);
        payload[MesaAyudaInteractionTypeField] = "chat";
        payload[MesaAyudaInteractionDirectionField] = "internal";
        payload[MesaAyudaInteractionActorTypeField] = "agent";

        var saved = await CreateMesaAyudaInteractionIdempotentlyAsync(
            idempotencyKey,
            payload,
            ticketId,
            content,
            expectedStructuredJson: null,
            user,
            ct);
        await UpdateMesaAyudaTicketProjectionAsync(
            ticketId,
            eventAt,
            investigation: null,
            user,
            ct);
        return saved;
    }

    public async Task<MesaAyudaInteractionDto> SaveMesaAyudaInvestigationAsync(
        MesaAyudaInvestigationCreate request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Investigation);
        var ticketId = NormalizeGuid(request.TicketId, nameof(request.TicketId));
        var idempotencyKey = NormalizeMesaAyudaIdempotencyKey(request.IdempotencyKey);
        var eventAt = request.EventAtUtc.ToUniversalTime();
        var structuredJson = JsonSerializer.Serialize(
            request.Investigation,
            MesaAyudaJsonOptions);
        var content = FirstNonEmpty(
            LimitMesaAyudaText(request.Investigation.Summary, 4000),
            "Auditoria IA completada; revisa los datos estructurados.");
        var user = GetMesaAyudaUser();
        var sequence = await GetNextMesaAyudaInteractionSequenceAsync(
            ticketId,
            user,
            ct);

        var payload = BuildMesaAyudaInteractionBasePayload(
            ticketId,
            idempotencyKey,
            sequence,
            eventAt,
            "Resultado de auditoria IA",
            content,
            "Auditor IA",
            "");
        payload[MesaAyudaInteractionStructuredJsonField] = structuredJson;
        payload[MesaAyudaInteractionModelResponseIdField] =
            LimitMesaAyudaText(request.Investigation.ResponseId, 200);
        payload[MesaAyudaInteractionTypeField] = "audit";
        payload[MesaAyudaInteractionDirectionField] = "internal";
        payload[MesaAyudaInteractionActorTypeField] = "ai";
        payload[MesaAyudaInteractionClassificationField] =
            LimitMesaAyudaText(request.Investigation.Classification, 40);
        payload["hd_triagestatus"] = "completed";
        payload[MesaAyudaInteractionConfidenceField] =
            Math.Clamp(request.Investigation.Confidence, 0m, 1m);

        var saved = await CreateMesaAyudaInteractionIdempotentlyAsync(
            idempotencyKey,
            payload,
            ticketId,
            content,
            structuredJson,
            user,
            ct);
        await UpdateMesaAyudaTicketProjectionAsync(
            ticketId,
            eventAt,
            request.Investigation,
            user,
            ct);
        return saved;
    }

    private ClaimsPrincipal GetMesaAyudaUser() =>
        _httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HttpContext available.");

    private async Task<MesaAyudaInteractionDto> CreateMesaAyudaInteractionIdempotentlyAsync(
        string idempotencyKey,
        IReadOnlyDictionary<string, object?> payload,
        string expectedTicketId,
        string expectedContent,
        string? expectedStructuredJson,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var existing = await GetMesaAyudaInteractionByIdempotencyKeyCoreAsync(
            idempotencyKey,
            user,
            ct);
        if (existing is not null)
        {
            ValidateMesaAyudaIdempotentReplay(
                existing,
                expectedTicketId,
                expectedContent,
                expectedStructuredJson);
            return existing;
        }

        var relativeUrl = $"/api/data/v9.2/{MesaAyudaInteractionEntitySetName}";
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, MesaAyudaJsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "POST",
            user,
            ct,
            content,
            AddRhReturnRepresentationHeaders);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
        {
            using var document = JsonDocument.Parse(body);
            var inline = BuildMesaAyudaInteractionDto(document.RootElement);
            if (inline is not null)
                return inline;
        }

        // Always read by the unique key after an uncertain response. This covers
        // successful 204 responses, time-adjacent retries, and a concurrent create
        // rejected by Dataverse's alternate-key constraint.
        var resolved = await GetMesaAyudaInteractionByIdempotencyKeyCoreAsync(
            idempotencyKey,
            user,
            ct);
        if (resolved is not null)
        {
            ValidateMesaAyudaIdempotentReplay(
                resolved,
                expectedTicketId,
                expectedContent,
                expectedStructuredJson);
            return resolved;
        }

        throw new InvalidOperationException(
            $"Dataverse no pudo registrar la interaccion durable ({(int)response.StatusCode} {response.ReasonPhrase}).");
    }

    private async Task<MesaAyudaInteractionDto?>
        GetMesaAyudaInteractionByIdempotencyKeyCoreAsync(
            string idempotencyKey,
            ClaimsPrincipal user,
            CancellationToken ct)
    {
        var safeKey = EscapeOdataLiteral(idempotencyKey);
        var relativeUrl =
            $"/api/data/v9.2/{MesaAyudaInteractionEntitySetName}" +
            $"?$select={BuildMesaAyudaInteractionSelectClause()}" +
            $"&$filter={MesaAyudaInteractionIdempotencyKeyField} eq '{safeKey}'&$top=1";
        var items = await GetDataverseEntitiesAsync(
            relativeUrl,
            user,
            ct,
            AddFormattedValueHeaders);
        return items.Count == 0 ? null : BuildMesaAyudaInteractionDto(items[0]);
    }

    private async Task UpdateMesaAyudaTicketProjectionAsync(
        string ticketId,
        DateTimeOffset eventAtUtc,
        MesaAyudaInvestigationResultDto? investigation,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            [MesaAyudaLastActivityAtField] =
                eventAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        };
        if (investigation is not null)
        {
            payload[MesaAyudaAiClassificationField] =
                LimitMesaAyudaText(investigation.Classification, 40);
            payload[MesaAyudaAiConfidenceField] =
                Math.Clamp(investigation.Confidence, 0m, 1m);
            payload[MesaAyudaAiSeverityField] =
                LimitMesaAyudaText(investigation.Severity, 40);
            payload[MesaAyudaAiSummaryField] =
                LimitMesaAyudaText(investigation.Summary, 10000);
            payload[MesaAyudaAutomationStatusField] = "waiting_agent_review";
        }
        else
        {
            payload[MesaAyudaAutomationStatusField] = "agent_active";
        }

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{SoporteCloudFallbackEntitySetName}({ticketId})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private static Dictionary<string, object?> BuildMesaAyudaInteractionBasePayload(
        string ticketId,
        string idempotencyKey,
        int sequence,
        DateTimeOffset eventAtUtc,
        string subject,
        string content,
        string actorName,
        string actorAddress) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [MesaAyudaInteractionNameField] = LimitMesaAyudaText(subject, 200),
            [MesaAyudaInteractionKeyField] = idempotencyKey,
            [MesaAyudaInteractionEventAtField] =
                eventAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [MesaAyudaInteractionSequenceField] = sequence,
            [MesaAyudaInteractionActorNameField] =
                LimitMesaAyudaText(actorName, 200),
            [MesaAyudaInteractionActorAddressField] =
                LimitMesaAyudaText(actorAddress, 320),
            [MesaAyudaInteractionSubjectField] = LimitMesaAyudaText(subject, 500),
            [MesaAyudaInteractionContentField] = content,
            [MesaAyudaInteractionVisibleCustomerField] = false,
            [$"{MesaAyudaInteractionTicketNavigationProperty}@odata.bind"] =
                $"/{SoporteCloudFallbackEntitySetName}({ticketId})"
        };

    private async Task<int> GetNextMesaAyudaInteractionSequenceAsync(
        string ticketId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var lookupValueField = BuildDashboardLookupValuePropertyName(
            MesaAyudaInteractionTicketLookupField);
        var relativeUrl =
            $"/api/data/v9.2/{MesaAyudaInteractionEntitySetName}" +
            $"?$select={MesaAyudaInteractionSequenceField}" +
            $"&$filter={lookupValueField} eq {ticketId}" +
            $"&$orderby={MesaAyudaInteractionSequenceField} desc&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        if (items.Count == 0)
            return 1;

        var current = ReadIntFlexible(
            items[0],
            MesaAyudaInteractionSequenceField);
        if (current >= int.MaxValue)
        {
            throw new InvalidOperationException(
                "La secuencia de interacciones alcanzo su limite.");
        }

        return Math.Max(current + 1, 1);
    }

    private static string BuildMesaAyudaTicketSelectClause(
        SoporteCloudMetadata metadata) =>
        string.Join(
            ",",
            new[]
            {
                metadata.BaseMetadata.PrimaryIdField,
                metadata.BaseMetadata.PrimaryNameField,
                SoporteCloudTitleField,
                SoporteCloudDescriptionField,
                SoporteCloudCreationDateField,
                SoporteCloudStateField,
                SoporteCloudTypeField,
                BuildDashboardLookupValuePropertyName(SoporteCloudClientField),
                SoporteCloudCategoryField,
                BuildDashboardLookupValuePropertyName(SoporteCloudCreatedByField),
                BuildDashboardLookupValuePropertyName(MesaAyudaOwnerLookupField),
                SoporteCloudSolutionField,
                SoporteCloudAttachmentField,
                SoporteCloudAttachmentNameField,
                SoporteCloudModifiedOnField,
                SoporteCloudCreatedOnFallbackField,
                MesaAyudaCaseNumberField,
                MesaAyudaSourceChannelField,
                MesaAyudaReceiveMailboxField,
                MesaAyudaExternalConversationField,
                MesaAyudaExternalCaseKeyField,
                MesaAyudaAiClassificationField,
                MesaAyudaAiConfidenceField,
                MesaAyudaAiSeverityField,
                MesaAyudaAiSummaryField,
                MesaAyudaAutomationStatusField,
                MesaAyudaLastActivityAtField,
                BuildDashboardLookupValuePropertyName(
                    MesaAyudaCustomerTenantLookupField)
            }
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private MesaAyudaDataverseTicketDto? BuildMesaAyudaTicketDto(
        SoporteCloudMetadata metadata,
        JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.BaseMetadata.PrimaryIdField),
            ReadString(item, SoporteCloudFallbackIdField));
        if (!Guid.TryParse(recordId, out var parsedRecordId))
            return null;

        var clientLookupField = BuildDashboardLookupValuePropertyName(
            SoporteCloudClientField);
        var createdByLookupField = BuildDashboardLookupValuePropertyName(
            SoporteCloudCreatedByField);
        var ownerLookupField = BuildDashboardLookupValuePropertyName(
            MesaAyudaOwnerLookupField);
        var tenantLookupField = BuildDashboardLookupValuePropertyName(
            MesaAyudaCustomerTenantLookupField);
        JsonElement tenant = default;
        var hasTenant = item.TryGetProperty(
                MesaAyudaCustomerTenantNavigationProperty,
                out tenant)
            && tenant.ValueKind == JsonValueKind.Object;
        var createdAtRaw = FirstNonEmpty(
            ReadString(item, SoporteCloudCreationDateField),
            ReadString(item, SoporteCloudCreatedOnFallbackField));
        var lastActivityRaw = FirstNonEmpty(
            ReadString(item, MesaAyudaLastActivityAtField),
            ReadString(item, SoporteCloudModifiedOnField),
            createdAtRaw);

        return new MesaAyudaDataverseTicketDto
        {
            RecordId = parsedRecordId.ToString("D"),
            CaseNumber = ReadString(item, MesaAyudaCaseNumberField).Trim(),
            Title = FirstNonEmpty(
                ReadString(item, SoporteCloudTitleField),
                ReadString(item, metadata.BaseMetadata.PrimaryNameField),
                "Caso sin titulo"),
            Description = ReadString(item, SoporteCloudDescriptionField).Trim(),
            ClientId = NormalizeOptionalGuid(ReadString(item, clientLookupField)),
            ClientName = FirstNonEmpty(
                ReadLookupFormattedValue(item, clientLookupField),
                "Cliente sin confirmar"),
            Status = FirstNonEmpty(
                ReadMesaAyudaFormattedValue(item, SoporteCloudStateField),
                "Sin estado"),
            Category = ReadMesaAyudaFormattedValue(
                item,
                SoporteCloudCategoryField),
            Workload = ReadMesaAyudaFormattedValue(item, SoporteCloudTypeField),
            CreatedAtValue = createdAtRaw,
            CreatedAtDisplay = FormatMesaAyudaDateTime(createdAtRaw),
            LastActivityAtValue = lastActivityRaw,
            LastActivityAtDisplay = FormatMesaAyudaDateTime(lastActivityRaw),
            CreatedByName = FirstNonEmpty(
                ReadLookupFormattedValue(item, createdByLookupField),
                "Digital Tech"),
            OwnerId = NormalizeOptionalGuid(ReadString(item, ownerLookupField)),
            OwnerName = FirstNonEmpty(
                ReadLookupFormattedValue(item, ownerLookupField),
                "Sin asignar"),
            SourceChannel = FirstNonEmpty(
                ReadMesaAyudaFormattedValue(item, MesaAyudaSourceChannelField),
                "Registro actual"),
            ReceiveMailbox = ReadString(
                item,
                MesaAyudaReceiveMailboxField).Trim(),
            ExternalConversation = ReadString(
                item,
                MesaAyudaExternalConversationField).Trim(),
            ExternalCaseKey = ReadString(
                item,
                MesaAyudaExternalCaseKeyField).Trim(),
            AiClassification = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaAiClassificationField),
            AiConfidence = ReadDecimal(item, MesaAyudaAiConfidenceField),
            AiSeverity = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaAiSeverityField),
            AiSummary = ReadString(item, MesaAyudaAiSummaryField).Trim(),
            AutomationStatus = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaAutomationStatusField),
            TenantRecordId = NormalizeOptionalGuid(
                ReadString(item, tenantLookupField)),
            TenantName = FirstNonEmpty(
                hasTenant
                    ? ReadString(
                        tenant,
                        MesaAyudaCustomerTenantNameField)
                    : "",
                ReadLookupFormattedValue(item, tenantLookupField)),
            TenantId = hasTenant
                ? ReadString(
                    tenant,
                    MesaAyudaCustomerTenantGuidField).Trim()
                : "",
            ExistingResolution = ReadString(
                item,
                SoporteCloudSolutionField).Trim(),
            HasAttachment =
                !string.IsNullOrWhiteSpace(
                    ReadString(item, SoporteCloudAttachmentField))
                || !string.IsNullOrWhiteSpace(
                    ReadString(item, SoporteCloudAttachmentNameField)),
            AttachmentFileName = FirstNonEmpty(
                ReadString(item, SoporteCloudAttachmentNameField),
                ReadString(
                    item,
                    $"{SoporteCloudAttachmentField}{FormattedValueAnnotationSuffix}"))
        };
    }

    private static string BuildMesaAyudaInteractionSelectClause() =>
        string.Join(
            ",",
            new[]
            {
                MesaAyudaInteractionIdField,
                BuildDashboardLookupValuePropertyName(
                    MesaAyudaInteractionTicketLookupField),
                MesaAyudaInteractionKeyField,
                MesaAyudaInteractionIdempotencyKeyField,
                MesaAyudaInteractionEventAtField,
                MesaAyudaInteractionSequenceField,
                MesaAyudaInteractionTypeField,
                MesaAyudaInteractionDirectionField,
                MesaAyudaInteractionActorTypeField,
                MesaAyudaInteractionActorNameField,
                MesaAyudaInteractionActorAddressField,
                MesaAyudaInteractionSubjectField,
                MesaAyudaInteractionContentField,
                MesaAyudaInteractionStructuredJsonField,
                MesaAyudaInteractionModelResponseIdField,
                MesaAyudaInteractionClassificationField,
                MesaAyudaInteractionConfidenceField,
                MesaAyudaInteractionVisibleCustomerField
            });

    private static MesaAyudaInteractionDto? BuildMesaAyudaInteractionDto(
        JsonElement item)
    {
        var recordId = ReadString(item, MesaAyudaInteractionIdField);
        if (!Guid.TryParse(recordId, out var parsedRecordId))
            return null;

        var ticketLookupField = BuildDashboardLookupValuePropertyName(
            MesaAyudaInteractionTicketLookupField);
        return new MesaAyudaInteractionDto
        {
            RecordId = parsedRecordId.ToString("D"),
            TicketId = NormalizeOptionalGuid(
                ReadString(item, ticketLookupField)),
            InteractionKey = ReadString(
                item,
                MesaAyudaInteractionKeyField).Trim(),
            IdempotencyKey = ReadString(
                item,
                MesaAyudaInteractionIdempotencyKeyField).Trim(),
            EventAtUtc = ParseMesaAyudaDateTimeOffset(
                ReadString(item, MesaAyudaInteractionEventAtField)),
            Sequence = ReadIntFlexible(item, MesaAyudaInteractionSequenceField),
            InteractionType = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaInteractionTypeField),
            Direction = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaInteractionDirectionField),
            ActorType = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaInteractionActorTypeField),
            ActorName = ReadString(
                item,
                MesaAyudaInteractionActorNameField).Trim(),
            ActorAddress = ReadString(
                item,
                MesaAyudaInteractionActorAddressField).Trim(),
            Subject = ReadString(
                item,
                MesaAyudaInteractionSubjectField).Trim(),
            Content = ReadString(
                item,
                MesaAyudaInteractionContentField).Trim(),
            StructuredJson = ReadString(
                item,
                MesaAyudaInteractionStructuredJsonField).Trim(),
            ModelResponseId = ReadString(
                item,
                MesaAyudaInteractionModelResponseIdField).Trim(),
            Classification = ReadMesaAyudaFormattedValue(
                item,
                MesaAyudaInteractionClassificationField),
            Confidence = ReadDecimal(
                item,
                MesaAyudaInteractionConfidenceField),
            VisibleToCustomer = ReadBool(
                item,
                MesaAyudaInteractionVisibleCustomerField)
        };
    }

    private static void ValidateMesaAyudaIdempotentReplay(
        MesaAyudaInteractionDto existing,
        string expectedTicketId,
        string expectedContent,
        string? expectedStructuredJson)
    {
        if (!string.Equals(
                NormalizeOptionalGuid(existing.TicketId),
                expectedTicketId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                existing.Content.Trim(),
                expectedContent.Trim(),
                StringComparison.Ordinal)
            || expectedStructuredJson is not null
            && !string.Equals(
                existing.StructuredJson.Trim(),
                expectedStructuredJson.Trim(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La clave de idempotencia ya pertenece a otra interaccion o a un contenido diferente.");
        }
    }

    private static string ReadMesaAyudaFormattedValue(
        JsonElement item,
        string fieldName) =>
        FirstNonEmpty(
            ReadString(item, $"{fieldName}{FormattedValueAnnotationSuffix}"),
            ReadString(item, fieldName));

    private static string NormalizeMesaAyudaIdempotencyKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length != 64
            || normalized.Any(character =>
                !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException(
                "La clave de idempotencia de Mesa de ayuda no es valida.");
        }

        return normalized;
    }

    private static string NormalizeRequiredMesaAyudaText(
        string? value,
        int maxLength,
        string emptyMessage)
    {
        var normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException(emptyMessage);

        return LimitMesaAyudaText(normalized, maxLength);
    }

    private static string LimitMesaAyudaText(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? "";
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static DateTimeOffset? ParseMesaAyudaDateTimeOffset(string? value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static string FormatMesaAyudaDateTime(string? value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dateTime))
        {
            return dateTime.ToOffset(TimeSpan.FromHours(-5))
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
        }

        if (DateOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date))
        {
            return date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
        }

        return value?.Trim() ?? "";
    }
}
