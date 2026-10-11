using FluentValidation;

namespace Pheme.TestFixtures
{
    // Messages may be structs: they are scanned, validated and sent like classes.
    public readonly record struct AppendEntry(string Text) : ICommand;

    public readonly record struct GetEntry(int Index) : IQuery<string>;

    internal sealed class StructMessageHandlers(Journal journal) : ICommandHandler<AppendEntry>, IQueryHandler<GetEntry, string>
    {
        public Task Handle(AppendEntry command, CancellationToken cancellationToken)
        {
            journal.Entries.Add(command.Text);
            return Task.CompletedTask;
        }

        public Task<string> Handle(GetEntry query, CancellationToken cancellationToken)
        {
            return Task.FromResult(journal.Entries[query.Index]);
        }
    }

    // Declared in the reverse order of their full type names: the scan registers them in name order.
    internal sealed class AppendEntryTextValidator : AbstractValidator<AppendEntry>
    {
        public AppendEntryTextValidator()
        {
            RuleFor(command => command.Text).NotEmpty();
        }
    }

    internal sealed class AppendEntryLengthValidator : AbstractValidator<AppendEntry>
    {
        public AppendEntryLengthValidator()
        {
            RuleFor(command => command.Text).MaximumLength(10);
        }
    }
}
