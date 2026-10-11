namespace Pheme
{
    /// <summary>
    /// Names of the traces and metrics Pheme emits, for OpenTelemetry configuration, filters and tests. Nothing is
    /// recorded until a listener subscribes, for example
    /// <c>.WithTracing(tracing =&gt; tracing.AddSource(PhemeDiagnostics.ActivitySourceName))</c> and
    /// <c>.WithMetrics(metrics =&gt; metrics.AddMeter(PhemeDiagnostics.MeterName))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every sent command and query, and every published domain event, gets an activity named after its full type
    /// name, such as <c>Shop.Ordering.PlaceOrder</c>, and one measurement of <see cref="MessageDurationName"/>. Both
    /// cover the whole pipeline (for a domain event, all its handlers), and the activity is the current one while it
    /// runs.
    /// </para>
    /// <para>
    /// Every delivery of an outbox message gets the same, named after the stored name of the integration event (see
    /// <see cref="OutboxMessage.Type"/>) and covering the handlers it calls. Its activity has the kind
    /// <see cref="System.Diagnostics.ActivityKind.Consumer"/>, the tag <see cref="OutboxSourceTagName"/> and a link to
    /// the activity that wrote the message (<see cref="OutboxMessage.TraceParent"/>); its parent is the current activity
    /// of the run, which the outbox processor does not have, so each delivery starts a trace.
    /// </para>
    /// <para>
    /// The outbox also reports the state of its stores: <see cref="OutboxMessagesName"/> and
    /// <see cref="OutboxOldestPendingAgeName"/>.
    /// </para>
    /// <para>
    /// All carry <see cref="MessageTypeTagName"/> and <see cref="MessageKindTagName"/>. A failure adds
    /// <see cref="ErrorTypeTagName"/> with the full type name of the exception and sets the activity status to
    /// <see cref="System.Diagnostics.ActivityStatusCode.Error"/> with the exception message. An exception thrown by a
    /// listener while recording is ignored.
    /// </para>
    /// </remarks>
    public static class PhemeDiagnostics
    {
        /// <summary>
        /// The name of the <see cref="System.Diagnostics.ActivitySource"/> of the message activities: <c>Pheme</c>.
        /// </summary>
        public const string ActivitySourceName = "Pheme";

        /// <summary>
        /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> of the message metrics: <c>Pheme</c>.
        /// </summary>
        /// <remarks>
        /// Created by the registered <see cref="System.Diagnostics.Metrics.IMeterFactory"/>, or, without one, by Pheme
        /// once per service provider.
        /// </remarks>
        public const string MeterName = "Pheme";

        /// <summary>
        /// The histogram of how long the handling of a message took, in seconds: <c>pheme.message.duration</c>.
        /// </summary>
        /// <remarks>Failures are the measurements with <see cref="ErrorTypeTagName"/>.</remarks>
        public const string MessageDurationName = "pheme.message.duration";

        /// <summary>
        /// The tag with the full type name of the message or domain event, such as <c>Shop.Ordering.PlaceOrder</c>, or
        /// the stored name of the integration event: <c>pheme.message.type</c>.
        /// </summary>
        public const string MessageTypeTagName = "pheme.message.type";

        /// <summary>
        /// The tag with <see cref="CommandKind"/>, <see cref="QueryKind"/>, <see cref="DomainEventKind"/> or
        /// <see cref="IntegrationEventKind"/>: <c>pheme.message.kind</c>.
        /// </summary>
        public const string MessageKindTagName = "pheme.message.kind";

        /// <summary>
        /// The tag with the <see cref="OutboxMessage.Source"/> of an outbox store: <c>pheme.outbox.source</c>. On the
        /// activity of a delivery, not on its duration measurement, and on the outbox gauges.
        /// </summary>
        public const string OutboxSourceTagName = "pheme.outbox.source";

        /// <summary>
        /// The gauge of how many messages of an outbox store are pending or parked: <c>pheme.outbox.messages</c>, with
        /// <see cref="OutboxSourceTagName"/> and <see cref="OutboxMessageStateTagName"/>.
        /// </summary>
        /// <remarks>Read from the stores only when collected.</remarks>
        public const string OutboxMessagesName = "pheme.outbox.messages";

        /// <summary>
        /// The tag of <see cref="OutboxMessagesName"/> with <see cref="OutboxPendingState"/> or
        /// <see cref="OutboxParkedState"/>: <c>pheme.outbox.message.state</c>.
        /// </summary>
        public const string OutboxMessageStateTagName = "pheme.outbox.message.state";

        /// <summary>
        /// The <see cref="OutboxMessageStateTagName"/> of messages waiting for delivery, also for their next attempt:
        /// <c>pending</c>.
        /// </summary>
        public const string OutboxPendingState = "pending";

        /// <summary>
        /// The <see cref="OutboxMessageStateTagName"/> of messages no longer delivered after too many failed attempts:
        /// <c>parked</c>.
        /// </summary>
        public const string OutboxParkedState = "parked";

        /// <summary>
        /// The gauge of how long ago the oldest pending message of an outbox store occurred, in seconds, 0 when none is
        /// pending: <c>pheme.outbox.oldest_pending.age</c>, with <see cref="OutboxSourceTagName"/>.
        /// </summary>
        /// <remarks>Read from the stores only when collected.</remarks>
        public const string OutboxOldestPendingAgeName = "pheme.outbox.oldest_pending.age";

        /// <summary>
        /// The tag with the full type name of the exception a failed message ended with: <c>error.type</c>, as in the
        /// OpenTelemetry semantic conventions.
        /// </summary>
        public const string ErrorTypeTagName = "error.type";

        /// <summary>
        /// The <see cref="MessageKindTagName"/> of a command, with or without a response: <c>command</c>.
        /// </summary>
        public const string CommandKind = "command";

        /// <summary>
        /// The <see cref="MessageKindTagName"/> of a query: <c>query</c>.
        /// </summary>
        public const string QueryKind = "query";

        /// <summary>
        /// The <see cref="MessageKindTagName"/> of a domain event: <c>domain_event</c>.
        /// </summary>
        public const string DomainEventKind = "domain_event";

        /// <summary>
        /// The <see cref="MessageKindTagName"/> of an integration event delivered from the outbox:
        /// <c>integration_event</c>.
        /// </summary>
        public const string IntegrationEventKind = "integration_event";
    }
}
