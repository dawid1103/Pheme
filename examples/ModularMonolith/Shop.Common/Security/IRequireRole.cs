namespace Shop.Common.Security
{
    /// <summary>
    /// A message only users in a role may send, checked by the authorization behavior of the API before the message
    /// is validated or handled. The role is part of the message's type, so a caller cannot leave it out.
    /// </summary>
    public interface IRequireRole
    {
        /// <summary>
        /// The role the user sending the message must be in, one of <see cref="Roles"/>.
        /// </summary>
        static abstract string RequiredRole { get; }
    }
}
