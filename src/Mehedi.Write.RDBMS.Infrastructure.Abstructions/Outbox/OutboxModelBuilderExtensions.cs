using Microsoft.EntityFrameworkCore;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Outbox;

/// <summary>Maps <see cref="OutboxMessage"/> in a <see cref="DbContext"/>.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps the outbox table. Call it from <c>OnModelCreating</c> and add a migration.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     base.OnModelCreating(modelBuilder);
    ///     modelBuilder.AddOutboxMessages();
    /// }
    /// </code>
    /// </example>
    public static ModelBuilder AddOutboxMessages(this ModelBuilder modelBuilder, string tableName = "OutboxMessages", string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(message => message.Id);
            builder.Property(message => message.Id).ValueGeneratedNever();
            builder.Property(message => message.EventName).HasMaxLength(256).IsRequired();
            builder.Property(message => message.EventType).HasMaxLength(1024).IsRequired();
            builder.Property(message => message.Payload).IsRequired();
            builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);

            // The dispatcher reads pending messages in order.
            builder.HasIndex(message => new { message.ProcessedOnUtc, message.OccurredOnUtc });
        });

        return modelBuilder;
    }
}
