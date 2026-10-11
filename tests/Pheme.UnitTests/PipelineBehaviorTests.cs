using Microsoft.Extensions.DependencyInjection;
using Pheme.Pipeline;

namespace Pheme.UnitTests
{
    public class PipelineBehaviorTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Behaviors_run_in_the_order_they_were_added_regardless_of_registration_order()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddTransient(typeof(InnerBehavior<,>))
                .AddPheme(options => options
                    .AddBehavior(typeof(OuterBehavior<,>))
                    .AddBehavior(typeof(MiddleBehavior<,>))
                    .AddBehavior(typeof(InnerBehavior<,>))));

            await Send(provider, new LoggedCommand());

            Assert.Equal(
                ["outer before", "middle before", "inner before", "handler", "inner after", "middle after", "outer after"],
                provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behaviors_of_several_AddPheme_calls_run_in_call_order_and_once_each()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(OuterBehavior<,>)).AddBehavior(typeof(MiddleBehavior<,>)))
                .AddPheme(options => options.AddBehavior(typeof(MiddleBehavior<,>)).AddBehavior(typeof(InnerBehavior<,>))));

            await Send(provider, new LoggedCommand());

            Assert.Equal(
                ["outer before", "middle before", "inner before", "handler", "inner after", "middle after", "outer after"],
                provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behavior_sees_the_response_of_the_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(ResponseLoggingBehavior<,>))));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            int commandResponse = await sender.Send(new LoggedCommandWithResponse(), Token);
            int queryResponse = await sender.Send(new LoggedQuery(), Token);

            Assert.Equal(1, commandResponse);
            Assert.Equal(2, queryResponse);
            Assert.Equal(["handler", "response 1", "handler", "response 2"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behavior_that_does_not_call_next_skips_the_handler_and_returns_its_own_response()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(ShortCircuitBehavior<>)).AddBehavior(typeof(InnerBehavior<,>))));

            int response = await Send(provider, new LoggedQuery());

            Assert.Equal(-1, response);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Command_without_response_passes_through_the_pipeline_with_unit_response()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(TypeLoggingBehavior<,>)).AddBehavior(typeof(CommandOnlyBehavior<>))));

            await Send(provider, new LoggedCommand());

            Assert.Equal(
                ["LoggedCommand -> Unit", "command only", "handler"],
                provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behavior_constrained_by_message_kind_applies_only_to_that_kind()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options
                    .AddBehavior(typeof(CommandOnlyBehavior<>))
                    .AddBehavior(typeof(CommandWithResponseOnlyBehavior<,>))));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
            List<string> entries = provider.GetRequiredService<PipelineLog>().Entries;

            await sender.Send(new LoggedQuery(), Token);
            Assert.Equal(["handler"], entries);

            entries.Clear();
            await sender.Send(new LoggedCommandWithResponse(), Token);
            Assert.Equal(["command with response only", "handler"], entries);

            entries.Clear();
            await sender.Send(new LoggedCommand(), Token);
            Assert.Equal(["command only", "handler"], entries);
        }

        [Fact]
        public async Task Behavior_with_a_response_shape_applies_only_to_matching_responses()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(ArrayResponseBehavior<,>))));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            string[] names = await sender.Send(new GetNames(), Token);
            await sender.Send(new LoggedQuery(), Token);

            Assert.Equal(["Ada", "Grace"], names);
            Assert.Equal(["2 String items", "handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Closed_behavior_applies_only_to_its_message_and_may_replace_the_response()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(DoublingBehavior))));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            int query = await sender.Send(new LoggedQuery(), Token);
            int command = await sender.Send(new LoggedCommandWithResponse(), Token);

            Assert.Equal(4, query);
            Assert.Equal(1, command);
        }

        [Fact]
        public async Task Without_behaviors_the_handler_is_called_directly()
        {
            using ServiceProvider provider = BuildProvider(services => services.AddPheme(_ => { }));

            int response = await Send(provider, new LoggedQuery());

            Assert.Equal(2, response);
            Assert.Equal(["handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behavior_may_pass_another_cancellation_token_to_the_rest_of_the_pipeline()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(TokenReplacingBehavior<,>))));
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TokenCommand(), Token);

            Assert.Equal([TokenReplacingBehavior<TokenCommand, Unit>.Token], provider.GetRequiredService<Recorder>().Tokens);
        }

        [Fact]
        public async Task Behaviors_are_resolved_from_the_callers_scope_on_each_send()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddScoped<ScopedDependency>()
                .AddPheme(options => options.AddBehavior(typeof(ScopeRecordingBehavior<,>))));
            using IServiceScope first = provider.CreateScope();
            using IServiceScope second = provider.CreateScope();

            await first.ServiceProvider.GetRequiredService<ISender>().Send(new LoggedCommand(), Token);
            await first.ServiceProvider.GetRequiredService<ISender>().Send(new LoggedCommand(), Token);
            await second.ServiceProvider.GetRequiredService<ISender>().Send(new LoggedCommand(), Token);

            List<object> recorded = provider.GetRequiredService<Recorder>().Messages;
            Assert.Equal(6, recorded.Count);
            Assert.Equal(3, recorded.OfType<ScopeRecordingBehavior<LoggedCommand, Unit>>().Distinct().Count());
            Assert.Equal(
                [
                    first.ServiceProvider.GetRequiredService<ScopedDependency>(),
                    first.ServiceProvider.GetRequiredService<ScopedDependency>(),
                    second.ServiceProvider.GetRequiredService<ScopedDependency>(),
                ],
                recorded.OfType<ScopedDependency>());
        }

        [Fact]
        public async Task Behavior_calling_next_again_runs_the_same_inner_behaviors_and_a_new_handler()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddScoped<ScopedDependency>()
                .AddTransient<ICommandHandler<RepeatedCommand>, RepeatedCommandHandler>()
                .AddPheme(options => options.AddBehavior(typeof(TwiceBehavior<,>)).AddBehavior(typeof(ScopeRecordingBehavior<,>))));

            await Send(provider, new RepeatedCommand());

            List<object> recorded = provider.GetRequiredService<Recorder>().Messages;
            Assert.Equal(6, recorded.Count);
            Assert.Single(recorded.OfType<ScopeRecordingBehavior<RepeatedCommand, Unit>>().Distinct());
            Assert.Single(recorded.OfType<ScopedDependency>().Distinct());
            Assert.Equal(2, recorded.OfType<RepeatedCommandHandler>().Distinct().Count());
        }

        [Fact]
        public async Task Behavior_exception_reaches_the_caller_unwrapped_in_the_returned_task()
        {
            var failure = new InvalidOperationException("Behavior failed.");
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton(failure)
                .AddPheme(options => options.AddBehavior(typeof(ThrowingBehavior<,>))));
            using IServiceScope scope = provider.CreateScope();

            Task send = scope.ServiceProvider.GetRequiredService<ISender>().Send(new LoggedCommand(), Token);

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => send));
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Synchronous_throw_of_an_inner_behavior_reaches_the_outer_behavior_as_a_faulted_task()
        {
            var failure = new InvalidOperationException("Behavior failed.");
            using ServiceProvider provider = BuildProvider(services => services
                .AddSingleton(failure)
                .AddPheme(options => options.AddBehavior(typeof(NextObservingBehavior<,>)).AddBehavior(typeof(ThrowingBehavior<,>))));

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new LoggedCommand())));

            Assert.Equal(["next returned Faulted"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Behavior_added_after_a_send_applies_to_later_sends()
        {
            IServiceCollection services = CreateServices()
                .AddPheme(options => options.AddBehavior(typeof(OuterBehavior<,>)));
            using (ServiceProvider first = services.BuildServiceProvider())
            {
                await Send(first, new LoggedCommand());
                Assert.Equal(["outer before", "handler", "outer after"], first.GetRequiredService<PipelineLog>().Entries);
            }

            services.AddPheme(options => options.AddBehavior(typeof(InnerBehavior<,>)));
            using ServiceProvider second = services.BuildServiceProvider();
            await Send(second, new LoggedCommand());

            Assert.Equal(
                ["outer before", "inner before", "handler", "inner after", "outer after"],
                second.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Closed_constructed_generic_behavior_applies_only_to_its_message()
        {
            using ServiceProvider provider = BuildProvider(services => services
                .AddPheme(options => options.AddBehavior(typeof(TypeLoggingBehavior<LoggedQuery, int>))));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new LoggedQuery(), Token);
            await sender.Send(new LoggedCommandWithResponse(), Token);

            Assert.Equal(["LoggedQuery -> Int32", "handler", "handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Theory]
        [InlineData(typeof(ClassOnlyBehavior<,>), typeof(LoggedQuery), true)]
        [InlineData(typeof(ClassOnlyBehavior<,>), typeof(StructQuery), false)]
        [InlineData(typeof(StructOnlyBehavior<,>), typeof(StructQuery), true)]
        [InlineData(typeof(StructOnlyBehavior<,>), typeof(LoggedQuery), false)]
        [InlineData(typeof(NewOnlyBehavior<,>), typeof(LoggedQuery), true)]
        [InlineData(typeof(NewOnlyBehavior<,>), typeof(StructQuery), true)]
        [InlineData(typeof(NewOnlyBehavior<,>), typeof(QueryWithArgument), false)]
        public void Open_generic_behavior_applies_only_to_messages_satisfying_its_special_constraints(
            Type behaviorType, Type messageType, bool applies)
        {
            Type serviceType = typeof(IPipelineBehavior<,>).MakeGenericType(messageType, typeof(int));

            bool closed = BehaviorTypes.TryClose(behaviorType, serviceType, out Type? closedType);

            Assert.Equal(applies, closed);
            Assert.Equal(applies ? behaviorType.MakeGenericType(messageType, typeof(int)) : null, closedType);
        }

        [Theory]
        [InlineData(typeof(List<string>), true)]
        [InlineData(typeof(HashSet<string>), false)]
        [InlineData(typeof(string[]), false)]
        [InlineData(typeof(int), false)]
        public void Open_generic_behavior_with_a_generic_response_shape_takes_its_type_argument_from_the_response(
            Type responseType, bool applies)
        {
            Type serviceType = typeof(IPipelineBehavior<,>).MakeGenericType(typeof(LoggedQuery), responseType);

            bool closed = BehaviorTypes.TryClose(typeof(ListResponseBehavior<,>), serviceType, out Type? closedType);

            Assert.Equal(applies, closed);
            Assert.Equal(applies ? typeof(ListResponseBehavior<LoggedQuery, string>) : null, closedType);
        }

        [Fact]
        public void AddBehavior_rejects_a_partially_closed_type()
        {
            Type definition = typeof(TypeLoggingBehavior<,>);
            Type partiallyClosed = definition.MakeGenericType(typeof(LoggedQuery), definition.GetGenericArguments()[1]);
            var options = new PhemeOptions(new ServiceCollection());

            ArgumentException exception = Assert.Throws<ArgumentException>("behaviorType", () => options.AddBehavior(partiallyClosed));

            Assert.Contains("is partially closed", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(typeof(string), "does not implement IPipelineBehavior")]
        [InlineData(typeof(IPipelineBehavior<,>), "is not a non-abstract class")]
        [InlineData(typeof(AbstractBehavior<,>), "is not a non-abstract class")]
        [InlineData(typeof(UninferableBehavior<,,>), "cannot infer")]
        public void AddBehavior_rejects_a_type_that_cannot_be_a_behavior(Type behaviorType, string reason)
        {
            var options = new PhemeOptions(new ServiceCollection());

            ArgumentException exception = Assert.Throws<ArgumentException>("behaviorType", () => options.AddBehavior(behaviorType));

            Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentNullException>("behaviorType", () => options.AddBehavior(null!));
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
            IServiceCollection services = CreateServices();
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        private static IServiceCollection CreateServices()
        {
            return new ServiceCollection()
                .AddSingleton<PipelineLog>()
                .AddSingleton<Recorder>()
                .AddTransient<ICommandHandler<LoggedCommand>, LoggedHandler>()
                .AddTransient<ICommandHandler<LoggedCommandWithResponse, int>, LoggedHandler>()
                .AddTransient<IQueryHandler<LoggedQuery, int>, LoggedHandler>()
                .AddTransient<IQueryHandler<GetNames, string[]>, LoggedHandler>()
                .AddTransient<ICommandHandler<TokenCommand>, TokenHandler>()
                .AddTransient<ICommandHandler<TokenCommandWithResponse, int>, TokenHandler>()
                .AddTransient<IQueryHandler<TokenQuery, int>, TokenHandler>();
        }
    }

    internal sealed class PipelineLog
    {
        public List<string> Entries { get; } = [];
    }

    internal sealed record LoggedCommand : ICommand;

    internal sealed record LoggedCommandWithResponse : ICommand<int>;

    internal sealed record LoggedQuery : IQuery<int>;

    internal sealed record GetNames : IQuery<string[]>;

    internal readonly record struct StructQuery : IQuery<int>;

    internal sealed record QueryWithArgument(int Value) : IQuery<int>;

    // Handles the messages above, so a scan of this assembly finds a handler for each.
    internal sealed class ConstrainedQueryHandler : IQueryHandler<StructQuery, int>, IQueryHandler<QueryWithArgument, int>
    {
        public Task<int> Handle(StructQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> Handle(QueryWithArgument query, CancellationToken cancellationToken)
        {
            return Task.FromResult(query.Value);
        }
    }

    internal sealed class LoggedHandler(PipelineLog log)
        : ICommandHandler<LoggedCommand>,
          ICommandHandler<LoggedCommandWithResponse, int>,
          IQueryHandler<LoggedQuery, int>,
          IQueryHandler<GetNames, string[]>
    {
        public Task Handle(LoggedCommand command, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.CompletedTask;
        }

        public Task<int> Handle(LoggedCommandWithResponse command, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.FromResult(1);
        }

        public Task<int> Handle(LoggedQuery query, CancellationToken cancellationToken)
        {
            log.Entries.Add("handler");
            return Task.FromResult(2);
        }

        public Task<string[]> Handle(GetNames query, CancellationToken cancellationToken)
        {
            return Task.FromResult<string[]>(["Ada", "Grace"]);
        }
    }

    internal abstract class NamedBehavior<TMessage, TResponse>(PipelineLog log, string name) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add($"{name} before");
            TResponse response = await next(cancellationToken);
            log.Entries.Add($"{name} after");
            return response;
        }
    }

    internal sealed class OuterBehavior<TMessage, TResponse>(PipelineLog log) : NamedBehavior<TMessage, TResponse>(log, "outer")
        where TMessage : IMessage;

    internal sealed class MiddleBehavior<TMessage, TResponse>(PipelineLog log) : NamedBehavior<TMessage, TResponse>(log, "middle")
        where TMessage : IMessage;

    internal sealed class InnerBehavior<TMessage, TResponse>(PipelineLog log) : NamedBehavior<TMessage, TResponse>(log, "inner")
        where TMessage : IMessage;

    internal sealed class ResponseLoggingBehavior<TMessage, TResponse>(PipelineLog log) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            TResponse response = await next(cancellationToken);
            log.Entries.Add($"response {response}");
            return response;
        }
    }

    internal sealed class TypeLoggingBehavior<TMessage, TResponse>(PipelineLog log) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add($"{typeof(TMessage).Name} -> {typeof(TResponse).Name}");
            return next(cancellationToken);
        }
    }

    internal sealed class ShortCircuitBehavior<TQuery> : IPipelineBehavior<TQuery, int>
        where TQuery : IQuery<int>
    {
        public Task<int> Handle(TQuery message, MessageHandlerDelegate<int> next, CancellationToken cancellationToken)
        {
            return Task.FromResult(-1);
        }
    }

    internal sealed class CommandOnlyBehavior<TCommand>(PipelineLog log) : IPipelineBehavior<TCommand, Unit>
        where TCommand : ICommand
    {
        public Task<Unit> Handle(TCommand message, MessageHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            log.Entries.Add("command only");
            return next(cancellationToken);
        }
    }

    internal sealed class CommandWithResponseOnlyBehavior<TCommand, TResponse>(PipelineLog log) : IPipelineBehavior<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<TResponse> Handle(TCommand message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add("command with response only");
            return next(cancellationToken);
        }
    }

    internal sealed class ArrayResponseBehavior<TQuery, TItem>(PipelineLog log) : IPipelineBehavior<TQuery, TItem[]>
        where TQuery : IQuery<TItem[]>
    {
        public async Task<TItem[]> Handle(TQuery message, MessageHandlerDelegate<TItem[]> next, CancellationToken cancellationToken)
        {
            TItem[] items = await next(cancellationToken);
            log.Entries.Add($"{items.Length} {typeof(TItem).Name} items");
            return items;
        }
    }

    internal sealed class ListResponseBehavior<TMessage, TItem> : PassThroughBehavior<TMessage, List<TItem>>
        where TMessage : IMessage;

    internal sealed class DoublingBehavior : IPipelineBehavior<LoggedQuery, int>
    {
        public async Task<int> Handle(LoggedQuery message, MessageHandlerDelegate<int> next, CancellationToken cancellationToken)
        {
            return await next(cancellationToken) * 2;
        }
    }

    internal sealed class TokenReplacingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public static readonly CancellationToken Token = new CancellationTokenSource().Token;

        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            return next(Token);
        }
    }

    internal sealed class ScopeRecordingBehavior<TMessage, TResponse>(Recorder recorder, ScopedDependency dependency)
        : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            recorder.Messages.Add(this);
            recorder.Messages.Add(dependency);
            return next(cancellationToken);
        }
    }

    internal sealed record RepeatedCommand : ICommand;

    internal sealed class RepeatedCommandHandler(Recorder recorder) : ICommandHandler<RepeatedCommand>
    {
        public Task Handle(RepeatedCommand command, CancellationToken cancellationToken)
        {
            recorder.Messages.Add(this);
            return Task.CompletedTask;
        }
    }

    internal sealed class TwiceBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            await next(cancellationToken);
            return await next(cancellationToken);
        }
    }

    internal sealed class ThrowingBehavior<TMessage, TResponse>(InvalidOperationException failure) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            throw failure;
        }
    }

    /// <summary>
    /// Records whether <c>next</c> threw synchronously or returned a task, and in which state.
    /// </summary>
    internal sealed class NextObservingBehavior<TMessage, TResponse>(PipelineLog log) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Task<TResponse> task;
            try
            {
                task = next(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                log.Entries.Add("next threw");
                throw;
            }

            log.Entries.Add($"next returned {task.Status}");
            return task;
        }
    }

    internal abstract class PassThroughBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            return next(cancellationToken);
        }
    }

    internal sealed class ClassOnlyBehavior<TMessage, TResponse> : PassThroughBehavior<TMessage, TResponse>
        where TMessage : class, IMessage;

    internal sealed class StructOnlyBehavior<TMessage, TResponse> : PassThroughBehavior<TMessage, TResponse>
        where TMessage : struct, IMessage;

    internal sealed class NewOnlyBehavior<TMessage, TResponse> : PassThroughBehavior<TMessage, TResponse>
        where TMessage : IMessage, new();

    internal abstract class AbstractBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public abstract Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    internal sealed class UninferableBehavior<TMessage, TResponse, TExtra> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            return next(cancellationToken);
        }
    }
}
