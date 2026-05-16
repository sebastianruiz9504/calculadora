using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Tasks;

public static class TaskStatusValues
{
    public const int Pending = 645250000;
    public const int Closed = 645250001;
    public const int Cancelled = 645250002;
}

public sealed class TaskBoardItemDto
{
    public string TaskId { get; set; } = "";
    public string UniqueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Description { get; set; } = "";
    public string AssigneeId { get; set; } = "";
    public string AssigneeName { get; set; } = "";
    public string AssigneeEmail { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string DueDateDisplay { get; set; } = "";
    public string CreatedOnDisplay { get; set; } = "";
    public string ClosedOnDisplay { get; set; } = "";
    public int StatusValue { get; set; }
    public string StatusLabel { get; set; } = "";
    public string ActionUrl { get; set; } = "";
    public bool IsManual { get; set; }
    public int PendingCount { get; set; }
    public string CloseComments { get; set; } = "";
    public bool HasCloseAttachment { get; set; }

    public bool CanExecute => !string.IsNullOrWhiteSpace(ActionUrl);
    public bool CanCloseManually => IsManual && StatusValue == TaskStatusValues.Pending;
}

public sealed class TaskSyncResultDto
{
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ClosedCount { get; set; }
    public int NotificationErrorCount { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class ManualTaskCreateRequest
{
    public string AssigneeId { get; set; } = "";
    public string AssigneeEmail { get; set; } = "";
    public string AssigneeName { get; set; } = "";
    public string DueDateValue { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ManualTaskCreateResult
{
    public string Message { get; set; } = "";
    public TaskBoardItemDto Task { get; set; } = new();
}

public sealed class ManualTaskCloseRequest
{
    public string TaskId { get; set; } = "";
    public string Comments { get; set; } = "";
}

public sealed class ManualTaskCloseResult
{
    public string Message { get; set; } = "";
    public TaskBoardItemDto Task { get; set; } = new();
}

public sealed class TaskNotificationPayload
{
    public string TaskId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string Description { get; set; } = "";
    public string AssigneeName { get; set; } = "";
    public string AssigneeEmail { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string ActionUrl { get; set; } = "";
    public bool IsManual { get; set; }
    public int PendingCount { get; set; }
    public string CreatedByName { get; set; } = "";
    public string CreatedByEmail { get; set; } = "";
    public IReadOnlyList<TaskNotificationTableRow> Rows { get; set; } = Array.Empty<TaskNotificationTableRow>();
}

public sealed class TaskNotificationTableRow
{
    public string Reference { get; set; } = "";
    public string Client { get; set; } = "";
    public string Detail { get; set; } = "";
    public string DueDate { get; set; } = "";
    public decimal Value { get; set; }
}

public sealed class TaskRuleDefinition
{
    public string UniqueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string AssigneeId { get; set; } = "";
    public string AssigneeEmail { get; set; } = "";
    public string AssigneeName { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Description { get; set; } = "";
    public string ActionUrl { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public int PendingCount { get; set; }
    public bool ShouldBeOpen { get; set; }
    public bool IsManual { get; set; }
    public IReadOnlyList<TaskNotificationTableRow> NotificationRows { get; set; } = Array.Empty<TaskNotificationTableRow>();
}

public sealed class TaskRuleAssignee
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}
