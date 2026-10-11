using Microsoft.AspNetCore.Mvc;
using Pheme;

namespace Shop.Api.Controllers
{
    /// <summary>
    /// Administration of the modules' outboxes: the parked messages and their requeueing once the cause of their
    /// failure is fixed. The example has no authentication; an application keeps such endpoints to its administrators.
    /// </summary>
    [ApiController]
    [Route("admin/outbox")]
    public sealed class OutboxController(IOutboxMaintenance maintenance) : ControllerBase
    {
        /// <summary>
        /// Lists the oldest parked messages of all modules, with their errors: 200.
        /// </summary>
        [HttpGet("parked")]
        public Task<IReadOnlyList<OutboxMessage>> GetParked(CancellationToken cancellationToken) =>
            maintenance.GetParked(cancellationToken: cancellationToken);

        /// <summary>
        /// Makes a message pending again with no failed attempts; the handlers that completed are not called again: 204,
        /// or 404 when no outbox has the message unprocessed.
        /// </summary>
        [HttpPost("{id:guid}/requeue")]
        public async Task<IActionResult> Requeue(Guid id, CancellationToken cancellationToken) =>
            await maintenance.Requeue(id, cancellationToken) ? NoContent() : NotFound();
    }
}
