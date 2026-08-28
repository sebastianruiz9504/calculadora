using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.MesaAyuda;
using CotizadorInterno.Web.Models.SoporteCloud;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public sealed class MesaAyudaWorkspaceService : IMesaAyudaWorkspaceService
{
    private static readonly DateOnly EarliestSupportedDate = new(2020, 1, 1);
    private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IDataverseService _dataverse;
    private readonly MesaAyudaOptions _options;

    public MesaAyudaWorkspaceService(
        IDataverseService dataverse,
        IOptions<MesaAyudaOptions> options)
    {
        _dataverse = dataverse;
        _options = options.Value;
    }

    public async Task<MesaAyudaWorkspaceDto> GetWorkspaceAsync(CancellationToken ct = default)
    {
        if (_options.SchemaProvisioned)
        {
            return await GetDurableWorkspaceAsync(ct);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var board = await _dataverse.GetSoporteCloudBoardAsync(
            EarliestSupportedDate,
            today,
            ct);
        var tickets = board.Records
            .Select(MapTicket)
            .OrderByDescending(ParseSortDate)
            .ThenBy(ticket => ticket.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MesaAyudaWorkspaceDto
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            SchemaProvisioned = _options.SchemaProvisioned,
            DataStatus = _options.SchemaProvisioned
                ? $"Dataverse conectado como fuente transaccional. {_options.MonitoredMailboxes.Length} buzones definidos."
                : $"Vista conectada a cr07a_ticket. {_options.MonitoredMailboxes.Length} buzones definidos; la bitacora, el consecutivo y las aprobaciones se activan al provisionar el esquema de Mesa de ayuda.",
            Queues = BuildQueues(tickets),
            Tickets = tickets
        };
    }

    private async Task<MesaAyudaWorkspaceDto> GetDurableWorkspaceAsync(
        CancellationToken ct)
    {
        var records = await _dataverse.GetMesaAyudaTicketsAsync(ct);
        var allInteractions = await _dataverse.GetMesaAyudaInteractionsAsync(ct);
        var interactionsByTicket = allInteractions
            .Where(interaction => Guid.TryParse(interaction.TicketId, out _))
            .GroupBy(
                interaction => NormalizeGuid(interaction.TicketId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MesaAyudaInteractionDto>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var tickets = records
            .Select(record => MapDurableTicket(
                record,
                interactionsByTicket.TryGetValue(
                    NormalizeGuid(record.RecordId),
                    out var interactions)
                    ? interactions
                    : Array.Empty<MesaAyudaInteractionDto>()))
            .ToList();

        var ordered = tickets
            .OrderByDescending(ParseSortDate)
            .ThenBy(ticket => ticket.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MesaAyudaWorkspaceDto
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            SchemaProvisioned = true,
            DataStatus =
                $"Dataverse conectado como fuente transaccional durable. {_options.MonitoredMailboxes.Length} buzones definidos.",
            Queues = BuildQueues(ordered),
            Tickets = ordered
        };
    }

    public async Task<MesaAyudaTicketDto?> GetTicketAsync(
        string ticketId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(ticketId, out var parsedTicketId))
        {
            return null;
        }

        var normalized = parsedTicketId.ToString("D");
        if (_options.SchemaProvisioned)
        {
            var record = await _dataverse.GetMesaAyudaTicketAsync(
                normalized,
                ct);
            if (record is null)
                return null;

            var interactions = await _dataverse.GetMesaAyudaInteractionsAsync(
                normalized,
                ct);
            return MapDurableTicket(record, interactions);
        }

        var workspace = await GetWorkspaceAsync(ct);
        return workspace.Tickets.FirstOrDefault(ticket =>
            string.Equals(
                NormalizeGuid(ticket.RecordId),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MesaAyudaTimelineEventDto> CreateInternalMessageAsync(
        MesaAyudaInternalMessageCreate request,
        CancellationToken ct = default)
    {
        var saved = await _dataverse.CreateMesaAyudaInternalMessageAsync(
            request,
            ct);
        return MapInteraction(saved);
    }

    public async Task<MesaAyudaInvestigationResultDto?> GetPersistedInvestigationAsync(
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var saved = await _dataverse.GetMesaAyudaInteractionByIdempotencyKeyAsync(
            idempotencyKey,
            ct);
        if (saved is null)
            return null;
        if (string.IsNullOrWhiteSpace(saved.StructuredJson))
        {
            throw new InvalidOperationException(
                "La auditoria ya existe, pero su resultado estructurado no esta disponible. No se ejecutara nuevamente el modelo.");
        }

        try
        {
            return JsonSerializer.Deserialize<MesaAyudaInvestigationResultDto>(
                saved.StructuredJson,
                PersistenceJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "La auditoria ya existe, pero su resultado estructurado no es valido. No se ejecutara nuevamente el modelo.",
                ex);
        }
    }

    public async Task<MesaAyudaTimelineEventDto> SaveInvestigationAsync(
        MesaAyudaInvestigationCreate request,
        CancellationToken ct = default)
    {
        var saved = await _dataverse.SaveMesaAyudaInvestigationAsync(
            request,
            ct);
        return MapInteraction(saved);
    }

    private static MesaAyudaTicketDto MapDurableTicket(
        MesaAyudaDataverseTicketDto source,
        IReadOnlyList<MesaAyudaInteractionDto> interactions)
    {
        var status = FirstNonEmpty(source.Status, "Sin estado");
        var statusKey = ResolveStatusKey(status);
        var timeline = new List<MesaAyudaTimelineEventDto>
        {
            new()
            {
                Kind = "case-created",
                Tone = "neutral",
                Label = "Caso registrado",
                Actor = FirstNonEmpty(source.CreatedByName, "Digital Tech"),
                Timestamp = source.CreatedAtDisplay,
                Body = FirstNonEmpty(
                    source.Description,
                    "El registro no contiene una descripcion."),
                Detail = "Origen: tabla cr07a_ticket"
            }
        };

        if (source.HasAttachment)
        {
            timeline.Add(new MesaAyudaTimelineEventDto
            {
                Kind = "attachment",
                Tone = "info",
                Label = "Evidencia adjunta",
                Actor = FirstNonEmpty(source.CreatedByName, "Digital Tech"),
                Timestamp = source.CreatedAtDisplay,
                Body = FirstNonEmpty(
                    source.AttachmentFileName,
                    "Archivo adjunto disponible."),
                Detail = "El contenido se mantiene en Dataverse."
            });
        }

        timeline.AddRange(interactions.Select(MapInteraction));

        if (!string.IsNullOrWhiteSpace(source.ExistingResolution))
        {
            timeline.Add(new MesaAyudaTimelineEventDto
            {
                Kind = "resolution",
                Tone = "success",
                Label = "Resolucion registrada",
                Actor = FirstNonEmpty(source.OwnerName, "Agente"),
                Timestamp = FirstNonEmpty(
                    source.LastActivityAtDisplay,
                    source.CreatedAtDisplay),
                Body = source.ExistingResolution.Trim(),
                Detail = "Resultado final del ticket."
            });
        }

        return new MesaAyudaTicketDto
        {
            RecordId = NormalizeGuid(source.RecordId),
            Reference = FirstNonEmpty(
                source.CaseNumber,
                BuildTransitionalReference(
                    source.RecordId,
                    source.CreatedAtValue)),
            ReferenceIsProvisional = string.IsNullOrWhiteSpace(
                source.CaseNumber),
            Title = FirstNonEmpty(source.Title, "Caso sin titulo"),
            Description = source.Description?.Trim() ?? "",
            ClientId = NormalizeGuid(source.ClientId),
            ClientName = FirstNonEmpty(
                source.ClientName,
                "Cliente sin confirmar"),
            Status = status,
            StatusKey = statusKey,
            StatusTone = ResolveStatusTone(statusKey),
            Channel = FirstNonEmpty(
                source.SourceChannel,
                "Registro actual"),
            Category = source.Category?.Trim() ?? "",
            Workload = FirstNonEmpty(
                source.Workload,
                source.AiClassification),
            CreatedAtDisplay = source.CreatedAtDisplay,
            LastActivityDisplay = FirstNonEmpty(
                source.LastActivityAtDisplay,
                source.CreatedAtDisplay),
            AssignedAgent = FirstNonEmpty(
                source.OwnerName,
                "Sin asignar"),
            TenantStatus = !string.IsNullOrWhiteSpace(source.TenantId)
                ? "Identidad confirmada"
                : !string.IsNullOrWhiteSpace(source.TenantRecordId)
                    ? "Tenant seleccionado; ID canonico pendiente"
                    : "Sin confirmar",
            TenantId = source.TenantId?.Trim() ?? "",
            ExistingResolution = source.ExistingResolution?.Trim() ?? "",
            HasAttachment = source.HasAttachment,
            AttachmentFileName =
                source.AttachmentFileName?.Trim() ?? "",
            Timeline = timeline
        };
    }

    private static MesaAyudaTimelineEventDto MapInteraction(
        MesaAyudaInteractionDto source)
    {
        var isAi = !string.IsNullOrWhiteSpace(source.ModelResponseId)
            || source.ActorName.Contains(
                "Auditor IA",
                StringComparison.OrdinalIgnoreCase)
            || source.Subject.Contains(
                "auditoria IA",
                StringComparison.OrdinalIgnoreCase);
        var detailParts = new List<string>();
        if (isAi)
        {
            if (!string.IsNullOrWhiteSpace(source.Classification))
                detailParts.Add($"Clasificacion: {source.Classification}");
            if (source.Confidence.HasValue)
            {
                detailParts.Add(
                    $"Confianza: {source.Confidence.Value:P0}");
            }

            detailParts.Add("Resultado persistido en Dataverse.");
        }
        else
        {
            detailParts.Add("Chat interno; no visible para el cliente.");
        }

        var investigation = isAi
            ? DeserializeInvestigation(source.StructuredJson)
            : null;
        return new MesaAyudaTimelineEventDto
        {
            Kind = isAi ? "audit" : "message",
            Tone = isAi ? "ai" : "neutral",
            Label = FirstNonEmpty(
                source.Subject,
                isAi ? "Auditoria IA" : "Mensaje interno"),
            Actor = FirstNonEmpty(
                source.ActorName,
                isAi ? "Auditor IA" : "Agente"),
            Timestamp = FormatTimelineTimestamp(source.EventAtUtc),
            Body = source.Content?.Trim() ?? "",
            Detail = string.Join(" · ", detailParts),
            Investigation = investigation
        };
    }

    private static MesaAyudaInvestigationResultDto? DeserializeInvestigation(
        string? structuredJson)
    {
        if (string.IsNullOrWhiteSpace(structuredJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MesaAyudaInvestigationResultDto>(
                structuredJson,
                PersistenceJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MesaAyudaTicketDto MapTicket(SoporteCloudTicketRowDto source)
    {
        var status = FirstNonEmpty(source.StateLabel, "Sin estado");
        var statusKey = ResolveStatusKey(status);
        var timeline = new List<MesaAyudaTimelineEventDto>
        {
            new()
            {
                Kind = "case-created",
                Tone = "neutral",
                Label = "Caso registrado",
                Actor = FirstNonEmpty(source.CreatorName, "Digital Tech"),
                Timestamp = FirstNonEmpty(source.CreationDateDisplay, source.CreationDateValue),
                Body = FirstNonEmpty(source.Description, "El registro no contiene una descripcion."),
                Detail = "Origen: tabla cr07a_ticket"
            }
        };

        if (source.HasAttachment)
        {
            timeline.Add(new MesaAyudaTimelineEventDto
            {
                Kind = "attachment",
                Tone = "info",
                Label = "Evidencia adjunta",
                Actor = FirstNonEmpty(source.CreatorName, "Digital Tech"),
                Timestamp = FirstNonEmpty(source.CreationDateDisplay, source.CreationDateValue),
                Body = FirstNonEmpty(source.AttachmentFileName, "Archivo adjunto disponible."),
                Detail = "El contenido se mantiene en Dataverse."
            });
        }

        if (!string.IsNullOrWhiteSpace(source.Solution))
        {
            timeline.Add(new MesaAyudaTimelineEventDto
            {
                Kind = "resolution",
                Tone = "success",
                Label = "Resolucion registrada",
                Actor = FirstNonEmpty(source.CreatorName, "Agente"),
                Timestamp = FirstNonEmpty(source.ModifiedOnDisplay, source.CreationDateDisplay),
                Body = source.Solution.Trim(),
                Detail = "Resultado existente del ticket."
            });
        }

        return new MesaAyudaTicketDto
        {
            RecordId = NormalizeGuid(source.RecordId),
            Reference = BuildTransitionalReference(source),
            ReferenceIsProvisional = true,
            Title = FirstNonEmpty(source.Title, "Caso sin titulo"),
            Description = source.Description?.Trim() ?? "",
            ClientId = NormalizeGuid(source.ClientId),
            ClientName = FirstNonEmpty(source.ClientName, "Cliente sin confirmar"),
            Status = status,
            StatusKey = statusKey,
            StatusTone = ResolveStatusTone(statusKey),
            Category = source.CategoryLabel?.Trim() ?? "",
            Workload = source.TypeLabel?.Trim() ?? "",
            CreatedAtDisplay = FirstNonEmpty(source.CreationDateDisplay, source.CreationDateValue),
            LastActivityDisplay = FirstNonEmpty(source.ModifiedOnDisplay, source.CreationDateDisplay),
            AssignedAgent = FirstNonEmpty(source.CreatorName, "Sin asignar"),
            ExistingResolution = source.Solution?.Trim() ?? "",
            HasAttachment = source.HasAttachment,
            AttachmentFileName = source.AttachmentFileName?.Trim() ?? "",
            Timeline = timeline
        };
    }

    private static IReadOnlyList<MesaAyudaQueueDto> BuildQueues(
        IReadOnlyList<MesaAyudaTicketDto> tickets)
    {
        int Count(string key) => tickets.Count(ticket =>
            string.Equals(ticket.StatusKey, key, StringComparison.OrdinalIgnoreCase));

        return
        [
            new MesaAyudaQueueDto { Key = "all", Label = "Todos los casos", Count = tickets.Count },
            new MesaAyudaQueueDto { Key = "new", Label = "Nuevos", Count = Count("new") },
            new MesaAyudaQueueDto { Key = "active", Label = "En curso", Count = Count("active") },
            new MesaAyudaQueueDto { Key = "waiting", Label = "En espera", Count = Count("waiting") },
            new MesaAyudaQueueDto { Key = "closed", Label = "Cerrados", Count = Count("closed") }
        ];
    }

    private static string ResolveStatusKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("cerrad", StringComparison.Ordinal)
            || normalized.Contains("resuelt", StringComparison.Ordinal)
            || normalized.Contains("finaliz", StringComparison.Ordinal))
        {
            return "closed";
        }

        if (normalized.Contains("esper", StringComparison.Ordinal)
            || normalized.Contains("pend", StringComparison.Ordinal)
            || normalized.Contains("bloque", StringComparison.Ordinal))
        {
            return "waiting";
        }

        if (normalized.Contains("nuevo", StringComparison.Ordinal)
            || normalized.Contains("recibid", StringComparison.Ordinal)
            || normalized.Contains("abiert", StringComparison.Ordinal))
        {
            return "new";
        }

        return "active";
    }

    private static string ResolveStatusTone(string statusKey) => statusKey switch
    {
        "closed" => "success",
        "waiting" => "warning",
        "new" => "info",
        _ => "active"
    };

    private static string BuildTransitionalReference(SoporteCloudTicketRowDto source)
        => BuildTransitionalReference(
            source.RecordId,
            source.CreationDateValue);

    private static string BuildTransitionalReference(
        string? recordId,
        string? creationDateValue)
    {
        var year = DateTime.UtcNow.Year;
        if (DateOnly.TryParse(
                creationDateValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            year = date.Year;
        }

        var compact = NormalizeGuid(recordId).Replace(
            "-",
            "",
            StringComparison.Ordinal);
        var suffix = compact.Length >= 7
            ? compact[..7].ToUpperInvariant()
            : compact.ToUpperInvariant();
        return $"TKT-{year}-{FirstNonEmpty(suffix, "LEGACY")}";
    }

    private static string FormatTimelineTimestamp(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToOffset(TimeSpan.FromHours(-5))
                .ToString(
                    "dd/MM/yyyy HH:mm",
                    CultureInfo.GetCultureInfo("es-CO"))
            : "";

    private static DateTime ParseSortDate(MesaAyudaTicketDto ticket)
    {
        if (DateTime.TryParse(
                ticket.LastActivityDisplay,
                CultureInfo.GetCultureInfo("es-CO"),
                DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
    }

    private static string NormalizeGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : value?.Trim() ?? "";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
