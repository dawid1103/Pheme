namespace Shop.Common.Errors
{
    /// <summary>
    /// Thrown when a message is invalid. The API answers 400 with the errors per field.
    /// </summary>
    public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : Exception("One or more validation errors occurred.")
    {
        /// <summary>
        /// The error messages, keyed by the name of the invalid field.
        /// </summary>
        public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    }
}
