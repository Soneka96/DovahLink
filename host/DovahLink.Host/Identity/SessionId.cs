namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies one client connection's session. Valid only for the socket it was created for; a
/// reconnect always receives a fresh value rather than reusing or migrating a prior session's.
/// </summary>
public readonly record struct SessionId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public SessionId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static SessionId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
