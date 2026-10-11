using System.Diagnostics;
using System.Globalization;
using Pheme;

namespace Shop.Api.Behaviors
{
    /// <summary>
    /// Adds the time a command took to the HTTP response as a <c>Server-Timing</c> metric. Pheme has two kinds of
    /// commands, so the behavior comes in two classes, <see cref="CommandTimingBehavior{TCommand}"/> and
    /// <see cref="CommandTimingBehavior{TCommand, TResponse}"/>; queries get neither.
    /// </summary>
    internal static class CommandTiming
    {
        public const string HeaderName = "Server-Timing";

        public static async Task<TResponse> Measure<TResponse>(
            IHttpContextAccessor httpContextAccessor,
            Type commandType,
            MessageHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            TResponse response = await next(cancellationToken);

            // A command sent by another command's handler adds a metric of its own.
            HttpResponse? httpResponse = httpContextAccessor.HttpContext?.Response;
            if (httpResponse is { HasStarted: false })
            {
                string duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
                httpResponse.Headers.Append(HeaderName, $"command;desc=\"{commandType.Name}\";dur={duration}");
            }

            return response;
        }
    }
}
