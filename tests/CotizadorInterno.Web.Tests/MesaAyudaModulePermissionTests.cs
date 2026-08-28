using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class MesaAyudaModulePermissionTests
{
    [Fact]
    public void CatalogKeepsMesaAyudaOutsideAssignablePermissions()
    {
        var definition = AppModuleCatalog.Find(AppModule.MesaAyuda);

        Assert.NotNull(definition);
        Assert.Equal(28, (int)AppModule.MesaAyuda);
        Assert.Equal("Mesa de ayuda", definition!.Label);
        Assert.Equal("MesaAyuda", definition.Controller);
        Assert.Equal(0, definition.OptionValue);
        Assert.DoesNotContain(
            AppModuleCatalog.PermissionModules,
            module => module.Key == AppModule.MesaAyuda);
    }

    [Fact]
    public void OnlyInitialAuthorizedDirectoryIdentityCanAccess()
    {
        var user = AuthorizedUser();

        Assert.True(MesaAyudaAccessPolicy.CanAccess(user));
        Assert.True(AppModuleAccessPolicy.CanAccess(AppModule.MesaAyuda, user));
    }

    [Theory]
    [InlineData("sruiz@digitaltechcolombia.com")]
    [InlineData(" SRUIZ@DIGITALTECHCOLOMBIA.COM ")]
    public void EmailAloneNeverGrantsMesaAyudaAccess(string email)
    {
        var user = new CurrentUserInfo
        {
            Email = email,
            EmployeeUserEmail = email
        };

        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.MesaAyuda, user));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abarriga@digitaltechcolombia.com")]
    [InlineData("dmarentes@digitaltechcolombia.com")]
    public void OtherUsersRemainDeniedEvenWithUnrelatedModulePermissions(string email)
    {
        var user = new CurrentUserInfo
        {
            Email = email,
            DirectoryObjectId = Guid.NewGuid().ToString("D"),
            TenantId = MesaAyudaAccessPolicy.DigitalTechTenantId,
            ModuleOptionValues =
            [
                AppModuleCatalog.SoporteCloud.OptionValue,
                AppModuleCatalog.Permissions.OptionValue
            ]
        };

        Assert.False(MesaAyudaAccessPolicy.CanAccess(user));
        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.MesaAyuda, user));
    }

    [Fact]
    public void ZeroOptionValueNeverBecomesAnAssignableAccessBypass()
    {
        var user = new CurrentUserInfo
        {
            Email = "otro@digitaltechcolombia.com",
            ModuleOptionValues = [0]
        };

        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.MesaAyuda, user));
        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.ProposalChat, user));
    }

    [Fact]
    public void CorrectObjectIdFromAnotherTenantIsDenied()
    {
        var user = AuthorizedUser();
        user.TenantId = "11111111-1111-1111-1111-111111111111";

        Assert.False(AppModuleAccessPolicy.CanAccess(AppModule.MesaAyuda, user));
    }

    [Fact]
    public void ControllerUsesTheSharedModuleAuthorizationAndAntiforgeryOnAnalyze()
    {
        var controllerAttribute = Assert.Single(
            typeof(MesaAyudaController).GetCustomAttributes(
                typeof(ModuleAuthorizeAttribute),
                inherit: true));
        Assert.IsType<ModuleAuthorizeAttribute>(controllerAttribute);

        var analyze = typeof(MesaAyudaController).GetMethod(nameof(MesaAyudaController.Analyze));
        Assert.NotNull(analyze);
        Assert.Single(
            analyze!.GetCustomAttributes(
                typeof(ValidateAntiForgeryTokenAttribute),
                inherit: true));

        var message = typeof(MesaAyudaController).GetMethod(
            nameof(MesaAyudaController.Message));
        Assert.NotNull(message);
        Assert.Single(
            message!.GetCustomAttributes(
                typeof(ValidateAntiForgeryTokenAttribute),
                inherit: true));
    }

    private static CurrentUserInfo AuthorizedUser() =>
        new()
        {
            DirectoryObjectId = MesaAyudaAccessPolicy.InitialAuthorizedObjectId,
            TenantId = MesaAyudaAccessPolicy.DigitalTechTenantId,
            Email = MesaAyudaAccessPolicy.InitialAuthorizedEmail
        };
}
