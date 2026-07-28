using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StockTracker.Shared.Contracts.Messaging;

public static class ServiceCollectionExtensions
{
    // Her servis aynı RabbitMQ host'una bu extension üzerinden bağlanır, böylece
    // bağlantı/host okuma mantığı tek yerde kalır ve servisler arasında tutarlı olur.
    public static IServiceCollection AddStockTrackerRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null)
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? configuration["RabbitMq:Host"] ?? "localhost";
        var user = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? configuration["RabbitMq:User"]
            ?? throw new InvalidOperationException("RABBITMQ_USER tanımlı değil.");
        var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? configuration["RabbitMq:Password"]
            ?? throw new InvalidOperationException("RABBITMQ_PASSWORD tanımlı değil.");

        services.AddMassTransit(x =>
        {
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, "/", h =>
                {
                    h.Username(user);
                    h.Password(password);
                });

                configureEndpoints?.Invoke(context, cfg);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
