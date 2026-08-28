using System.Globalization;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string AgentLearningFeedbackSetName = "cr07a_agentlearningfeedbacks";
    private const string AgentLearningFeedbackIdField = "cr07a_agentlearningfeedbackid";
    private const string AgentLearningFeedbackNameField = "cr07a_name";
    private const string AgentLearningFeedbackQuestionField = "cr07a_question";
    private const string AgentLearningFeedbackAnswerField = "cr07a_answer";
    private const string AgentLearningFeedbackCategoryField = "cr07a_category";
    private const string AgentLearningFeedbackExpectedAnswerField = "cr07a_expectedanswer";
    private const string AgentLearningFeedbackNotesField = "cr07a_notes";
    private const string AgentLearningFeedbackStatusField = "cr07a_status";
    private const string AgentLearningFeedbackReviewNotesField = "cr07a_reviewnotes";
    private const string AgentLearningFeedbackSourcesJsonField = "cr07a_sourcesjson";
    private const string AgentLearningFeedbackContextJsonField = "cr07a_contextjson";
    private const string AgentLearningFeedbackCreatedByNameField = "cr07a_createdbyname";
    private const string AgentLearningFeedbackCreatedByEmailField = "cr07a_createdbyemail";
    private const string AgentLearningFeedbackCreatedByIdField = "cr07a_createdbyid";
    private const string AgentLearningFeedbackCreatedOnField = "createdon";
    private const string AgentLearningFeedbackFallbackFileName = "dashboard-agent-feedback.jsonl";
    private static readonly SemaphoreSlim AgentLearningFeedbackFileLock = new(1, 1);

    public async Task<DashboardAgentFeedbackResultDto> CreateDashboardAgentFeedbackAsync(
        DashboardAgentFeedbackRequestDto request,
        CancellationToken ct = default)
    {
        var normalized = NormalizeAgentLearningFeedbackRequest(request);
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await ResolveAgentLearningCurrentUserAsync(ct);

        try
        {
            var payload = new Dictionary<string, object?>
            {
                [AgentLearningFeedbackNameField] = TruncateAgentLearningText(normalized.Question, 120),
                [AgentLearningFeedbackQuestionField] = TruncateAgentLearningText(normalized.Question, 3900),
                [AgentLearningFeedbackAnswerField] = TruncateAgentLearningText(normalized.Answer, 3900),
                [AgentLearningFeedbackCategoryField] = normalized.Category,
                [AgentLearningFeedbackExpectedAnswerField] = TruncateAgentLearningText(normalized.ExpectedAnswer, 3900),
                [AgentLearningFeedbackNotesField] = TruncateAgentLearningText(normalized.Notes, 3900),
                [AgentLearningFeedbackStatusField] = "pending",
                [AgentLearningFeedbackSourcesJsonField] = TruncateAgentLearningText(SerializeAgentLearningJson(normalized.Sources), 9000),
                [AgentLearningFeedbackContextJsonField] = TruncateAgentLearningText(SerializeAgentLearningJson(normalized.ContextSummary), 9000),
                [AgentLearningFeedbackCreatedByNameField] = TruncateAgentLearningText(currentUser.DisplayName, 240),
                [AgentLearningFeedbackCreatedByEmailField] = TruncateAgentLearningText(currentUser.Email, 240),
                [AgentLearningFeedbackCreatedByIdField] = TruncateAgentLearningText(currentUser.SystemUserId, 80)
            };

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{AgentLearningFeedbackSetName}",
                "POST",
                payload,
                httpContext.User,
                ct);

            return new DashboardAgentFeedbackResultDto
            {
                Message = "Solicitud enviada a la bandeja de aprendizaje.",
                Storage = "dataverse"
            };
        }
        catch (Exception ex) when (ShouldFallbackAgentLearningFeedback(ex))
        {
            _logger.LogWarning(ex, "No fue posible guardar feedback del agente en Dataverse. Se usara respaldo local.");
            var fallbackId = await AppendAgentLearningFeedbackFallbackAsync(normalized, currentUser, ct);
            return new DashboardAgentFeedbackResultDto
            {
                FeedbackId = fallbackId,
                Message = "Solicitud enviada a aprendizaje. Se guardo en respaldo local porque Dataverse no acepto la tabla.",
                Storage = "local"
            };
        }
    }

    public async Task<DashboardAgentLearningBoardDto> GetDashboardAgentLearningFeedbackAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        try
        {
            var fields = new[]
            {
                AgentLearningFeedbackIdField,
                AgentLearningFeedbackCreatedOnField,
                AgentLearningFeedbackQuestionField,
                AgentLearningFeedbackAnswerField,
                AgentLearningFeedbackCategoryField,
                AgentLearningFeedbackExpectedAnswerField,
                AgentLearningFeedbackNotesField,
                AgentLearningFeedbackStatusField,
                AgentLearningFeedbackReviewNotesField,
                AgentLearningFeedbackSourcesJsonField,
                AgentLearningFeedbackContextJsonField,
                AgentLearningFeedbackCreatedByNameField,
                AgentLearningFeedbackCreatedByEmailField
            };
            var url = $"/api/data/v9.2/{AgentLearningFeedbackSetName}?$select={string.Join(",", fields)}&$orderby={AgentLearningFeedbackCreatedOnField} desc&$top=100";
            var items = await GetDataverseEntitiesAsync(url, httpContext.User, ct, AddFormattedValueHeaders);
            var rows = items
                .Select(ReadAgentLearningFeedbackRow)
                .Where(static row => !string.IsNullOrWhiteSpace(row.FeedbackId))
                .ToList();

            return new DashboardAgentLearningBoardDto
            {
                RecordsCount = rows.Count,
                Storage = "dataverse",
                Rows = rows
            };
        }
        catch (Exception ex) when (ShouldFallbackAgentLearningFeedback(ex))
        {
            _logger.LogWarning(ex, "No fue posible leer feedback del agente desde Dataverse. Se usara respaldo local.");
            var rows = await ReadAgentLearningFeedbackFallbackRowsAsync(ct);
            return new DashboardAgentLearningBoardDto
            {
                RecordsCount = rows.Count,
                Storage = "local",
                Rows = rows
            };
        }
    }

    public async Task<DashboardAgentFeedbackResultDto> UpdateDashboardAgentLearningFeedbackStatusAsync(
        DashboardAgentLearningStatusUpdateRequestDto request,
        CancellationToken ct = default)
    {
        var feedbackId = (request.FeedbackId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(feedbackId))
            throw new InvalidOperationException("Selecciona un registro de aprendizaje.");

        var status = NormalizeAgentLearningStatus(request.Status);
        var reviewNotes = (request.ReviewNotes ?? "").Trim();
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        try
        {
            var payload = new Dictionary<string, object?>
            {
                [AgentLearningFeedbackStatusField] = status,
                [AgentLearningFeedbackReviewNotesField] = TruncateAgentLearningText(reviewNotes, 3900)
            };
            var url = $"/api/data/v9.2/{AgentLearningFeedbackSetName}({Guid.Parse(feedbackId):D})";
            await CallDataverseSendAsync(url, "PATCH", payload, httpContext.User, ct);

            return new DashboardAgentFeedbackResultDto
            {
                FeedbackId = feedbackId,
                Message = "Estado de aprendizaje actualizado.",
                Storage = "dataverse"
            };
        }
        catch (Exception ex) when (ShouldFallbackAgentLearningFeedback(ex))
        {
            _logger.LogWarning(ex, "No fue posible actualizar feedback del agente en Dataverse. Se usara respaldo local.");
            await UpdateAgentLearningFeedbackFallbackAsync(feedbackId, status, reviewNotes, ct);
            return new DashboardAgentFeedbackResultDto
            {
                FeedbackId = feedbackId,
                Message = "Estado de aprendizaje actualizado en respaldo local.",
                Storage = "local"
            };
        }
    }

    private async Task<CurrentUserInfo> ResolveAgentLearningCurrentUserAsync(CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        try
        {
            return await GetCurrentUserAsync(ct)
                ?? BuildCurrentUserInfoFromClaims(httpContext.User);
        }
        catch (Exception ex) when (ShouldFallbackAgentLearningFeedback(ex))
        {
            _logger.LogWarning(ex, "No fue posible resolver el usuario completo para feedback del agente.");
            return BuildCurrentUserInfoFromClaims(httpContext.User);
        }
    }

    private static DashboardAgentFeedbackRequestDto NormalizeAgentLearningFeedbackRequest(DashboardAgentFeedbackRequestDto? request)
    {
        if (request is null)
            throw new InvalidOperationException("No se recibio la solicitud de aprendizaje.");

        var question = (request.Question ?? "").Trim();
        var answer = (request.Answer ?? "").Trim();
        if (string.IsNullOrWhiteSpace(question))
            throw new InvalidOperationException("La pregunta es obligatoria para aprendizaje.");

        return new DashboardAgentFeedbackRequestDto
        {
            Question = question,
            Answer = answer,
            Category = NormalizeAgentLearningCategory(request.Category),
            ExpectedAnswer = (request.ExpectedAnswer ?? "").Trim(),
            Notes = (request.Notes ?? "").Trim(),
            Sources = request.Sources ?? Array.Empty<DashboardAgentSourceDto>(),
            ContextSummary = request.ContextSummary
        };
    }

    private static string NormalizeAgentLearningCategory(string? category)
    {
        var value = (category ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "incorrect" => "incorrect",
            "missing-data" => "missing-data",
            "learning" => "learning",
            "other" => "other",
            _ => "learning"
        };
    }

    private static string NormalizeAgentLearningStatus(string? status)
    {
        var value = (status ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "pending" => "pending",
            "reviewed" => "reviewed",
            "implemented" => "implemented",
            "discarded" => "discarded",
            _ => "pending"
        };
    }

    private static DashboardAgentLearningFeedbackRowDto ReadAgentLearningFeedbackRow(JsonElement item)
    {
        var createdOnValue = ReadString(item, AgentLearningFeedbackCreatedOnField);
        return new DashboardAgentLearningFeedbackRowDto
        {
            FeedbackId = ReadString(item, AgentLearningFeedbackIdField),
            CreatedOnValue = createdOnValue,
            CreatedOnDisplay = FirstNonEmpty(
                ReadString(item, $"{AgentLearningFeedbackCreatedOnField}{FormattedValueAnnotationSuffix}"),
                FormatAgentLearningDate(createdOnValue),
                createdOnValue),
            Question = ReadString(item, AgentLearningFeedbackQuestionField),
            Answer = ReadString(item, AgentLearningFeedbackAnswerField),
            Category = NormalizeAgentLearningCategory(ReadString(item, AgentLearningFeedbackCategoryField)),
            ExpectedAnswer = ReadString(item, AgentLearningFeedbackExpectedAnswerField),
            Notes = ReadString(item, AgentLearningFeedbackNotesField),
            Status = NormalizeAgentLearningStatus(ReadString(item, AgentLearningFeedbackStatusField)),
            ReviewNotes = ReadString(item, AgentLearningFeedbackReviewNotesField),
            SourcesJson = ReadString(item, AgentLearningFeedbackSourcesJsonField),
            ContextSummaryJson = ReadString(item, AgentLearningFeedbackContextJsonField),
            CreatedByName = ReadString(item, AgentLearningFeedbackCreatedByNameField),
            CreatedByEmail = ReadString(item, AgentLearningFeedbackCreatedByEmailField)
        };
    }

    private static string FormatAgentLearningDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : "";
    }

    private static string SerializeAgentLearningJson(object? value)
    {
        if (value is null)
            return "";

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string TruncateAgentLearningText(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maxLength
            ? text
            : text[..maxLength];
    }

    private static bool ShouldFallbackAgentLearningFeedback(Exception ex)
    {
        return ex is not OperationCanceledException && !IsIncrementalConsentChallenge(ex);
    }

    private static string GetAgentLearningFeedbackFallbackFilePath()
    {
        var appDataPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, AgentLearningFeedbackFallbackFileName);
    }

    private static async Task<string> AppendAgentLearningFeedbackFallbackAsync(
        DashboardAgentFeedbackRequestDto request,
        CurrentUserInfo currentUser,
        CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        var row = new DashboardAgentLearningFeedbackRowDto
        {
            FeedbackId = id,
            CreatedOnValue = now.ToString("O", CultureInfo.InvariantCulture),
            CreatedOnDisplay = now.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            Question = request.Question,
            Answer = request.Answer,
            Category = request.Category,
            ExpectedAnswer = request.ExpectedAnswer,
            Notes = request.Notes,
            Status = "pending",
            SourcesJson = SerializeAgentLearningJson(request.Sources),
            ContextSummaryJson = SerializeAgentLearningJson(request.ContextSummary),
            CreatedByName = currentUser.DisplayName,
            CreatedByEmail = currentUser.Email
        };

        var line = JsonSerializer.Serialize(row, JsonOptions) + Environment.NewLine;
        await AgentLearningFeedbackFileLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(GetAgentLearningFeedbackFallbackFilePath(), line, Encoding.UTF8, ct);
        }
        finally
        {
            AgentLearningFeedbackFileLock.Release();
        }

        return id;
    }

    private static async Task<IReadOnlyList<DashboardAgentLearningFeedbackRowDto>> ReadAgentLearningFeedbackFallbackRowsAsync(CancellationToken ct)
    {
        var filePath = GetAgentLearningFeedbackFallbackFilePath();
        if (!File.Exists(filePath))
            return Array.Empty<DashboardAgentLearningFeedbackRowDto>();

        await AgentLearningFeedbackFileLock.WaitAsync(ct);
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8, ct);
            return lines
                .Select(TryParseAgentLearningFallbackRow)
                .Where(static row => row is not null)
                .Select(static row => row!)
                .OrderByDescending(static row => row.CreatedOnValue)
                .Take(100)
                .ToList();
        }
        finally
        {
            AgentLearningFeedbackFileLock.Release();
        }
    }

    private static async Task UpdateAgentLearningFeedbackFallbackAsync(
        string feedbackId,
        string status,
        string reviewNotes,
        CancellationToken ct)
    {
        var filePath = GetAgentLearningFeedbackFallbackFilePath();
        if (!File.Exists(filePath))
            throw new InvalidOperationException("No se encontro el registro de aprendizaje en respaldo local.");

        await AgentLearningFeedbackFileLock.WaitAsync(ct);
        try
        {
            var rows = (await File.ReadAllLinesAsync(filePath, Encoding.UTF8, ct))
                .Select(TryParseAgentLearningFallbackRow)
                .Where(static row => row is not null)
                .Select(static row => row!)
                .ToList();

            var row = rows.FirstOrDefault(item => string.Equals(item.FeedbackId, feedbackId, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                throw new InvalidOperationException("No se encontro el registro de aprendizaje.");

            row.Status = NormalizeAgentLearningStatus(status);
            row.ReviewNotes = (reviewNotes ?? "").Trim();

            var lines = rows.Select(item => JsonSerializer.Serialize(item, JsonOptions));
            await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8, ct);
        }
        finally
        {
            AgentLearningFeedbackFileLock.Release();
        }
    }

    private static DashboardAgentLearningFeedbackRowDto? TryParseAgentLearningFallbackRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DashboardAgentLearningFeedbackRowDto>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
