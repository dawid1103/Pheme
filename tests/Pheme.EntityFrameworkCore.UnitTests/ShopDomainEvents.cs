namespace Pheme.EntityFrameworkCore.UnitTests
{
    public sealed record OrderPlacedDomainEvent(string Name);

    public sealed record OrderRejectedDomainEvent(string Reason);

    public sealed record OrderConfirmedDomainEvent(Order Order);

    public sealed record PingDomainEvent(Order Order);

    public sealed record ShipmentDispatchedDomainEvent(string Name);

    /// <summary>
    /// Handled by adding a note and saving the context that publishes the event, asynchronously or not.
    /// </summary>
    public sealed record NoteSavedDomainEvent(string Text, bool Synchronously = false);

    /// <summary>
    /// Handled by adding a note to another instance of the publishing context's type and saving it.
    /// </summary>
    public sealed record NoteSavedElsewhereDomainEvent(string Text);
}
