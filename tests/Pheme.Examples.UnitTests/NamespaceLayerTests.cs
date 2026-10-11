using Shipping.Domain;

namespace Pheme.Examples.UnitTests
{
    /// <summary>
    /// Layer boundaries of a module whose layers are folders of one assembly (shipping), with
    /// <see cref="PhemeLayerRule"/> of <c>Pheme.Testing</c>: the signatures of the types of each layer's namespace against
    /// the rule of the layer. Needs no database.
    /// </summary>
    public class NamespaceLayerTests
    {
        public static TheoryData<string, PhemeLayerRule> Layers => new()
        {
            { "Shipping.Domain", PhemeLayerRule.Domain },
            { "Shipping.Application", PhemeLayerRule.Application },
            { "Shipping.Infrastructure", PhemeLayerRule.Infrastructure },
        };

        [Theory]
        [MemberData(nameof(Layers))]
        public void The_types_of_a_layer_keep_its_rule(string layer, PhemeLayerRule rule)
        {
            rule.Check(typeof(Shipment).Assembly, layer);
        }

        [Fact]
        public void The_rule_detects_a_layer_breaking_it()
        {
            // The handlers of the application layer implement Pheme's interfaces, which a domain layer must not use.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Domain.Check(typeof(Shipment).Assembly, "Shipping.Application"));

            Assert.Contains(
                "Shipping.Application.PrepareShipment.PrepareShipmentIntegrationEventHandler: interface uses Pheme.IIntegrationEventHandler`1",
                failure.Message,
                StringComparison.Ordinal);
        }
    }
}
