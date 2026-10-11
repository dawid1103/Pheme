using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Runs an action once, right before the next save of a <see cref="DbContext"/> of type
    /// <typeparamref name="TContext"/> writes: after the handler read its data and after the domain events of the save
    /// are published, so the action is another request changing the same rows in between, at a set point instead of by
    /// chance.
    /// </summary>
    /// <remarks>
    /// Registered after Pheme's interceptor, so it runs after the events are published. The action is taken before it
    /// runs, so a save inside it, of the other request, runs as usual.
    /// </remarks>
    public sealed class BeforeNextSaveInterceptor<TContext> : SaveChangesInterceptor
        where TContext : DbContext
    {
        private Func<Task>? action;

        /// <summary>
        /// Runs <paramref name="next"/> before the next save of a <typeparamref name="TContext"/>.
        /// </summary>
        public void Run(Func<Task> next) => action = next;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is TContext && Interlocked.Exchange(ref action, null) is { } next)
            {
                await next();
            }

            return result;
        }
    }
}
