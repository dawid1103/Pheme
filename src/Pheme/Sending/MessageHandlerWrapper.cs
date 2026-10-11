using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Pheme.Diagnostics;
using Pheme.Internal;
using Pheme.Pipeline;

namespace Pheme.Sending
{
    /// <summary>
    /// Runs the pipeline behaviors that apply to <typeparamref name="TMessage"/> around its handler, inside the
    /// message's activity and duration measurement.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TResponse">The response type, <see cref="Unit"/> for a command without a response.</typeparam>
    internal abstract class MessageHandlerWrapper<TMessage, TResponse> : IMessageHandlerWrapper<TResponse>
        where TMessage : IMessage
    {
        private static readonly string MessageType = TypeNames.Format(typeof(TMessage));
        private static readonly string MessageKind = MessageContracts.GetMessageKindOf(typeof(TMessage));

        // The wrapper is shared by all service providers, but the behaviors belong to a service collection: the closed
        // behavior types are cached per collection, dropped with it, and recomputed when its behaviors change.
        private readonly ConditionalWeakTable<PipelineBehaviors, ClosedBehaviors> closedBehaviors = new();

        public async Task<TResponse> Handle(
            IMessage message,
            IServiceProvider serviceProvider,
            PipelineBehaviors behaviors,
            MessageInstrumentation instrumentation,
            CancellationToken cancellationToken)
        {
            var typedMessage = (TMessage)message;
            MessageMeasurement measurement = instrumentation.Start(MessageType, MessageKind);
            TResponse response;
            try
            {
                response = await HandlePipeline(typedMessage, serviceProvider, behaviors, cancellationToken);
            }
            catch (Exception exception)
            {
                measurement.Stop(exception);
                throw;
            }

            measurement.Stop();
            return response;
        }

        /// <summary>
        /// Resolves the handler and handles the message: the innermost step of the pipeline. Not called when a behavior
        /// skips the rest of the pipeline, so the handler is not even created then.
        /// </summary>
        protected abstract Task<TResponse> HandleMessage(TMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken);

        protected static THandler GetHandler<THandler>(IServiceProvider serviceProvider)
            where THandler : class
        {
            return serviceProvider.GetService(typeof(THandler)) as THandler
                ?? throw new InvalidOperationException(
                    $"No handler is registered for {MessageKind} '{MessageType}'. "
                    + $"Implement '{TypeNames.Format(typeof(THandler))}'; is the handler's assembly added with "
                    + "AddHandlersFromAssemblies? Add the assembly of the message too, so the check at start-up reports a "
                    + "missing handler.");
        }

        private Task<TResponse> HandlePipeline(
            TMessage message, IServiceProvider serviceProvider, PipelineBehaviors behaviors, CancellationToken cancellationToken)
        {
            Type[] types = GetBehaviorTypes(behaviors);
            if (types.Length == 0)
            {
                return HandleMessage(message, serviceProvider, cancellationToken);
            }

            MessageHandlerDelegate<TResponse> next = token => HandleMessage(message, serviceProvider, token);
            for (int index = types.Length - 1; index >= 0; index--)
            {
                var behavior = (IPipelineBehavior<TMessage, TResponse>)serviceProvider.GetRequiredService(types[index]);
                MessageHandlerDelegate<TResponse> inner = next;
                next = token => Invoke(behavior, message, inner, token);
            }

            return next(cancellationToken);
        }

        private Type[] GetBehaviorTypes(PipelineBehaviors behaviors)
        {
            if (closedBehaviors.TryGetValue(behaviors, out ClosedBehaviors? cached) && cached.Version == behaviors.Version)
            {
                return cached.Types;
            }

            (Type[] types, int version) = behaviors.Close(typeof(TMessage), typeof(TResponse));
            closedBehaviors.AddOrUpdate(behaviors, new ClosedBehaviors(types, version));
            return types;
        }

        /// <summary>
        /// Runs one behavior. A synchronous throw of a non-async behavior becomes a faulted task, so <c>next</c> of the
        /// outer behavior never throws synchronously.
        /// </summary>
        private static Task<TResponse> Invoke(
            IPipelineBehavior<TMessage, TResponse> behavior, TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            try
            {
                return behavior.Handle(message, next, cancellationToken);
            }
            catch (Exception exception)
            {
                return Task.FromException<TResponse>(exception);
            }
        }

        private sealed record ClosedBehaviors(Type[] Types, int Version);
    }
}
