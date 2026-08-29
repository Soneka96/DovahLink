namespace DovahLink.Host.State;

/// <summary>
/// A state area's revision within one play context. Advances only when that area's authoritative
/// state actually changes; sending or requesting another snapshot does not advance it.
/// </summary>
public readonly record struct RevisionNumber
{
    /// <summary>The underlying revision value.</summary>
    public ulong Value { get; }

    /// <summary>Creates a revision number wrapping an existing value.</summary>
    /// <param name="value">The underlying revision value.</param>
    public RevisionNumber(ulong value)
    {
        Value = value;
    }

    /// <summary>The starting revision for a state area that has never changed.</summary>
    public static RevisionNumber Initial => new(0);

    /// <summary>Returns the next revision after this one.</summary>
    public RevisionNumber Next() => new(Value + 1);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
