using FluentValidation;

namespace Pheme.TestFixtures
{
    internal sealed class WriteEntryValidator : AbstractValidator<WriteEntry>
    {
        public WriteEntryValidator()
        {
            RuleFor(command => command.Text).NotEmpty();
        }
    }
}
