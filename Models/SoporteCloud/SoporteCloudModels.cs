using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.SoporteCloud;

public sealed class SoporteCloudPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string InitialStartDate { get; set; } = "";
    public string InitialEndDate { get; set; } = "";
}

public sealed class SoporteCloudOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class SoporteCloudTicketRowDto
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreationDateValue { get; set; } = "";
    public string CreationDateDisplay { get; set; } = "";
    public int? StateValue { get; set; }
    public string StateLabel { get; set; } = "";
    public int? TypeValue { get; set; }
    public string TypeLabel { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public string CategoryLabel { get; set; } = "";
    public string CreatorId { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public decimal HoursTaken { get; set; }
    public int? MethodValue { get; set; }
    public string MethodLabel { get; set; } = "";
    public string Solution { get; set; } = "";
    public bool HasAttachment { get; set; }
    public string AttachmentFileName { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class SoporteCloudCreatorSummaryDto
{
    public string CreatorId { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class SoporteCloudBreakdownDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class SoporteCloudBoardDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public int TotalTickets { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalCreators { get; set; }
    public int TotalClients { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<SoporteCloudTicketRowDto> Records { get; set; } = Array.Empty<SoporteCloudTicketRowDto>();
    public IReadOnlyList<SoporteCloudCreatorSummaryDto> CreatorSummaries { get; set; } = Array.Empty<SoporteCloudCreatorSummaryDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> TypeBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> MethodBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudBreakdownDto> CategoryBreakdowns { get; set; } = Array.Empty<SoporteCloudBreakdownDto>();
    public IReadOnlyList<SoporteCloudOptionDto> StateOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> TypeOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> CategoryOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
    public IReadOnlyList<SoporteCloudOptionDto> MethodOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
}

public sealed class SoporteCloudTrainingRowDto
{
    public string RecordId { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public decimal DurationMinutes { get; set; }
    public decimal DurationHours { get; set; }
    public string DurationDisplay { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int Attendees { get; set; }
    public int? TopicValue { get; set; }
    public string TopicLabel { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
}

public sealed class SoporteCloudTrainingOwnerSummaryDto
{
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
}

public sealed class SoporteCloudTrainingBreakdownDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class SoporteCloudTrainingTimePointDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHours { get; set; }
    public int TotalAttendees { get; set; }
}

public sealed class SoporteCloudTrainingsBoardDto
{
    public string StartDateValue { get; set; } = "";
    public string EndDateValue { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public int TotalTrainings { get; set; }
    public decimal TotalHoursDelivered { get; set; }
    public int TotalClients { get; set; }
    public int TotalAttendees { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<SoporteCloudTrainingRowDto> Records { get; set; } = Array.Empty<SoporteCloudTrainingRowDto>();
    public IReadOnlyList<SoporteCloudTrainingOwnerSummaryDto> OwnerSummaries { get; set; } = Array.Empty<SoporteCloudTrainingOwnerSummaryDto>();
    public IReadOnlyList<SoporteCloudTrainingBreakdownDto> TopicBreakdowns { get; set; } = Array.Empty<SoporteCloudTrainingBreakdownDto>();
    public IReadOnlyList<SoporteCloudTrainingBreakdownDto> ClientBreakdowns { get; set; } = Array.Empty<SoporteCloudTrainingBreakdownDto>();
    public IReadOnlyList<SoporteCloudTrainingTimePointDto> TimeSeries { get; set; } = Array.Empty<SoporteCloudTrainingTimePointDto>();
    public IReadOnlyList<SoporteCloudOptionDto> TopicOptions { get; set; } = Array.Empty<SoporteCloudOptionDto>();
}

public sealed class SoporteCloudSaveRequest
{
    public string RecordId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreationDateValue { get; set; } = "";
    public int? StateValue { get; set; }
    public int? TypeValue { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int? CategoryValue { get; set; }
    public decimal HoursTaken { get; set; }
    public int? MethodValue { get; set; }
    public string Solution { get; set; } = "";
}

public sealed class SoporteCloudSaveResultDto
{
    public string Message { get; set; } = "";
    public SoporteCloudTicketRowDto Record { get; set; } = new();
}

public sealed class SoporteCloudFileUploadResultDto
{
    public string Message { get; set; } = "";
    public SoporteCloudTicketRowDto Record { get; set; } = new();
}

public sealed class SoporteCloudFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class SoporteCloudSurveyOptionDto
{
    public string OptionId { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public decimal Points { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SoporteCloudSurveyTopicDto
{
    public string TopicId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int KnowledgeQuestionCount { get; set; }
}

public sealed class SoporteCloudSurveyQuestionDto
{
    public string QuestionId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string TopicName { get; set; } = "";
    public int ComponentValue { get; set; }
    public string ComponentLabel { get; set; } = "";
    public int InputTypeValue { get; set; }
    public string InputTypeLabel { get; set; } = "";
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public decimal MaxPoints { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public IReadOnlyList<SoporteCloudSurveyOptionDto> Options { get; set; } = Array.Empty<SoporteCloudSurveyOptionDto>();
}

public sealed class SoporteCloudSurveySessionDto
{
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string TopicName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public int StateValue { get; set; }
    public string StateLabel { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public int ScanCount { get; set; }
    public int RegisteredCount { get; set; }
    public int CompletedCount { get; set; }
    public decimal AverageScore { get; set; }
    public decimal AverageScorePercent { get; set; }
    public decimal AverageSatisfaction { get; set; }
}

public sealed class SoporteCloudSurveyParticipantDto
{
    public string ParticipantId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public string SubmittedOnDisplay { get; set; } = "";
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ScorePercent { get; set; }
}

public sealed class SoporteCloudSurveyQuestionStatsDto
{
    public string QuestionId { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public int ComponentValue { get; set; }
    public int TotalAnswers { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public decimal AveragePoints { get; set; }
    public decimal CorrectPercent { get; set; }
    public decimal AverageRating { get; set; }
}

public sealed class SoporteCloudSurveyParticipantAnswerDto
{
    public string ParticipantId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public string AnswerText { get; set; } = "";
    public bool IsCorrect { get; set; }
    public decimal Points { get; set; }
}

public sealed class SoporteCloudSurveySessionDetailDto
{
    public SoporteCloudSurveySessionDto Session { get; set; } = new();
    public IReadOnlyList<SoporteCloudSurveyParticipantDto> Participants { get; set; } = Array.Empty<SoporteCloudSurveyParticipantDto>();
    public IReadOnlyList<SoporteCloudSurveyParticipantDto> Leaderboard { get; set; } = Array.Empty<SoporteCloudSurveyParticipantDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> KnowledgeQuestionStats { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> SatisfactionQuestionStats { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
    public IReadOnlyList<SoporteCloudSurveyParticipantAnswerDto> KnowledgeAnswers { get; set; } = Array.Empty<SoporteCloudSurveyParticipantAnswerDto>();
}

public sealed class SoporteCloudSurveyBoardDto
{
    public string Message { get; set; } = "";
    public int TotalSessions { get; set; }
    public int OpenSessions { get; set; }
    public int TotalResponses { get; set; }
    public decimal AverageScorePercent { get; set; }
    public IReadOnlyList<SoporteCloudSurveyTopicDto> Topics { get; set; } = Array.Empty<SoporteCloudSurveyTopicDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionDto> Questions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
    public IReadOnlyList<SoporteCloudSurveySessionDto> Sessions { get; set; } = Array.Empty<SoporteCloudSurveySessionDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> BestQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> WeakQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
}

public sealed class SoporteCloudSurveyTopicSaveRequest
{
    public string TopicId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class SoporteCloudSurveyQuestionSaveRequest
{
    public string QuestionId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public int ComponentValue { get; set; }
    public int InputTypeValue { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public decimal MaxPoints { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SoporteCloudSurveyOptionDto> Options { get; set; } = Array.Empty<SoporteCloudSurveyOptionDto>();
}

public sealed class SoporteCloudSurveySessionSaveRequest
{
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string DateValue { get; set; } = "";
}

public sealed class SoporteCloudSurveyAnswerSubmitDto
{
    public string QuestionId { get; set; } = "";
    public string OptionId { get; set; } = "";
    public decimal? NumericValue { get; set; }
    public string TextValue { get; set; } = "";
}

public sealed class SoporteCloudSurveySubmitRequest
{
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public IReadOnlyList<SoporteCloudSurveyAnswerSubmitDto> Answers { get; set; } = Array.Empty<SoporteCloudSurveyAnswerSubmitDto>();
}

public sealed class SoporteCloudSurveySaveResultDto
{
    public string Message { get; set; } = "";
    public SoporteCloudSurveyBoardDto Board { get; set; } = new();
}

public sealed class SoporteCloudSurveySubmitResultDto
{
    public string Message { get; set; } = "";
    public bool IsClosed { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ScorePercent { get; set; }
    public IReadOnlyList<SoporteCloudSurveyParticipantDto> Leaderboard { get; set; } = Array.Empty<SoporteCloudSurveyParticipantDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> QuestionStats { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
}

public sealed class SoporteCloudPublicSurveyViewModel
{
    public string Code { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SessionName { get; set; } = "";
    public string TopicName { get; set; } = "";
    public bool IsClosed { get; set; }
    public string Message { get; set; } = "";
    public IReadOnlyList<SoporteCloudSurveyQuestionDto> KnowledgeQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionDto> SatisfactionQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
    public IReadOnlyList<SoporteCloudSurveyParticipantDto> Leaderboard { get; set; } = Array.Empty<SoporteCloudSurveyParticipantDto>();
    public IReadOnlyList<SoporteCloudSurveyQuestionStatsDto> QuestionStats { get; set; } = Array.Empty<SoporteCloudSurveyQuestionStatsDto>();
}
