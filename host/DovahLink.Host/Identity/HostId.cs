namespace DovahLink.Host.Identity;

/// <summary>Identifies one persistent DovahLink Host installation, independently of its machine name or endpoint.</summary>
public readonly record struct HostId
{
    /// <summary>The underlying non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping a non-empty UUID.</summary>
    /// <param name="value">The UUID identifying the Host installation.</param>
    /// <exception cref="ArgumentException">The UUID is empty.</exception>
    public HostId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A Host ID must not be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Creates a new, cryptographically random UUID for a Host installation.</summary>
    public static HostId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("D");
}
