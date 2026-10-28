using Mehedi.Application.SharedKernel.Persistence;
using Mehedi.EventBus;
using Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Tests;

public sealed record OrderPlaced(Guid OrderId, decimal Total) : IntegrationEvent;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IWriteDbContext
{
    public static TestDbContext Create(string? name = null) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddOutboxMessages();

    public IQueryable<TEntity> Entities<TEntity>() where TEntity : class => Set<TEntity>();

    public Task<IEnumerable<TEntity>> FromSqlRawAsync<TEntity>(string sql, params object[] parameters) where TEntity : class =>
        throw new NotSupportedException();

    public Task<IEnumerable<TResponse>> FromSqlRawAsync<TEntity, TResponse>(string sql, params object[] parameters)
        where TEntity : class where TResponse : class =>
        throw new NotSupportedException();

    public Task<IEnumerable<T>> FromSqlFunctionAsync<T>(string functionName, object parameters) where T : class =>
        throw new NotSupportedException();
}

/// <summary>Records what was published; can be told to fail or refuse.</summary>
public sealed class FakeProducer : IEventProducer
{
    public List<IntegrationEvent> Published { get; } = [];

    public Func<IntegrationEvent, bool>? Acknowledge { get; set; }

    public Func<IntegrationEvent, Exception?>? Fail { get; set; }

    public Task<bool> PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (Fail?.Invoke(@event) is { } exception)
        {
            throw exception;
        }

        var acknowledged = Acknowledge?.Invoke(@event) ?? true;
        if (acknowledged)
        {
            Published.Add(@event);
        }

        return Task.FromResult(acknowledged);
    }
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
