using System.Threading.Channels;
using CotizadorInterno.Web.Models.Reportes;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class ReportesGenerationQueue : BackgroundService, IReportesGenerationQueue
{
    private readonly Channel<ReporteGenerarRequest> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportesGenerationQueue> _logger;

    public ReportesGenerationQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportesGenerationQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queue = Channel.CreateBounded<ReporteGenerarRequest>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueAsync(ReporteGenerarRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var queuedRequest = new ReporteGenerarRequest
        {
            ClienteId = request.ClienteId,
            Periodo = request.Periodo
        };

        return _queue.Writer.WriteAsync(queuedRequest, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reportes = scope.ServiceProvider.GetRequiredService<IAzureOpenAIReportService>();
                var result = await reportes.GenerateReportAsync(request, stoppingToken);
                _logger.LogInformation(
                    "Generacion asincrona de informe finalizada para cliente {ClienteId}, periodo {Periodo}, estado {Estado}.",
                    request.ClienteId,
                    request.Periodo,
                    result.Estado);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Generacion asincrona de informe fallo antes de completar para cliente {ClienteId}, periodo {Periodo}.",
                    request.ClienteId,
                    request.Periodo);

                await MarkAsFailedAsync(request, ex, stoppingToken);
            }
        }
    }

    private async Task MarkAsFailedAsync(
        ReporteGenerarRequest request,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReportesDataverseRepository>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<ReportesOptions>>().Value;
            await repository.UpsertGeneratedReportAsync(new ReporteHtmlGeneradoRecord
            {
                ClienteId = request.ClienteId,
                Periodo = request.Periodo,
                HtmlGenerado = "",
                Estado = "Error",
                FechaGeneracion = DateTimeOffset.UtcNow.ToString("O"),
                PromptVersion = options.PromptVersion,
                Errores = Truncate(BuildExceptionDetail(ex), 3900)
            }, ct);
        }
        catch (Exception saveError) when (saveError is not OperationCanceledException)
        {
            _logger.LogError(
                saveError,
                "No fue posible marcar como Error el informe fallido para cliente {ClienteId}, periodo {Periodo}.",
                request.ClienteId,
                request.Periodo);
        }
    }

    private static string BuildExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var trimmed = current.Message.Trim();
            if (!messages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                messages.Add(trimmed);
        }

        return string.Join(" | ", messages);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
