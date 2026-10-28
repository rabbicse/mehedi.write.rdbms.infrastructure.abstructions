using Mehedi.Application.SharedKernel.Persistence;
using Mehedi.EventBus;
using Microsoft.EntityFrameworkCore;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>
/// <see cref="Mehedi.EventBus.IIntegrationEventOutbox"/> that adds <see cref="OutboxMessage"/> rows to the scoped write context,
/// so they share its transaction.
/// </summary>
public sealed class IntegrationEventOutbox : IIntegrationEventOutbox
{
    private readonly DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the outbox over the scoped write context.</summary>
    /// <exception cref="InvalidOperationException">The write context is not an EF Core <see cref="DbContext"/>.</exception>
    public IntegrationEventOutbox(IWriteDbContext writeDbContext, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writeDbContext);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbContext = writeDbContext as DbContext
            ?? throw new InvalidOperationException("The outbox needs the IWriteDbContext to be an EF Core DbContext.");
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public void Add(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _dbContext.Set<OutboxMessage>().Add(OutboxMessage.Create(integrationEvent, _timeProvider.GetUtcNow().UtcDateTime));
    }
}
