using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.SoporteCloud;

public sealed class LiveSurveySessionState
{
    [JsonIgnore]
    public object SyncRoot { get; } = new();
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string ClosingOperationId { get; set; } = "";
    public DateTimeOffset? ClosingStartedUtc { get; set; }
    public string Code { get; init; } = "";
    public string SessionName { get; set; } = "";
    public string TopicName { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public string Phase { get; set; } = "registration";
    public int CurrentQuestionIndex { get; set; } = -1;
    public string CurrentQuestionId { get; set; } = "";
    public string PendingPhase { get; set; } = "";
    public int PendingQuestionIndex { get; set; } = -1;
    public DateTimeOffset? QuestionStartedOnUtc { get; set; }
    public DateTimeOffset? RankingEndsOnUtc { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SoporteCloudSurveyQuestionDto> SatisfactionQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionDto> KnowledgeQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ConcurrentDictionary<string, LiveSurveyParticipantState> Participants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ConcurrentDictionary<string, DateTimeOffset> RemovedParticipants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LiveSurveyParticipantState
{
    [JsonIgnore]
    public object SyncRoot { get; } = new();
    public string ParticipantKey { get; init; } = "";
    public string FullName { get; set; } = "";
    public string Identification { get; set; } = "";
    public string Company { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Completed { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? WheelNumber { get; set; }
    public DateTimeOffset? WheelSpunAt { get; set; }
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ConcurrentDictionary<string, LiveSurveyAnswerState> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LiveSurveyAnswerState
{
    public string QuestionId { get; init; } = "";
    public string OptionId { get; set; } = "";
    public decimal? NumericValue { get; set; }
    public string TextValue { get; set; } = "";
    public decimal Points { get; set; }
    public decimal MaxPoints { get; set; }
    public bool IsCorrect { get; set; }
    public decimal ResponseSeconds { get; set; }
    public DateTimeOffset AnsweredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class LiveSurveySessionIndex
{
    public string Code { get; set; } = "";
}
