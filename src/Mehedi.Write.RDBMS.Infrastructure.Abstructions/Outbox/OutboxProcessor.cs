using Mehedi.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>
/// Publishes one batch of pending <see cref="OutboxMessage"/>s, oldest first. Used by
/// <see cref="OutboxDispatcher{TDbContext}"/>; can also be called directly (from a test, a job or an admin endpoint).
/// </summary>
/// <typeparam name="TDbContext">The context that maps the outbox table.</typeparam>
public sealed class OutboxProcessor<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IEventProducer _producer;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor<TDbContext>> _logger;

    /// <summary>Initializes the processor.</summary>
    public OutboxProcessor(
        TDbContext dbContext,
        IEventProducer producer,
        TimeProvider timeProvider,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor<TDbContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContext = dbContext;
        _producer = producer;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Publishes up to <see cref="OutboxOptions.BatchSize"/> pending messages and saves their state.
    /// Stops at the first failure so later events are not published ahead of an earlier one.
    /// </summary>
    /// <returns>The number of messages published.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any publish failure is recorded on the message and retried later; it must not stop the batch from being saved.")]
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        List<OutboxMessage> pending = await _dbContext.Set<OutboxMessage>()
            .Where(message => message.ProcessedOnUtc == null && message.Attempts < _options.MaxAttempts)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int published = 0;
        foreach (OutboxMessage message in pending)
        {
            try
            {
                IntegrationEvent integrationEvent = Deserialize(message);
                bool acknowledged = await _producer.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
                if (!acknowledged)
                {
                    message.MarkFailed("The broker did not acknowledge the message.");
                    break;
                }

                message.MarkProcessed(_timeProvider.GetUtcNow().UtcDateTime);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publishing outbox message {MessageId} ({EventName}) failed", message.Id, message.EventName);
                message.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        if (pending.Count > 0)
        {
            // Save even when cancelled, so a published message is not published again.
            await _dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return published;
    }

    private static IntegrationEvent Deserialize(OutboxMessage message)
    {
        Type eventType = Type.GetType(message.EventType, throwOnError: false)
            ?? throw new InvalidOperationException($"Event type '{message.EventType}' cannot be loaded.");

        return JsonSerializer.Deserialize(message.Payload, eventType) as IntegrationEvent
            ?? throw new InvalidOperationException($"The payload of message {message.Id} is not a {eventType.Name}.");
    }
}
