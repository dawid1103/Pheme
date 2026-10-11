using Pheme;

// Layers as namespaces of this assembly, for PhemeLayerRuleTests: Valid.* keep their rules, Broken.* break them, one way
// per type. The namespaces are outside the project's own, so each check sees only the types written for it.

namespace Valid.Domain
{
    public interface IDomainEvent;

    public sealed record OrderPlacedDomainEvent(Guid OrderId) : IDomainEvent;
}

namespace Valid.Domain.Orders
{
    public sealed class Order
    {
        private readonly List<Valid.Domain.IDomainEvent> domainEvents = [];

        public Guid Id { get; } = Guid.CreateVersion7();

        public IReadOnlyList<Valid.Domain.IDomainEvent> DomainEvents => domainEvents;
    }
}

namespace Valid.DomainEvents
{
    // Next to Valid.Domain, not inside it: the Domain rule does not check it.
    public sealed class Publisher(ISender sender)
    {
        public ISender Sender { get; } = sender;
    }
}

namespace Valid.Contracts
{
    public sealed record OrderPlacedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;

    public sealed record CancelShipmentCommand(Guid OrderId) : ICommand;
}

namespace Valid.Application
{
    public sealed class OrderingModule;

    public sealed record PlaceOrderCommand(string Product) : ICommand<Guid>;

    public sealed class PlaceOrderCommandHandler(IOutboxWriter<OrderingModule> outbox) : ICommandHandler<PlaceOrderCommand, Guid>
    {
        public async Task<Guid> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
        {
            // An async method: its state machine is a type of the namespace too.
            var orderId = Guid.CreateVersion7();
            await Task.Yield();
            outbox.Add(new Valid.Contracts.OrderPlacedIntegrationEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow, orderId));
            return orderId;
        }
    }

    public sealed class PrepareShipmentIntegrationEventHandler : IIntegrationEventHandler<Valid.Contracts.OrderPlacedIntegrationEvent>
    {
        public Task Handle(Valid.Contracts.OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

namespace Valid.Infrastructure
{
    // The infrastructure may implement or use a store, and registers the module's DbContext and handlers.
    public sealed class OrderingRegistration(IOutboxStore store)
    {
        public IOutboxStore Store { get; } = store;

        public static void Configure(PhemeDbContextOptions options) => ArgumentNullException.ThrowIfNull(options);

        public static void AddHandlers(PhemeOptions options) => options.AddHandlersFromAssemblyContaining<OrderingRegistration>();
    }
}

namespace Broken.Domain
{
    public sealed class BaseTypeViolation : List<ICommand>;

    public sealed class InterfaceViolation : ICommand;

    public sealed class FieldViolation
    {
        public ISender? Sender;
    }

    public sealed class PropertyViolation
    {
        public IDomainEventPublisher? Publisher { get; set; }
    }

    public sealed class EventViolation
    {
        public event Action<ISender>? SenderChanged;

        public void Change(ISender sender) => SenderChanged?.Invoke(sender);
    }

    public sealed class ConstructorViolation(ISender sender)
    {
        public override string ToString() => sender.ToString()!;
    }

    public sealed class ParameterViolation
    {
        public static void Handle(ICommand command) => ArgumentNullException.ThrowIfNull(command);
    }

    public sealed class OperatorViolation
    {
        public static OperatorViolation operator +(OperatorViolation left, ICommand right) => left;
    }

    public sealed class ArrayAndByRefViolation
    {
        public static void Handle(ref ICommand[] commands) => ArgumentNullException.ThrowIfNull(commands);
    }
}

namespace Broken.Domain.Orders
{
    // Inside Broken.Domain: the Domain rule checks it.
    public sealed class ReturnTypeViolation
    {
        public static Task<Unit> Handle() => Task.FromResult(default(Unit));
    }
}

namespace Broken.Contracts
{
    public sealed record ShipmentPreparedIntegrationEvent(Guid OrderId, OutboxMessage Message) : IIntegrationEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }
}

namespace Broken.Application
{
    public sealed class StoreReader(IOutboxStore store)
    {
        private readonly IOutboxMessageFactory? factory = null;

        public OutboxStats? Stats { get; set; }

        public Task<IReadOnlyList<OutboxMessage>> Read(CancellationToken cancellationToken) =>
            store.GetByType(factory?.ToString() ?? "", cancellationToken);
    }

    public sealed class Registration
    {
        // Pheme, a package of the infrastructure and the composition root, and Pheme.Testing, a package no rule allows.
        public static void Configure(PhemeOptions options, PhemeLayerRule rule)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(rule);
        }
    }
}

namespace Broken.AsyncApplication
{
    public sealed class AsyncReader
    {
        // A local variable alive across an await is a field of the state machine the compiler generates.
        public static async Task<int> Count(IServiceProvider services)
        {
            var store = (IOutboxStore)services.GetService(typeof(IOutboxStore))!;
            await Task.Yield();
            return (await store.GetByType("ordering.order-placed", CancellationToken.None)).Count;
        }
    }
}
