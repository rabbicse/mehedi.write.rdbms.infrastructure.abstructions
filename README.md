[![Nuget](https://img.shields.io/nuget/v/Mehedi.Write.RDBMS.Infrastructure.Abstractions)](https://www.nuget.org/packages/Mehedi.Write.RDBMS.Infrastructure.Abstractions/)
[![Nuget](https://img.shields.io/nuget/dt/Mehedi.Write.RDBMS.Infrastructure.Abstractions)](https://www.nuget.org/packages/Mehedi.Write.RDBMS.Infrastructure.Abstractions/)

# Mehedi.Write.RDBMS.Infrastructure.Abstructions
Some useful base classes and interfaces, mainly used with the Write RDMS layer inside CleanArchitecture template. 

## Transactional outbox

Publishing an integration event straight after `SaveChangesAsync` is a *dual write*. If the broker is down,
or the process dies in between, the data is saved but the event is lost. The outbox stores the event **in the
same transaction** as the data, and a hosted dispatcher publishes it afterwards.

```csharp
// 1. Map the table and add a migration (dotnet ef migrations add AddOutbox)
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddOutboxMessages();             // table "OutboxMessages"
}

// 2. Register it (needs IWriteDbContext and an IEventProducer, e.g. Mehedi.EventBus.Kafka)
services.AddIntegrationEventOutbox<OrderingDbContext>(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.BatchSize = 50;
    options.MaxAttempts = 10;
});

// 3. In a command handler: add the event, then save. Both commit together, or neither does.
outbox.Add(new OrderPlacedEvent(order.Id, order.CustomerId, order.Total));
await unitOfWork.SaveChangesAsync(cancellationToken);
```

How the dispatcher behaves:
- It publishes pending messages oldest first, keeping each event's original `Id`, so consumers can de-duplicate.
- It stops a batch at the first failure, so a later event never overtakes an earlier one. The failure is recorded in `Attempts` and `LastError`, and the message is retried on the next poll.
- After `MaxAttempts` failures it skips the message. Reset `Attempts` to 0 to retry it.
- Delivery is at least once. A crash between publishing and saving, or two instances running at once, can publish an event twice, so make consumers idempotent.

## Technologies
- .NET Core 8
- C#
- MediatR
- xUnit

## Prerequisites
- net8.0
- Mehedi.Application.SharedKernel (>= 1.0.0)
- Mehedi.Core.SharedKernel (>= 1.0.0)
- Microsoft.EntityFrameworkCore (>= 8.0.3)
- Microsoft.EntityFrameworkCore.Relational (>= 8.0.3)
- Mehedi.EventBus.Abstractions (>= 1.0.1)
- Microsoft.Extensions.Hosting.Abstractions (>= 9.0.4)

## Packaging
To pack nuget package write the following command
```
dotnet pack
```

To publish package of Mehedi.Write.RDBMS.Infrastructure.Abstractions.1.0.0.nupkg write the following command
```
dotnet nuget push .\bin\Release\Mehedi.Write.RDBMS.Infrastructure.Abstractions.1.0.0.nupkg --api-key <api-key> --source https://api.nuget.org/v3/index.json
```

## References
- [eShop](https://github.com/dotnet/eShop)
- [Ardalis.SharedKernel](https://github.com/ardalis/Ardalis.SharedKernel)
- [Clean Architecture Solution Template](https://github.com/jasontaylordev/CleanArchitecture)
- [ASP.NET Core C# CQRS Event Sourcing, REST API, DDD, SOLID Principles and Clean Architecture](https://github.com/jeangatto/ASP.NET-Core-Clean-Architecture-CQRS-Event-Sourcing)

