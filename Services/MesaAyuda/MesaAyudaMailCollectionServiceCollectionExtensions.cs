using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CotizadorInterno.Web.Services.MesaAyuda;

public static class MesaAyudaMailCollectionServiceCollectionExtensions
{
    public static IServiceCollection AddMesaAyudaMailCollection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MesaAyudaMailCollectionOptions>()
            .Bind(configuration.GetSection(
                MesaAyudaMailCollectionOptions.SectionName))
            .Validate(
                MesaAyudaMailCollectionOptions.IsValid,
                "Configuración inválida de MesaAyudaMailCollection.")
            .ValidateOnStart();

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<
            IMesaAyudaGraphTokenProvider,
            MesaAyudaGraphTokenProvider>();
        services
            .AddHttpClient<IMesaAyudaGraphMailClient, GraphMesaAyudaMailClient>(
                (serviceProvider, client) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<
                                MesaAyudaMailCollectionOptions>>()
                        .Value;
                    client.Timeout =
                        TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
                });

        if (!configuration
                .GetSection(MesaAyudaMailCollectionOptions.SectionName)
                .GetValue<bool>(nameof(MesaAyudaMailCollectionOptions.Enabled)))
        {
            return services;
        }

        services.TryAddScoped<IMesaAyudaMailCollector, MesaAyudaMailCollector>();
        services.AddHostedService<MesaAyudaMailCollectionHostedService>();
        return services;
    }
}
