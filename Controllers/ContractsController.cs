using System.Text.Json;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Contracts;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Contracts)]
public sealed class ContractsController : Controller
{
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const long AnalysisLimit = 25L * 1024 * 1024;
    private const long StorageLimit = 128L * 1024 * 1024;
    private readonly IDataverseService _dataverse;
    private readonly IContractsAiService _contractsAi;
    private readonly ILogger<ContractsController> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ContractsController(IDataverseService dataverse, IContractsAiService contractsAi, ILogger<ContractsController> logger)
    {
        _dataverse = dataverse;
        _contractsAi = contractsAi;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(await _dataverse.GetContractsPageAsync(ct));
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery] string q, CancellationToken ct)
    {
        return Ok(await _dataverse.SearchClientsAsync(q, top: 12, ct: ct));
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(AnalysisLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = AnalysisLimit)]
    public async Task<IActionResult> AnalyzeRut(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            return BadRequest(Error("Adjunta el RUT del cliente."));
        try
        {
            var content = await ReadFileAsync(file, AnalysisLimit, ct);
            return Ok(await _contractsAi.AnalyzeRutAsync(file.FileName, file.ContentType, content, ct));
        }
        catch (TimeoutException ex) { return AnalysisTimeout("del RUT", ex); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible analizar el RUT con Azure OpenAI.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(AnalysisLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = AnalysisLimit)]
    public async Task<IActionResult> AnalyzeOffer(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            return BadRequest(Error("Adjunta la oferta aprobada."));
        try
        {
            var content = await ReadFileAsync(file, AnalysisLimit, ct);
            return Ok(await _contractsAi.AnalyzeOfferAsync(file.FileName, file.ContentType, content, ct));
        }
        catch (TimeoutException ex) { return AnalysisTimeout("de la oferta", ex); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible analizar la oferta con Azure OpenAI.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(270L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 270L * 1024 * 1024)]
    public async Task<IActionResult> Create(string payloadJson, IFormFile? rutFile, IFormFile? offerFile, CancellationToken ct)
    {
        if (rutFile is null || offerFile is null)
            return BadRequest(Error("Adjunta el RUT y la oferta aprobada."));
        try
        {
            var request = JsonSerializer.Deserialize<ContractCreateRequest>(payloadJson ?? "", JsonOptions)
                ?? throw new InvalidOperationException("La información del contrato no es válida.");
            var rutContent = await ReadFileAsync(rutFile, StorageLimit, ct);
            var offerContent = await ReadFileAsync(offerFile, StorageLimit, ct);
            return Ok(await _dataverse.CreateContractAsync(
                request,
                rutFile.FileName, rutFile.ContentType, rutContent,
                offerFile.FileName, offerFile.ContentType, offerContent,
                ct));
        }
        catch (JsonException ex) { return BadRequest(Error("La información del formulario no se pudo interpretar.", ex)); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible crear el contrato.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> CreateServiceOrder([FromBody] ContractServiceOrderCreateRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(Error("Envía los datos de la orden de servicio."));
        try { return Ok(await _dataverse.CreateContractServiceOrderAsync(request, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible crear la orden de servicio.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(StorageLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = StorageLimit)]
    public async Task<IActionResult> UploadSignedContract(string contractId, IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            return BadRequest(Error("Selecciona el contrato firmado."));
        try
        {
            var content = await ReadFileAsync(file, StorageLimit, ct);
            return Ok(await _dataverse.UploadContractSignedFileAsync(contractId, file.FileName, file.ContentType, content, ct));
        }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible cargar el contrato firmado.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    [RequestSizeLimit(StorageLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = StorageLimit)]
    public async Task<IActionResult> UploadSignedOrder(string orderId, IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            return BadRequest(Error("Selecciona la orden firmada."));
        try
        {
            var content = await ReadFileAsync(file, StorageLimit, ct);
            return Ok(await _dataverse.UploadContractOrderSignedFileAsync(orderId, file.FileName, file.ContentType, content, ct));
        }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible cargar la orden firmada.", ex); }
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> GenerateAct([FromBody] ContractActRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(Error("Selecciona el contrato y la orden."));
        try { return Ok(await _dataverse.GenerateContractDeliveryActAsync(request.ContractId, request.OrderId, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible generar el acta de entrega.", ex); }
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Download(string kind, string recordId, string fileKey, CancellationToken ct)
    {
        try
        {
            var file = await _dataverse.DownloadContractFileAsync(kind, recordId, fileKey, ct);
            return file is null ? NotFound() : File(file.Content, file.ContentType, file.FileName);
        }
        catch (InvalidOperationException ex) { return BadRequest(Error(ex.Message, ex)); }
        catch (Exception ex) { return ServerError("No fue posible descargar el archivo.", ex); }
    }

    private static async Task<byte[]> ReadFileAsync(IFormFile file, long maxBytes, CancellationToken ct)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("El archivo está vacío.");
        if (file.Length > maxBytes)
            throw new InvalidOperationException($"El archivo {file.FileName} supera el máximo de {maxBytes / 1024 / 1024} MB.");
        await using var source = file.OpenReadStream();
        using var target = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await source.CopyToAsync(target, ct);
        return target.ToArray();
    }

    private object Error(string message, Exception? ex = null) => new
    {
        message,
        detail = ex?.InnerException?.Message ?? "",
        traceId = HttpContext.TraceIdentifier
    };

    private IActionResult ServerError(string message, Exception ex)
    {
        _logger.LogError(ex, "{Message} TraceId: {TraceId}", message, HttpContext.TraceIdentifier);
        return StatusCode(StatusCodes.Status500InternalServerError, Error(message, ex));
    }

    private IActionResult AnalysisTimeout(string documentName, TimeoutException ex)
    {
        var message = $"El análisis {documentName} tardó más de lo permitido. " +
            "Intenta con un PDF con texto seleccionable o un archivo de menor tamaño.";
        _logger.LogWarning(ex, "{Message} TraceId: {TraceId}", message, HttpContext.TraceIdentifier);
        return StatusCode(StatusCodes.Status504GatewayTimeout, Error(message, ex));
    }

    public sealed class ContractActRequest
    {
        public string ContractId { get; set; } = "";
        public string OrderId { get; set; } = "";
    }
}
