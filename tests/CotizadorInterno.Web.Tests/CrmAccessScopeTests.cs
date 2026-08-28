using System.Reflection;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.Crm;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CrmAccessScopeTests
{
    [Fact]
    public async Task UserRoleIsAlwaysRestrictedToItsOwnOwnerEvenWhenViewAsIsRequested()
    {
        var userId = Guid.NewGuid().ToString("D");
        var otherUserId = Guid.NewGuid().ToString("D");
        var resolver = CreateResolver(
            User(userId, CrmAccessPolicy.UserOptionValue),
            [
                Owner(userId, "Comercial uno", CrmAccessPolicy.UserOptionValue),
                Owner(otherUserId, "Comercial dos", CrmAccessPolicy.UserOptionValue)
            ]);

        var scope = await resolver.ResolveAsync(otherUserId);

        Assert.Equal(CrmRole.User, scope.Role);
        Assert.Equal(userId, scope.ActorSystemUserId);
        Assert.Equal(userId, scope.OwnerFilterSystemUserId);
        Assert.Equal("", scope.ViewAsOwnerId);
        Assert.False(scope.CanViewAll);
        Assert.True(scope.CanReadOwner(userId));
        Assert.False(scope.CanReadOwner(otherUserId));
    }

    [Fact]
    public async Task AdministratorCanViewAllOrSelectOneActiveCrmUser()
    {
        var administratorId = Guid.NewGuid().ToString("D");
        var commercialId = Guid.NewGuid().ToString("D");
        var resolver = CreateResolver(
            User(administratorId, CrmAccessPolicy.AdministratorOptionValue),
            [
                Owner(
                    administratorId,
                    "Administrador",
                    CrmAccessPolicy.AdministratorOptionValue),
                Owner(commercialId, "Comercial", CrmAccessPolicy.UserOptionValue)
            ]);

        var allScope = await resolver.ResolveAsync();
        var userScope = await resolver.ResolveAsync(commercialId);

        Assert.Equal(CrmRole.Administrator, allScope.Role);
        Assert.True(allScope.CanViewAll);
        Assert.True(allScope.CanReadOwner(Guid.NewGuid().ToString("D")));

        Assert.Equal(CrmRole.Administrator, userScope.Role);
        Assert.True(userScope.IsViewingAsUser);
        Assert.Equal(commercialId, userScope.ViewAsOwnerId);
        Assert.Equal(commercialId, userScope.OwnerFilterSystemUserId);
        Assert.True(userScope.CanReadOwner(commercialId));
        Assert.False(userScope.CanReadOwner(administratorId));
    }

    [Fact]
    public async Task AdministratorCannotViewAsAnInactiveOrNonCrmUser()
    {
        var administratorId = Guid.NewGuid().ToString("D");
        var inactiveUserId = Guid.NewGuid().ToString("D");
        var resolver = CreateResolver(
            User(administratorId, CrmAccessPolicy.AdministratorOptionValue),
            [
                Owner(
                    administratorId,
                    "Administrador",
                    CrmAccessPolicy.AdministratorOptionValue),
                Owner(
                    inactiveUserId,
                    "Usuario inactivo",
                    CrmAccessPolicy.UserOptionValue,
                    isActive: false)
            ]);

        await Assert.ThrowsAsync<CrmAccessDeniedException>(
            () => resolver.ResolveAsync(inactiveUserId));
    }

    [Fact]
    public async Task MissingCrmRoleIsRejectedBeforeRecordsAreLoaded()
    {
        var userId = Guid.NewGuid().ToString("D");
        var resolver = CreateResolver(
            User(userId, AppModuleCatalog.Calculator.OptionValue),
            []);

        await Assert.ThrowsAsync<CrmAccessDeniedException>(
            () => resolver.ResolveAsync());
    }

    private static CrmAccessScopeResolver CreateResolver(
        CurrentUserInfo currentUser,
        IReadOnlyList<EmployeeModulePermissionRowDto> rows)
    {
        var service = DispatchProxy.Create<IDataverseService, ScopeDataverseProxy>();
        var proxy = (ScopeDataverseProxy)service;
        proxy.CurrentUser = currentUser;
        proxy.Rows = rows;
        return new CrmAccessScopeResolver(service);
    }

    private static CurrentUserInfo User(string systemUserId, int optionValue) => new()
    {
        SystemUserId = systemUserId,
        DisplayName = "Usuario de prueba",
        ModuleOptionValues = [optionValue]
    };

    private static EmployeeModulePermissionRowDto Owner(
        string systemUserId,
        string name,
        int optionValue,
        bool isActive = true) => new()
    {
        EmployeeId = Guid.NewGuid().ToString("D"),
        SystemUserId = systemUserId,
        EmployeeName = name,
        UserDisplayName = name,
        UserEmail = $"{name.Replace(' ', '.').ToLowerInvariant()}@example.com",
        IsActive = isActive,
        ModuleOptionValues = [optionValue]
    };

    public class ScopeDataverseProxy : DispatchProxy
    {
        public CurrentUserInfo? CurrentUser { get; set; }
        public IReadOnlyList<EmployeeModulePermissionRowDto> Rows { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IDataverseService.GetCurrentUserAsync) =>
                    Task.FromResult(CurrentUser),
                nameof(IDataverseService.GetEmployeeModulePermissionsAsync) =>
                    Task.FromResult(Rows),
                _ => throw new NotSupportedException(
                    $"La prueba no implementa {targetMethod?.Name ?? "un método desconocido"}.")
            };
        }
    }
}
