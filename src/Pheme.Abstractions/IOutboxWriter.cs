namespace Pheme
{
    /// <summary>
    /// Adds integration events to the outbox of a module, in the same transaction as the change that raised them.
    /// </summary>
    /// <typeparam name="TModule">
    /// A marker type of the module, owned by its application layer, for example
    /// <c>public sealed class OrderingModule;</c>. It addresses the module's outbox without the application layer
    /// seeing the infrastructure, such as the module's <c>DbContext</c>.
    /// </typeparam>
    /// <remarks>
    /// The infrastructure registers a writer for each module, bound to the outbox of the module's database, for example
    /// with <c>UseOutbox&lt;TModule&gt;</c> of <c>AddPhemeDbContext</c> in <c>Pheme.EntityFrameworkCore</c>. Resolve it
    /// from the scope of the change, so the event is saved together with it.
    /// </remarks>
    public interface IOutboxWriter<TModule>
    {
        /// <summary>
        /// Adds an integration event to the outbox of the module, to be saved with the module's next save, in the same
        /// transaction as the other changes. Nothing is written until then; when the save fails or never happens, the
        /// event is never delivered.
        /// </summary>
        /// <param name="integrationEvent">
        /// The event to add, stored under the name of its runtime type and serialized with that type.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="integrationEvent"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The runtime type of <paramref name="integrationEvent"/> is generic, its
        /// <see cref="IntegrationEventNameAttribute"/> has an empty name, or no
        /// <see cref="IIntegrationEventHandler{TEvent}"/> of it is registered: the outbox delivers an event only to the
        /// handlers of its exact runtime type, and would park it.
        /// </exception>
        void Add(IIntegrationEvent integrationEvent);
    }
}
