namespace Pheme
{
    /// <summary>
    /// Gives an integration event type the stable name stored with its outbox messages, independent of the type's
    /// namespace, assembly and name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this attribute the stored name is the type's full name, so renaming or moving the type leaves the
    /// messages still waiting in the outbox with a name no type has. Set it before the first deployment: changing the
    /// name later has the same effect.
    /// </para>
    /// <para>
    /// The name must not be empty and must be unique among the integration events handled by the application; both are
    /// checked when the application starts, for the event types of all registered integration event handlers. A derived
    /// type does not inherit the name.
    /// </para>
    /// </remarks>
    /// <param name="name">The stable name, for example <c>ordering.order-placed</c>.</param>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class IntegrationEventNameAttribute(string name) : Attribute
    {
        /// <summary>
        /// Gets the stable name of the integration event type.
        /// </summary>
        public string Name { get; } = name;
    }
}
