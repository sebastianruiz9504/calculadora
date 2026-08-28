using System.ComponentModel.DataAnnotations;

namespace CotizadorInterno.Web.Models.Crm;

public enum CrmDealStage
{
    Prospecting = 645250000,
    Discovery = 645250001,
    Qualification = 645250002,
    Proposal = 645250003,
    Negotiation = 645250004,
    Won = 645250005,
    Lost = 645250006
}

public enum CrmDealKind
{
    EstimatedOpportunity = 645250000,
    QuotedBusiness = 645250001
}

public enum CrmCompanyLifecycle
{
    Lead = 645250000,
    ActiveCustomer = 645250001,
    Inactive = 645250002
}

public enum CrmActivityType
{
    Call = 645250000,
    Meeting = 645250001,
    Email = 645250002,
    Task = 645250003,
    Note = 645250004,
    Offer = 645250005
}

public enum CrmMeetingType
{
    Portfolio = 645250000,
    FollowUp = 645250001
}

public enum CrmActivityStatus
{
    Planned = 645250000,
    Completed = 645250001,
    Cancelled = 645250002
}

public enum CrmContactLifecycle
{
    Lead = 645250000,
    MarketingQualified = 645250001,
    SalesQualified = 645250002,
    Customer = 645250003,
    Inactive = 645250004
}

public sealed record CrmChoiceItem(int Value, string Label, bool IsClosed = false);

public static class CrmCatalog
{
    public static IReadOnlyList<CrmChoiceItem> DealStages { get; } =
    [
        new((int)CrmDealStage.Prospecting, "Prospección"),
        new((int)CrmDealStage.Discovery, "Descubrimiento"),
        new((int)CrmDealStage.Qualification, "Calificación"),
        new((int)CrmDealStage.Proposal, "Propuesta"),
        new((int)CrmDealStage.Negotiation, "Negociación"),
        new((int)CrmDealStage.Won, "Ganado", true),
        new((int)CrmDealStage.Lost, "Perdido", true)
    ];

    public static IReadOnlyList<CrmChoiceItem> DealKinds { get; } =
    [
        new((int)CrmDealKind.EstimatedOpportunity, "Oportunidad estimada"),
        new((int)CrmDealKind.QuotedBusiness, "Negocio cotizado")
    ];

    public static IReadOnlyList<CrmChoiceItem> CompanyLifecycles { get; } =
    [
        new((int)CrmCompanyLifecycle.Lead, "Lead"),
        new((int)CrmCompanyLifecycle.ActiveCustomer, "Cliente activo"),
        new((int)CrmCompanyLifecycle.Inactive, "Cliente inactivo")
    ];

    public static IReadOnlyList<CrmChoiceItem> ActivityTypes { get; } =
    [
        new((int)CrmActivityType.Call, "Llamada"),
        new((int)CrmActivityType.Meeting, "Reunión"),
        new((int)CrmActivityType.Email, "Correo"),
        new((int)CrmActivityType.Task, "Tarea"),
        new((int)CrmActivityType.Note, "Nota"),
        new((int)CrmActivityType.Offer, "Oferta")
    ];

    public static IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; } =
    [
        new((int)CrmActivityStatus.Planned, "Planeada"),
        new((int)CrmActivityStatus.Completed, "Completada"),
        new((int)CrmActivityStatus.Cancelled, "Cancelada")
    ];

    public static IReadOnlyList<CrmChoiceItem> MeetingTypes { get; } =
    [
        new((int)CrmMeetingType.Portfolio, "Portafolio"),
        new((int)CrmMeetingType.FollowUp, "Seguimiento")
    ];

    public static IReadOnlyList<CrmChoiceItem> ContactLifecycles { get; } =
    [
        new((int)CrmContactLifecycle.Lead, "Lead"),
        new((int)CrmContactLifecycle.MarketingQualified, "MQL"),
        new((int)CrmContactLifecycle.SalesQualified, "SQL"),
        new((int)CrmContactLifecycle.Customer, "Cliente"),
        new((int)CrmContactLifecycle.Inactive, "Inactivo")
    ];

    public static string DealStageLabel(int value) =>
        DealStages.FirstOrDefault(item => item.Value == value)?.Label ?? $"Etapa {value}";

    public static string DealKindLabel(int value) =>
        DealKinds.FirstOrDefault(item => item.Value == value)?.Label ?? $"Tipo {value}";

    public static string CompanyLifecycleLabel(int value) =>
        CompanyLifecycles.FirstOrDefault(item => item.Value == value)?.Label ?? $"Estado {value}";

    public static string ActivityTypeLabel(int value) =>
        ActivityTypes.FirstOrDefault(item => item.Value == value)?.Label ?? $"Actividad {value}";

    public static string ActivityStatusLabel(int value) =>
        ActivityStatuses.FirstOrDefault(item => item.Value == value)?.Label ?? $"Estado {value}";

    public static string MeetingTypeLabel(int value) =>
        MeetingTypes.FirstOrDefault(item => item.Value == value)?.Label ?? $"Tipo de reunión {value}";
}

public sealed class CrmWorkspaceQuery
{
    [StringLength(100)]
    public string Search { get; set; } = "";

    public CrmDealStage? Stage { get; set; }

    [Range(1, 500)]
    public int CompanyPage { get; set; } = 1;

    [Range(1, 500)]
    public int ContactPage { get; set; } = 1;

    [Range(1, 500)]
    public int DealPage { get; set; } = 1;

    [Range(1, 500)]
    public int ActivityPage { get; set; } = 1;

    [Range(10, 100)]
    public int PageSize { get; set; } = 25;

    [Range(7, 365)]
    public int PerformanceDays { get; set; } = 30;

    public string ViewAsOwnerId { get; set; } = "";
}

public sealed class CrmDetailQuery
{
    [Range(1, 500)]
    public int ContactPage { get; set; } = 1;

    [Range(1, 500)]
    public int DealPage { get; set; } = 1;

    [Range(1, 500)]
    public int ActivityPage { get; set; } = 1;

    [Range(1, 500)]
    public int HistoryPage { get; set; } = 1;

    [Range(5, 50)]
    public int PageSize { get; set; } = 12;

    public string ViewAsOwnerId { get; set; } = "";
}

public sealed class CrmWorkspaceViewModel
{
    public CrmAccessViewModel Access { get; init; } = new();
    public CrmWorkspaceQuery Query { get; init; } = new();
    public CrmPerformanceSummary Performance { get; init; } = new();
    public CrmPagedResult<CrmCompanySummary> Companies { get; init; } = CrmPagedResult<CrmCompanySummary>.Empty();
    public CrmPagedResult<CrmContactSummary> Contacts { get; init; } = CrmPagedResult<CrmContactSummary>.Empty();
    public CrmPagedResult<CrmDealSummary> Deals { get; init; } = CrmPagedResult<CrmDealSummary>.Empty();
    public CrmPagedResult<CrmActivitySummary> Activities { get; init; } = CrmPagedResult<CrmActivitySummary>.Empty();
    public IReadOnlyList<CrmChoiceItem> DealStages { get; init; } = CrmCatalog.DealStages;
    public IReadOnlyList<CrmChoiceItem> CompanyLifecycles { get; init; } = CrmCatalog.CompanyLifecycles;
    public IReadOnlyList<CrmChoiceItem> ActivityTypes { get; init; } = CrmCatalog.ActivityTypes;
    public IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; init; } = CrmCatalog.ActivityStatuses;
    public IReadOnlyList<CrmChoiceItem> MeetingTypes { get; init; } = CrmCatalog.MeetingTypes;
    public IReadOnlyList<CrmChoiceItem> ContactLifecycles { get; init; } = CrmCatalog.ContactLifecycles;
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CrmPagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public bool HasMore { get; init; }
    public bool HasPrevious => Page > 1;
    public bool HasNext => HasMore || checked(Page * PageSize) < TotalCount;

    public static CrmPagedResult<T> Empty(int page = 1, int pageSize = 25) => new()
    {
        Page = page,
        PageSize = pageSize
    };
}

public sealed class CrmPerformanceSummary
{
    public DateTimeOffset FromUtc { get; init; }
    public DateTimeOffset ToUtc { get; init; }
    public int CompletedCalls { get; init; }
    public int CompletedMeetings { get; init; }
    public int CompletedOffers { get; init; }
}

public sealed class CrmRecordAuditInfo
{
    public string OwnerId { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public string CreatedById { get; init; } = "";
    public string CreatedByName { get; init; } = "";
    public string ModifiedById { get; init; } = "";
    public string ModifiedByName { get; init; } = "";
    public DateTimeOffset? CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

public sealed class CrmCompanySummary
{
    public string Id { get; init; } = "";
    public string OperationalClientId { get; init; } = "";
    public string Name { get; init; } = "";
    public string TaxId { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string City { get; init; } = "";
    public int LifecycleValue { get; init; } = (int)CrmCompanyLifecycle.Lead;
    public string LifecycleLabel { get; init; } = "Lead";
    public DateTimeOffset? ConvertedAtUtc { get; init; }
    public CrmRecordAuditInfo Audit { get; init; } = new();
    public bool IsActiveCustomer =>
        LifecycleValue == (int)CrmCompanyLifecycle.ActiveCustomer
        && !string.IsNullOrWhiteSpace(OperationalClientId);
}

public sealed class CrmContactSummary
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string FullName => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string JobTitle { get; init; } = "";
    public int LifecycleValue { get; init; }
    public string LifecycleLabel { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool DoNotEmail { get; init; }
    public bool DoNotCall { get; init; }
    public CrmRecordAuditInfo Audit { get; init; } = new();
    public DateTimeOffset? ModifiedAtUtc => Audit.ModifiedAtUtc;
}

public sealed class CrmDealSummary
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string PrimaryContactId { get; init; } = "";
    public string PrimaryContactName { get; init; } = "";
    public int KindValue { get; init; } = (int)CrmDealKind.EstimatedOpportunity;
    public string KindLabel { get; init; } = "Oportunidad estimada";
    public string ScenarioId { get; init; } = "";
    public int StageValue { get; init; }
    public string StageLabel { get; init; } = "";
    public decimal EstimatedValue { get; init; }
    public decimal? Score { get; init; }
    public decimal? ContractValue { get; init; }
    public decimal Probability { get; init; }
    public DateOnly? ExpectedCloseDate { get; init; }
    public DateOnly? ActualCloseDate { get; init; }
    public string NextAction { get; init; } = "";
    public DateTimeOffset? NextActionAtUtc { get; init; }
    public string LostReason { get; init; } = "";
    public string BusinessLine { get; init; } = "";
    public string Description { get; init; } = "";
    public bool ProvisioningRequested { get; init; }
    public DateTimeOffset? ProvisioningRequestedAtUtc { get; init; }
    public string ProvisioningRequestId { get; init; } = "";
    public CrmRecordAuditInfo Audit { get; init; } = new();
    public DateTimeOffset? ModifiedAtUtc => Audit.ModifiedAtUtc;
    public bool CanMarkWon =>
        KindValue == (int)CrmDealKind.QuotedBusiness
        && Score.HasValue
        && ContractValue.HasValue
        && ProvisioningRequested
        && ProvisioningRequestedAtUtc.HasValue
        && !string.IsNullOrWhiteSpace(ProvisioningRequestId);
    public decimal PipelineValue =>
        KindValue == (int)CrmDealKind.QuotedBusiness
            ? ContractValue ?? 0m
            : EstimatedValue;
}

public sealed class CrmActivitySummary
{
    public string Id { get; init; } = "";
    public string Subject { get; init; } = "";
    public int TypeValue { get; init; }
    public string TypeLabel { get; init; } = "";
    public int? MeetingTypeValue { get; init; }
    public string MeetingTypeLabel { get; init; } = "";
    public string TypeDisplayLabel => string.IsNullOrWhiteSpace(MeetingTypeLabel)
        ? TypeLabel
        : $"{TypeLabel} · {MeetingTypeLabel}";
    public int StatusValue { get; init; }
    public string StatusLabel { get; init; } = "";
    public string Result { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTimeOffset? PlannedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public int? DurationMinutes { get; init; }
    public string CompanyId { get; init; } = "";
    public string CompanyName { get; init; } = "";
    public string ContactId { get; init; } = "";
    public string ContactName { get; init; } = "";
    public string DealId { get; init; } = "";
    public string DealName { get; init; } = "";
    public CrmRecordAuditInfo Audit { get; init; } = new();
}

public sealed class CrmStageHistorySummary
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DealId { get; init; } = "";
    public string DealName { get; init; } = "";
    public int? PreviousStageValue { get; init; }
    public string PreviousStageLabel { get; init; } = "";
    public int? NewStageValue { get; init; }
    public string NewStageLabel { get; init; } = "";
    public DateTimeOffset? ChangedAtUtc { get; init; }
    public decimal? DurationDays { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class CrmCompanyDetailViewModel
{
    public CrmAccessViewModel Access { get; init; } = new();
    public CrmDetailQuery Query { get; init; } = new();
    public CrmCompanySummary Company { get; init; } = new();
    public CrmPagedResult<CrmContactSummary> Contacts { get; init; } =
        CrmPagedResult<CrmContactSummary>.Empty(pageSize: 12);
    public CrmPagedResult<CrmDealSummary> Deals { get; init; } =
        CrmPagedResult<CrmDealSummary>.Empty(pageSize: 12);
    public CrmPagedResult<CrmActivitySummary> Activities { get; init; } =
        CrmPagedResult<CrmActivitySummary>.Empty(pageSize: 12);
    public IReadOnlyList<CrmChoiceItem> ContactLifecycles { get; init; } = CrmCatalog.ContactLifecycles;
    public IReadOnlyList<CrmChoiceItem> DealStages { get; init; } = CrmCatalog.DealStages;
    public IReadOnlyList<CrmChoiceItem> ActivityTypes { get; init; } = CrmCatalog.ActivityTypes;
    public IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; init; } = CrmCatalog.ActivityStatuses;
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CrmContactDetailViewModel
{
    public CrmAccessViewModel Access { get; init; } = new();
    public CrmDetailQuery Query { get; init; } = new();
    public CrmContactSummary Contact { get; init; } = new();
    public CrmCompanySummary? Company { get; init; }
    public CrmPagedResult<CrmDealSummary> Deals { get; init; } =
        CrmPagedResult<CrmDealSummary>.Empty(pageSize: 12);
    public CrmPagedResult<CrmActivitySummary> Activities { get; init; } =
        CrmPagedResult<CrmActivitySummary>.Empty(pageSize: 12);
    public IReadOnlyList<CrmChoiceItem> ContactLifecycles { get; init; } = CrmCatalog.ContactLifecycles;
    public IReadOnlyList<CrmChoiceItem> DealStages { get; init; } = CrmCatalog.DealStages;
    public IReadOnlyList<CrmChoiceItem> ActivityTypes { get; init; } = CrmCatalog.ActivityTypes;
    public IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; init; } = CrmCatalog.ActivityStatuses;
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CrmDealDetailViewModel
{
    public CrmAccessViewModel Access { get; init; } = new();
    public CrmDetailQuery Query { get; init; } = new();
    public CrmDealSummary Deal { get; init; } = new();
    public CrmCompanySummary? Company { get; init; }
    public CrmContactSummary? PrimaryContact { get; init; }
    public CrmPagedResult<CrmActivitySummary> Activities { get; init; } =
        CrmPagedResult<CrmActivitySummary>.Empty(pageSize: 12);
    public CrmPagedResult<CrmStageHistorySummary> StageHistory { get; init; } =
        CrmPagedResult<CrmStageHistorySummary>.Empty(pageSize: 12);
    public IReadOnlyList<CrmChoiceItem> DealStages { get; init; } = CrmCatalog.DealStages;
    public IReadOnlyList<CrmChoiceItem> ActivityTypes { get; init; } = CrmCatalog.ActivityTypes;
    public IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; init; } = CrmCatalog.ActivityStatuses;
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CrmActivityDetailViewModel
{
    public CrmAccessViewModel Access { get; init; } = new();
    public CrmDetailQuery Query { get; init; } = new();
    public CrmActivitySummary Activity { get; init; } = new();
    public CrmCompanySummary? Company { get; init; }
    public CrmContactSummary? Contact { get; init; }
    public CrmDealSummary? Deal { get; init; }
    public CrmPagedResult<CrmActivitySummary> RelatedActivities { get; init; } =
        CrmPagedResult<CrmActivitySummary>.Empty(pageSize: 12);
    public IReadOnlyList<CrmChoiceItem> ActivityTypes { get; init; } = CrmCatalog.ActivityTypes;
    public IReadOnlyList<CrmChoiceItem> ActivityStatuses { get; init; } = CrmCatalog.ActivityStatuses;
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CrmCompanyCreateRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [StringLength(50)]
    public string TaxId { get; set; } = "";

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [Phone, StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string City { get; set; } = "";
}

public sealed class CrmContactCreateRequest : IValidatableObject
{
    [Required]
    public string CompanyId { get; set; } = "";

    [Required, StringLength(150, MinimumLength = 2)]
    public string FirstName { get; set; } = "";

    [StringLength(150)]
    public string LastName { get; set; } = "";

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [Phone, StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(150)]
    public string JobTitle { get; set; } = "";

    public CrmContactLifecycle Lifecycle { get; set; } = CrmContactLifecycle.Customer;
    public bool IsPrimary { get; set; }
    public bool DoNotEmail { get; set; }
    public bool DoNotCall { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Guid.TryParse(CompanyId, out _))
            yield return new ValidationResult("La empresa seleccionada no es válida.", [nameof(CompanyId)]);

        if (string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Phone))
            yield return new ValidationResult("Registra al menos un correo o un teléfono.", [nameof(Email), nameof(Phone)]);

        if (!Enum.IsDefined(Lifecycle))
            yield return new ValidationResult("La etapa de ciclo de vida no es válida.", [nameof(Lifecycle)]);
    }
}

public class CrmDealFromCalculatorRequest : IValidatableObject
{
    public string DealId { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 1)]
    public string ScenarioId { get; set; } = "";

    [Required]
    public string CompanyId { get; set; } = "";

    public string PrimaryContactId { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 3)]
    public string Name { get; set; } = "";

    public CrmDealKind Kind { get; set; } = CrmDealKind.EstimatedOpportunity;

    [Range(typeof(decimal), "0", "100000000000")]
    public decimal EstimatedValue { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal Probability { get; set; }

    public DateOnly? ExpectedCloseDate { get; set; }

    [StringLength(500)]
    public string NextAction { get; set; } = "";

    public DateTimeOffset? NextActionAtUtc { get; set; }

    [StringLength(100)]
    public string BusinessLine { get; set; } = "";

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(DealId) && !Guid.TryParse(DealId, out _))
            yield return new ValidationResult("El negocio seleccionado no es válido.", [nameof(DealId)]);

        if (!Guid.TryParse(CompanyId, out _))
            yield return new ValidationResult("La empresa seleccionada no es válida.", [nameof(CompanyId)]);

        if (!string.IsNullOrWhiteSpace(PrimaryContactId) && !Guid.TryParse(PrimaryContactId, out _))
            yield return new ValidationResult("El contacto principal no es válido.", [nameof(PrimaryContactId)]);

        if (!Enum.IsDefined(Kind))
            yield return new ValidationResult("El tipo de registro comercial no es válido.", [nameof(Kind)]);
    }
}

public sealed class CrmCalculatorDealUpsertCommand : CrmDealFromCalculatorRequest
{
    public bool ApplyCommercialFields { get; set; }
    public decimal? Score { get; set; }
    public decimal? ContractValue { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        if (Kind == CrmDealKind.QuotedBusiness && !Score.HasValue)
        {
            yield return new ValidationResult(
                "El negocio cotizado requiere el puntaje calculado.",
                [nameof(Score)]);
        }

        if (Kind == CrmDealKind.QuotedBusiness && !ContractValue.HasValue)
        {
            yield return new ValidationResult(
                "El negocio cotizado requiere el valor del contrato calculado.",
                [nameof(ContractValue)]);
        }
    }
}

public sealed class CrmActivityCreateRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Subject { get; set; } = "";

    public CrmActivityType Type { get; set; } = CrmActivityType.Call;
    public CrmMeetingType? MeetingType { get; set; }
    public CrmActivityStatus Status { get; set; } = CrmActivityStatus.Planned;

    [StringLength(500)]
    public string Result { get; set; } = "";

    [StringLength(4000)]
    public string Notes { get; set; } = "";

    public DateTimeOffset? PlannedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    [Range(1, 1440)]
    public int? DurationMinutes { get; set; }

    public string CompanyId { get; set; } = "";
    public string ContactId { get; set; } = "";
    public string DealId { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Type))
            yield return new ValidationResult("El tipo de actividad no es válido.", [nameof(Type)]);

        if (MeetingType.HasValue && !Enum.IsDefined(MeetingType.Value))
        {
            yield return new ValidationResult(
                "El tipo de reunión no es válido.",
                [nameof(MeetingType)]);
        }

        if (Type == CrmActivityType.Meeting && !MeetingType.HasValue)
        {
            yield return new ValidationResult(
                "Selecciona si la reunión es de Portafolio o Seguimiento.",
                [nameof(MeetingType)]);
        }

        if (Type != CrmActivityType.Meeting && MeetingType.HasValue)
        {
            yield return new ValidationResult(
                "El tipo de reunión solo aplica a actividades de tipo Reunión.",
                [nameof(MeetingType)]);
        }

        if (!Enum.IsDefined(Status))
            yield return new ValidationResult("El estado de la actividad no es válido.", [nameof(Status)]);

        if (!IsOptionalGuid(CompanyId))
            yield return new ValidationResult("La empresa seleccionada no es válida.", [nameof(CompanyId)]);

        if (!IsOptionalGuid(ContactId))
            yield return new ValidationResult("El contacto seleccionado no es válido.", [nameof(ContactId)]);

        if (!IsOptionalGuid(DealId))
            yield return new ValidationResult("El negocio seleccionado no es válido.", [nameof(DealId)]);

        if (string.IsNullOrWhiteSpace(CompanyId)
            && string.IsNullOrWhiteSpace(ContactId)
            && string.IsNullOrWhiteSpace(DealId))
        {
            yield return new ValidationResult(
                "Relaciona la actividad con una empresa, un contacto o un negocio.",
                [nameof(CompanyId), nameof(ContactId), nameof(DealId)]);
        }

        if (Status == CrmActivityStatus.Planned && PlannedAtUtc is null)
            yield return new ValidationResult("Indica la fecha planeada de la actividad.", [nameof(PlannedAtUtc)]);

        if (Status == CrmActivityStatus.Completed && string.IsNullOrWhiteSpace(Result))
            yield return new ValidationResult("Registra el resultado de la actividad completada.", [nameof(Result)]);
    }

    private static bool IsOptionalGuid(string value) =>
        string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _);
}

public sealed class CrmManualDealCreateRequest : IValidatableObject
{
    [Required]
    public string CompanyId { get; set; } = "";

    public string PrimaryContactId { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 3)]
    public string Name { get; set; } = "";

    [Range(typeof(decimal), "0", "100000000000")]
    public decimal EstimatedContractValue { get; set; }

    [Range(typeof(decimal), "-100000000000", "100000000000")]
    public decimal EstimatedScore { get; set; }

    [Required, StringLength(120, MinimumLength = 2)]
    public string Category { get; set; } = "";

    [Required, StringLength(1000, MinimumLength = 3)]
    public string BriefDescription { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Guid.TryParse(CompanyId, out _))
            yield return new ValidationResult("La empresa seleccionada no es válida.", [nameof(CompanyId)]);

        if (!string.IsNullOrWhiteSpace(PrimaryContactId) && !Guid.TryParse(PrimaryContactId, out _))
        {
            yield return new ValidationResult(
                "El contacto principal no es válido.",
                [nameof(PrimaryContactId)]);
        }
    }
}

public sealed class CrmDealStageChangeRequest : IValidatableObject
{
    [Required]
    public string DealId { get; set; } = "";

    public CrmDealStage NewStage { get; set; }

    [StringLength(1000)]
    public string Reason { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Guid.TryParse(DealId, out _))
            yield return new ValidationResult("El negocio seleccionado no es válido.", [nameof(DealId)]);

        if (!Enum.IsDefined(NewStage))
            yield return new ValidationResult("La nueva etapa no es válida.", [nameof(NewStage)]);

        if (NewStage == CrmDealStage.Lost && string.IsNullOrWhiteSpace(Reason))
            yield return new ValidationResult("Indica el motivo por el que se perdió el negocio.", [nameof(Reason)]);
    }
}

public sealed class CrmMutationResult<T>
{
    public string Message { get; init; } = "";
    public T? Record { get; init; }
    public string TraceId { get; init; } = "";
}
