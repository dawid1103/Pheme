using System.Reflection;

namespace Pheme.Examples.UnitTests
{
    /// <summary>
    /// Layer boundaries of a module whose layers are assemblies (ordering), with <see cref="PhemeLayerRule"/> of
    /// <c>Pheme.Testing</c>: the Pheme assemblies each assembly references, and the types of its signatures, against the
    /// rule of its layer. Needs no database.
    /// </summary>
    public class AssemblyLayerTests
    {
        public static TheoryData<string, PhemeLayerRule> Layers => new()
        {
            { "Ordering.Domain", PhemeLayerRule.Domain },
            { "Ordering.Application", PhemeLayerRule.Application },
            { "Ordering.Infrastructure", PhemeLayerRule.Infrastructure },
            // Shared kernel: the contracts between the modules, integration events, are messages.
            { "Shop.Common", PhemeLayerRule.Contracts },
        };

        [Theory]
        [MemberData(nameof(Layers))]
        public void An_assembly_keeps_the_rule_of_its_layer(string assembly, PhemeLayerRule rule)
        {
            rule.Check(Assembly.Load(assembly));
        }

        [Fact]
        public void The_rule_detects_an_assembly_breaking_it()
        {
            // The composition root registers Pheme, which an application layer must not do.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Application.Check(typeof(Program).Assembly));

            Assert.Contains($"{Environment.NewLine}- references Pheme{Environment.NewLine}", failure.Message, StringComparison.Ordinal);
            Assert.Contains($"{Environment.NewLine}- references Pheme.FluentValidation", failure.Message, StringComparison.Ordinal);
        }
    }
}
