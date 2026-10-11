namespace Pheme.Registration
{
    /// <summary>
    /// Options without settings: the hook that lets the options infrastructure run
    /// <see cref="HandlerRegistrationValidator"/> on host start (<c>ValidateOnStart</c>) and cache its result, which
    /// <see cref="Sending.Sender"/> reads to validate on first use without a host.
    /// </summary>
    internal sealed class HandlerRegistrationOptions;
}
