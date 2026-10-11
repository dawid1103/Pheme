namespace Shop.Common.Errors
{
    /// <summary>
    /// Thrown by a handler when a valid message cannot be carried out in the current state, such as an order of more
    /// items than are available. The API answers 422.
    /// </summary>
    public sealed class BusinessRuleException(string message) : Exception(message);
}
