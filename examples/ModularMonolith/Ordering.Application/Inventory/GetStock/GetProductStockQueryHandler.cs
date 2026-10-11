using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Inventory;
using Pheme;
using Shop.Common.Errors;

namespace Ordering.Application.Inventory.GetStock
{
    internal sealed class GetProductStockQueryHandler(IOrderingDbContext db) : IQueryHandler<GetProductStockQuery, ProductStockDto>
    {
        public async Task<ProductStockDto> Handle(GetProductStockQuery query, CancellationToken cancellationToken)
        {
            ProductStock stock = await db.ProductStocks.AsNoTracking().SingleOrDefaultAsync(s => s.Product == query.Product, cancellationToken)
                ?? throw new NotFoundException($"Product {query.Product} has no stock.");

            return new ProductStockDto(stock.Product, stock.Available);
        }
    }
}
