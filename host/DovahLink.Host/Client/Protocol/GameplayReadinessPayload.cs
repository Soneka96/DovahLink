using System.Text.Json;
using System.Text.Json.Serialization;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>gameplay_readiness</c> message payload, per <c>protocol/schema/README.md</c>'s
/// "<c>gameplay_readiness</c>" section. Host-originated only and unsolicited. It carries only the
/// coarse ready / not-ready fact and its per-session sequence: no character, save, location, resource,
/// Adapter, or diagnostic data.
/// </summary>
public sealed record GameplayReadinessPayload
{
    /// <summary>Whether gameplay is ready: the Host's authoritative answer, never inferred by the client.</summary>
    public required bool Ready { get; init; }

    /// <summary>
    /// The positive, strictly increasing per-session publication sequence, starting at 1 for each
    /// admitted session. A client discards a message whose sequence is not newer than the last it
    /// applied for the same session. A non-positive value is malformed on the wire in both directions:
    /// it fails decoding, and it fails encoding so the Host can never emit it.
    /// </summary>
    [JsonConverter(typeof(PositiveSequenceConverter))]
    public required long Sequence { get; init; }

    /// <summary>Reads and writes a strictly positive integer, rejecting zero, negative, fractional, and non-numeric values.</summary>
    private sealed class PositiveSequenceConverter : JsonConverter<long>
    {
        /// <inheritdoc/>
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out long value) || value <= 0)
            {
                throw new JsonException("A readiness sequence must be a positive integer.");
            }

            return value;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            if (value <= 0)
            {
                throw new JsonException("A readiness sequence must be a positive integer.");
            }

            writer.WriteNumberValue(value);
        }
    }
}
