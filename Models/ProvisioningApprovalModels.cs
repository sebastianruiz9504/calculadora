namespace CotizadorInterno.Web.Models;

public enum ProvisioningRequestLifecycleStatus
{
    PendingApproval = 0,
    FlowDispatchFailed = 1,
    Approved = 2,
    Rejected = 3
}

public sealed class ProvisioningApprovalActor
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class ProvisioningApprovalDecision
{
    public string ApprovalId { get; set; } = "";
    public bool Approved { get; set; }
    public string Outcome { get; set; } = "";
    public string Comments { get; set; } = "";
    public DateTimeOffset? RespondedAtUtc { get; set; }
    public ProvisioningApprovalActor? Approver { get; set; }
}

public enum ProvisioningHardwareSyncStatus
{
    Pending = 0,
    NotRequired = 1,
    Completed = 2,
    Failed = 3
}

public sealed class ProvisioningHardwareSyncInfo
{
    public ProvisioningHardwareSyncStatus Status { get; set; } = ProvisioningHardwareSyncStatus.Pending;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int ImportedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class ProvisioningStoredRequest
{
    public string RequestId { get; set; } = "";
    public string Source { get; set; } = "";
    public ProvisioningRequestLifecycleStatus Status { get; set; } = ProvisioningRequestLifecycleStatus.PendingApproval;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string FlowDispatchMessage { get; set; } = "";
    public ProvisioningRequestInput Request { get; set; } = new();
    public ProvisioningApprovalDecision? Approval { get; set; }
    public ProvisioningHardwareSyncInfo HardwareSync { get; set; } = new();
}

public sealed class ProvisioningApprovalCallbackInput
{
    public string RequestId { get; set; } = "";
    public bool? Approved { get; set; }
    public string Outcome { get; set; } = "";
    public string Comments { get; set; } = "";
    public string ApprovalId { get; set; } = "";
    public DateTimeOffset? RespondedAtUtc { get; set; }
    public ProvisioningApprovalActor? Approver { get; set; }
}
