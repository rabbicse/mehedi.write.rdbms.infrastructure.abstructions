using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>Registers the transactional outbox.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IIntegrationEventOutbox"/> and the hosted <see cref="OutboxDispatcher{TDbContext}"/>.
    /// Requires an <c>IWriteDbContext</c> (the same <typeparamref name="TDbContext"/> instance) and an
    /// <c>IEventProducer</c>, and the outbox table mapped with <see cref="OutboxModelBuilderExtensions.AddOutboxMessages"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddIntegrationEventOutbox&lt;OrderingDbContext&gt;(options => options.PollingInterval = TimeSpan.FromSeconds(1));
    ///
    /// // in a command handler, before unitOfWork.SaveChangesAsync():
    /// outbox.Add(new OrderPlacedEvent(order.Id, order.CustomerId, order.Total));
    /// </code>
    /// </example>
    public static IServiceCollection AddIntegrationEventOutbox<TDbContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<OutboxOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.BatchSize > 0, "OutboxOptions.BatchSize must be greater than zero.")
            .Validate(options => options.MaxAttempts > 0, "OutboxOptions.MaxAttempts must be greater than zero.")
            .Validate(options => options.PollingInterval > TimeSpan.Zero, "OutboxOptions.PollingInterval must be positive.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IIntegrationEventOutbox, IntegrationEventOutbox>();
        services.TryAddScoped<OutboxProcessor<TDbContext>>();
        services.AddHostedService<OutboxDispatcher<TDbContext>>();

        return services;
    }
}
