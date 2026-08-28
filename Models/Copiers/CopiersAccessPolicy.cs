namespace CotizadorInterno.Web.Models.Copiers;

public static class CopiersAccessPolicy
{
    private static readonly HashSet<string> PreventiveMaintenanceFrequencyEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "adaza@digitaltechcolombia.com",
        "sruiz@digitaltechcolombia.com"
    };

    public static bool CanEditPreventiveMaintenanceFrequency(CurrentUserInfo? user)
    {
        if (user is null)
            return false;

        return IsPreventiveMaintenanceFrequencyEditor(user.Email)
            || IsPreventiveMaintenanceFrequencyEditor(user.EmployeeUserEmail);
    }

    private static bool IsPreventiveMaintenanceFrequencyEditor(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && PreventiveMaintenanceFrequencyEditors.Contains(email.Trim());
}
