namespace Ordering.Application.Orders.Confirmation
{
    /// <summary>
    /// Tells customers about their orders, by e-mail for example; implemented by the infrastructure.
    /// </summary>
    public interface ICustomerNotifier
    {
        /// <summary>
        /// Confirms a placed order to the customer.
        /// </summary>
        Task SendOrderConfirmation(Guid orderId, string product, int quantity, CancellationToken cancellationToken);
    }
}
