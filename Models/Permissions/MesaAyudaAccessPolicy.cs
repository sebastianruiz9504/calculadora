namespace CotizadorInterno.Web.Models.Permissions;

public static class MesaAyudaAccessPolicy
{
    public const string InitialAuthorizedEmail = "sruiz@digitaltechcolombia.com";
    public const string InitialAuthorizedObjectId = "7210b83f-ca5c-447a-aadb-4aefa72f17ad";
    public const string DigitalTechTenantId = "cab7ea42-4a21-4548-952f-fcde81f2bdd6";

    public static bool CanAccess(CurrentUserInfo? user)
    {
        if (user is null)
        {
            return false;
        }

        return MatchesGuid(user.DirectoryObjectId, InitialAuthorizedObjectId)
            && MatchesGuid(user.TenantId, DigitalTechTenantId);
    }

    private static bool MatchesGuid(string? value, string expected) =>
        Guid.TryParse(value, out var parsed)
        && Guid.TryParse(expected, out var expectedGuid)
        && parsed == expectedGuid;
}
