[![Nuget](https://img.shields.io/nuget/v/Mehedi.Write.RDBMS.Infrastructure.Abstractions)](https://www.nuget.org/packages/Mehedi.Write.RDBMS.Infrastructure.Abstractions/)
[![Nuget](https://img.shields.io/nuget/dt/Mehedi.Write.RDBMS.Infrastructure.Abstractions)](https://www.nuget.org/packages/Mehedi.Write.RDBMS.Infrastructure.Abstractions/)

# Mehedi.Write.RDBMS.Infrastructure.Abstructions
Some useful base classes and interfaces, mainly used with the Write RDMS layer inside CleanArchitecture template. 

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
- [Ardalis.SharedKernel](https://github.com/ardalis/Ardalis.SharedKernel)
- [Clean Architecture Solution Template](https://github.com/jasontaylordev/CleanArchitecture)
- [ASP.NET Core C# CQRS Event Sourcing, REST API, DDD, SOLID Principles and Clean Architecture](https://github.com/jeangatto/ASP.NET-Core-Clean-Architecture-CQRS-Event-Sourcing)

