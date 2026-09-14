namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies one continuity epoch of the Host's authoritative-state store -- not an Adapter or
/// Host process instance. See <see cref="StateAuthorityLifecycle"/> for how and when this value
/// changes.
/// </summary>
public readonly record struct StateAuthorityId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public StateAuthorityId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
