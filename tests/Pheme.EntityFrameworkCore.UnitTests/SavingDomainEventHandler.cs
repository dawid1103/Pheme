using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// Handlers that save a context, as a handler written for publishing after the save may: the context of the scope, which publishes
    /// the event, or another instance built from the same options, so it shares the scope's interceptor.
    /// </summary>
    internal sealed class SavingDomainEventHandler(ShopDbContext db, DbContextOptions<ShopDbContext> options)
        : IDomainEventHandler<NoteSavedDomainEvent>, IDomainEventHandler<NoteSavedElsewhereDomainEvent>
    {
        public async Task Handle(NoteSavedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            db.Notes.Add(new Note(domainEvent.Text));
            if (domainEvent.Synchronously)
            {
                db.SaveChanges();
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task Handle(NoteSavedElsewhereDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            await using var other = new ShopDbContext(options);
            other.Notes.Add(new Note(domainEvent.Text));
            await other.SaveChangesAsync(cancellationToken);
        }
    }
}
