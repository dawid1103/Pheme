namespace Shop.Common.Security
{
    /// <summary>
    /// The user on whose behalf a message is handled, implemented by the API from the authenticated request. Outside a
    /// request, such as in the delivery of an integration event, there is no user.
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>
        /// The name of the user, or <see langword="null"/> when there is none.
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// Tells whether the user is in <paramref name="role"/>; <see langword="false"/> when there is no user.
        /// </summary>
        bool IsInRole(string role);
    }
}
