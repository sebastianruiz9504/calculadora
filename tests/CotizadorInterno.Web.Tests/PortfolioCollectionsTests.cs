using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class PortfolioCollectionsTests
{
    private static readonly string Client = Guid.NewGuid().ToString();
    private static BillingInvoiceRowDto Row(int number = 7504, int days = 1, string? client = null) => new() { RecordId = Guid.NewGuid().ToString(), ClientId = client ?? Client, ClientName = "Compañía Prueba", InvoiceNumber = $"FV-2-{number}", NetTotalInvoice = 100000, TotalInvoice = 120000, CreditNoteTotal = 20000, IsOverdue = days > 0, IsPortfolioPending = true, DueDateValue = PortfolioCollectionRules.Today().AddDays(-days).ToString("yyyy-MM-dd"), DueDateDisplay = PortfolioCollectionRules.Today().AddDays(-days).ToString("dd/MM/yyyy"), EmissionDateDisplay = "01/11/2026", AgeDays = days };
    [Theory]
    [InlineData("DT4937", false)][InlineData("FV-2-5798", false)][InlineData("FV-3-0000", false)][InlineData("FV-3-0004", false)][InlineData("FV-1-7135", false)][InlineData("FV-2-7453", false)][InlineData("FV-1-7198", false)][InlineData("FV-1-7212", false)][InlineData("FV-2-7503", false)][InlineData("FV-2-7504", true)][InlineData("FV-1-10000", true)][InlineData("invalid", false)]
    public void CutoffMatchesRequestedInvoices(string invoice, bool visible) => Assert.Equal(visible, PortfolioCollectionRules.IsVisible(invoice));
    [Fact] public void NoMailOnDueDateAndNoHistoricalBackfill()
    {
        var today = PortfolioCollectionRules.Today();
        Assert.False(PortfolioCollectionRules.IsNewlyOverdue(Row(days: 0), today, today));
        Assert.False(PortfolioCollectionRules.IsNewlyOverdue(Row(days: 2), today, today));
        Assert.True(PortfolioCollectionRules.IsNewlyOverdue(Row(days: 1), today, today));
        Assert.False(PortfolioCollectionRules.IsNewlyOverdue(Row(7212), today, today));
    }
    [Fact] public async Task PagingAndAccentInsensitiveSearch()
    {
        var repo = new FakeRepo(Enumerable.Range(7504, 117).Select(i => Row(i)).ToArray());
        var service = Service(repo, new Mail());
        var page = await service.PageAsync("compania", 2, 50, "client", false, default);
        Assert.Equal(117, page.Total); Assert.Equal(50, page.Items.Count); Assert.Equal(3, page.Pages);
        Assert.Empty((await service.PageAsync("unmatched", 50, 50, null, false, default)).Items);
        Assert.Equal(1, (await service.PageAsync("7504", 1, 50, null, false, default)).Total);
    }
    [Fact] public async Task ConcurrentAndRepeatedSendHappensOnceWithPdfAndEditableRecipients()
    {
        var rows = new[] { Row(), Row(7505) }; var repo = new FakeRepo(rows); var mail = new Mail(); var service = Service(repo, mail);
        var request = Request(rows); request.AdditionalRecipients = "extra@example.com"; request.SaveAdditionalRecipients = true;
        await Task.WhenAll(service.SendAsync(request, "Collector", false, default), service.SendAsync(request, "Collector", false, default));
        await service.SendAsync(request, "Collector", false, default);
        Assert.Single(mail.Messages); Assert.Equal("extra@example.com", repo.Saved);
        var msg = mail.Messages.Single(); Assert.Equal("Edited subject", msg.Subject); Assert.Contains("Edited body", msg.HtmlBody);
        Assert.Contains("FV-2-7504", msg.HtmlBody); Assert.Contains("FV-2-7505", msg.HtmlBody); Assert.Contains("extra@example.com", msg.To);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(msg.Attachments.Single().Content));
        Assert.Equal("sent", repo.Events[request.RequestId]);
    }
    [Fact] public async Task PaymentBetweenPreviewAndSendBlocksEmail()
    {
        var rows = new[] { Row() }; var repo = new FakeRepo(rows) { FreshRows = Array.Empty<BillingInvoiceRowDto>() }; var mail = new Mail();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(repo, mail).SendAsync(Request(rows), "Collector", false, default));
        Assert.Empty(mail.Messages); Assert.Empty(repo.Events);
    }
    [Fact] public async Task ChangedBalanceRequiresReview()
    {
        var rows = new[] { Row() }; var request = Request(rows); rows[0].NetTotalInvoice = 50000;
        var repo = new FakeRepo(rows); var mail = new Mail();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(repo, mail).SendAsync(request, "Collector", false, default)); Assert.Empty(mail.Messages);
    }
    [Fact] public async Task UnknownDeliveryIsDurableAndNeverAutomaticallyRetried()
    {
        var rows = new[] { Row() }; var repo = new FakeRepo(rows); var mail = new Mail { Fail = true }; var service = Service(repo, mail); var request = Request(rows);
        Assert.Equal("uncertain", (await service.SendAsync(request, "Collector", false, default)).Status);
        await service.SendAsync(request, "Collector", false, default); Assert.Single(mail.Messages);
    }
    [Fact] public async Task AcceptedMailWithFailedHistoryWriteIsNotResent()
    {
        var rows = new[] { Row() }; var repo = new FakeRepo(rows) { FailCompletion = true }; var mail = new Mail(); var service = Service(repo, mail); var request = Request(rows);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(request, "Collector", false, default));
        Assert.Equal("sending", repo.Events[request.RequestId]); await service.SendAsync(request, "Collector", false, default); Assert.Single(mail.Messages);
    }
    [Fact] public async Task AutomationOnlySendsNewOverdueInvoiceOnceAcrossRuns()
    {
        var repo = new FakeRepo(new[] { Row(), Row(7505, 10), Row(7506, 0) }); var mail = new Mail(); var service = Service(repo, mail);
        await service.RunAutomaticAsync(default); await service.RunAutomaticAsync(default); Assert.Single(mail.Messages);
        Assert.Contains("FV-2-7505", mail.Messages.Single().HtmlBody); Assert.DoesNotContain("FV-2-7506", mail.Messages.Single().HtmlBody);
    }
    [Fact] public async Task NotesAreIdempotentAndNeverSendEmail()
    {
        var rows = new[] { Row() }; var repo = new FakeRepo(rows); var mail = new Mail(); var service = Service(repo, mail);
        var note = new CollectionNoteRequest { RequestId = Guid.NewGuid(), InvoiceId = rows[0].RecordId, Message = "Paga el lunes" };
        await service.AddNoteAsync(note, "Collector", default); await service.AddNoteAsync(note, "Collector", default); Assert.Single(repo.Events); Assert.Empty(mail.Messages);
    }
    [Fact] public void InvalidRecipientsRejectedAndHtmlEscaped()
    {
        Assert.Throws<InvalidOperationException>(() => PortfolioCollectionRules.Emails("a@example.com;bad"));
        Assert.Single(PortfolioCollectionRules.Emails("a@example.com;A@example.com"));
        var html = PortfolioCollectionRules.EmailHtml("<script>bad()</script>", PortfolioCollectionRules.StatementHtml("<img>", new[] { Row() }));
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img>", html);
    }
    private static CollectionEmailRequest Request(BillingInvoiceRowDto[] rows) => new() { RequestId = Guid.NewGuid(), InvoiceId = rows[0].RecordId, Template = "second", Subject = "Edited subject", Message = "Edited body", Recipients = "billing@example.com", StatementVersion = PortfolioCollectionRules.StatementVersion(rows) };
    private static PortfolioCollectionsService Service(FakeRepo repo, Mail mail) => new(repo, mail, Options.Create(new CollectionOptions { Enabled = true, ActivationDate = PortfolioCollectionRules.Today().ToString("yyyy-MM-dd") }), NullLogger<PortfolioCollectionsService>.Instance);
    private sealed class Mail : IReportesEmailSender
    {
        public ConcurrentBag<ReportesEmailMessage> Messages { get; } = new(); public bool Fail;
        public async Task<ReportesEmailSendResult> SendAsync(ReportesEmailMessage message, ClaimsPrincipal user, CancellationToken ct = default) { Messages.Add(message); await Task.Yield(); if (Fail) throw new HttpRequestException("Unknown outcome"); return new(); }
    }
    private sealed class FakeRepo(BillingInvoiceRowDto[] rows) : IPortfolioCollectionsRepository
    {
        public ConcurrentDictionary<Guid, string> Events { get; } = new(); public BillingInvoiceRowDto[]? FreshRows; public bool FailCompletion; public string? Saved;
        public Task<CollectionSnapshot> GetCollectionSnapshotAsync(bool refresh = false, string? clientId = null, CancellationToken ct = default) => Task.FromResult(new CollectionSnapshot(clientId is null ? rows : (FreshRows ?? rows).Where(r => r.ClientId == clientId).ToArray(), DateTimeOffset.UtcNow));
        public Task<CollectionContact> GetCollectionContactAsync(string id, string name, CancellationToken ct) => Task.FromResult(new CollectionContact(id, name, "billing@example.com", "billing@example.com", "", new[] { "billing@example.com" }));
        public Task SaveCollectionContactAsync(string id, string additional, CancellationToken ct) { Saved = additional; return Task.CompletedTask; }
        public Task<CollectionHistoryPage> GetCollectionHistoryAsync(string invoiceId, int page, CancellationToken ct) => Task.FromResult(new CollectionHistoryPage(Array.Empty<CollectionHistory>(), false, page));
        public Task<bool> TryCreateCollectionEventAsync(Guid id, string invoiceId, string clientId, string kind, string status, string author, string recipients, string subject, string message, IEnumerable<string> invoiceIds, CancellationToken ct) => Task.FromResult(Events.TryAdd(id, status));
        public Task CompleteCollectionEventAsync(Guid id, string status, CancellationToken ct) { if (FailCompletion) throw new InvalidOperationException("Write failed"); Events[id] = status; return Task.CompletedTask; }
        public Task<string?> GetCollectionEventStatusAsync(Guid id, CancellationToken ct) => Task.FromResult(Events.TryGetValue(id, out var status) ? status : null);
        public Task<IReadOnlySet<string>> GetAutomaticCollectionClaimsAsync(IEnumerable<string> invoiceIds, CancellationToken ct) => Task.FromResult<IReadOnlySet<string>>(invoiceIds.Where(id => Events.ContainsKey(PortfolioCollectionRules.AutomaticId(id))).ToHashSet());
    }
}
