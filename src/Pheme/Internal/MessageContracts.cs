namespace Pheme.Internal
{
    /// <summary>
    /// Recognizes message and handler contracts by reflection, for scanning assemblies and validating registrations.
    /// </summary>
    internal static class MessageContracts
    {
        private static readonly Type[] HandlerDefinitions =
        [
            typeof(ICommandHandler<>),
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
        ];

        /// <summary>
        /// Gets whether <paramref name="type"/> is a closed <see cref="ICommandHandler{TCommand}"/>,
        /// <see cref="ICommandHandler{TCommand, TResponse}"/> or <see cref="IQueryHandler{TQuery, TResponse}"/>.
        /// </summary>
        public static bool IsHandlerInterface(Type type)
        {
            return type.IsGenericType && !type.ContainsGenericParameters && HandlerDefinitions.Contains(type.GetGenericTypeDefinition());
        }

        /// <summary>
        /// Gets whether <paramref name="type"/> is a closed <see cref="IDomainEventHandler{TEvent}"/>. Not a handler interface
        /// in the sense of <see cref="IsHandlerInterface"/>: an event may have any number of handlers.
        /// </summary>
        public static bool IsDomainEventHandlerInterface(Type type)
        {
            return type.IsGenericType && !type.ContainsGenericParameters && type.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>);
        }

        /// <summary>
        /// Gets whether <paramref name="type"/> is a closed <see cref="IIntegrationEventHandler{TEvent}"/>. Like a domain
        /// event, an integration event may have any number of handlers.
        /// </summary>
        public static bool IsIntegrationEventHandlerInterface(Type type)
        {
            return type.IsGenericType && !type.ContainsGenericParameters && type.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>);
        }

        /// <summary>
        /// Gets whether <paramref name="type"/> is <see cref="ICommand"/>, a closed <see cref="ICommand{TResponse}"/>
        /// or a closed <see cref="IQuery{TResponse}"/>.
        /// </summary>
        public static bool IsMessageInterface(Type type)
        {
            return type == typeof(ICommand)
                || (type.IsGenericType
                    && (type.GetGenericTypeDefinition() == typeof(ICommand<>) || type.GetGenericTypeDefinition() == typeof(IQuery<>)));
        }

        /// <summary>
        /// Gets the handler interface that handles <paramref name="messageType"/> sent as <paramref name="messageInterface"/>,
        /// for example <c>IQueryHandler&lt;GetOrder, Order&gt;</c> for <c>GetOrder : IQuery&lt;Order&gt;</c>.
        /// </summary>
        public static Type GetHandlerInterface(Type messageType, Type messageInterface)
        {
            if (messageInterface == typeof(ICommand))
            {
                return typeof(ICommandHandler<>).MakeGenericType(messageType);
            }

            Type handlerDefinition = messageInterface.GetGenericTypeDefinition() == typeof(ICommand<>)
                ? typeof(ICommandHandler<,>)
                : typeof(IQueryHandler<,>);
            return handlerDefinition.MakeGenericType(messageType, messageInterface.GetGenericArguments()[0]);
        }

        /// <summary>
        /// Gets "command" or "query" for a handler interface, as used in error messages.
        /// </summary>
        public static string GetMessageKind(Type handlerInterface)
        {
            return handlerInterface.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                ? PhemeDiagnostics.QueryKind
                : PhemeDiagnostics.CommandKind;
        }

        /// <summary>
        /// Gets "query" for a message implementing <see cref="IQuery{TResponse}"/>, otherwise "command", as used in logs,
        /// traces and metrics.
        /// </summary>
        public static string GetMessageKindOf(Type messageType)
        {
            return messageType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQuery<>))
                ? PhemeDiagnostics.QueryKind
                : PhemeDiagnostics.CommandKind;
        }
    }
}
