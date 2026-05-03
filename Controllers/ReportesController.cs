using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.Reportes;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ApiController]
[Route("api/reportes")]
[ModuleAuthorize(AppModule.SoporteCloud)]
public sealed class ReportesController : ControllerBase
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private readonly IAzureOpenAIReportService _reportes;
    private readonly IReportesDataverseRepository _repository;
    private readonly ILogger<ReportesController> _logger;

    public ReportesController(
        IAzureOpenAIReportService reportes,
        IReportesDataverseRepository repository,
        ILogger<ReportesController> logger)
    {
        _reportes = reportes;
        _repository = repository;
        _logger = logger;
    }

    [HttpPost("generar")]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Generar([FromBody] ReporteGenerarRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(CreateErrorPayload("Debes indicar clienteId y periodo."));

        try
        {
            return Ok(await _reportes.GenerateReportAsync(request, ct));
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
                error = report.Errores
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
