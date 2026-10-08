using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Subscription;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// One simulated public client: a fresh <see cref="PublicStateSubscription"/> bound to a recording
/// <see cref="FakePublicConnectionContext"/>, over the harness's real feed. Mirrors a connection's
/// lifetime -- created, subscribed, then torn down with <see cref="Disconnect"/> -- without owning any Host state.
/// </summary>
public sealed class LivePipelineClient
{
    /// <summary>Decodes every message this client's connection received.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Creates a client over an already-composed subscription and connection.</summary>
    /// <param name="subscription">The client's own subscription, not yet bound.</param>
    /// <param name="connection">The recording connection the subscription sends through.</param>
    /// <param name="codec">The codec used to decode what the connection received.</param>
    public LivePipelineClient(PublicStateSubscription subscription, FakePublicConnectionContext connection, IPublicEnvelopeCodec codec)
    {
        Subscription = subscription;
        Connection = connection;
        this.codec = codec;
        subscription.Bind(connection, SessionId.NewId());
    }

    /// <summary>The client's own subscription.</summary>
    public PublicStateSubscription Subscription { get; }

    /// <summary>The recording connection the subscription sends through.</summary>
    public FakePublicConnectionContext Connection { get; }

    /// <summary>Every <c>state_snapshot</c> received so far, in arrival order.</summary>
    public IReadOnlyList<ReceivedSnapshot> Snapshots => DecodeAll<StateSnapshotPayload>(PublicMessageType.StateSnapshot)
        .Select(item => new ReceivedSnapshot(item.Payload.StateArea, item.Payload.Revision, item.Payload.Data, item.Envelope.StateAuthorityId, item.Envelope.PlayContextId))
        .ToArray();

    /// <summary>
    /// Every <c>state_snapshot</c> received on either lane so far: the Control/Recovery lane baselines
    /// first, then the Data lane's keyed-replaceable snapshots. Lets a test detect the same revision
    /// being delivered once on each lane, which <see cref="Snapshots"/> alone cannot see.
    /// </summary>
    public IReadOnlyList<ReceivedSnapshot> AllLaneSnapshots => Snapshots
        .Concat(Connection.SentSnapshots
            .Select(sent => DecodeSnapshot(sent.Payload))
            .OfType<ReceivedSnapshot>())
        .ToArray();

    /// <summary>Every <c>state_event</c> received so far, in arrival order.</summary>
    public IReadOnlyList<StateEventPayload> Events => DecodeAll<StateEventPayload>(PublicMessageType.StateEvent)
        .Select(item => item.Payload)
        .ToArray();

    /// <summary>Number of <c>error</c> messages received so far.</summary>
    public int ErrorCount => Connection.SentPayloads.Count(sent => codec.TryDecode(sent.Payload, out PublicEnvelope? envelope)
        && envelope.MessageType == PublicMessageType.Error);

    /// <summary>Drives a full <c>subscribe</c> exchange for <paramref name="areas"/>, as a caller with no competing Control-lane send would.</summary>
    /// <param name="areas">The complete desired state-area set.</param>
    /// <param name="messageId">The <c>subscribe</c> message id used as the baseline correlation.</param>
    /// <returns>The areas the subscription accepted.</returns>
    public IReadOnlyList<string> Subscribe(IReadOnlyList<string> areas, string messageId = "subscribe-1")
    {
        (IReadOnlyList<string> accepted, _) = Subscription.HandleSubscribe(areas, reservedControlCapacity: 0, baselineCorrelationMessageId: messageId);
        Subscription.SendSupersededSnapshotRequestErrors();
        Subscription.EstablishAcceptedBaselines(accepted, messageId);
        return accepted;
    }

    /// <summary>Tears the client down the way a closed connection does: only per-connection state is destroyed.</summary>
    public void Disconnect() => Subscription.Unsubscribe();

    /// <summary>Decodes one <c>state_snapshot</c> frame.</summary>
    /// <param name="bytes">The encoded frame.</param>
    /// <returns>The decoded snapshot, or <see langword="null"/> when the frame is not a decodable snapshot.</returns>
    private ReceivedSnapshot? DecodeSnapshot(byte[] bytes) =>
        codec.TryDecode(bytes, out PublicEnvelope? envelope)
        && envelope.MessageType == PublicMessageType.StateSnapshot
        && codec.TryDecodePayload(envelope, out StateSnapshotPayload? payload)
            ? new ReceivedSnapshot(payload.StateArea, payload.Revision, payload.Data, envelope.StateAuthorityId, envelope.PlayContextId)
            : null;

    /// <summary>Decodes every received message of one type together with its envelope.</summary>
    /// <typeparam name="TPayload">The payload type for <paramref name="type"/>.</typeparam>
    /// <param name="type">The message type to select.</param>
    private List<(PublicEnvelope Envelope, TPayload Payload)> DecodeAll<TPayload>(PublicMessageType type)
        where TPayload : class
    {
        List<(PublicEnvelope, TPayload)> decoded = [];
        foreach ((byte[] bytes, _) in Connection.SentPayloads)
        {
            if (codec.TryDecode(bytes, out PublicEnvelope? envelope)
                && envelope.MessageType == type
                && codec.TryDecodePayload(envelope, out TPayload? payload))
            {
                decoded.Add((envelope, payload));
            }
        }

        return decoded;
    }
}
