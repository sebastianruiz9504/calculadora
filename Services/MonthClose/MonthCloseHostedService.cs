namespace CotizadorInterno.Web.Services.MonthClose;

public sealed class MonthCloseHostedService(IServiceScopeFactory scopes,IConfiguration config,ILogger<MonthCloseHostedService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue("MonthClose:Enabled",true))return;
        await Task.Delay(TimeSpan.FromSeconds(45),stoppingToken);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow);
                using var scope=scopes.CreateScope();
                var service=scope.ServiceProvider.GetRequiredService<MonthCloseService>();
                // Catch up after restarts; completed versions are create-only and skipped.
                await service.CaptureMissingAsync(today.Month==1?today.Year-1:today.Year,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Falló el cierre mensual. Se reintentará; no se sobrescriben cierres previos.");}
            await Task.Delay(TimeSpan.FromMinutes(10),stoppingToken);
        }
    }
}
