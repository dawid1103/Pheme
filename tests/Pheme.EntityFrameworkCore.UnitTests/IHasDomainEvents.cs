namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// The application's own contract of entities raising events, as a consumer would define it.
    /// </summary>
    public interface IHasDomainEvents
    {
        IReadOnlyCollection<object> DomainEvents { get; }

        void ClearDomainEvents();
    }
}
