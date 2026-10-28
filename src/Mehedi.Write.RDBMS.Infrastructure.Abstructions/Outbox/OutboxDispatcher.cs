using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>
/// Hosted service that keeps publishing the outbox: a batch at a time while there is work, then it waits
/// <see cref="OutboxOptions.PollingInterval"/>.
/// </summary>
/// <remarks>
/// Several instances of a service may run this at once; a message can then be published twice, which
/// at-least-once consumers tolerate.
/// </remarks>
/// <typeparam name="TDbContext">The context that maps the outbox table.</typeparam>
public sealed class OutboxDispatcher<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher<TDbContext>> _logger;

    /// <summary>Initializes the dispatcher.</summary>
    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher<TDbContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A database outage must not stop the dispatcher; it logs and tries again after the polling interval.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox dispatcher for {DbContext} started", typeof(TDbContext).Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            int published = 0;
            try
            {
                AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    OutboxProcessor<TDbContext> processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TDbContext>>();
                    published = await processor.PublishPendingAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading the outbox of {DbContext} failed", typeof(TDbContext).Name);
            }

            // A full batch means there is probably more to do: go again at once.
            if (published < _options.BatchSize)
            {
                try
                {
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Outbox dispatcher for {DbContext} stopped", typeof(TDbContext).Name);
    }
}
