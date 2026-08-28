using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;

namespace CotizadorInterno.Web.Services;

public interface IAutomaticTaskSyncQueue
{
    bool TryQueue(ClaimsPrincipal user);
}

public sealed class AutomaticTaskSyncQueue : IAutomaticTaskSyncQueue
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);
    private readonly Channel<AutomaticTaskSyncWorkItem> _channel = Channel.CreateUnbounded<AutomaticTaskSyncWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly object _gate = new();
    private DateTimeOffset _lastAcceptedUtc = DateTimeOffset.MinValue;
    private bool _hasPendingOrRunningWork;

    public bool TryQueue(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var nowUtc = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_hasPendingOrRunningWork || nowUtc - _lastAcceptedUtc < MinimumInterval)
                return false;

            _hasPendingOrRunningWork = true;
            _lastAcceptedUtc = nowUtc;
        }

        var accepted = _channel.Writer.TryWrite(new AutomaticTaskSyncWorkItem(ClonePrincipal(user), nowUtc));
        if (!accepted)
            MarkCompleted();

        return accepted;
    }

    internal ValueTask<AutomaticTaskSyncWorkItem> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    internal void MarkCompleted()
    {
        lock (_gate)
        {
            _hasPendingOrRunningWork = false;
        }
    }

    private static ClaimsPrincipal ClonePrincipal(ClaimsPrincipal user)
    {
        var identities = user.Identities.Select(identity =>
            new ClaimsIdentity(
                identity.Claims,
                identity.AuthenticationType,
                identity.NameClaimType,
                identity.RoleClaimType));

        return new ClaimsPrincipal(identities);
    }
}

internal sealed record AutomaticTaskSyncWorkItem(ClaimsPrincipal User, DateTimeOffset QueuedAtUtc);

public sealed class AutomaticTaskSyncHostedService : BackgroundService
{
    private readonly AutomaticTaskSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomaticTaskSyncHostedService> _logger;

    public AutomaticTaskSyncHostedService(
        AutomaticTaskSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AutomaticTaskSyncHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AutomaticTaskSyncWorkItem workItem;
            try
            {
                workItem = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunSyncAsync(workItem, stoppingToken);
        }
    }

    private async Task RunSyncAsync(AutomaticTaskSyncWorkItem workItem, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previousContext = httpContextAccessor.HttpContext;
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = workItem.User,
                RequestServices = scope.ServiceProvider
            };

            try
            {
                var dataverse = scope.ServiceProvider.GetRequiredService<IDataverseService>();
                var result = await dataverse.SyncAutomaticTasksAsync(stoppingToken);
                _logger.LogInformation(
                    "Sincronizacion automatica de tareas completada. Creadas: {CreatedCount}, actualizadas: {UpdatedCount}, cerradas: {ClosedCount}, errores notificacion: {NotificationErrorCount}.",
                    result.CreatedCount,
                    result.UpdatedCount,
                    result.ClosedCount,
                    result.NotificationErrorCount);
            }
            finally
            {
                httpContextAccessor.HttpContext = previousContext;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo la sincronizacion automatica de tareas en segundo plano.");
        }
        finally
        {
            _queue.MarkCompleted();
        }
    }
}
