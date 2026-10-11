using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pheme.Diagnostics;

namespace Pheme.Outbox
{
    /// <summary>
    /// Reads the due messages of every outbox store and delivers each, every handler in a scope of its own, one run at a
    /// time per service provider.
    /// </summary>
    internal sealed class OutboxRunner : IOutboxRunner, IDisposable
    {
        // Type names never contain a line break, while generic ones contain ", ".
        private const char HandlerSeparator = '\n';

        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly IServiceScopeFactory scopeFactory;
        private readonly IntegrationEventDispatcher dispatcher;
        private readonly MessageInstrumentation instrumentation;
        private readonly OutboxOptions options;
        private readonly TimeProvider timeProvider;
        private readonly ILogger logger;

        public OutboxRunner(
            IServiceScopeFactory scopeFactory,
            IntegrationEventDispatcher dispatcher,
            MessageInstrumentation instrumentation,
            IOptions<OutboxOptions> options,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory)
        {
            this.scopeFactory = scopeFactory;
            this.dispatcher = dispatcher;
            this.instrumentation = instrumentation;
            this.options = options.Value;
            this.timeProvider = timeProvider;
            logger = loggerFactory.CreateLogger(OutboxOptions.CategoryName);

            // Here, because the runner is created once per provider and always with the processor.
            OutboxMetrics.Observe(instrumentation.Meter, scopeFactory, this.options, timeProvider);
        }

        public async Task<OutboxRunResult> RunOnce(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await Run(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public void Dispose()
        {
            gate.Dispose();
        }

        private async Task<OutboxRunResult> Run(CancellationToken cancellationToken)
        {
            // Read in a short scope of their own, which no delivery shares.
            var batches = new List<(string Source, IReadOnlyList<OutboxMessage> Messages)>();
            await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                foreach (IOutboxStore store in scope.ServiceProvider.GetServices<IOutboxStore>())
                {
                    batches.Add((store.Source, await store.GetPending(options.BatchSize, options.MaxAttempts, now, cancellationToken)));
                }
            }

            int processed = 0;
            var failed = new List<OutboxMessage>();
            foreach ((string source, IReadOnlyList<OutboxMessage> messages) in batches)
            {
                foreach (OutboxMessage message in messages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await Deliver(source, message, cancellationToken))
                    {
                        processed++;
                    }
                    else
                    {
                        failed.Add(message);
                    }
                }
            }

            return new OutboxRunResult(processed, failed, batches.Any(batch => batch.Messages.Count >= options.BatchSize));
        }

        /// <summary>
        /// Delivers one message and saves the outcome; returns whether all its handlers have completed.
        /// </summary>
        private async Task<bool> Deliver(string source, OutboxMessage message, CancellationToken cancellationToken)
        {
            IntegrationEventDispatchResult result;
            MessageMeasurement measurement = instrumentation.StartIntegrationEvent(message.Type, source, message.TraceParent);
            try
            {
                string[] completedHandlers = message.CompletedHandlers?.Split(HandlerSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
                result = await dispatcher.Dispatch(message.Type, message.Content, completedHandlers, cancellationToken);
            }
            catch (Exception exception)
            {
                measurement.Stop(exception);
                throw;
            }

            Exception? error = result.Error is AggregateException { InnerExceptions.Count: 1 } aggregate
                ? aggregate.InnerExceptions[0]
                : result.Error;
            measurement.Stop(error);

            // A handler failing on shutdown most likely failed because of it, so the attempt does not count.
            if (error is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // A scope of its own, as each handler has, so whatever a failed handler left in its scope, such as tracked
            // entities, is not saved with the outcome. Saved also on shutdown: the handlers have done their work.
            await using AsyncServiceScope statusScope = scopeFactory.CreateAsyncScope();
            IOutboxStore store = statusScope.ServiceProvider.GetServices<IOutboxStore>().First(candidate => candidate.Source == source);
            if (error is null)
            {
                await store.MarkAsProcessed(message.Id, CancellationToken.None);
                return true;
            }

            // The delay grows with the attempts of the message: an outage of what its handlers call is outlasted without
            // retrying in a tight loop, and a message failing for good does not slow the others down.
            bool parked = message.AttemptCount + 1 >= options.MaxAttempts;
            TimeSpan retryDelay = options.GetRetryDelay(message.AttemptCount + 1);
            message.Error = error.ToString();
            message.CompletedHandlers = result.CompletedHandlers.Count == 0 ? null : string.Join(HandlerSeparator, result.CompletedHandlers);
            message.NextAttemptOnUtc = parked ? null : timeProvider.GetUtcNow() + retryDelay;
            message.AttemptCount = await store.MarkAsFailed(
                message.Id, message.Error, message.CompletedHandlers, message.NextAttemptOnUtc, CancellationToken.None);
            if (message.AttemptCount >= options.MaxAttempts)
            {
                OutboxProcessorLog.Parked(logger, message.Type, message.Id, source, message.AttemptCount, error);
            }
            else
            {
                OutboxProcessorLog.Failed(logger, message.Type, message.Id, source, message.AttemptCount, options.MaxAttempts, retryDelay, error);
            }

            return false;
        }
    }
}
