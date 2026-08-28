namespace CotizadorInterno.Web.Services.MesaAyuda;

public sealed class MesaAyudaOptions
{
    public bool SchemaProvisioned { get; set; }
    public string[] MonitoredMailboxes { get; set; } = Array.Empty<string>();
}

public sealed class MesaAyudaAiOptions
{
    public string Provider { get; set; } = "AzureOpenAI";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5-6-sol";
    public string ReasoningEffort { get; set; } = "high";
    public int MaxOutputTokens { get; set; } = 12000;
    public bool StoreResponses { get; set; }

    public bool UsesAzureOpenAi =>
        string.Equals(Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Model)
        && (UsesAzureOpenAi
            ? IsValidAzureEndpoint(Endpoint)
            : !string.IsNullOrWhiteSpace(ApiKey));

    internal static bool IsValidAzureEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && endpoint.Host.EndsWith(
            ".openai.azure.com",
            StringComparison.OrdinalIgnoreCase);
}
