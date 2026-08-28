using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services.MesaAyuda;

internal interface IMesaAyudaGraphTokenProvider
{
    ValueTask<AccessToken> GetTokenAsync(CancellationToken ct);
}

internal sealed class MesaAyudaGraphTokenProvider : IMesaAyudaGraphTokenProvider
{
    internal const string GraphScope = "https://graph.microsoft.com/.default";

    private readonly TokenCredential _credential;

    public MesaAyudaGraphTokenProvider(
        IOptions<MesaAyudaMailCollectionOptions> options,
        IHostEnvironment environment)
    {
        var chain = CreateCredentialChain(options.Value, environment.IsDevelopment());
        _credential = new ChainedTokenCredential(chain);
    }

    public ValueTask<AccessToken> GetTokenAsync(CancellationToken ct) =>
        _credential.GetTokenAsync(
            new TokenRequestContext([GraphScope]),
            ct);

    internal static TokenCredential[] CreateCredentialChain(
        MesaAyudaMailCollectionOptions options,
        bool isDevelopment)
    {
        TokenCredential managedIdentity = string.IsNullOrWhiteSpace(
            options.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(
                    options.ManagedIdentityClientId.Trim()));

        return isDevelopment
            ? [managedIdentity, new AzureCliCredential()]
            : [managedIdentity];
    }
}
