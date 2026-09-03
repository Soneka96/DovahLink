namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies one specific pairing challenge instance, distinct from the client that owns it. Lets a
/// caller that reserved a challenge before an asynchronous adapter or persistence step prove, on
/// return, that the challenge it is about to commit or roll back is still the exact same one --
/// never a later challenge that happens to share the same owning <see cref="ClientId"/>.
/// </summary>
public readonly record struct ChallengeId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public ChallengeId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static ChallengeId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
