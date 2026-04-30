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
using System.Text;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.SoporteCloud)]
public sealed class SoporteCloudController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
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
    public async Task<IActionResult> CloseSurveySession([FromQuery] string sessionId, CancellationToken ct)
    {
        try
        {
            return Ok(await _dataverse.CloseSoporteCloudSurveySessionAsync(sessionId, ct));
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
}
