using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Crm;
using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Services.Crm;

public interface ICrmAccessScopeResolver
{
    Task<CrmAccessScope> ResolveAsync(
        string? viewAsOwnerId = null,
        CancellationToken ct = default);
}

public sealed class CrmAccessScopeResolver : ICrmAccessScopeResolver
{
    private readonly IDataverseService _dataverse;

    public CrmAccessScopeResolver(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    public async Task<CrmAccessScope> ResolveAsync(
        string? viewAsOwnerId = null,
        CancellationToken ct = default)
    {
        var currentUser = await _dataverse.GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var actorId = NormalizeGuid(currentUser.SystemUserId);
        if (string.IsNullOrWhiteSpace(actorId) || !CrmAccessPolicy.CanAccess(currentUser))
            throw new CrmAccessDeniedException("No tienes un rol activo dentro del CRM.");

        var isAdministrator = CrmAccessPolicy.IsAdministrator(currentUser);
        var ownerRows = await _dataverse.GetEmployeeModulePermissionsAsync(ct);
        var owners = ownerRows
            .Where(row => row.IsActive && CrmAccessPolicy.HasCrmRole(row.ModuleOptionValues))
            .Select(row => new CrmOwnerOption
            {
                Id = NormalizeGuid(row.SystemUserId),
                Name = FirstNonEmpty(row.UserDisplayName, row.EmployeeName, row.UserEmail),
                Email = row.UserEmail?.Trim() ?? ""
            })
            .Where(owner => !string.IsNullOrWhiteSpace(owner.Id))
            .GroupBy(owner => owner.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (owners.All(owner => !SameGuid(owner.Id, actorId)))
        {
            owners.Add(new CrmOwnerOption
            {
                Id = actorId,
                Name = FirstNonEmpty(currentUser.DisplayName, currentUser.EmployeeName, currentUser.Email),
                Email = FirstNonEmpty(currentUser.Email, currentUser.EmployeeUserEmail)
            });
            owners = owners
                .OrderBy(owner => owner.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var requestedOwnerId = NormalizeGuid(viewAsOwnerId);
        if (!string.IsNullOrWhiteSpace(viewAsOwnerId) && string.IsNullOrWhiteSpace(requestedOwnerId))
            throw new CrmValidationException("El usuario seleccionado para la vista no es válido.");

        if (!isAdministrator)
        {
            return BuildScope(
                currentUser,
                CrmRole.User,
                actorId,
                "",
                owners);
        }

        if (string.IsNullOrWhiteSpace(requestedOwnerId))
        {
            return BuildScope(
                currentUser,
                CrmRole.Administrator,
                "",
                "",
                owners);
        }

        if (owners.All(owner => !SameGuid(owner.Id, requestedOwnerId)))
            throw new CrmAccessDeniedException("El usuario seleccionado no tiene un rol activo dentro del CRM.");

        return BuildScope(
            currentUser,
            CrmRole.Administrator,
            requestedOwnerId,
            requestedOwnerId,
            owners);
    }

    private static CrmAccessScope BuildScope(
        CurrentUserInfo currentUser,
        CrmRole role,
        string ownerFilterSystemUserId,
        string viewAsOwnerId,
        IReadOnlyList<CrmOwnerOption> owners) => new()
    {
        ActorSystemUserId = NormalizeGuid(currentUser.SystemUserId),
        ActorName = FirstNonEmpty(currentUser.DisplayName, currentUser.EmployeeName, currentUser.Email),
        Role = role,
        OwnerFilterSystemUserId = ownerFilterSystemUserId,
        ViewAsOwnerId = viewAsOwnerId,
        Owners = owners
    };

    private static bool SameGuid(string? left, string? right) =>
        Guid.TryParse(left, out var parsedLeft)
        && Guid.TryParse(right, out var parsedRight)
        && parsedLeft == parsedRight;

    private static string NormalizeGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : "";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
