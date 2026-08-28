using System.Net.Mail;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using Microsoft.AspNetCore.Http;

namespace CotizadorInterno.Web.Services.CopiersMtoV2;

internal static class CopiersMaintenanceV2Validation
{
    private const string SupportedEmailSizeFormulaVersion = "graph-json-base64-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ReservedLocationAnswerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "latitude",
        "longitude",
        "accuracymeters",
        "locationcapturedatutc",
        "locationsource",
        "geolocation",
        "gpslatitude",
        "gpslongitude",
        "ubicaciongps",
        "coordenadasgps",
        "serviceaddress",
        "serviceaddressinternal",
        "direccionservicio",
        "internalnotes",
        "notasinternas"
    };

    private static readonly IReadOnlyDictionary<string, PublicAnswerDefinition> PublicAnswerDefinitions =
        new Dictionary<string, PublicAnswerDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["servicereference"] = new("service_reference", "Orden o referencia", 1, false),
        ["equipmentreference"] = new("equipment_reference", "Referencia del equipo", 2, false),
        ["servicestartedat"] = new("service_started_at", "Inicio de visita", 3, true),
        ["onsitecontact"] = new("onsite_contact", "Persona que atendió", 4, true),
        ["onsiteemail"] = new("onsite_email", "Correo de contacto", 5, true),
        ["maintenancetype"] = new("maintenance_type", "Tipo de mantenimiento", 6, true),
        ["serviceresult"] = new("service_result", "Resultado del servicio", 7, true),
        ["reportedissue"] = new("reported_issue", "Solicitud o falla reportada", 8, true),
        ["technicaldiagnosis"] = new("technical_diagnosis", "Diagnóstico técnico", 9, true),
        ["partsused"] = new("parts_used", "Repuestos o materiales", 10, false),
        ["counters"] = new("counters", "Contadores", 11, false),
        ["recommendations"] = new("recommendations", "Recomendaciones", 12, false),
        ["signerdocument"] = new("signer_document", "Identificación de quien firma", 13, false)
    };

    private readonly record struct PublicAnswerDefinition(
        string Key,
        string Label,
        int SortOrder,
        bool Required);

    public static string Required(string? value, string code, string label, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new CopiersMaintenanceV2ValidationException(code, $"Debes indicar {label}.");
        if (normalized.Length > maxLength)
            throw new CopiersMaintenanceV2ValidationException(code, $"{label} supera {maxLength:N0} caracteres.");
        return normalized;
    }

    public static string Optional(string? value, string code, string label, int maxLength)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > maxLength)
            throw new CopiersMaintenanceV2ValidationException(code, $"{label} supera {maxLength:N0} caracteres.");
        return normalized;
    }

    public static string SubmissionKey(string? value, CopiersMaintenanceV2Options options)
    {
        var key = Required(value, "submission_key_required", "la clave de envio", options.SubmissionKeyMaxLength);
        if (key.Length < 16 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new CopiersMaintenanceV2ValidationException(
                "submission_key_invalid",
                "La clave de envio debe tener al menos 16 caracteres y usar solo letras, numeros, punto, guion, guion bajo o dos puntos.");
        }
        return key;
    }

    public static string FormVersion(string? value, CopiersMaintenanceV2Options options)
    {
        var version = Required(value, "form_version_required", "la version del formulario", options.FormVersionMaxLength);
        if (!options.AllowedFormVersions.Contains(version, StringComparer.Ordinal))
            throw new CopiersMaintenanceV2ValidationException("form_version_not_allowed", "La versión del formulario ya no está habilitada.");
        return version;
    }

    public static string RequiredGuid(string? value, string code, string label)
    {
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new CopiersMaintenanceV2ValidationException(code, $"{label} no es valido.");
        return parsed.ToString("D");
    }

    public static string OptionalGuid(string? value, string code, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return RequiredGuid(value, code, label);
    }

    public static CopiersMaintenanceV2ActorContext Actor(CopiersMaintenanceV2ActorContext? actor)
    {
        if (actor is null)
            throw new CopiersMaintenanceV2ValidationException("actor_required", "No fue posible identificar al tecnico autenticado.");

        var normalized = new CopiersMaintenanceV2ActorContext
        {
            SystemUserId = RequiredGuid(actor.SystemUserId, "actor_invalid", "El tecnico autenticado"),
            DisplayName = Required(actor.DisplayName, "actor_name_required", "el nombre del tecnico", 200),
            Email = Required(actor.Email, "actor_email_required", "el correo del tecnico", 320)
        };
        ValidateEmailAddress(normalized.Email, "actor_email_invalid", "El correo del técnico no es válido.");
        return normalized;
    }

    public static void ValidateServiceDate(DateOnly value)
    {
        if (value == default)
            throw new CopiersMaintenanceV2ValidationException("service_date_required", "Debes indicar la fecha del servicio.");
    }

    public static int MaintenanceType(int? value, CopiersMaintenanceV2DataverseOptions bindings)
    {
        if (value == bindings.MaintenanceTypeCorrectiveValue || value == bindings.MaintenanceTypePreventiveValue)
            return value.Value;
        throw new CopiersMaintenanceV2ValidationException("maintenance_type_invalid", "El tipo de mantenimiento seleccionado no es válido.");
    }

    public static string MaintenanceTypeLabel(int? value, CopiersMaintenanceV2DataverseOptions bindings) =>
        MaintenanceType(value, bindings) == bindings.MaintenanceTypeCorrectiveValue ? "Correctivo" : "Preventivo";

    public static void ValidateCustomerEmail(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new CopiersMaintenanceV2ValidationException("customer_email_required", "El cliente no tiene un correo valido para recibir el reporte.");

        ValidateEmailAddress(normalized, "customer_email_invalid", "El correo del cliente no es válido.");
    }

    public static IReadOnlyList<CopiersMaintenanceV2FormAnswerSnapshot> ParseAnswers(
        string? answersJson,
        CopiersMaintenanceV2Options options)
    {
        var raw = string.IsNullOrWhiteSpace(answersJson) ? "[]" : answersJson.Trim();
        if (Encoding.UTF8.GetByteCount(raw) > options.AnswersJsonMaxBytes)
            throw new CopiersMaintenanceV2ValidationException("answers_too_large", "Las respuestas del formulario superan el limite permitido.");

        List<CopiersMaintenanceV2FormAnswerInputDto>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<CopiersMaintenanceV2FormAnswerInputDto>>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CopiersMaintenanceV2ValidationException("answers_invalid_json", $"Las respuestas del formulario no son JSON valido: {ex.Message}");
        }

        parsed ??= new List<CopiersMaintenanceV2FormAnswerInputDto>();
        if (parsed.Count > options.MaxAnswerCount)
            throw new CopiersMaintenanceV2ValidationException("answers_count_exceeded", $"El formulario admite maximo {options.MaxAnswerCount:N0} respuestas.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CopiersMaintenanceV2FormAnswerSnapshot>(parsed.Count);
        foreach (var item in parsed)
        {
            var key = Required(item.Key, "answer_key_required", "la clave de cada respuesta", options.AnswerKeyMaxLength);
            var normalizedKey = NormalizeKey(key);
            if (ReservedLocationAnswerKeys.Contains(normalizedKey))
            {
                throw new CopiersMaintenanceV2ValidationException(
                    "location_answer_forbidden",
                    "La ubicacion solo se guarda internamente y no puede incluirse entre las respuestas imprimibles.");
            }
            if (!PublicAnswerDefinitions.TryGetValue(normalizedKey, out var definition))
            {
                throw new CopiersMaintenanceV2ValidationException(
                    "answer_key_not_allowed",
                    $"La respuesta {key} no pertenece a la versión aprobada del formulario.");
            }
            if (!keys.Add(normalizedKey))
                throw new CopiersMaintenanceV2ValidationException("answer_key_duplicate", $"La respuesta {key} esta repetida.");

            var value = Optional(item.Value, "answer_value_too_long", $"La respuesta {definition.Key}", options.AnswerValueMaxLength);
            if (definition.Required && string.IsNullOrWhiteSpace(value))
                throw new CopiersMaintenanceV2ValidationException("answer_required", $"Debes indicar {definition.Label.ToLowerInvariant()}.");
            if (definition.Key == "service_result")
                value = ServiceResultLabel(value);

            result.Add(new CopiersMaintenanceV2FormAnswerSnapshot
            {
                Key = definition.Key,
                Label = definition.Label,
                Value = value,
                SortOrder = definition.SortOrder
            });
        }

        var missingRequired = PublicAnswerDefinitions
            .Where(item => item.Value.Required && !keys.Contains(item.Key))
            .Select(item => item.Value.Label)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            throw new CopiersMaintenanceV2ValidationException(
                "answers_required_missing",
                $"Falta información obligatoria del formulario: {string.Join(", ", missingRequired)}.");
        }

        return result
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ServiceResultLabel(string value) => value switch
    {
        "completed" => "Finalizado y operativo",
        "completed-observation" => "Finalizado con observaciones",
        "pending-parts" => "Pendiente por repuesto",
        "pending-approval" => "Pendiente por autorización",
        "not-completed" => "No finalizado",
        _ => throw new CopiersMaintenanceV2ValidationException("service_result_invalid", "El resultado del servicio no es válido.")
    };

    private static void ValidateEmailAddress(string value, string code, string message)
    {
        try
        {
            var parsed = new MailAddress(value);
            if (!string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new CopiersMaintenanceV2ValidationException(code, message);
        }
    }

    public static CopiersMaintenanceV2InternalLocationData? Location(
        CopiersMaintenanceV2FinalizeMultipartRequestDto request,
        DateTimeOffset nowUtc,
        CopiersMaintenanceV2Options options)
    {
        var hasAny = request.Latitude.HasValue
            || request.Longitude.HasValue
            || request.AccuracyMeters.HasValue
            || request.LocationCapturedAtUtc.HasValue;
        if (!hasAny && !options.RequireLocation)
            return null;
        if (!request.Latitude.HasValue || !request.Longitude.HasValue || !request.AccuracyMeters.HasValue || !request.LocationCapturedAtUtc.HasValue)
            throw new CopiersMaintenanceV2ValidationException("location_required", "Debes permitir la ubicacion para finalizar el reporte.");

        var latitude = request.Latitude.Value;
        var longitude = request.Longitude.Value;
        var accuracy = request.AccuracyMeters.Value;
        if (!double.IsFinite(latitude) || latitude is < -90d or > 90d)
            throw new CopiersMaintenanceV2ValidationException("latitude_invalid", "La latitud capturada no es valida.");
        if (!double.IsFinite(longitude) || longitude is < -180d or > 180d)
            throw new CopiersMaintenanceV2ValidationException("longitude_invalid", "La longitud capturada no es valida.");
        if (!double.IsFinite(accuracy) || accuracy < 0d || accuracy > options.MaxLocationAccuracyMeters)
        {
            throw new CopiersMaintenanceV2ValidationException(
                "location_accuracy_invalid",
                $"La precision de ubicacion debe ser de {options.MaxLocationAccuracyMeters:N0} metros o mejor.");
        }

        var capturedAtUtc = request.LocationCapturedAtUtc.Value.ToUniversalTime();
        if (capturedAtUtc > nowUtc + options.MaxDeviceClockFutureSkew)
            throw new CopiersMaintenanceV2ValidationException("location_time_future", "La hora de ubicacion esta demasiado adelantada.");
        if (capturedAtUtc < nowUtc - options.MaxLocationAge)
            throw new CopiersMaintenanceV2ValidationException("location_stale", "La ubicacion expiro; vuelve a capturarla antes de enviar.");

        return new CopiersMaintenanceV2InternalLocationData
        {
            Latitude = NormalizeLocationDecimal(latitude),
            Longitude = NormalizeLocationDecimal(longitude),
            AccuracyMeters = NormalizeLocationDecimal(accuracy),
            CapturedAtUtc = capturedAtUtc,
            Source = Optional(request.LocationSource, "location_source_too_long", "La fuente de ubicacion", 80)
        };
    }

    private static double NormalizeLocationDecimal(double value)
    {
        var normalized = Math.Round(value, 7, MidpointRounding.AwayFromZero);
        return normalized == 0d ? 0d : normalized;
    }

    public static DateTimeOffset DeviceSignedAt(
        DateTimeOffset? value,
        DateTimeOffset nowUtc,
        CopiersMaintenanceV2Options options)
    {
        if (!value.HasValue)
            throw new CopiersMaintenanceV2ValidationException("signed_at_required", "No se recibio la hora de la firma.");
        var utc = value.Value.ToUniversalTime();
        if (utc > nowUtc + options.MaxDeviceClockFutureSkew)
            throw new CopiersMaintenanceV2ValidationException("signed_at_future", "La hora de la firma esta demasiado adelantada.");
        if (utc < nowUtc - TimeSpan.FromDays(1))
            throw new CopiersMaintenanceV2ValidationException("signed_at_stale", "La firma expiro; solicita al cliente firmar nuevamente.");
        return utc;
    }

    public static async Task<CopiersMaintenanceV2StoredFile> ReadSignatureAsync(
        IFormFile? file,
        CopiersMaintenanceV2Options options,
        CancellationToken ct)
    {
        if (file is null)
            throw new CopiersMaintenanceV2ValidationException("signature_required", "El cliente debe firmar antes de enviar.");
        var stored = await ReadFileAsync(file, options.MaxSignatureBytes, options.AllowedSignatureExtensions, "signature", ct);
        var sanitized = ReencodeImage(stored.Content, ".jpg", options, isSignature: true);
        ValidateSignatureInk(sanitized, options);
        return ToStoredFile(Path.ChangeExtension(stored.FileName, ".jpg"), "image/jpeg", sanitized);
    }

    public static async Task<IReadOnlyList<CopiersMaintenanceV2StoredFile>> ReadAttachmentsAsync(
        IReadOnlyList<IFormFile>? files,
        CopiersMaintenanceV2Options options,
        CancellationToken ct)
    {
        var submitted = files ?? Array.Empty<IFormFile>();
        if (submitted.Any(item => item is null || item.Length <= 0))
            throw new CopiersMaintenanceV2ValidationException("attachment_empty", "No se permiten adjuntos vacíos.");
        var nonEmpty = submitted.ToList();
        if (nonEmpty.Count > options.MaxAttachmentCount)
            throw new CopiersMaintenanceV2ValidationException("attachment_count_exceeded", $"Puedes adjuntar maximo {options.MaxAttachmentCount:N0} archivos.");

        long submittedTotal = 0;
        long securedTotal = 0;
        var result = new List<CopiersMaintenanceV2StoredFile>(nonEmpty.Count);
        foreach (var file in nonEmpty)
        {
            var stored = await ReadFileAsync(file, options.MaxAttachmentBytes, options.AllowedAttachmentExtensions, "attachment", ct);
            submittedTotal = checked(submittedTotal + stored.Size);
            if (submittedTotal > options.MaxTotalAttachmentBytes)
                throw new CopiersMaintenanceV2ValidationException("attachment_total_exceeded", "Los adjuntos superan el limite total permitido.");

            // Raw uploads are never persisted. Decode/re-encode first so even the
            // internal copy is an inert pixel-only image without embedded metadata.
            var extension = Path.GetExtension(stored.FileName).ToLowerInvariant();
            var outputExtension = extension == ".png" ? ".png" : ".jpg";
            var securedContent = ReencodeImage(stored.Content, outputExtension, options, isSignature: false);
            if (securedContent.LongLength > options.MaxAttachmentBytes)
                throw new CopiersMaintenanceV2ValidationException("attachment_too_large", "Una imagen saneada supera el límite permitido.");
            securedTotal = checked(securedTotal + securedContent.LongLength);
            if (securedTotal > options.MaxTotalAttachmentBytes)
                throw new CopiersMaintenanceV2ValidationException("attachment_total_exceeded", "Los adjuntos saneados superan el límite total permitido.");
            result.Add(ToStoredFile(
                stored.FileName,
                outputExtension == ".png" ? "image/png" : "image/jpeg",
                securedContent));
        }
        return result;
    }

    public static IReadOnlyList<CopiersMaintenanceV2StoredFile> BuildCustomerSafeAttachments(
        IReadOnlyList<CopiersMaintenanceV2StoredFile> originals,
        CopiersMaintenanceV2Options options)
    {
        ArgumentNullException.ThrowIfNull(originals);
        var result = new List<CopiersMaintenanceV2StoredFile>(originals.Count);
        long total = 0;
        for (var index = 0; index < originals.Count; index++)
        {
            var original = originals[index];
            var extension = Path.GetExtension(original.FileName).ToLowerInvariant();
            if (extension is not ".jpg" and not ".jpeg" and not ".png")
            {
                throw new CopiersMaintenanceV2ValidationException(
                    "attachment_customer_format_blocked",
                    "En el piloto solo se pueden enviar imágenes JPG o PNG; PDF y Word requieren conversión segura.");
            }

            var outputExtension = extension == ".png" ? ".png" : ".jpg";
            // The internal copy was already reconstructed during upload. The
            // customer derivative receives only a generic name and a new buffer.
            var content = original.Content.ToArray();
            if (content.LongLength > options.MaxAttachmentBytes)
                throw new CopiersMaintenanceV2ValidationException("attachment_too_large", "Una imagen saneada supera el límite permitido.");
            total = checked(total + content.LongLength);
            if (total > options.MaxTotalAttachmentBytes)
                throw new CopiersMaintenanceV2ValidationException("attachment_total_exceeded", "Los adjuntos saneados superan el límite total permitido.");
            result.Add(ToStoredFile(
                $"adjunto-{index + 1:000}{outputExtension}",
                outputExtension == ".png" ? "image/png" : "image/jpeg",
                content));
        }
        return result;
    }

    public static void ValidateEmailPackageSize(
        CopiersMaintenanceV2StoredFile signedReport,
        IReadOnlyList<CopiersMaintenanceV2StoredFile> customerAttachments,
        CopiersMaintenanceV2EmailOutboxSnapshot emailOutbox,
        CopiersMaintenanceV2Options options)
    {
        if (!string.Equals(
                options.EmailSizeFormulaVersion,
                SupportedEmailSizeFormulaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"La fórmula de tamaño de correo '{options.EmailSizeFormulaVersion}' no está soportada.");
        }

        var files = customerAttachments.Prepend(signedReport);
        long estimated = checked(
            64 * 1024L
            + Encoding.UTF8.GetByteCount(SupportedEmailSizeFormulaVersion)
            + Encoding.UTF8.GetByteCount(emailOutbox.Subject)
            + Encoding.UTF8.GetByteCount(emailOutbox.HtmlBody));
        foreach (var recipient in emailOutbox.To)
            estimated = checked(estimated + Encoding.UTF8.GetByteCount(recipient) + 256L);
        foreach (var file in files)
        {
            var base64Bytes = checked(4 * ((file.Size + 2) / 3));
            estimated = checked(
                estimated
                + base64Bytes
                + Encoding.UTF8.GetByteCount(file.FileName)
                + Encoding.UTF8.GetByteCount(file.ContentType)
                + 1024L);
        }
        if (estimated > options.MaxEmailEncodedBytes)
        {
            throw new CopiersMaintenanceV2ValidationException(
                "email_package_too_large",
                "El PDF y los adjuntos superan el tamaño seguro del correo. Reduce la cantidad o resolución de las imágenes.");
        }
    }

    public static CopiersMaintenanceV2StoredFile ValidatePdf(
        CopiersMaintenanceV2RenderedPdf rendered,
        CopiersMaintenanceV2Options options)
    {
        if (rendered is null || rendered.Content.Length == 0)
            throw new CopiersMaintenanceV2ValidationException("pdf_empty", "El generador no devolvio el PDF firmado.");
        if (rendered.Content.LongLength > options.MaxGeneratedPdfBytes)
            throw new CopiersMaintenanceV2ValidationException("pdf_too_large", "El PDF firmado supera el limite permitido.");
        if (!HasPrefix(rendered.Content, "%PDF-"u8))
            throw new CopiersMaintenanceV2ValidationException("pdf_invalid", "El documento generado no es un PDF valido.");

        var fileName = SanitizeFileName(rendered.FileName, "reporte-servicio-firmado.pdf");
        if (!string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            fileName = Path.ChangeExtension(fileName, ".pdf");
        return ToStoredFile(fileName, "application/pdf", rendered.Content);
    }

    public static void VerifyHash(CopiersMaintenanceV2StoredFile file)
    {
        var actual = Convert.ToHexString(SHA256.HashData(file.Content));
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CopiersMaintenanceV2ValidationException("file_hash_mismatch", "El hash de un archivo no coincide con su contenido.");
    }

    private static async Task<CopiersMaintenanceV2StoredFile> ReadFileAsync(
        IFormFile file,
        long maxBytes,
        IEnumerable<string> allowedExtensions,
        string kind,
        CancellationToken ct)
    {
        var displayName = kind == "attachment" ? "El adjunto" : "El archivo de firma";
        if (file.Length <= 0)
            throw new CopiersMaintenanceV2ValidationException($"{kind}_empty", $"{displayName} esta vacio.");
        if (file.Length > maxBytes)
            throw new CopiersMaintenanceV2ValidationException($"{kind}_too_large", $"{displayName} supera el limite permitido.");

        var fileName = SanitizeFileName(file.FileName, kind);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = new HashSet<string>(allowedExtensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        if (!allowed.Contains(extension))
            throw new CopiersMaintenanceV2ValidationException($"{kind}_extension_invalid", $"El tipo de archivo {extension} no esta permitido.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream(file.Length > int.MaxValue ? 0 : (int)file.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
                break;
            if (output.Length + read > maxBytes)
                throw new CopiersMaintenanceV2ValidationException($"{kind}_too_large", $"{displayName} supera el limite permitido.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        var content = output.ToArray();
        if (!MatchesFileSignature(extension, content))
            throw new CopiersMaintenanceV2ValidationException($"{kind}_content_invalid", $"{displayName} no coincide con su extension.");
        return ToStoredFile(fileName, ResolveContentType(extension), content);
    }

    private static CopiersMaintenanceV2StoredFile ToStoredFile(string fileName, string contentType, byte[] content) =>
        new()
        {
            FileName = fileName,
            ContentType = contentType,
            Size = content.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(content)),
            Content = content
        };

    private static string SanitizeFileName(string? value, string fallback)
    {
        var safe = Path.GetFileName(value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(safe))
            safe = fallback;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '-');
        return safe.Length <= 180 ? safe : safe[..180];
    }

    private static string NormalizeExtension(string value) =>
        value.StartsWith(".", StringComparison.Ordinal) ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";

    private static bool MatchesFileSignature(string extension, byte[] content) => extension switch
    {
        ".pdf" => HasPrefix(content, "%PDF-"u8),
        ".png" => HasPrefix(content, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => HasPrefix(content, new byte[] { 0xFF, 0xD8, 0xFF }),
        ".doc" => HasPrefix(content, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
        ".docx" => HasPrefix(content, new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        _ => false
    };

    private static string ResolveContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    // System.Drawing is intentionally guarded at runtime. The current App Service
    // target is Windows; non-Windows hosts fail closed before any platform API call.
#pragma warning disable CA1416
    private static byte[] ReencodeImage(
        byte[] content,
        string outputExtension,
        CopiersMaintenanceV2Options options,
        bool isSignature)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new CopiersMaintenanceV2ValidationException(
                "image_sanitizer_platform_unavailable",
                "El saneamiento seguro de imágenes no está disponible en este servidor.");
        }

        try
        {
            using var input = new MemoryStream(content, writable: false);
            using var source = Image.FromStream(input, useEmbeddedColorManagement: true, validateImageData: true);
            var isJpeg = source.RawFormat.Guid == ImageFormat.Jpeg.Guid;
            var isPng = source.RawFormat.Guid == ImageFormat.Png.Guid;
            if ((!isJpeg && !isPng) || (isSignature && !isJpeg))
                throw new CopiersMaintenanceV2ValidationException("image_content_invalid", "La imagen no tiene un formato JPG o PNG válido.");

            ApplyExifOrientation(source);
            var maxDimension = isSignature ? options.MaxSignatureDimensionPixels : options.MaxAttachmentImageDimensionPixels;
            var maxPixels = isSignature
                ? checked((long)options.MaxSignatureDimensionPixels * options.MaxSignatureDimensionPixels)
                : options.MaxAttachmentImagePixels;
            if (source.Width <= 0
                || source.Height <= 0
                || source.Width > maxDimension
                || source.Height > maxDimension
                || checked((long)source.Width * source.Height) > maxPixels)
            {
                throw new CopiersMaintenanceV2ValidationException("image_dimensions_invalid", "Las dimensiones de la imagen no son seguras.");
            }
            if (isSignature
                && (source.Width < options.MinSignatureWidthPixels || source.Height < options.MinSignatureHeightPixels))
            {
                throw new CopiersMaintenanceV2ValidationException("signature_dimensions_invalid", "Las dimensiones de la firma no son válidas.");
            }

            var pixelFormat = outputExtension == ".png" ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;
            using var clean = new Bitmap(source.Width, source.Height, pixelFormat);
            using (var graphics = Graphics.FromImage(clean))
            {
                graphics.Clear(outputExtension == ".png" ? Color.Transparent : Color.White);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, clean.Width, clean.Height));
            }

            using var output = new MemoryStream();
            if (outputExtension == ".png")
            {
                clean.Save(output, ImageFormat.Png);
            }
            else
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
                using var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                clean.Save(output, codec, encoderParameters);
            }
            return output.ToArray();
        }
        catch (CopiersMaintenanceV2ValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new CopiersMaintenanceV2ValidationException("image_decode_failed", "La imagen está dañada o no puede sanearse de forma segura.");
        }
    }

    private static void ApplyExifOrientation(Image image)
    {
        const int orientationPropertyId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationPropertyId))
            return;
        var property = image.GetPropertyItem(orientationPropertyId);
        if (property?.Value is not { Length: >= 2 } bytes)
            return;
        var orientation = BitConverter.ToUInt16(bytes, 0);
        var rotateFlip = orientation switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };
        if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
            image.RotateFlip(rotateFlip);
    }

    private static void ValidateSignatureInk(byte[] content, CopiersMaintenanceV2Options options)
    {
        using var input = new MemoryStream(content, writable: false);
        using var image = new Bitmap(input);
        var sampleStep = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(((long)image.Width * image.Height) / 1_000_000d)));
        var inkPixels = 0;
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < image.Height; y += sampleStep)
        {
            for (var x = 0; x < image.Width; x += sampleStep)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R + pixel.G + pixel.B >= 720)
                    continue;
                inkPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        if (inkPixels < options.MinSignatureInkPixels
            || maxX - minX < options.MinSignatureWidthPixels / 2
            || maxY - minY < options.MinSignatureHeightPixels / 2)
        {
            throw new CopiersMaintenanceV2ValidationException("signature_ink_required", "La imagen de firma está vacía o no contiene trazos suficientes.");
        }
    }
#pragma warning restore CA1416

    private static bool HasPrefix(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix) =>
        content.Length >= prefix.Length && content[..prefix.Length].SequenceEqual(prefix);

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}

