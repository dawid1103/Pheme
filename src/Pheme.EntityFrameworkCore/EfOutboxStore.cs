using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// The outbox store of <typeparamref name="TContext"/>: the messages of its source in its outbox table, read without
    /// tracking and updated with single statements, so nothing else tracked by the context is ever saved with them.
    /// </summary>
    internal sealed class EfOutboxStore<TContext> : IOutboxStore
        where TContext : DbContext
    {
        private readonly TContext context;
        private readonly TimeProvider timeProvider;

        public EfOutboxStore(
            TContext context,
            DbContextRegistration<TContext> registration,
            IOptions<OutboxValidationOptions> validation,
            IServiceProvider serviceProvider)
        {
            // Without a host the startup validation does not run; its errors instead of the container's, and two stores
            // of one source must not deliver.
            _ = validation.Value;
            this.context = context;
            timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
            Source = registration.OutboxSource;
        }

        public string Source { get; }

        private IQueryable<OutboxMessage> Messages =>
            DbContextRegistration<TContext>.OutboxMessages(context).Where(message => message.Source == Source);

        private IQueryable<OutboxMessage> Unprocessed => Messages.Where(message => message.ProcessedOnUtc == null);

        public async Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken)
        {
            // Npgsql writes only an offset of 0 to timestamptz.
            DateTimeOffset nowUtc = now.ToUniversalTime();
            return await Unprocessed
                .AsNoTracking()
                .Where(message => message.AttemptCount < maxAttempts)
                .Where(message => message.NextAttemptOnUtc == null || message.NextAttemptOnUtc <= nowUtc)
                .OrderBy(message => message.OccurredOnUtc)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        public async Task MarkAsProcessed(Guid messageId, CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            await Messages
                .Where(message => message.Id == messageId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.ProcessedOnUtc, now)
                        .SetProperty(message => message.Error, (string?)null),
                    cancellationToken);
        }

        public async Task<int> MarkAsFailed(
            Guid messageId, string errorMessage, string? completedHandlers, DateTimeOffset? nextAttemptOnUtc, CancellationToken cancellationToken)
        {
            DateTimeOffset? nextAttempt = nextAttemptOnUtc?.ToUniversalTime();
            IQueryable<OutboxMessage> message = Messages.Where(message => message.Id == messageId);
            await message.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                    .SetProperty(message => message.Error, errorMessage)
                    .SetProperty(message => message.CompletedHandlers, completedHandlers)
                    .SetProperty(message => message.NextAttemptOnUtc, nextAttempt),
                cancellationToken);
            return await message.Select(message => message.AttemptCount).SingleAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<OutboxMessage>> GetParked(int limit, int maxAttempts, CancellationToken cancellationToken)
        {
            return await Unprocessed
                .AsNoTracking()
                .Where(message => message.AttemptCount >= maxAttempts)
                .OrderBy(message => message.OccurredOnUtc)
                .ThenBy(message => message.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> Requeue(Guid messageId, CancellationToken cancellationToken)
        {
            int requeued = await Unprocessed
                .Where(message => message.Id == messageId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.AttemptCount, 0)
                        .SetProperty(message => message.Error, (string?)null)
                        .SetProperty(message => message.NextAttemptOnUtc, (DateTimeOffset?)null),
                    cancellationToken);
            return requeued > 0;
        }

        public Task<int> DeleteProcessedBefore(DateTimeOffset processedBefore, CancellationToken cancellationToken)
        {
            // Npgsql writes only an offset of 0 to timestamptz.
            DateTimeOffset before = processedBefore.ToUniversalTime();
            return Messages.Where(message => message.ProcessedOnUtc < before).ExecuteDeleteAsync(cancellationToken);
        }

        public async Task<OutboxStats> GetStats(int maxAttempts, CancellationToken cancellationToken)
        {
            IQueryable<OutboxMessage> pending = Unprocessed.Where(message => message.AttemptCount < maxAttempts);
            return new OutboxStats
            {
                Pending = await pending.CountAsync(cancellationToken),
                Parked = await Unprocessed.CountAsync(message => message.AttemptCount >= maxAttempts, cancellationToken),
                OldestPendingOccurredOnUtc = await pending.MinAsync(message => (DateTimeOffset?)message.OccurredOnUtc, cancellationToken),
            };
        }

        public async Task<IReadOnlyList<OutboxMessage>> GetByType(string type, CancellationToken cancellationToken)
        {
            return await Messages
                .AsNoTracking()
                .Where(message => message.Type == type)
                .OrderBy(message => message.OccurredOnUtc)
                .ThenBy(message => message.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> Redeliver(Guid messageId, CancellationToken cancellationToken)
        {
            int redelivered = await Messages
                .Where(message => message.Id == messageId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.ProcessedOnUtc, (DateTimeOffset?)null)
                        .SetProperty(message => message.Error, (string?)null)
                        .SetProperty(message => message.CompletedHandlers, (string?)null)
                        .SetProperty(message => message.NextAttemptOnUtc, (DateTimeOffset?)null)
                        .SetProperty(message => message.AttemptCount, 0),
                    cancellationToken);
            return redelivered > 0;
        }
    }
}
