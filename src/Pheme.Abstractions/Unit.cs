namespace Pheme
{
    /// <summary>
    /// The response type of an <see cref="ICommand"/> inside the pipeline - a type with a single value.
    /// </summary>
    /// <remarks>
    /// Only pipeline behaviors see it: a behavior handling all messages receives <see cref="Unit"/> as the
    /// response of a command without a response. Handlers and callers of <see cref="ISender"/> use
    /// <see cref="Task"/> instead. All values are equal; use <see langword="default"/>.
    /// </remarks>
    public readonly record struct Unit;
}
