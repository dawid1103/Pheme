using System.Text.Json;

namespace Pheme.Outbox
{
    /// <summary>
    /// The JSON format of <see cref="OutboxMessage.Content"/>, shared by the message factory and the dispatcher.
    /// </summary>
    internal static class OutboxSerialization
    {
        /// <summary>
        /// The web defaults: camel case properties, read case-insensitively, so content written by
        /// <see cref="JsonSerializer"/> with its default options reads as well.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
