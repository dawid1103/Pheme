using Microsoft.EntityFrameworkCore;

namespace Pheme
{
    /// <summary>
    /// The target of the startup validation of the domain event dispatch of <typeparamref name="TContext"/>,
    /// <see cref="DomainEventDispatchValidation{TContext}"/>; it holds nothing.
    /// </summary>
    internal sealed class DomainEventDispatchValidationOptions<TContext>
        where TContext : DbContext;
}
