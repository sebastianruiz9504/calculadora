using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Contracts;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIContractsService : IContractsAiService
{
    private const int MaxInputBytes = 25 * 1024 * 1024;
    private const int MaxExtractedCharacters = 80_000;
    private const int RutMaxOutputTokens = 2_500;
    private const int OfferMaxOutputTokens = 6_000;
    private const int RutTimeoutSeconds = 90;
    private const int OfferTimeoutSeconds = 120;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<AzureOpenAIContractsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientStringJsonConverter(), new LenientStringListJsonConverter() }
    };

    private static readonly JsonElement RutResponseSchema = ParseSchema("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "legalName": { "type": "string" },
            "nit": { "type": "string" },
            "verificationDigit": { "type": "string" },
            "legalForm": { "type": "string" },
            "mainAddress": { "type": "string" },
            "notificationAddress": { "type": "string" },
            "city": { "type": "string" },
            "department": { "type": "string" },
            "email": { "type": "string" },
            "phone": { "type": "string" },
            "legalRepresentativeName": { "type": "string" },
            "legalRepresentativeId": { "type": "string" },
            "taxResponsibilities": { "type": "array", "items": { "type": "string" } },
            "economicActivities": { "type": "array", "items": { "type": "string" } },
            "sourceNotes": { "type": "array", "items": { "type": "string" } },
            "confidence": { "type": "number" }
          },
          "required": [
            "legalName", "nit", "verificationDigit", "legalForm", "mainAddress",
            "notificationAddress", "city", "department", "email", "phone",
            "legalRepresentativeName", "legalRepresentativeId", "taxResponsibilities",
            "economicActivities", "sourceNotes", "confidence"
          ]
        }
        """);

    private static readonly JsonElement OfferResponseSchema = ParseSchema("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "contractType": { "type": "string" },
            "currency": { "type": "string" },
            "durationMonths": { "type": "integer" },
            "paymentDays": { "type": "integer" },
            "nonRenewalNoticeDays": { "type": "integer" },
            "deliveryBusinessDays": { "type": "integer" },
            "startCondition": { "type": "string" },
            "executionAddress": { "type": "string" },
            "billingEmail": { "type": "string" },
            "clientContact": { "type": "string" },
            "recommendedTitle": { "type": "string" },
            "summary": { "type": "string" },
            "equipmentLines": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "equipmentOrService": { "type": "string" },
                  "quantity": { "type": "integer" },
                  "brand": { "type": "string" },
                  "model": { "type": "string" },
                  "colorMode": { "type": "string" },
                  "includedPrints": { "type": "integer" },
                  "includedScans": { "type": "integer" },
                  "monthlyFee": { "type": "number" },
                  "additionalClickPrice": { "type": "number" },
                  "vatPercent": { "type": "number" },
                  "vatIncluded": { "type": "boolean" },
                  "notes": { "type": "string" }
                },
                "required": [
                  "equipmentOrService", "quantity", "brand", "model", "colorMode",
                  "includedPrints", "includedScans", "monthlyFee", "additionalClickPrice",
                  "vatPercent", "vatIncluded", "notes"
                ]
              }
            },
            "valueAddedServices": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "description": { "type": "string" },
                  "scope": { "type": "string" },
                  "frequency": { "type": "string" },
                  "deliveryMethod": { "type": "string" }
                },
                "required": ["description", "scope", "frequency", "deliveryMethod"]
              }
            },
            "specialConditions": { "type": "array", "items": { "type": "string" } },
            "warnings": { "type": "array", "items": { "type": "string" } },
            "confidence": { "type": "number" }
          },
          "required": [
            "contractType", "currency", "durationMonths", "paymentDays",
            "nonRenewalNoticeDays", "deliveryBusinessDays", "startCondition",
            "executionAddress", "billingEmail", "clientContact", "recommendedTitle",
            "summary", "equipmentLines", "valueAddedServices", "specialConditions",
            "warnings", "confidence"
          ]
        }
        """);

    public AzureOpenAIContractsService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIContractsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ContractRutExtractionDto> AnalyzeRutAsync(
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        ValidateFile(fileName, content);
        var json = await AnalyzeDocumentAsync(
            fileName,
            contentType,
            content,
            BuildRutPrompt(),
            "Extrae la información jurídica del RUT adjunto. Devuelve únicamente el objeto JSON solicitado.",
            "contract_rut_extraction",
            RutResponseSchema,
            RutMaxOutputTokens,
            RutTimeoutSeconds,
            ct);

        var result = JsonSerializer.Deserialize<ContractRutExtractionDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Azure OpenAI no devolvio una extracción RUT válida.");
        NormalizeRut(result);
        return result;
    }

    public async Task<ContractOfferExtractionDto> AnalyzeOfferAsync(
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        ValidateFile(fileName, content);
        var json = await AnalyzeDocumentAsync(
            fileName,
            contentType,
            content,
            BuildOfferPrompt(),
            "Analiza la oferta comercial aprobada y conviértela al esquema Copiers. Devuelve únicamente el objeto JSON solicitado.",
            "contract_offer_extraction",
            OfferResponseSchema,
            OfferMaxOutputTokens,
            OfferTimeoutSeconds,
            ct);

        var result = JsonSerializer.Deserialize<ContractOfferExtractionDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Azure OpenAI no devolvio una extracción de oferta válida.");
        NormalizeOffer(result);
        if (result.EquipmentLines.Count == 0)
            throw new InvalidOperationException("La oferta no contiene líneas de equipos identificables. Revisa el archivo o diligencia las líneas manualmente.");

        return result;
    }

    private async Task<string> AnalyzeDocumentAsync(
        string fileName,
        string contentType,
        byte[] content,
        string systemPrompt,
        string userInstruction,
        string schemaName,
        JsonElement responseSchema,
        int maxOutputTokens,
        int timeoutSeconds,
        CancellationToken ct)
    {
        ValidateOptions();
        var client = _httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        var endpoint = _options.Endpoint.TrimEnd('/');
        var uri = $"{endpoint}/openai/v1/responses";
        var documentInput = BuildAzureInput(fileName, contentType, content);
        var effectiveTimeoutSeconds = Math.Clamp(Math.Min(_options.TimeoutSeconds, timeoutSeconds), 45, 180);
        var startedAt = DateTimeOffset.UtcNow;

        var userContent = new List<object>
        {
            documentInput.Payload,
            new { type = "input_text", text = userInstruction }
        };

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = _options.DeploymentName.Trim(),
            ["input"] = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[] { new { type = "input_text", text = systemPrompt } }
                },
                new
                {
                    role = "user",
                    content = userContent
                }
            },
            ["max_output_tokens"] = Math.Clamp(Math.Min(_options.MaxTokens, maxOutputTokens), 2_000, 8_000),
            ["reasoning"] = new { effort = "low" },
            ["text"] = new
            {
                verbosity = "low",
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema = responseSchema
                }
            },
            ["store"] = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeoutSeconds));

        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Azure OpenAI superó el límite de {effectiveTimeoutSeconds} segundos al procesar el documento. " +
                "Intenta con un PDF con texto seleccionable o un archivo de menor tamaño.",
                ex);
        }

        using (response)
        {
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Azure OpenAI fallo al analizar {DocumentKind}. Status={StatusCode}.",
                Path.GetExtension(fileName),
                (int)response.StatusCode);
            throw new InvalidOperationException($"Azure OpenAI error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {TrimError(body)}");
        }

            _logger.LogInformation(
                "Azure OpenAI analizó {FileName} usando {InputMode} en {ElapsedMilliseconds} ms.",
                SanitizeFileName(fileName),
                documentInput.Mode,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            return ExtractJsonObject(ExtractResponsesText(body));
        }
    }

    private static DocumentInput BuildAzureInput(string fileName, string contentType, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var normalizedContentType = NormalizeContentType(contentType, extension);
        if (extension == ".pdf")
        {
            var pdfText = TryExtractPdfText(content);
            if (HasUsefulPdfText(pdfText))
            {
                return new DocumentInput(
                    new
                    {
                        type = "input_text",
                        text = $"ARCHIVO PDF: {SanitizeFileName(fileName)}\n\n{LimitText(pdfText)}"
                    },
                    "pdf_text");
            }

            return new DocumentInput(
                new
                {
                    type = "input_file",
                    filename = SanitizeFileName(fileName),
                    file_data = $"data:application/pdf;base64,{Convert.ToBase64String(content)}"
                },
                "pdf_vision");
        }

        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            return new DocumentInput(
                new
                {
                    type = "input_image",
                    image_url = $"data:{normalizedContentType};base64,{Convert.ToBase64String(content)}"
                },
                "image_vision");
        }

        var extractedText = extension switch
        {
            ".docx" => ExtractDocxText(content),
            ".xlsx" => ExtractXlsxText(content),
            ".csv" or ".txt" or ".json" => DecodeText(content),
            _ => throw new InvalidOperationException("Formato no soportado. Usa PDF, DOCX, XLSX, CSV, TXT, JPG o PNG.")
        };

        return new DocumentInput(
            new
            {
                type = "input_text",
                text = $"ARCHIVO: {SanitizeFileName(fileName)}\n\n{LimitText(extractedText)}"
            },
            "extracted_text");
    }

    private static string TryExtractPdfText(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = PdfDocument.Open(stream);
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page).Trim();
                if (string.IsNullOrWhiteSpace(pageText))
                    continue;

                builder.AppendLine($"--- PÁGINA {page.Number} ---");
                builder.AppendLine(pageText);
                if (builder.Length >= MaxExtractedCharacters)
                    break;
            }

            return LimitText(builder.ToString());
        }
        catch
        {
            return "";
        }
    }

    private static bool HasUsefulPdfText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var nonWhitespace = value.Count(static ch => !char.IsWhiteSpace(ch));
        var lettersOrDigits = value.Count(static ch => char.IsLetterOrDigit(ch));
        return nonWhitespace >= 250 && lettersOrDigits >= nonWhitespace * 0.55;
    }

    private static string ExtractDocxText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var parts = archive.Entries
            .Where(static entry =>
                entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        foreach (var entry in parts)
        {
            using var partStream = entry.Open();
            var document = XDocument.Load(partStream);
            foreach (var paragraph in document.Descendants(w + "p"))
            {
                var text = string.Concat(paragraph.Descendants(w + "t").Select(static node => node.Value));
                if (!string.IsNullOrWhiteSpace(text))
                    builder.AppendLine(text.Trim());
            }
        }

        return builder.ToString();
    }

    private static string ExtractXlsxText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var builder = new StringBuilder();
        foreach (var worksheet in workbook.Worksheets)
        {
            builder.AppendLine($"### HOJA: {worksheet.Name}");
            var range = worksheet.RangeUsed();
            if (range is null)
                continue;

            foreach (var row in range.RowsUsed())
            {
                builder.AppendLine(string.Join('\t', row.Cells(range.RangeAddress.FirstAddress.ColumnNumber, range.RangeAddress.LastAddress.ColumnNumber)
                    .Select(static cell => cell.GetFormattedString().Trim())));
            }
        }

        return builder.ToString();
    }

    private static string DecodeText(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    private static string ExtractResponsesText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("La respuesta de Azure OpenAI no contiene output.");

        var builder = new StringBuilder();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    builder.Append(text.GetString());
            }
        }

        var result = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Azure OpenAI no devolvio contenido de extracción.");
        return result;
    }

    private static string ExtractJsonObject(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = value.IndexOf('\n');
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && lastFence > firstLine)
                value = value[(firstLine + 1)..lastFence].Trim();
        }

        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Azure OpenAI no devolvio un objeto JSON.");
        var json = value[start..(end + 1)];
        using var _ = JsonDocument.Parse(json);
        return json;
    }

    private static string BuildRutPrompt() => """
        Eres un extractor jurídico colombiano especializado en RUT de la DIAN. Tu tarea es transcribir y normalizar,
        nunca inventar. Si un dato no aparece, usa cadena vacía o lista vacía. Conserva nombres y direcciones tal como
        aparecen, separa el dígito de verificación del NIT y no confundas representante legal con contador o contacto.
        confidence debe estar entre 0 y 1. Devuelve exclusivamente JSON con esta forma exacta:
        {
          "legalName":"", "nit":"", "verificationDigit":"", "legalForm":"",
          "mainAddress":"", "notificationAddress":"", "city":"", "department":"",
          "email":"", "phone":"", "legalRepresentativeName":"", "legalRepresentativeId":"",
          "taxResponsibilities":[], "economicActivities":[], "sourceNotes":[], "confidence":0
        }
        En sourceNotes registra inconsistencias, páginas ilegibles o campos inferidos; no incluyas texto fuera del JSON.
        """;

    private static string BuildOfferPrompt() => """
        Eres un analista comercial de contratos Copiers en Colombia. Convierte cualquier oferta aprobada de impresión
        a un esquema uniforme para generar un contrato marco y órdenes de servicio. Identifica todas las líneas de
        equipos aunque la tabla cambie de formato. No inventes precios, cantidades, volúmenes, ubicaciones o servicios.
        Los importes deben ser números sin símbolos ni separadores de miles. Si IVA está incluido, vatIncluded=true;
        vatPercent es el porcentaje identificado o 19 cuando la oferta indica "más IVA" sin especificarlo. Distingue
        impresiones de digitalizaciones y canon mensual de precio por clic. Genera valueAddedServices únicamente con
        servicios visibles en la oferta. Usa warnings para ambigüedades. confidence debe estar entre 0 y 1.
        Devuelve exclusivamente JSON con esta forma exacta:
        {
          "contractType":"Copiers", "currency":"COP", "durationMonths":12, "paymentDays":30,
          "nonRenewalNoticeDays":30, "deliveryBusinessDays":2,
          "startCondition":"", "executionAddress":"", "billingEmail":"", "clientContact":"",
          "recommendedTitle":"", "summary":"",
          "equipmentLines":[{
            "equipmentOrService":"", "quantity":1, "brand":"", "model":"", "colorMode":"",
            "includedPrints":0, "includedScans":0, "monthlyFee":0, "additionalClickPrice":0,
            "vatPercent":19, "vatIncluded":false, "notes":""
          }],
          "valueAddedServices":[{"description":"", "scope":"", "frequency":"", "deliveryMethod":""}],
          "specialConditions":[], "warnings":[], "confidence":0
        }
        No incluyas Markdown ni texto fuera del JSON.
        """;

    private static void NormalizeRut(ContractRutExtractionDto value)
    {
        value.LegalName = (value.LegalName ?? "").Trim();
        value.Nit = NormalizeNit(value.Nit ?? "");
        value.VerificationDigit = (value.VerificationDigit ?? "").Trim().TrimStart('-');
        value.LegalForm = (value.LegalForm ?? "").Trim();
        value.MainAddress = (value.MainAddress ?? "").Trim();
        value.NotificationAddress = (value.NotificationAddress ?? "").Trim();
        value.City = (value.City ?? "").Trim();
        value.Department = (value.Department ?? "").Trim();
        value.Email = (value.Email ?? "").Trim();
        value.Phone = (value.Phone ?? "").Trim();
        value.LegalRepresentativeName = (value.LegalRepresentativeName ?? "").Trim();
        value.LegalRepresentativeId = (value.LegalRepresentativeId ?? "").Trim();
        value.TaxResponsibilities ??= Array.Empty<string>();
        value.EconomicActivities ??= Array.Empty<string>();
        value.SourceNotes ??= Array.Empty<string>();
        value.Confidence = Math.Clamp(value.Confidence, 0m, 1m);
    }

    private static void NormalizeOffer(ContractOfferExtractionDto value)
    {
        value.ContractType = "Copiers";
        value.Currency = string.IsNullOrWhiteSpace(value.Currency) ? "COP" : value.Currency.Trim().ToUpperInvariant();
        value.DurationMonths = value.DurationMonths <= 0 ? 12 : Math.Clamp(value.DurationMonths, 1, 120);
        value.PaymentDays = value.PaymentDays < 0 ? 30 : Math.Clamp(value.PaymentDays, 0, 365);
        value.NonRenewalNoticeDays = value.NonRenewalNoticeDays < 0 ? 30 : Math.Clamp(value.NonRenewalNoticeDays, 0, 365);
        value.DeliveryBusinessDays = value.DeliveryBusinessDays <= 0 ? 2 : Math.Clamp(value.DeliveryBusinessDays, 1, 180);
        value.StartCondition = (value.StartCondition ?? "").Trim();
        value.ExecutionAddress = (value.ExecutionAddress ?? "").Trim();
        value.BillingEmail = (value.BillingEmail ?? "").Trim();
        value.ClientContact = (value.ClientContact ?? "").Trim();
        value.RecommendedTitle = (value.RecommendedTitle ?? "").Trim();
        value.Summary = (value.Summary ?? "").Trim();
        value.EquipmentLines ??= Array.Empty<ContractEquipmentLineDto>();
        value.ValueAddedServices ??= Array.Empty<ContractValueAddedLineDto>();
        value.SpecialConditions ??= Array.Empty<string>();
        value.Warnings ??= Array.Empty<string>();
        value.Confidence = Math.Clamp(value.Confidence, 0m, 1m);

        foreach (var line in value.EquipmentLines)
        {
            line.EquipmentOrService = (line.EquipmentOrService ?? "").Trim();
            line.Brand = (line.Brand ?? "").Trim();
            line.Model = (line.Model ?? "").Trim();
            line.ColorMode = (line.ColorMode ?? "").Trim();
            line.Notes = (line.Notes ?? "").Trim();
            line.Quantity = Math.Max(1, line.Quantity);
            line.IncludedPrints = Math.Max(0, line.IncludedPrints);
            line.IncludedScans = Math.Max(0, line.IncludedScans);
            line.MonthlyFee = Math.Max(0, line.MonthlyFee);
            line.AdditionalClickPrice = Math.Max(0, line.AdditionalClickPrice);
            line.VatPercent = line.VatPercent <= 0 ? 19m : Math.Clamp(line.VatPercent, 0m, 100m);
        }

        foreach (var line in value.ValueAddedServices)
        {
            line.Description = (line.Description ?? "").Trim();
            line.Scope = (line.Scope ?? "").Trim();
            line.Frequency = (line.Frequency ?? "").Trim();
            line.DeliveryMethod = (line.DeliveryMethod ?? "").Trim();
        }
    }

    private static string NormalizeNit(string value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static string NormalizeContentType(string contentType, string extension)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains('/', StringComparison.Ordinal))
            return contentType.Split(';', 2)[0].Trim();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "documento" : safe;
    }

    private static string LimitText(string value) =>
        value.Length <= MaxExtractedCharacters ? value : value[..MaxExtractedCharacters];

    private static string TrimError(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value[..Math.Min(value.Length, 1500)];

    private static void ValidateFile(string fileName, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El archivo esta vacío.");
        if (content.Length > MaxInputBytes)
            throw new InvalidOperationException("El archivo supera el máximo de 25 MB para análisis con IA.");
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            throw new InvalidOperationException("El archivo debe tener una extensión reconocible.");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException("AzureOpenAI:Endpoint no esta configurado.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("AzureOpenAI:ApiKey no esta configurado.");
        if (string.IsNullOrWhiteSpace(_options.DeploymentName))
            throw new InvalidOperationException("AzureOpenAI:DeploymentName no esta configurado.");
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string FlattenString(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetRawText();
            case JsonValueKind.Array:
                return string.Join(", ", element.EnumerateArray()
                    .Select(FlattenString)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            case JsonValueKind.Object:
            {
                string[] preferredNames = ["value", "text", "name", "label", "code", "description", "address"];
                var preferred = preferredNames
                    .Select(name => element.TryGetProperty(name, out var property) ? FlattenString(property) : "")
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (preferred.Length > 0)
                    return string.Join(" - ", preferred);

                return string.Join(" - ", element.EnumerateObject()
                    .Select(static property => FlattenString(property.Value))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
            default:
                return "";
        }
    }

    private sealed class LenientStringJsonConverter : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString() ?? "";
            if (reader.TokenType == JsonTokenType.Null)
                return "";

            using var document = JsonDocument.ParseValue(ref reader);
            return FlattenString(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value ?? "");
    }

    private sealed class LenientStringListJsonConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return Array.Empty<string>();

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return ToStringList(root);

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                        return ToStringList(property.Value);
                }
            }

            var single = FlattenString(root).Trim();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : [single];
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value ?? Array.Empty<string>())
                writer.WriteStringValue(item ?? "");
            writer.WriteEndArray();
        }

        private static IReadOnlyList<string> ToStringList(JsonElement array) =>
            array.EnumerateArray()
                .Select(FlattenString)
                .Select(static value => value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private sealed record DocumentInput(object Payload, string Mode);
}
