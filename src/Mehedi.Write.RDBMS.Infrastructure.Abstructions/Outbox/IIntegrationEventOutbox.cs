using Mehedi.EventBus;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>
/// Collects integration events so they are stored by the next <c>SaveChangesAsync</c>, atomically with the
/// business data, and published afterwards by <see cref="OutboxDispatcher{TDbContext}"/>.
/// </summary>
/// <remarks>
/// This removes the dual-write problem: the event is published if and only if the transaction commits,
/// even when the broker is down at that moment. Delivery is at least once, so consumers must be idempotent.
/// </remarks>
public interface IIntegrationEventOutbox
{
    /// <summary>Adds an event to the outbox. It is written when the unit of work saves.</summary>
    void Add(IntegrationEvent integrationEvent);
}
