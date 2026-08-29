namespace DovahLink.Host.Identity;

/// <summary>
/// Identifies one native adapter connection instance. An adapter restart (SKSE plugin reload or a
/// Skyrim process restart) always produces a new value; it is the host-observed successor to the
/// old single-process <c>bridgeInstanceId</c> concept.
/// </summary>
public readonly record struct AdapterInstanceId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public AdapterInstanceId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static AdapterInstanceId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
