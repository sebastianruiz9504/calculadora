using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Permissions;

public static class CrmAccessPolicy
{
    public const int UserOptionValue = 645250025;
    public const int AdministratorOptionValue = 645250026;
    public const string InitialAdministratorObjectId = "7210b83f-ca5c-447a-aadb-4aefa72f17ad";
    public const string DigitalTechTenantId = "cab7ea42-4a21-4548-952f-fcde81f2bdd6";

    public static bool CanAccess(CurrentUserInfo? user) =>
        IsAdministrator(user)
        || user?.HasModule(UserOptionValue) == true;

    public static bool IsAdministrator(CurrentUserInfo? user)
    {
        if (user is null)
            return false;

        return user.HasModule(AdministratorOptionValue)
            || MatchesGuid(user.DirectoryObjectId, InitialAdministratorObjectId)
                && MatchesGuid(user.TenantId, DigitalTechTenantId);
    }

    public static bool HasCrmRole(IEnumerable<int>? optionValues)
    {
        if (optionValues is null)
            return false;

        return optionValues.Contains(UserOptionValue)
            || optionValues.Contains(AdministratorOptionValue);
    }

    private static bool MatchesGuid(string? value, string expected) =>
        Guid.TryParse(value, out var parsed)
        && Guid.TryParse(expected, out var expectedGuid)
        && parsed == expectedGuid;
}
