using System.Reflection;
using Pheme.TestFixtures;

namespace Pheme.Testing.UnitTests
{
    /// <summary>
    /// The layer rules on the namespaces of <c>LayerFixtures.cs</c> and, for layers as assemblies, on the assemblies of
    /// Pheme and of the tests, whose references are known.
    /// </summary>
    public class PhemeLayerRuleTests
    {
        private static readonly Assembly Fixtures = typeof(PhemeLayerRuleTests).Assembly;

        public static TheoryData<PhemeLayerRule, string> ValidNamespaces => new()
        {
            { PhemeLayerRule.Domain, "Valid.Domain" },
            { PhemeLayerRule.Contracts, "Valid.Contracts" },
            { PhemeLayerRule.Application, "Valid.Application" },
            { PhemeLayerRule.Infrastructure, "Valid.Infrastructure" },
        };

        [Theory]
        [MemberData(nameof(ValidNamespaces))]
        public void A_namespace_keeping_its_rule_passes(PhemeLayerRule rule, string @namespace)
        {
            rule.Check(Fixtures, @namespace);
        }

        [Theory]
        [InlineData("Broken.Domain.BaseTypeViolation: base type uses Pheme.ICommand (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.InterfaceViolation: interface uses Pheme.ICommand (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.FieldViolation: field Sender uses Pheme.ISender (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.PropertyViolation: property Publisher uses Pheme.IDomainEventPublisher (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.EventViolation: event SenderChanged uses Pheme.ISender (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.ConstructorViolation: parameter sender of .ctor uses Pheme.ISender (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.ParameterViolation: parameter command of Handle uses Pheme.ICommand (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.Orders.ReturnTypeViolation: return type of Handle uses Pheme.Unit (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.OperatorViolation: parameter right of op_Addition uses Pheme.ICommand (Pheme.Abstractions)")]
        [InlineData("Broken.Domain.ArrayAndByRefViolation: parameter commands of Handle uses Pheme.ICommand (Pheme.Abstractions)")]
        public void The_domain_rule_finds_a_Pheme_type_in_each_part_of_a_signature(string violation)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Domain.Check(Fixtures, "Broken.Domain"));

            Assert.Contains($"{Environment.NewLine}- {violation}{Environment.NewLine}", failure.Message + Environment.NewLine, StringComparison.Ordinal);
        }

        [Fact]
        public void The_application_rule_finds_the_store_contracts_and_the_Pheme_assemblies_it_does_not_allow_all_at_once()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Application.Check(Fixtures, "Broken.Application"));

            Assert.Equal(
                Message(
                    "The Application layer, namespace 'Broken.Application' of assembly 'LayerRuleTests', breaks its rule in 6 "
                    + "place(s); it may use Pheme.Abstractions, but not IOutboxStore, IOutboxMessageFactory, OutboxMessage, OutboxStats:",
                    "Broken.Application.Registration: parameter options of Configure uses Pheme.PhemeOptions (Pheme)",
                    "Broken.Application.Registration: parameter rule of Configure uses Pheme.PhemeLayerRule (Pheme.Testing)",
                    "Broken.Application.StoreReader: field factory uses Pheme.IOutboxMessageFactory (Pheme.Abstractions)",
                    "Broken.Application.StoreReader: parameter store of .ctor uses Pheme.IOutboxStore (Pheme.Abstractions)",
                    "Broken.Application.StoreReader: property Stats uses Pheme.OutboxStats (Pheme.Abstractions)",
                    "Broken.Application.StoreReader: return type of Read uses Pheme.OutboxMessage (Pheme.Abstractions)"),
                failure.Message);
        }

        [Fact]
        public void The_state_machine_of_an_async_method_is_checked_with_its_local_variables()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Application.Check(Fixtures, "Broken.AsyncApplication"));

            // The compiler names the state machine and the field of the local variable.
            Assert.Contains(
                failure.Message.Split(Environment.NewLine),
                line => line.StartsWith("- Broken.AsyncApplication.AsyncReader+<Count>", StringComparison.Ordinal)
                    && line.EndsWith(" uses Pheme.IOutboxStore (Pheme.Abstractions)", StringComparison.Ordinal));
        }

        [Fact]
        public void The_contracts_rule_finds_a_store_contract()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Contracts.Check(Fixtures, "Broken.Contracts"));

            Assert.Contains(
                "Broken.Contracts.ShipmentPreparedIntegrationEvent: property Message uses Pheme.OutboxMessage (Pheme.Abstractions)",
                failure.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_infrastructure_rule_finds_the_Pheme_assemblies_it_does_not_allow_but_not_Pheme_or_the_store_contracts()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Infrastructure.Check(Fixtures, "Broken.Application"));

            Assert.Equal(
                Message(
                    "The Infrastructure layer, namespace 'Broken.Application' of assembly 'LayerRuleTests', breaks its rule in 1 "
                    + "place(s); it may use Pheme.Abstractions, Pheme and Pheme.EntityFrameworkCore:",
                    "Broken.Application.Registration: parameter rule of Configure uses Pheme.PhemeLayerRule (Pheme.Testing)"),
                failure.Message);
        }

        [Fact]
        public void A_namespace_without_types_is_rejected_as_nothing_would_be_checked()
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(
                "namespace",
                () => PhemeLayerRule.Domain.Check(Fixtures, "Valid.Domian"));

            Assert.StartsWith(
                "Assembly 'LayerRuleTests' has no types in namespace 'Valid.Domian', so the Domain rule would check nothing.",
                failure.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void An_assembly_keeping_its_rule_passes()
        {
            // References no Pheme assembly.
            PhemeLayerRule.Domain.Check(typeof(ICommand).Assembly);
            // References Pheme.Abstractions; messages and handlers.
            PhemeLayerRule.Contracts.Check(typeof(WriteEntry).Assembly);
            PhemeLayerRule.Application.Check(typeof(WriteEntry).Assembly);
            // References Pheme.Abstractions; implements the store contracts.
            PhemeLayerRule.Infrastructure.Check(typeof(PhemeDbContextOptions).Assembly);
        }

        [Fact]
        public void The_domain_rule_finds_a_referenced_Pheme_assembly()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Domain.Check(typeof(WriteEntry).Assembly));

            Assert.Equal(
                Message(
                    "The Domain layer, assembly 'Pheme.TestFixtures', breaks its rule in 1 place(s); it may use no Pheme assembly:",
                    "references Pheme.Abstractions"),
                failure.Message);
        }

        public static TheoryData<PhemeLayerRule> RulesWithoutStoreContracts => [PhemeLayerRule.Contracts, PhemeLayerRule.Application];

        [Theory]
        [MemberData(nameof(RulesWithoutStoreContracts))]
        public void A_rule_finds_the_store_contracts_in_the_signatures_of_an_assembly_referencing_only_allowed_ones(PhemeLayerRule rule)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => rule.Check(typeof(PhemeDbContextOptions).Assembly));

            Assert.DoesNotContain("references", failure.Message, StringComparison.Ordinal);
            Assert.Contains(": interface uses Pheme.IOutboxStore (Pheme.Abstractions)", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_infrastructure_rule_finds_every_Pheme_assembly_it_does_not_allow_including_ones_no_rule_knows()
        {
            // Pheme.TestFixtures stands for a Pheme package added later.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => PhemeLayerRule.Infrastructure.Check(Fixtures));

            Assert.Equal(
                Message(
                    "The Infrastructure layer, assembly 'LayerRuleTests', breaks its rule in 2 place(s); it may use "
                    + "Pheme.Abstractions, Pheme and Pheme.EntityFrameworkCore:",
                    "references Pheme.TestFixtures",
                    "references Pheme.Testing"),
                failure.Message);
        }

        [Fact]
        public void The_rules_are_named_after_their_layers()
        {
            Assert.Equal(
                ["Domain", "Contracts", "Application", "Infrastructure"],
                [.. new[] { PhemeLayerRule.Domain, PhemeLayerRule.Contracts, PhemeLayerRule.Application, PhemeLayerRule.Infrastructure }
                    .Select(rule => rule.ToString())]);
        }

        private static string Message(string header, params string[] violations) =>
            header + string.Concat(violations.Select(violation => $"{Environment.NewLine}- {violation}"));
    }
}
