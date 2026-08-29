namespace DovahLink.Host.State;

/// <summary>Identifies one authoritative state area, scoped independently within each play context.</summary>
public readonly record struct StateAreaId
{
    /// <summary>The underlying identifier value.</summary>
    public string Value { get; }

    /// <summary>Creates a state area identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public StateAreaId(string value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
