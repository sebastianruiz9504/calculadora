using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.SoporteCloud;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string SurveyTopicLogicalName = "cr07a_capacitaciontema";
    private const string SurveyTopicFallbackEntitySetName = "cr07a_capacitaciontemas";
    private const string SurveyTopicFallbackIdField = "cr07a_capacitaciontemaid";
    private const string SurveyTopicPrimaryNameField = "cr07a_name";
    private const string SurveyTopicDescriptionField = "cr07a_descripcion";
    private const string SurveyTopicActiveField = "cr07a_activo";

    private const string SurveyQuestionLogicalName = "cr07a_capacitacionpregunta";
    private const string SurveyQuestionFallbackEntitySetName = "cr07a_capacitacionpreguntas";
    private const string SurveyQuestionFallbackIdField = "cr07a_capacitacionpreguntaid";
    private const string SurveyQuestionPrimaryNameField = "cr07a_name";
    private const string SurveyQuestionTopicField = "cr07a_tema";
    private const string SurveyQuestionComponentField = "cr07a_componente";
    private const string SurveyQuestionInputTypeField = "cr07a_tiporespuesta";
    private const string SurveyQuestionTextField = "cr07a_pregunta";
    private const string SurveyQuestionSortOrderField = "cr07a_orden";
    private const string SurveyQuestionMaxPointsField = "cr07a_puntajemaximo";
    private const string SurveyQuestionActiveField = "cr07a_activa";

    private const string SurveyOptionLogicalName = "cr07a_capacitacionopcion";
    private const string SurveyOptionFallbackEntitySetName = "cr07a_capacitacionopcions";
    private const string SurveyOptionFallbackIdField = "cr07a_capacitacionopcionid";
    private const string SurveyOptionPrimaryNameField = "cr07a_name";
    private const string SurveyOptionQuestionField = "cr07a_pregunta";
    private const string SurveyOptionTextField = "cr07a_opcion";
    private const string SurveyOptionCorrectField = "cr07a_escorrecta";
    private const string SurveyOptionPointsField = "cr07a_puntos";
    private const string SurveyOptionSortOrderField = "cr07a_orden";
    private const string SurveyOptionActiveField = "cr07a_activa";

    private const string SurveySessionLogicalName = "cr07a_capacitacionsesion";
    private const string SurveySessionFallbackEntitySetName = "cr07a_capacitacionsesions";
    private const string SurveySessionFallbackIdField = "cr07a_capacitacionsesionid";
    private const string SurveySessionPrimaryNameField = "cr07a_name";
    private const string SurveySessionTopicField = "cr07a_tema";
    private const string SurveySessionClientField = "cr07a_cliente";
    private const string SurveySessionDateField = "cr07a_fecha";
    private const string SurveySessionCodeField = "cr07a_codigo";
    private const string SurveySessionStateField = "cr07a_estado";
    private const string SurveySessionClosedOnField = "cr07a_cerradaen";
    private const string SurveySessionCreatedOnField = "createdon";
    private const string SurveySessionScanCountField = "cr07a_escaneos";

    private const string SurveyParticipantLogicalName = "cr07a_capacitacionparticipante";
    private const string SurveyParticipantFallbackEntitySetName = "cr07a_capacitacionparticipantes";
    private const string SurveyParticipantFallbackIdField = "cr07a_capacitacionparticipanteid";
    private const string SurveyParticipantPrimaryNameField = "cr07a_name";
    private const string SurveyParticipantSessionField = "cr07a_sesion";
    private const string SurveyParticipantEmailField = "cr07a_email";
    private const string SurveyParticipantCompanyField = "cr07a_empresa";
    private const string SurveyParticipantScoreField = "cr07a_puntaje";
    private const string SurveyParticipantMaxScoreField = "cr07a_puntajemaximo";
    private const string SurveyParticipantScorePercentField = "cr07a_porcentaje";
    private const string SurveyParticipantSubmittedOnField = "cr07a_respondidaen";

    private const string SurveyAnswerLogicalName = "cr07a_capacitacionrespuesta";
    private const string SurveyAnswerFallbackEntitySetName = "cr07a_capacitacionrespuestas";
    private const string SurveyAnswerFallbackIdField = "cr07a_capacitacionrespuestaid";
    private const string SurveyAnswerPrimaryNameField = "cr07a_name";
    private const string SurveyAnswerSessionField = "cr07a_sesion";
    private const string SurveyAnswerParticipantField = "cr07a_participante";
    private const string SurveyAnswerQuestionField = "cr07a_pregunta";
    private const string SurveyAnswerOptionField = "cr07a_opcion";
    private const string SurveyAnswerComponentField = "cr07a_componente";
    private const string SurveyAnswerPointsField = "cr07a_puntos";
    private const string SurveyAnswerMaxPointsField = "cr07a_puntajemaximo";
    private const string SurveyAnswerCorrectField = "cr07a_correcta";
    private const string SurveyAnswerNumericValueField = "cr07a_valornumerico";
    private const string SurveyAnswerTextValueField = "cr07a_respuestatexto";
    private const string SurveyAnswerSubmittedOnField = "cr07a_respondidaen";

    private const int SurveyComponentKnowledge = 645250000;
    private const int SurveyComponentSatisfaction = 645250001;
    private const int SurveyInputSingleChoice = 645250000;
    private const int SurveyInputRating = 645250001;
    private const int SurveyInputText = 645250002;
    private const int SurveySessionStateOpen = 645250001;
    private const int SurveySessionStateClosed = 645250002;
    private const string SurveySatisfactionTopicName = "Satisfaccion";
    private const string SurveySatisfactionTopicDescription = "Tema fijo para todas las sesiones de capacitacion.";

    private static readonly IReadOnlyDictionary<int, string> SurveyComponentLabels = new Dictionary<int, string>
    {
        [SurveyComponentKnowledge] = "Preguntas de conocimiento",
        [SurveyComponentSatisfaction] = "Satisfaccion"
    };

    private static readonly IReadOnlyDictionary<int, string> SurveyInputTypeLabels = new Dictionary<int, string>
    {
        [SurveyInputSingleChoice] = "Seleccion unica",
        [SurveyInputRating] = "Escala 1 a 5",
        [SurveyInputText] = "Texto abierto"
    };

    private static readonly IReadOnlyDictionary<int, string> SurveySessionStateLabels = new Dictionary<int, string>
    {
        [SurveySessionStateOpen] = "Abierta",
        [SurveySessionStateClosed] = "Cerrada"
    };

    private static readonly IReadOnlyList<SurveySatisfactionQuestionSeed> SurveySatisfactionQuestionSeeds =
        new[]
        {
            new SurveySatisfactionQuestionSeed("Nombre Completo", SurveyInputText, 1),
            new SurveySatisfactionQuestionSeed("Empresa", SurveyInputText, 2),
            new SurveySatisfactionQuestionSeed("De 1 a 5 cuanto califica la sesion", SurveyInputRating, 3),
            new SurveySatisfactionQuestionSeed("Comentarios y sugerencias", SurveyInputText, 4)
        };

    public async Task<SoporteCloudSurveyBoardDto> GetSoporteCloudSurveyBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        await EnsureSurveySatisfactionDefaultsAsync(metadata, httpContext.User, ct);
        var context = await LoadSurveyContextAsync(metadata, httpContext.User, ct);
        return BuildSurveyBoard(context, BuildSurveyPublicUrl);
    }

    public async Task<SoporteCloudSurveySessionDetailDto> GetSoporteCloudSurveySessionDetailAsync(string sessionId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedSessionId = NormalizeGuid(sessionId, nameof(sessionId));
        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        var context = await LoadSurveyContextAsync(metadata, httpContext.User, ct);
        var session = context.Sessions.FirstOrDefault(item => string.Equals(item.SessionId, normalizedSessionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontramos la sesion de encuesta solicitada.");

        return BuildSessionDetail(session, context, BuildSurveyPublicUrl);
    }

    public async Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveyTopicAsync(
        SoporteCloudSurveyTopicSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Debes indicar el nombre del tema.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        var topicId = NormalizeOptionalGuid(request.TopicId);
        var existingTopics = await LoadSurveyTopicsAsync(metadata, httpContext.User, ct);
        var existingTopic = string.IsNullOrWhiteSpace(topicId)
            ? null
            : existingTopics.FirstOrDefault(item => string.Equals(item.TopicId, topicId, StringComparison.OrdinalIgnoreCase));
        if (IsSatisfactionTopicName(name) || (existingTopic is not null && IsSatisfactionTopic(existingTopic)))
            throw new InvalidOperationException("El tema Satisfaccion es fijo y no se puede editar.");

        var isCreate = string.IsNullOrWhiteSpace(topicId);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Topic.PrimaryNameField] = name,
            [SurveyTopicDescriptionField] = (request.Description ?? "").Trim(),
            [SurveyTopicActiveField] = request.IsActive
        };

        await CallDataverseSendAsync(
            isCreate
                ? $"/api/data/v9.2/{metadata.Topic.EntitySetName}"
                : $"/api/data/v9.2/{metadata.Topic.EntitySetName}({topicId})",
            isCreate ? "POST" : "PATCH",
            payload,
            httpContext.User,
            ct);

        return new SoporteCloudSurveySaveResultDto
        {
            Message = isCreate ? "Tema creado correctamente." : "Tema actualizado correctamente.",
            Board = await GetSoporteCloudSurveyBoardAsync(ct)
        };
    }

    public async Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveyQuestionAsync(
        SoporteCloudSurveyQuestionSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var text = (request.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Debes escribir la pregunta.");

        if (request.ComponentValue == SurveyComponentKnowledge && string.IsNullOrWhiteSpace(NormalizeOptionalGuid(request.TopicId)))
            throw new InvalidOperationException("Las preguntas de conocimiento deben estar asociadas a un tema.");

        if (request.InputTypeValue == SurveyInputSingleChoice
            && request.Options.Count(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Text)) < 2)
        {
            throw new InvalidOperationException("Las preguntas de seleccion unica deben tener al menos dos opciones activas.");
        }

        if (request.ComponentValue == SurveyComponentKnowledge
            && request.InputTypeValue == SurveyInputSingleChoice
            && !request.Options.Any(item => item.IsActive && item.IsCorrect && !string.IsNullOrWhiteSpace(item.Text)))
        {
            throw new InvalidOperationException("Las preguntas de conocimiento deben tener al menos una opcion correcta.");
        }

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        var questionId = NormalizeOptionalGuid(request.QuestionId);
        var existingQuestion = string.IsNullOrWhiteSpace(questionId)
            ? null
            : (await LoadSurveyQuestionsAsync(metadata, httpContext.User, ct))
                .FirstOrDefault(item => string.Equals(item.QuestionId, questionId, StringComparison.OrdinalIgnoreCase));
        if (request.ComponentValue == SurveyComponentSatisfaction
            || existingQuestion?.ComponentValue == SurveyComponentSatisfaction)
        {
            throw new InvalidOperationException("Las preguntas de Satisfaccion son fijas y no se pueden editar.");
        }

        var requestedTopicId = NormalizeOptionalGuid(request.TopicId);
        if (!string.IsNullOrWhiteSpace(requestedTopicId))
        {
            var topic = (await LoadSurveyTopicsAsync(metadata, httpContext.User, ct))
                .FirstOrDefault(item => string.Equals(item.TopicId, requestedTopicId, StringComparison.OrdinalIgnoreCase));
            if (topic is not null && IsSatisfactionTopic(topic))
                throw new InvalidOperationException("El tema Satisfaccion es fijo y no acepta preguntas editables.");
        }

        var isCreate = string.IsNullOrWhiteSpace(questionId);
        var component = NormalizeSurveyComponent(request.ComponentValue);
        var inputType = NormalizeSurveyInputType(request.InputTypeValue);
        var maxPoints = component == SurveyComponentKnowledge
            ? Math.Max(RoundCurrency(request.MaxPoints), 0m)
            : 0m;
        if (component == SurveyComponentKnowledge && maxPoints <= 0m)
            maxPoints = 1m;

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Question.PrimaryNameField] = TruncateSurveyName(text),
            [SurveyQuestionTextField] = text,
            [SurveyQuestionComponentField] = component,
            [SurveyQuestionInputTypeField] = inputType,
            [SurveyQuestionSortOrderField] = Math.Max(request.SortOrder, 0),
            [SurveyQuestionMaxPointsField] = maxPoints,
            [SurveyQuestionActiveField] = request.IsActive
        };

        if (component == SurveyComponentKnowledge)
        {
            payload[$"{metadata.QuestionTopicNavigationProperty}@odata.bind"] =
                $"/{metadata.Topic.EntitySetName}({NormalizeGuid(request.TopicId, nameof(request.TopicId))})";
        }
        else if (!isCreate)
        {
            payload[$"{metadata.QuestionTopicNavigationProperty}@odata.bind"] = null;
        }

        if (isCreate)
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await CallRhDataverseResponseAsync(
                $"/api/data/v9.2/{metadata.Question.EntitySetName}",
                "POST",
                httpContext.User,
                ct,
                content,
                AddRhReturnRepresentationHeaders);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

            questionId = ExtractRhRecordId(response, body, metadata.Question.PrimaryIdField);
        }
        else
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.Question.EntitySetName}({questionId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        if (inputType == SurveyInputSingleChoice)
            await SaveSurveyOptionsAsync(metadata, questionId, request.Options, httpContext.User, ct);

        return new SoporteCloudSurveySaveResultDto
        {
            Message = isCreate ? "Pregunta creada correctamente." : "Pregunta actualizada correctamente.",
            Board = await GetSoporteCloudSurveyBoardAsync(ct)
        };
    }

    public async Task<SoporteCloudSurveySaveResultDto> SaveSoporteCloudSurveySessionAsync(
        SoporteCloudSurveySessionSaveRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Debes indicar el nombre de la sesion.");

        var topicId = NormalizeGuid(request.TopicId, nameof(request.TopicId));
        var sessionDate = ParseSurveyDate(request.DateValue) ?? DateOnly.FromDateTime(GetSurveyBogotaNow().DateTime);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        var topics = await LoadSurveyTopicsAsync(metadata, httpContext.User, ct);
        var selectedTopic = topics.FirstOrDefault(item => string.Equals(item.TopicId, topicId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No encontramos el tema seleccionado.");
        if (IsSatisfactionTopic(selectedTopic))
            throw new InvalidOperationException("Selecciona un tema de conocimiento. Satisfaccion se agrega automaticamente a todas las sesiones.");
        if (!selectedTopic.IsActive)
            throw new InvalidOperationException("El tema seleccionado esta inactivo.");

        var topicQuestions = await LoadSurveyQuestionsAsync(metadata, httpContext.User, ct);
        var hasKnowledgeQuestions = topicQuestions.Any(question =>
            question.ComponentValue == SurveyComponentKnowledge
            && question.IsActive
            && string.Equals(question.TopicId, topicId, StringComparison.OrdinalIgnoreCase));
        if (!hasKnowledgeQuestions)
            throw new InvalidOperationException("El tema seleccionado debe tener al menos una pregunta activa de conocimiento.");

        var sessionId = NormalizeOptionalGuid(request.SessionId);
        var isCreate = string.IsNullOrWhiteSpace(sessionId);
        var clientId = FirstNonEmpty(
            NormalizeOptionalGuid(request.ClientId),
            string.IsNullOrWhiteSpace(request.ClientName) ? "" : await ResolveSoporteCloudClientIdAsync(request.ClientName, ct));

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Session.PrimaryNameField] = name,
            [SurveySessionDateField] = sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [SurveySessionStateField] = SurveySessionStateOpen
        };

        if (isCreate)
        {
            payload[SurveySessionCodeField] = GenerateSurveyCode();
            if (metadata.HasSessionScanCountField)
                payload[SurveySessionScanCountField] = 0;
        }

        payload[$"{metadata.SessionTopicNavigationProperty}@odata.bind"] =
            $"/{metadata.Topic.EntitySetName}({topicId})";

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            payload[$"{metadata.SessionClientNavigationProperty}@odata.bind"] =
                $"/{ClientsEntitySetName}({NormalizeGuid(clientId, nameof(request.ClientId))})";
        }
        else if (!isCreate)
        {
            payload[$"{metadata.SessionClientNavigationProperty}@odata.bind"] = null;
        }

        await CallDataverseSendAsync(
            isCreate
                ? $"/api/data/v9.2/{metadata.Session.EntitySetName}"
                : $"/api/data/v9.2/{metadata.Session.EntitySetName}({sessionId})",
            isCreate ? "POST" : "PATCH",
            payload,
            httpContext.User,
            ct);

        return new SoporteCloudSurveySaveResultDto
        {
            Message = isCreate ? "Sesion creada y QR habilitado." : "Sesion actualizada correctamente.",
            Board = await GetSoporteCloudSurveyBoardAsync(ct)
        };
    }

    public async Task<SoporteCloudSurveySaveResultDto> CloseSoporteCloudSurveySessionAsync(string sessionId, CancellationToken ct = default)
    {
        var normalizedSessionId = NormalizeGuid(sessionId, nameof(sessionId));
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var metadata = await ResolveSoporteCloudSurveyMetadataAsync(httpContext.User, ct);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SurveySessionStateField] = SurveySessionStateClosed,
            [SurveySessionClosedOnField] = FormatSurveyDateTime(GetSurveyBogotaNow())
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.Session.EntitySetName}({normalizedSessionId})",
            "PATCH",
            payload,
            httpContext.User,
            ct);

        return new SoporteCloudSurveySaveResultDto
        {
            Message = "Sesion cerrada. Los resultados publicos ya pueden verse desde el QR.",
            Board = await GetSoporteCloudSurveyBoardAsync(ct)
        };
    }

    public async Task<SoporteCloudPublicSurveyViewModel> GetSoporteCloudPublicSurveyAsync(string code, CancellationToken ct = default)
    {
        var metadata = await ResolveSoporteCloudSurveyAppMetadataAsync(ct);
        var context = await LoadPublicSurveyContextAsync(metadata, code, ct);
        var session = context.Sessions.FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos una encuesta activa para el codigo indicado.");
        var isClosed = session.StateValue == SurveySessionStateClosed;
        if (!isClosed)
            await TrackSurveyScanAsync(metadata, session, ct);

        var detail = BuildSessionDetail(session, context, codeValue => BuildSurveyPublicUrl(codeValue));

        return new SoporteCloudPublicSurveyViewModel
        {
            Code = session.Code,
            SessionId = session.SessionId,
            SessionName = session.Name,
            TopicName = session.TopicName,
            IsClosed = isClosed,
            Message = isClosed
                ? "La encuesta ya fue cerrada. Estos son los resultados de conocimiento de la sesion."
                : "Completa las preguntas de conocimiento y la encuesta de satisfaccion.",
            KnowledgeQuestions = context.Questions
                .Where(question => question.ComponentValue == SurveyComponentKnowledge
                    && question.IsActive
                    && string.Equals(question.TopicId, session.TopicId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(question => question.SortOrder)
                .ThenBy(question => question.Text, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SatisfactionQuestions = context.Questions
                .Where(question => question.ComponentValue == SurveyComponentSatisfaction && question.IsActive)
                .OrderBy(question => question.SortOrder)
                .ThenBy(question => question.Text, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Leaderboard = isClosed ? detail.Leaderboard : Array.Empty<SoporteCloudSurveyParticipantDto>(),
            QuestionStats = isClosed ? detail.KnowledgeQuestionStats : Array.Empty<SoporteCloudSurveyQuestionStatsDto>()
        };
    }

    public async Task<SoporteCloudSurveySubmitResultDto> SubmitSoporteCloudPublicSurveyAsync(
        SoporteCloudSurveySubmitRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var fullName = (request.FullName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            throw new InvalidOperationException("Debes indicar tu nombre.");

        var metadata = await ResolveSoporteCloudSurveyAppMetadataAsync(ct);
        var context = await LoadPublicSurveyContextAsync(metadata, request.Code, ct);
        var session = context.Sessions.FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos una encuesta para el codigo indicado.");
        if (session.StateValue == SurveySessionStateClosed)
            throw new InvalidOperationException("La encuesta ya fue cerrada.");

        var questions = context.Questions
            .Where(question => question.IsActive
                && (question.ComponentValue == SurveyComponentSatisfaction
                    || string.Equals(question.TopicId, session.TopicId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var answersByQuestion = request.Answers
            .Where(answer => !string.IsNullOrWhiteSpace(answer.QuestionId))
            .GroupBy(answer => NormalizeOptionalGuid(answer.QuestionId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var computedAnswers = new List<SurveyComputedAnswer>();
        foreach (var question in questions)
        {
            if (!answersByQuestion.TryGetValue(question.QuestionId, out var answer))
                throw new InvalidOperationException($"Falta responder: {question.Text}");

            computedAnswers.Add(ComputeSurveyAnswer(question, answer));
        }

        var knowledgeAnswers = computedAnswers
            .Where(answer => answer.Question.ComponentValue == SurveyComponentKnowledge)
            .ToList();
        var score = RoundCurrency(knowledgeAnswers.Sum(answer => answer.Points));
        var maxScore = RoundCurrency(questions
            .Where(question => question.ComponentValue == SurveyComponentKnowledge)
            .Sum(question => question.MaxPoints));
        var percent = maxScore <= 0m
            ? 0m
            : Math.Round((score * 100m) / maxScore, 2, MidpointRounding.AwayFromZero);

        var participantId = await CreateSurveyParticipantAsync(metadata, session, request, fullName, score, maxScore, percent, ct);
        foreach (var answer in computedAnswers)
        {
            await CreateSurveyAnswerAsync(metadata, session, participantId, answer, ct);
        }

        return new SoporteCloudSurveySubmitResultDto
        {
            Message = "Respuestas guardadas. Los resultados se publicaran cuando el instructor cierre la sesion.",
            IsClosed = false,
            Score = score,
            MaxScore = maxScore,
            ScorePercent = percent
        };
    }

    public async Task<SoporteCloudSurveySessionDetailDto> GetSoporteCloudPublicSurveyResultsAsync(string code, CancellationToken ct = default)
    {
        var metadata = await ResolveSoporteCloudSurveyAppMetadataAsync(ct);
        var context = await LoadPublicSurveyContextAsync(metadata, code, ct);
        var session = context.Sessions.FirstOrDefault()
            ?? throw new InvalidOperationException("No encontramos una encuesta para el codigo indicado.");
        if (session.StateValue != SurveySessionStateClosed)
            throw new InvalidOperationException("Los resultados estaran disponibles cuando el instructor cierre la sesion.");

        var detail = BuildSessionDetail(session, context, codeValue => BuildSurveyPublicUrl(codeValue));
        return new SoporteCloudSurveySessionDetailDto
        {
            Session = detail.Session,
            Leaderboard = detail.Leaderboard,
            KnowledgeQuestionStats = detail.KnowledgeQuestionStats
        };
    }

    private async Task SaveSurveyOptionsAsync(
        SoporteCloudSurveyMetadata metadata,
        string questionId,
        IReadOnlyList<SoporteCloudSurveyOptionDto> options,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var existingOptions = await LoadSurveyOptionsAsync(metadata, user, ct);
        var existingForQuestion = existingOptions
            .Where(option => string.Equals(option.QuestionId, questionId, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(option => option.OptionId, StringComparer.OrdinalIgnoreCase);
        var submittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var option in options.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            order++;
            var optionId = NormalizeOptionalGuid(option.OptionId);
            var isCreate = string.IsNullOrWhiteSpace(optionId) || !existingForQuestion.ContainsKey(optionId);
            if (!isCreate)
                submittedIds.Add(optionId);

            var points = option.IsCorrect && option.Points <= 0m ? 1m : Math.Max(RoundCurrency(option.Points), 0m);
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [metadata.Option.PrimaryNameField] = TruncateSurveyName(option.Text),
                [SurveyOptionTextField] = option.Text.Trim(),
                [SurveyOptionCorrectField] = option.IsCorrect,
                [SurveyOptionPointsField] = points,
                [SurveyOptionSortOrderField] = option.SortOrder > 0 ? option.SortOrder : order,
                [SurveyOptionActiveField] = option.IsActive
            };
            payload[$"{metadata.OptionQuestionNavigationProperty}@odata.bind"] =
                $"/{metadata.Question.EntitySetName}({questionId})";

            await CallDataverseSendAsync(
                isCreate
                    ? $"/api/data/v9.2/{metadata.Option.EntitySetName}"
                    : $"/api/data/v9.2/{metadata.Option.EntitySetName}({optionId})",
                isCreate ? "POST" : "PATCH",
                payload,
                user,
                ct);
        }

        foreach (var option in existingForQuestion.Values.Where(item => !submittedIds.Contains(item.OptionId)))
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.Option.EntitySetName}({option.OptionId})",
                "PATCH",
                new Dictionary<string, object?> { [SurveyOptionActiveField] = false },
                user,
                ct);
        }
    }

    private async Task<string> CreateSurveyParticipantAsync(
        SoporteCloudSurveyMetadata metadata,
        SoporteCloudSurveySessionDto session,
        SoporteCloudSurveySubmitRequest request,
        string fullName,
        decimal score,
        decimal maxScore,
        decimal percent,
        CancellationToken ct)
    {
        var now = GetSurveyBogotaNow();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Participant.PrimaryNameField] = fullName,
            [SurveyParticipantEmailField] = (request.Email ?? "").Trim(),
            [SurveyParticipantCompanyField] = (request.Company ?? "").Trim(),
            [SurveyParticipantScoreField] = score,
            [SurveyParticipantMaxScoreField] = maxScore,
            [SurveyParticipantScorePercentField] = percent,
            [SurveyParticipantSubmittedOnField] = FormatSurveyDateTime(now)
        };
        payload[$"{metadata.ParticipantSessionNavigationProperty}@odata.bind"] =
            $"/{metadata.Session.EntitySetName}({session.SessionId})";

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallDataverseAppResponseAsync(
            $"/api/data/v9.2/{metadata.Participant.EntitySetName}",
            "POST",
            ct,
            content,
            AddRhReturnRepresentationHeaders);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse app error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var participantId = ExtractRhRecordId(response, body, metadata.Participant.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(participantId))
            throw new InvalidOperationException("Dataverse guardo el participante, pero no devolvio el identificador.");

        return participantId;
    }

    private async Task CreateSurveyAnswerAsync(
        SoporteCloudSurveyMetadata metadata,
        SoporteCloudSurveySessionDto session,
        string participantId,
        SurveyComputedAnswer answer,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Answer.PrimaryNameField] = TruncateSurveyName(answer.Question.Text),
            [SurveyAnswerComponentField] = answer.Question.ComponentValue,
            [SurveyAnswerPointsField] = answer.Points,
            [SurveyAnswerMaxPointsField] = answer.MaxPoints,
            [SurveyAnswerCorrectField] = answer.IsCorrect,
            [SurveyAnswerNumericValueField] = answer.NumericValue,
            [SurveyAnswerTextValueField] = answer.TextValue,
            [SurveyAnswerSubmittedOnField] = FormatSurveyDateTime(GetSurveyBogotaNow())
        };
        payload[$"{metadata.AnswerSessionNavigationProperty}@odata.bind"] =
            $"/{metadata.Session.EntitySetName}({session.SessionId})";
        payload[$"{metadata.AnswerParticipantNavigationProperty}@odata.bind"] =
            $"/{metadata.Participant.EntitySetName}({participantId})";
        payload[$"{metadata.AnswerQuestionNavigationProperty}@odata.bind"] =
            $"/{metadata.Question.EntitySetName}({answer.Question.QuestionId})";
        if (!string.IsNullOrWhiteSpace(answer.OptionId))
        {
            payload[$"{metadata.AnswerOptionNavigationProperty}@odata.bind"] =
                $"/{metadata.Option.EntitySetName}({answer.OptionId})";
        }

        await CallDataverseAppSendAsync(
            $"/api/data/v9.2/{metadata.Answer.EntitySetName}",
            "POST",
            payload,
            ct);
    }

    private SurveyComputedAnswer ComputeSurveyAnswer(
        SoporteCloudSurveyQuestionDto question,
        SoporteCloudSurveyAnswerSubmitDto answer)
    {
        if (question.InputTypeValue == SurveyInputSingleChoice)
        {
            var optionId = NormalizeOptionalGuid(answer.OptionId);
            var option = question.Options.FirstOrDefault(item => string.Equals(item.OptionId, optionId, StringComparison.OrdinalIgnoreCase) && item.IsActive)
                ?? throw new InvalidOperationException($"Selecciona una opcion valida para: {question.Text}");
            var points = question.ComponentValue == SurveyComponentKnowledge
                ? Math.Min(question.MaxPoints, Math.Max(option.Points, option.IsCorrect ? question.MaxPoints : 0m))
                : 0m;

            return new SurveyComputedAnswer
            {
                Question = question,
                OptionId = option.OptionId,
                Points = RoundCurrency(points),
                MaxPoints = question.ComponentValue == SurveyComponentKnowledge ? question.MaxPoints : 0m,
                IsCorrect = option.IsCorrect,
                TextValue = option.Text
            };
        }

        if (question.InputTypeValue == SurveyInputRating)
        {
            var value = answer.NumericValue ?? 0m;
            if (value < 1m || value > 5m)
                throw new InvalidOperationException($"La calificacion debe estar entre 1 y 5 para: {question.Text}");

            return new SurveyComputedAnswer
            {
                Question = question,
                NumericValue = value,
                Points = 0m,
                MaxPoints = 0m,
                IsCorrect = false
            };
        }

        var text = (answer.TextValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Debes responder: {question.Text}");

        return new SurveyComputedAnswer
        {
            Question = question,
            TextValue = text,
            Points = 0m,
            MaxPoints = question.ComponentValue == SurveyComponentKnowledge ? question.MaxPoints : 0m,
            IsCorrect = false
        };
    }

    private async Task EnsureSurveySatisfactionDefaultsAsync(
        SoporteCloudSurveyMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var topics = await LoadSurveyTopicsAsync(metadata, user, ct);
            var satisfactionTopic = topics.FirstOrDefault(IsSatisfactionTopic);
            var satisfactionTopicId = satisfactionTopic?.TopicId ?? "";
            if (string.IsNullOrWhiteSpace(satisfactionTopicId))
            {
                satisfactionTopicId = await CreateSurveySatisfactionTopicAsync(metadata, user, ct);
            }
            else
            {
                var topicPatch = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (!string.Equals(satisfactionTopic!.Name, SurveySatisfactionTopicName, StringComparison.Ordinal))
                    topicPatch[metadata.Topic.PrimaryNameField] = SurveySatisfactionTopicName;
                if (!string.Equals(satisfactionTopic.Description, SurveySatisfactionTopicDescription, StringComparison.Ordinal))
                    topicPatch[SurveyTopicDescriptionField] = SurveySatisfactionTopicDescription;
                if (!satisfactionTopic.IsActive)
                    topicPatch[SurveyTopicActiveField] = true;

                if (topicPatch.Count > 0)
                {
                    await CallDataverseSendAsync(
                        $"/api/data/v9.2/{metadata.Topic.EntitySetName}({satisfactionTopicId})",
                        "PATCH",
                        topicPatch,
                        user,
                        ct);
                }
            }

            var questions = await LoadSurveyQuestionsAsync(metadata, user, ct);
            var satisfactionQuestions = questions
                .Where(question => question.ComponentValue == SurveyComponentSatisfaction)
                .ToList();
            var questionsByKey = satisfactionQuestions
                .GroupBy(question => NormalizeSurveyTextKey(question.Text), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var defaultKeys = SurveySatisfactionQuestionSeeds
                .Select(seed => NormalizeSurveyTextKey(seed.Text))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var seed in SurveySatisfactionQuestionSeeds)
            {
                var key = NormalizeSurveyTextKey(seed.Text);
                if (!questionsByKey.TryGetValue(key, out var existingQuestion))
                {
                    await SaveSurveySatisfactionQuestionSeedAsync(metadata, satisfactionTopicId, seed, user, ct);
                    continue;
                }

                var patch = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (!string.Equals(existingQuestion.Text, seed.Text, StringComparison.Ordinal))
                {
                    patch[metadata.Question.PrimaryNameField] = TruncateSurveyName(seed.Text);
                    patch[SurveyQuestionTextField] = seed.Text;
                }
                if (existingQuestion.InputTypeValue != seed.InputTypeValue)
                    patch[SurveyQuestionInputTypeField] = seed.InputTypeValue;
                if (existingQuestion.SortOrder != seed.SortOrder)
                    patch[SurveyQuestionSortOrderField] = seed.SortOrder;
                if (existingQuestion.MaxPoints != 0m)
                    patch[SurveyQuestionMaxPointsField] = 0m;
                if (!existingQuestion.IsActive)
                    patch[SurveyQuestionActiveField] = true;
                if (!string.Equals(existingQuestion.TopicId, satisfactionTopicId, StringComparison.OrdinalIgnoreCase))
                {
                    patch[$"{metadata.QuestionTopicNavigationProperty}@odata.bind"] =
                        $"/{metadata.Topic.EntitySetName}({satisfactionTopicId})";
                }

                if (patch.Count > 0)
                {
                    await CallDataverseSendAsync(
                        $"/api/data/v9.2/{metadata.Question.EntitySetName}({existingQuestion.QuestionId})",
                        "PATCH",
                        patch,
                        user,
                        ct);
                }
            }

            foreach (var extraQuestion in satisfactionQuestions.Where(question => !defaultKeys.Contains(NormalizeSurveyTextKey(question.Text)) && question.IsActive))
            {
                await CallDataverseSendAsync(
                    $"/api/data/v9.2/{metadata.Question.EntitySetName}({extraQuestion.QuestionId})",
                    "PATCH",
                    new Dictionary<string, object?> { [SurveyQuestionActiveField] = false },
                    user,
                    ct);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible verificar los valores fijos de Satisfaccion para las encuestas.");
        }
    }

    private async Task<string> CreateSurveySatisfactionTopicAsync(
        SoporteCloudSurveyMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Topic.PrimaryNameField] = SurveySatisfactionTopicName,
            [SurveyTopicDescriptionField] = SurveySatisfactionTopicDescription,
            [SurveyTopicActiveField] = true
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{metadata.Topic.EntitySetName}",
            "POST",
            user,
            ct,
            content,
            AddRhReturnRepresentationHeaders);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var topicId = ExtractRhRecordId(response, body, metadata.Topic.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(topicId))
            throw new InvalidOperationException("Dataverse guardo el tema Satisfaccion, pero no devolvio el identificador.");

        return topicId;
    }

    private async Task SaveSurveySatisfactionQuestionSeedAsync(
        SoporteCloudSurveyMetadata metadata,
        string satisfactionTopicId,
        SurveySatisfactionQuestionSeed seed,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.Question.PrimaryNameField] = TruncateSurveyName(seed.Text),
            [SurveyQuestionTextField] = seed.Text,
            [SurveyQuestionComponentField] = SurveyComponentSatisfaction,
            [SurveyQuestionInputTypeField] = seed.InputTypeValue,
            [SurveyQuestionSortOrderField] = seed.SortOrder,
            [SurveyQuestionMaxPointsField] = 0m,
            [SurveyQuestionActiveField] = true,
            [$"{metadata.QuestionTopicNavigationProperty}@odata.bind"] = $"/{metadata.Topic.EntitySetName}({satisfactionTopicId})"
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.Question.EntitySetName}",
            "POST",
            payload,
            user,
            ct);
    }

    private async Task TrackSurveyScanAsync(
        SoporteCloudSurveyMetadata metadata,
        SoporteCloudSurveySessionDto session,
        CancellationToken ct)
    {
        if (!metadata.HasSessionScanCountField || string.IsNullOrWhiteSpace(session.SessionId))
            return;

        var nextScanCount = Math.Max(session.ScanCount, 0) + 1;
        try
        {
            await CallDataverseAppSendAsync(
                $"/api/data/v9.2/{metadata.Session.EntitySetName}({session.SessionId})",
                "PATCH",
                new Dictionary<string, object?> { [SurveySessionScanCountField] = nextScanCount },
                ct);
            session.ScanCount = nextScanCount;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "No fue posible registrar escaneo para la sesion {SessionId}.", session.SessionId);
        }
    }

    private async Task<SoporteCloudSurveyMetadata> ResolveSoporteCloudSurveyMetadataAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var topic = await ResolveRhEntityMetadataAsync(SurveyTopicLogicalName, SurveyTopicFallbackEntitySetName, SurveyTopicFallbackIdField, SurveyTopicPrimaryNameField, user, ct);
        var question = await ResolveRhEntityMetadataAsync(SurveyQuestionLogicalName, SurveyQuestionFallbackEntitySetName, SurveyQuestionFallbackIdField, SurveyQuestionPrimaryNameField, user, ct);
        var option = await ResolveRhEntityMetadataAsync(SurveyOptionLogicalName, SurveyOptionFallbackEntitySetName, SurveyOptionFallbackIdField, SurveyOptionPrimaryNameField, user, ct);
        var session = await ResolveRhEntityMetadataAsync(SurveySessionLogicalName, SurveySessionFallbackEntitySetName, SurveySessionFallbackIdField, SurveySessionPrimaryNameField, user, ct);
        var participant = await ResolveRhEntityMetadataAsync(SurveyParticipantLogicalName, SurveyParticipantFallbackEntitySetName, SurveyParticipantFallbackIdField, SurveyParticipantPrimaryNameField, user, ct);
        var answer = await ResolveRhEntityMetadataAsync(SurveyAnswerLogicalName, SurveyAnswerFallbackEntitySetName, SurveyAnswerFallbackIdField, SurveyAnswerPrimaryNameField, user, ct);

        return new SoporteCloudSurveyMetadata
        {
            Topic = topic,
            Question = question,
            Option = option,
            Session = session,
            Participant = participant,
            Answer = answer,
            HasSessionScanCountField = await SurveyAttributeExistsAsync(SurveySessionLogicalName, SurveySessionScanCountField, user, ct),
            QuestionTopicNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyQuestionLogicalName, SurveyQuestionTopicField, user, ct),
            OptionQuestionNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyOptionLogicalName, SurveyOptionQuestionField, user, ct),
            SessionTopicNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveySessionLogicalName, SurveySessionTopicField, user, ct),
            SessionClientNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveySessionLogicalName, SurveySessionClientField, user, ct),
            ParticipantSessionNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyParticipantLogicalName, SurveyParticipantSessionField, user, ct),
            AnswerSessionNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerSessionField, user, ct),
            AnswerParticipantNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerParticipantField, user, ct),
            AnswerQuestionNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerQuestionField, user, ct),
            AnswerOptionNavigationProperty = await ResolveSurveyLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerOptionField, user, ct)
        };
    }

    private async Task<SoporteCloudSurveyMetadata> ResolveSoporteCloudSurveyAppMetadataAsync(CancellationToken ct)
    {
        var topic = await ResolveSurveyAppEntityMetadataAsync(SurveyTopicLogicalName, SurveyTopicFallbackEntitySetName, SurveyTopicFallbackIdField, SurveyTopicPrimaryNameField, ct);
        var question = await ResolveSurveyAppEntityMetadataAsync(SurveyQuestionLogicalName, SurveyQuestionFallbackEntitySetName, SurveyQuestionFallbackIdField, SurveyQuestionPrimaryNameField, ct);
        var option = await ResolveSurveyAppEntityMetadataAsync(SurveyOptionLogicalName, SurveyOptionFallbackEntitySetName, SurveyOptionFallbackIdField, SurveyOptionPrimaryNameField, ct);
        var session = await ResolveSurveyAppEntityMetadataAsync(SurveySessionLogicalName, SurveySessionFallbackEntitySetName, SurveySessionFallbackIdField, SurveySessionPrimaryNameField, ct);
        var participant = await ResolveSurveyAppEntityMetadataAsync(SurveyParticipantLogicalName, SurveyParticipantFallbackEntitySetName, SurveyParticipantFallbackIdField, SurveyParticipantPrimaryNameField, ct);
        var answer = await ResolveSurveyAppEntityMetadataAsync(SurveyAnswerLogicalName, SurveyAnswerFallbackEntitySetName, SurveyAnswerFallbackIdField, SurveyAnswerPrimaryNameField, ct);

        return new SoporteCloudSurveyMetadata
        {
            Topic = topic,
            Question = question,
            Option = option,
            Session = session,
            Participant = participant,
            Answer = answer,
            HasSessionScanCountField = await SurveyAppAttributeExistsAsync(SurveySessionLogicalName, SurveySessionScanCountField, ct),
            QuestionTopicNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyQuestionLogicalName, SurveyQuestionTopicField, ct),
            OptionQuestionNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyOptionLogicalName, SurveyOptionQuestionField, ct),
            SessionTopicNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveySessionLogicalName, SurveySessionTopicField, ct),
            SessionClientNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveySessionLogicalName, SurveySessionClientField, ct),
            ParticipantSessionNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyParticipantLogicalName, SurveyParticipantSessionField, ct),
            AnswerSessionNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerSessionField, ct),
            AnswerParticipantNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerParticipantField, ct),
            AnswerQuestionNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerQuestionField, ct),
            AnswerOptionNavigationProperty = await ResolveSurveyAppLookupNavigationPropertyAsync(SurveyAnswerLogicalName, SurveyAnswerOptionField, ct)
        };
    }

    private async Task<bool> SurveyAttributeExistsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                $"/Attributes(LogicalName='{EscapeOdataLiteral(attributeLogicalName)}')?$select=LogicalName";
            await CallDataverseGetJsonAsync(relativeUrl, user, ct);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "No se encontro la columna opcional {AttributeLogicalName} en {EntityLogicalName}.", attributeLogicalName, entityLogicalName);
            return false;
        }
    }

    private async Task<bool> SurveyAppAttributeExistsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken ct)
    {
        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                $"/Attributes(LogicalName='{EscapeOdataLiteral(attributeLogicalName)}')?$select=LogicalName";
            await CallDataverseAppGetJsonAsync(relativeUrl, ct);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "No se encontro la columna opcional app-only {AttributeLogicalName} en {EntityLogicalName}.", attributeLogicalName, entityLogicalName);
            return false;
        }
    }

    private async Task<string> ResolveSurveyLookupNavigationPropertyAsync(
        string entityLogicalName,
        string lookupField,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            return await ResolveRhLookupNavigationPropertyAsync(entityLogicalName, lookupField, lookupField, user, ct);
        }
        catch (InvalidOperationException)
        {
            return lookupField;
        }
    }

    private async Task<RhEntityMetadata> ResolveSurveyAppEntityMetadataAsync(
        string logicalName,
        string fallbackEntitySetName,
        string fallbackPrimaryIdField,
        string fallbackPrimaryNameField,
        CancellationToken ct)
    {
        if (_rhEntityMetadataCache.TryGetValue(logicalName, out var cached))
            return cached;

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(logicalName)}')" +
                "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute";
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var metadata = new RhEntityMetadata
            {
                LogicalName = logicalName,
                EntitySetName = FirstNonEmpty(ReadString(doc.RootElement, "EntitySetName"), fallbackEntitySetName),
                PrimaryIdField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryIdAttribute"), fallbackPrimaryIdField),
                PrimaryNameField = FirstNonEmpty(ReadString(doc.RootElement, "PrimaryNameAttribute"), fallbackPrimaryNameField)
            };
            _rhEntityMetadataCache[logicalName] = metadata;
            return metadata;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver metadata app-only para {LogicalName}. Se usara fallback.", logicalName);
            var fallback = new RhEntityMetadata
            {
                LogicalName = logicalName,
                EntitySetName = fallbackEntitySetName,
                PrimaryIdField = fallbackPrimaryIdField,
                PrimaryNameField = fallbackPrimaryNameField
            };
            _rhEntityMetadataCache[logicalName] = fallback;
            return fallback;
        }
    }

    private async Task<string> ResolveSurveyAppLookupNavigationPropertyAsync(
        string entityLogicalName,
        string lookupField,
        CancellationToken ct)
    {
        var cacheKey = $"{entityLogicalName}|{lookupField}";
        if (_rhLookupNavigationPropertyCache.TryGetValue(cacheKey, out var cached)
            && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(entityLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)";
            var json = await CallDataverseAppGetJsonAsync(relativeUrl, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ManyToOneRelationships", out var relationships)
                && relationships.ValueKind == JsonValueKind.Array)
            {
                var navigationProperty = relationships
                    .EnumerateArray()
                    .Where(relationship => string.Equals(ReadString(relationship, "ReferencingAttribute"), lookupField, StringComparison.OrdinalIgnoreCase))
                    .Select(relationship => ReadString(relationship, "ReferencingEntityNavigationPropertyName"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(navigationProperty))
                {
                    _rhLookupNavigationPropertyCache[cacheKey] = navigationProperty.Trim();
                    return navigationProperty.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible resolver lookup app-only {LookupField} para {EntityLogicalName}.", lookupField, entityLogicalName);
        }

        _rhLookupNavigationPropertyCache[cacheKey] = lookupField;
        return lookupField;
    }

    private async Task<SurveyContext> LoadSurveyContextAsync(
        SoporteCloudSurveyMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var topicsTask = LoadSurveyTopicsAsync(metadata, user, ct);
        var questionsTask = LoadSurveyQuestionsAsync(metadata, user, ct);
        var optionsTask = LoadSurveyOptionsAsync(metadata, user, ct);
        var sessionsTask = LoadSurveySessionsAsync(metadata, user, ct);
        var participantsTask = LoadSurveyParticipantsAsync(metadata, user, ct);
        var answersTask = LoadSurveyAnswersAsync(metadata, user, ct);
        await Task.WhenAll(topicsTask, questionsTask, optionsTask, sessionsTask, participantsTask, answersTask);

        return HydrateSurveyContext(
            topicsTask.Result,
            questionsTask.Result,
            optionsTask.Result,
            sessionsTask.Result,
            participantsTask.Result,
            answersTask.Result);
    }

    private async Task<SurveyContext> LoadPublicSurveyContextAsync(
        SoporteCloudSurveyMetadata metadata,
        string code,
        CancellationToken ct)
    {
        var normalizedCode = NormalizeSurveyCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new InvalidOperationException("El codigo de encuesta no es valido.");

        var filter = Uri.EscapeDataString($"{SurveySessionCodeField} eq '{EscapeOdataLiteral(normalizedCode)}'");
        var sessionItems = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.Session.EntitySetName}?$select={BuildSurveySessionSelectClause(metadata)}&$filter={filter}&$top=1",
            ct,
            AddFormattedValueHeaders);
        var sessions = sessionItems
            .Select(item => BuildSurveySessionDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
        if (sessions.Count == 0)
            return new SurveyContext();

        var questions = await LoadSurveyQuestionsAppAsync(metadata, ct);
        var options = await LoadSurveyOptionsAppAsync(metadata, ct);
        var sessionId = sessions[0].SessionId;
        var participantFilter = Uri.EscapeDataString($"{BuildDashboardLookupValuePropertyName(SurveyParticipantSessionField)} eq {sessionId}");
        var answerFilter = Uri.EscapeDataString($"{BuildDashboardLookupValuePropertyName(SurveyAnswerSessionField)} eq {sessionId}");
        var participantsTask = LoadSurveyParticipantsAppAsync(metadata, participantFilter, ct);
        var answersTask = LoadSurveyAnswersAppAsync(metadata, answerFilter, ct);
        await Task.WhenAll(participantsTask, answersTask);

        return HydrateSurveyContext(
            Array.Empty<SurveyTopicRaw>(),
            questions,
            options,
            sessions,
            participantsTask.Result,
            answersTask.Result);
    }

    private async Task<IReadOnlyList<SurveyTopicRaw>> LoadSurveyTopicsAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Topic.EntitySetName}?$select={metadata.Topic.PrimaryIdField},{metadata.Topic.PrimaryNameField},{SurveyTopicDescriptionField},{SurveyTopicActiveField}&$orderby={metadata.Topic.PrimaryNameField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        return items.Select(item => new SurveyTopicRaw
        {
            TopicId = ReadString(item, metadata.Topic.PrimaryIdField).Trim(),
            Name = ReadString(item, metadata.Topic.PrimaryNameField).Trim(),
            Description = ReadString(item, SurveyTopicDescriptionField).Trim(),
            IsActive = !item.TryGetProperty(SurveyTopicActiveField, out _) || ReadBool(item, SurveyTopicActiveField)
        }).Where(item => !string.IsNullOrWhiteSpace(item.TopicId)).ToList();
    }

    private async Task<IReadOnlyList<SurveyQuestionRaw>> LoadSurveyQuestionsAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Question.EntitySetName}?$select={BuildSurveyQuestionSelectClause(metadata)}&$orderby={SurveyQuestionComponentField} asc,{SurveyQuestionSortOrderField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyQuestionRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SurveyOptionRaw>> LoadSurveyOptionsAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Option.EntitySetName}?$select={BuildSurveyOptionSelectClause(metadata)}&$orderby={SurveyOptionSortOrderField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyOptionRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SoporteCloudSurveySessionDto>> LoadSurveySessionsAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Session.EntitySetName}?$select={BuildSurveySessionSelectClause(metadata)}&$orderby={SurveySessionDateField} desc,{SurveySessionCreatedOnField} desc&$top=250";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => BuildSurveySessionDto(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SoporteCloudSurveyParticipantDto>> LoadSurveyParticipantsAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Participant.EntitySetName}?$select={BuildSurveyParticipantSelectClause(metadata)}&$orderby={SurveyParticipantSubmittedOnField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyParticipantDto(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SurveyAnswerRaw>> LoadSurveyAnswersAsync(SoporteCloudSurveyMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{metadata.Answer.EntitySetName}?$select={BuildSurveyAnswerSelectClause(metadata)}&$orderby={SurveyAnswerSubmittedOnField} desc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyAnswerRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SurveyQuestionRaw>> LoadSurveyQuestionsAppAsync(SoporteCloudSurveyMetadata metadata, CancellationToken ct)
    {
        var items = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.Question.EntitySetName}?$select={BuildSurveyQuestionSelectClause(metadata)}&$orderby={SurveyQuestionComponentField} asc,{SurveyQuestionSortOrderField} asc",
            ct,
            AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyQuestionRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SurveyOptionRaw>> LoadSurveyOptionsAppAsync(SoporteCloudSurveyMetadata metadata, CancellationToken ct)
    {
        var items = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.Option.EntitySetName}?$select={BuildSurveyOptionSelectClause(metadata)}&$orderby={SurveyOptionSortOrderField} asc",
            ct,
            AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyOptionRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SoporteCloudSurveyParticipantDto>> LoadSurveyParticipantsAppAsync(SoporteCloudSurveyMetadata metadata, string filter, CancellationToken ct)
    {
        var items = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.Participant.EntitySetName}?$select={BuildSurveyParticipantSelectClause(metadata)}&$filter={filter}&$orderby={SurveyParticipantSubmittedOnField} desc",
            ct,
            AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyParticipantDto(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private async Task<IReadOnlyList<SurveyAnswerRaw>> LoadSurveyAnswersAppAsync(SoporteCloudSurveyMetadata metadata, string filter, CancellationToken ct)
    {
        var items = await GetDataverseAppEntitiesAsync(
            $"/api/data/v9.2/{metadata.Answer.EntitySetName}?$select={BuildSurveyAnswerSelectClause(metadata)}&$filter={filter}&$orderby={SurveyAnswerSubmittedOnField} desc",
            ct,
            AddFormattedValueHeaders);
        return items.Select(item => BuildSurveyAnswerRaw(metadata, item)).Where(item => item is not null).Select(item => item!).ToList();
    }

    private static string BuildSurveyQuestionSelectClause(SoporteCloudSurveyMetadata metadata) =>
        string.Join(",", new[]
        {
            metadata.Question.PrimaryIdField,
            metadata.Question.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(SurveyQuestionTopicField),
            SurveyQuestionComponentField,
            SurveyQuestionInputTypeField,
            SurveyQuestionTextField,
            SurveyQuestionSortOrderField,
            SurveyQuestionMaxPointsField,
            SurveyQuestionActiveField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

    private static string BuildSurveyOptionSelectClause(SoporteCloudSurveyMetadata metadata) =>
        string.Join(",", new[]
        {
            metadata.Option.PrimaryIdField,
            metadata.Option.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(SurveyOptionQuestionField),
            SurveyOptionTextField,
            SurveyOptionCorrectField,
            SurveyOptionPointsField,
            SurveyOptionSortOrderField,
            SurveyOptionActiveField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

    private static string BuildSurveySessionSelectClause(SoporteCloudSurveyMetadata metadata) =>
        string.Join(",", new[]
        {
            metadata.Session.PrimaryIdField,
            metadata.Session.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(SurveySessionTopicField),
            BuildDashboardLookupValuePropertyName(SurveySessionClientField),
            SurveySessionDateField,
            SurveySessionCodeField,
            SurveySessionStateField,
            SurveySessionCreatedOnField,
            metadata.HasSessionScanCountField ? SurveySessionScanCountField : ""
        }.Where(field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string BuildSurveyParticipantSelectClause(SoporteCloudSurveyMetadata metadata) =>
        string.Join(",", new[]
        {
            metadata.Participant.PrimaryIdField,
            metadata.Participant.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(SurveyParticipantSessionField),
            SurveyParticipantEmailField,
            SurveyParticipantCompanyField,
            SurveyParticipantScoreField,
            SurveyParticipantMaxScoreField,
            SurveyParticipantScorePercentField,
            SurveyParticipantSubmittedOnField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

    private static string BuildSurveyAnswerSelectClause(SoporteCloudSurveyMetadata metadata) =>
        string.Join(",", new[]
        {
            metadata.Answer.PrimaryIdField,
            BuildDashboardLookupValuePropertyName(SurveyAnswerSessionField),
            BuildDashboardLookupValuePropertyName(SurveyAnswerParticipantField),
            BuildDashboardLookupValuePropertyName(SurveyAnswerQuestionField),
            BuildDashboardLookupValuePropertyName(SurveyAnswerOptionField),
            SurveyAnswerComponentField,
            SurveyAnswerPointsField,
            SurveyAnswerMaxPointsField,
            SurveyAnswerCorrectField,
            SurveyAnswerNumericValueField,
            SurveyAnswerTextValueField,
            SurveyAnswerSubmittedOnField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

    private SurveyQuestionRaw? BuildSurveyQuestionRaw(SoporteCloudSurveyMetadata metadata, JsonElement item)
    {
        var questionId = ReadString(item, metadata.Question.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(questionId))
            return null;

        var topicLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyQuestionTopicField) }, "tema");
        var componentValue = ReadIntFlexible(item, SurveyQuestionComponentField);
        var inputTypeValue = ReadIntFlexible(item, SurveyQuestionInputTypeField);
        return new SurveyQuestionRaw
        {
            QuestionId = questionId,
            TopicId = ReadString(item, topicLookup).Trim(),
            TopicName = FirstNonEmpty(ReadLookupFormattedValue(item, topicLookup), ReadString(item, $"{SurveyQuestionTopicField}{FormattedValueAnnotationSuffix}").Trim()),
            ComponentValue = NormalizeSurveyComponent(componentValue),
            InputTypeValue = NormalizeSurveyInputType(inputTypeValue),
            Text = FirstNonEmpty(ReadString(item, SurveyQuestionTextField), ReadString(item, metadata.Question.PrimaryNameField)).Trim(),
            SortOrder = Math.Max(ReadIntFlexible(item, SurveyQuestionSortOrderField), 0),
            MaxPoints = Math.Max(ReadDecimal(item, SurveyQuestionMaxPointsField) ?? 0m, 0m),
            IsActive = !item.TryGetProperty(SurveyQuestionActiveField, out _) || ReadBool(item, SurveyQuestionActiveField)
        };
    }

    private SurveyOptionRaw? BuildSurveyOptionRaw(SoporteCloudSurveyMetadata metadata, JsonElement item)
    {
        var optionId = ReadString(item, metadata.Option.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(optionId))
            return null;

        var questionLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyOptionQuestionField) }, "pregunta");
        return new SurveyOptionRaw
        {
            OptionId = optionId,
            QuestionId = ReadString(item, questionLookup).Trim(),
            Text = FirstNonEmpty(ReadString(item, SurveyOptionTextField), ReadString(item, metadata.Option.PrimaryNameField)).Trim(),
            IsCorrect = ReadBool(item, SurveyOptionCorrectField),
            Points = Math.Max(ReadDecimal(item, SurveyOptionPointsField) ?? 0m, 0m),
            SortOrder = Math.Max(ReadIntFlexible(item, SurveyOptionSortOrderField), 0),
            IsActive = !item.TryGetProperty(SurveyOptionActiveField, out _) || ReadBool(item, SurveyOptionActiveField)
        };
    }

    private SoporteCloudSurveySessionDto? BuildSurveySessionDto(SoporteCloudSurveyMetadata metadata, JsonElement item)
    {
        var sessionId = ReadString(item, metadata.Session.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var topicLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveySessionTopicField) }, "tema");
        var clientLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveySessionClientField) }, "cliente");
        var date = ReadDateOnly(item, SurveySessionDateField);
        var state = ReadIntFlexible(item, SurveySessionStateField);
        if (!SurveySessionStateLabels.ContainsKey(state))
            state = SurveySessionStateOpen;

        return new SoporteCloudSurveySessionDto
        {
            SessionId = sessionId,
            Name = FirstNonEmpty(ReadString(item, metadata.Session.PrimaryNameField), "Sesion de capacitacion"),
            Code = NormalizeSurveyCode(ReadString(item, SurveySessionCodeField)),
            TopicId = ReadString(item, topicLookup).Trim(),
            TopicName = FirstNonEmpty(ReadLookupFormattedValue(item, topicLookup), ReadString(item, $"{SurveySessionTopicField}{FormattedValueAnnotationSuffix}").Trim(), "Sin tema"),
            ClientId = ReadString(item, clientLookup).Trim(),
            ClientName = FirstNonEmpty(ReadLookupFormattedValue(item, clientLookup), ReadString(item, $"{SurveySessionClientField}{FormattedValueAnnotationSuffix}").Trim(), "Sin cliente"),
            DateValue = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DateDisplay = date?.ToString("dd/MM/yyyy", SoporteCloudCulture) ?? "Sin fecha",
            StateValue = state,
            StateLabel = SurveySessionStateLabels.TryGetValue(state, out var label) ? label : "Abierta",
            ScanCount = metadata.HasSessionScanCountField ? Math.Max(ReadIntFlexible(item, SurveySessionScanCountField), 0) : 0
        };
    }

    private SoporteCloudSurveyParticipantDto? BuildSurveyParticipantDto(SoporteCloudSurveyMetadata metadata, JsonElement item)
    {
        var participantId = ReadString(item, metadata.Participant.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(participantId))
            return null;

        var submitted = ReadSurveyDateTime(item, SurveyParticipantSubmittedOnField);
        return new SoporteCloudSurveyParticipantDto
        {
            ParticipantId = participantId,
            FullName = FirstNonEmpty(ReadString(item, metadata.Participant.PrimaryNameField), "Participante"),
            Email = ReadString(item, SurveyParticipantEmailField).Trim(),
            Company = ReadString(item, SurveyParticipantCompanyField).Trim(),
            SubmittedOnDisplay = submitted?.ToString("dd/MM/yyyy HH:mm", SoporteCloudCulture) ?? "",
            Score = RoundCurrency(ReadDecimal(item, SurveyParticipantScoreField) ?? 0m),
            MaxScore = RoundCurrency(ReadDecimal(item, SurveyParticipantMaxScoreField) ?? 0m),
            ScorePercent = RoundCurrency(ReadDecimal(item, SurveyParticipantScorePercentField) ?? 0m)
        };
    }

    private SurveyAnswerRaw? BuildSurveyAnswerRaw(SoporteCloudSurveyMetadata metadata, JsonElement item)
    {
        var answerId = ReadString(item, metadata.Answer.PrimaryIdField).Trim();
        if (string.IsNullOrWhiteSpace(answerId))
            return null;

        var sessionLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyAnswerSessionField) }, "sesion");
        var participantLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyAnswerParticipantField) }, "participante");
        var questionLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyAnswerQuestionField) }, "pregunta");
        var optionLookup = DetectLookupValueProperty(item, new[] { BuildDashboardLookupValuePropertyName(SurveyAnswerOptionField) }, "opcion");

        return new SurveyAnswerRaw
        {
            AnswerId = answerId,
            SessionId = ReadString(item, sessionLookup).Trim(),
            ParticipantId = ReadString(item, participantLookup).Trim(),
            QuestionId = ReadString(item, questionLookup).Trim(),
            OptionId = ReadString(item, optionLookup).Trim(),
            ComponentValue = NormalizeSurveyComponent(ReadIntFlexible(item, SurveyAnswerComponentField)),
            Points = RoundCurrency(ReadDecimal(item, SurveyAnswerPointsField) ?? 0m),
            MaxPoints = RoundCurrency(ReadDecimal(item, SurveyAnswerMaxPointsField) ?? 0m),
            IsCorrect = ReadBool(item, SurveyAnswerCorrectField),
            NumericValue = ReadDecimal(item, SurveyAnswerNumericValueField),
            TextValue = ReadString(item, SurveyAnswerTextValueField).Trim()
        };
    }

    private static SurveyContext HydrateSurveyContext(
        IReadOnlyList<SurveyTopicRaw> topics,
        IReadOnlyList<SurveyQuestionRaw> questions,
        IReadOnlyList<SurveyOptionRaw> options,
        IReadOnlyList<SoporteCloudSurveySessionDto> sessions,
        IReadOnlyList<SoporteCloudSurveyParticipantDto> participants,
        IReadOnlyList<SurveyAnswerRaw> answers)
    {
        var optionsByQuestion = options
            .GroupBy(option => option.QuestionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.SortOrder).ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var topicById = topics.ToDictionary(topic => topic.TopicId, StringComparer.OrdinalIgnoreCase);
        var questionDtos = questions
            .Select(question =>
            {
                optionsByQuestion.TryGetValue(question.QuestionId, out var questionOptions);
                topicById.TryGetValue(question.TopicId, out var topic);
                return new SoporteCloudSurveyQuestionDto
                {
                    QuestionId = question.QuestionId,
                    TopicId = question.TopicId,
                    TopicName = question.ComponentValue == SurveyComponentSatisfaction
                        ? FirstNonEmpty(question.TopicName, topic?.Name, SurveySatisfactionTopicName)
                        : FirstNonEmpty(question.TopicName, topic?.Name),
                    ComponentValue = question.ComponentValue,
                    ComponentLabel = SurveyComponentLabels.TryGetValue(question.ComponentValue, out var componentLabel) ? componentLabel : "",
                    InputTypeValue = question.InputTypeValue,
                    InputTypeLabel = SurveyInputTypeLabels.TryGetValue(question.InputTypeValue, out var inputLabel) ? inputLabel : "",
                    Text = question.Text,
                    SortOrder = question.SortOrder,
                    MaxPoints = question.MaxPoints,
                    IsActive = question.IsActive,
                    IsLocked = question.ComponentValue == SurveyComponentSatisfaction,
                    Options = (questionOptions ?? new List<SurveyOptionRaw>())
                        .Select(option => new SoporteCloudSurveyOptionDto
                        {
                            OptionId = option.OptionId,
                            Text = option.Text,
                            IsCorrect = option.IsCorrect,
                            Points = option.Points,
                            SortOrder = option.SortOrder,
                            IsActive = option.IsActive
                        })
                        .ToList()
                };
            })
            .OrderBy(question => question.ComponentValue)
            .ThenBy(question => question.TopicName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(question => question.SortOrder)
            .ToList();

        var topicDtos = topics
            .Select(topic => new SoporteCloudSurveyTopicDto
            {
                TopicId = topic.TopicId,
                Name = topic.Name,
                Description = topic.Description,
                IsActive = topic.IsActive,
                IsLocked = IsSatisfactionTopic(topic),
                KnowledgeQuestionCount = questionDtos.Count(question => question.ComponentValue == SurveyComponentKnowledge
                    && string.Equals(question.TopicId, topic.TopicId, StringComparison.OrdinalIgnoreCase)
                    && question.IsActive)
            })
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SurveyContext
        {
            Topics = topicDtos,
            Questions = questionDtos,
            Sessions = sessions.ToList(),
            Participants = participants.ToList(),
            Answers = answers.ToList()
        };
    }

    private SoporteCloudSurveyBoardDto BuildSurveyBoard(SurveyContext context, Func<string, string> publicUrlBuilder)
    {
        var detailedSessions = context.Sessions
            .Select(session => BuildSessionDetail(session, context, publicUrlBuilder).Session)
            .OrderByDescending(session => session.DateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allKnowledgeStats = context.Sessions
            .SelectMany(session => BuildSessionDetail(session, context, publicUrlBuilder).KnowledgeQuestionStats)
            .Where(stat => stat.TotalAnswers > 0)
            .ToList();
        var totalResponses = detailedSessions.Sum(session => session.CompletedCount);
        var weightedPercent = detailedSessions
            .Where(session => session.CompletedCount > 0)
            .Sum(session => session.AverageScorePercent * session.CompletedCount);

        return new SoporteCloudSurveyBoardDto
        {
            TotalSessions = detailedSessions.Count,
            OpenSessions = detailedSessions.Count(session => session.StateValue == SurveySessionStateOpen),
            TotalResponses = totalResponses,
            AverageScorePercent = totalResponses == 0 ? 0m : RoundCurrency(weightedPercent / totalResponses),
            Topics = context.Topics,
            Questions = context.Questions,
            Sessions = detailedSessions,
            BestQuestions = allKnowledgeStats
                .OrderByDescending(stat => stat.CorrectPercent)
                .ThenByDescending(stat => stat.TotalAnswers)
                .Take(5)
                .ToList(),
            WeakQuestions = allKnowledgeStats
                .OrderBy(stat => stat.CorrectPercent)
                .ThenByDescending(stat => stat.TotalAnswers)
                .Take(5)
                .ToList(),
            Message = detailedSessions.Count == 0
                ? "Aun no hay sesiones de encuesta configuradas."
                : $"Se cargaron {detailedSessions.Count} sesion(es) de encuesta."
        };
    }

    private SoporteCloudSurveySessionDetailDto BuildSessionDetail(
        SoporteCloudSurveySessionDto session,
        SurveyContext context,
        Func<string, string> publicUrlBuilder)
    {
        var participants = context.Participants
            .Where(participant => context.Answers.Any(answer =>
                string.Equals(answer.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(answer.ParticipantId, participant.ParticipantId, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(participant => participant.ParticipantId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(participant => participant.ScorePercent)
            .ThenByDescending(participant => participant.Score)
            .ThenBy(participant => participant.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var answers = context.Answers
            .Where(answer => string.Equals(answer.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var applicableKnowledgeQuestions = context.Questions
            .Where(question => question.ComponentValue == SurveyComponentKnowledge
                && question.IsActive
                && string.Equals(question.TopicId, session.TopicId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var satisfactionQuestions = context.Questions
            .Where(question => question.ComponentValue == SurveyComponentSatisfaction && question.IsActive)
            .ToList();
        var requiredQuestionIds = applicableKnowledgeQuestions
            .Concat(satisfactionQuestions)
            .Select(question => question.QuestionId)
            .Where(questionId => !string.IsNullOrWhiteSpace(questionId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completedParticipantIds = requiredQuestionIds.Count == 0
            ? participants.Select(participant => participant.ParticipantId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : answers
                .GroupBy(answer => answer.ParticipantId, StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                {
                    var answeredQuestionIds = group
                        .Select(answer => answer.QuestionId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return requiredQuestionIds.All(answeredQuestionIds.Contains);
                })
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completedParticipants = participants
            .Where(participant => completedParticipantIds.Contains(participant.ParticipantId))
            .ToList();
        var knowledgeStats = applicableKnowledgeQuestions
            .Select(question => BuildQuestionStats(question, answers))
            .OrderByDescending(stat => stat.TotalAnswers)
            .ThenBy(stat => stat.QuestionText, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var satisfactionStats = satisfactionQuestions
            .Select(question => BuildQuestionStats(question, answers))
            .Where(stat => stat.TotalAnswers > 0)
            .OrderBy(stat => stat.QuestionText, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var knowledgeQuestionById = applicableKnowledgeQuestions.ToDictionary(question => question.QuestionId, StringComparer.OrdinalIgnoreCase);
        var knowledgeAnswers = answers
            .Where(answer => knowledgeQuestionById.ContainsKey(answer.QuestionId))
            .Select(answer =>
            {
                var question = knowledgeQuestionById[answer.QuestionId];
                return new SoporteCloudSurveyParticipantAnswerDto
                {
                    ParticipantId = answer.ParticipantId,
                    QuestionId = answer.QuestionId,
                    QuestionText = question.Text,
                    AnswerText = answer.TextValue,
                    IsCorrect = answer.IsCorrect,
                    Points = answer.Points
                };
            })
            .ToList();
        var avgScorePercent = completedParticipants.Count == 0 ? 0m : RoundCurrency(completedParticipants.Average(item => item.ScorePercent));
        var avgScore = completedParticipants.Count == 0 ? 0m : RoundCurrency(completedParticipants.Average(item => item.Score));
        var avgSatisfaction = satisfactionStats.Count == 0 ? 0m : RoundCurrency(satisfactionStats.Average(item => item.AverageRating));

        var detailedSession = new SoporteCloudSurveySessionDto
        {
            SessionId = session.SessionId,
            Name = session.Name,
            Code = session.Code,
            TopicId = session.TopicId,
            TopicName = session.TopicName,
            ClientId = session.ClientId,
            ClientName = session.ClientName,
            DateValue = session.DateValue,
            DateDisplay = session.DateDisplay,
            StateValue = session.StateValue,
            StateLabel = session.StateLabel,
            PublicUrl = publicUrlBuilder(session.Code),
            ScanCount = session.ScanCount,
            RegisteredCount = participants.Count,
            CompletedCount = completedParticipants.Count,
            AverageScore = avgScore,
            AverageScorePercent = avgScorePercent,
            AverageSatisfaction = avgSatisfaction
        };

        return new SoporteCloudSurveySessionDetailDto
        {
            Session = detailedSession,
            Participants = participants,
            Leaderboard = completedParticipants.Take(10).ToList(),
            KnowledgeQuestionStats = knowledgeStats,
            SatisfactionQuestionStats = satisfactionStats,
            KnowledgeAnswers = knowledgeAnswers
        };
    }

    private static SoporteCloudSurveyQuestionStatsDto BuildQuestionStats(
        SoporteCloudSurveyQuestionDto question,
        IReadOnlyList<SurveyAnswerRaw> answers)
    {
        var questionAnswers = answers
            .Where(answer => string.Equals(answer.QuestionId, question.QuestionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var total = questionAnswers.Count;
        var correct = questionAnswers.Count(answer => answer.IsCorrect);
        var ratings = questionAnswers
            .Select(answer => answer.NumericValue)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return new SoporteCloudSurveyQuestionStatsDto
        {
            QuestionId = question.QuestionId,
            QuestionText = question.Text,
            ComponentValue = question.ComponentValue,
            TotalAnswers = total,
            CorrectAnswers = correct,
            WrongAnswers = question.ComponentValue == SurveyComponentKnowledge ? Math.Max(total - correct, 0) : 0,
            AveragePoints = total == 0 ? 0m : RoundCurrency(questionAnswers.Average(answer => answer.Points)),
            CorrectPercent = total == 0 ? 0m : Math.Round((correct * 100m) / total, 2, MidpointRounding.AwayFromZero),
            AverageRating = ratings.Count == 0 ? 0m : RoundCurrency(ratings.Average())
        };
    }

    private string BuildSurveyPublicUrl(string code)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return $"/SoporteCloud/Encuesta/{Uri.EscapeDataString(code)}";

        var request = httpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";
        return $"{baseUrl}/SoporteCloud/Encuesta/{Uri.EscapeDataString(code)}";
    }

    private static string GenerateSurveyCode()
    {
        return Convert.ToHexString(Guid.NewGuid().ToByteArray())
            .Replace("-", "", StringComparison.Ordinal)
            .Substring(0, 8)
            .ToUpperInvariant();
    }

    private static string NormalizeSurveyCode(string? code) =>
        (code ?? "").Trim().ToUpperInvariant();

    private static bool IsSatisfactionTopic(SurveyTopicRaw topic) =>
        IsSatisfactionTopicName(topic.Name);

    private static bool IsSatisfactionTopicName(string? value) =>
        string.Equals(NormalizeSurveyTextKey(value), NormalizeSurveyTextKey(SurveySatisfactionTopicName), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSurveyTextKey(string? value)
    {
        var normalized = (value ?? "").Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static int NormalizeSurveyComponent(int value) =>
        value == SurveyComponentSatisfaction ? SurveyComponentSatisfaction : SurveyComponentKnowledge;

    private static int NormalizeSurveyInputType(int value) =>
        value is SurveyInputRating or SurveyInputText ? value : SurveyInputSingleChoice;

    private static string TruncateSurveyName(string value)
    {
        var normalized = (value ?? "").ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180];
    }

    private static DateOnly? ParseSurveyDate(string? raw)
    {
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed;

        return null;
    }

    private static DateTimeOffset? ReadSurveyDateTime(JsonElement item, string fieldName)
    {
        var raw = ReadString(item, fieldName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
            return offset;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            return new DateTimeOffset(dateTime);

        return null;
    }

    private static DateTimeOffset GetSurveyBogotaNow()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(utcNow, timezone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return utcNow;
    }

    private static string FormatSurveyDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private sealed class SoporteCloudSurveyMetadata
    {
        public RhEntityMetadata Topic { get; init; } = new();
        public RhEntityMetadata Question { get; init; } = new();
        public RhEntityMetadata Option { get; init; } = new();
        public RhEntityMetadata Session { get; init; } = new();
        public RhEntityMetadata Participant { get; init; } = new();
        public RhEntityMetadata Answer { get; init; } = new();
        public bool HasSessionScanCountField { get; init; }
        public string QuestionTopicNavigationProperty { get; init; } = SurveyQuestionTopicField;
        public string OptionQuestionNavigationProperty { get; init; } = SurveyOptionQuestionField;
        public string SessionTopicNavigationProperty { get; init; } = SurveySessionTopicField;
        public string SessionClientNavigationProperty { get; init; } = SurveySessionClientField;
        public string ParticipantSessionNavigationProperty { get; init; } = SurveyParticipantSessionField;
        public string AnswerSessionNavigationProperty { get; init; } = SurveyAnswerSessionField;
        public string AnswerParticipantNavigationProperty { get; init; } = SurveyAnswerParticipantField;
        public string AnswerQuestionNavigationProperty { get; init; } = SurveyAnswerQuestionField;
        public string AnswerOptionNavigationProperty { get; init; } = SurveyAnswerOptionField;
    }

    private sealed class SurveyContext
    {
        public IReadOnlyList<SoporteCloudSurveyTopicDto> Topics { get; init; } = Array.Empty<SoporteCloudSurveyTopicDto>();
        public IReadOnlyList<SoporteCloudSurveyQuestionDto> Questions { get; init; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
        public IReadOnlyList<SoporteCloudSurveySessionDto> Sessions { get; init; } = Array.Empty<SoporteCloudSurveySessionDto>();
        public IReadOnlyList<SoporteCloudSurveyParticipantDto> Participants { get; init; } = Array.Empty<SoporteCloudSurveyParticipantDto>();
        public IReadOnlyList<SurveyAnswerRaw> Answers { get; init; } = Array.Empty<SurveyAnswerRaw>();
    }

    private sealed class SurveyTopicRaw
    {
        public string TopicId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsActive { get; init; } = true;
    }

    private sealed class SurveyQuestionRaw
    {
        public string QuestionId { get; init; } = "";
        public string TopicId { get; init; } = "";
        public string TopicName { get; init; } = "";
        public int ComponentValue { get; init; }
        public int InputTypeValue { get; init; }
        public string Text { get; init; } = "";
        public int SortOrder { get; init; }
        public decimal MaxPoints { get; init; }
        public bool IsActive { get; init; } = true;
    }

    private sealed class SurveyOptionRaw
    {
        public string OptionId { get; init; } = "";
        public string QuestionId { get; init; } = "";
        public string Text { get; init; } = "";
        public bool IsCorrect { get; init; }
        public decimal Points { get; init; }
        public int SortOrder { get; init; }
        public bool IsActive { get; init; } = true;
    }

    private sealed class SurveyAnswerRaw
    {
        public string AnswerId { get; init; } = "";
        public string SessionId { get; init; } = "";
        public string ParticipantId { get; init; } = "";
        public string QuestionId { get; init; } = "";
        public string OptionId { get; init; } = "";
        public int ComponentValue { get; init; }
        public decimal Points { get; init; }
        public decimal MaxPoints { get; init; }
        public bool IsCorrect { get; init; }
        public decimal? NumericValue { get; init; }
        public string TextValue { get; init; } = "";
    }

    private sealed class SurveyComputedAnswer
    {
        public SoporteCloudSurveyQuestionDto Question { get; init; } = new();
        public string OptionId { get; init; } = "";
        public decimal? NumericValue { get; init; }
        public string TextValue { get; init; } = "";
        public decimal Points { get; init; }
        public decimal MaxPoints { get; init; }
        public bool IsCorrect { get; init; }
    }

    private sealed class SurveySatisfactionQuestionSeed
    {
        public SurveySatisfactionQuestionSeed(string text, int inputTypeValue, int sortOrder)
        {
            Text = text;
            InputTypeValue = inputTypeValue;
            SortOrder = sortOrder;
        }

        public string Text { get; }
        public int InputTypeValue { get; }
        public int SortOrder { get; }
    }
}
