using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmModulePermissionTests
{
    [Fact]
    public void CatalogDefinesCrmWithDedicatedDataverseOption()
    {
        var definition = AppModuleCatalog.Find(AppModule.Crm);

        Assert.NotNull(definition);
        Assert.Equal(27, (int)AppModule.Crm);
        Assert.Equal("CRM", definition!.Label);
        Assert.Equal("Area comercial", definition.Category);
        Assert.Equal("Crm", definition.Controller);
        Assert.Equal(645250025, definition.OptionValue);
        Assert.Equal("Index", definition.Action);
        Assert.True(definition.IsNavigable);
    }

    [Fact]
    public void CatalogIncludesCrmInPermissionsAndCommercialNavigationExactlyOnce()
    {
        Assert.Single(AppModuleCatalog.AllModules, module => module.Key == AppModule.Crm);
        Assert.Single(AppModuleCatalog.PermissionModules, module => module.Key == AppModule.Crm);
        Assert.Single(AppModuleCatalog.NavigationModules, module => module.Key == AppModule.Crm);

        var commercialGroup = Assert.Single(
            AppModuleCatalog.TopNavigationGroups,
            group => group.Label == "Area comercial");

        Assert.True(commercialGroup.IsDropdown);
        Assert.Single(commercialGroup.Modules, module => module.Key == AppModule.Crm);
    }

    [Fact]
    public void CatalogDefinesASeparateNonNavigableCrmAdministratorRole()
    {
        var definition = AppModuleCatalog.Find(AppModule.CrmAdministrator);

        Assert.NotNull(definition);
        Assert.Equal("CRM Administrador", definition!.Label);
        Assert.Equal(CrmAccessPolicy.AdministratorOptionValue, definition.OptionValue);
        Assert.False(definition.IsNavigable);
        Assert.Single(
            AppModuleCatalog.PermissionModules,
            module => module.Key == AppModule.CrmAdministrator);
        Assert.DoesNotContain(
            AppModuleCatalog.NavigationModules,
            module => module.Key == AppModule.CrmAdministrator);
    }

    [Fact]
    public void CrmAccessRequiresTheAssignedModuleOption()
    {
        var user = new CurrentUserInfo
        {
            Email = "sruiz@digitaltechcolombia.com",
            EmployeeUserEmail = "sruiz@digitaltechcolombia.com"
        };

        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.Crm, user));
        Assert.False(AppModuleAccessPolicy.HasSpecificUserAccess(AppModule.Crm, user));

        user.ModuleOptionValues.Add(AppModuleCatalog.Crm.OptionValue);

        Assert.True(user.HasModule(AppModule.Crm));
        Assert.True(AppModuleAccessPolicy.CanAccess(AppModule.Crm, user));
    }

    [Fact]
    public void OtherModuleOptionsDoNotGrantCrmAccess()
    {
        var user = new CurrentUserInfo
        {
            ModuleOptionValues = new List<int>
            {
                AppModuleCatalog.Calculator.OptionValue,
                AppModuleCatalog.Hardware.OptionValue,
                AppModuleCatalog.Contracts.OptionValue
            }
        };

        Assert.False(user.HasModule(AppModule.Crm));
        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.Crm, user));
    }

    [Fact]
    public void AdministratorRoleGrantsCrmAccessWithoutDuplicatingTheUserRole()
    {
        var user = new CurrentUserInfo
        {
            ModuleOptionValues = [CrmAccessPolicy.AdministratorOptionValue]
        };

        Assert.False(user.HasModule(AppModule.Crm));
        Assert.True(CrmAccessPolicy.IsAdministrator(user));
        Assert.True(AppModuleAccessPolicy.CanAccess(AppModule.Crm, user));
    }

    [Fact]
    public void InitialAdministratorBootstrapRequiresBothObjectAndTenantIdentity()
    {
        var user = new CurrentUserInfo
        {
            DirectoryObjectId = CrmAccessPolicy.InitialAdministratorObjectId,
            TenantId = CrmAccessPolicy.DigitalTechTenantId
        };

        Assert.True(CrmAccessPolicy.IsAdministrator(user));

        user.TenantId = Guid.NewGuid().ToString("D");

        Assert.False(CrmAccessPolicy.IsAdministrator(user));
        Assert.False(CrmAccessPolicy.CanAccess(user));
    }
}
