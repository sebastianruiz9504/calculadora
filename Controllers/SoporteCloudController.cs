using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.SoporteCloud;
using CotizadorInterno.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using QRCoder;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.SoporteCloud)]
public sealed class SoporteCloudController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const string LivePhaseRegistration = "registration";
    private const string LivePhaseIntro = "intro";
    private const string LivePhaseQuestion = "question";
    private const string LivePhaseRanking = "ranking";
    private const string LivePhaseWinners = "winners";
    private const string LivePhaseSurvey = "survey";
    private const string LivePhaseClosed = "closed";
    private const string LivePhaseRemoved = "removed";
    private const int SurveyComponentKnowledge = 645250000;
    private const int SurveyInputSingleChoice = 645250000;
    private const int SurveyInputRating = 645250001;
    private const int SurveyInputText = 645250002;
    private const int SurveyInputMultipleChoice = 645250003;
    private const int SurveyInputMatching = 645250004;
    private const int SurveySessionStateOpen = 645250001;
    private const int SurveySessionStateClosed = 645250002;
    private const string SurveyMatchingSeparator = "|||";
    private const decimal LiveQuestionMaxPoints = 10m;
    private const decimal LiveQuestionSubmitGraceSeconds = 1.5m;
    private static readonly TimeSpan LiveQuestionDuration = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<string, LiveSurveySessionState> LiveSurveySessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDataverseService _dataverse;

    public SoporteCloudController(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var today = ResolveBogotaToday();
        var startDate = new DateOnly(today.Year, today.Month, 1);
        var model = new SoporteCloudPageViewModel
        {
            CurrentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo(),
            InitialStartDate = startDate.ToString("yyyy-MM-dd"),
            InitialEndDate = today.ToString("yyyy-MM-dd")
        };

        return View(model);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Data([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct)
    {
        try
        {
            var result = await _dataverse.GetSoporteCloudBoardAsync(startDate, endDate, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar los tickets de soporte cloud.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery] string q, CancellationToken ct)
    {
        try
        {
            var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
            return Ok(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible buscar clientes.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Trainings([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromQuery] bool all, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.GetSoporteCloudTrainingsBoardAsync(startDate, endDate, ct, includeAll: all));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar las capacitaciones de soporte cloud.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveTraining([FromBody] SoporteCloudTrainingSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la capacitacion a guardar."));

        try
        {
            return Ok(await _dataverse.SaveSoporteCloudTrainingAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la capacitacion.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ReportClients(CancellationToken ct)
    {
        try
        {
            var items = await _dataverse.SearchClientsAsync("", top: 5000, ct: ct);
            return Ok(items);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar los clientes para reportes.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Save([FromBody] SoporteCloudSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el ticket a guardar."));

        try
        {
            var result = await _dataverse.SaveSoporteCloudTicketAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar el ticket de soporte cloud.", ex));
        }
    }

    [HttpDelete]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Delete([FromQuery] string recordId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recordId))
            return BadRequest(CreateErrorPayload("Debes indicar el ticket a eliminar."));

        try
        {
            return Ok(await _dataverse.DeleteSoporteCloudTicketAsync(recordId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible eliminar el ticket de soporte cloud.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(134217728)]
    [RequestFormLimits(MultipartBodyLengthLimit = 134217728)]
    public async Task<IActionResult> UploadFile(string recordId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(CreateErrorPayload("Debes seleccionar un archivo valido."));

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            var result = await _dataverse.UploadSoporteCloudAttachmentAsync(
                recordId,
                file.FileName,
                file.ContentType,
                buffer.ToArray(),
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el adjunto del ticket.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DownloadFile(string recordId, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadSoporteCloudAttachmentAsync(recordId, ct);
            if (file is null || file.Content.Length == 0)
                return NotFound();

            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible descargar el adjunto del ticket.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SurveyBoard(CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.GetSoporteCloudSurveyBoardAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar las encuestas de capacitacion.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SurveySessionDetail([FromQuery] string sessionId, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.GetSoporteCloudSurveySessionDetailAsync(sessionId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el detalle de la sesion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveSurveyTopic([FromBody] SoporteCloudSurveyTopicSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar el tema a guardar."));

        try
        {
            return Ok(await _dataverse.SaveSoporteCloudSurveyTopicAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar el tema.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveSurveyQuestion([FromBody] SoporteCloudSurveyQuestionSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la pregunta a guardar."));

        try
        {
            return Ok(await _dataverse.SaveSoporteCloudSurveyQuestionAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la pregunta.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DeleteSurveyQuestion([FromQuery] string questionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return BadRequest(CreateErrorPayload("Debes indicar la pregunta a eliminar."));

        try
        {
            return Ok(await _dataverse.DeleteSoporteCloudSurveyQuestionAsync(questionId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible eliminar la pregunta.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveSurveySession([FromBody] SoporteCloudSurveySessionSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la sesion a guardar."));

        try
        {
            return Ok(await _dataverse.SaveSoporteCloudSurveySessionAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar la sesion.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> StartLiveSurvey([FromBody] SoporteCloudSurveySessionSaveRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar la sesion a iniciar."));

        try
        {
            var saveResult = await _dataverse.SaveSoporteCloudSurveySessionAsync(request, ct);
            var session = FindSavedSurveySession(saveResult.Board, request)
                ?? throw new InvalidOperationException("La sesion fue guardada, pero no pudimos resolver el codigo publico.");
            var liveState = EnsureLiveSurveyState(session);
            var topicQuestions = ResolveSessionKnowledgeQuestions(saveResult.Board, session);
            RememberLiveSurveyQuestions(liveState, topicQuestions);

            return Ok(new SoporteCloudLiveSurveyStartResultDto
            {
                Message = "Sesion live iniciada. Muestra el QR para registrar participantes.",
                Board = saveResult.Board,
                Session = session,
                State = BuildLiveSurveyStateDto(liveState, session, topicQuestions)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible iniciar la encuesta live.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> LiveSurveyState([FromQuery] string sessionId, CancellationToken ct)
    {
        try
        {
            var (liveState, session, questions) = await ResolveLiveSurveySessionStateAsync(sessionId, ct);
            return Ok(BuildLiveSurveyStateDto(liveState, session, questions));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el estado live.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> AdvanceLiveSurvey([FromQuery] string sessionId, CancellationToken ct)
    {
        try
        {
            var (liveState, session, questions) = await ResolveLiveSurveySessionStateAsync(sessionId, ct);

            lock (liveState.SyncRoot)
            {
                AdvanceLiveTimedPhaseIfDue(liveState);
                if (session.StateValue == SurveySessionStateClosed || liveState.Phase == LivePhaseClosed)
                {
                    liveState.Phase = LivePhaseClosed;
                    TouchLiveState(liveState);
                }
                else if (liveState.Phase == LivePhaseRegistration)
                {
                    liveState.Phase = LivePhaseIntro;
                    TouchLiveState(liveState);
                }
                else if (liveState.Phase == LivePhaseIntro)
                {
                    if (questions.Count == 0)
                    {
                        liveState.Phase = LivePhaseSurvey;
                        liveState.CurrentQuestionIndex = -1;
                        TouchLiveState(liveState);
                    }
                    else
                    {
                        StartLiveQuestion(liveState, questions, 0);
                    }
                }
                else if (liveState.Phase == LivePhaseRanking)
                {
                    if (liveState.PendingPhase == LivePhaseQuestion && liveState.PendingQuestionIndex >= 0)
                    {
                        StartLiveQuestion(liveState, questions, liveState.PendingQuestionIndex);
                    }
                    else
                    {
                        liveState.Phase = LivePhaseWinners;
                        liveState.CurrentQuestionIndex = -1;
                        liveState.CurrentQuestionId = "";
                        liveState.PendingPhase = LivePhaseSurvey;
                        liveState.PendingQuestionIndex = -1;
                        liveState.RankingEndsOnUtc = null;
                        liveState.QuestionStartedOnUtc = null;
                        TouchLiveState(liveState);
                    }
                }
                else if (liveState.Phase == LivePhaseWinners)
                {
                    liveState.Phase = LivePhaseSurvey;
                    liveState.CurrentQuestionIndex = -1;
                    liveState.CurrentQuestionId = "";
                    liveState.PendingPhase = "";
                    liveState.PendingQuestionIndex = -1;
                    liveState.RankingEndsOnUtc = null;
                    liveState.QuestionStartedOnUtc = null;
                    TouchLiveState(liveState);
                }
                else if (liveState.Phase == LivePhaseSurvey)
                {
                    liveState.Phase = LivePhaseClosed;
                    liveState.CurrentQuestionIndex = -1;
                    liveState.CurrentQuestionId = "";
                    liveState.PendingPhase = "";
                    liveState.PendingQuestionIndex = -1;
                    liveState.RankingEndsOnUtc = null;
                    liveState.QuestionStartedOnUtc = null;
                    TouchLiveState(liveState);
                }
            }

            return Ok(BuildLiveSurveyStateDto(liveState, session, questions));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible avanzar la encuesta live.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CloseLiveSurvey([FromQuery] string sessionId, [FromBody] SoporteCloudSurveyCloseSessionRequest? request, CancellationToken ct)
    {
        try
        {
            var hasRememberedState = TryGetRememberedLiveSurveyBySessionId(sessionId, out var rememberedState, out _);
            if (hasRememberedState)
                await PersistLiveKnowledgeResultsAsync(rememberedState, ct);

            var result = await _dataverse.CloseSoporteCloudSurveySessionAsync(sessionId, request?.DurationMinutes, ct);
            var session = FindSurveySession(result.Board, sessionId);
            var liveStateToClose = session is not null
                ? EnsureLiveSurveyState(session)
                : hasRememberedState ? rememberedState : null;
            if (liveStateToClose is not null)
            {
                lock (liveStateToClose.SyncRoot)
                {
                    liveStateToClose.Phase = LivePhaseClosed;
                    liveStateToClose.Sequence++;
                    liveStateToClose.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cerrar la encuesta live.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> RemoveLiveParticipant([FromQuery] string sessionId, [FromQuery] string participantKey, CancellationToken ct)
    {
        var normalizedKey = NormalizeLiveKey(participantKey);
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(normalizedKey))
            return BadRequest(CreateErrorPayload("Debes indicar la sesion y el participante a retirar."));

        try
        {
            var (liveState, session, questions) = await ResolveLiveSurveySessionStateAsync(sessionId, ct);

            lock (liveState.SyncRoot)
            {
                liveState.Participants.TryRemove(normalizedKey, out _);
                liveState.RemovedParticipants[normalizedKey] = DateTimeOffset.UtcNow;
                TouchLiveState(liveState);
            }

            return Ok(BuildLiveSurveyStateDto(liveState, session, questions));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible retirar el participante.", ex));
        }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CloseSurveySession([FromQuery] string sessionId, [FromBody] SoporteCloudSurveyCloseSessionRequest? request, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.CloseSoporteCloudSurveySessionAsync(sessionId, request?.DurationMinutes, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cerrar la sesion.", ex));
        }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SurveyExport([FromQuery] string sessionId, CancellationToken ct)
    {
        try
        {
            var detail = await _dataverse.GetSoporteCloudSurveySessionDetailAsync(sessionId, ct);
            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet("Respuestas");
            var questions = detail.KnowledgeQuestionStats
                .OrderBy(item => item.QuestionText, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var headers = new[] { "Sesion", "Tema", "Cliente", "Participante", "Email", "Empresa", "Fecha respuesta", "Puntaje", "Porcentaje" };
            for (var i = 0; i < headers.Length; i++)
                worksheet.Cell(1, i + 1).Value = headers[i];
            for (var i = 0; i < questions.Count; i++)
                worksheet.Cell(1, headers.Length + i + 1).Value = questions[i].QuestionText;

            var rowIndex = 2;
            foreach (var participant in detail.Participants.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
            {
                worksheet.Cell(rowIndex, 1).Value = detail.Session.Name;
                worksheet.Cell(rowIndex, 2).Value = detail.Session.TopicName;
                worksheet.Cell(rowIndex, 3).Value = detail.Session.ClientName;
                worksheet.Cell(rowIndex, 4).Value = participant.FullName;
                worksheet.Cell(rowIndex, 5).Value = participant.Email;
                worksheet.Cell(rowIndex, 6).Value = participant.Company;
                worksheet.Cell(rowIndex, 7).Value = participant.SubmittedOnDisplay;
                worksheet.Cell(rowIndex, 8).Value = participant.Score;
                worksheet.Cell(rowIndex, 9).Value = participant.ScorePercent / 100m;
                worksheet.Cell(rowIndex, 9).Style.NumberFormat.Format = "0.00%";

                var answersByQuestion = detail.KnowledgeAnswers
                    .Where(answer => string.Equals(answer.ParticipantId, participant.ParticipantId, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(answer => answer.QuestionId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < questions.Count; i++)
                {
                    answersByQuestion.TryGetValue(questions[i].QuestionId, out var answer);
                    worksheet.Cell(rowIndex, headers.Length + i + 1).Value = answer is null
                        ? ""
                        : $"{answer.AnswerText} ({(answer.IsCorrect ? "Correcta" : "Incorrecta")}, {answer.Points:N2} pts)";
                }

                rowIndex++;
            }

            worksheet.Range(1, 1, 1, headers.Length + questions.Count).Style.Font.Bold = true;
            worksheet.RangeUsed()?.SetAutoFilter();
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"Reporte-Encuesta-{detail.Session.Code}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible exportar el reporte.", ex));
        }
    }

    [AllowAnonymous]
    [HttpGet("SoporteCloud/Encuesta/{code}")]
    public async Task<IActionResult> Encuesta([FromRoute] string code, CancellationToken ct)
    {
        try
        {
            var model = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct);
            return View("Encuesta", model);
        }
        catch (Exception ex)
        {
            return View("Encuesta", new SoporteCloudPublicSurveyViewModel
            {
                Code = code,
                IsClosed = true,
                Message = BuildExceptionDetail(ex)
            });
        }
    }

    [AllowAnonymous]
    [HttpPost("SoporteCloud/Encuesta/{code}/Responder")]
    public async Task<IActionResult> SubmitSurvey([FromRoute] string code, [FromBody] SoporteCloudSurveySubmitRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar tus respuestas."));

        request.Code = code;
        try
        {
            ApplyLiveScoreOverrides(code, request);
            return Ok(await _dataverse.SubmitSoporteCloudPublicSurveyAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible guardar tus respuestas.", ex));
        }
    }

    [AllowAnonymous]
    [HttpGet("SoporteCloud/Encuesta/{code}/Resultados")]
    public async Task<IActionResult> PublicSurveyResults([FromRoute] string code, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.GetSoporteCloudPublicSurveyResultsAsync(code, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar los resultados.", ex));
        }
    }

    [AllowAnonymous]
    [HttpGet("SoporteCloud/Encuesta/{code}/LiveState")]
    public async Task<IActionResult> PublicLiveSurveyState([FromRoute] string code, [FromQuery] string participantKey, CancellationToken ct)
    {
        try
        {
            if (TryGetRememberedLiveSurvey(code, out var rememberedState, out var rememberedQuestions)
                && rememberedQuestions.Count > 0)
            {
                var snapshot = CreateLiveSurveySnapshot(rememberedState, rememberedQuestions);
                return Ok(BuildLiveSurveyStateDto(rememberedState, snapshot, rememberedQuestions, participantKey));
            }

            var survey = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct, trackScan: false);
            var liveState = EnsureLiveSurveyState(survey);
            return Ok(BuildLiveSurveyStateDto(liveState, survey, survey.KnowledgeQuestions, participantKey));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible cargar el estado live.", ex));
        }
    }

    [AllowAnonymous]
    [HttpPost("SoporteCloud/Encuesta/{code}/LiveRegister")]
    public async Task<IActionResult> PublicLiveSurveyRegister([FromRoute] string code, [FromBody] SoporteCloudLiveSurveyRegisterRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar tus datos de registro."));

        var fullName = (request.FullName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            return BadRequest(CreateErrorPayload("Debes indicar tu nombre."));
        var email = (request.Email ?? "").Trim();
        if (!IsCorporateEmail(email))
            return BadRequest(CreateErrorPayload("Ingresa un correo corporativo valido. No se permiten correos personales."));

        try
        {
            SoporteCloudPublicSurveyViewModel survey;
            LiveSurveySessionState liveState;
            IReadOnlyList<SoporteCloudSurveyQuestionDto> questions;
            if (TryGetRememberedLiveSurvey(code, out var rememberedState, out var rememberedQuestions))
            {
                liveState = rememberedState;
                questions = rememberedQuestions;
                survey = CreateLiveSurveySnapshot(liveState, questions);
            }
            else
            {
                survey = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct, trackScan: false);
                liveState = EnsureLiveSurveyState(survey);
                questions = survey.KnowledgeQuestions;
            }

            if (survey.IsClosed || liveState.Phase == LivePhaseClosed)
                return BadRequest(CreateErrorPayload("La encuesta ya fue cerrada."));

            var participantKey = BuildParticipantKey(request);
            if (liveState.RemovedParticipants.ContainsKey(participantKey))
                return BadRequest(CreateErrorPayload("Tu registro fue retirado por el organizador de la sesion."));

            liveState.Participants.AddOrUpdate(
                participantKey,
                _ => new LiveSurveyParticipantState
                {
                    ParticipantKey = participantKey,
                    FullName = fullName,
                    Email = email,
                    Company = (request.Company ?? "").Trim(),
                    Identification = (request.Identification ?? "").Trim(),
                    Role = (request.Role ?? "").Trim(),
                    RegisteredAt = DateTimeOffset.UtcNow
                },
                (_, existing) =>
                {
                    existing.FullName = fullName;
                    existing.Email = email;
                    existing.Company = (request.Company ?? "").Trim();
                    existing.Identification = (request.Identification ?? "").Trim();
                    existing.Role = (request.Role ?? "").Trim();
                    return existing;
                });

            lock (liveState.SyncRoot)
            {
                liveState.Sequence++;
                liveState.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Ok(new SoporteCloudLiveSurveyRegisterResultDto
            {
                ParticipantKey = participantKey,
                Message = "Registro recibido. Espera a que el presentador inicie.",
                State = BuildLiveSurveyStateDto(liveState, survey, questions, participantKey)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible registrar el participante.", ex));
        }
    }

    [AllowAnonymous]
    [HttpPost("SoporteCloud/Encuesta/{code}/LiveAnswer")]
    public async Task<IActionResult> PublicLiveSurveyAnswer([FromRoute] string code, [FromBody] SoporteCloudLiveSurveyAnswerRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes enviar la respuesta."));

        var receivedAt = DateTimeOffset.UtcNow;
        try
        {
            SoporteCloudPublicSurveyViewModel survey;
            LiveSurveySessionState liveState;
            IReadOnlyList<SoporteCloudSurveyQuestionDto> questions;
            if (TryGetRememberedLiveSurvey(code, out var rememberedState, out var rememberedQuestions)
                && rememberedQuestions.Count > 0)
            {
                liveState = rememberedState;
                questions = rememberedQuestions;
                survey = CreateLiveSurveySnapshot(liveState, questions);
            }
            else
            {
                survey = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct, trackScan: false);
                liveState = EnsureLiveSurveyState(survey);
                questions = survey.KnowledgeQuestions;
            }

            if (survey.IsClosed || liveState.Phase == LivePhaseClosed)
                return BadRequest(CreateErrorPayload("La encuesta ya fue cerrada."));

            var participantKey = BuildParticipantKey(request);
            if (string.IsNullOrWhiteSpace(participantKey)
                || !liveState.Participants.TryGetValue(participantKey, out var participant))
                return BadRequest(CreateErrorPayload("Debes registrarte antes de responder."));

            var questionId = NormalizeOptionalGuidLocal(request.QuestionId);
            var question = questions.FirstOrDefault(item =>
                string.Equals(item.QuestionId, questionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("No encontramos la pregunta enviada.");

            LiveSurveyAnswerState answer;
            lock (liveState.SyncRoot)
            {
                AdvanceLiveTimedPhaseIfDue(liveState, receivedAt);
                var activeQuestionId = liveState.Phase == LivePhaseQuestion
                    ? liveState.CurrentQuestionId
                    : "";
                if (string.IsNullOrWhiteSpace(activeQuestionId))
                {
                    activeQuestionId = questions
                        .ElementAtOrDefault(Math.Max(liveState.CurrentQuestionIndex, 0))
                        ?.QuestionId ?? "";
                }
                if (!string.Equals(activeQuestionId, question.QuestionId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("El tiempo de esta pregunta ya finalizo.");

                var startedOn = liveState.QuestionStartedOnUtc ?? DateTimeOffset.UtcNow;
                var responseSeconds = Math.Max(0m, (decimal)(receivedAt - startedOn).TotalSeconds);
                if (responseSeconds > (decimal)LiveQuestionDuration.TotalSeconds + LiveQuestionSubmitGraceSeconds)
                {
                    AdvanceLiveTimedPhaseIfDue(liveState);
                    throw new InvalidOperationException("El tiempo de respuesta finalizo.");
                }

                answer = BuildLiveAnswerState(question, request, responseSeconds);
                lock (participant.SyncRoot)
                {
                    if (participant.Answers.TryGetValue(question.QuestionId, out var previous))
                    {
                        participant.Score -= previous.Points;
                        participant.MaxScore -= previous.MaxPoints;
                    }

                    participant.Answers[question.QuestionId] = answer;
                    participant.Score += answer.Points;
                    participant.MaxScore += answer.MaxPoints;
                }

                liveState.Sequence++;
                liveState.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Ok(BuildLiveSurveyStateDto(liveState, survey, questions, participantKey));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible registrar la respuesta live.", ex));
        }
    }

    [AllowAnonymous]
    [HttpPost("SoporteCloud/Encuesta/{code}/LiveComplete")]
    public async Task<IActionResult> PublicLiveSurveyComplete([FromRoute] string code, [FromBody] SoporteCloudLiveSurveyCompleteRequest? request, CancellationToken ct)
    {
        try
        {
            SoporteCloudPublicSurveyViewModel survey;
            LiveSurveySessionState liveState;
            IReadOnlyList<SoporteCloudSurveyQuestionDto> questions;
            if (TryGetRememberedLiveSurvey(code, out var rememberedState, out var rememberedQuestions))
            {
                liveState = rememberedState;
                questions = rememberedQuestions;
                survey = CreateLiveSurveySnapshot(liveState, questions);
            }
            else
            {
                survey = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct, trackScan: false);
                liveState = EnsureLiveSurveyState(survey);
                questions = survey.KnowledgeQuestions;
            }

            var participantKey = BuildParticipantKey(request);
            if (!string.IsNullOrWhiteSpace(participantKey))
            {
                var totalKnowledgePoints = questions.Count * LiveQuestionMaxPoints;
                liveState.Participants.AddOrUpdate(
                    participantKey,
                    _ => new LiveSurveyParticipantState
                    {
                        ParticipantKey = participantKey,
                        FullName = (request?.FullName ?? "").Trim(),
                        Email = (request?.Email ?? "").Trim(),
                        MaxScore = totalKnowledgePoints,
                        Completed = true,
                        CompletedAt = DateTimeOffset.UtcNow
                    },
                    (_, existing) =>
                    {
                        existing.Completed = true;
                        existing.CompletedAt = DateTimeOffset.UtcNow;
                        existing.MaxScore = Math.Max(existing.MaxScore, totalKnowledgePoints);
                        return existing;
                    });
            }

            lock (liveState.SyncRoot)
            {
                liveState.Sequence++;
                liveState.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return Ok(BuildLiveSurveyStateDto(liveState, survey, questions, participantKey));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible actualizar el estado del participante.", ex));
        }
    }

    [AllowAnonymous]
    [HttpPost("SoporteCloud/Encuesta/{code}/LiveWheel")]
    public async Task<IActionResult> PublicLiveWheelSpin([FromRoute] string code, [FromBody] SoporteCloudLiveWheelSpinRequest? request, CancellationToken ct)
    {
        try
        {
            SoporteCloudPublicSurveyViewModel survey;
            LiveSurveySessionState liveState;
            IReadOnlyList<SoporteCloudSurveyQuestionDto> questions;
            if (TryGetRememberedLiveSurvey(code, out var rememberedState, out var rememberedQuestions))
            {
                liveState = rememberedState;
                questions = rememberedQuestions;
                survey = CreateLiveSurveySnapshot(liveState, questions);
            }
            else
            {
                survey = await _dataverse.GetSoporteCloudPublicSurveyAsync(code, ct, trackScan: false);
                liveState = EnsureLiveSurveyState(survey);
                questions = survey.KnowledgeQuestions;
            }

            var participantKey = NormalizeLiveKey(request?.ParticipantKey);
            if (string.IsNullOrWhiteSpace(participantKey))
                return BadRequest(CreateErrorPayload("Debes registrarte antes de girar la ruleta."));

            var number = 0;
            lock (liveState.SyncRoot)
            {
                var participant = liveState.Participants.GetOrAdd(
                    participantKey,
                    _ => new LiveSurveyParticipantState
                    {
                        ParticipantKey = participantKey,
                        FullName = (request?.FullName ?? "").Trim(),
                        Email = (request?.Email ?? "").Trim(),
                        RegisteredAt = DateTimeOffset.UtcNow
                    });

                lock (participant.SyncRoot)
                {
                    if (participant.WheelNumber is null)
                    {
                        participant.WheelNumber = RandomNumberGenerator.GetInt32(1, 101);
                        participant.WheelSpunAt = DateTimeOffset.UtcNow;
                    }

                    number = participant.WheelNumber.Value;
                }

                TouchLiveState(liveState);
            }

            return Ok(new SoporteCloudLiveWheelSpinResultDto
            {
                Number = number,
                State = BuildLiveSurveyStateDto(liveState, survey, questions, participantKey)
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, CreateErrorPayload("No fue posible registrar la ruleta.", ex));
        }
    }

    [AllowAnonymous]
    [HttpGet("SoporteCloud/Encuesta/{code}/Qr")]
    public IActionResult SurveyQr([FromRoute] string code)
    {
        var targetUrl = Url.ActionLink("Encuesta", "SoporteCloud", new { code })
            ?? $"{Request.Scheme}://{Request.Host}/SoporteCloud/Encuesta/{Uri.EscapeDataString(code)}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        var svg = qr.GetGraphic(5, "#17263c", "#ffffff", drawQuietZones: true);
        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    private static SoporteCloudSurveySessionDto? FindSavedSurveySession(SoporteCloudSurveyBoardDto board, SoporteCloudSurveySessionSaveRequest request)
    {
        var sessions = board.Sessions ?? Array.Empty<SoporteCloudSurveySessionDto>();
        if (!string.IsNullOrWhiteSpace(request.SessionId))
            return FindSurveySession(board, request.SessionId);

        return sessions.FirstOrDefault(session =>
                string.Equals(session.Name, request.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.TopicId, request.TopicId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(request.DateValue) || string.Equals(session.DateValue, request.DateValue, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(request.ClientId) || string.Equals(session.ClientId, request.ClientId, StringComparison.OrdinalIgnoreCase)))
            ?? sessions.FirstOrDefault(session =>
                string.Equals(session.Name, request.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.TopicId, request.TopicId, StringComparison.OrdinalIgnoreCase));
    }

    private static SoporteCloudSurveySessionDto? FindSurveySession(SoporteCloudSurveyBoardDto board, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        return (board.Sessions ?? Array.Empty<SoporteCloudSurveySessionDto>())
            .FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SoporteCloudSurveyQuestionDto> ResolveSessionKnowledgeQuestions(
        SoporteCloudSurveyBoardDto board,
        SoporteCloudSurveySessionDto session)
    {
        return (board.Questions ?? Array.Empty<SoporteCloudSurveyQuestionDto>())
            .Where(question => question.ComponentValue == 645250000
                && question.IsActive
                && string.Equals(question.TopicId, session.TopicId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<(LiveSurveySessionState LiveState, SoporteCloudSurveySessionDto Session, IReadOnlyList<SoporteCloudSurveyQuestionDto> Questions)> ResolveLiveSurveySessionStateAsync(
        string sessionId,
        CancellationToken ct)
    {
        if (TryGetRememberedLiveSurveyBySessionId(sessionId, out var rememberedState, out var rememberedQuestions))
        {
            return (
                rememberedState,
                CreateLiveSurveySessionSnapshot(rememberedState),
                rememberedQuestions);
        }

        var board = await _dataverse.GetSoporteCloudSurveyBoardAsync(ct);
        var session = FindSurveySession(board, sessionId)
            ?? throw new InvalidOperationException("No encontramos la sesion live solicitada.");
        var liveState = EnsureLiveSurveyState(session);
        var questions = ResolveSessionKnowledgeQuestions(board, session);
        RememberLiveSurveyQuestions(liveState, questions);
        return (liveState, session, questions);
    }

    private static LiveSurveySessionState EnsureLiveSurveyState(SoporteCloudSurveySessionDto session)
    {
        var code = NormalizeLiveCode(session.Code);
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("La sesion no tiene codigo publico.");

        var state = LiveSurveySessions.GetOrAdd(code, _ => new LiveSurveySessionState
        {
            Code = code,
            SessionId = session.SessionId,
            SessionName = session.Name,
            TopicName = session.TopicName,
            PublicUrl = session.PublicUrl,
            Phase = session.StateValue == SurveySessionStateClosed ? LivePhaseClosed : LivePhaseRegistration,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        lock (state.SyncRoot)
        {
            state.SessionId = session.SessionId;
            state.SessionName = session.Name;
            state.TopicName = session.TopicName;
            state.PublicUrl = session.PublicUrl;
            if (session.StateValue == SurveySessionStateClosed)
                state.Phase = LivePhaseClosed;
        }

        return state;
    }

    private static LiveSurveySessionState EnsureLiveSurveyState(SoporteCloudPublicSurveyViewModel survey)
    {
        var code = NormalizeLiveCode(survey.Code);
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("La encuesta no tiene codigo publico.");

        var state = LiveSurveySessions.GetOrAdd(code, _ => new LiveSurveySessionState
        {
            Code = code,
            SessionId = survey.SessionId,
            SessionName = survey.SessionName,
            TopicName = survey.TopicName,
            PublicUrl = $"/SoporteCloud/Encuesta/{Uri.EscapeDataString(code)}",
            Phase = survey.IsClosed ? LivePhaseClosed : LivePhaseRegistration,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        lock (state.SyncRoot)
        {
            state.SessionId = survey.SessionId;
            state.SessionName = survey.SessionName;
            state.TopicName = survey.TopicName;
            state.PublicUrl = $"/SoporteCloud/Encuesta/{Uri.EscapeDataString(code)}";
            state.KnowledgeQuestions = survey.KnowledgeQuestions;
            if (survey.IsClosed)
                state.Phase = LivePhaseClosed;
        }

        return state;
    }

    private static void RememberLiveSurveyQuestions(
        LiveSurveySessionState liveState,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        lock (liveState.SyncRoot)
        {
            liveState.KnowledgeQuestions = questions.Count == 0
                ? Array.Empty<SoporteCloudSurveyQuestionDto>()
                : questions.ToList();
        }
    }

    private static bool TryGetRememberedLiveSurvey(
        string code,
        out LiveSurveySessionState liveState,
        out IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        liveState = null!;
        questions = Array.Empty<SoporteCloudSurveyQuestionDto>();
        var normalizedCode = NormalizeLiveCode(code);
        if (string.IsNullOrWhiteSpace(normalizedCode)
            || !LiveSurveySessions.TryGetValue(normalizedCode, out var state))
            return false;

        lock (state.SyncRoot)
        {
            liveState = state;
            questions = state.KnowledgeQuestions;
            return true;
        }
    }

    private static bool TryGetRememberedLiveSurveyBySessionId(
        string sessionId,
        out LiveSurveySessionState liveState,
        out IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        liveState = null!;
        questions = Array.Empty<SoporteCloudSurveyQuestionDto>();
        var normalizedSessionId = NormalizeOptionalGuidLocal(sessionId);
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
            return false;

        foreach (var state in LiveSurveySessions.Values)
        {
            lock (state.SyncRoot)
            {
                if (!string.Equals(state.SessionId, normalizedSessionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                liveState = state;
                questions = state.KnowledgeQuestions;
                return true;
            }
        }

        return false;
    }

    private static SoporteCloudPublicSurveyViewModel CreateLiveSurveySnapshot(
        LiveSurveySessionState liveState,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        return new SoporteCloudPublicSurveyViewModel
        {
            Code = liveState.Code,
            SessionId = liveState.SessionId,
            SessionName = liveState.SessionName,
            TopicName = liveState.TopicName,
            IsClosed = liveState.Phase == LivePhaseClosed,
            KnowledgeQuestions = questions
        };
    }

    private static SoporteCloudSurveySessionDto CreateLiveSurveySessionSnapshot(LiveSurveySessionState liveState)
    {
        var isClosed = liveState.Phase == LivePhaseClosed;
        return new SoporteCloudSurveySessionDto
        {
            SessionId = liveState.SessionId,
            Code = liveState.Code,
            Name = liveState.SessionName,
            TopicName = liveState.TopicName,
            PublicUrl = liveState.PublicUrl,
            StateValue = isClosed ? SurveySessionStateClosed : SurveySessionStateOpen,
            StateLabel = isClosed ? "Cerrada" : "Abierta"
        };
    }

    private static SoporteCloudLiveSurveyStateDto BuildLiveSurveyStateDto(
        LiveSurveySessionState liveState,
        SoporteCloudSurveySessionDto session,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions,
        string participantKey = "")
    {
        lock (liveState.SyncRoot)
        {
            liveState.KnowledgeQuestions = questions;
            AdvanceLiveTimedPhaseIfDue(liveState);
            var isClosed = session.StateValue == SurveySessionStateClosed || liveState.Phase == LivePhaseClosed;
            var normalizedParticipantKey = NormalizeLiveKey(participantKey);
            var wasRemoved = !string.IsNullOrWhiteSpace(normalizedParticipantKey)
                && liveState.RemovedParticipants.ContainsKey(normalizedParticipantKey);
            var phase = wasRemoved ? LivePhaseRemoved : (isClosed ? LivePhaseClosed : liveState.Phase);
            var currentQuestion = phase is LivePhaseQuestion or LivePhaseRanking
                ? ResolveLiveCurrentQuestion(liveState, questions)
                : null;
            var currentQuestionAnsweredCount = currentQuestion is null
                ? 0
                : CountLiveQuestionAnswers(liveState, currentQuestion.QuestionId);

            return new SoporteCloudLiveSurveyStateDto
            {
                SessionId = session.SessionId,
                Code = NormalizeLiveCode(session.Code),
                SessionName = session.Name,
                TopicName = session.TopicName,
                PublicUrl = session.PublicUrl,
                Phase = phase,
                PhaseLabel = ResolveLivePhaseLabel(phase),
                Message = ResolveLivePhaseMessage(phase, liveState.CurrentQuestionIndex, questions.Count),
                Sequence = liveState.Sequence,
                RegisteredCount = liveState.Participants.Count,
                CompletedCount = liveState.Participants.Count(item => item.Value.Completed),
                CurrentQuestionIndex = phase is LivePhaseQuestion or LivePhaseRanking ? liveState.CurrentQuestionIndex : -1,
                CurrentQuestionAnsweredCount = currentQuestionAnsweredCount,
                TotalQuestions = questions.Count,
                IsClosed = isClosed,
                CanAdvance = !isClosed && (phase is LivePhaseRegistration or LivePhaseIntro or LivePhaseRanking or LivePhaseWinners or LivePhaseSurvey),
                ServerNowUtc = DateTimeOffset.UtcNow,
                QuestionStartedOnUtc = phase == LivePhaseQuestion ? liveState.QuestionStartedOnUtc : null,
                QuestionEndsOnUtc = phase == LivePhaseQuestion && liveState.QuestionStartedOnUtc is not null
                    ? liveState.QuestionStartedOnUtc.Value.Add(LiveQuestionDuration)
                    : null,
                QuestionDurationSeconds = (int)LiveQuestionDuration.TotalSeconds,
                RankingEndsOnUtc = phase is LivePhaseRanking or LivePhaseWinners ? liveState.RankingEndsOnUtc : null,
                CurrentQuestion = currentQuestion,
                QuestionResponses = BuildLiveQuestionResponses(liveState, questions),
                Participants = BuildLiveParticipants(liveState),
                Ranking = BuildLiveRanking(liveState),
                WheelRanking = BuildLiveWheelRanking(liveState),
                ParticipantProgress = BuildLiveParticipantProgress(liveState, participantKey)
            };
        }
    }

    private static SoporteCloudLiveSurveyStateDto BuildLiveSurveyStateDto(
        LiveSurveySessionState liveState,
        SoporteCloudPublicSurveyViewModel survey,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions,
        string participantKey = "")
    {
        lock (liveState.SyncRoot)
        {
            liveState.KnowledgeQuestions = questions;
            AdvanceLiveTimedPhaseIfDue(liveState);
            var normalizedParticipantKey = NormalizeLiveKey(participantKey);
            var wasRemoved = !string.IsNullOrWhiteSpace(normalizedParticipantKey)
                && liveState.RemovedParticipants.ContainsKey(normalizedParticipantKey);
            var phase = wasRemoved ? LivePhaseRemoved : (survey.IsClosed ? LivePhaseClosed : liveState.Phase);
            var currentQuestion = phase is LivePhaseQuestion or LivePhaseRanking
                ? ResolveLiveCurrentQuestion(liveState, questions)
                : null;
            var currentQuestionAnsweredCount = currentQuestion is null
                ? 0
                : CountLiveQuestionAnswers(liveState, currentQuestion.QuestionId);
            var includeRanking = phase is LivePhaseRanking or LivePhaseWinners;
            var includeWheelRanking = phase is LivePhaseSurvey or LivePhaseClosed;

            return new SoporteCloudLiveSurveyStateDto
            {
                SessionId = survey.SessionId,
                Code = NormalizeLiveCode(survey.Code),
                SessionName = survey.SessionName,
                TopicName = survey.TopicName,
                PublicUrl = liveState.PublicUrl,
                Phase = phase,
                PhaseLabel = ResolveLivePhaseLabel(phase),
                Message = ResolveLivePhaseMessage(phase, liveState.CurrentQuestionIndex, questions.Count),
                Sequence = liveState.Sequence,
                RegisteredCount = liveState.Participants.Count,
                CompletedCount = liveState.Participants.Count(item => item.Value.Completed),
                CurrentQuestionIndex = phase is LivePhaseQuestion or LivePhaseRanking ? liveState.CurrentQuestionIndex : -1,
                CurrentQuestionAnsweredCount = currentQuestionAnsweredCount,
                TotalQuestions = questions.Count,
                IsClosed = survey.IsClosed || phase == LivePhaseClosed,
                CanAdvance = false,
                ServerNowUtc = DateTimeOffset.UtcNow,
                QuestionStartedOnUtc = phase == LivePhaseQuestion ? liveState.QuestionStartedOnUtc : null,
                QuestionEndsOnUtc = phase == LivePhaseQuestion && liveState.QuestionStartedOnUtc is not null
                    ? liveState.QuestionStartedOnUtc.Value.Add(LiveQuestionDuration)
                    : null,
                QuestionDurationSeconds = (int)LiveQuestionDuration.TotalSeconds,
                RankingEndsOnUtc = phase is LivePhaseRanking or LivePhaseWinners ? liveState.RankingEndsOnUtc : null,
                CurrentQuestion = currentQuestion,
                QuestionResponses = Array.Empty<SoporteCloudLiveSurveyQuestionResponseDto>(),
                Participants = Array.Empty<SoporteCloudLiveSurveyParticipantItemDto>(),
                Ranking = includeRanking
                    ? BuildLiveRanking(liveState).Take(10).ToList()
                    : Array.Empty<SoporteCloudLiveSurveyRankingItemDto>(),
                WheelRanking = includeWheelRanking
                    ? BuildLiveWheelRanking(liveState).Take(10).ToList()
                    : Array.Empty<SoporteCloudLiveWheelRankingItemDto>(),
                ParticipantProgress = BuildLiveParticipantProgress(liveState, participantKey)
            };
        }
    }

    private static string ResolveLivePhaseLabel(string phase) =>
        phase switch
        {
            LivePhaseRegistration => "Registro por QR",
            LivePhaseIntro => "Introduccion",
            LivePhaseQuestion => "Preguntas en curso",
            LivePhaseRanking => "Ranking parcial",
            LivePhaseWinners => "Ganadores",
            LivePhaseSurvey => "Encuesta final",
            LivePhaseClosed => "Cerrada",
            LivePhaseRemoved => "Retirado",
            _ => "Registro por QR"
        };

    private static string ResolveLivePhaseMessage(string phase, int currentQuestionIndex, int totalQuestions) =>
        phase switch
        {
            LivePhaseRegistration => "Muestra el QR y espera el registro de participantes.",
            LivePhaseIntro => "Mantente atento a las preguntas que haremos durante la capacitacion.",
            LivePhaseQuestion => $"Pregunta {Math.Max(currentQuestionIndex, 0) + 1} de {totalQuestions}. Tienen {(int)LiveQuestionDuration.TotalSeconds} segundos para responder.",
            LivePhaseRanking => "Tiempo cerrado. Ranking actualizado antes de la siguiente pregunta.",
            LivePhaseWinners => "Podio final de aprendizaje antes de la encuesta final.",
            LivePhaseSurvey => "Encuesta final y ruleta en curso.",
            LivePhaseClosed => "Gracias por participar. Esperamos verte en nuestras proximas sesiones y en nuestras redes sociales.",
            LivePhaseRemoved => "Tu registro fue retirado por el organizador de la sesion.",
            _ => ""
        };

    private static void AdvanceLiveTimedPhaseIfDue(LiveSurveySessionState liveState, DateTimeOffset? nowUtc = null)
    {
        if (liveState.Phase != LivePhaseQuestion || liveState.QuestionStartedOnUtc is null)
            return;

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (liveState.QuestionStartedOnUtc.Value.Add(LiveQuestionDuration) > now)
            return;

        liveState.Phase = LivePhaseRanking;
        liveState.RankingEndsOnUtc = null;
        TouchLiveState(liveState);
    }

    private static SoporteCloudSurveyQuestionDto? ResolveLiveCurrentQuestion(
        LiveSurveySessionState liveState,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        if (!string.IsNullOrWhiteSpace(liveState.CurrentQuestionId))
        {
            var byId = questions.FirstOrDefault(question =>
                string.Equals(question.QuestionId, liveState.CurrentQuestionId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        return questions.ElementAtOrDefault(Math.Max(liveState.CurrentQuestionIndex, 0));
    }

    private static void StartLiveQuestion(
        LiveSurveySessionState liveState,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions,
        int questionIndex)
    {
        var safeIndex = Math.Max(questionIndex, 0);
        var question = questions.ElementAtOrDefault(safeIndex);
        liveState.Phase = LivePhaseQuestion;
        liveState.CurrentQuestionIndex = safeIndex;
        liveState.CurrentQuestionId = question?.QuestionId ?? "";
        liveState.QuestionStartedOnUtc = DateTimeOffset.UtcNow;
        liveState.RankingEndsOnUtc = null;
        liveState.PendingPhase = liveState.CurrentQuestionIndex + 1 < questions.Count ? LivePhaseQuestion : LivePhaseWinners;
        liveState.PendingQuestionIndex = liveState.PendingPhase == LivePhaseQuestion ? liveState.CurrentQuestionIndex + 1 : -1;
        TouchLiveState(liveState);
    }

    private static void TouchLiveState(LiveSurveySessionState liveState)
    {
        liveState.Sequence++;
        liveState.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static int CountLiveQuestionAnswers(LiveSurveySessionState liveState, string questionId)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            return 0;

        return liveState.Participants.Values.Count(participant => participant.Answers.ContainsKey(questionId));
    }

    private static IReadOnlyList<SoporteCloudLiveSurveyQuestionResponseDto> BuildLiveQuestionResponses(
        LiveSurveySessionState liveState,
        IReadOnlyList<SoporteCloudSurveyQuestionDto> questions)
    {
        var registeredCount = liveState.Participants.Count;
        return questions
            .Select((question, index) => new SoporteCloudLiveSurveyQuestionResponseDto
            {
                QuestionId = question.QuestionId,
                QuestionText = question.Text,
                QuestionIndex = index,
                AnsweredCount = CountLiveQuestionAnswers(liveState, question.QuestionId),
                RegisteredCount = registeredCount
            })
            .ToList();
    }

    private static IReadOnlyList<SoporteCloudLiveSurveyParticipantItemDto> BuildLiveParticipants(LiveSurveySessionState liveState)
    {
        return liveState.Participants.Values
            .Select(participant =>
            {
                lock (participant.SyncRoot)
                {
                    return new SoporteCloudLiveSurveyParticipantItemDto
                    {
                        ParticipantKey = participant.ParticipantKey,
                        FullName = participant.FullName,
                        Email = participant.Email,
                        Company = participant.Company,
                        Role = participant.Role,
                        Score = participant.Score,
                        AnsweredCount = participant.Answers.Count,
                        Completed = participant.Completed,
                        WheelNumber = participant.WheelNumber
                    };
                }
            })
            .OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SoporteCloudLiveSurveyRankingItemDto> BuildLiveRanking(LiveSurveySessionState liveState)
    {
        return liveState.Participants.Values
            .Select(participant =>
            {
                lock (participant.SyncRoot)
                {
                    return new SoporteCloudLiveSurveyRankingItemDto
                    {
                        ParticipantKey = participant.ParticipantKey,
                        FullName = participant.FullName,
                        Company = participant.Company,
                        Score = participant.Score,
                        MaxScore = participant.MaxScore,
                        AnsweredCount = participant.Answers.Count,
                        CorrectAnswers = participant.Answers.Values.Count(answer => answer.IsCorrect)
                    };
                }
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.CorrectAnswers)
            .ThenByDescending(item => item.AnsweredCount)
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SoporteCloudLiveWheelRankingItemDto> BuildLiveWheelRanking(LiveSurveySessionState liveState)
    {
        return liveState.Participants.Values
            .Select(participant =>
            {
                lock (participant.SyncRoot)
                {
                    return participant.WheelNumber is null
                        ? null
                        : new SoporteCloudLiveWheelRankingItemDto
                        {
                            ParticipantKey = participant.ParticipantKey,
                            FullName = participant.FullName,
                            Company = participant.Company,
                            Number = participant.WheelNumber.Value,
                            SpunAtUtc = participant.WheelSpunAt
                        };
                }
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.Number)
            .ThenBy(item => item.SpunAtUtc)
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SoporteCloudLiveSurveyParticipantProgressDto? BuildLiveParticipantProgress(
        LiveSurveySessionState liveState,
        string participantKey)
    {
        var normalizedKey = NormalizeLiveKey(participantKey);
        if (string.IsNullOrWhiteSpace(normalizedKey)
            || !liveState.Participants.TryGetValue(normalizedKey, out var participant))
            return null;

        lock (participant.SyncRoot)
        {
            return new SoporteCloudLiveSurveyParticipantProgressDto
            {
                ParticipantKey = participant.ParticipantKey,
                FullName = participant.FullName,
                Identification = participant.Identification,
                Company = participant.Company,
                Role = participant.Role,
                Email = participant.Email,
                Score = participant.Score,
                MaxScore = participant.MaxScore,
                CorrectAnswers = participant.Answers.Values.Count(answer => answer.IsCorrect),
                Completed = participant.Completed,
                WheelNumber = participant.WheelNumber,
                Answers = participant.Answers.Values
                    .OrderBy(answer => answer.AnsweredAt)
                    .Select(answer => new SoporteCloudLiveSurveyAnswerRestoreDto
                    {
                        QuestionId = answer.QuestionId,
                        OptionId = answer.OptionId,
                        NumericValue = answer.NumericValue,
                        TextValue = answer.TextValue,
                        Points = answer.Points,
                        MaxPoints = answer.MaxPoints,
                        IsCorrect = answer.IsCorrect,
                        ResponseSeconds = answer.ResponseSeconds
                    })
                    .ToList()
            };
        }
    }

    private static LiveSurveyAnswerState BuildLiveAnswerState(
        SoporteCloudSurveyQuestionDto question,
        SoporteCloudLiveSurveyAnswerRequest request,
        decimal responseSeconds)
    {
        var maxPoints = question.ComponentValue == SurveyComponentKnowledge ? LiveQuestionMaxPoints : 0m;
        var answer = new LiveSurveyAnswerState
        {
            QuestionId = question.QuestionId,
            MaxPoints = maxPoints,
            ResponseSeconds = Math.Round(responseSeconds, 2, MidpointRounding.AwayFromZero),
            AnsweredAt = DateTimeOffset.UtcNow
        };

        if (question.InputTypeValue == SurveyInputSingleChoice)
        {
            var optionId = NormalizeOptionalGuidLocal(request.OptionId);
            var option = question.Options.FirstOrDefault(item =>
                string.Equals(item.OptionId, optionId, StringComparison.OrdinalIgnoreCase) && item.IsActive)
                ?? throw new InvalidOperationException($"Selecciona una opcion valida para: {question.Text}");
            answer.OptionId = option.OptionId;
            answer.IsCorrect = option.IsCorrect;
            answer.Points = answer.IsCorrect ? CalculateLiveTimedPoints(responseSeconds) : 0m;
            return answer;
        }

        if (question.InputTypeValue == SurveyInputMultipleChoice)
        {
            var selectedIds = ParseLiveAnswerIds(request.TextValue);
            if (selectedIds.Count == 0)
                throw new InvalidOperationException($"Selecciona al menos una opcion para: {question.Text}");

            var activeOptions = question.Options
                .Where(item => item.IsActive)
                .ToDictionary(item => item.OptionId, StringComparer.OrdinalIgnoreCase);
            var selectedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selectedId in selectedIds)
            {
                if (!activeOptions.ContainsKey(selectedId))
                    throw new InvalidOperationException($"Selecciona opciones validas para: {question.Text}");

                selectedSet.Add(selectedId);
            }

            var correctSet = activeOptions.Values
                .Where(item => item.IsCorrect)
                .Select(item => item.OptionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            answer.IsCorrect = correctSet.Count > 0 && selectedSet.SetEquals(correctSet);
            answer.Points = answer.IsCorrect ? CalculateLiveTimedPoints(responseSeconds) : 0m;
            answer.TextValue = JsonSerializer.Serialize(selectedSet);
            return answer;
        }

        if (question.InputTypeValue == SurveyInputMatching)
        {
            var submittedPairs = ParseLiveMatchingAnswer(request.TextValue);
            var activeOptions = question.Options
                .Where(item => item.IsActive)
                .ToList();
            if (activeOptions.Count == 0)
                throw new InvalidOperationException($"No encontramos pares configurados para: {question.Text}");

            var allCorrect = true;
            foreach (var option in activeOptions)
            {
                var expected = ParseLiveMatchingOptionText(option.Text);
                if (string.IsNullOrWhiteSpace(option.OptionId)
                    || string.IsNullOrWhiteSpace(expected.Text)
                    || string.IsNullOrWhiteSpace(expected.Target)
                    || !submittedPairs.TryGetValue(option.OptionId, out var submittedTarget)
                    || !string.Equals(NormalizeLiveMatchKey(submittedTarget), NormalizeLiveMatchKey(expected.Target), StringComparison.Ordinal))
                {
                    allCorrect = false;
                }
            }

            answer.IsCorrect = allCorrect;
            answer.Points = allCorrect ? CalculateLiveTimedPoints(responseSeconds) : 0m;
            answer.TextValue = request.TextValue;
            return answer;
        }

        if (question.InputTypeValue == SurveyInputRating)
        {
            var value = request.NumericValue ?? 0m;
            if (value < 1m || value > 5m)
                throw new InvalidOperationException($"La calificacion debe estar entre 1 y 5 para: {question.Text}");

            answer.NumericValue = value;
            return answer;
        }

        var text = (request.TextValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Debes responder: {question.Text}");

        answer.TextValue = text;
        return answer;
    }

    private static decimal CalculateLiveTimedPoints(decimal responseSeconds)
    {
        if (responseSeconds <= 6m)
            return 10m;
        if (responseSeconds <= 10m)
            return 7m;
        if (responseSeconds <= 15m)
            return 4m;

        return 0m;
    }

    private static IReadOnlyList<string> ParseLiveAnswerIds(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return document.RootElement
                        .EnumerateArray()
                        .Select(item =>
                        {
                            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("optionId", out var optionId))
                                return optionId.GetString();

                            return item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                        })
                        .Select(NormalizeOptionalGuidLocal)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch (JsonException)
            {
            }
        }

        return value
            .Split(new[] { ",", ";", "|", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOptionalGuidLocal)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseLiveMatchingAnswer(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var value = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        if (value.StartsWith("[", StringComparison.Ordinal) || value.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        var optionId = item.TryGetProperty("optionId", out var optionIdElement)
                            ? NormalizeOptionalGuidLocal(optionIdElement.GetString())
                            : "";
                        var target = item.TryGetProperty("target", out var targetElement)
                            ? (targetElement.GetString() ?? "").Trim()
                            : "";
                        if (!string.IsNullOrWhiteSpace(optionId))
                            result[optionId] = target;
                    }

                    return result;
                }

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var optionId = NormalizeOptionalGuidLocal(property.Name);
                        if (string.IsNullOrWhiteSpace(optionId))
                            continue;

                        result[optionId] = property.Value.ValueKind == JsonValueKind.String
                            ? (property.Value.GetString() ?? "").Trim()
                            : property.Value.ToString().Trim();
                    }

                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        foreach (var segment in value.Split(new[] { "\r\n", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(new[] { "=>", "->", "=" }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                result[NormalizeOptionalGuidLocal(parts[0])] = parts[1].Trim();
        }

        return result;
    }

    private static (string Text, string Target) ParseLiveMatchingOptionText(string? value)
    {
        var text = (value ?? "").Trim();
        var separatorIndex = text.IndexOf(SurveyMatchingSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
            return (text, "");

        return (text[..separatorIndex].Trim(), text[(separatorIndex + SurveyMatchingSeparator.Length)..].Trim());
    }

    private static string NormalizeLiveMatchKey(string? value)
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

    private static string BuildParticipantKey(SoporteCloudLiveSurveyRegisterRequest request)
    {
        return FirstNonEmptyLocal(
            NormalizeLiveKey(request.ParticipantKey),
            NormalizeLiveKey(request.Email),
            NormalizeLiveKey(request.Identification),
            NormalizeLiveKey($"{request.FullName}|{request.Company}"),
            Guid.NewGuid().ToString("N"));
    }

    private static string BuildParticipantKey(SoporteCloudLiveSurveyCompleteRequest? request)
    {
        if (request is null)
            return "";

        return FirstNonEmptyLocal(
            NormalizeLiveKey(request.ParticipantKey),
            NormalizeLiveKey(request.Email),
            NormalizeLiveKey(request.FullName));
    }

    private static string BuildParticipantKey(SoporteCloudLiveSurveyAnswerRequest? request)
    {
        if (request is null)
            return "";

        return NormalizeLiveKey(request.ParticipantKey);
    }

    private async Task<int> PersistLiveKnowledgeResultsAsync(LiveSurveySessionState liveState, CancellationToken ct)
    {
        var submissions = new List<SoporteCloudSurveySubmitRequest>();
        lock (liveState.SyncRoot)
        {
            foreach (var participant in liveState.Participants.Values)
            {
                lock (participant.SyncRoot)
                {
                    var fullName = (participant.FullName ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    submissions.Add(new SoporteCloudSurveySubmitRequest
                    {
                        Code = liveState.Code,
                        ParticipantKey = participant.ParticipantKey,
                        FullName = fullName,
                        Email = participant.Email,
                        Company = participant.Company,
                        Answers = participant.Answers.Values
                            .Select(answer => new SoporteCloudSurveyAnswerSubmitDto
                            {
                                QuestionId = answer.QuestionId,
                                OptionId = answer.OptionId,
                                NumericValue = answer.NumericValue,
                                TextValue = answer.TextValue,
                                PointsOverride = answer.Points,
                                MaxPointsOverride = answer.MaxPoints,
                                IsCorrectOverride = answer.IsCorrect
                            })
                            .ToList()
                    });
                }
            }
        }

        if (submissions.Count == 0)
            return 0;

        return await _dataverse.SaveSoporteCloudLiveKnowledgeResultsAsync(liveState.Code, submissions, ct);
    }

    private static void ApplyLiveScoreOverrides(string code, SoporteCloudSurveySubmitRequest request)
    {
        var participantKey = FirstNonEmptyLocal(
            NormalizeLiveKey(request.ParticipantKey),
            NormalizeLiveKey(request.Email),
            NormalizeLiveKey($"{request.FullName}|{request.Company}"));
        if (string.IsNullOrWhiteSpace(participantKey)
            || !LiveSurveySessions.TryGetValue(NormalizeLiveCode(code), out var liveState)
            || !liveState.Participants.TryGetValue(participantKey, out var participant)
            || request.Answers.Count == 0)
            return;

        lock (participant.SyncRoot)
        {
            foreach (var submittedAnswer in request.Answers)
            {
                var questionId = NormalizeOptionalGuidLocal(submittedAnswer.QuestionId);
                if (string.IsNullOrWhiteSpace(questionId)
                    || !participant.Answers.TryGetValue(questionId, out var liveAnswer))
                    continue;

                submittedAnswer.PointsOverride = liveAnswer.Points;
                submittedAnswer.MaxPointsOverride = liveAnswer.MaxPoints;
                submittedAnswer.IsCorrectOverride = liveAnswer.IsCorrect;
            }
        }
    }

    private static string NormalizeLiveCode(string? value) =>
        (value ?? "").Trim().ToUpperInvariant();

    private static string NormalizeOptionalGuidLocal(string? value) =>
        (value ?? "").Trim();

    private static bool IsCorporateEmail(string? value)
    {
        var email = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var address = new MailAddress(email);
            if (!string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
                return false;

            var domain = address.Host.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(domain) || !domain.Contains('.', StringComparison.Ordinal))
                return false;

            var personalDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "gmail.com",
                "googlemail.com",
                "hotmail.com",
                "hotmail.es",
                "outlook.com",
                "outlook.es",
                "live.com",
                "live.com.co",
                "msn.com",
                "yahoo.com",
                "yahoo.es",
                "icloud.com",
                "me.com",
                "aol.com",
                "proton.me",
                "protonmail.com"
            };

            return !personalDomains.Contains(domain);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeLiveKey(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "" : normalized;
    }

    private static string FirstNonEmptyLocal(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private static string BuildExceptionDetail(Exception? ex)
    {
        if (ex is null)
            return "";

        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmedMessage = current.Message.Trim();
            if (!messages.Contains(trimmedMessage, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmedMessage);
        }

        return string.Join(" | ", messages);
    }

    private static DateOnly ResolveBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }

    private sealed class LiveSurveySessionState
    {
        public object SyncRoot { get; } = new();
        public string SessionId { get; set; } = "";
        public string Code { get; init; } = "";
        public string SessionName { get; set; } = "";
        public string TopicName { get; set; } = "";
        public string PublicUrl { get; set; } = "";
        public string Phase { get; set; } = LivePhaseRegistration;
        public int CurrentQuestionIndex { get; set; } = -1;
        public string CurrentQuestionId { get; set; } = "";
        public string PendingPhase { get; set; } = "";
        public int PendingQuestionIndex { get; set; } = -1;
        public DateTimeOffset? QuestionStartedOnUtc { get; set; }
        public DateTimeOffset? RankingEndsOnUtc { get; set; }
        public int Sequence { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public IReadOnlyList<SoporteCloudSurveyQuestionDto> KnowledgeQuestions { get; set; } = Array.Empty<SoporteCloudSurveyQuestionDto>();
        public ConcurrentDictionary<string, LiveSurveyParticipantState> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, DateTimeOffset> RemovedParticipants { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LiveSurveyParticipantState
    {
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
        public ConcurrentDictionary<string, LiveSurveyAnswerState> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LiveSurveyAnswerState
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
}
