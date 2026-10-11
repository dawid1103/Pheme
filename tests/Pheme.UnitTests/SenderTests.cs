using Microsoft.Extensions.DependencyInjection;
using Pheme.Registration;

namespace Pheme.UnitTests
{
    public class SenderTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Send_command_without_response_invokes_its_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton<Recorder>()
                .AddTransient<ICommandHandler<RecordCommand>, RecordCommandHandler>());
            var command = new RecordCommand("first");

            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Token);

            Assert.Same(command, Assert.Single(provider.GetRequiredService<Recorder>().Messages));
        }

        [Fact]
        public async Task Send_command_with_response_returns_handler_response()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<ICommandHandler<AddNumbers, int>, AddNumbersHandler>());

            using IServiceScope scope = provider.CreateScope();

            int sum = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AddNumbers(2, 3), Token);

            Assert.Equal(5, sum);
        }

        [Fact]
        public async Task Send_query_returns_handler_response()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient<IQueryHandler<GetGreeting, string>, GetGreetingHandler>());

            using IServiceScope scope = provider.CreateScope();

            string greeting = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetGreeting("Ada"), Token);

            Assert.Equal("Hello, Ada", greeting);
        }

        [Fact]
        public async Task Send_passes_cancellation_token_to_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton<Recorder>()
                .AddTransient<ICommandHandler<TokenCommand>, TokenHandler>()
                .AddTransient<ICommandHandler<TokenCommandWithResponse, int>, TokenHandler>()
                .AddTransient<IQueryHandler<TokenQuery, int>, TokenHandler>());
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
            using var cancellation = new CancellationTokenSource();

            await sender.Send(new TokenCommand(), cancellation.Token);
            await sender.Send(new TokenCommandWithResponse(), cancellation.Token);
            await sender.Send(new TokenQuery(), cancellation.Token);

            Assert.Equal(
                [cancellation.Token, cancellation.Token, cancellation.Token],
                provider.GetRequiredService<Recorder>().Tokens);
        }

        [Fact]
        public async Task Send_without_registered_handler_throws_with_message_kind_and_type_name()
        {
            using ServiceProvider provider = BuildProvider(_ => { });
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            InvalidOperationException command = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sender.Send(new UnhandledCommand(), Token));
            InvalidOperationException commandWithResponse = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sender.Send(new UnhandledCommandWithResponse(), Token));
            InvalidOperationException query = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sender.Send(new UnhandledQuery(), Token));

            Assert.StartsWith("No handler is registered for command 'Pheme.UnitTests.UnhandledCommand'.", command.Message, StringComparison.Ordinal);
            Assert.StartsWith(
                "No handler is registered for command 'Pheme.UnitTests.UnhandledCommandWithResponse'.", commandWithResponse.Message, StringComparison.Ordinal);
            Assert.StartsWith("No handler is registered for query 'Pheme.UnitTests.UnhandledQuery'.", query.Message, StringComparison.Ordinal);
            Assert.EndsWith(
                "Implement 'Pheme.IQueryHandler<Pheme.UnitTests.UnhandledQuery, System.Int32>'; is the handler's assembly added "
                + "with AddHandlersFromAssemblies? Add the assembly of the message too, so the check at start-up reports a "
                + "missing handler.",
                query.Message,
                StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Send_rethrows_handler_exception_unwrapped(bool throwAfterAwait)
        {
            var failure = new InvalidOperationException("Handler failed.");
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton(failure)
                .AddTransient<ICommandHandler<FailingCommand>, FailingCommandHandler>());

            using IServiceScope scope = provider.CreateScope();

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailingCommand(throwAfterAwait), Token));

            Assert.Same(failure, thrown);
        }

        [Fact]
        public async Task Send_resolves_handler_from_the_callers_scope()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddScoped<ScopedDependency>()
                .AddTransient<IQueryHandler<GetScopedDependency, ScopedDependency>, GetScopedDependencyHandler>());
            using IServiceScope first = provider.CreateScope();
            using IServiceScope second = provider.CreateScope();

            ScopedDependency fromFirst = await first.ServiceProvider.GetRequiredService<ISender>().Send(new GetScopedDependency(), Token);
            ScopedDependency fromFirstAgain = await first.ServiceProvider.GetRequiredService<ISender>().Send(new GetScopedDependency(), Token);
            ScopedDependency fromSecond = await second.ServiceProvider.GetRequiredService<ISender>().Send(new GetScopedDependency(), Token);

            Assert.Same(first.ServiceProvider.GetRequiredService<ScopedDependency>(), fromFirst);
            Assert.Same(fromFirst, fromFirstAgain);
            Assert.Same(second.ServiceProvider.GetRequiredService<ScopedDependency>(), fromSecond);
            Assert.NotSame(fromFirst, fromSecond);
        }

        [Fact]
        public async Task Send_of_different_message_types_in_parallel_reaches_the_right_handlers()
        {
            Type[] tags =
            [
                typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
                typeof(decimal), typeof(string), typeof(Guid), typeof(DateTime),
            ];
            using ServiceProvider provider = BuildProvider(services =>
            {
                foreach (Type tag in tags)
                {
                    services.AddTransient(
                        typeof(IQueryHandler<,>).MakeGenericType(typeof(Echo<>).MakeGenericType(tag), typeof(string)),
                        typeof(EchoHandler<>).MakeGenericType(tag));
                }
            });
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
            IQuery<string>[] queries = [.. tags.Select(tag => (IQuery<string>)Activator.CreateInstance(typeof(Echo<>).MakeGenericType(tag))!)];

            string[] responses = await Task.WhenAll(
                Enumerable.Range(0, 20)
                    .SelectMany(_ => queries)
                    .Select(query => Task.Run(() => sender.Send(query, Token), Token)));

            Assert.Equal(Enumerable.Range(0, 20).SelectMany(_ => tags.Select(tag => tag.Name)), responses);
        }

        [Fact]
        public async Task Send_null_message_throws_argument_null_exception()
        {
            using ServiceProvider provider = BuildProvider(_ => { });
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await Assert.ThrowsAsync<ArgumentNullException>("command", () => sender.Send((ICommand)null!, Token));
            await Assert.ThrowsAsync<ArgumentNullException>("command", () => sender.Send((ICommand<int>)null!, Token));
            await Assert.ThrowsAsync<ArgumentNullException>("query", () => sender.Send((IQuery<int>)null!, Token));
        }

        private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
        {
            IServiceCollection services = new ServiceCollection().AddPhemeCore();
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    internal sealed class Recorder
    {
        public List<object> Messages { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];
    }

    internal sealed record RecordCommand(string Value) : ICommand;

    internal sealed class RecordCommandHandler(Recorder recorder) : ICommandHandler<RecordCommand>
    {
        public Task Handle(RecordCommand command, CancellationToken cancellationToken)
        {
            recorder.Messages.Add(command);
            return Task.CompletedTask;
        }
    }

    internal sealed record AddNumbers(int Left, int Right) : ICommand<int>;

    internal sealed class AddNumbersHandler : ICommandHandler<AddNumbers, int>
    {
        public Task<int> Handle(AddNumbers command, CancellationToken cancellationToken)
        {
            return Task.FromResult(command.Left + command.Right);
        }
    }

    internal sealed record GetGreeting(string Name) : IQuery<string>;

    internal sealed class GetGreetingHandler : IQueryHandler<GetGreeting, string>
    {
        public async Task<string> Handle(GetGreeting query, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return $"Hello, {query.Name}";
        }
    }

    internal sealed record TokenCommand : ICommand;

    internal sealed record TokenCommandWithResponse : ICommand<int>;

    internal sealed record TokenQuery : IQuery<int>;

    internal sealed class TokenHandler(Recorder recorder)
        : ICommandHandler<TokenCommand>, ICommandHandler<TokenCommandWithResponse, int>, IQueryHandler<TokenQuery, int>
    {
        public Task Handle(TokenCommand command, CancellationToken cancellationToken)
        {
            recorder.Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task<int> Handle(TokenCommandWithResponse command, CancellationToken cancellationToken)
        {
            recorder.Tokens.Add(cancellationToken);
            return Task.FromResult(0);
        }

        public Task<int> Handle(TokenQuery query, CancellationToken cancellationToken)
        {
            recorder.Tokens.Add(cancellationToken);
            return Task.FromResult(0);
        }
    }

    internal sealed record UnhandledCommand : ICommand;

    internal sealed record UnhandledCommandWithResponse : ICommand<int>;

    internal sealed record UnhandledQuery : IQuery<int>;

    internal sealed record FailingCommand(bool ThrowAfterAwait) : ICommand;

    internal sealed class FailingCommandHandler(InvalidOperationException failure) : ICommandHandler<FailingCommand>
    {
        public Task Handle(FailingCommand command, CancellationToken cancellationToken)
        {
            return command.ThrowAfterAwait ? ThrowAfterAwait() : throw failure;

            async Task ThrowAfterAwait()
            {
                await Task.Yield();
                throw failure;
            }
        }
    }

    internal sealed class ScopedDependency;

    internal sealed record GetScopedDependency : IQuery<ScopedDependency>;

    internal sealed class GetScopedDependencyHandler(ScopedDependency dependency) : IQueryHandler<GetScopedDependency, ScopedDependency>
    {
        public Task<ScopedDependency> Handle(GetScopedDependency query, CancellationToken cancellationToken)
        {
            return Task.FromResult(dependency);
        }
    }

    internal sealed record Echo<TTag> : IQuery<string>;

    internal sealed class EchoHandler<TTag> : IQueryHandler<Echo<TTag>, string>
    {
        public Task<string> Handle(Echo<TTag> query, CancellationToken cancellationToken)
        {
            return Task.FromResult(typeof(TTag).Name);
        }
    }
}
