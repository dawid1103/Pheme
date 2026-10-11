namespace Pheme.TestFixtures
{
    /// <summary>
    /// One class handling several messages: registered under each handler interface.
    /// </summary>
    internal sealed class JournalHandlers(Journal journal)
        : ICommandHandler<WriteEntry>, ICommandHandler<WriteEntryAndCount, int>, IQueryHandler<CountEntries, int>
    {
        public Task Handle(WriteEntry command, CancellationToken cancellationToken)
        {
            journal.Entries.Add(command.Text);
            return Task.CompletedTask;
        }

        public Task<int> Handle(WriteEntryAndCount command, CancellationToken cancellationToken)
        {
            journal.Entries.Add(command.Text);
            return Task.FromResult(journal.Entries.Count);
        }

        public Task<int> Handle(CountEntries query, CancellationToken cancellationToken)
        {
            return Task.FromResult(journal.Entries.Count);
        }
    }

    internal sealed class PingHandler : IQueryHandler<Ping, string>
    {
        public Task<string> Handle(Ping query, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Pong: {query.Text}");
        }
    }

    internal static class NestedHandlers
    {
        private sealed class GetNestedAnswerHandler : IQueryHandler<GetNestedAnswer, int>
        {
            public Task<int> Handle(GetNestedAnswer query, CancellationToken cancellationToken)
            {
                return Task.FromResult(42);
            }
        }
    }

    /// <summary>
    /// Not a handler by itself: abstract classes are skipped, only the concrete subclass is registered.
    /// </summary>
    internal abstract class AnswerHandlerBase<TQuery> : IQueryHandler<TQuery, int>
        where TQuery : IQuery<int>
    {
        public Task<int> Handle(TQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(Answer);
        }

        protected abstract int Answer { get; }
    }

    internal sealed class GetGenericAnswerHandler : AnswerHandlerBase<GetGenericAnswer>
    {
        protected override int Answer => 7;
    }

    /// <summary>
    /// Open generic: cannot be registered for an unknown message type, so the scan skips it.
    /// </summary>
    internal sealed class OpenGenericHandler<TQuery> : IQueryHandler<TQuery, string>
        where TQuery : IQuery<string>
    {
        public Task<string> Handle(TQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
