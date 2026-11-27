namespace CotizadorInterno.Web.Models.Dashboard;

public sealed record PortfolioPage(IReadOnlyList<BillingInvoiceRowDto> Items, int Page, int PageSize, int Total, int Pages, DateTimeOffset RefreshedAt, PortfolioDashboardDto Summary);
public sealed record CollectionContact(string ClientId, string Name, string Email, string BillingEmail, string CollectionEmail, IReadOnlyList<string> Recipients);
public sealed record CollectionHistory(string Id, string Kind, string Status, string Author, string Recipients, string Subject, string Message, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
public sealed record CollectionHistoryPage(IReadOnlyList<CollectionHistory> Items, bool HasMore, int Page);
public sealed record CollectionDetail(BillingInvoiceRowDto Invoice, CollectionContact Contact, IReadOnlyList<BillingInvoiceRowDto> Invoices, CollectionHistoryPage History, IReadOnlyDictionary<string, CollectionEmailPreview> Templates);
public sealed class CollectionNoteRequest
{
    public Guid RequestId { get; set; }
    public string InvoiceId { get; set; } = "";
    public string Message { get; set; } = "";
}
public sealed class CollectionEmailRequest
{
    public Guid RequestId { get; set; }
    public string InvoiceId { get; set; } = "";
    public string Template { get; set; } = "second";
    public string Recipients { get; set; } = "";
    public string AdditionalRecipients { get; set; } = "";
    public bool SaveAdditionalRecipients { get; set; }
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public string StatementVersion { get; set; } = "";
}
public sealed class CollectionRecipientRequest
{
    public string InvoiceId { get; set; } = "";
    public string AdditionalRecipients { get; set; } = "";
}
public sealed record CollectionEmailPreview(string Subject, string Message, string StatementHtml, string StatementVersion);
public sealed record CollectionSendResult(string Status, string Message);
public sealed class CollectionOptions
{
    public bool Enabled { get; set; }
    public string ActivationDate { get; set; } = "";
    public string Sender { get; set; } = "cartera@digitaltechcolombia.com";
    public int IntervalMinutes { get; set; } = 15;
}
