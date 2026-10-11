namespace Pheme.UnitTests
{
    public class OutboxContractTests
    {
        [Fact]
        public void Options_defaults()
        {
            var options = new OutboxOptions();

            Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
            Assert.Equal(20, options.BatchSize);
            Assert.Equal(10, options.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay);
            Assert.Equal(TimeSpan.FromMinutes(15), options.MaxRetryDelay);
        }

        [Fact]
        public void Retry_delay_doubles_after_each_failed_attempt_up_to_MaxRetryDelay()
        {
            var options = new OutboxOptions();

            TimeSpan[] delays = [.. Enumerable.Range(1, 9).Select(options.GetRetryDelay)];

            Assert.Equal([10, 20, 40, 80, 160, 320, 640, 900, 900], delays.Select(delay => delay.TotalSeconds));
            Assert.Equal(TimeSpan.FromMinutes(15), options.GetRetryDelay(int.MaxValue));
        }

        [Fact]
        public void Retry_delay_of_zero_retries_at_once_and_MaxRetryDelay_below_it_caps_every_delay()
        {
            var options = new OutboxOptions { RetryDelay = TimeSpan.Zero };

            Assert.Equal(TimeSpan.Zero, options.GetRetryDelay(int.MaxValue));

            options.RetryDelay = TimeSpan.FromMinutes(1);
            options.MaxRetryDelay = TimeSpan.FromSeconds(30);

            Assert.Equal(TimeSpan.FromSeconds(30), options.GetRetryDelay(1));
        }

        [Fact]
        public void RetryDelay_negative_is_rejected()
        {
            var options = new OutboxOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.RetryDelay = TimeSpan.FromTicks(-1));
            Assert.Equal(TimeSpan.FromSeconds(10), options.RetryDelay);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxRetryDelay_not_positive_is_rejected(int milliseconds)
        {
            var options = new OutboxOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxRetryDelay = TimeSpan.FromMilliseconds(milliseconds));
            Assert.Equal(TimeSpan.FromMinutes(15), options.MaxRetryDelay);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void PollingInterval_not_positive_is_rejected(int milliseconds)
        {
            var options = new OutboxOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.PollingInterval = TimeSpan.FromMilliseconds(milliseconds));
            Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void BatchSize_below_1_is_rejected(int value)
        {
            var options = new OutboxOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.BatchSize = value);
            Assert.Equal(20, options.BatchSize);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxAttempts_below_1_is_rejected(int value)
        {
            var options = new OutboxOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxAttempts = value);
            Assert.Equal(10, options.MaxAttempts);
        }

        [Fact]
        public void Integration_event_name_is_not_inherited()
        {
            Assert.NotNull(typeof(NamedEvent).GetCustomAttributes(typeof(IntegrationEventNameAttribute), inherit: true).SingleOrDefault());
            Assert.Empty(typeof(DerivedEvent).GetCustomAttributes(typeof(IntegrationEventNameAttribute), inherit: true));
        }

        [IntegrationEventName("tests.named")]
        private class NamedEvent;

        private sealed class DerivedEvent : NamedEvent;
    }
}
