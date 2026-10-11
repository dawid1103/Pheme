namespace Shop.Common.Errors
{
    /// <summary>
    /// Thrown when the user may not send a message, such as a warehouse command sent by someone outside the warehouse
    /// staff. The API answers 403.
    /// </summary>
    public sealed class ForbiddenException(string message) : Exception(message);
}
