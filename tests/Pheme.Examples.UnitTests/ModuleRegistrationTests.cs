using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ordering.Infrastructure;
using Shop.Common.Contracts;

namespace Pheme.Examples.UnitTests
{
    /// <summary>
    /// A module left out of the composition root, as <c>Program</c> registers the shop: the integration events only it
    /// handles fail the start, not the command that writes them. Needs no database.
    /// </summary>
    public class ModuleRegistrationTests
    {
        [Fact]
        public void The_shop_without_the_shipping_module_fails_on_start_with_the_events_left_without_a_handler()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddOrdering("Host=localhost")
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<OrderPlacedIntegrationEvent>().AddOutbox())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "Pheme handler registrations have 1 problem:" + Environment.NewLine
                + "- Integration event 'Shop.Common.Contracts.OrderCancelledIntegrationEvent' has no handler, so the outbox "
                + "would park it. Implement 'IIntegrationEventHandler<Shop.Common.Contracts.OrderCancelledIntegrationEvent>'. "
                + "Pheme searched assemblies 'Ordering.Application', 'Shop.Common'; is the handler's assembly added with "
                + "AddHandlersFromAssemblies?",
                exception.Message);
        }
    }
}
