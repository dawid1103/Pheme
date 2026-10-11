using Microsoft.EntityFrameworkCore;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// Records what the handlers saw, for the assertions.
    /// </summary>
    public sealed class Probe
    {
        public List<string> Log { get; } = [];

        public List<DbContext> Contexts { get; } = [];

        public Exception? Thrown { get; set; }
    }
}
