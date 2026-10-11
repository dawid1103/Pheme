using System.Reflection;
using System.Text.Json;
using Pheme;
using Pheme.Testing;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Delivers, reads and redelivers the messages of the outbox of Pheme in integration tests, at the points the test
    /// chooses instead of on the polling of the outbox processor.
    /// </summary>
    public static class PhemeTestingServiceProviderExtensions
    {
        // The stored format of integration events, a contract of the messages already in the database, so it is
        // repeated here instead of shared through the internals of Pheme: JSON with the web defaults.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Delivers the pending outbox messages of all stores, run after run, until a run delivers nothing, so the
        /// integration events the handlers write are delivered too.
        /// </summary>
        /// <param name="services">The services of the application under test, or of a scope of it.</param>
        /// <param name="maxRounds">
        /// The maximum number of runs (<see cref="IOutboxRunner.RunOnce"/>), the last of which must deliver nothing. The
        /// default is 10.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the delivery.</param>
        /// <returns>A task that completes when no message is left to deliver.</returns>
        /// <remarks>
        /// A message whose delivery fails is reported once: the next runs of this application deliver it again, as the
        /// outbox does, but its failures no longer throw, so a test sharing the application with the one that caused
        /// it does not fail. A failed message is retried by each run until it is processed or parked
        /// (<see cref="OutboxOptions.MaxAttempts"/>), as <see cref="PhemeTestingServiceCollectionExtensions.UsePhemeOutboxForTests"/>
        /// sets <see cref="OutboxOptions.RetryDelay"/> to zero. Tests running in parallel on one application share its
        /// outbox: a failure is reported to the call that sees it first.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRounds"/> is less than 1.</exception>
        /// <exception cref="InvalidOperationException">
        /// The delivery of a message failed, not reported before (the message names its type, identifier, store and
        /// error); the last run still delivered messages, which may be an endless chain of handlers writing events; or
        /// <see cref="PhemeTestingServiceCollectionExtensions.UsePhemeOutboxForTests"/> was not called, so the outbox
        /// processor would deliver the messages, and miss their failures, behind the test's back.
        /// </exception>
        public static async Task DrainOutbox(this IServiceProvider services, int maxRounds = 10, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxRounds, 1);

            OutboxDrainState state = services.GetService<OutboxDrainState>() ?? throw new InvalidOperationException(
                "DrainOutbox needs the outbox processor turned off; otherwise it delivers messages, and their failures go "
                + "unnoticed, behind the test's back. Call services.UsePhemeOutboxForTests() in the services of the test.");
            IOutboxRunner runner = services.GetRequiredService<IOutboxRunner>();
            for (int round = 0; round < maxRounds; round++)
            {
                OutboxRunResult result = await runner.RunOnce(cancellationToken);
                OutboxMessage[] failed = state.Report(result.Failed);
                if (failed.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"Delivery of {failed.Length} outbox message(s) failed:"
                        + string.Concat(failed.Select(message =>
                            $"{Environment.NewLine}- '{message.Type}' {message.Id} of store '{message.Source}', attempt "
                            + $"{message.AttemptCount}: {message.Error}")));
                }

                if (result.Processed == 0)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"The outbox still delivered messages in run {maxRounds} of DrainOutbox. A handler may write integration "
                + "events in an endless chain; for a longer chain, pass a higher maxRounds.");
        }

        /// <summary>
        /// Gets the integration events of type <typeparamref name="TEvent"/> saved in the outbox, whatever the state of
        /// their messages: pending, parked or processed.
        /// </summary>
        /// <typeparam name="TEvent">
        /// The type of the events; only events of exactly this type are returned, not of its subtypes, as handlers get
        /// them.
        /// </typeparam>
        /// <param name="services">The services of the application under test, or of a scope of it.</param>
        /// <param name="source">
        /// The <see cref="OutboxMessage.Source"/> of the store to read, or <see langword="null"/> for all stores.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>
        /// The events, in the order of <see cref="OutboxMessage.OccurredOnUtc"/>, then of <see cref="OutboxMessage.Id"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">No store has the name <paramref name="source"/>.</exception>
        public static async Task<IReadOnlyList<TEvent>> GetOutboxEvents<TEvent>(
            this IServiceProvider services,
            string? source = null,
            CancellationToken cancellationToken = default)
            where TEvent : IIntegrationEvent
        {
            ArgumentNullException.ThrowIfNull(services);

            string type = StoredName<TEvent>();
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IOutboxStore[] stores = [.. scope.ServiceProvider.GetServices<IOutboxStore>()];
            IOutboxStore[] selected = [.. stores.Where(store => source is null || store.Source == source)];
            if (selected.Length == 0 && source is not null)
            {
                throw new ArgumentException(
                    $"No outbox store is named '{source}'. The stores are {string.Join(", ", stores.Select(store => $"'{store.Source}'"))}.",
                    nameof(source));
            }

            var messages = new List<OutboxMessage>();
            foreach (IOutboxStore store in selected)
            {
                messages.AddRange(await store.GetByType(type, cancellationToken));
            }

            return [.. messages.OrderBy(message => message.OccurredOnUtc).ThenBy(message => message.Id).Select(Deserialize<TEvent>)];
        }

        /// <summary>
        /// Makes the processed integration events of type <typeparamref name="TEvent"/> that match
        /// <paramref name="predicate"/> pending again, with no completed handlers, so the next
        /// <see cref="DrainOutbox"/> delivers them to all their handlers once more: a test that the handlers are
        /// idempotent.
        /// </summary>
        /// <typeparam name="TEvent">The type of the events; only events of exactly this type, not of its subtypes.</typeparam>
        /// <param name="services">The services of the application under test, or of a scope of it.</param>
        /// <param name="predicate">Selects the events to redeliver.</param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>The number of events made pending.</returns>
        /// <remarks>
        /// Messages that are pending or parked are left as they are. The next failure of a redelivered message is
        /// reported by <see cref="DrainOutbox"/> again.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> or <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        public static async Task<int> Redeliver<TEvent>(
            this IServiceProvider services,
            Func<TEvent, bool> predicate,
            CancellationToken cancellationToken = default)
            where TEvent : IIntegrationEvent
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(predicate);

            string type = StoredName<TEvent>();
            OutboxDrainState? state = services.GetService<OutboxDrainState>();
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            int redelivered = 0;
            foreach (IOutboxStore store in scope.ServiceProvider.GetServices<IOutboxStore>())
            {
                foreach (OutboxMessage message in await store.GetByType(type, cancellationToken))
                {
                    if (message.ProcessedOnUtc is not null
                        && predicate(Deserialize<TEvent>(message))
                        && await store.Redeliver(message.Id, cancellationToken))
                    {
                        state?.Forget(message.Id);
                        redelivered++;
                    }
                }
            }

            return redelivered;
        }

        // The other half of the stored format: the IntegrationEventName, otherwise the full type name.
        private static string StoredName<TEvent>()
        {
            return typeof(TEvent).GetCustomAttribute<IntegrationEventNameAttribute>(inherit: false)?.Name ?? typeof(TEvent).FullName!;
        }

        private static TEvent Deserialize<TEvent>(OutboxMessage message)
        {
            return JsonSerializer.Deserialize<TEvent>(message.Content, JsonOptions)!;
        }
    }
}
