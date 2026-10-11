using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pheme.UnitTests
{
    /// <summary>
    /// The table of the in-memory outbox stores, shared by all scopes of a provider, with the work their scopes saved.
    /// </summary>
    internal sealed class InMemoryOutbox(TimeProvider timeProvider)
    {
        private readonly List<OutboxMessage> messages = [];
        private readonly List<string> savedWork = [];

        /// <summary>
        /// Gets what the units of work of the status scopes held when a store saved the status of a message, as a
        /// <c>DbContext</c> saves its tracked entities together with the status.
        /// </summary>
        public IReadOnlyList<string> SavedWork
        {
            get
            {
                lock (messages)
                {
                    return [.. savedWork];
                }
            }
        }

        /// <summary>
        /// Gets whether a message with <paramref name="id"/> is stored.
        /// </summary>
        public bool Contains(Guid id)
        {
            lock (messages)
            {
                return messages.Any(message => message.Id == id);
            }
        }

        /// <summary>
        /// Gets the stored message with <paramref name="id"/>.
        /// </summary>
        public OutboxMessage this[Guid id]
        {
            get
            {
                lock (messages)
                {
                    return Copy(messages.Single(message => message.Id == id));
                }
            }
        }

        /// <summary>
        /// Gets or sets how many reads of pending messages fail before they succeed, as with a database that is down.
        /// </summary>
        public int FailingReads { get; set; }

        /// <summary>
        /// Gets or sets how many deletions of processed messages fail before they succeed.
        /// </summary>
        public int FailingDeletes { get; set; }

        /// <summary>
        /// Gets the sources whose statistics cannot be read, as with a database that is down.
        /// </summary>
        public HashSet<string> UnreadableStats { get; } = [];

        public OutboxMessage Add(IServiceProvider provider, IIntegrationEvent integrationEvent, string source = "Tests")
        {
            OutboxMessage message = provider.GetRequiredService<IOutboxMessageFactory>().Create(integrationEvent, source);
            Add(message);
            return message;
        }

        public void Add(OutboxMessage message)
        {
            lock (messages)
            {
                messages.Add(Copy(message));
            }
        }

        public IReadOnlyList<OutboxMessage> GetPending(string source, int batchSize, int maxAttempts, DateTimeOffset now)
        {
            lock (messages)
            {
                if (FailingReads > 0)
                {
                    FailingReads--;
                    throw new InvalidOperationException("The database is down.");
                }

                return [.. messages
                    .Where(message => message.Source == source && message.ProcessedOnUtc is null && message.AttemptCount < maxAttempts)
                    .Where(message => message.NextAttemptOnUtc is null || message.NextAttemptOnUtc <= now)
                    .OrderBy(message => message.OccurredOnUtc)
                    .ThenBy(message => message.Id)
                    .Take(batchSize)
                    .Select(Copy)];
            }
        }

        public void MarkAsProcessed(Guid messageId, UnitOfWork work)
        {
            lock (messages)
            {
                OutboxMessage message = messages.Single(message => message.Id == messageId);
                message.ProcessedOnUtc = timeProvider.GetUtcNow();
                message.Error = null;
                savedWork.AddRange(work.Pending);
            }
        }

        public int MarkAsFailed(Guid messageId, string errorMessage, string? completedHandlers, DateTimeOffset? nextAttemptOnUtc, UnitOfWork work)
        {
            lock (messages)
            {
                OutboxMessage message = messages.Single(message => message.Id == messageId);
                message.AttemptCount++;
                message.Error = errorMessage;
                message.CompletedHandlers = completedHandlers;
                message.NextAttemptOnUtc = nextAttemptOnUtc;
                savedWork.AddRange(work.Pending);
                return message.AttemptCount;
            }
        }

        public IReadOnlyList<OutboxMessage> GetParked(string source, int limit, int maxAttempts)
        {
            lock (messages)
            {
                return [.. messages
                    .Where(message => message.Source == source && message.ProcessedOnUtc is null && message.AttemptCount >= maxAttempts)
                    .OrderBy(message => message.OccurredOnUtc)
                    .ThenBy(message => message.Id)
                    .Take(limit)
                    .Select(Copy)];
            }
        }

        public bool Requeue(string source, Guid messageId)
        {
            lock (messages)
            {
                if (messages.SingleOrDefault(message => message.Source == source && message.Id == messageId && message.ProcessedOnUtc is null) is not { } message)
                {
                    return false;
                }

                message.AttemptCount = 0;
                message.Error = null;
                message.NextAttemptOnUtc = null;
                return true;
            }
        }

        public int DeleteProcessedBefore(string source, DateTimeOffset processedBefore)
        {
            lock (messages)
            {
                if (FailingDeletes > 0)
                {
                    FailingDeletes--;
                    throw new InvalidOperationException("The database is down.");
                }

                return messages.RemoveAll(message => message.Source == source && message.ProcessedOnUtc < processedBefore);
            }
        }

        public OutboxStats GetStats(string source, int maxAttempts)
        {
            lock (messages)
            {
                if (UnreadableStats.Contains(source))
                {
                    throw new InvalidOperationException("The database is down.");
                }

                OutboxMessage[] pending = [.. messages.Where(message => message.Source == source && message.ProcessedOnUtc is null && message.AttemptCount < maxAttempts)];
                return new OutboxStats
                {
                    Pending = pending.Length,
                    Parked = messages.Count(message => message.Source == source && message.ProcessedOnUtc is null && message.AttemptCount >= maxAttempts),
                    OldestPendingOccurredOnUtc = pending.Length == 0 ? null : pending.Min(message => message.OccurredOnUtc),
                };
            }
        }

        private static OutboxMessage Copy(OutboxMessage message)
        {
            return new OutboxMessage
            {
                Id = message.Id,
                Type = message.Type,
                Content = message.Content,
                Source = message.Source,
                OccurredOnUtc = message.OccurredOnUtc,
                ProcessedOnUtc = message.ProcessedOnUtc,
                Error = message.Error,
                AttemptCount = message.AttemptCount,
                NextAttemptOnUtc = message.NextAttemptOnUtc,
                CompletedHandlers = message.CompletedHandlers,
                TraceParent = message.TraceParent,
            };
        }
    }

    /// <summary>
    /// A scoped unit of work, standing in for a <c>DbContext</c> with tracked changes.
    /// </summary>
    internal sealed class UnitOfWork
    {
        public List<string> Pending { get; } = [];
    }

    /// <summary>
    /// A scoped store of <see cref="InMemoryOutbox"/>, saving its scope's unit of work with each status, as a store on a
    /// <c>DbContext</c> would.
    /// </summary>
    internal sealed class InMemoryOutboxStore(string source, InMemoryOutbox outbox, UnitOfWork work) : IOutboxStore
    {
        public string Source => source;

        public Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.GetPending(source, batchSize, maxAttempts, now));
        }

        public Task MarkAsProcessed(Guid messageId, CancellationToken cancellationToken)
        {
            outbox.MarkAsProcessed(messageId, work);
            return Task.CompletedTask;
        }

        public Task<int> MarkAsFailed(
            Guid messageId, string errorMessage, string? completedHandlers, DateTimeOffset? nextAttemptOnUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.MarkAsFailed(messageId, errorMessage, completedHandlers, nextAttemptOnUtc, work));
        }

        public Task<IReadOnlyList<OutboxMessage>> GetParked(int limit, int maxAttempts, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.GetParked(source, limit, maxAttempts));
        }

        public Task<bool> Requeue(Guid messageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.Requeue(source, messageId));
        }

        public Task<int> DeleteProcessedBefore(DateTimeOffset processedBefore, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.DeleteProcessedBefore(source, processedBefore));
        }

        public Task<OutboxStats> GetStats(int maxAttempts, CancellationToken cancellationToken)
        {
            return Task.FromResult(outbox.GetStats(source, maxAttempts));
        }

        // Pheme.Testing is tested on the EF Core store.
        public Task<IReadOnlyList<OutboxMessage>> GetByType(string type, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> Redeliver(Guid messageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    internal static class InMemoryOutboxServiceCollectionExtensions
    {
        public static IServiceCollection AddInMemoryOutboxStore(this IServiceCollection services, string source = "Tests")
        {
            services.TryAddSingleton<InMemoryOutbox>();
            services.TryAddScoped<UnitOfWork>();
            return services.AddScoped<IOutboxStore>(
                provider => new InMemoryOutboxStore(source, provider.GetRequiredService<InMemoryOutbox>(), provider.GetRequiredService<UnitOfWork>()));
        }
    }
}
