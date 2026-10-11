using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pheme.Outbox
{
    /// <summary>
    /// Runs the outbox in the background while the host runs: at once again after a full batch (without failures when
    /// <see cref="OutboxOptions.RetryDelay"/> is zero), otherwise after <see cref="OutboxOptions.PollingInterval"/>; deletes the processed messages older than
    /// <see cref="OutboxOptions.ProcessedRetention"/> on start and then every <see cref="RetentionInterval"/>, logging each deletion. Does
    /// nothing when <see cref="OutboxOptions.ProcessInBackground"/> is off.
    /// </summary>
    internal sealed class OutboxProcessor(
        IOutboxRunner runner,
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : BackgroundService
    {
        public static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            OutboxOptions outboxOptions = options.Value;
            if (!outboxOptions.ProcessInBackground)
            {
                return;
            }

            ILogger logger = loggerFactory.CreateLogger(OutboxOptions.CategoryName);
            DateTimeOffset nextRetention = timeProvider.GetUtcNow();
            while (true)
            {
                bool backlog = false;
                try
                {
                    DateTimeOffset now = timeProvider.GetUtcNow();
                    if (outboxOptions.ProcessedRetention is { } retention && now >= nextRetention)
                    {
                        nextRetention = now + RetentionInterval;
                        await DeleteProcessed(now - retention, nextRetention, logger, stoppingToken);
                    }

                    OutboxRunResult result = await runner.RunOnce(stoppingToken);

                    // A failed message waits for its retry delay. Without one it is at the head of its store's next
                    // batch, so the next run waits for the polling interval.
                    backlog = result.HasMore && (result.Failed.Count == 0 || outboxOptions.RetryDelay > TimeSpan.Zero);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // A store that cannot be read, for example while the database is down, is tried again later.
                    OutboxProcessorLog.RunFailed(logger, outboxOptions.PollingInterval, exception);
                }

                if (!backlog)
                {
                    try
                    {
                        await Task.Delay(outboxOptions.PollingInterval, timeProvider, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        // A store that fails does not stop the retention of the others, nor the delivery.
        private async Task DeleteProcessed(DateTimeOffset processedBefore, DateTimeOffset nextRun, ILogger logger, CancellationToken stoppingToken)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            foreach (IOutboxStore store in scope.ServiceProvider.GetServices<IOutboxStore>())
            {
                try
                {
                    int deleted = await store.DeleteProcessedBefore(processedBefore, stoppingToken);
                    OutboxProcessorLog.RetentionCompleted(logger, options.Value.RetentionLogLevel, deleted, processedBefore, store.Source, nextRun);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    OutboxProcessorLog.RetentionFailed(logger, store.Source, exception);
                }
            }
        }
    }
}
