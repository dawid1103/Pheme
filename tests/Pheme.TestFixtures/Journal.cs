namespace Pheme.TestFixtures
{
    /// <summary>
    /// Shared state written by the fixture handlers, registered by the test as a singleton.
    /// </summary>
    public sealed class Journal
    {
        public List<string> Entries { get; } = [];
    }
}
