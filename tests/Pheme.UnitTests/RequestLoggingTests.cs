using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pheme.UnitTests
{
    public class RequestLoggingTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Handled_message_is_logged_on_debug_with_kind_type_and_elapsed_time()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services.AddPheme(options => options.AddRequestLogging()));
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new LoggedCommand(), Token);
            await sender.Send(new LoggedQuery(), Token);

            Assert.Collection(
                logs.Entries,
                entry => AssertHandled(entry, "command", "Pheme.UnitTests.LoggedCommand"),
                entry => AssertHandled(entry, "query", "Pheme.UnitTests.LoggedQuery"));

            static void AssertHandled(LogEntry entry, string kind, string type)
            {
                Assert.Equal(RequestLoggingOptions.CategoryName, entry.Category);
                Assert.Equal(LogLevel.Debug, entry.Level);
                Assert.Equal(new EventId(1, "MessageHandled"), entry.EventId);
                Assert.Matches($@"^{kind} {type.Replace(".", @"\.", StringComparison.Ordinal)} handled in \d+\.\d ms$", entry.Message);
                Assert.Equal(kind, entry.Properties["MessageKind"]);
                Assert.Equal(type, entry.Properties["MessageType"]);
                Assert.True((double)entry.Properties["ElapsedMs"]! >= 0);
                Assert.Null(entry.Exception);
            }
        }

        [Fact]
        public async Task Handled_message_is_logged_on_the_configured_level()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddPheme(options => options.AddRequestLogging(logging => logging.SuccessLevel = LogLevel.Information)));

            await Send(provider, new LoggedCommand());

            LogEntry entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
        }

        [Fact]
        public async Task Expected_exception_is_logged_with_its_message_and_without_stack_trace()
        {
            var failure = new ExpectedLoggedException("Order is closed.");
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging(logging => logging.IsExpected = exception => exception is ExpectedLoggedException)));

            Assert.Same(failure, await Assert.ThrowsAsync<ExpectedLoggedException>(() => Send(provider, new FailingLoggedCommand())));

            LogEntry entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal(new EventId(2, "MessageExpectedFailure"), entry.EventId);
            Assert.Null(entry.Exception);
            Assert.Matches(
                @"^command Pheme\.UnitTests\.FailingLoggedCommand failed in \d+\.\d ms with Pheme\.UnitTests\.ExpectedLoggedException: Order is closed\.$",
                entry.Message);
            Assert.Equal("Pheme.UnitTests.ExpectedLoggedException", entry.Properties["ExceptionType"]);
            Assert.Equal("Order is closed.", entry.Properties["ExceptionMessage"]);
        }

        [Fact]
        public async Task Expected_exception_is_logged_on_the_configured_level()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(new ExpectedLoggedException("Order is closed."))
                .AddPheme(options => options.AddRequestLogging(logging =>
                {
                    logging.IsExpected = exception => exception is ExpectedLoggedException;
                    logging.ExpectedFailureLevel = LogLevel.Warning;
                })));

            await Assert.ThrowsAsync<ExpectedLoggedException>(() => Send(provider, new FailingLoggedCommand()));

            Assert.Equal(LogLevel.Warning, Assert.Single(logs.Entries).Level);
        }

        [Fact]
        public async Task Unexpected_exception_is_logged_as_error_with_the_exception_and_propagated()
        {
            var failure = new InvalidOperationException("Database is down.");
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging(logging => logging.IsExpected = exception => exception is ExpectedLoggedException)));

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new FailingLoggedCommand()));

            Assert.Same(failure, thrown);
            Assert.Contains(nameof(FailingLoggedHandler), thrown.StackTrace, StringComparison.Ordinal);
            LogEntry entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Equal(new EventId(3, "MessageFailed"), entry.EventId);
            Assert.Same(failure, entry.Exception);
            Assert.Matches(
                @"^command Pheme\.UnitTests\.FailingLoggedCommand failed in \d+\.\d ms with System\.InvalidOperationException$",
                entry.Message);
        }

        [Fact]
        public async Task Unexpected_exception_is_not_logged_on_level_none_and_still_propagated()
        {
            var failure = new InvalidOperationException("Database is down.");
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging(logging => logging.UnexpectedFailureLevel = LogLevel.None)));

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new FailingLoggedCommand())));

            Assert.Empty(logs.Entries);
        }

        [Fact]
        public async Task Exception_propagates_unchanged_when_the_IsExpected_predicate_throws()
        {
            var failure = new InvalidOperationException("Database is down.");
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging(logging => logging.IsExpected = _ => throw new FormatException())));

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new FailingLoggedCommand())));
        }

        [Fact]
        public async Task Cancellation_requested_by_the_caller_is_expected()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services.AddPheme(options => options.AddRequestLogging()));
            using IServiceScope scope = provider.CreateScope();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new CancelableLoggedCommand(), cancellation.Token));

            LogEntry entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Null(entry.Exception);
        }

        [Fact]
        public async Task Cancellation_not_requested_by_the_caller_is_unexpected()
        {
            var failure = new TaskCanceledException("HTTP request timed out.");
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging()));

            Assert.Same(failure, await Assert.ThrowsAsync<TaskCanceledException>(() => Send(provider, new FailingLoggedCommand())));

            LogEntry entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(failure, entry.Exception);
        }

        [Fact]
        public async Task Request_logging_is_the_outermost_behavior_regardless_of_the_order_of_AddBehavior()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddPheme(options => options.AddBehavior(typeof(ShortCircuitBehavior<>)).AddRequestLogging())
                .AddPheme(options => options.AddBehavior(typeof(OuterBehavior<,>))));

            // The short-circuit behavior skips everything inside it, so the request is logged only from outside it.
            int response = await Send(provider, new LoggedQuery());

            Assert.Equal(-1, response);
            Assert.Equal(LogLevel.Debug, Assert.Single(logs.Entries).Level);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Request_logging_added_by_several_AddPheme_calls_logs_once_with_options_configured_in_call_order()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .AddPheme(options => options.AddRequestLogging(logging =>
                {
                    logging.SuccessLevel = LogLevel.Warning;
                    logging.UnexpectedFailureLevel = LogLevel.Critical;
                }))
                .AddPheme(options => options.AddRequestLogging())
                .AddPheme(options => options.AddRequestLogging(logging => logging.SuccessLevel = LogLevel.Information)));

            await Send(provider, new LoggedCommand());

            Assert.Equal(LogLevel.Information, Assert.Single(logs.Entries).Level);
            RequestLoggingOptions configured = provider.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;
            Assert.Equal(LogLevel.Critical, configured.UnexpectedFailureLevel);
        }

        [Fact]
        public async Task Options_configured_with_services_Configure_are_honoured()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services
                .Configure<RequestLoggingOptions>(logging => logging.SuccessLevel = LogLevel.Warning)
                .AddPheme(options => options.AddRequestLogging()));

            await Send(provider, new LoggedCommand());

            Assert.Equal(LogLevel.Warning, Assert.Single(logs.Entries).Level);
        }

        [Fact]
        public async Task Logger_throwing_on_success_does_not_fail_the_handled_message()
        {
            using ServiceProvider provider = BuildProvider(new ThrowingLoggerFactory(), services => services
                .AddPheme(options => options.AddRequestLogging()));

            await Send(provider, new LoggedCommand());

            Assert.Equal(["handler"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Logger_throwing_on_failure_does_not_replace_the_exception()
        {
            var failure = new InvalidOperationException("Database is down.");
            using ServiceProvider provider = BuildProvider(new ThrowingLoggerFactory(), services => services
                .AddSingleton<Exception>(failure)
                .AddPheme(options => options.AddRequestLogging()));

            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new FailingLoggedCommand())));
        }

        [Fact]
        public async Task Request_logging_without_a_logger_factory_fails_on_first_send_with_the_dependency_injection_error()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<PipelineLog>()
                .AddTransient<ICommandHandler<LoggedCommand>, LoggedHandler>()
                .AddPheme(options => options.AddRequestLogging())
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Send(provider, new LoggedCommand()));

            Assert.Contains("'Microsoft.Extensions.Logging.ILoggerFactory'", exception.Message, StringComparison.Ordinal);
            Assert.Empty(provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Without_request_logging_nothing_is_logged()
        {
            var logs = new TestLoggerFactory();
            using ServiceProvider provider = BuildProvider(logs, services => services.AddPheme(_ => { }));

            await Send(provider, new LoggedCommand());

            Assert.Empty(logs.Entries);
        }

        [Fact]
        public void Options_have_documented_defaults_and_reject_invalid_values()
        {
            var options = new RequestLoggingOptions();

            Assert.Equal(LogLevel.Debug, options.SuccessLevel);
            Assert.Equal(LogLevel.Information, options.ExpectedFailureLevel);
            Assert.Equal(LogLevel.Error, options.UnexpectedFailureLevel);
            Assert.False(options.IsExpected(new InvalidOperationException()));
            Assert.Throws<ArgumentOutOfRangeException>("value", () => options.SuccessLevel = (LogLevel)42);
            Assert.Throws<ArgumentOutOfRangeException>("value", () => options.ExpectedFailureLevel = (LogLevel)42);
            Assert.Throws<ArgumentOutOfRangeException>("value", () => options.UnexpectedFailureLevel = (LogLevel)42);
            Assert.Throws<ArgumentNullException>("value", () => options.IsExpected = null!);
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

        private static ServiceProvider BuildProvider(ILoggerFactory logs, Action<IServiceCollection> configure)
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(logs)
                .AddSingleton<PipelineLog>()
                .AddTransient<ICommandHandler<LoggedCommand>, LoggedHandler>()
                .AddTransient<IQueryHandler<LoggedQuery, int>, LoggedHandler>()
                .AddTransient<ICommandHandler<FailingLoggedCommand>, FailingLoggedHandler>()
                .AddTransient<ICommandHandler<CancelableLoggedCommand>, FailingLoggedHandler>();
            configure(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    internal sealed record FailingLoggedCommand : ICommand;

    internal sealed record CancelableLoggedCommand : ICommand;

    internal sealed class ExpectedLoggedException(string message) : Exception(message);

    internal sealed class FailingLoggedHandler(IServiceProvider services) : ICommandHandler<FailingLoggedCommand>, ICommandHandler<CancelableLoggedCommand>
    {
        public async Task Handle(FailingLoggedCommand command, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw services.GetRequiredService<Exception>();
        }

        public Task Handle(CancelableLoggedCommand command, CancellationToken cancellationToken)
        {
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    internal sealed class ThrowingLoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ThrowingLogger();
        }

        public void AddProvider(ILoggerProvider provider)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        private sealed class ThrowingLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                throw new FormatException("Log provider failed.");
            }
        }
    }

    internal sealed record LogEntry(
        string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);

    internal sealed class TestLoggerFactory : ILoggerFactory
    {
        private readonly List<LogEntry> entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (entries)
                {
                    return [.. entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(categoryName, this);
        }

        public void AddProvider(ILoggerProvider provider)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        private void Add(LogEntry entry)
        {
            lock (entries)
            {
                entries.Add(entry);
            }
        }

        private sealed class TestLogger(string category, TestLoggerFactory factory) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var properties = ((IReadOnlyList<KeyValuePair<string, object?>>)state!).ToDictionary();
                factory.Add(new LogEntry(category, logLevel, eventId, formatter(state, exception), exception, properties));
            }
        }
    }
}
