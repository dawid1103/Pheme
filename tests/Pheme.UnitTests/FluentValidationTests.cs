using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class FluentValidationTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Message_without_validators_reaches_the_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddFluentValidation()));

            int count = await Send(provider, new CountOrders());

            Assert.Equal(3, count);
            Assert.Equal(["handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Valid_message_passes_its_validators_and_reaches_the_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddTransient<IValidator<PlaceOrder>, QuantityValidator>()
                .AddPheme(options => options.AddFluentValidation()));

            await Send(provider, new PlaceOrder("Ada", 2));

            Assert.Equal(["handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Failing_validator_throws_validation_exception_before_the_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddPheme(options => options.AddFluentValidation()));

            ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
                () => Send(provider, new PlaceOrder(string.Empty, 2)));

            ValidationFailure failure = Assert.Single(exception.Errors);
            Assert.Equal(nameof(PlaceOrder.Customer), failure.PropertyName);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Failures_of_all_validators_are_thrown_together()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddTransient<IValidator<PlaceOrder>, QuantityValidator>()
                .AddPheme(options => options.AddFluentValidation()));

            ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
                () => Send(provider, new PlaceOrder(string.Empty, 0)));

            Assert.Equal(
                [nameof(PlaceOrder.Customer), nameof(PlaceOrder.Quantity)],
                exception.Errors.Select(failure => failure.PropertyName));
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Validators_run_one_after_another()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<CheckStock>, FirstStockValidator>()
                .AddTransient<IValidator<CheckStock>, SecondStockValidator>()
                .AddPheme(options => options.AddFluentValidation()));

            int available = await Send(provider, new CheckStock(5));

            Assert.Equal(5, available);
            Assert.Equal(
                ["first start", "first end", "second start", "second end", "handler"],
                provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Exception_factory_makes_the_thrown_exception()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddTransient<IValidator<PlaceOrder>, QuantityValidator>()
                .AddPheme(options => options.AddFluentValidation(failures => new OrderRejectedException(failures))));

            OrderRejectedException exception = await Assert.ThrowsAsync<OrderRejectedException>(
                () => Send(provider, new PlaceOrder(string.Empty, 0)));

            Assert.Equal(2, exception.Failures.Count);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Exception_factory_returning_null_throws_invalid_operation_exception()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddPheme(options => options.AddFluentValidation(_ => null!)));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(provider, new PlaceOrder(string.Empty, 2)));

            Assert.Contains("returned null", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Validation_runs_at_its_position_in_the_pipeline()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddPheme(options => options
                    .AddBehavior(typeof(OuterBehavior<,>))
                    .AddFluentValidation()
                    .AddBehavior(typeof(InnerBehavior<,>))));

            await Assert.ThrowsAsync<ValidationException>(() => Send(provider, new PlaceOrder(string.Empty, 2)));

            Assert.Equal(["outer before"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Internal_validator_from_another_assembly_is_registered_as_transient()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddFluentValidation());
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using IServiceScope scope = provider.CreateScope();

            await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new WriteEntry(string.Empty), Token));

            ServiceDescriptor descriptor = Assert.Single(ValidatorDescriptors<WriteEntry>(services));
            Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
            Assert.Empty(provider.GetRequiredService<Journal>().Entries);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Validators_are_registered_from_assemblies_of_calls_that_do_not_add_validation(bool validationFirst)
        {
            IServiceCollection services = new ServiceCollection().AddSingleton<Journal>();
            if (validationFirst)
            {
                services
                    .AddPheme(options => options.AddFluentValidation())
                    .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            }
            else
            {
                services
                    .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                    .AddPheme(options => options.AddFluentValidation());
            }

            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new WriteEntry(string.Empty), Token));
            Assert.Single(ValidatorDescriptors<WriteEntry>(services));
        }

        [Fact]
        public async Task Module_registering_its_handlers_before_the_composition_root_gets_its_validators_and_one_pipeline()
        {
            var logs = new TestLoggerFactory();
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(logs)
                .AddSingleton<Journal>()
                // The module, from its infrastructure: its handlers only.
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                // The composition root: the pipeline, without assemblies.
                .AddPheme(options => options.AddRequestLogging().AddFluentValidation());
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new WriteEntry(string.Empty), Token));

            Assert.Single(ValidatorDescriptors<WriteEntry>(services));
            Assert.Single(logs.Entries, entry => entry.Category == RequestLoggingOptions.CategoryName);
            Assert.Empty(provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public async Task Struct_message_is_validated_by_scanned_validators_in_the_order_of_their_full_type_names()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddFluentValidation());
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new AppendEntry(new string(' ', 11)), Token));

            Assert.Equal(
                ["Pheme.TestFixtures.AppendEntryLengthValidator", "Pheme.TestFixtures.AppendEntryTextValidator"],
                ValidatorDescriptors<AppendEntry>(services).Select(descriptor => descriptor.ImplementationType!.FullName));
            Assert.Equal(["MaximumLengthValidator", "NotEmptyValidator"], exception.Errors.Select(failure => failure.ErrorCode));
            Assert.Empty(provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public async Task Failure_with_warning_severity_also_stops_the_pipeline()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, WarningCustomerValidator>()
                .AddPheme(options => options.AddFluentValidation()));

            ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
                () => Send(provider, new PlaceOrder(string.Empty, 2)));

            Assert.Equal(Severity.Warning, Assert.Single(exception.Errors).Severity);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public void AddFluentValidation_on_a_collection_with_registrations_copied_from_another_throws()
        {
            IServiceCollection original = new ServiceCollection()
                .AddPheme(options => options.AddFluentValidation());
            IServiceCollection copy = new ServiceCollection();
            foreach (ServiceDescriptor descriptor in original)
            {
                copy.Add(descriptor);
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => copy.AddPheme(options => options.AddFluentValidation(failures => new OrderRejectedException(failures))));

            Assert.Contains("copied from another service collection", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Validator_scan_of_an_assembly_with_types_that_cannot_be_loaded_names_the_assembly_and_the_loader_errors()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ValidatorScanner.Scan(new ServiceCollection(), new UnloadableAssembly()));

            Assert.StartsWith(
                "Pheme cannot scan assembly 'Broken.Module' for validators because some of its types cannot be loaded: "
                + "Could not load 'Missing'. Bad type.",
                exception.Message,
                StringComparison.Ordinal);
            Assert.IsType<System.Reflection.ReflectionTypeLoadException>(exception.InnerException);
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 2)]
        public async Task Validator_registered_by_hand_and_by_the_scan_reports_its_failures_once(
            bool byHandFirst,
            int expectedRegistrations)
        {
            // The fixture validator is internal, as application validators usually are, so it is registered by type.
            Type validatorType = typeof(Ping).Assembly.GetType("Pheme.TestFixtures.WriteEntryValidator", throwOnError: true)!;
            IServiceCollection services = new ServiceCollection().AddSingleton<Journal>();
            if (byHandFirst)
            {
                services.AddTransient(typeof(IValidator<WriteEntry>), validatorType);
            }

            services.AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddFluentValidation());
            if (!byHandFirst)
            {
                services.AddTransient(typeof(IValidator<WriteEntry>), validatorType);
            }

            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new WriteEntry(string.Empty), Token));

            Assert.Single(exception.Errors);
            Assert.Equal(expectedRegistrations, ValidatorDescriptors<WriteEntry>(services).Count());
        }

        [Fact]
        public async Task Exception_factory_of_one_call_applies_to_validation_added_by_another()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddPheme(options => options.AddFluentValidation())
                .AddPheme(options => options.AddFluentValidation(failures => new OrderRejectedException(failures)))
                .AddPheme(options => options.AddFluentValidation()));

            OrderRejectedException exception = await Assert.ThrowsAsync<OrderRejectedException>(
                () => Send(provider, new PlaceOrder(string.Empty, 2)));

            Assert.Single(exception.Failures);
        }

        [Fact]
        public async Task The_same_exception_factory_may_be_passed_by_several_calls()
        {
            Func<IReadOnlyList<ValidationFailure>, Exception> factory = failures => new OrderRejectedException(failures);
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IValidator<PlaceOrder>, CustomerValidator>()
                .AddPheme(options => options.AddFluentValidation(factory))
                .AddPheme(options => options.AddFluentValidation(factory)));

            OrderRejectedException exception = await Assert.ThrowsAsync<OrderRejectedException>(
                () => Send(provider, new PlaceOrder(string.Empty, 2)));

            Assert.Single(exception.Failures);
        }

        [Fact]
        public void Different_exception_factories_are_rejected()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddFluentValidation(failures => new OrderRejectedException(failures)));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => services.AddPheme(options => options.AddFluentValidation(failures => new ValidationException(failures))));

            Assert.Contains("different exception factory", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Null_arguments_throw_argument_null_exception()
        {
            var options = new PhemeOptions(new ServiceCollection());

            Assert.Throws<ArgumentNullException>("options", () => ((PhemeOptions)null!).AddFluentValidation());
            Assert.Throws<ArgumentNullException>("options", () => ((PhemeOptions)null!).AddFluentValidation(failures => new ValidationException(failures)));
            Assert.Throws<ArgumentNullException>("exceptionFactory", () => options.AddFluentValidation(null!));
        }

        private static IEnumerable<ServiceDescriptor> ValidatorDescriptors<T>(IServiceCollection services)
        {
            return services.Where(descriptor => descriptor.ServiceType == typeof(IValidator<T>));
        }

        private static async Task<TResponse> Send<TResponse>(ServiceProvider provider, ICommand<TResponse> command)
        {
            using IServiceScope scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Token);
        }

        private static async Task<TResponse> Send<TResponse>(ServiceProvider provider, IQuery<TResponse> query)
        {
            using IServiceScope scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Token);
        }

        private static async Task Send(ServiceProvider provider, ICommand command)
        {
            using IServiceScope scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Token);
        }

        private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<PipelineLog>()
                .AddTransient<ICommandHandler<PlaceOrder>, OrderHandler>()
                .AddTransient<ICommandHandler<CheckStock, int>, OrderHandler>()
                .AddTransient<IQueryHandler<CountOrders, int>, OrderHandler>();
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    internal sealed record PlaceOrder(string Customer, int Quantity) : ICommand;

    internal sealed record CheckStock(int Quantity) : ICommand<int>;

    internal sealed record CountOrders : IQuery<int>;

    internal sealed class OrderHandler(PipelineLog log)
        : ICommandHandler<PlaceOrder>, ICommandHandler<CheckStock, int>, IQueryHandler<CountOrders, int>
    {
        public Task Handle(PlaceOrder command, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.CompletedTask;
        }

        public Task<int> Handle(CheckStock command, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.FromResult(command.Quantity);
        }

        public Task<int> Handle(CountOrders query, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.FromResult(3);
        }
    }

    internal sealed class CustomerValidator : AbstractValidator<PlaceOrder>
    {
        public CustomerValidator()
        {
            RuleFor(command => command.Customer).NotEmpty();
        }
    }

    internal sealed class WarningCustomerValidator : AbstractValidator<PlaceOrder>
    {
        public WarningCustomerValidator()
        {
            RuleFor(command => command.Customer).NotEmpty().WithSeverity(Severity.Warning);
        }
    }

    internal sealed class QuantityValidator : AbstractValidator<PlaceOrder>
    {
        public QuantityValidator()
        {
            RuleFor(command => command.Quantity).GreaterThan(0);
        }
    }

    /// <summary>
    /// Logs the start and the end of an async rule, so validators running in parallel would interleave in the log.
    /// </summary>
    internal abstract class LoggingStockValidator : AbstractValidator<CheckStock>
    {
        protected LoggingStockValidator(PipelineLog log, string name)
        {
            RuleFor(command => command.Quantity).MustAsync(async (quantity, cancellationToken) =>
            {
                log.Entries.Add($"{name} start");
                await Task.Delay(20, cancellationToken);
                log.Entries.Add($"{name} end");
                return true;
            });
        }
    }

    internal sealed class FirstStockValidator(PipelineLog log) : LoggingStockValidator(log, "first");

    internal sealed class SecondStockValidator(PipelineLog log) : LoggingStockValidator(log, "second");

    internal sealed class OrderRejectedException(IReadOnlyList<ValidationFailure> failures) : Exception("Order rejected.")
    {
        public IReadOnlyList<ValidationFailure> Failures { get; } = failures;
    }
}
