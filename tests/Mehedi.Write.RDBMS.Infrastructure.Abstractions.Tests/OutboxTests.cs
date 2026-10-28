using Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Tests;

public class OutboxTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static OutboxProcessor<TestDbContext> Processor(TestDbContext db, FakeProducer producer, TimeProvider clock, int maxAttempts = 10) =>
        new(db, producer, clock, Options.Create(new OutboxOptions { MaxAttempts = maxAttempts, BatchSize = 50 }),
            NullLogger<OutboxProcessor<TestDbContext>>.Instance);

    [Fact]
    public void A_message_keeps_the_event_id_type_and_payload()
    {
        var @event = new OrderPlaced(Guid.NewGuid(), 42.5m);

        var message = OutboxMessage.Create(@event, Start.UtcDateTime);

        Assert.Equal(@event.Id, message.Id);
        Assert.Equal(nameof(OrderPlaced), message.EventName);
        Assert.Equal(typeof(OrderPlaced).AssemblyQualifiedName, message.EventType);
        Assert.Contains("42.5", message.Payload, StringComparison.Ordinal);
        Assert.Null(message.ProcessedOnUtc);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public async Task Added_events_are_stored_by_the_next_save()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = TestDbContext.Create(name))
        {
            var outbox = new IntegrationEventOutbox(db, new FixedTimeProvider(Start));
            outbox.Add(new OrderPlaced(Guid.NewGuid(), 10m));

            Assert.Empty(await db.Set<OutboxMessage>().ToListAsync());
            await db.SaveChangesAsync();
        }

        await using var verify = TestDbContext.Create(name);
        var stored = Assert.Single(await verify.Set<OutboxMessage>().ToListAsync());
        Assert.Equal(Start.UtcDateTime, stored.OccurredOnUtc);
    }

    [Fact]
    public async Task Pending_events_are_published_in_order_with_their_original_ids()
    {
        await using var db = TestDbContext.Create();
        var clock = new FixedTimeProvider(Start);
        var first = new OrderPlaced(Guid.NewGuid(), 1m);
        var second = new OrderPlaced(Guid.NewGuid(), 2m);
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(second, Start.UtcDateTime.AddSeconds(1)));
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(first, Start.UtcDateTime));
        await db.SaveChangesAsync();
        var producer = new FakeProducer();

        var published = await Processor(db, producer, clock).PublishPendingAsync(CancellationToken.None);

        Assert.Equal(2, published);
        Assert.Equal(new[] { first.Id, second.Id }, producer.Published.Select(e => e.Id));
        Assert.Equal(1m, Assert.IsType<OrderPlaced>(producer.Published[0]).Total);
        Assert.All(await db.Set<OutboxMessage>().ToListAsync(), m => Assert.Equal(Start.UtcDateTime, m.ProcessedOnUtc));
    }

    [Fact]
    public async Task Published_messages_are_not_published_again()
    {
        await using var db = TestDbContext.Create();
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(new OrderPlaced(Guid.NewGuid(), 1m), Start.UtcDateTime));
        await db.SaveChangesAsync();
        var producer = new FakeProducer();
        var processor = Processor(db, producer, new FixedTimeProvider(Start));

        await processor.PublishPendingAsync(CancellationToken.None);
        var secondRun = await processor.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(0, secondRun);
        Assert.Single(producer.Published);
    }

    [Fact]
    public async Task A_failure_is_recorded_and_stops_the_batch_to_keep_the_order()
    {
        await using var db = TestDbContext.Create();
        var failing = new OrderPlaced(Guid.NewGuid(), 1m);
        var later = new OrderPlaced(Guid.NewGuid(), 2m);
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(failing, Start.UtcDateTime));
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(later, Start.UtcDateTime.AddSeconds(1)));
        await db.SaveChangesAsync();
        var producer = new FakeProducer { Fail = e => e.Id == failing.Id ? new TimeoutException("broker down") : null };

        var published = await Processor(db, producer, new FixedTimeProvider(Start)).PublishPendingAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(producer.Published);
        var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == failing.Id);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("broker down", message.LastError, StringComparison.Ordinal);
        Assert.Null((await db.Set<OutboxMessage>().SingleAsync(m => m.Id == later.Id)).ProcessedOnUtc);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_eventually_skipped()
    {
        await using var db = TestDbContext.Create();
        var poison = new OrderPlaced(Guid.NewGuid(), 1m);
        var healthy = new OrderPlaced(Guid.NewGuid(), 2m);
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(poison, Start.UtcDateTime));
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(healthy, Start.UtcDateTime.AddSeconds(1)));
        await db.SaveChangesAsync();
        var producer = new FakeProducer { Fail = e => e.Id == poison.Id ? new InvalidOperationException("bad") : null };
        var processor = Processor(db, producer, new FixedTimeProvider(Start), maxAttempts: 2);

        await processor.PublishPendingAsync(CancellationToken.None);
        await processor.PublishPendingAsync(CancellationToken.None);
        var third = await processor.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(1, third);
        Assert.Equal(healthy.Id, Assert.Single(producer.Published).Id);
    }

    [Fact]
    public async Task An_unacknowledged_publish_counts_as_a_failure()
    {
        await using var db = TestDbContext.Create();
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(new OrderPlaced(Guid.NewGuid(), 1m), Start.UtcDateTime));
        await db.SaveChangesAsync();

        await Processor(db, new FakeProducer { Acknowledge = _ => false }, new FixedTimeProvider(Start))
            .PublishPendingAsync(CancellationToken.None);

        var message = await db.Set<OutboxMessage>().SingleAsync();
        Assert.Null(message.ProcessedOnUtc);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var message = OutboxMessage.Create(new OrderPlaced(Guid.NewGuid(), 1m), Start.UtcDateTime);

        message.MarkFailed(new string('x', OutboxMessage.MaxErrorLength + 100));

        Assert.Equal(OutboxMessage.MaxErrorLength, message.LastError!.Length);
    }

    [Fact]
    public void The_outbox_and_its_dispatcher_are_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationEventOutbox<TestDbContext>();

        Assert.Contains(services, d => d.ServiceType == typeof(IIntegrationEventOutbox));
        Assert.Contains(services, d => d.ServiceType == typeof(OutboxProcessor<TestDbContext>));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxDispatcher<TestDbContext>));
    }
}
