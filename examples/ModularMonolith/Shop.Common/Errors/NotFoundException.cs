namespace Shop.Common.Errors
{
    /// <summary>
    /// Thrown by a handler when the resource a message refers to does not exist. The API answers 404.
    /// </summary>
    public sealed class NotFoundException(string message) : Exception(message);
}
