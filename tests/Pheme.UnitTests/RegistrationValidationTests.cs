using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pheme.Registration;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class RegistrationValidationTests
    {
        private static readonly Assembly[] SearchedAssemblies = [typeof(Ping).Assembly, typeof(RegistrationValidationTests).Assembly];

        [Fact]
        public void Messages_with_one_handler_each_have_no_problems()
        {
            IServiceCollection services = new ServiceCollection()
                .AddTransient<ICommandHandler<AddNumbers, int>, AddNumbersHandler>()
                .AddTransient<IQueryHandler<GetGreeting, string>, GetGreetingHandler>()
                .AddKeyedTransient<IQueryHandler<GetGreeting, string>, GetGreetingHandler>("keyed");

            IReadOnlyList<string> problems = HandlerRegistrationValidator.FindProblems(
                [typeof(AddNumbers), typeof(GetGreeting)], [], services, SearchedAssemblies);

            Assert.Empty(problems);
        }

        [Theory]
        [InlineData(typeof(UnhandledCommand), "Command", "Pheme.ICommandHandler<Pheme.UnitTests.UnhandledCommand>")]
        [InlineData(typeof(UnhandledCommandWithResponse), "Command", "Pheme.ICommandHandler<Pheme.UnitTests.UnhandledCommandWithResponse, System.Int32>")]
        [InlineData(typeof(UnhandledQuery), "Query", "Pheme.IQueryHandler<Pheme.UnitTests.UnhandledQuery, System.Int32>")]
        public void Message_without_handler_is_reported_with_the_expected_handler_and_the_searched_assemblies(
            Type messageType, string kind, string handlerInterface)
        {
            // A keyed handler is never resolved by the sender, so it does not count.
            IServiceCollection services = new ServiceCollection()
                .AddKeyedTransient<ICommandHandler<UnhandledCommand>>("keyed", (_, _) => null!);

            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([messageType], [], services, SearchedAssemblies));

            Assert.Equal(
                $"{kind} '{messageType.FullName}' has no handler. Implement '{handlerInterface}'. Pheme searched assemblies "
                + "'Pheme.TestFixtures', 'Pheme.UnitTests'; is the handler's assembly added with AddHandlersFromAssemblies?",
                problem);
        }

        [Fact]
        public void Message_with_several_handlers_is_reported_with_all_of_them()
        {
            IServiceCollection services = new ServiceCollection()
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>, FirstQueryWithTwoHandlersHandler>()
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>, SecondQueryWithTwoHandlersHandler>()
                .AddSingleton<IQueryHandler<QueryWithTwoHandlers, int>>(new FirstQueryWithTwoHandlersHandler())
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>>(_ => new SecondQueryWithTwoHandlersHandler());

            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([typeof(QueryWithTwoHandlers)], [], services, SearchedAssemblies));

            Assert.Equal(
                "Query 'Pheme.UnitTests.QueryWithTwoHandlers' has 4 handlers: 'Pheme.UnitTests.FirstQueryWithTwoHandlersHandler', "
                + "'Pheme.UnitTests.SecondQueryWithTwoHandlersHandler', 'Pheme.UnitTests.FirstQueryWithTwoHandlersHandler' (instance), "
                + "a factory delegate. A message has exactly one handler; remove the others. To swap a handler, for example "
                + "for a fake in tests, replace its registration with services.Replace(...), or remove it with "
                + "services.RemoveAll<Pheme.IQueryHandler<Pheme.UnitTests.QueryWithTwoHandlers, System.Int32>>() before adding "
                + "the other one.",
                problem);
        }

        [Fact]
        public void Scanned_handler_registered_again_by_hand_after_AddPheme_is_reported_as_one_class_registered_twice()
        {
            Type pingHandler = typeof(Ping).Assembly.GetType("Pheme.TestFixtures.PingHandler", throwOnError: true)!;
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddTransient(typeof(IQueryHandler<Ping, string>), pingHandler);

            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([], [], services, SearchedAssemblies));

            Assert.Equal(
                "Query 'Pheme.TestFixtures.Ping' has handler 'Pheme.TestFixtures.PingHandler' registered 2 times. AddPheme "
                + "already registers the handlers of its assembly, so a registration by hand is a second one; remove it, or "
                + "keep it before AddPheme to give the handler another lifetime.",
                problem);
        }

        [Fact]
        public void Handler_of_an_assembly_not_scanned_registered_twice_by_hand_is_reported_as_one_class()
        {
            IServiceCollection services = new ServiceCollection()
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>, FirstQueryWithTwoHandlersHandler>()
                .AddScoped<IQueryHandler<QueryWithTwoHandlers, int>, FirstQueryWithTwoHandlersHandler>();

            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([], [], services, [typeof(Ping).Assembly]));

            Assert.Equal(
                "Query 'Pheme.UnitTests.QueryWithTwoHandlers' has handler 'Pheme.UnitTests.FirstQueryWithTwoHandlersHandler' "
                + "registered 2 times. Remove the extra registrations.",
                problem);
        }

        [Fact]
        public void Several_handlers_are_reported_also_for_a_message_outside_the_searched_assemblies()
        {
            IServiceCollection services = new ServiceCollection()
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>, FirstQueryWithTwoHandlersHandler>()
                .AddTransient<IQueryHandler<QueryWithTwoHandlers, int>, SecondQueryWithTwoHandlersHandler>();

            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([], [], services, SearchedAssemblies));

            Assert.StartsWith("Query 'Pheme.UnitTests.QueryWithTwoHandlers' has 2 handlers: ", problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(typeof(CommandAndQuery), "'Pheme.ICommand', 'Pheme.IQuery<System.Int32>'")]
        [InlineData(typeof(QueryOfTwoResponses), "'Pheme.IQuery<System.Int32>', 'Pheme.IQuery<System.String>'")]
        public void Message_implementing_several_message_interfaces_is_reported_instead_of_its_handlers(Type messageType, string interfaces)
        {
            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([messageType], [], new ServiceCollection(), SearchedAssemblies));

            Assert.Equal(
                $"Message '{messageType.FullName}' implements {interfaces}. "
                + "A message implements exactly one of ICommand, ICommand<TResponse> and IQuery<TResponse>.",
                problem);
        }

        [Fact]
        public void Message_implementing_only_IMessage_is_reported()
        {
            string problem = Assert.Single(HandlerRegistrationValidator.FindProblems([typeof(PlainMessage)], [], new ServiceCollection(), SearchedAssemblies));

            Assert.Equal(
                "Message 'Pheme.UnitTests.PlainMessage' implements only IMessage. "
                + "Implement ICommand, ICommand<TResponse> or IQuery<TResponse>, not IMessage.",
                problem);
        }

        [Fact]
        public void Behavior_registered_as_a_pipeline_behavior_service_is_reported_with_a_hint_to_AddBehavior()
        {
            IServiceCollection services = new ServiceCollection()
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>))
                .AddTransient<IPipelineBehavior<LoggedQuery, int>, DoublingBehavior>()
                .AddKeyedTransient<IPipelineBehavior<LoggedQuery, int>>("keyed", (_, _) => new DoublingBehavior());

            IReadOnlyList<string> problems = HandlerRegistrationValidator.FindProblems([], [], services, SearchedAssemblies);

            Assert.Equal(
                [
                    "Service 'Pheme.IPipelineBehavior<TMessage, TResponse>' is registered with "
                    + "'Pheme.UnitTests.OuterBehavior<TMessage, TResponse>', but Pheme does not resolve pipeline behaviors as "
                    + "this service. Add behaviors with PhemeOptions.AddBehavior(typeof(Pheme.UnitTests.OuterBehavior<,>)).",
                    "Service 'Pheme.IPipelineBehavior<Pheme.UnitTests.LoggedQuery, System.Int32>' is registered with "
                    + "'Pheme.UnitTests.DoublingBehavior', but Pheme does not resolve pipeline behaviors as this service. Add "
                    + "behaviors with PhemeOptions.AddBehavior(typeof(Pheme.UnitTests.DoublingBehavior)).",
                    "Service 'Pheme.IPipelineBehavior<Pheme.UnitTests.LoggedQuery, System.Int32>' is registered with a factory "
                    + "delegate, but Pheme does not resolve pipeline behaviors as this service. Add behaviors with "
                    + "PhemeOptions.AddBehavior(typeof(MyBehavior<,>)).",
                ],
                problems);
        }

        [Fact]
        public void Domain_event_handlers_of_an_interface_or_abstract_event_type_are_reported_with_a_hint_to_an_open_generic_handler()
        {
            // An open generic handler, a keyed one and one of a concrete event can run, so they are not reported.
            IServiceCollection services = new ServiceCollection()
                .AddTransient<IDomainEventHandler<IAuditedDomainEvent>, AnyAuditedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<IAuditedDomainEvent>, AnyAuditedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<ParcelDomainEvent>>(_ => null!)
                .AddKeyedTransient<IDomainEventHandler<ParcelDomainEvent>>("keyed", (_, _) => null!)
                .AddTransient(typeof(IDomainEventHandler<>), typeof(AuditDomainEventHandler<>))
                .AddTransient<IDomainEventHandler<ShippedDomainEvent>, AuditDomainEventHandler<ShippedDomainEvent>>();

            IReadOnlyList<string> problems = HandlerRegistrationValidator.FindProblems([], [], services, SearchedAssemblies);

            Assert.Equal(
                [
                    "Domain event 'Pheme.UnitTests.IAuditedDomainEvent' is an interface, so its handlers 'Pheme.UnitTests.AnyAuditedDomainEventHandler' "
                    + "never run: Pheme publishes an event only to the handlers of its exact runtime type. Handle the concrete "
                    + "event types, or all of them with an open generic handler, "
                    + "MyDomainEventHandler<TEvent> : IDomainEventHandler<TEvent> where TEvent : Pheme.UnitTests.IAuditedDomainEvent, "
                    + "registered by hand with "
                    + "services.AddTransient(typeof(IDomainEventHandler<>), typeof(MyDomainEventHandler<>)).",
                    "Domain event 'Pheme.UnitTests.ParcelDomainEvent' is abstract, so its handlers a factory delegate never run: Pheme "
                    + "publishes an event only to the handlers of its exact runtime type. Handle the concrete event types, or "
                    + "all of them with an open generic handler, "
                    + "MyDomainEventHandler<TEvent> : IDomainEventHandler<TEvent> where TEvent : Pheme.UnitTests.ParcelDomainEvent, "
                    + "registered by hand with "
                    + "services.AddTransient(typeof(IDomainEventHandler<>), typeof(MyDomainEventHandler<>)).",
                ],
                problems);
        }

        [Fact]
        public void Behavior_registered_as_a_pipeline_behavior_service_fails_on_start()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>))
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.StartsWith("Pheme handler registrations have 1 problem:", exception.Message, StringComparison.Ordinal);
            Assert.Contains("PhemeOptions.AddBehavior(typeof(Pheme.UnitTests.OuterBehavior<,>))", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Valid_registrations_pass_validation_on_start_and_on_first_sender()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using IServiceScope scope = provider.CreateScope();

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISender>());
        }

        [Fact]
        public void Valid_registrations_are_validated_once_for_the_start_and_all_senders()
        {
            var counter = new CountingValidator();
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddSingleton<IValidateOptions<HandlerRegistrationOptions>>(counter)
                .BuildServiceProvider();

            provider.GetRequiredService<IStartupValidator>().Validate();
            for (int i = 0; i < 3; i++)
            {
                using IServiceScope scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<ISender>();
            }

            Assert.Equal(1, counter.Calls);
        }

        [Fact]
        public void Invalid_registrations_fail_on_start_with_all_problems_in_one_exception()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<RegistrationValidationTests>())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            AssertReportsAllProblemsOfTheTestAssembly(exception);
        }

        [Fact]
        public void Invalid_registrations_fail_every_resolution_of_the_sender_without_a_host()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<RegistrationValidationTests>())
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            OptionsValidationException first = Assert.Throws<OptionsValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>());
            OptionsValidationException second = Assert.Throws<OptionsValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>());

            AssertReportsAllProblemsOfTheTestAssembly(first);
            Assert.Equal(first.Message, second.Message);
        }

        [Fact]
        public void Validation_covers_registrations_changed_after_AddPheme()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddTransient<IQueryHandler<CountEntries, int>, OtherCountEntriesHandler>();
            services.RemoveAll<IQueryHandler<Ping, string>>();
            using ServiceProvider provider = services.BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.StartsWith("Pheme handler registrations have 2 problems:", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Query 'Pheme.TestFixtures.Ping' has no handler.", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Query 'Pheme.TestFixtures.CountEntries' has 2 handlers:", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Handler_replaced_by_hand_after_AddPheme_passes_validation()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            services.Replace(ServiceDescriptor.Transient<IQueryHandler<CountEntries, int>, OtherCountEntriesHandler>());
            using ServiceProvider provider = services.BuildServiceProvider();

            provider.GetRequiredService<IStartupValidator>().Validate();
        }

        /// <summary>
        /// The test assembly has invalid messages on purpose, a second handler for a query of the fixtures, a handler of an
        /// interface domain event, and integration event handlers, added without the outbox here.
        /// </summary>
        private static void AssertReportsAllProblemsOfTheTestAssembly(OptionsValidationException exception)
        {
            string[] lines = exception.Message.Split(Environment.NewLine);
            string[] expected =
            [
                "- Message 'Pheme.UnitTests.CommandAndQuery' implements ",
                "- Message 'Pheme.UnitTests.PlainMessage' implements only IMessage.",
                "- Message 'Pheme.UnitTests.QueryOfTwoResponses' implements ",
                "- Command 'Pheme.UnitTests.UnhandledCommand' has no handler.",
                "- Command 'Pheme.UnitTests.UnhandledCommandWithResponse' has no handler.",
                "- Query 'Pheme.UnitTests.UnhandledQuery' has no handler.",
                "- Query 'Pheme.TestFixtures.CountEntries' has 2 handlers: ",
                "- Query 'Pheme.UnitTests.QueryWithTwoHandlers' has 2 handlers: ",
                "- Domain event 'Pheme.UnitTests.IAuditedDomainEvent' is an interface, so its handlers 'Pheme.UnitTests.AnyAuditedDomainEventHandler' never run",
                "- Integration event handlers are registered ('Pheme.UnitTests.OrderIntegrationEventHandler'), but the outbox is not,",
            ];

            Assert.Equal($"Pheme handler registrations have {expected.Length} problems:", lines[0]);
            Assert.Equal(expected.Length, lines.Length - 1);
            Assert.All(expected, prefix => Assert.Contains(lines, line => line.StartsWith(prefix, StringComparison.Ordinal)));
        }
    }

    internal sealed class CountingValidator : IValidateOptions<HandlerRegistrationOptions>
    {
        public int Calls { get; private set; }

        public ValidateOptionsResult Validate(string? name, HandlerRegistrationOptions options)
        {
            Calls++;
            return ValidateOptionsResult.Skip;
        }
    }

    internal sealed record CommandAndQuery : ICommand, IQuery<int>;

    internal sealed record QueryOfTwoResponses : IQuery<int>, IQuery<string>;

    internal sealed record PlainMessage : IMessage;

    internal sealed record QueryWithTwoHandlers : IQuery<int>;

    internal sealed class FirstQueryWithTwoHandlersHandler : IQueryHandler<QueryWithTwoHandlers, int>
    {
        public Task<int> Handle(QueryWithTwoHandlers query, CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }
    }

    internal sealed class SecondQueryWithTwoHandlersHandler : IQueryHandler<QueryWithTwoHandlers, int>
    {
        public Task<int> Handle(QueryWithTwoHandlers query, CancellationToken cancellationToken)
        {
            return Task.FromResult(2);
        }
    }

    internal abstract record ParcelDomainEvent;

    /// <summary>
    /// Never runs: no event's runtime type is an interface. Scanned, so the test assembly fails validation also for it.
    /// </summary>
    internal sealed class AnyAuditedDomainEventHandler : IDomainEventHandler<IAuditedDomainEvent>
    {
        public Task Handle(IAuditedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class OtherCountEntriesHandler : IQueryHandler<CountEntries, int>
    {
        public Task<int> Handle(CountEntries query, CancellationToken cancellationToken)
        {
            return Task.FromResult(-1);
        }
    }
}
