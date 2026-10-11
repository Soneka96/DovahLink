using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Readiness;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Readiness;

/// <summary>Tests for <see cref="PublicGameplayReadinessPublisher"/>.</summary>
public class PublicGameplayReadinessPublisherTests
{
    /// <summary>Verifies that binding publishes the current value once as sequence 1, on the control lane, with the session, captured authority, and null correlation and client.</summary>
    [Fact]
    public void Bind_PublishesCurrentValueAsSequenceOne()
    {
        var rig = new Rig();

        rig.Bind();

        (PublicEnvelope envelope, GameplayReadinessPayload payload) = Assert.Single(rig.Published());
        Assert.Equal(PublicOutboundLane.ControlOrRecovery, Assert.Single(rig.Connection.SentPayloads).Lane);
        Assert.Equal(PublicMessageType.GameplayReadiness, envelope.MessageType);
        Assert.Equal(rig.SessionId.ToString(), envelope.SessionId);
        Assert.Null(envelope.CorrelationId);
        Assert.Null(envelope.ClientId);
        Assert.Equal(rig.Lifecycle.Current.ToString(), envelope.StateAuthorityId);
        Assert.False(payload.Ready);
        Assert.Equal(1, payload.Sequence);
    }

    /// <summary>Verifies that a session admitted while gameplay is already ready receives true as its initial value.</summary>
    [Fact]
    public void Bind_WhileReady_PublishesReady()
    {
        var rig = new Rig { InitiallyReady = true };

        rig.Bind();

        (_, GameplayReadinessPayload payload) = Assert.Single(rig.Published());
        Assert.True(payload.Ready);
        Assert.Equal(1, payload.Sequence);
    }

    /// <summary>Verifies that each change is published once, in order, with a strictly increasing sequence.</summary>
    [Fact]
    public void Changes_PublishWithStrictlyIncreasingSequence()
    {
        var rig = new Rig();
        rig.Bind();

        rig.Evaluator.SetReady(true);
        rig.Evaluator.SetReady(false);
        rig.Evaluator.SetReady(true);

        Assert.Equal(
            [(false, 1L), (true, 2L), (false, 3L), (true, 4L)],
            rig.Published().Select(message => (message.Payload.Ready, message.Payload.Sequence)).ToArray());
    }

    /// <summary>Verifies that a repeated equivalent notification produces no publication and does not advance the sequence.</summary>
    [Fact]
    public void RepeatedEquivalentNotification_PublishesNothing()
    {
        var rig = new Rig();
        rig.Bind();

        rig.Evaluator.RaiseReadinessChanged(false);
        rig.Evaluator.RaiseReadinessChanged(false);
        rig.Evaluator.SetReady(true);
        rig.Evaluator.RaiseReadinessChanged(true);

        Assert.Equal([1L, 2L], rig.Published().Select(message => message.Payload.Sequence).ToArray());
    }

    /// <summary>Verifies that the publisher re-reads the current value rather than trusting a superseded value carried by the notification.</summary>
    [Fact]
    public void SupersededNotification_PublishesTheCurrentValue()
    {
        var rig = new Rig { InitiallyReady = true };
        rig.Bind();
        rig.Evaluator.IsReady = false;

        rig.Evaluator.RaiseReadinessChanged(true);

        (_, GameplayReadinessPayload latest) = rig.Published()[^1];
        Assert.False(latest.Ready);
        Assert.Equal(2, latest.Sequence);
    }

    /// <summary>Verifies that an authority rotation republishes an unchanged value under the new authority, and that the old authority is never reused.</summary>
    [Fact]
    public void AuthorityRotation_RepublishesUnchangedValueUnderNewAuthority()
    {
        var rig = new Rig();
        rig.Bind();
        string firstAuthority = rig.Lifecycle.Current.ToString();

        rig.Lifecycle.NotifyRotated();

        IReadOnlyList<(PublicEnvelope Envelope, GameplayReadinessPayload Payload)> published = rig.Published();
        Assert.Equal(2, published.Count);
        Assert.Equal(firstAuthority, published[0].Envelope.StateAuthorityId);
        Assert.Equal(rig.Lifecycle.Current.ToString(), published[1].Envelope.StateAuthorityId);
        Assert.NotEqual(published[0].Envelope.StateAuthorityId, published[1].Envelope.StateAuthorityId);
        Assert.False(published[1].Payload.Ready);
        Assert.Equal(2, published[1].Payload.Sequence);
    }

    /// <summary>Verifies that a rotation landing between the authority capture and the value read is retried, so the value is never stamped with an authority it was not read under.</summary>
    [Fact]
    public void RotationDuringRead_IsRetriedAndStampedWithTheNewAuthority()
    {
        var rig = new Rig();
        rig.Bind();
        rig.Connection.SentPayloads.Clear();
        rig.Lifecycle.OnCurrentRead = () => rig.Lifecycle.NotifyRotated();

        rig.Evaluator.SetReady(true);

        string currentAuthority = rig.Lifecycle.Current.ToString();
        Assert.All(rig.Published(), message => Assert.Equal(currentAuthority, message.Envelope.StateAuthorityId));
        Assert.True(rig.Published()[^1].Payload.Ready);
    }

    /// <summary>Verifies that unbinding stops all later publication and detaches from the shared signals, including a notification already queued.</summary>
    [Fact]
    public void Unbind_StopsPublicationAndDetaches()
    {
        var rig = new Rig();
        rig.Bind();
        rig.Connection.SentPayloads.Clear();

        rig.Publisher.Unbind();
        rig.Evaluator.SetReady(true);
        rig.Evaluator.RaiseReadinessChanged(false);
        rig.Lifecycle.NotifyRotated();

        Assert.Empty(rig.Connection.SentPayloads);
        Assert.False(rig.Evaluator.HasSubscribers);
        Assert.False(rig.Lifecycle.HasSubscribers);
        Assert.Equal(0, rig.Connection.RequestCloseCalls);
    }

    /// <summary>Verifies that binding after unbinding, or binding twice, never publishes again.</summary>
    [Fact]
    public void BindAfterUnbindOrTwice_PublishesNothingMore()
    {
        var rig = new Rig();
        rig.Bind();
        rig.Bind();
        Assert.Single(rig.Connection.SentPayloads);

        rig.Publisher.Unbind();
        rig.Bind();

        Assert.Single(rig.Connection.SentPayloads);
        Assert.False(rig.Evaluator.HasSubscribers);
    }

    /// <summary>Verifies that unbinding a publisher that was never bound is harmless.</summary>
    [Fact]
    public void UnbindWithoutBind_DoesNotThrow()
    {
        var rig = new Rig();

        Exception? exception = Record.Exception(rig.Publisher.Unbind);

        Assert.Null(exception);
    }

    /// <summary>Verifies that a failed control-lane admission closes the connection once through the existing policy and ends publication for the session.</summary>
    [Fact]
    public void SendFailure_ClosesConnectionAndEndsPublication()
    {
        var rig = new Rig();
        rig.Bind();
        rig.Connection.TrySendResult = false;
        rig.Evaluator.SetReady(true);
        Assert.Equal(1, rig.Connection.RequestCloseCalls);

        // The connection recovers, but this session already failed closed and must stay silent.
        rig.Connection.TrySendResult = true;
        rig.Connection.SentPayloads.Clear();
        rig.Evaluator.SetReady(false);

        Assert.Equal(1, rig.Connection.RequestCloseCalls);
        Assert.Empty(rig.Connection.SentPayloads);
        Assert.False(rig.Evaluator.HasSubscribers);
    }

    /// <summary>Verifies that a failure on the initial publication also closes the connection rather than leaving the session without any value.</summary>
    [Fact]
    public void InitialSendFailure_ClosesConnection()
    {
        var rig = new Rig();
        rig.Connection.TrySendResult = false;

        rig.Bind();

        Assert.Equal(1, rig.Connection.RequestCloseCalls);
        Assert.False(rig.Evaluator.HasSubscribers);
    }

    /// <summary>Verifies that a faulted authority lifecycle, which has no safe authority to stamp, fails the connection closed without sending.</summary>
    [Fact]
    public void FaultedLifecycle_FailsClosedWithoutSending()
    {
        var rig = new Rig(new ThrowingLifecycle());

        rig.Bind();

        Assert.Empty(rig.Connection.SentPayloads);
        Assert.Equal(1, rig.Connection.RequestCloseCalls);
    }

    /// <summary>Verifies that two sessions on one Host publish independently: each starts at sequence 1, and a later session receives the current value, not the earlier session history.</summary>
    [Fact]
    public void TwoSessions_HaveIndependentSequences()
    {
        var evaluator = new FakeGameplayReadinessEvaluator();
        var lifecycle = new FakeStateAuthorityLifecycle();
        var first = new Rig(evaluator: evaluator, lifecycle: lifecycle);
        first.Bind();
        evaluator.SetReady(true);
        evaluator.SetReady(false);
        evaluator.SetReady(true);

        var second = new Rig(evaluator: evaluator, lifecycle: lifecycle);
        second.Bind();
        evaluator.SetReady(false);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], first.Published().Select(message => message.Payload.Sequence).ToArray());
        Assert.Equal([1L, 2L], second.Published().Select(message => message.Payload.Sequence).ToArray());
        Assert.True(second.Published()[0].Payload.Ready);
        Assert.NotEqual(first.SessionId.ToString(), second.SessionId.ToString());
        Assert.All(second.Published(), message => Assert.Equal(second.SessionId.ToString(), message.Envelope.SessionId));
    }

    /// <summary>Verifies that ending one session does not stop another session sharing the same evaluator.</summary>
    [Fact]
    public void EndingOneSession_DoesNotAffectAnother()
    {
        var evaluator = new FakeGameplayReadinessEvaluator();
        var lifecycle = new FakeStateAuthorityLifecycle();
        var ended = new Rig(evaluator: evaluator, lifecycle: lifecycle);
        var live = new Rig(evaluator: evaluator, lifecycle: lifecycle);
        ended.Bind();
        live.Bind();

        ended.Publisher.Unbind();
        evaluator.SetReady(true);

        Assert.Single(ended.Published());
        Assert.Equal([1L, 2L], live.Published().Select(message => message.Payload.Sequence).ToArray());
    }

    /// <summary>Verifies the privacy boundary: the payload carries exactly the two coarse members, and the play context id is the current opaque one.</summary>
    [Fact]
    public void PublishedMessage_CarriesOnlyCoarseMembersAndTheCurrentPlayContextId()
    {
        var rig = new Rig { InitiallyReady = true };
        var context = PlayContextId.NewId();
        rig.PlayContext.NotifyTransition(context);

        rig.Bind();

        (PublicEnvelope envelope, _) = Assert.Single(rig.Published());
        Assert.Equal(
            ["ready", "sequence"],
            envelope.Payload.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(context.ToString(), envelope.PlayContextId);
    }

    /// <summary>Verifies that publishing outside an active play context stamps a null play context id.</summary>
    [Fact]
    public void PublishedMessage_WithoutPlayContext_HasNullPlayContextId()
    {
        var rig = new Rig();

        rig.Bind();

        (PublicEnvelope envelope, _) = Assert.Single(rig.Published());
        Assert.Null(envelope.PlayContextId);
    }

    /// <summary>Verifies that an authority that never stabilizes across the validated read fails the connection closed instead of publishing a guess.</summary>
    [Fact]
    public void AuthorityThatNeverStabilizes_FailsClosedWithoutSending()
    {
        var rig = new Rig(new ConstantlyRotatingLifecycle());

        rig.Bind();

        Assert.Empty(rig.Connection.SentPayloads);
        Assert.Equal(1, rig.Connection.RequestCloseCalls);
        Assert.False(rig.Evaluator.HasSubscribers);
    }

    /// <summary>
    /// Verifies under concurrent serialized change notifications that every message is delivered in
    /// wire order with consecutive sequence numbers from 1, and that the last message matches the
    /// final current value.
    /// </summary>
    [Fact]
    public async Task ConcurrentChanges_PublishConsecutiveSequencesInWireOrder()
    {
        var rig = new Rig();
        rig.Bind();
        object serialize = new();
        bool next = true;

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                // The real evaluator serializes change notifications; the fake needs the same.
                lock (serialize)
                {
                    rig.Evaluator.SetReady(next);
                    next = !next;
                }
            }
        })));

        IReadOnlyList<(PublicEnvelope Envelope, GameplayReadinessPayload Payload)> published = rig.Published();
        Assert.Equal(
            Enumerable.Range(1, published.Count).Select(value => (long)value).ToArray(),
            published.Select(message => message.Payload.Sequence).ToArray());
        Assert.Equal(rig.Evaluator.IsReady, published[^1].Payload.Ready);
        Assert.Equal(801, published.Count);
    }

    /// <summary>Verifies binding rejects a missing connection.</summary>
    [Fact]
    public void Bind_NullConnection_Throws()
    {
        var rig = new Rig();

        Assert.Throws<ArgumentNullException>(() => rig.Publisher.Bind(null!, SessionId.NewId()));
    }

    /// <summary>Verifies the constructor rejects missing collaborators.</summary>
    [Fact]
    public void Constructor_NullCollaborators_Throw()
    {
        var evaluator = new FakeGameplayReadinessEvaluator();
        var codec = new PublicEnvelopeCodec();
        var playContext = new FakePlayContextTracker();
        var lifecycle = new FakeStateAuthorityLifecycle();

        Assert.Throws<ArgumentNullException>(() => new PublicGameplayReadinessPublisher(null!, codec, playContext, lifecycle));
        Assert.Throws<ArgumentNullException>(() => new PublicGameplayReadinessPublisher(evaluator, null!, playContext, lifecycle));
        Assert.Throws<ArgumentNullException>(() => new PublicGameplayReadinessPublisher(evaluator, codec, null!, lifecycle));
        Assert.Throws<ArgumentNullException>(() => new PublicGameplayReadinessPublisher(evaluator, codec, playContext, null!));
    }

    /// <summary>A publisher, its fakes, and one fake connection for one session.</summary>
    private sealed class Rig
    {
        private readonly PublicEnvelopeCodec codec = new();

        public Rig(IStateAuthorityLifecycle? lifecycleOverride = null, FakeGameplayReadinessEvaluator? evaluator = null, FakeStateAuthorityLifecycle? lifecycle = null)
        {
            Evaluator = evaluator ?? new FakeGameplayReadinessEvaluator();
            Lifecycle = lifecycle ?? new FakeStateAuthorityLifecycle();
            PlayContext = new FakePlayContextTracker();
            Publisher = new PublicGameplayReadinessPublisher(Evaluator, codec, PlayContext, lifecycleOverride ?? Lifecycle);
        }

        public FakeGameplayReadinessEvaluator Evaluator { get; }

        public FakeStateAuthorityLifecycle Lifecycle { get; }

        public FakePlayContextTracker PlayContext { get; }

        public FakePublicConnectionContext Connection { get; } = new();

        public PublicGameplayReadinessPublisher Publisher { get; }

        public SessionId SessionId { get; } = SessionId.NewId();

        public bool InitiallyReady
        {
            set => Evaluator.IsReady = value;
        }

        public void Bind() => Publisher.Bind(Connection, SessionId);

        /// <summary>Decodes every readiness message this session's connection received, in wire order.</summary>
        public IReadOnlyList<(PublicEnvelope Envelope, GameplayReadinessPayload Payload)> Published()
        {
            var result = new List<(PublicEnvelope, GameplayReadinessPayload)>();
            foreach ((byte[] bytes, _) in Connection.SentPayloads)
            {
                Assert.True(codec.TryDecode(bytes, out PublicEnvelope? envelope));
                Assert.True(codec.TryDecodePayload(envelope!, out GameplayReadinessPayload? payload));
                result.Add((envelope!, payload!));
            }

            return result;
        }
    }

    /// <summary>An authority lifecycle whose value changes on every read, so no read is ever stable.</summary>
    private sealed class ConstantlyRotatingLifecycle : IStateAuthorityLifecycle
    {
        public StateAuthorityId Current => new(Guid.NewGuid());

        public bool IsFaulted => false;

        public event Action? FatalFailureOccurred
        {
            add { }
            remove { }
        }

        public event Action<StateAuthorityId>? Rotated
        {
            add { }
            remove { }
        }
    }

    /// <summary>An authority lifecycle in the faulted state: it has no safe value, so reading it throws.</summary>
    private sealed class ThrowingLifecycle : IStateAuthorityLifecycle
    {
        public StateAuthorityId Current => throw new InvalidOperationException("faulted");

        public bool IsFaulted => true;

        public event Action? FatalFailureOccurred
        {
            add { }
            remove { }
        }

        public event Action<StateAuthorityId>? Rotated
        {
            add { }
            remove { }
        }
    }
}
