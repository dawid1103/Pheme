# Getting started

One feature end to end, from the [example application](https://github.com/dawid1103/Pheme/blob/master/examples/README.md),
a small shop: placing an order is a command, reading it a query, and the order's history and stock react to the order
through domain events. Each snippet links to its file; some are shortened, and the command leaves out the interfaces
the [recipes](04-pipeline-behaviors.md) add to it. The packages each layer references are in
[Installation](01-installation.md).

## How it fits together

```
POST /orders
  │
  ▼
OrdersController ── ISender.Send(new PlaceOrderCommand(...)) ─────────────────── API (composition root)
  │
  ▼
pipeline:  request logging → your behaviors → validation (PlaceOrderCommandValidator) → your behaviors
  │
  ▼
PlaceOrderCommandHandler ──────────────────────────────────────────────────────── Application
  │   Order.Place(...)            the entity records OrderPlacedDomainEvent ───── Domain
  │   db.Orders.Add(order)
  │   await db.SaveChangesAsync()
  ▼
Pheme interceptor, before anything is written ─────────────────────────────────── Infrastructure
  │   OrderPlacedDomainEvent → RecordOrderHistoryDomainEventHandler,
  │                            WriteOrderPlacedToOutboxDomainEventHandler (outbox),
  │                            AllocateStockDomainEventHandler            (same DbContext)
  ▼
one transaction: the order + the handlers' changes  →  201 Created
```

A handler that throws, in the pipeline or among the event handlers, stops everything: nothing is saved and the
exception reaches the controller unchanged.

## Naming

Naming is up to you: Pheme reads no names. The example's convention puts the kind of a type in its suffix -
`PlaceOrderCommand`, `GetOrderQuery`, `OrderPlacedDomainEvent`, `OrderPlacedIntegrationEvent`, and handlers ending in
`CommandHandler`, `QueryHandler`, `DomainEventHandler` or `IntegrationEventHandler` - so a file list, a span name or
the handlers recorded with an outbox message tell a domain event from an integration event. Events are named in the
past tense and kept next to their aggregate, in an `Events` folder. An event handler is named after what it does, not
after its event, as an event may have several handlers and one class may handle several events; the event is its type
argument: `AllocateStockDomainEventHandler : IDomainEventHandler<OrderPlacedDomainEvent>`.

## CQRS in five steps

**1. A command and its handler** (Application). A command changes state and returns nothing, or a light result such
as an id
([`PlaceOrder`](https://github.com/dawid1103/Pheme/tree/master/examples/ModularMonolith/Ordering.Application/Orders/PlaceOrder)):

```csharp
public sealed record PlaceOrderCommand(string Product, int Quantity, bool Express = false) : ICommand<Guid>;

internal sealed class PlaceOrderCommandHandler(IOrderingDbContext db) : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Guid> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        Order order = Order.Place(command.Product, command.Quantity, command.Express);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}
```

A command without a result implements `ICommand` and its handler `ICommandHandler<TCommand>`, returning `Task`.
Handlers may be `internal`; nothing registers them by hand.

**2. Its validator** (Application, optional), an ordinary FluentValidation validator, found like the handlers
([`PlaceOrderCommandValidator.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Orders/PlaceOrder/PlaceOrderCommandValidator.cs)):

```csharp
internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(command => command.Product).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Quantity).InclusiveBetween(1, 100);
    }
}
```

**3. A query and its handler** (Application). A query reads state
([`GetOrder`](https://github.com/dawid1103/Pheme/tree/master/examples/ModularMonolith/Ordering.Application/Orders/GetOrder)):

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

internal sealed class GetOrderQueryHandler(IOrderingDbContext db) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        Order order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken)
            ?? throw new NotFoundException($"Order {query.OrderId} does not exist.");

        return new OrderDto(order.Id, order.Product, order.Quantity, order.Status.ToString());
    }
}
```

**4. Register Pheme** (API) with the pipeline
([`Program.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Program.cs)).
The smallest working setup is one line with the assembly holding the handlers; the rest is optional:

```csharp
builder.Services.AddPheme(options =>
{
    // The assemblies holding the handlers (and validators). In the example each module adds its own (step 8), and
    // this call the contracts the modules share.
    options.AddHandlersFromAssemblyContaining<OrderPlacedIntegrationEvent>();

    // Optional: one log per message with its outcome and time; validation and domain exceptions are not errors.
    options.AddRequestLogging(logging => logging.IsExpected = exception =>
        exception is ValidationException or ForbiddenException or NotFoundException or BusinessRuleException
            || ApiExceptionFilter.IsConflict(exception));

    // Optional: the application's own behaviors, in order, outermost first.
    options.AddBehavior(typeof(AuthorizationBehavior<,>));
    options.AddBehavior(typeof(CommandTimingBehavior<>));
    options.AddBehavior(typeof(CommandTimingBehavior<,>));

    // Optional (Pheme.FluentValidation): run the validators; failures become the application's exception.
    options.AddFluentValidation(failures => new ValidationException(failures
        .GroupBy(failure => failure.PropertyName)
        .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray())));

    // Optional: more of the application's behaviors, innermost last (see "Pipeline behaviors" and its recipes).
    options.AddBehavior(typeof(QueryCachingBehavior<,>));
    options.AddBehavior(typeof(IdempotencyBehavior<,>));
    options.AddBehavior(typeof(AuditBehavior<,>));
    options.AddBehavior(typeof(TransactionBehavior<>));
});
```

A message without a handler, or with two, fails the start of the application with a list of all such problems.

**5. Send** through `ISender` (API)
([`OrdersController.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Controllers/OrdersController.cs)):

```csharp
public sealed class OrdersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        Guid id = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }
}
```

That is CQRS done. Domain events add four pieces.

## Domain events in four more steps

**6. The entity records its events** (Domain). The contract is the application's own, an interface or a base class;
the domain does not reference Pheme
([`IHasDomainEvents.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Domain/IHasDomainEvents.cs),
[`Order.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Domain/Orders/Order.cs)):

```csharp
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

public sealed record OrderPlacedDomainEvent(Guid OrderId, string Product, int Quantity, bool Express) : IDomainEvent;

public sealed class Order : IHasDomainEvents
{
    private readonly List<IDomainEvent> domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

    public static Order Place(string product, int quantity, bool express)
    {
        var order = new Order(Guid.CreateVersion7(), product, quantity, express) { Status = OrderStatus.Placed };
        order.domainEvents.Add(new OrderPlacedDomainEvent(order.Id, product, quantity, express));
        return order;
    }

    public void ClearDomainEvents() => domainEvents.Clear();
}
```

`IDomainEvent` is the domain's own marker too; Pheme needs none.

**7. A handler reacts** (Application). It changes the same `DbContext` and does not save: the save that published the
event saves its changes too
([`AllocateStockDomainEventHandler.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Inventory/AllocateStockDomainEventHandler.cs)):

```csharp
internal sealed class AllocateStockDomainEventHandler(IOrderingDbContext db)
    : IDomainEventHandler<OrderPlacedDomainEvent>
{
    public async Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ProductStock? stock = await db.ProductStocks.SingleOrDefaultAsync(s => s.Product == domainEvent.Product, cancellationToken);
        if (stock is null)
        {
            return;
        }

        if (!stock.CanAllocate(domainEvent.Quantity))
        {
            throw new BusinessRuleException(
                $"Only {stock.Available} items of {stock.Product} are available, not {domainEvent.Quantity}.");
        }

        stock.Allocate(domainEvent.Quantity);
        await db.Record(domainEvent.OrderId, $"Allocated {domainEvent.Quantity} x {domainEvent.Product}", cancellationToken);
    }
}
```

An event may have any number of handlers; `AddPheme` finds them like the other handlers.

**8. The module registers its handlers, and the `DbContext` publishes on save** (Infrastructure): add the module's
assembly, name the `DbContext` and tell Pheme how to read the entities' events
([`OrderingServiceCollectionExtensions.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Infrastructure/OrderingServiceCollectionExtensions.cs)):

```csharp
services.AddPheme(pheme => pheme.AddHandlersFromAssemblyContaining<OrderingModule>());
services.AddDbContext<OrderingDbContext>(options => options.UseNpgsql(connectionString));
services.AddPhemeDbContext<OrderingDbContext>(pheme =>
    pheme.CollectDomainEventsFrom<IHasDomainEvents>(entity => entity.DomainEvents, entity => entity.ClearDomainEvents()));
```

The `DbContext` stays a plain `DbContext`, without a base class. The calls of `AddPheme` in the modules and in the
composition root share one registration ("Modules" in [Commands and queries](03-commands-and-queries.md)).

**9. Wire the module in** (API): the module's registration, then `AddPheme` from step 4:

```csharp
builder.Services.AddOrdering(connectionString);   // AddPheme with its assembly, AddDbContext, AddPhemeDbContext, ...
builder.Services.AddPheme(options => ...);
```

Now `POST /orders` saves the order, its history and the allocated stock in one transaction, or nothing when the stock
is short. Details: [Commands and queries](03-commands-and-queries.md), [Domain events](07-domain-events.md),
[Publishing on save](07-domain-events.md#publishing-on-save).

---

Previous: [Installation](01-installation.md) · [Contents](../README.md) · Next: [Commands and queries](03-commands-and-queries.md)
