namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies a paired client across its persistent trust lifetime. Owned by the host; unrelated
/// to any single connection's <see cref="SessionId"/>.
/// </summary>
public readonly record struct ClientId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public ClientId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static ClientId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
