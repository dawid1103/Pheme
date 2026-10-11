namespace Pheme.TestFixtures
{
    /// <summary>
    /// One class handling several events. Declared before <see cref="AuditEntryDomainEventHandler"/>, but registered
    /// after it: the scan registers domain event handlers in the order of their full type names.
    /// </summary>
    internal sealed class JournalDomainEventHandler(Journal journal) : IDomainEventHandler<EntryAddedDomainEvent>, IDomainEventHandler<JournalClearedDomainEvent>
    {
        public Task Handle(EntryAddedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add($"journal: {domainEvent.Text}");
            return Task.CompletedTask;
        }

        public Task Handle(JournalClearedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("journal: cleared");
            return Task.CompletedTask;
        }
    }

    internal sealed class AuditEntryDomainEventHandler(Journal journal) : IDomainEventHandler<EntryAddedDomainEvent>
    {
        public Task Handle(EntryAddedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add($"audit: {domainEvent.Text}");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Handles <see cref="NoteTakenDomainEvent"/> only: dispatch is by exact type, so
    /// <see cref="UrgentNoteTakenDomainEvent"/> does not reach it.
    /// </summary>
    internal sealed class NoteTakenDomainEventHandler(Journal journal) : IDomainEventHandler<NoteTakenDomainEvent>
    {
        public Task Handle(NoteTakenDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add($"note: {domainEvent.Text}");
            return Task.CompletedTask;
        }
    }
}
