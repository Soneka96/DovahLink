using System.Text.Json.Serialization;

namespace DovahLink.Host.Trust;

/// <summary>
/// Identifies one specific Known Device incarnation behind a <see cref="TrustRecord.ClientId"/>,
/// distinct from that client identity itself and from <see cref="TrustRecord.ShortId"/>. Internal
/// concurrency/identity infrastructure only: never sent on the wire, never shown to a human, and
/// never derived from <see cref="TrustRecord.ShortId"/> (which is reusable once its record is
/// removed). Lets a caller that captured a Known Device's identity before an asynchronous
/// administrative mutation prove, on completion, that the record it is about to mutate is still the
/// exact same incarnation it originally resolved -- never a later, unrelated incarnation for the
/// same client that happens to reuse the same short ID or display name.
/// </summary>
public readonly record struct KnownDeviceIncarnationId
{
    /// <summary>The underlying identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier wrapping an existing value.</summary>
    /// <param name="value">The underlying identifier value.</param>
    /// <remarks>
    /// Marked as the deserialization constructor because a struct's implicit parameterless
    /// constructor would otherwise take priority and leave <see cref="Value"/> at its default,
    /// since <see cref="Value"/> has no public setter for the serializer to assign afterward.
    /// </remarks>
    [JsonConstructor]
    public KnownDeviceIncarnationId(Guid value)
    {
        Value = value;
    }

    /// <summary>Creates a new, randomly generated identifier.</summary>
    public static KnownDeviceIncarnationId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
