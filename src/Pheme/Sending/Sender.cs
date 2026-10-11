using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Pheme.Diagnostics;
using Pheme.Pipeline;
using Pheme.Registration;

namespace Pheme.Sending
{
    /// <summary>
    /// Resolves handlers and pipeline behaviors from the service provider it was resolved from. Wrappers are cached per
    /// message type and response type for all senders.
    /// </summary>
    internal sealed class Sender : ISender
    {
        private static readonly ConcurrentDictionary<Type, IMessageHandlerWrapper<Unit>> CommandWrappers = new();

        private readonly IServiceProvider serviceProvider;
        private readonly PipelineBehaviors behaviors;
        private readonly MessageInstrumentation instrumentation;

        public Sender(
            IServiceProvider serviceProvider,
            IOptionsMonitor<HandlerRegistrationOptions> handlerRegistrations,
            PipelineBehaviors behaviors,
            MessageInstrumentation instrumentation)
        {
            // Validates the handler registrations when no host has done it on start. The monitor caches a valid
            // result, shared with ValidateOnStart, so the validation runs once; invalid registrations throw every time.
            _ = handlerRegistrations.CurrentValue;
            this.serviceProvider = serviceProvider;
            this.behaviors = behaviors;
            this.instrumentation = instrumentation;
        }

        public Task Send(ICommand command, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);

            IMessageHandlerWrapper<Unit> wrapper = CommandWrappers.GetOrAdd(
                command.GetType(),
                static commandType => CreateWrapper<Unit>(typeof(CommandHandlerWrapper<>), commandType));
            return wrapper.Handle(command, serviceProvider, behaviors, instrumentation, cancellationToken);
        }

        public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);

            IMessageHandlerWrapper<TResponse> wrapper = ResponseWrappers<TResponse>.Commands.GetOrAdd(
                command.GetType(),
                static commandType => CreateWrapper<TResponse>(typeof(CommandHandlerWrapper<,>), commandType, typeof(TResponse)));
            return wrapper.Handle(command, serviceProvider, behaviors, instrumentation, cancellationToken);
        }

        public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            IMessageHandlerWrapper<TResponse> wrapper = ResponseWrappers<TResponse>.Queries.GetOrAdd(
                query.GetType(),
                static queryType => CreateWrapper<TResponse>(typeof(QueryHandlerWrapper<,>), queryType, typeof(TResponse)));
            return wrapper.Handle(query, serviceProvider, behaviors, instrumentation, cancellationToken);
        }

        private static IMessageHandlerWrapper<TResponse> CreateWrapper<TResponse>(Type wrapperDefinition, params Type[] typeArguments)
        {
            return (IMessageHandlerWrapper<TResponse>)Activator.CreateInstance(wrapperDefinition.MakeGenericType(typeArguments))!;
        }

        private static class ResponseWrappers<TResponse>
        {
            public static readonly ConcurrentDictionary<Type, IMessageHandlerWrapper<TResponse>> Commands = new();
            public static readonly ConcurrentDictionary<Type, IMessageHandlerWrapper<TResponse>> Queries = new();
        }
    }
}
