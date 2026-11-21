namespace DispatchWeave;

/// <summary>
/// Represents a void response type for requests that do not return a value.
/// </summary>
public readonly record struct Unit : IComparable<Unit>, IComparable
{
    /// <summary>
    /// The single default <see cref="Unit"/> value.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// A completed task returning <see cref="Value"/>.
    /// </summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => 0;

    /// <inheritdoc />
    public override string ToString() => "()";
}
