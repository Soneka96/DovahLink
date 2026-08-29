namespace DovahLink.Host.Identity;

/// <summary>Identifies one accepted transport connection; sessions cannot cross this boundary.</summary>
public readonly record struct ConnectionId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    public ConnectionId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new connection identity.</summary>
    public static ConnectionId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
