using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// Checks the outboxes of all contexts with <see cref="PhemeDbContextOptions.UseOutbox{TModule}"/>: the outbox of
    /// Pheme that creates and delivers their messages is added, and no module or source belongs to two contexts.
    /// </summary>
    /// <remarks>
    /// Runs on start, and without a host when a writer or a store is first resolved. It checks the registrations
    /// only: the model of a context is checked on first use, since checking it here would create the context.
    /// </remarks>
    internal sealed class OutboxValidation(IEnumerable<DbContextRegistration> contexts, IServiceProviderIsService services)
        : IValidateOptions<OutboxValidationOptions>
    {
        public ValidateOptionsResult Validate(string? name, OutboxValidationOptions options)
        {
            DbContextRegistration[] all = [.. contexts];
            var problems = new List<string>();

            if (!services.IsService(typeof(IOutboxMessageFactory)))
            {
                problems.AddRange(all.Select(context =>
                    $"{context.ContextType.Name} writes integration events to its outbox (UseOutbox in AddPhemeDbContext), "
                    + "but the outbox of Pheme is not added, so nothing would create or deliver them. Call "
                    + "options.AddOutbox() in services.AddPheme(...) in the composition root."));
            }

            foreach (IGrouping<Type, DbContextRegistration> module in all
                .SelectMany(context => context.Options.OutboxModules, (context, module) => (Context: context, Module: module))
                .GroupBy(pair => pair.Module, pair => pair.Context)
                .Where(group => group.Count() > 1))
            {
                problems.Add(
                    $"The module {module.Key.FullName} writes to the outboxes of {Names(module)} (UseOutbox<{module.Key.Name}>() "
                    + $"in their AddPhemeDbContext), but its IOutboxWriter<{module.Key.Name}> writes to one context only. "
                    + "Keep the call only for the context that saves the module's changes.");
            }

            foreach (IGrouping<string, DbContextRegistration> source in all
                .GroupBy(context => context.OutboxSource, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                problems.Add(
                    $"The outboxes of {Names(source)} have the same source '{source.Key}', but the source names one outbox "
                    + "in its messages, its delivery and its maintenance. Set PhemeDbContextOptions.OutboxSource in "
                    + "AddPhemeDbContext of all but one of them.");
            }

            return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
        }

        private static string Names(IEnumerable<DbContextRegistration> contexts)
        {
            return string.Join(", ", contexts.Select(context => context.ContextType.FullName));
        }
    }
}
