namespace Pheme.TestFixtures
{
    // Messages are public, as application contracts usually are; their handlers are internal.
    public sealed record WriteEntry(string Text) : ICommand;

    public sealed record WriteEntryAndCount(string Text) : ICommand<int>;

    public sealed record CountEntries : IQuery<int>;

    public sealed record Ping(string Text) : IQuery<string>;

    public sealed record GetNestedAnswer : IQuery<int>;

    public sealed record GetGenericAnswer : IQuery<int>;
}
