using System.Collections.Concurrent;
using Shipping.Application.PrepareShipment;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Records the pickups instead of booking them, and fails the ones a test asks it to.
    /// </summary>
    public sealed class FakeCarrier : ICarrier
    {
        private readonly ConcurrentQueue<Guid> requestedPickups = new();
        private readonly ConcurrentDictionary<Guid, int> failuresLeft = new();

        /// <summary>
        /// Fails the next <paramref name="times"/> pickups of the order, as an unavailable carrier would; by default
        /// every pickup until <see cref="Repair"/>.
        /// </summary>
        public void Fail(Guid orderId, int times = int.MaxValue) => failuresLeft[orderId] = times;

        /// <summary>
        /// Books the pickups of the order again, as once the carrier is back.
        /// </summary>
        public void Repair(Guid orderId) => failuresLeft.TryRemove(orderId, out _);

        /// <summary>
        /// How many times the pickup of the order was requested, failed requests included.
        /// </summary>
        public int PickupRequestsOf(Guid orderId) => requestedPickups.Count(id => id == orderId);

        public Task SchedulePickup(Guid orderId, CancellationToken cancellationToken)
        {
            requestedPickups.Enqueue(orderId);
            if (failuresLeft.TryGetValue(orderId, out int left) && left > 0)
            {
                failuresLeft[orderId] = left - 1;
                throw new HttpRequestException($"The carrier is unavailable; the pickup of order {orderId} is not booked.");
            }

            return Task.CompletedTask;
        }
    }
}
