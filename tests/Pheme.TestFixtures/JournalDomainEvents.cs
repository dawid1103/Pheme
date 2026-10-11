namespace Pheme.TestFixtures
{
    // Domain events are plain application types: no Pheme interface.
    public sealed record EntryAddedDomainEvent(string Text);

    public sealed record JournalClearedDomainEvent;

    public record NoteTakenDomainEvent(string Text);

    public sealed record UrgentNoteTakenDomainEvent(string Text) : NoteTakenDomainEvent(Text);
}
