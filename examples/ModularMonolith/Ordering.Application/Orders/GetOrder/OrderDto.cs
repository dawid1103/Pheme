namespace Ordering.Application.Orders.GetOrder
{
    /// <summary>
    /// An order as the API shows it.
    /// </summary>
    public sealed record OrderDto(Guid Id, string Product, int Quantity, string Status);
}
