using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Calculator;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Calculator)]
public sealed class CalculatorController : Controller
{
    private readonly IDataverseService _dataverse;
    private readonly IQuoteCalculator _calculator;
    private readonly IAzureOpenAIQuoteProposalService _proposalService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CalculatorOptions _calculatorOptions;
    private readonly ILogger<CalculatorController> _logger;
    private const string DataverseScope = "https://orgc79ca19c.crm2.dynamics.com/user_impersonation";
    private const int ProvisioningDescriptionMaxLength = 4000;
    private const int ProvisioningLongDescriptionMaxLength = 1048576;
    private const string ProvisioningDescriptionField = "cr07a_aprovisionamientodetallelargo";
    private const string ProvisioningLegacyDescriptionField = "cr07a_description";
    private const int ProvisioningContractKindNewBusinessValue = 645250000;
    private const int ProvisioningContractKindRenewalValue = 645250001;
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly JsonSerializerOptions ProvisioningDescriptionJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CalculatorController(
        IDataverseService dataverse,
        IQuoteCalculator calculator,
        IAzureOpenAIQuoteProposalService proposalService,
        IHttpClientFactory httpClientFactory,
        IOptions<CalculatorOptions> calculatorOptions,
        ILogger<CalculatorController> logger)
    {
        _dataverse = dataverse;
        _calculator = calculator;
        _proposalService = proposalService;
        _httpClientFactory = httpClientFactory;
        _calculatorOptions = calculatorOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct);
        var storedScenarios = await _dataverse.GetScenariosForUserAsync(ct);

        ViewData["CurrentUser"] = currentUser;
        ViewData["StoredScenarios"] = storedScenarios;
        return View();
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ProductSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchProductsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientSearch([FromQuery] string q, CancellationToken ct)
    {
        var items = await _dataverse.SearchClientsAsync(q, top: 12, ct: ct);
        return Json(items);
    }

    [HttpGet]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> ClientRenewalDates([FromQuery] string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new
            {
                message = "Debes seleccionar un cliente valido.",
                detail = "El parametro clientId llego vacio.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var items = await _dataverse.SearchRenewalDatesByClientAsync(clientId, top: 250, ct: ct);
            return Json(items);
        }
        catch (InvalidOperationException ex)
        {
            var traceId = HttpContext.TraceIdentifier;

            _logger.LogError(
                ex,
                "Error consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "No se pudieron consultar las fechas de renovacion.",
                detail = BuildDiagnosticMessage(ex),
                traceId
            });
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            var detail = CompactDiagnosticMessage(ex.Message);

            _logger.LogError(
                ex,
                "Error inesperado consultando fechas de renovacion para cliente {ClientId}. TraceId: {TraceId}.",
                clientId,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Ocurrio un error inesperado consultando las fechas de renovacion.",
                detail = string.IsNullOrWhiteSpace(detail)
                    ? "No se recibio detalle adicional del servidor."
                    : detail,
                traceId
            });
        }
    }

    [HttpPost]
    public IActionResult Calculate([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var result = _calculator.Calculate(input);

        return Json(new
        {
            points = result.Points,
            commission = result.Commission,
            prorationDays = result.ProrationDays,
            prorationFactor = result.ProrationFactor,
            totalMonthlySale = result.TotalMonthlySale,
            totalSale = result.TotalSale
        });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SaveScenario([FromBody] ScenarioSaveRequest input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (string.IsNullOrWhiteSpace(input.ScenarioId))
            return BadRequest("ScenarioId requerido.");

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("Debe incluir lÃ­neas.");

        await _dataverse.UpsertScenarioAsync(input, ct);
        return Ok(new { ok = true });
    }

    [HttpDelete]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> DeleteScenario([FromQuery] string scenarioId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest("ScenarioId requerido.");

        await _dataverse.DeleteScenarioAsync(scenarioId, ct);
        return Ok(new { ok = true });
    }

    [HttpPost]
    public IActionResult Export([FromBody] QuoteScenarioInput input)
    {
        if (input is null)
            return BadRequest("Payload invÃ¡lido.");

        NormalizeProrationRules(input);

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("No hay lÃ­neas para exportar.");

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var productValidation = ValidateSelectedProducts(input.Lines, "exportar el Excel");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return BadRequest(productValidation);

        var fileName = BuildFileName(input.ScenarioName);
        using var workbook = BuildWorkbook(input);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    public async Task<IActionResult> GenerateProposal([FromBody] QuoteScenarioInput input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invalido.");

        NormalizeProrationRules(input);

        if (input.Lines is null || input.Lines.Count == 0)
            return BadRequest("No hay lineas para generar la propuesta.");

        var licenseValidation = ValidateLicenseCaps(input);
        if (!string.IsNullOrWhiteSpace(licenseValidation))
            return BadRequest(licenseValidation);

        var productValidation = ValidateSelectedProducts(input.Lines, "generar la propuesta");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return BadRequest(productValidation);

        try
        {
            var result = _calculator.Calculate(input);
            var proposalInput = new QuoteProposalGenerationInput
            {
                Scenario = input,
                Result = result,
                PreparedByName = ResolveCurrentUserName(),
                PreparedByEmail = ResolveCurrentUserEmail(),
                GeneratedAt = DateTimeOffset.UtcNow
            };
            var html = await _proposalService.GenerateProposalHtmlAsync(proposalInput, ct);
            return File(
                Encoding.UTF8.GetBytes(html),
                "text/html; charset=utf-8",
                BuildHtmlFileName(input.ScenarioName));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible generar la propuesta HTML.");
            return BadRequest(BuildDiagnosticMessage(ex));
        }
    }

    [HttpPost]
    public IActionResult ValidateProvisioning([FromBody] ProvisioningRequestInput? input)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        return Ok(new { ok = true });
    }

    [HttpPost]
    [AuthorizeForScopes(Scopes = new[] { DataverseScope })]
    public async Task<IActionResult> SubmitProvisioning([FromBody] ProvisioningRequestInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invÃƒÂ¡lido.");

        var validationError = ValidateProvisioningPayload(input);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(_calculatorOptions.ProvisioningRequestFlowUrl))
        {
            return BadRequest("Configura la URL del flujo en Calculator:ProvisioningRequestFlowUrl antes de enviar la solicitud.");
        }

        try
        {
            await EnsureHardwareProductsForProvisioningAsync(input, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(BuildDiagnosticMessage(ex));
        }

        var requestId = Guid.NewGuid().ToString("N");
        var payload = BuildProvisioningFlowPayload(input, requestId);
        var client = _httpClientFactory.CreateClient();
        try
        {
            using var response = await client.PostAsJsonAsync(_calculatorOptions.ProvisioningRequestFlowUrl, payload, cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var message = string.IsNullOrWhiteSpace(body)
                    ? $"El flujo respondiÃ³ con error HTTP {(int)response.StatusCode}."
                    : body;
                return BadRequest(message);
            }
        }
        catch (Exception ex)
        {
            var message = BuildDiagnosticMessage(ex);
            return BadRequest(message);
        }

        return Ok(new
        {
            ok = true,
            requestId,
            message = "Solicitud enviada a aprobacion."
        });
    }

    private static XLWorkbook BuildWorkbook(QuoteScenarioInput input)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("CotizaciÃ³n");

        var row = 1;
        sheet.Cell(row, 1).Value = "Escenario";
        sheet.Cell(row, 2).Value = input.ScenarioName;
        row++;

        sheet.Cell(row, 1).Value = "Tipo de negocio";
        sheet.Cell(row, 2).Value = input.DealType.ToString();
        row++;

        if (input.RequiresProration)
        {
            sheet.Cell(row, 1).Value = "Prorrateo";
            sheet.Cell(row, 2).Value = input.StartDate.HasValue && input.EndDate.HasValue
                ? $"{input.StartDate:yyyy-MM-dd} al {input.EndDate:yyyy-MM-dd}"
                : "Pendiente fechas de prorrateo";
            row++;
        }

        row++;

        var headers = new List<string>
        {
            "Tipo",
            "Producto",
            "Margen %",
            "DuraciÃ³n (meses)",
            "Venta UND",
            "Cantidad",
            "Venta Mensual",
            "Venta Total",
            "Precio Sugerido"
        };

        var headerRow = row;
        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        sheet.Range(headerRow, 1, headerRow, headers.Count).Style.Font.Bold = true;
        row++;

        var idxSaleUnit = headers.IndexOf("Venta UND") + 1;
        var idxMonthly = headers.IndexOf("Venta Mensual") + 1;
        var idxTotal = headers.IndexOf("Venta Total") + 1;
        var idxSuggested = headers.IndexOf("Precio Sugerido") + 1;

        decimal tSaleUnit = 0m, tMonthly = 0m, tTotal = 0m, tSuggested = 0m;

        foreach (var line in input.Lines)
        {
            var computed = ComputeLine(line);

            sheet.Cell(row, 1).Value = line.BusinessType.ToString();
            sheet.Cell(row, 2).Value = line.ProductDescription;
            sheet.Cell(row, 3).Value = Round2(line.MarginPercent);
            sheet.Cell(row, 4).Value = line.ContractMonths;
            sheet.Cell(row, idxSaleUnit).Value = computed.SaleUnit;
            sheet.Cell(row, 6).Value = line.Quantity;
            sheet.Cell(row, idxMonthly).Value = computed.Monthly;
            sheet.Cell(row, idxTotal).Value = computed.Total;
            sheet.Cell(row, idxSuggested).Value = Round2(line.SuggestedRetailPrice);

            tSaleUnit += computed.SaleUnit * line.Quantity;
            tMonthly += computed.Monthly;
            tTotal += computed.Total;
            tSuggested += line.SuggestedRetailPrice * line.Quantity;

            row++;
        }

        sheet.Cell(row, 1).Value = "Totales";
        sheet.Cell(row, 3).Value = "â€”";
        sheet.Cell(row, 4).Value = "â€”";
        sheet.Cell(row, idxSaleUnit).Value = Round2(tSaleUnit);
        sheet.Cell(row, 6).Value = "â€”";
        sheet.Cell(row, idxMonthly).Value = Round2(tMonthly);
        sheet.Cell(row, idxTotal).Value = Round2(tTotal);
        sheet.Cell(row, idxSuggested).Value = Round2(tSuggested);

        sheet.Range(headerRow + 1, 1, row, headers.Count).Style.NumberFormat.Format = "#,##0.00";
        sheet.Column(6).Style.NumberFormat.Format = "0";
        sheet.Column(4).Style.NumberFormat.Format = "0";
        sheet.Column(3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Columns().AdjustToContents();

        return workbook;
    }

    private static string? ValidateProvisioningPayload(ProvisioningRequestInput input)
    {
        if (input.LineItems is null || input.LineItems.Count == 0)
            return "No hay lÃ­neas para enviar.";

        var productValidation = ValidateSelectedProducts(input.LineItems, "enviar la solicitud de aprovisionamiento");
        if (!string.IsNullOrWhiteSpace(productValidation))
            return productValidation;

        if (input.Scenario is null)
            return "No se recibio el tipo de negocio del escenario.";

        if (!Enum.IsDefined(typeof(DealType), input.Scenario.DealTypeValue))
            return "El tipo de negocio del escenario no es valido.";

        var contractKindCode = ResolveProvisioningContractKindCode(input.Aprovisionamiento);
        if (contractKindCode is not ProvisioningContractKindNewBusinessValue and not ProvisioningContractKindRenewalValue)
            return "Selecciona si el contrato es negocio nuevo o renovacion.";

        var attachment = input.Attachment;
        if (attachment is null)
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        if (string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.Base64))
            return "Debes adjuntar la oferta autorizada o correo de aprobaciÃ³n.";

        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant().TrimStart('.');
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "jpg", "jpeg", "doc", "docx"
        };
        if (!allowedExtensions.Contains(extension))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        var allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/jpg",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        if (string.IsNullOrWhiteSpace(attachment.ContentType) || !allowedContentTypes.Contains(attachment.ContentType))
            return "El adjunto debe ser PDF, JPG/JPEG o DOC/DOCX.";

        try
        {
            _ = Convert.FromBase64String(attachment.Base64);
        }
        catch (FormatException)
        {
            return "El adjunto no es vÃ¡lido.";
        }

        return null;
    }

    private async Task EnsureHardwareProductsForProvisioningAsync(ProvisioningRequestInput input, CancellationToken ct)
    {
        if (input.LineItems is null || input.LineItems.Count == 0)
            return;

        var cache = new Dictionary<string, ProductLookupItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var (line, index) in input.LineItems.Select((value, index) => (value, index)))
        {
            if (!IsHardwareLine(line.Tipo) || !string.IsNullOrWhiteSpace(line.ProductoId))
                continue;

            var productName = (line.ProductoNombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(productName))
                throw new InvalidOperationException($"La linea {index + 1} de Hardware no tiene producto.");

            var suggestedRetailPrice = line.VentaUnd > 0m
                ? line.VentaUnd
                : line.CostoUnd * (1m + (line.MargenPorcentaje / 100m));
            var cacheKey = BuildHardwareProductCacheKey(productName, line.CostoUnd, suggestedRetailPrice);

            if (!cache.TryGetValue(cacheKey, out var product))
            {
                product = await _dataverse.EnsureCalculatorProductAsync(new ProductCreateInput
                {
                    Description = productName,
                    PurchasePrice = line.CostoUnd,
                    SuggestedRetailPrice = suggestedRetailPrice,
                    Acelerador = 0m
                }, ct);
                cache[cacheKey] = product;
            }

            line.ProductoId = product.Id;
            if (string.IsNullOrWhiteSpace(line.LineId) || line.LineId.StartsWith("line-", StringComparison.OrdinalIgnoreCase))
                line.LineId = product.Id;
        }
    }

    private static string BuildHardwareProductCacheKey(string productName, decimal costUnit, decimal suggestedRetailPrice) =>
        string.Join(
            "|",
            productName.Trim().ToUpperInvariant(),
            Round2(costUnit).ToString("0.##", CultureInfo.InvariantCulture),
            Round2(suggestedRetailPrice).ToString("0.##", CultureInfo.InvariantCulture));

    private static object BuildProvisioningFlowPayload(
        ProvisioningRequestInput input,
        string requestId)
    {
        var requester = input.Requester;
        var cliente = input.Cliente;
        var aprovisionamiento = input.Aprovisionamiento;
        var scenario = input.Scenario;
        var resultado = input.Resultado;
        var attachment = input.Attachment;
        var dealTypeValue = ResolveDealTypeValue(scenario);
        var dealTypeLabel = ResolveDealTypeLabel(scenario);
        var contractKindCode = ResolveProvisioningContractKindCode(aprovisionamiento);
        var contractKindLabel = ResolveProvisioningContractKindLabel(contractKindCode, aprovisionamiento);
        var isNewBusinessContract = contractKindCode == ProvisioningContractKindNewBusinessValue;
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var lineItems = input.LineItems.Select(item => new ProvisioningFlowLinePayload
        {
            LineId = item.LineId?.Trim() ?? "",
            ProductoId = item.ProductoId?.Trim() ?? "",
            ProductoNombre = item.ProductoNombre?.Trim() ?? "",
            Cantidad = Round2(item.Cantidad),
            Number = Round2(item.Number),
            CostoUnd = Round2(item.CostoUnd),
            VentaUnd = Round2(item.VentaUnd),
            MargenPorcentaje = Round2(item.MargenPorcentaje),
            DuracionMeses = item.DuracionMeses,
            VentaMensual = Round2(item.VentaMensual),
            VentaTotal = Round2(item.VentaTotal),
            TieneIva = item.TieneIva,
            Tipo = item.Tipo?.Trim() ?? "",
            RequiereProrrateo = scenario?.RequiresProration ?? false,
            Inicio = normalizedScenarioStartDate,
            Final = normalizedScenarioEndDate
        }).ToList();
        var descriptionText = BuildFullProvisioningDescription(cliente, aprovisionamiento, scenario, resultado, lineItems, dealTypeLabel);
        var legacyDescriptionText = BuildLimitedProvisioningDescription(cliente, aprovisionamiento, scenario, resultado, lineItems, dealTypeLabel);
        var lineItemsJson = SerializeDetailedProvisioningLines(lineItems);
        var lineItemsTableText = BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: true);
        var lineItemsTableMarkdown = BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: false);
        var lineItemsTableHtml = BuildProvisioningLineItemsHtmlTable(lineItems);
        var notificationSummaryText = BuildProvisioningNotificationSummaryText(requester, cliente, aprovisionamiento, scenario, resultado, dealTypeLabel, contractKindLabel, requestId);
        var teamsMessageMarkdown = BuildProvisioningTeamsMessageMarkdown(notificationSummaryText, lineItemsTableMarkdown);
        var emailHtml = BuildProvisioningEmailHtml(notificationSummaryText, lineItemsTableHtml);

        return new
        {
            requestId,
            source = input.Source?.Trim() ?? "",
            businessId = input.BusinessId?.Trim() ?? "",
            requester = requester is null ? null : new
            {
                systemUserId = requester.SystemUserId?.Trim() ?? "",
                displayName = requester.DisplayName?.Trim() ?? "",
                email = requester.Email?.Trim() ?? ""
            },
            cliente = cliente is null ? null : new
            {
                clienteId = cliente.ClienteId?.Trim() ?? "",
                nombre = cliente.Nombre?.Trim() ?? ""
            },
            aprovisionamiento = aprovisionamiento is null ? null : new
            {
                fecha = aprovisionamiento.Fecha?.Trim() ?? "",
                tipoContratoCode = aprovisionamiento.TipoContratoCode?.Trim() ?? "",
                tipoContratoLabel = aprovisionamiento.TipoContratoLabel?.Trim() ?? "",
                tipoContratoPuntajeCode = contractKindCode,
                tipoContratoPuntajeLabel = contractKindLabel,
                cr07a_tipodecontrato = contractKindCode,
                esNegocioNuevo = isNewBusinessContract,
                esRenovacion = contractKindCode == ProvisioningContractKindRenewalValue
            },
            scenario = scenario is null ? null : new
            {
                dealTypeValue,
                dealTypeLabel,
                contractKindCode,
                contractKindLabel,
                shouldProvisionCloudProduct = isNewBusinessContract,
                requiresProration = scenario.RequiresProration,
                startDate = normalizedScenarioStartDate,
                endDate = normalizedScenarioEndDate
            },
            resultado = resultado is null ? null : new
            {
                puntaje = RoundWholeNumber(resultado.Puntaje),
                comision = RoundWholeNumber(resultado.Comision),
                prorrateoDias = resultado.ProrrateoDias,
                prorrateoFactor = RoundWholeNumber(resultado.ProrrateoFactor),
                prorrateoTexto = resultado.ProrrateoTexto?.Trim() ?? "",
                ventaMensualTotal = RoundWholeNumber(resultado.VentaMensualTotal),
                ventaTotal = RoundWholeNumber(resultado.VentaTotal),
                ventaTotalAnual = RoundWholeNumber(resultado.VentaTotalAnual)
            },
            descriptionText,
            legacyDescriptionText,
            lineItemsJson,
            lineItemsTableText,
            lineItemsTableMarkdown,
            lineItemsTableHtml,
            notificationSummaryText,
            teamsMessageMarkdown,
            emailHtml,
            descriptionTextLength = descriptionText.Length,
            legacyDescriptionTextLength = legacyDescriptionText.Length,
            dataverseFields = new
            {
                description = ProvisioningDescriptionField,
                legacyDescription = ProvisioningLegacyDescriptionField
            },
            notification = new
            {
                summaryText = notificationSummaryText,
                teamsMarkdown = teamsMessageMarkdown,
                emailHtml,
                lineItemsTableText,
                lineItemsTableMarkdown,
                lineItemsTableHtml
            },
            lineItems = lineItems.Select(item => new
            {
                lineId = item.LineId,
                productoId = item.ProductoId,
                productoNombre = item.ProductoNombre,
                // The Power Automate trigger schema expects integers for these fields.
                cantidad = RoundWholeNumber(item.Cantidad),
                number = RoundWholeNumber(item.Number),
                costoUnd = RoundWholeNumber(item.CostoUnd),
                ventaUnd = RoundWholeNumber(item.VentaUnd),
                margenPorcentaje = RoundWholeNumber(item.MargenPorcentaje),
                duracionMeses = item.DuracionMeses,
                ventaMensual = RoundWholeNumber(item.VentaMensual),
                ventaTotal = RoundWholeNumber(item.VentaTotal),
                tieneIva = item.TieneIva,
                tipo = item.Tipo,
                requiereProrrateo = item.RequiereProrrateo,
                inicio = item.Inicio,
                final = item.Final
            }),
            attachment = attachment is null ? null : new
            {
                fileName = attachment.FileName?.Trim() ?? "",
                contentType = attachment.ContentType?.Trim() ?? "",
                base64 = attachment.Base64 ?? ""
            }
        };
    }

    private static string BuildFullProvisioningDescription(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        string dealTypeLabel)
    {
        var description = BuildProvisioningDescriptionText(
            BuildProvisioningDescriptionHeader(cliente, aprovisionamiento, scenario, resultado, dealTypeLabel),
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 160, includeCommercialFields: true, includeTechnicalFields: true));
        return TruncateTextForDescription(description, ProvisioningLongDescriptionMaxLength);
    }

    private static string BuildLimitedProvisioningDescription(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        string dealTypeLabel)
    {
        var headerText = BuildProvisioningDescriptionHeader(cliente, aprovisionamiento, scenario, resultado, dealTypeLabel);
        var detailedDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 120, includeCommercialFields: true, includeTechnicalFields: true));
        if (FitsProvisioningDescriptionLimit(detailedDescription))
            return detailedDescription;

        var compactDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 90, includeCommercialFields: true, includeTechnicalFields: false));
        if (FitsProvisioningDescriptionLimit(compactDescription))
            return compactDescription;

        foreach (var maxProductNameLength in new[] { 120, 80, 50, 30 })
        {
            compactDescription = BuildProvisioningDescriptionText(
                headerText,
                BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength, includeCommercialFields: true, includeTechnicalFields: false));
            if (FitsProvisioningDescriptionLimit(compactDescription))
                return compactDescription;
        }

        compactDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(lineItems, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false));
        if (FitsProvisioningDescriptionLimit(compactDescription))
            return compactDescription;

        return BuildProvisioningDescriptionWithLineBudget(headerText, lineItems);
    }

    private static string BuildProvisioningDescriptionHeader(
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        string dealTypeLabel)
    {
        var builder = new StringBuilder();
        var normalizedProvisioningDate = NormalizeDateLikeValue(aprovisionamiento?.Fecha);
        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        var requiresProration = scenario?.RequiresProration == true;
        var contractKindCode = ResolveProvisioningContractKindCode(aprovisionamiento);
        var contractKindLabel = ResolveProvisioningContractKindLabel(contractKindCode, aprovisionamiento);

        builder.AppendLine($"Cliente: {cliente?.Nombre?.Trim() ?? ""}");
        builder.AppendLine($"Fecha aprovisionamiento: {normalizedProvisioningDate}");
        if (!string.IsNullOrWhiteSpace(contractKindLabel))
            builder.AppendLine($"Tipo contrato: {contractKindLabel}");
        builder.AppendLine($"Tipo negocio: {dealTypeLabel}");
        builder.AppendLine($"Requiere prorrateo: {(requiresProration ? "Si" : "No")}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioStartDate))
            builder.AppendLine($"Inicio: {normalizedScenarioStartDate}");
        if (!string.IsNullOrWhiteSpace(normalizedScenarioEndDate))
            builder.AppendLine($"Final: {normalizedScenarioEndDate}");
        builder.AppendLine($"Puntaje: {FormatDecimalText(resultado?.Puntaje ?? 0m)}");
        builder.AppendLine($"Comisión: {FormatDecimalText(resultado?.Comision ?? 0m)}");
        builder.AppendLine($"Prorrateo: {(resultado?.ProrrateoTexto?.Trim() ?? (requiresProration ? "Si" : "No"))}");
        builder.AppendLine($"Venta mensual total: {FormatDecimalText(resultado?.VentaMensualTotal ?? 0m)}");
        builder.AppendLine($"Venta total anual: {FormatDecimalText(resultado?.VentaTotalAnual ?? resultado?.VentaTotal ?? 0m)}");
        return builder.ToString();
    }

    private static string BuildProvisioningDescriptionText(string headerText, string linesContent, string? extraMetadataLine = null)
    {
        var builder = new StringBuilder(headerText.Length + linesContent.Length + 24);
        builder.Append(headerText);
        if (!string.IsNullOrWhiteSpace(extraMetadataLine))
            extraMetadataLine = extraMetadataLine.Trim();
        builder.AppendLine();
        builder.AppendLine("Líneas:");
        builder.Append(linesContent);
        if (!string.IsNullOrWhiteSpace(extraMetadataLine))
        {
            builder.AppendLine();
            builder.Append(extraMetadataLine);
        }
        return builder.ToString();
    }

    private static string BuildProvisioningNotificationSummaryText(
        ProvisioningRequester? requester,
        ProvisioningClient? cliente,
        ProvisioningAprovisionamiento? aprovisionamiento,
        ProvisioningScenarioContext? scenario,
        ProvisioningResultado? resultado,
        string dealTypeLabel,
        string contractKindLabel,
        string requestId)
    {
        var rows = new List<(string Label, string Value)>
        {
            ("Solicitud", requestId),
            ("Cliente", cliente?.Nombre?.Trim() ?? ""),
            ("Solicitante", FirstNonEmpty(requester?.DisplayName, requester?.Email)),
            ("Correo solicitante", requester?.Email?.Trim() ?? ""),
            ("Fecha aprovisionamiento", NormalizeDateLikeValue(aprovisionamiento?.Fecha)),
            ("Tipo contrato", contractKindLabel),
            ("Tipo negocio", dealTypeLabel),
            ("Requiere prorrateo", scenario?.RequiresProration == true ? "Si" : "No")
        };

        var normalizedScenarioStartDate = NormalizeDateLikeValue(scenario?.StartDate);
        var normalizedScenarioEndDate = NormalizeDateLikeValue(scenario?.EndDate);
        if (!string.IsNullOrWhiteSpace(normalizedScenarioStartDate))
            rows.Add(("Inicio", normalizedScenarioStartDate));
        if (!string.IsNullOrWhiteSpace(normalizedScenarioEndDate))
            rows.Add(("Final", normalizedScenarioEndDate));

        rows.Add(("Prorrateo", resultado?.ProrrateoTexto?.Trim() ?? ""));
        rows.Add(("Puntaje", FormatDecimalForNotification(resultado?.Puntaje ?? 0m)));
        rows.Add(("Comision", FormatMoneyForNotification(resultado?.Comision ?? 0m)));
        rows.Add(("Venta mensual total", FormatMoneyForNotification(resultado?.VentaMensualTotal ?? 0m)));
        rows.Add(("Venta total anual", FormatMoneyForNotification(resultado?.VentaTotalAnual ?? resultado?.VentaTotal ?? 0m)));

        var labelWidth = rows.Max(static row => row.Label.Length);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Value))
                continue;

            builder.Append(row.Label.PadRight(labelWidth));
            builder.Append(": ");
            builder.AppendLine(row.Value.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildProvisioningTeamsMessageMarkdown(string summaryText, string lineItemsTableMarkdown)
    {
        var builder = new StringBuilder(summaryText.Length + lineItemsTableMarkdown.Length + 80);
        builder.AppendLine("**Solicitud de aprovisionamiento**");
        builder.AppendLine();
        builder.AppendLine("```");
        builder.AppendLine(summaryText);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("**Lineas solicitadas**");
        builder.Append(lineItemsTableMarkdown);
        return builder.ToString();
    }

    private static string BuildProvisioningEmailHtml(string summaryText, string lineItemsTableHtml)
    {
        var builder = new StringBuilder(summaryText.Length + lineItemsTableHtml.Length + 512);
        builder.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#172033;font-size:14px;line-height:1.45;\">");
        builder.Append("<h2 style=\"margin:0 0 14px;color:#102a43;font-size:20px;\">Solicitud de aprovisionamiento</h2>");
        builder.Append("<pre style=\"margin:0 0 18px;padding:14px;background:#f6f8fb;border:1px solid #d9e2ec;border-radius:6px;white-space:pre-wrap;font-family:Consolas,Segoe UI,Arial,sans-serif;font-size:13px;\">");
        builder.Append(WebUtility.HtmlEncode(summaryText));
        builder.Append("</pre>");
        builder.Append("<h3 style=\"margin:0 0 10px;color:#102a43;font-size:16px;\">Lineas solicitadas</h3>");
        builder.Append(lineItemsTableHtml);
        builder.Append("</div>");
        return builder.ToString();
    }

    private static string BuildProvisioningLineItemsMarkdownTable(
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems,
        int? maxProductNameLength,
        bool includeCommercialFields,
        bool includeTechnicalFields)
    {
        if (lineItems.Count == 0)
            return "_Sin lineas._";

        var builder = new StringBuilder(lineItems.Count * 180);
        if (includeCommercialFields)
        {
            builder.Append("| # | Tipo | Producto | Cant. | Costo und. | Venta und. | Margen % | Meses | Venta mensual | Venta total | IVA | Inicio | Final |");
            if (includeTechnicalFields)
                builder.Append(" Producto Id |");
            builder.AppendLine();
            builder.Append("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|---|");
            if (includeTechnicalFields)
                builder.Append(" --- |");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| # | Tipo | Producto | Cant. | Venta mensual | Venta total | IVA |");
            builder.AppendLine("|---:|---|---|---:|---:|---:|---|");
        }

        for (var index = 0; index < lineItems.Count; index++)
        {
            var item = lineItems[index];
            if (includeCommercialFields)
            {
                builder.Append("| ");
                builder.Append(index + 1);
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Tipo));
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.ProductoNombre, maxProductNameLength));
                builder.Append(" | ");
                builder.Append(FormatQuantityForNotification(item.Cantidad));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.CostoUnd));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaUnd));
                builder.Append(" | ");
                builder.Append(FormatPercentForNotification(item.MargenPorcentaje));
                builder.Append(" | ");
                builder.Append(item.DuracionMeses.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaMensual));
                builder.Append(" | ");
                builder.Append(FormatMoneyForNotification(item.VentaTotal));
                builder.Append(" | ");
                builder.Append(item.TieneIva ? "Si" : "No");
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Inicio));
                builder.Append(" | ");
                builder.Append(FormatMarkdownCell(item.Final));
                builder.Append(" |");
                if (includeTechnicalFields)
                {
                    builder.Append(' ');
                    builder.Append(FormatMarkdownCell(item.ProductoId));
                    builder.Append(" |");
                }
                builder.AppendLine();
                continue;
            }

            builder.Append("| ");
            builder.Append(index + 1);
            builder.Append(" | ");
            builder.Append(FormatMarkdownCell(item.Tipo));
            builder.Append(" | ");
            builder.Append(FormatMarkdownCell(item.ProductoNombre, maxProductNameLength));
            builder.Append(" | ");
            builder.Append(FormatQuantityForNotification(item.Cantidad));
            builder.Append(" | ");
            builder.Append(FormatMoneyForNotification(item.VentaMensual));
            builder.Append(" | ");
            builder.Append(FormatMoneyForNotification(item.VentaTotal));
            builder.Append(" | ");
            builder.Append(item.TieneIva ? "Si" : "No");
            builder.AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildProvisioningLineItemsHtmlTable(IReadOnlyList<ProvisioningFlowLinePayload> lineItems)
    {
        if (lineItems.Count == 0)
            return "<p style=\"margin:0;color:#52616b;\">Sin lineas.</p>";

        var builder = new StringBuilder(lineItems.Count * 220);
        builder.Append("<table style=\"border-collapse:collapse;width:100%;font-size:13px;\">");
        builder.Append("<thead><tr style=\"background:#102a43;color:#fff;\">");
        foreach (var header in new[] { "#", "Tipo", "Producto", "Cant.", "Venta und.", "Meses", "Venta mensual", "Venta total", "IVA" })
        {
            builder.Append("<th style=\"padding:9px 10px;border:1px solid #bcccdc;text-align:left;\">");
            builder.Append(WebUtility.HtmlEncode(header));
            builder.Append("</th>");
        }
        builder.Append("</tr></thead><tbody>");

        for (var index = 0; index < lineItems.Count; index++)
        {
            var item = lineItems[index];
            builder.Append("<tr>");
            AppendHtmlCell(builder, (index + 1).ToString(CultureInfo.InvariantCulture), alignRight: true);
            AppendHtmlCell(builder, item.Tipo);
            AppendHtmlCell(builder, item.ProductoNombre);
            AppendHtmlCell(builder, FormatQuantityForNotification(item.Cantidad), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaUnd), alignRight: true);
            AppendHtmlCell(builder, item.DuracionMeses.ToString(CultureInfo.InvariantCulture), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaMensual), alignRight: true);
            AppendHtmlCell(builder, FormatMoneyForNotification(item.VentaTotal), alignRight: true);
            AppendHtmlCell(builder, item.TieneIva ? "Si" : "No");
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static void AppendHtmlCell(StringBuilder builder, string? value, bool alignRight = false)
    {
        builder.Append("<td style=\"padding:8px 10px;border:1px solid #d9e2ec;vertical-align:top;");
        if (alignRight)
            builder.Append("text-align:right;white-space:nowrap;");
        builder.Append("\">");
        builder.Append(WebUtility.HtmlEncode(FirstNonEmpty(value, "-")));
        builder.Append("</td>");
    }

    private static string FormatMarkdownCell(string? value, int? maxLength = null)
    {
        var compact = CompactWhitespace(value);
        if (maxLength.HasValue)
            compact = TrimTextForDescription(compact, maxLength.Value);

        return string.IsNullOrWhiteSpace(compact)
            ? "-"
            : compact.Replace("|", "/", StringComparison.Ordinal);
    }

    private static string CompactWhitespace(string? value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string FormatQuantityForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture);

    private static string FormatMoneyForNotification(decimal value) =>
        "$" + Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", ColombianCulture);

    private static string FormatPercentForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture) + "%";

    private static string FormatDecimalForNotification(decimal value) =>
        Round2(value).ToString("#,0.##", ColombianCulture);

    private static string SerializeDetailedProvisioningLines(IReadOnlyList<ProvisioningFlowLinePayload> lineItems) =>
        JsonSerializer.Serialize(lineItems.Select(item => new
        {
            lineId = item.LineId,
            productoId = item.ProductoId,
            productoNombre = item.ProductoNombre,
            cantidad = item.Cantidad,
            number = item.Number,
            costoUnd = item.CostoUnd,
            ventaUnd = item.VentaUnd,
            margenPorcentaje = item.MargenPorcentaje,
            duracionMeses = item.DuracionMeses,
            ventaMensual = item.VentaMensual,
            ventaTotal = item.VentaTotal,
            tieneIva = item.TieneIva,
            tipo = item.Tipo,
            requiereProrrateo = item.RequiereProrrateo,
            inicio = item.Inicio,
            final = item.Final
        }), ProvisioningDescriptionJsonOptions);

    private static string BuildProvisioningDescriptionWithLineBudget(
        string headerText,
        IReadOnlyList<ProvisioningFlowLinePayload> lineItems)
    {
        var includedLines = new List<ProvisioningFlowLinePayload>();
        var lastAcceptedDescription = BuildProvisioningDescriptionText(
            headerText,
            BuildProvisioningLineItemsMarkdownTable(includedLines, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false),
            $"Lineas incluidas en descripcion: 0/{lineItems.Count}");

        if (!FitsProvisioningDescriptionLimit(lastAcceptedDescription))
            return TruncateProvisioningDescription(lastAcceptedDescription);

        foreach (var item in lineItems)
        {
            includedLines.Add(item);

            var candidate = BuildProvisioningDescriptionText(
                headerText,
                BuildProvisioningLineItemsMarkdownTable(includedLines, maxProductNameLength: 30, includeCommercialFields: false, includeTechnicalFields: false),
                $"Lineas incluidas en descripcion: {includedLines.Count}/{lineItems.Count}");
            if (FitsProvisioningDescriptionLimit(candidate))
            {
                lastAcceptedDescription = candidate;
                continue;
            }

            includedLines.RemoveAt(includedLines.Count - 1);
            break;
        }

        return FitsProvisioningDescriptionLimit(lastAcceptedDescription)
            ? lastAcceptedDescription
            : TruncateProvisioningDescription(lastAcceptedDescription);
    }

    private static bool FitsProvisioningDescriptionLimit(string value) =>
        value.Length <= ProvisioningDescriptionMaxLength
        && JsonSerializer.Serialize(value).Length <= ProvisioningDescriptionMaxLength;

    private static string TruncateProvisioningDescription(string value)
    {
        var truncated = TruncateTextForDescription(value, ProvisioningDescriptionMaxLength);
        if (FitsProvisioningDescriptionLimit(truncated))
            return truncated;

        var low = 0;
        var high = Math.Min(value.Length, ProvisioningDescriptionMaxLength);
        var best = "";
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = TruncateTextForDescription(value, mid);
            if (FitsProvisioningDescriptionLimit(candidate))
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private static string TrimTextForDescription(string value, int? maxLength) =>
        maxLength.HasValue
            ? TruncateTextForDescription(value, maxLength.Value)
            : value;

    private static string TruncateTextForDescription(string value, int maxLength)
    {
        if (maxLength <= 0)
            return "";

        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        if (maxLength <= 3)
            return value[..maxLength];

        return value[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static string ResolveDealTypeLabel(ProvisioningScenarioContext? scenario)
    {
        return ResolveDealTypeValue(scenario) switch
        {
            0 => "ClienteNuevo",
            1 => "CrossSale",
            2 => "Renovacion 1 vez",
            3 => "Renovacion 2 veces",
            4 => "Renovacion 3 veces o mas",
            _ => "ClienteNuevo"
        };
    }

    private static int ResolveDealTypeValue(ProvisioningScenarioContext? scenario)
    {
        if (scenario?.RequiresProration == true)
            return (int)DealType.CrossSale;

        if (scenario is not null && Enum.IsDefined(typeof(DealType), scenario.DealTypeValue))
            return scenario.DealTypeValue;

        return (int)DealType.ClienteNuevo;
    }

    private static int ResolveProvisioningContractKindCode(ProvisioningAprovisionamiento? aprovisionamiento)
    {
        var rawCode = aprovisionamiento?.TipoContratoCode?.Trim();
        if (int.TryParse(rawCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode)
            && parsedCode is ProvisioningContractKindNewBusinessValue or ProvisioningContractKindRenewalValue)
        {
            return parsedCode;
        }

        var normalizedLabel = NormalizeContractKindToken(aprovisionamiento?.TipoContratoLabel);
        return normalizedLabel switch
        {
            "negocionuevo" or "nuevo" => ProvisioningContractKindNewBusinessValue,
            "renovacion" or "renovación" or "contratoexistente" => ProvisioningContractKindRenewalValue,
            _ => 0
        };
    }

    private static string ResolveProvisioningContractKindLabel(int contractKindCode, ProvisioningAprovisionamiento? aprovisionamiento)
    {
        if (!string.IsNullOrWhiteSpace(aprovisionamiento?.TipoContratoLabel))
            return aprovisionamiento.TipoContratoLabel.Trim();

        return contractKindCode switch
        {
            ProvisioningContractKindNewBusinessValue => "Negocio nuevo",
            ProvisioningContractKindRenewalValue => "Renovacion",
            _ => ""
        };
    }

    private static string NormalizeContractKindToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value
            .Trim()
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(normalized)
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");
    }

    private static string NormalizeDateLikeValue(string? raw, bool preferIsoWhenPossible = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var trimmed = raw.Trim();
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && !DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("es-CO"), DateTimeStyles.AssumeUniversal, out parsed))
        {
            return trimmed;
        }

        return preferIsoWhenPossible
            ? parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            : parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatDecimalText(decimal value) =>
        Round2(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static string? ValidateLicenseCaps(QuoteScenarioInput input)
    {
        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<QuoteLineInput> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productDescription = line.ProductDescription?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productDescription))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductId) && line.BusinessType != BusinessType.Hardware)
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static string? ValidateSelectedProducts(IReadOnlyList<ProvisioningLineItem> lines, string actionLabel)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var productName = line.ProductoNombre?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(productName))
                return $"La linea {index + 1} no tiene producto.";

            if (string.IsNullOrWhiteSpace(line.ProductoId) && !IsHardwareLine(line.Tipo))
                return $"La linea {index + 1} debe seleccionar un producto valido de la lista antes de {actionLabel}.";
        }

        return null;
    }

    private static bool IsHardwareLine(string? tipo) =>
        string.Equals((tipo ?? "").Trim(), BusinessType.Hardware.ToString(), StringComparison.OrdinalIgnoreCase);

    private static void NormalizeProrationRules(QuoteScenarioInput input)
    {
        if (input.RequiresProration)
            input.DealType = DealType.CrossSale;
    }

    private static void NormalizeProrationRules(ScenarioSaveRequest input)
    {
        if (input.RequiresProration)
            input.DealType = (int)DealType.CrossSale;
    }

    private static ExportLine ComputeLine(QuoteLineInput line)
    {
        var saleUnit = Round2(line.CostUnit * (1m + (line.MarginPercent / 100m)));
        var monthly = Round2(saleUnit * line.Quantity);
        var total = Round2(monthly * line.ContractMonths);

        return new ExportLine(saleUnit, monthly, total);
    }

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static int RoundWholeNumber(decimal value) =>
        (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string BuildFileName(string? scenarioName)
    {
        var safe = string.Join("_", (scenarioName ?? "Cotizacion").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Cotizacion";
        return $"{safe}.xlsx";
    }

    private static string BuildHtmlFileName(string? scenarioName)
    {
        var safe = string.Join("_", (scenarioName ?? "Propuesta").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Propuesta";
        return $"{safe}_propuesta.html";
    }

    private string ResolveCurrentUserName()
    {
        return User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.Identity?.Name
            ?? "";
    }

    private string ResolveCurrentUserEmail()
    {
        return User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("email")?.Value
            ?? "";
    }

    private sealed class ProvisioningFlowLinePayload
    {
        public string LineId { get; set; } = "";
        public string ProductoId { get; set; } = "";
        public string ProductoNombre { get; set; } = "";
        public decimal Cantidad { get; set; }
        public decimal Number { get; set; }
        public decimal CostoUnd { get; set; }
        public decimal VentaUnd { get; set; }
        public decimal MargenPorcentaje { get; set; }
        public int DuracionMeses { get; set; }
        public decimal VentaMensual { get; set; }
        public decimal VentaTotal { get; set; }
        public bool TieneIva { get; set; }
        public string Tipo { get; set; } = "";
        public bool RequiereProrrateo { get; set; }
        public string Inicio { get; set; } = "";
        public string Final { get; set; } = "";
    }

    private static string BuildDiagnosticMessage(Exception ex)
    {
        var messages = new List<string>();

        for (var current = ex; current is not null && messages.Count < 3; current = current.InnerException)
        {
            var message = CompactDiagnosticMessage(current.Message);
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (messages.Contains(message, StringComparer.OrdinalIgnoreCase))
                continue;

            messages.Add(message);
        }

        return messages.Count == 0
            ? "No se recibio detalle adicional del backend."
            : string.Join(" | ", messages);
    }

    private static string CompactDiagnosticMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var compact = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return compact.Length > 500
            ? $"{compact[..497]}..."
            : compact;
    }

    private sealed record ExportLine(decimal SaleUnit, decimal Monthly, decimal Total);
}
