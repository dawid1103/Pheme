namespace Shipping.Application
{
    /// <summary>
    /// The shipping module, as a marker: handlers write integration events through <c>IOutboxWriter&lt;ShippingModule&gt;</c>
    /// without seeing the <c>DbContext</c> whose outbox stores them.
    /// </summary>
    public sealed class ShippingModule;
}
