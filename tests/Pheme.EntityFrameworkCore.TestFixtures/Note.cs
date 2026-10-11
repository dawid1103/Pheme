namespace Pheme.EntityFrameworkCore.TestFixtures
{
    /// <summary>
    /// An entity without events, written by handlers.
    /// </summary>
    public class Note(string text)
    {
        public int Id { get; set; }

        public string Text { get; set; } = text;
    }
}
