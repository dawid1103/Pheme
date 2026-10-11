namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// One command of the ordering module that changed its data: who sent it, when, and its content.
    /// </summary>
    public sealed class AuditEntry
    {
        private AuditEntry(string? user, string command, string content, DateTimeOffset occurredOnUtc)
        {
            User = user;
            Command = command;
            Content = content;
            OccurredOnUtc = occurredOnUtc;
        }

        /// <summary>
        /// The identifier of the entry.
        /// </summary>
        public Guid Id { get; private set; } = Guid.CreateVersion7();

        /// <summary>
        /// The name of the user who sent the command, or <see langword="null"/> for an anonymous one.
        /// </summary>
        public string? User { get; private set; }

        /// <summary>
        /// The name of the command's type.
        /// </summary>
        public string Command { get; private set; }

        /// <summary>
        /// The JSON of the command.
        /// </summary>
        public string Content { get; private set; }

        /// <summary>
        /// When the command was sent.
        /// </summary>
        public DateTimeOffset OccurredOnUtc { get; private set; }

        /// <summary>
        /// Records a command sent by <paramref name="user"/>.
        /// </summary>
        public static AuditEntry Record(string? user, string command, string content, DateTimeOffset occurredOnUtc) =>
            new(user, command, content, occurredOnUtc);
    }
}
