using CotizadorInterno.Web.Models.Crm;

namespace CotizadorInterno.Web.Services.Crm;

public interface ICrmRepository
{
    Task<CrmWorkspaceViewModel> GetWorkspaceAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct = default);

    Task<CrmWorkspaceViewModel> GetWorkspaceAsync(
        CrmWorkspaceQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmCompanyDetailViewModel> GetCompanyDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default);

    Task<CrmCompanyDetailViewModel> GetCompanyDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmContactDetailViewModel> GetContactDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default);

    Task<CrmContactDetailViewModel> GetContactDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmDealDetailViewModel> GetDealDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default);

    Task<CrmDealDetailViewModel> GetDealDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmActivityDetailViewModel> GetActivityDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default);

    Task<CrmActivityDetailViewModel> GetActivityDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<IReadOnlyList<CrmCompanySummary>> SearchCompaniesAsync(
        string search,
        int top = 12,
        CancellationToken ct = default);

    Task<IReadOnlyList<CrmCompanySummary>> SearchCompaniesAsync(
        string search,
        int top,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmCompanySummary> CreateCompanyAsync(
        CrmCompanyCreateRequest request,
        CancellationToken ct = default);

    Task<CrmCompanySummary> CreateCompanyAsync(
        CrmCompanyCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmContactSummary> CreateContactAsync(
        CrmContactCreateRequest request,
        CancellationToken ct = default);

    Task<CrmContactSummary> CreateContactAsync(
        CrmContactCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmDealSummary> UpsertDealFromCalculatorAsync(
        CrmCalculatorDealUpsertCommand command,
        CancellationToken ct = default);

    Task<CrmDealSummary> UpsertDealFromCalculatorAsync(
        CrmCalculatorDealUpsertCommand command,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmDealSummary?> GetDealByScenarioIdAsync(
        string scenarioId,
        CancellationToken ct = default);

    Task<CrmDealSummary?> MarkProvisioningRequestedAsync(
        string scenarioId,
        string requestId,
        DateTimeOffset requestedAtUtc,
        CancellationToken ct = default);

    Task<CrmDealSummary?> MarkProvisioningRequestedAsync(
        string scenarioId,
        string requestId,
        DateTimeOffset requestedAtUtc,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmActivitySummary> CreateActivityAsync(
        CrmActivityCreateRequest request,
        CancellationToken ct = default);

    Task<CrmActivitySummary> CreateActivityAsync(
        CrmActivityCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmDealSummary> CreateEstimatedDealAsync(
        CrmManualDealCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmDealSummary> ChangeDealStageAsync(
        CrmDealStageChangeRequest request,
        CancellationToken ct = default);

    Task<CrmDealSummary> ChangeDealStageAsync(
        CrmDealStageChangeRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);

    Task<CrmOwnerChangeResult> UpdateOwnerAsync(
        CrmOwnerChangeRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default);
}

public class CrmRepositoryException : Exception
{
    public CrmRepositoryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class CrmValidationException : CrmRepositoryException
{
    public CrmValidationException(string message)
        : base(message)
    {
    }
}

public sealed class CrmNotFoundException : CrmRepositoryException
{
    public CrmNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class CrmConflictException : CrmRepositoryException
{
    public CrmConflictException(string message)
        : base(message)
    {
    }
}

public sealed class CrmDataverseException : CrmRepositoryException
{
    public CrmDataverseException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public sealed class CrmAccessDeniedException : CrmRepositoryException
{
    public CrmAccessDeniedException(string message)
        : base(message)
    {
    }
}
