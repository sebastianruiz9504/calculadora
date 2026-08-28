using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CotizadorInterno.Web.Models.MesaAyuda;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.MesaAyuda;

internal sealed class GraphMesaAyudaMailClient : IMesaAyudaGraphMailClient
{
    private const string GraphHost = "graph.microsoft.com";
    private const int MaxContinuationLinkLength = 32768;
    private const int MaxRetries = 3;
    private static readonly Uri GraphBaseUri = new(
        "https://graph.microsoft.com/v1.0/",
        UriKind.Absolute);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];
    private const string SelectedFields =
        "id,internetMessageId,conversationId,subject,from,sender,toRecipients," +
        "ccRecipients,receivedDateTime,sentDateTime,body,bodyPreview,importance," +
        "hasAttachments,isRead";

    private readonly HttpClient _httpClient;
    private readonly IMesaAyudaGraphTokenProvider _tokenProvider;
    private readonly MesaAyudaMailCollectionOptions _options;
    private readonly ILogger<GraphMesaAyudaMailClient> _logger;

    public GraphMesaAyudaMailClient(
        HttpClient httpClient,
        IMesaAyudaGraphTokenProvider tokenProvider,
        IOptions<MesaAyudaMailCollectionOptions> options,
        ILogger<GraphMesaAyudaMailClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MesaAyudaMailDeltaPage> GetDeltaPageAsync(
        MesaAyudaMailDeltaRequest request,
        CancellationToken ct = default)
    {
        var mailbox = NormalizeMailbox(request.Mailbox);
        var requestUri = string.IsNullOrWhiteSpace(request.ContinuationLink)
            ? BuildInitialDeltaUri(
                mailbox,
                request.InitialReceivedAfterUtc,
                _options.PageSize)
            : ValidateContinuationUri(mailbox, request.ContinuationLink);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var graphRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var token = await _tokenProvider.GetTokenAsync(ct);
            graphRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Token);
            graphRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            graphRequest.Headers.TryAddWithoutValidation(
                "Prefer",
                "IdType=\"ImmutableId\"");

            using var response = await _httpClient.SendAsync(
                graphRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (response.IsSuccessStatusCode)
                return await ReadPageAsync(mailbox, response.Content, ct);

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                throw new MesaAyudaDeltaTokenExpiredException(
                    $"Microsoft Graph rechazó el cursor delta de {mailbox}. " +
                    "Se requiere reinicialización supervisada.");
            }

            if (attempt < MaxRetries
                && RetryableStatusCodes.Contains(response.StatusCode))
            {
                var delay = ResolveRetryDelay(response, attempt);
                _logger.LogWarning(
                    "Microsoft Graph respondió {StatusCode} leyendo correo de {Mailbox}; reintento {Attempt}/{MaxRetries} en {DelaySeconds}s.",
                    (int)response.StatusCode,
                    mailbox,
                    attempt,
                    MaxRetries,
                    delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            throw new HttpRequestException(
                $"Microsoft Graph rechazó la lectura delta de {mailbox} " +
                $"con HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        throw new InvalidOperationException(
            "Se agotaron los reintentos de Microsoft Graph.");
    }

    internal static Uri BuildInitialDeltaUri(
        string mailbox,
        DateTimeOffset receivedAfterUtc,
        int pageSize)
    {
        var normalizedMailbox = NormalizeMailbox(mailbox);
        var timestamp = receivedAfterUtc
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var filter = Uri.EscapeDataString($"receivedDateTime ge {timestamp}");
        var pathMailbox = Uri.EscapeDataString(normalizedMailbox);
        return new Uri(
            GraphBaseUri,
            $"users/{pathMailbox}/mailFolders/inbox/messages/delta" +
            $"?changeType=created&$select={SelectedFields}" +
            $"&$filter={filter}&$top={pageSize}");
    }

    internal static Uri ValidateContinuationUri(
        string mailbox,
        string continuationLink)
    {
        if (continuationLink.Length > MaxContinuationLinkLength
            || !Uri.TryCreate(continuationLink, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, GraphHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "El cursor delta de Microsoft Graph no pertenece al endpoint permitido.");
        }

        var normalizedMailbox = NormalizeMailbox(mailbox);
        var expectedPaths = new[]
        {
            $"/v1.0/users/{normalizedMailbox}/mailFolders/inbox/messages/delta",
            $"/v1.0/users/{normalizedMailbox}/mailFolders('inbox')/messages/delta",
            $"/v1.0/users('{normalizedMailbox}')/mailFolders('inbox')/messages/delta"
        };
        var actualPath = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!expectedPaths.Any(
                expectedPath => string.Equals(
                    actualPath,
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "El cursor delta no corresponde al buzón y carpeta Inbox configurados.");
        }

        return uri;
    }

    private async Task<MesaAyudaMailDeltaPage> ReadPageAsync(
        string mailbox,
        HttpContent content,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength > _options.MaxResponseBytes)
        {
            throw new InvalidOperationException(
                "Microsoft Graph devolvió una página mayor al límite configurado.");
        }

        await content.LoadIntoBufferAsync(_options.MaxResponseBytes, ct);
        var envelope = await content.ReadFromJsonAsync<GraphMailDeltaEnvelope>(
            JsonOptions,
            ct)
            ?? throw new InvalidOperationException(
                "Microsoft Graph devolvió una página delta vacía.");

        var changes = envelope.Value
            .Select(message => MapChange(mailbox, message))
            .ToArray();
        return new MesaAyudaMailDeltaPage(
            changes,
            NormalizeAndValidateLink(mailbox, envelope.NextLink),
            NormalizeAndValidateLink(mailbox, envelope.DeltaLink));
    }

    private static MesaAyudaMailDeltaChange MapChange(
        string mailbox,
        GraphMailMessage source)
    {
        if (source.Removed.HasValue)
            return new MesaAyudaMailDeltaChange(true, null);

        var graphMessageId = source.Id?.Trim() ?? "";
        if (graphMessageId.Length == 0)
            return new MesaAyudaMailDeltaChange(false, null);

        return new MesaAyudaMailDeltaChange(
            false,
            new MesaAyudaCollectedMail
            {
                IdempotencyKey = MesaAyudaCollectedMail.CreateIdempotencyKey(
                    mailbox,
                    graphMessageId),
                Mailbox = mailbox,
                GraphMessageId = graphMessageId,
                InternetMessageId = source.InternetMessageId?.Trim() ?? "",
                ConversationId = source.ConversationId?.Trim() ?? "",
                ChangeTag = source.ChangeTag?.Trim() ?? "",
                Subject = source.Subject?.Trim() ?? "",
                From = MapParty(source.From),
                Sender = MapParty(source.Sender),
                ToRecipients = source.ToRecipients
                    .Select(MapParty)
                    .Where(party => party is not null)
                    .Select(party => party!)
                    .ToArray(),
                CcRecipients = source.CcRecipients
                    .Select(MapParty)
                    .Where(party => party is not null)
                    .Select(party => party!)
                    .ToArray(),
                ReceivedAtUtc = source.ReceivedAtUtc?.ToUniversalTime(),
                SentAtUtc = source.SentAtUtc?.ToUniversalTime(),
                BodyContentType = source.Body?.ContentType?.Trim() ?? "",
                Body = source.Body?.Content ?? "",
                BodyPreview = source.BodyPreview ?? "",
                Importance = source.Importance?.Trim() ?? "",
                HasAttachments = source.HasAttachments,
                IsRead = source.IsRead
            });
    }

    private static MesaAyudaMailParty? MapParty(GraphMailRecipient? recipient)
    {
        var address = recipient?.EmailAddress?.Address?.Trim() ?? "";
        return address.Length == 0
            ? null
            : new MesaAyudaMailParty(
                recipient?.EmailAddress?.Name?.Trim() ?? "",
                address);
    }

    private static string? NormalizeAndValidateLink(
        string mailbox,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return ValidateContinuationUri(mailbox, value.Trim()).AbsoluteUri;
    }

    private static string NormalizeMailbox(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("El buzón es obligatorio.", nameof(value));

        return normalized;
    }

    private static TimeSpan ResolveRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return ClampRetryDelay(delta);

        if (retryAfter?.Date is { } date)
            return ClampRetryDelay(date - DateTimeOffset.UtcNow);

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static TimeSpan ClampRetryDelay(TimeSpan value) =>
        value < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : value > TimeSpan.FromSeconds(60)
                ? TimeSpan.FromSeconds(60)
                : value;
}

public sealed class MesaAyudaDeltaTokenExpiredException : InvalidOperationException
{
    public MesaAyudaDeltaTokenExpiredException(string message)
        : base(message)
    {
    }
}
