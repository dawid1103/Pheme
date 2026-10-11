namespace Pheme.EntityFrameworkCore.TestFixtures
{
    public record OrderSubmittedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, string OrderName) : IIntegrationEvent;

    public sealed record ExpressOrderSubmittedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, string OrderName, string Courier)
        : OrderSubmittedIntegrationEvent(EventId, OccurredOn, OrderName);

    [IntegrationEventName("shipping.parcel-sent")]
    public sealed record ParcelSentIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    /// <summary>
    /// An event no handler of the tests handles.
    /// </summary>
    public sealed record UnhandledIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
}
