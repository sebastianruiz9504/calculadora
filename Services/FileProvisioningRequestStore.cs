using System.Text.Json;
using CotizadorInterno.Web.Models;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class FileProvisioningRequestStore : IProvisioningRequestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _rootPath;

    public FileProvisioningRequestStore(IOptions<CalculatorOptions> options, IWebHostEnvironment environment)
    {
        var configuredPath = options.Value.ProvisioningRequestStorePath?.Trim() ?? "";
        _rootPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "ProvisioningRequests")
            : (Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath)));

        Directory.CreateDirectory(_rootPath);
    }

    public async Task SavePendingAsync(ProvisioningStoredRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.RequestId = NormalizeRequestId(request.RequestId);
        request.UpdatedAtUtc = request.CreatedAtUtc == default ? DateTimeOffset.UtcNow : request.UpdatedAtUtc;
        await SaveAsync(request, ct);
    }

    public async Task<ProvisioningStoredRequest?> GetAsync(string requestId, CancellationToken ct = default)
    {
        var normalizedRequestId = NormalizeRequestId(requestId);
        await _gate.WaitAsync(ct);
        try
        {
            var path = GetRequestPath(normalizedRequestId);
            if (!File.Exists(path))
                return null;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ProvisioningStoredRequest>(stream, JsonOptions, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProvisioningStoredRequest>> GetApprovedPendingHardwareSyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_rootPath))
                return Array.Empty<ProvisioningStoredRequest>();

            var files = Directory.GetFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly);
            var result = new List<ProvisioningStoredRequest>(files.Length);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file);
                var item = await JsonSerializer.DeserializeAsync<ProvisioningStoredRequest>(stream, JsonOptions, ct);
                if (item is null)
                    continue;

                if (item.Status != ProvisioningRequestLifecycleStatus.Approved || item.Approval?.Approved != true)
                    continue;

                if (item.HardwareSync.Status is ProvisioningHardwareSyncStatus.Completed or ProvisioningHardwareSyncStatus.NotRequired)
                    continue;

                result.Add(item);
            }

            return result
                .OrderBy(item => item.CreatedAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkFlowDispatchFailedAsync(string requestId, string message, CancellationToken ct = default)
    {
        var existing = await GetRequiredAsync(requestId, ct);
        existing.Status = ProvisioningRequestLifecycleStatus.FlowDispatchFailed;
        existing.FlowDispatchMessage = message?.Trim() ?? "";
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(existing, ct);
    }

    public async Task<ProvisioningStoredRequest> ApplyApprovalAsync(ProvisioningApprovalCallbackInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var existing = await GetRequiredAsync(input.RequestId, ct);
        var approved = ResolveApproved(input);
        existing.Status = approved
            ? ProvisioningRequestLifecycleStatus.Approved
            : ProvisioningRequestLifecycleStatus.Rejected;
        existing.Approval = new ProvisioningApprovalDecision
        {
            ApprovalId = input.ApprovalId?.Trim() ?? "",
            Approved = approved,
            Outcome = ResolveOutcome(input, approved),
            Comments = input.Comments?.Trim() ?? "",
            RespondedAtUtc = input.RespondedAtUtc ?? DateTimeOffset.UtcNow,
            Approver = input.Approver is null
                ? null
                : new ProvisioningApprovalActor
                {
                    DisplayName = input.Approver.DisplayName?.Trim() ?? "",
                    Email = input.Approver.Email?.Trim() ?? ""
                }
        };
        existing.HardwareSync = approved
            ? new ProvisioningHardwareSyncInfo
            {
                Status = ProvisioningHardwareSyncStatus.Pending
            }
            : new ProvisioningHardwareSyncInfo
            {
                Status = ProvisioningHardwareSyncStatus.NotRequired,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
                ImportedCount = 0,
                Message = "La solicitud fue rechazada y no requiere sincronizacion de hardware."
            };
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(existing, ct);
        return existing;
    }

    public async Task MarkHardwareSyncResultAsync(
        string requestId,
        ProvisioningHardwareSyncStatus status,
        int importedCount,
        string message,
        CancellationToken ct = default)
    {
        var existing = await GetRequiredAsync(requestId, ct);
        existing.HardwareSync = new ProvisioningHardwareSyncInfo
        {
            Status = status,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            ImportedCount = Math.Max(0, importedCount),
            Message = message?.Trim() ?? ""
        };
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(existing, ct);
    }

    private async Task<ProvisioningStoredRequest> GetRequiredAsync(string requestId, CancellationToken ct)
    {
        var existing = await GetAsync(requestId, ct);
        if (existing is null)
            throw new FileNotFoundException("No se encontro la solicitud de aprovisionamiento.", NormalizeRequestId(requestId));

        return existing;
    }

    private async Task SaveAsync(ProvisioningStoredRequest request, CancellationToken ct)
    {
        var normalizedRequestId = NormalizeRequestId(request.RequestId);
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_rootPath);
            var path = GetRequestPath(normalizedRequestId);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, request, JsonOptions, ct);
            }

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tempPath, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetRequestPath(string requestId) => Path.Combine(_rootPath, $"{requestId}.json");

    private static string NormalizeRequestId(string? requestId)
    {
        var normalized = (requestId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("requestId requerido.");

        if (normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new InvalidOperationException("requestId invalido.");

        return normalized;
    }

    private static bool ResolveApproved(ProvisioningApprovalCallbackInput input)
    {
        if (input.Approved.HasValue)
            return input.Approved.Value;

        var outcome = input.Outcome?.Trim() ?? "";
        return outcome.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("approve", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("aprobado", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("positive", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveOutcome(ProvisioningApprovalCallbackInput input, bool approved)
    {
        var outcome = input.Outcome?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(outcome))
            return outcome;

        return approved ? "approved" : "rejected";
    }
}
