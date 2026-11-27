using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using Microsoft.Extensions.Caching.Memory;

namespace CotizadorInterno.Web.Services;

public sealed record CollectionSnapshot(IReadOnlyList<BillingInvoiceRowDto> Rows, DateTimeOffset RefreshedAt);
public interface IPortfolioCollectionsRepository
{
    Task<CollectionSnapshot> GetCollectionSnapshotAsync(bool refresh = false, string? clientId = null, CancellationToken ct = default);
    Task<CollectionContact> GetCollectionContactAsync(string clientId, string name, CancellationToken ct);
    Task SaveCollectionContactAsync(string clientId, string additional, CancellationToken ct);
    Task<CollectionHistoryPage> GetCollectionHistoryAsync(string invoiceId, int page, CancellationToken ct);
    Task<bool> TryCreateCollectionEventAsync(Guid id, string invoiceId, string clientId, string kind, string status, string author, string recipients, string subject, string message, IEnumerable<string> invoiceIds, CancellationToken ct);
    Task CompleteCollectionEventAsync(Guid id, string status, CancellationToken ct);
    Task<string?> GetCollectionEventStatusAsync(Guid id, CancellationToken ct);
    Task<IReadOnlySet<string>> GetAutomaticCollectionClaimsAsync(IEnumerable<string> invoiceIds, CancellationToken ct);
}

public sealed partial class DataverseService : IPortfolioCollectionsRepository
{
    private const string CollectionEvents = "/api/data/v9.2/cr07a_carteragestions";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CollectionGates = new();
    public async Task<CollectionSnapshot> GetCollectionSnapshotAsync(bool refresh = false, string? clientId = null, CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        // Never share delegated records with another identity or the automatic worker.
        var identity = user is null ? "app" : string.Join(":", user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value, user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value, user.Identity?.Name);
        var key = $"collections:v1:{_dataverseBaseUrl}:{identity}:{GetBogotaToday():yyyyMMdd}";
        if (clientId is not null) return await LoadCollectionSnapshotAsync(user, NormalizeGuid(clientId, nameof(clientId)), ct);
        if (!refresh && _memoryCache.TryGetValue<CollectionSnapshot>(key, out var cached)) return cached!;
        var gate = CollectionGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!refresh && _memoryCache.TryGetValue<CollectionSnapshot>(key, out cached)) return cached!;
            var snapshot = await LoadCollectionSnapshotAsync(user, null, ct);
            _memoryCache.Set(key, snapshot, TimeSpan.FromSeconds(60));
            return snapshot;
        }
        finally { gate.Release(); }
    }

    private async Task<CollectionSnapshot> LoadCollectionSnapshotAsync(ClaimsPrincipal? user, string? clientId, CancellationToken ct)
    {
        var metadata = new RhEntityMetadata { LogicalName = _dashboardBillingTableLogicalName, EntitySetName = _dashboardBillingTableSetName, PrimaryIdField = _dashboardBillingIdField, PrimaryNameField = _dashboardBillingPrimaryNameField };
        var filter = $"{_dashboardBillingPaymentDateField} eq null and ({_dashboardBillingPaymentValueField} eq null or {_dashboardBillingPaymentValueField} le 0) and {_dashboardBillingTotalField} gt 0";
        if (clientId is not null) filter += $" and {BuildDashboardLookupValuePropertyName(_dashboardBillingClientField)} eq {clientId}";
        var rows = (await QueryBillingRecordsAsync(metadata, filter, _dashboardBillingDueDateField, user, ct))
            .Where(r => PortfolioCollectionRules.IsVisible(r.InvoiceNumber)).ToList();
        // Fail closed: never collect an unreconciled balance if credit notes cannot be read.
        var notes = await LoadDashboardAcceptedCreditNotesAsync(ct);
        var index = BuildBillingCreditNoteMatchIndex(rows);
        foreach (var note in notes)
        {
            var row = FindBillingRowForCreditNote(note, index);
            if (row is null) continue;
            row.CreditNoteTotal = RoundCurrency(row.CreditNoteTotal + note.Total);
            row.CreditNoteVat = RoundCurrency(row.CreditNoteVat + note.Vat);
            row.CreditNoteCount++;
        }
        return new(BuildBillingInvoiceRows(rows.Where(r => r.IsPortfolioPending).ToList(), GetBogotaToday()), DateTimeOffset.UtcNow);
    }

    public async Task<CollectionContact> GetCollectionContactAsync(string clientId, string name, CancellationToken ct)
    {
        if (!Guid.TryParse(clientId, out var id)) throw new InvalidOperationException("La factura no tiene un cliente vinculado. Vincúlalo antes de enviar correos.");
        var json = await CallDataverseAppGetJsonAsync($"/api/data/v9.2/cr07a_clientes({id})?$select=cr07a_correoelectronico,cr07a_correofacturacion,cr07a_correocobranza", ct);
        using var doc = JsonDocument.Parse(json);
        var email = ReadString(doc.RootElement, "cr07a_correoelectronico");
        var billing = ReadString(doc.RootElement, "cr07a_correofacturacion");
        var collection = ReadString(doc.RootElement, "cr07a_correocobranza");
        return new(clientId, name, email, billing, collection, PortfolioCollectionRules.Emails(string.Join(";", email, billing, collection), strict: false));
    }

    public async Task SaveCollectionContactAsync(string clientId, string additional, CancellationToken ct)
    {
        var id = Guid.Parse(clientId);
        // Optimistic merge avoids losing recipients saved concurrently by another collector.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var json = await CallDataverseAppGetJsonAsync($"/api/data/v9.2/cr07a_clientes({id})?$select=cr07a_correocobranza", ct);
            using var doc = JsonDocument.Parse(json);
            var emails = string.Join(";", PortfolioCollectionRules.Emails(ReadString(doc.RootElement, "cr07a_correocobranza") + ";" + additional));
            using var content = JsonContent.Create(new Dictionary<string, object> { ["cr07a_correocobranza"] = emails });
            using var response = await CallDataverseAppResponseAsync($"/api/data/v9.2/cr07a_clientes({id})", "PATCH", ct, content, r => r.Headers.TryAddWithoutValidation("If-Match", ReadString(doc.RootElement, "@odata.etag")));
            if (response.IsSuccessStatusCode) return;
            if (response.StatusCode != HttpStatusCode.PreconditionFailed) throw new InvalidOperationException("No fue posible guardar el correo de cobranza.");
        }
        throw new InvalidOperationException("El cliente fue modificado simultáneamente. Intenta guardar de nuevo.");
    }

    public async Task<CollectionHistoryPage> GetCollectionHistoryAsync(string invoiceId, int page, CancellationToken ct)
    {
        var id = Guid.Parse(invoiceId).ToString("D");
        page = Math.Clamp(page, 1, 1000);
        // FetchXML simple paging is bounded to a single invoice's history, 30 messages at a time.
        var xml = $"<fetch count='30' page='{page}'><entity name='cr07a_carteragestion'><all-attributes/><order attribute='createdon' descending='true'/><order attribute='cr07a_carteragestionid'/><filter type='or'><condition attribute='cr07a_factura' operator='eq' value='{id}'/><condition attribute='cr07a_facturas' operator='like' value='%{id}%'/></filter></entity></fetch>";
        var json = await CallDataverseAppGetJsonAsync($"{CollectionEvents}?fetchXml={Uri.EscapeDataString(xml)}", ct, r => r.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"Microsoft.Dynamics.CRM.morerecords\""));
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("value").EnumerateArray().Select(e => new CollectionHistory(
            ReadString(e, "cr07a_carteragestionid"), ReadString(e, "cr07a_tipo"), ReadString(e, "cr07a_estado"), ReadString(e, "cr07a_autor"), ReadString(e, "cr07a_destinatarios"), ReadString(e, "cr07a_asunto"), ReadString(e, "cr07a_mensaje"),
            DateTimeOffset.Parse(ReadString(e, "createdon")), DateTimeOffset.TryParse(ReadString(e, "cr07a_enviadoel"), out var sent) ? sent : null)).ToArray();
        var more = doc.RootElement.TryGetProperty("@Microsoft.Dynamics.CRM.morerecords", out var m) ? m.GetBoolean() : items.Length == 30;
        return new(items, more, page);
    }

    public async Task<bool> TryCreateCollectionEventAsync(Guid id, string invoiceId, string clientId, string kind, string status, string author, string recipients, string subject, string message, IEnumerable<string> invoiceIds, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new InvalidOperationException("Falta el identificador de la operación.");
        var payload = new Dictionary<string, object> { ["cr07a_name"] = $"{kind} {id}", ["cr07a_factura"] = Guid.Parse(invoiceId).ToString("D"), ["cr07a_cliente"] = clientId,
            ["cr07a_tipo"] = kind, ["cr07a_estado"] = status, ["cr07a_autor"] = author, ["cr07a_destinatarios"] = recipients, ["cr07a_asunto"] = subject, ["cr07a_mensaje"] = message,
            ["cr07a_facturas"] = string.Join(";", invoiceIds.Select(i => Guid.Parse(i).ToString("D"))) };
        using var content = JsonContent.Create(payload);
        using var response = await CallDataverseAppResponseAsync($"{CollectionEvents}({id})", "PATCH", ct, content, r => r.Headers.TryAddWithoutValidation("If-None-Match", "*"));
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return false;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("No fue posible registrar la gestión de cartera. No se envió el correo.");
        return true;
    }

    public async Task CompleteCollectionEventAsync(Guid id, string status, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?> { ["cr07a_estado"] = status };
        if (status == "sent") payload["cr07a_enviadoel"] = DateTimeOffset.UtcNow;
        await CallDataverseAppSendAsync($"{CollectionEvents}({id})", "PATCH", payload, ct, r => r.Headers.TryAddWithoutValidation("If-Match", "*"));
    }
    public async Task<string?> GetCollectionEventStatusAsync(Guid id, CancellationToken ct)
    {
        using var response = await CallDataverseAppResponseAsync($"{CollectionEvents}({id})?$select=cr07a_estado", "GET", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ReadString(doc.RootElement, "cr07a_estado");
    }
    public async Task<IReadOnlySet<string>> GetAutomaticCollectionClaimsAsync(IEnumerable<string> invoiceIds, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in invoiceIds.Distinct().Chunk(50))
        {
            var filter = "(cr07a_tipo eq 'automatic' or cr07a_tipo eq 'first') and (" + string.Join(" or ", batch.Select(id => $"cr07a_factura eq '{Guid.Parse(id):D}'")) + ")";
            var rows = await GetDataverseAppEntitiesAsync($"{CollectionEvents}?$select=cr07a_factura&$filter={Uri.EscapeDataString(filter)}", ct);
            foreach (var row in rows) result.Add(ReadString(row, "cr07a_factura"));
        }
        return result;
    }
}
