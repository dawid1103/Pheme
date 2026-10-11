namespace Ordering.Application.Inventory.GetStock
{
    /// <summary>
    /// The stock of a product as the API shows it.
    /// </summary>
    public sealed record ProductStockDto(string Product, int Available);
}
