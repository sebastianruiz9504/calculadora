using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Reportes;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ApiController]
[Route("api/reportes")]
[ModuleAuthorize(AppModule.SoporteCloud)]
public sealed class ReportesController : ControllerBase
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const string GraphMailSendScope = ReportesEmailSender.MailSendScope;
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);
    private const int MaxRecommendationLength = 2000;
    private const int MaxAttachmentCount = 8;
    private const long MaxAttachmentBytes = 8 * 1024 * 1024;
    private const long MaxTotalAttachmentBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly char[] EmailRecipientSeparators = { ';', ',', '\n', '\r', '\t' };
    private readonly IReportesDataverseRepository _repository;
    private readonly IReportesGenerationQueue _generationQueue;
    private readonly IReportesEmailSender _emailSender;
    private readonly ReportesOptions _reportesOptions;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly ILogger<ReportesController> _logger;

    public ReportesController(
        IReportesDataverseRepository repository,
        IReportesGenerationQueue generationQueue,
        IReportesEmailSender emailSender,
        IOptions<ReportesOptions> reportesOptions,
        ITokenAcquisition tokenAcquisition,
        ILogger<ReportesController> logger)
    {
        _repository = repository;
        _generationQueue = generationQueue;
        _emailSender = emailSender;
        _reportesOptions = reportesOptions.Value;
        _tokenAcquisition = tokenAcquisition;
        _logger = logger;
    }

    private sealed record ReporteEmailRecipients(IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Source);

    [HttpPost("generar")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Generar(CancellationToken ct)
    {
        try
        {
            var (request, attachments) = await ReadGenerateRequestAsync(ct);
            if (request is null)
                return BadRequest(CreateErrorPayload("Debes indicar clienteId y periodo."));

            var periodo = ResolveReportPeriod(request.Periodo);
            var recomendacionMensual = NormalizeRecommendation(request.RecomendacionMensual);
            if (string.IsNullOrWhiteSpace(recomendacionMensual))
                return BadRequest(CreateErrorPayload("Debes escribir la recomendacion mensual antes de generar el informe."));

            var queued = await _repository.UpsertGeneratedReportAsync(new ReporteHtmlGeneradoRecord
            {
                ClienteId = request.ClienteId,
                Periodo = periodo,
                HtmlGenerado = "",
                Estado = "Generando",
                FechaGeneracion = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                PromptVersion = _reportesOptions.PromptVersion,
                Errores = ""
            }, ct);

            if (!string.IsNullOrWhiteSpace(queued.RecordId))
            {
                await _repository.ReplaceGeneratedReportAttachmentsAsync(queued.RecordId, attachments, ct);
            }
            else if (attachments.Count > 0)
            {
                throw new InvalidOperationException("Dataverse no devolvio el id del informe para guardar los anexos.");
            }

            await _generationQueue.QueueAsync(new ReporteGenerarRequest
            {
                ClienteId = request.ClienteId,
                Periodo = periodo,
                RecomendacionMensual = recomendacionMensual
            }, ct);

            return Accepted(new ReporteGenerarResult
            {
                IdReporte = queued.RecordId,
                Html = "",
                Estado = "Generando"
            });
        }
        catch (ReportesConfigurationException ex)
        {
            _logger.LogWarning(ex, "Configuracion incompleta para generar informe mensual.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible generar informe mensual.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado generando informe mensual.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible generar el informe mensual.", ex));
        }
    }

    private static string ResolveReportPeriod(string? rawPeriod)
    {
        var value = rawPeriod?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            var nowBogota = DateTimeOffset.UtcNow.ToOffset(BogotaOffset);
            return new DateOnly(nowBogota.Year, nowBogota.Month, 1)
                .AddMonths(-1)
                .ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidOperationException("El periodo debe tener formato yyyy-MM.");
        }

        return new DateOnly(parsed.Year, parsed.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private static string NormalizeRecommendation(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > MaxRecommendationLength)
            throw new InvalidOperationException($"La recomendacion mensual no puede superar {MaxRecommendationLength:N0} caracteres.");

        return normalized;
    }

    private async Task<(ReporteGenerarRequest? Request, IReadOnlyList<ReporteEmailAttachment> Attachments)> ReadGenerateRequestAsync(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
        {
            var jsonRequest = await JsonSerializer.DeserializeAsync<ReporteGenerarRequest>(Request.Body, JsonOptions, ct);
            return (jsonRequest, Array.Empty<ReporteEmailAttachment>());
        }

        var form = await Request.ReadFormAsync(ct);
        var request = new ReporteGenerarRequest
        {
            ClienteId = form["clienteId"].FirstOrDefault() ?? "",
            Periodo = form["periodo"].FirstOrDefault() ?? "",
            RecomendacionMensual = form["recomendacionMensual"].FirstOrDefault() ?? ""
        };

        return (request, await ReadUploadedAttachmentsAsync(form.Files, ct));
    }

    private static async Task<IReadOnlyList<ReporteEmailAttachment>> ReadUploadedAttachmentsAsync(IFormFileCollection files, CancellationToken ct)
    {
        var attachments = new List<ReporteEmailAttachment>();
        long totalBytes = 0;
        foreach (var file in files.Where(file => file.Length > 0))
        {
            if (attachments.Count >= MaxAttachmentCount)
                throw new InvalidOperationException($"Puedes adjuntar maximo {MaxAttachmentCount} documentos por reporte.");

            if (file.Length > MaxAttachmentBytes)
                throw new InvalidOperationException($"El archivo {file.FileName} supera el limite de {MaxAttachmentBytes / 1024 / 1024} MB.");

            totalBytes += file.Length;
            if (totalBytes > MaxTotalAttachmentBytes)
                throw new InvalidOperationException($"Los anexos superan el limite total de {MaxTotalAttachmentBytes / 1024 / 1024} MB.");

            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            attachments.Add(new ReporteEmailAttachment
            {
                FileName = NormalizeFileName(file.FileName),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                Size = memory.Length,
                Content = memory.ToArray()
            });
        }

        return attachments;
    }

    [HttpGet("generados")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Generados([FromQuery] string periodo, CancellationToken ct)
    {
        try
        {
            var reports = await _repository.ListGeneratedReportsAsync(periodo, ct);
            return Ok(reports.Select(report => new
            {
                idReporte = report.RecordId,
                clienteId = report.ClienteId,
                clienteNombre = report.ClienteNombre,
                periodo = report.Periodo,
                estado = report.Estado,
                fechaGeneracion = report.FechaGeneracion,
                promptVersion = report.PromptVersion,
                errores = report.Errores
            }));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible consultar informes generados.");
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado consultando informes generados.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible consultar los informes generados.", ex));
        }
    }

    [HttpGet("generados/{idReporte}")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> GeneradoDetalle([FromRoute] string idReporte, CancellationToken ct)
    {
        try
        {
            var report = await _repository.GetGeneratedReportAsync(idReporte, ct);
            if (report is null)
                return NotFound(CreateErrorPayload("No se encontro el informe solicitado."));

            var attachments = await _repository.ListGeneratedReportAttachmentsAsync(report.RecordId, includeContent: false, ct);
            return Ok(new
            {
                idReporte = report.RecordId,
                clienteId = report.ClienteId,
                clienteNombre = report.ClienteNombre,
                periodo = report.Periodo,
                html = report.HtmlGenerado,
                estado = report.Estado,
                fechaGeneracion = report.FechaGeneracion,
                promptVersion = report.PromptVersion,
                error = report.Errores,
                anexos = attachments.Select(attachment => new
                {
                    id = attachment.Id,
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    size = attachment.Size
                })
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible consultar informe generado {IdReporte}.", idReporte);
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado consultando informe generado {IdReporte}.", idReporte);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible consultar el informe generado.", ex));
        }
    }

    [HttpGet("correo/consentimiento")]
    [AuthorizeForScopes(Scopes = new[] { GraphMailSendScope })]
    public async Task<IActionResult> EmailConsent()
    {
        _ = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { GraphMailSendScope }, user: User);
        return Redirect("~/SoporteCloud#reportes");
    }

    [HttpPost("generados/{idReporte}/enviar")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope, GraphMailSendScope })]
    public async Task<IActionResult> EnviarGenerado(
        [FromRoute] string idReporte,
        [FromBody] ReporteSendEmailRequest? request,
        CancellationToken ct)
    {
        try
        {
            var result = await SendGeneratedReportAsync(idReporte, request ?? new ReporteSendEmailRequest(), testEmail: "", ct);
            return Ok(result);
        }
        catch (ReportesConfigurationException ex)
        {
            _logger.LogWarning(ex, "Configuracion incompleta enviando informe generado {IdReporte}.", idReporte);
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (MicrosoftIdentityWebChallengeUserException ex)
        {
            _logger.LogWarning(ex, "Se requiere consentimiento Mail.Send para enviar informe generado {IdReporte}.", idReporte);
            return StatusCode(StatusCodes.Status403Forbidden, CreateEmailConsentPayload(ex.Message));
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogWarning(ex, "Se requiere consentimiento Mail.Send para enviar informe generado {IdReporte}.", idReporte);
            return StatusCode(StatusCodes.Status403Forbidden, CreateEmailConsentPayload(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible enviar informe generado {IdReporte}.", idReporte);
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado enviando informe generado {IdReporte}.", idReporte);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible enviar el informe generado.", ex));
        }
    }

    [HttpPost("generados/{idReporte}/enviar-prueba")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope, GraphMailSendScope })]
    public async Task<IActionResult> EnviarPruebaGenerado(
        [FromRoute] string idReporte,
        [FromBody] ReporteTestEmailRequest? request,
        CancellationToken ct)
    {
        try
        {
            if (request is null)
                return BadRequest(CreateErrorPayload("Debes indicar el correo de prueba."));

            var result = await SendGeneratedReportAsync(idReporte, request, request.TestEmail, ct);
            return Ok(result);
        }
        catch (ReportesConfigurationException ex)
        {
            _logger.LogWarning(ex, "Configuracion incompleta enviando prueba de informe {IdReporte}.", idReporte);
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (MicrosoftIdentityWebChallengeUserException ex)
        {
            _logger.LogWarning(ex, "Se requiere consentimiento Mail.Send para enviar prueba de informe {IdReporte}.", idReporte);
            return StatusCode(StatusCodes.Status403Forbidden, CreateEmailConsentPayload(ex.Message));
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogWarning(ex, "Se requiere consentimiento Mail.Send para enviar prueba de informe {IdReporte}.", idReporte);
            return StatusCode(StatusCodes.Status403Forbidden, CreateEmailConsentPayload(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No fue posible enviar prueba de informe generado {IdReporte}.", idReporte);
            return BadRequest(CreateErrorPayload(ex.Message, ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado enviando prueba de informe generado {IdReporte}.", idReporte);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateErrorPayload("No fue posible enviar la prueba del informe.", ex));
        }
    }

    private async Task<object> SendGeneratedReportAsync(
        string idReporte,
        ReporteSendEmailRequest request,
        string testEmail,
        CancellationToken ct)
    {
        var report = await _repository.GetGeneratedReportAsync(idReporte, ct)
            ?? throw new InvalidOperationException("No se encontro el informe solicitado.");
        if (string.IsNullOrWhiteSpace(report.HtmlGenerado)
            || !string.Equals(report.Estado, "Generado", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solo se pueden enviar informes con estado Generado y HTML disponible.");
        }

        var client = await _repository.GetClientAsync(report.ClienteId, ct)
            ?? new ReporteClienteData
            {
                ClienteId = report.ClienteId,
                Nombre = report.ClienteNombre
            };

        var recipients = string.IsNullOrWhiteSpace(testEmail)
            ? ResolveClientEmailRecipients(client)
            : new ReporteEmailRecipients(
                ResolveEmailRecipients(testEmail, "correo de prueba"),
                Array.Empty<string>(),
                "prueba");

        var reportFileName = BuildReportFileName(client.Nombre, report.Periodo);
        var placeholders = BuildTemplatePlaceholders(report, client, reportFileName);
        var subjectTemplate = FirstNonEmpty(request.SubjectTemplate, _reportesOptions.Email.DefaultSubjectTemplate);
        var bodyTemplate = FirstNonEmpty(request.BodyTemplate, _reportesOptions.Email.DefaultBodyTemplate);
        var subject = ApplyTemplate(subjectTemplate, placeholders);
        var htmlBody = BuildEmailHtml(ApplyTemplate(bodyTemplate, placeholders));
        var attachments = await BuildEmailAttachmentsAsync(report, reportFileName, ct);

        var emailResult = await _emailSender.SendAsync(new ReportesEmailMessage
        {
            To = recipients.To,
            Cc = recipients.Cc,
            Subject = subject,
            HtmlBody = htmlBody,
            Attachments = attachments
        }, User, ct);

        return new
        {
            message = string.IsNullOrWhiteSpace(testEmail) ? "Informe enviado al cliente." : "Prueba enviada.",
            destinatarios = recipients.To,
            copiados = recipients.Cc,
            destinatariosFuente = recipients.Source,
            remitente = emailResult.Sender,
            modoEnvio = emailResult.Mode,
            contacto = new
            {
                nombre = client.PersonaACargo,
                correo = client.Correo
            },
            cliente = new
            {
                id = client.ClienteId,
                nombre = client.Nombre
            },
            adjuntos = attachments.Select(attachment => new
            {
                fileName = attachment.FileName,
                contentType = attachment.ContentType,
                size = attachment.Size
            })
        };
    }

    private async Task<IReadOnlyList<ReporteEmailAttachment>> BuildEmailAttachmentsAsync(
        ReporteHtmlGeneradoRecord report,
        string reportFileName,
        CancellationToken ct)
    {
        var htmlBytes = Encoding.UTF8.GetBytes(report.HtmlGenerado);
        var attachments = new List<ReporteEmailAttachment>
        {
            new()
            {
                FileName = reportFileName,
                ContentType = "text/html",
                Size = htmlBytes.LongLength,
                Content = htmlBytes
            }
        };

        attachments.AddRange(await _repository.ListGeneratedReportAttachmentsAsync(report.RecordId, includeContent: true, ct));
        return attachments;
    }

    private static Dictionary<string, string> BuildTemplatePlaceholders(
        ReporteHtmlGeneradoRecord report,
        ReporteClienteData client,
        string reportFileName)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNombre"] = FirstNonEmpty(client.Nombre, report.ClienteNombre, "Cliente"),
            ["ClienteId"] = FirstNonEmpty(client.ClienteId, report.ClienteId),
            ["ContactoNombre"] = FirstNonEmpty(client.PersonaACargo, client.Nombre, report.ClienteNombre, "equipo"),
            ["ContactoCorreo"] = client.Correo,
            ["Periodo"] = report.Periodo,
            ["FechaGeneracion"] = FormatDateTimeForEmail(report.FechaGeneracion),
            ["ReporteNombre"] = reportFileName
        };
    }

    private ReporteEmailRecipients ResolveClientEmailRecipients(ReporteClienteData client)
    {
        var recipientOverride = FindClientRecipientOverride(client);
        if (recipientOverride is not null)
        {
            var clientLabel = FirstNonEmpty(client.Nombre, client.ClienteId, "cliente");
            var to = ResolveEmailRecipients(recipientOverride.To, $"destinatarios configurados para {clientLabel}");
            var cc = ExcludeRecipients(
                ResolveOptionalEmailRecipients(recipientOverride.Cc, $"copias configuradas para {clientLabel}"),
                to);

            return new ReporteEmailRecipients(to, cc, "configuracion");
        }

        return new ReporteEmailRecipients(
            ResolveEmailRecipients(client.Correo, "correo del cliente"),
            Array.Empty<string>(),
            "cliente");
    }

    private ReportesEmailRecipientOverrideOptions? FindClientRecipientOverride(ReporteClienteData client)
    {
        var overrides = _reportesOptions.Email.RecipientOverrides ?? Array.Empty<ReportesEmailRecipientOverrideOptions>();
        if (overrides.Length == 0)
            return null;

        var clientId = client.ClienteId.Trim();
        var normalizedClientName = NormalizeClientMatchToken(client.Nombre);
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var byId = overrides.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.ClientId)
                && string.Equals(item.ClientId.Trim(), clientId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(normalizedClientName))
            return null;

        var exactName = overrides.FirstOrDefault(item =>
            string.Equals(NormalizeClientMatchToken(item.ClientName), normalizedClientName, StringComparison.Ordinal));
        if (exactName is not null)
            return exactName;

        return overrides.FirstOrDefault(item =>
            EnumerateClientNameMatchTerms(item)
                .Select(NormalizeClientMatchToken)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Any(term => normalizedClientName.Contains(term, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> EnumerateClientNameMatchTerms(ReportesEmailRecipientOverrideOptions item)
    {
        if (!string.IsNullOrWhiteSpace(item.ClientName))
            yield return item.ClientName;

        foreach (var term in item.ClientNameContains ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(term))
                yield return term;
        }
    }

    private static IReadOnlyList<string> ResolveEmailRecipients(string rawValue, string fieldName)
    {
        return ResolveEmailRecipients(new[] { rawValue }, fieldName);
    }

    private static IReadOnlyList<string> ResolveEmailRecipients(IEnumerable<string>? rawValues, string fieldName)
    {
        return ResolveEmailRecipients(rawValues, fieldName, required: true);
    }

    private static IReadOnlyList<string> ResolveOptionalEmailRecipients(IEnumerable<string>? rawValues, string fieldName)
    {
        return ResolveEmailRecipients(rawValues, fieldName, required: false);
    }

    private static IReadOnlyList<string> ResolveEmailRecipients(IEnumerable<string>? rawValues, string fieldName, bool required)
    {
        var parts = (rawValues ?? Array.Empty<string>())
            .SelectMany(static value => (value ?? "").Split(EmailRecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count == 0 && required)
            throw new InvalidOperationException($"No se encontro {fieldName}.");

        var emails = new List<string>();
        foreach (var part in parts)
        {
            try
            {
                emails.Add(new MailAddress(part).Address);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"El {fieldName} no es valido: {part}.", ex);
            }
        }

        return emails
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ExcludeRecipients(
        IReadOnlyList<string> recipients,
        IReadOnlyList<string> excludedRecipients)
    {
        if (recipients.Count == 0 || excludedRecipients.Count == 0)
            return recipients;

        var excluded = new HashSet<string>(excludedRecipients, StringComparer.OrdinalIgnoreCase);
        return recipients
            .Where(recipient => !excluded.Contains(recipient))
            .ToList();
    }

    private static string NormalizeClientMatchToken(string? value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static string BuildReportFileName(string clientName, string period)
    {
        var client = NormalizeFileName(FirstNonEmpty(clientName, "Cliente"));
        var normalizedPeriod = NormalizeFileName(FirstNonEmpty(period, DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture)));
        return $"Informe-M365-{client}-{normalizedPeriod}.html";
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var result = template ?? "";
        foreach (var item in placeholders)
        {
            result = result.Replace($"{{{item.Key}}}", item.Value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        return result.Trim();
    }

    private static string BuildEmailHtml(string bodyText)
    {
        var encoded = HtmlEncoder.Default.Encode(bodyText ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $"""
<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="utf-8">
</head>
<body style="margin:0;background:#f6f8fb;color:#17263c;font-family:Arial,sans-serif;">
    <div style="max-width:720px;margin:0 auto;padding:28px;">
        <div style="background:#ffffff;border:1px solid #dce6f2;border-radius:8px;padding:24px;line-height:1.55;font-size:15px;">
            {encoded}
        </div>
    </div>
</body>
</html>
""";
    }

    private static string FormatDateTimeForEmail(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed
                .ToOffset(BogotaOffset)
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
        }

        return value ?? "";
    }

    private static string NormalizeFileName(string? raw)
    {
        var value = Path.GetFileName((raw ?? "").Trim());
        if (string.IsNullOrWhiteSpace(value))
            return "archivo";

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length <= 120 ? value : value[..120];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private object CreateErrorPayload(string message, Exception? ex = null)
    {
        var detail = ex is null ? "" : BuildExceptionDetail(ex);
        return new
        {
            message,
            detail = string.Equals(detail, message, StringComparison.Ordinal) ? "" : detail,
            traceId = HttpContext.TraceIdentifier
        };
    }

    private object CreateEmailConsentPayload(string detail = "") => new
    {
        message = "Debes autorizar el permiso de correo para enviar reportes desde tu buzon.",
        detail,
        action = "mailConsentRequired",
        consentUrl = Url.Action(nameof(EmailConsent), "Reportes") ?? "/api/reportes/correo/consentimiento",
        traceId = HttpContext.TraceIdentifier
    };

    private static string BuildExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }
}
