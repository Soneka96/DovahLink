namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies the current Skyrim play context (one save/load lifetime). The host owns this
/// identity's authoritative value once the adapter notifies a transition; loading another save
/// always produces a new value.
/// </summary>
public readonly record struct PlayContextId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public PlayContextId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static PlayContextId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
