using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CotizadorInterno.Web.Models.Crm;

public enum CrmRole
{
    None = 0,
    User = 1,
    Administrator = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CrmObjectType
{
    Company = 1,
    Contact = 2,
    Deal = 3,
    Activity = 4
}

public sealed class CrmOwnerOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

public sealed class CrmAccessScope
{
    public string ActorSystemUserId { get; init; } = "";
    public string ActorName { get; init; } = "";
    public CrmRole Role { get; init; }
    public string OwnerFilterSystemUserId { get; init; } = "";
    public string ViewAsOwnerId { get; init; } = "";
    public IReadOnlyList<CrmOwnerOption> Owners { get; init; } = Array.Empty<CrmOwnerOption>();

    public bool IsAdministrator => Role == CrmRole.Administrator;
    public bool IsViewingAsUser => IsAdministrator && !string.IsNullOrWhiteSpace(ViewAsOwnerId);
    public bool CanViewAll => IsAdministrator && string.IsNullOrWhiteSpace(OwnerFilterSystemUserId);
    public string CreateOwnerSystemUserId =>
        !string.IsNullOrWhiteSpace(OwnerFilterSystemUserId)
            ? OwnerFilterSystemUserId
            : ActorSystemUserId;

    public bool CanReadOwner(string? ownerSystemUserId)
    {
        if (CanViewAll)
            return true;

        return Guid.TryParse(OwnerFilterSystemUserId, out var expected)
            && Guid.TryParse(ownerSystemUserId, out var actual)
            && expected == actual;
    }

    public CrmAccessViewModel ToViewModel() => new()
    {
        IsAdministrator = IsAdministrator,
        IsViewingAsUser = IsViewingAsUser,
        CanViewAll = CanViewAll,
        ViewAsOwnerId = ViewAsOwnerId,
        Owners = Owners
    };
}

public sealed class CrmAccessViewModel
{
    public bool IsAdministrator { get; init; }
    public bool IsViewingAsUser { get; init; }
    public bool CanViewAll { get; init; }
    public string ViewAsOwnerId { get; init; } = "";
    public IReadOnlyList<CrmOwnerOption> Owners { get; init; } = Array.Empty<CrmOwnerOption>();
}

public sealed class CrmOwnerChangeRequest : IValidatableObject
{
    public CrmObjectType ObjectType { get; set; }

    [Required]
    public string RecordId { get; set; } = "";

    [Required]
    public string NewOwnerSystemUserId { get; set; } = "";

    public string ViewAsOwnerId { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(ObjectType))
            yield return new ValidationResult("El tipo de objeto CRM no es válido.", [nameof(ObjectType)]);

        if (!Guid.TryParse(RecordId, out _))
            yield return new ValidationResult("El registro seleccionado no es válido.", [nameof(RecordId)]);

        if (!Guid.TryParse(NewOwnerSystemUserId, out _))
        {
            yield return new ValidationResult(
                "El nuevo propietario no es válido.",
                [nameof(NewOwnerSystemUserId)]);
        }

        if (!string.IsNullOrWhiteSpace(ViewAsOwnerId) && !Guid.TryParse(ViewAsOwnerId, out _))
            yield return new ValidationResult("El usuario seleccionado no es válido.", [nameof(ViewAsOwnerId)]);
    }
}

public sealed class CrmOwnerChangeResult
{
    public CrmObjectType ObjectType { get; init; }
    public string RecordId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public bool RemainsVisible { get; init; }
}
