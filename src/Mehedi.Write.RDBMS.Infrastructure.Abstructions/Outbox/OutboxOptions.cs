namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>Settings of the outbox dispatcher.</summary>
public sealed class OutboxOptions
{
    /// <summary>How long the dispatcher waits when the outbox is empty. Default 2 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How many messages are published per database round trip. Default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// After this many failed attempts a message is left alone (inspect <see cref="OutboxMessage.LastError"/>
    /// and reset <see cref="OutboxMessage.Attempts"/> to retry it). Default 10.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
