using Microsoft.Extensions.Options;

namespace Pheme.Outbox
{
    /// <summary>
    /// Runs the maintenance of the outbox on the stores of its scope.
    /// </summary>
    internal sealed class OutboxMaintenance(IEnumerable<IOutboxStore> stores, IOptions<OutboxOptions> options) : IOutboxMaintenance
    {
        private const int RequeueBatchSize = 100;

        private readonly int maxAttempts = options.Value.MaxAttempts;

        public async Task<IReadOnlyList<OutboxMessage>> GetParked(int limit = 100, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

            var parked = new List<OutboxMessage>();
            foreach (IOutboxStore store in stores)
            {
                parked.AddRange(await store.GetParked(limit, maxAttempts, cancellationToken));
            }

            return [.. parked.OrderBy(message => message.OccurredOnUtc).ThenBy(message => message.Id).Take(limit)];
        }

        public async Task<bool> Requeue(Guid messageId, CancellationToken cancellationToken = default)
        {
            bool requeued = false;
            foreach (IOutboxStore store in stores)
            {
                requeued |= await store.Requeue(messageId, cancellationToken);
            }

            return requeued;
        }

        public async Task<int> RequeueAllParked(string? source = null, CancellationToken cancellationToken = default)
        {
            IOutboxStore[] selected = [.. stores.Where(store => source is null || store.Source == source)];
            if (selected.Length == 0 && source is not null)
            {
                throw new ArgumentException(
                    $"No outbox store is named '{source}'. The stores are {string.Join(", ", stores.Select(store => $"'{store.Source}'"))}.",
                    nameof(source));
            }

            int requeued = 0;
            foreach (IOutboxStore store in selected)
            {
                // A requeued message is no longer parked, so each page holds the next ones.
                IReadOnlyList<OutboxMessage> parked;
                do
                {
                    parked = await store.GetParked(RequeueBatchSize, maxAttempts, cancellationToken);
                    foreach (OutboxMessage message in parked)
                    {
                        if (await store.Requeue(message.Id, cancellationToken))
                        {
                            requeued++;
                        }
                    }
                }
                while (parked.Count == RequeueBatchSize);
            }

            return requeued;
        }
    }
}
