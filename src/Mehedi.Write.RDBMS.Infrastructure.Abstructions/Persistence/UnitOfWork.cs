using MediatR;
using Mehedi.Application.SharedKernel.Extensions;
using Mehedi.Application.SharedKernel.Persistence;
using Mehedi.Application.SharedKernel.Services;
using Mehedi.Core.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Persistence;

#pragma warning disable S2139 // Exceptions should be either logged or rethrown but not both
#pragma warning disable CA1848 // Use the LoggerMessage delegates
#pragma warning disable CA2254 // Template should be a static expression
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable S2629 // Logging templates should be constant
/// <summary>
/// UnitOfWork will be used to save all pending transactions to RDBMS databases
/// </summary>
/// <param name="writeDbContext"></param>
/// <param name="eventStoreRepository"></param>
/// <param name="mediator"></param>
/// <param name="logger"></param>
#pragma warning disable S3881 // "IDisposable" should be implemented correctly
public class UnitOfWork(
#pragma warning restore S3881 // "IDisposable" should be implemented correctly
    IWriteDbContext writeDbContext,
    IMediator mediator,
    ILogger<UnitOfWork> logger,
    IEventStoreRepository? eventStoreRepository = null) : IUnitOfWork
{
    private readonly IEventStoreRepository? _eventStoreRepository = eventStoreRepository;
    private readonly ILogger<UnitOfWork> _logger = logger;
    private readonly IMediator _mediator = mediator;
    private readonly IWriteDbContext _writeDbContext = writeDbContext;

    #region IUnitOfWork
    /// <summary>
    /// SaveChangesAsync: will trigger all domain events and integrated events
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_writeDbContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("WriteDbContext is not a valid DbContext.");
        }

        // Creating the execution strategy (Connection resiliency and database retries).
        var strategy = dbContext?.Database.CreateExecutionStrategy()
            ?? throw new InvalidOperationException("Database execution strategy could not be created");

        // Executing the strategy.
        return await strategy.ExecuteAsync(async () =>
        {
            if (dbContext?.Database == null)
            {
                throw new InvalidOperationException("Database access is not available");
            }

            // First await the transaction creation with ConfigureAwait
            var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            // Then handle disposal with ConfigureAwait
            await using var _ = transaction.ConfigureAwait(false);

            _logger.LogInformation(message: $"Begin transaction: '{transaction.TransactionId}'");

            try
            {
                // Getting the domain events and event stores from the tracked entities in the EF Core context.
                var (domainEvents, eventStores) = BeforeSaveChanges();

                // Save transactions to RDBMS database
                var rowsAffected = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation($"Commit transaction: '{transaction.TransactionId}'");

                // Commit transactions
                await transaction.CommitAsync().ConfigureAwait(false);

                // Triggering the events and saving the stores.
                await AfterSaveChangesAsync(domainEvents, eventStores).ConfigureAwait(false);

                _logger.LogInformation($"Transaction successfully confirmed: '{transaction.TransactionId}', Rows Affected: {rowsAffected}");
                return await Task.FromResult(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected exception occurred while committing the transaction: '{TransactionId}'", transaction.TransactionId);

                await transaction.RollbackAsync().ConfigureAwait(false);

                throw;
            }
        }).ConfigureAwait(false);
    }
    #endregion

    #region Private-Method(s)
    /// <summary>
    /// Executes logic before saving changes to the database.
    /// </summary>
    /// <returns>A tuple containing the list of domain events and event stores.</returns>
    private (IReadOnlyList<BaseDomainEvent> domainEvents, IReadOnlyList<EventStoreEvent> eventStores) BeforeSaveChanges()
    {
        if (_writeDbContext is not DbContext dbContext)
        {
            throw new InvalidOperationException("WriteDbContext is not a valid DbContext.");
        }
        // Get all domain entities with pending domain events
        var domainEntities = dbContext.ChangeTracker
            .Entries<BaseEntity>()  // Use generic version to ensure type safety
            .Where(entry =>
                entry.Entity != null &&
                entry.Entity.DomainEvents != null &&
                entry.Entity.DomainEvents.Any())
            .Select(entry => entry.Entity)
            .ToList();

        // Get all domain events from the domain entities
        var domainEvents = domainEntities
            .SelectMany(entry => entry.DomainEvents)
            .ToList();

        // Convert domain events to event stores
        var eventStores = domainEvents
            .ConvertAll(@event => new EventStoreEvent(
                @event.AggregateId,
                @event.GetGenericTypeName(),
                @event.ToJson()!));

        // Clear domain events from the entities
        domainEntities.ForEach(entry => entry.ClearDomainEvents());

        return (domainEvents.AsReadOnly(), eventStores.AsReadOnly());
    }

    /// <summary>
    /// Performs necessary actions after saving changes, such as publishing domain events and storing event stores.
    /// </summary>
    /// <param name="domainEvents">The list of domain events.</param>
    /// <param name="eventStores">The list of event stores.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AfterSaveChangesAsync(
        IReadOnlyList<BaseDomainEvent> domainEvents,
        IReadOnlyList<EventStoreEvent> eventStores)
    {
        // If there are no domain events or event stores, return without performing any actions.
        if (!domainEvents.Any() || !eventStores.Any())
            return;

        // Publish each domain event in parallel using _mediator.
        var tasks = domainEvents
            .AsParallel()
            .Select(@event => _mediator.Publish(@event))
            .ToList();

        // Wait for all the published events to be processed.
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Store the event stores using _eventStoreRepository.
        if (_eventStoreRepository != null)
        {
            await _eventStoreRepository.StoreAsync(eventStores).ConfigureAwait(false);
        }
    }
    #endregion

    #region IDisposable

    // To detect redundant calls.
    private bool _disposed;

    // Public implementation of Dispose pattern callable by consumers.
    ~UnitOfWork() => Dispose(false);

    // Public implementation of Dispose pattern callable by consumers.
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        // Dispose managed state (managed objects).
        if (disposing)
        {
            _writeDbContext?.Dispose();
            if (_eventStoreRepository is IDisposable eventStoreDisposable)
            {
                eventStoreDisposable.Dispose();
            }
        }

        _disposed = true;
    }
    #endregion
}
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CA2254 // Template should be a static expression
#pragma warning restore CA1848 // Use the LoggerMessage delegates
#pragma warning restore S2629 // Logging templates should be constant
#pragma warning restore S2139 // Exceptions should be either logged or rethrown but not both
