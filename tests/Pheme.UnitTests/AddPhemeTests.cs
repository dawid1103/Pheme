using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class AddPhemeTests
    {
        private static readonly Type[] HandlerDefinitions = [typeof(ICommandHandler<>), typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Registers_internal_handler_from_another_assembly()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>()));
            using IServiceScope scope = provider.CreateScope();

            string pong = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping("hi"), Token);

            Assert.Equal("Pong: hi", pong);
        }

        [Fact]
        public async Task Registers_class_handling_several_messages_under_each_handler_interface()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>()));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new WriteEntry("first"), Token);
            int countAfterWrite = await sender.Send(new WriteEntryAndCount("second"), Token);
            int count = await sender.Send(new CountEntries(), Token);

            Assert.Equal(2, countAfterWrite);
            Assert.Equal(2, count);
            Assert.Equal(["first", "second"], provider.GetRequiredService<Journal>().Entries);
        }

        [Fact]
        public async Task Registers_nested_and_derived_handlers_but_skips_abstract_and_open_generic_ones()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            Assert.Equal(42, await sender.Send(new GetNestedAnswer(), Token));
            Assert.Equal(7, await sender.Send(new GetGenericAnswer(), Token));
            Assert.DoesNotContain(HandlerDescriptors(services), descriptor => descriptor.ImplementationType is { IsAbstract: true });
            Assert.DoesNotContain(HandlerDescriptors(services), descriptor => descriptor.ServiceType.ContainsGenericParameters);
        }

        [Fact]
        public async Task Struct_command_and_query_are_scanned_validated_and_sent()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<Journal>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            provider.GetRequiredService<IStartupValidator>().Validate();
            await sender.Send(new AppendEntry("first"), Token);
            string entry = await sender.Send(new GetEntry(0), Token);

            Assert.Equal("first", entry);
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICommandHandler<AppendEntry>));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQueryHandler<GetEntry, string>));
        }

        [Fact]
        public void AddPheme_on_a_collection_with_registrations_copied_from_another_throws_and_leaves_the_original_unchanged()
        {
            IServiceCollection original = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            ServiceDescriptor[] before = [.. original];
            IServiceCollection copy = new ServiceCollection();
            foreach (ServiceDescriptor descriptor in original)
            {
                copy.Add(descriptor);
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => copy.AddPheme(options => options.AddHandlersFromAssemblyContaining<AddPhemeTests>()));

            Assert.Contains("copied from another service collection", exception.Message, StringComparison.Ordinal);
            Assert.Equal(before, original);
        }

        [Fact]
        public void Assembly_with_types_that_cannot_be_loaded_fails_with_the_assembly_name_and_the_loader_errors()
        {
            var assembly = new UnloadableAssembly();
            IServiceCollection services = new ServiceCollection();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => services.AddPheme(options => options.AddHandlersFromAssemblies(assembly)));

            Assert.Equal(
                "Pheme cannot scan assembly 'Broken.Module' because some of its types cannot be loaded: Could not load "
                + "'Missing'. Bad type. Is a dependency of the assembly missing or of another version?",
                exception.Message);
            Assert.IsType<ReflectionTypeLoadException>(exception.InnerException);
        }

        [Fact]
        public void Registering_the_same_assembly_twice_registers_each_handler_once()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddPheme(options => options.AddHandlersFromAssemblies(typeof(Ping).Assembly, typeof(Ping).Assembly));

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISender));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQueryHandler<Ping, string>));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICommandHandler<WriteEntry>));
            Assert.Equal(8, HandlerDescriptors(services).Count());
        }

        [Fact]
        public void Marker_types_add_the_assemblies_containing_them_once()
        {
            // A static class cannot be a type argument: AddHandlersFromAssemblyContaining<JournalModule>() is CS0718.
            IServiceCollection byMarkers = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining(typeof(JournalModule), typeof(AddPhemeTests), typeof(Ping)));
            IServiceCollection byAssemblies = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblies(typeof(Ping).Assembly, typeof(AddPhemeTests).Assembly));

            Assert.Single(byMarkers, descriptor => descriptor.ServiceType == typeof(IQueryHandler<Ping, string>));
            Assert.Single(byMarkers, descriptor => descriptor.ServiceType == typeof(ICommandHandler<AddNumbers, int>));
            Assert.Equal(
                HandlerDescriptors(byAssemblies).Select(descriptor => (descriptor.ServiceType, descriptor.ImplementationType)),
                HandlerDescriptors(byMarkers).Select(descriptor => (descriptor.ServiceType, descriptor.ImplementationType)));
        }

        [Fact]
        public void Handler_registered_by_hand_is_not_registered_again_by_the_scan()
        {
            Type pingHandler = typeof(Ping).Assembly.GetType("Pheme.TestFixtures.PingHandler", throwOnError: true)!;
            IServiceCollection services = new ServiceCollection()
                .AddTransient(typeof(IQueryHandler<Ping, string>), pingHandler)
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQueryHandler<Ping, string>));
        }

        [Fact]
        public void Handler_registered_by_hand_before_AddPheme_keeps_its_lifetime()
        {
            Type pingHandler = typeof(Ping).Assembly.GetType("Pheme.TestFixtures.PingHandler", throwOnError: true)!;
            IServiceCollection services = new ServiceCollection()
                .AddScoped(typeof(IQueryHandler<Ping, string>), pingHandler)
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            using IServiceScope scope = provider.CreateScope();

            ServiceDescriptor descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQueryHandler<Ping, string>));
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.Same(
                scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>(),
                scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>());
        }

        [Fact]
        public void Separate_calls_with_different_assemblies_register_handlers_from_all_of_them()
        {
            // The test assembly has messages without handlers on purpose, so the sender would fail validation here.
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<AddPhemeTests>());

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IQueryHandler<Ping, string>));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICommandHandler<AddNumbers, int>));
        }

        [Fact]
        public void Handlers_are_transient_by_default()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());

            Assert.All(HandlerDescriptors(services), descriptor => Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime));
        }

        [Fact]
        public void Sender_is_scoped_so_a_singleton_depending_on_it_fails_validation_on_build()
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<SenderConsumer>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>());

            AggregateException exception = Assert.Throws<AggregateException>(
                () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));

            Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISender)).Lifetime);
            Assert.Contains("Cannot consume scoped service 'Pheme.ISender'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Assembly_scanner_sees_every_assembly_of_earlier_and_later_calls_once()
        {
            var scanned = new List<Assembly>();
            IServiceCollection? scannedServices = null;
            Action<IServiceCollection, Assembly> scanner = (services, assembly) =>
            {
                scannedServices = services;
                scanned.Add(assembly);
            };

            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>())
                .AddPheme(options => options
                    .AddAssemblyScanner(scanner)
                    .AddAssemblyScanner(scanner)
                    .AddHandlersFromAssemblyContaining<AddPhemeTests>())
                .AddPheme(options => options
                    .AddAssemblyScanner(scanner)
                    .AddHandlersFromAssemblyContaining<Ping>()
                    .AddHandlersFromAssemblyContaining<ISender>());

            Assert.Equal([typeof(Ping).Assembly, typeof(AddPhemeTests).Assembly, typeof(ISender).Assembly], scanned);
            Assert.Same(services, scannedServices);
        }

        [Fact]
        public void Null_arguments_throw_argument_null_exception()
        {
            IServiceCollection services = new ServiceCollection();
            var options = new PhemeOptions(services);

            Assert.Throws<ArgumentNullException>("services", () => ((IServiceCollection)null!).AddPheme(_ => { }));
            Assert.Throws<ArgumentNullException>("configure", () => services.AddPheme(null!));
            Assert.Throws<ArgumentNullException>("assemblies", () => options.AddHandlersFromAssemblies(null!));
            Assert.Throws<ArgumentNullException>("assemblies", () => options.AddHandlersFromAssemblies(typeof(Ping).Assembly, null!));
            Assert.Throws<ArgumentNullException>("markerTypes", () => options.AddHandlersFromAssemblyContaining(null!));
            Assert.Throws<ArgumentNullException>("markerTypes", () => options.AddHandlersFromAssemblyContaining(typeof(Ping), null!));
            Assert.Throws<ArgumentNullException>("scanner", () => options.AddAssemblyScanner(null!));
            Assert.Throws<ArgumentNullException>("configure", () => options.AddRequestLogging(null!));
        }

        private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
        {
            IServiceCollection services = new ServiceCollection();
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        private static IEnumerable<ServiceDescriptor> HandlerDescriptors(IServiceCollection services)
        {
            return services.Where(descriptor =>
                descriptor.ServiceType.IsGenericType && HandlerDefinitions.Contains(descriptor.ServiceType.GetGenericTypeDefinition()));
        }
    }

    /// <summary>
    /// An assembly whose types fail to load, as when a dependency is missing.
    /// </summary>
    internal sealed class UnloadableAssembly : Assembly
    {
        public override AssemblyName GetName()
        {
            return new AssemblyName("Broken.Module");
        }

        public override Type[] GetTypes()
        {
            throw new ReflectionTypeLoadException(
                [typeof(Ping), null],
                [new FileNotFoundException("Could not load 'Missing'."), new FileNotFoundException("Could not load 'Missing'."), new TypeLoadException("Bad type.")]);
        }
    }

    internal sealed class SenderConsumer(ISender sender)
    {
        public ISender Sender { get; } = sender;
    }
}
