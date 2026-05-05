using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Copiers;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Services;

public sealed class UserCalendarService : IUserCalendarService
{
    public const string CalendarWriteScope = "Calendars.ReadWrite";
    private const string BogotaGraphTimeZone = "SA Pacific Standard Time";
    private static readonly string[] GraphScopes = { CalendarWriteScope };
    private static readonly JsonSerializerOptions CalendarJsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly M365Options _options;

    public UserCalendarService(
        ITokenAcquisition tokenAcquisition,
        IHttpClientFactory httpClientFactory,
        IOptions<M365Options> options)
    {
        _tokenAcquisition = tokenAcquisition;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<CopiersPreventiveMaintenanceScheduleResultDto> SchedulePreventiveMaintenanceAsync(
        CopiersPreventiveMaintenanceScheduleRequestDto request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var clientName = (request.ClientName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clientName))
            throw new InvalidOperationException("Debes indicar el cliente del mantenimiento preventivo.");

        if (!DateOnly.TryParse(request.DateValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidOperationException("Debes seleccionar una fecha valida.");

        if (!TimeOnly.TryParse(request.TimeValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new InvalidOperationException("Debes seleccionar una hora valida.");

        var durationMinutes = Math.Clamp(request.DurationMinutes ?? 60, 15, 480);
        var start = date.ToDateTime(time);
        var end = start.AddMinutes(durationMinutes);
        var subject = $"Mantenimiento preventivo - {clientName}";
        var token = await _tokenAcquisition.GetAccessTokenForUserAsync(GraphScopes, user: user);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["subject"] = subject,
            ["body"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contentType"] = "Text",
                ["content"] = $"Cliente: {clientName}{Environment.NewLine}Mantenimiento preventivo programado desde Cotizador Interno."
            },
            ["start"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dateTime"] = start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["timeZone"] = BogotaGraphTimeZone
            },
            ["end"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dateTime"] = end.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["timeZone"] = BogotaGraphTimeZone
            },
            ["showAs"] = "busy",
            ["isReminderOn"] = true,
            ["reminderMinutesBeforeStart"] = 15
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, CalendarJsonOptions), Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, BuildGraphUri("/me/events"))
        {
            Content = content
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        requestMessage.Headers.TryAddWithoutValidation("Prefer", $"outlook.timezone=\"{BogotaGraphTimeZone}\"");

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(requestMessage, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Graph devolvio {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 900)}");

        using var doc = JsonDocument.Parse(body);
        return new CopiersPreventiveMaintenanceScheduleResultDto
        {
            Message = "Espacio reservado en tu calendario.",
            EventId = ReadString(doc.RootElement, "id"),
            WebLink = ReadString(doc.RootElement, "webLink")
        };
    }

    private Uri BuildGraphUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.GraphBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _options.GraphBaseUrl.TrimEnd('/');

        return new Uri($"{baseUrl}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static string ReadString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return "";

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : property.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }
}
