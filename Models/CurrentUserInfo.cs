using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Models;

public sealed class CurrentUserInfo
{
    public string SystemUserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string EmployeeUserDisplayName { get; set; } = "";
    public string EmployeeUserEmail { get; set; } = "";
    public List<int> ModuleOptionValues { get; set; } = new();
    public string AguasSdaAppUserId { get; set; } = "";
    public string AguasSdaAreaIntervencionId { get; set; } = "";
    public string AguasSdaAreaIntervencionName { get; set; } = "";
    public List<int> AguasSdaRoleValues { get; set; } = new();
    public string PermissionLoadWarning { get; set; } = "";

    public bool HasPermissionLoadWarning =>
        !string.IsNullOrWhiteSpace(PermissionLoadWarning);

    public bool HasModule(int optionValue) =>
        ModuleOptionValues.Contains(optionValue);

    public bool HasModule(AppModule module)
    {
        var definition = AppModuleCatalog.Find(module);
        return definition is not null && HasModule(definition.OptionValue);
    }

    public bool HasAguasSdaRole(int roleValue) =>
        AguasSdaRoleValues.Contains(roleValue);

    public bool HasAnyAguasSdaRole(params int[] roleValues) =>
        roleValues.Any(HasAguasSdaRole);
}
