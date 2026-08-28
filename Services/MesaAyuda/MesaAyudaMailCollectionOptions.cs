using System.Net.Mail;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public sealed class MesaAyudaMailCollectionOptions
{
    public const string SectionName = "MesaAyudaMailCollection";
    public const int InitialLookbackDays = 7;

    public bool Enabled { get; set; }
    public bool RunOnStartup { get; set; } = true;
    public string[] Mailboxes { get; set; } = [];
    public string ManagedIdentityClientId { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 60;
    public int PageSize { get; set; } = 50;
    public int MaxPagesPerMailbox { get; set; } = 100;
    public int HttpTimeoutSeconds { get; set; } = 100;
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;

    internal IReadOnlyList<string> GetNormalizedMailboxes() =>
        Mailboxes
            .Where(mailbox => !string.IsNullOrWhiteSpace(mailbox))
            .Select(mailbox => mailbox.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static bool IsValid(MesaAyudaMailCollectionOptions options)
    {
        if (!options.Enabled)
            return true;

        var mailboxes = options.GetNormalizedMailboxes();
        return mailboxes.Count > 0
            && mailboxes.All(IsExactEmailAddress)
            && (string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                || Guid.TryParse(options.ManagedIdentityClientId, out _))
            && options.PollIntervalSeconds is >= 30 and <= 86400
            && options.PageSize is >= 1 and <= 100
            && options.MaxPagesPerMailbox is >= 1 and <= 500
            && options.HttpTimeoutSeconds is >= 10 and <= 300
            && options.MaxResponseBytes is >= 1_048_576 and <= 67_108_864;
    }

    private static bool IsExactEmailAddress(string value) =>
        MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase);
}
