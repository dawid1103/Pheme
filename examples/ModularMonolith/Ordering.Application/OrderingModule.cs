namespace Ordering.Application
{
    /// <summary>
    /// The ordering module, as a marker: handlers write integration events through <c>IOutboxWriter&lt;OrderingModule&gt;</c>
    /// without seeing the <c>DbContext</c> whose outbox stores them.
    /// </summary>
    public sealed class OrderingModule;
}
