using Pheme.Diagnostics;
using Pheme.Pipeline;

namespace Pheme.Sending
{
    /// <summary>
    /// Dispatches messages of one type to their handler. Created once per message type and response type, so
    /// reflection runs only on their first send.
    /// </summary>
    /// <typeparam name="TResponse">The response type, <see cref="Unit"/> for a command without a response.</typeparam>
    internal interface IMessageHandlerWrapper<TResponse>
    {
        Task<TResponse> Handle(
            IMessage message,
            IServiceProvider serviceProvider,
            PipelineBehaviors behaviors,
            MessageInstrumentation instrumentation,
            CancellationToken cancellationToken);
    }
}
