using Mehedi.EventBus;
using System.Text.Json;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>
/// An integration event stored in the write database, in the same transaction as the change that raised it,
/// until <see cref="OutboxDispatcher{TDbContext}"/> has published it.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Maximum stored length of <see cref="LastError"/>.</summary>
    public const int MaxErrorLength = 2000;

    private OutboxMessage()
    {
        EventName = string.Empty;
        EventType = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>The integration event id (also the message id).</summary>
    public Guid Id { get; private set; }

    /// <summary>The event class name, for people reading the table.</summary>
    public string EventName { get; private set; }

    /// <summary>The assembly-qualified .NET type, used to deserialize <see cref="Payload"/>.</summary>
    public string EventType { get; private set; }

    /// <summary>The event serialized as JSON.</summary>
    public string Payload { get; private set; }

    /// <summary>When the event was added to the outbox (UTC).</summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>When the event was published (UTC); <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>How many publish attempts failed.</summary>
    public int Attempts { get; private set; }

    /// <summary>The last publish error, if any.</summary>
    public string? LastError { get; private set; }

    /// <summary>Creates a pending message for <paramref name="integrationEvent"/>.</summary>
    public static OutboxMessage Create(IntegrationEvent integrationEvent, DateTime occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type type = integrationEvent.GetType();
        return new OutboxMessage
        {
            Id = integrationEvent.Id,
            EventName = type.Name,
            EventType = type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, type),
            OccurredOnUtc = occurredOnUtc
        };
    }

    /// <summary>Records a successful publish.</summary>
    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        LastError = null;
    }

    /// <summary>Records a failed publish attempt.</summary>
    public void MarkFailed(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Attempts++;
        LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}
