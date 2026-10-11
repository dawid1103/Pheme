# Layers

`Pheme.Abstractions` is the abstraction: interfaces without dependencies. Wrapping it in an application's own
`ICommand` adds code, not protection. Keep each layer to what it needs instead, and guard that with an
architecture test:

| Layer | May use | Must not use |
|---|---|---|
| Domain | nothing from Pheme | `Pheme.*` |
| Contracts (shared kernel) | `Pheme.Abstractions`: `IIntegrationEvent`, commands one module sends another | the other packages; the store contracts of `Pheme.Abstractions`: `IOutboxStore`, `IOutboxMessageFactory`, `OutboxMessage`, `OutboxStats` |
| Application | `Pheme.Abstractions`: messages, handlers, `IDomainEventHandler<TEvent>`, `IIntegrationEventHandler<TEvent>`, `IOutboxWriter<TModule>`, `IPipelineBehavior<TMessage, TResponse>` | `Pheme`, `Pheme.FluentValidation`, `Pheme.EntityFrameworkCore`; the store contracts of `Pheme.Abstractions`: `IOutboxStore`, `IOutboxMessageFactory`, `OutboxMessage`, `OutboxStats` |
| Infrastructure | `Pheme.Abstractions`, `Pheme.EntityFrameworkCore`; `Pheme` to register the module's handlers (`AddPheme` with its assembly) | `Pheme.FluentValidation` |
| Composition root | everything: `AddPheme` with the pipeline and the outbox, behaviors, `ISender`, `IOutboxMaintenance` | - |

`PhemeLayerRule` of `Pheme.Testing` checks the rows of this table: `PhemeLayerRule.Domain`, `Contracts`, `Application`
and `Infrastructure`. A Pheme package a rule does not allow, `Pheme.Testing` or one added later, is forbidden. Each
check throws one `InvalidOperationException` listing every violation, with no test framework needed:

```csharp
using System.Reflection;
using Pheme;

public class LayerTests
{
    // Layers as assemblies: the Pheme assemblies each references, and the store contracts in its type signatures.
    public static TheoryData<string, PhemeLayerRule> Assemblies => new()
    {
        { "Shop.Domain", PhemeLayerRule.Domain },
        { "Shop.Contracts", PhemeLayerRule.Contracts },
        { "Shop.Application", PhemeLayerRule.Application },
        { "Shop.Infrastructure", PhemeLayerRule.Infrastructure },
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void An_assembly_keeps_the_rule_of_its_layer(string assembly, PhemeLayerRule rule) =>
        rule.Check(Assembly.Load(assembly));

    // Layers as folders of one assembly: the namespace, with the namespaces inside it.
    public static TheoryData<string, PhemeLayerRule> Namespaces => new()
    {
        { "Shipping.Domain", PhemeLayerRule.Domain },
        { "Shipping.Application", PhemeLayerRule.Application },
        { "Shipping.Infrastructure", PhemeLayerRule.Infrastructure },
    };

    [Theory]
    [MemberData(nameof(Namespaces))]
    public void A_namespace_keeps_the_rule_of_its_layer(string layer, PhemeLayerRule rule) =>
        rule.Check(typeof(Shipment).Assembly, layer);
}
```

An assembly is checked by the Pheme assemblies it references, which the compiler keeps only when the code uses them,
method bodies included. A namespace is checked by the signatures of its types: base types, interfaces, fields,
properties, events, parameters and return types, with their generic arguments. The store contracts are in
`Pheme.Abstractions`, which the contracts and application layers may reference, so both checks look for them in the
signatures. Method bodies are not part of a signature, but the types the compiler generates from them are checked,
such as the state machine of an async method with its local variables. A namespace without types fails the check, so a
renamed one does not pass with nothing checked. The example's tests are written this way
([`AssemblyLayerTests.cs`](https://github.com/dawid1103/Pheme/blob/master/tests/Pheme.Examples.UnitTests/AssemblyLayerTests.cs),
[`NamespaceLayerTests.cs`](https://github.com/dawid1103/Pheme/blob/master/tests/Pheme.Examples.UnitTests/NamespaceLayerTests.cs)).

---

Previous: [Operating the outbox](09-operating-the-outbox.md) · [Contents](../README.md) · Next: [Testing](11-testing.md)
