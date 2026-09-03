using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Client.Dispatch;

/// <summary>
/// The host's authenticated application dispatcher for the canonical <c>ping</c>, pairing, and
/// <c>rename_request</c> client messages: it maps them to their owning host services and turns the
/// typed outcomes into canonical responses. The sole boundary where client intent becomes host
/// pairing/trust/session behavior; no client request reaches Skyrim code directly. Session admission,
/// envelope decoding, and per-tier message authorization all happen upstream of this dispatcher.
/// </summary>
public interface IClientMessageDispatcher
{
    /// <summary>
    /// Dispatches one authenticated, already-authorized client message to its owning host service, and
    /// sends the canonical response or error directly on <paramref name="connection"/>.
    /// </summary>
    /// <param name="clientId">The connection's admitted client identity.</param>
    /// <param name="sessionId">The connection's admitted session identity.</param>
    /// <param name="connection">The connection to send the response or error on.</param>
    /// <param name="envelope">The already-decoded, already-authorized client envelope to dispatch.</param>
    /// <param name="cancellationToken">The token used to cancel any awaited service call.</param>
    /// <returns>The dispatch's side effects for the caller to apply.</returns>
    Task<ClientDispatchResult> DispatchAsync(
        ClientId clientId,
        SessionId sessionId,
        IPublicConnectionContext connection,
        PublicEnvelope envelope,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IClientMessageDispatcher"/>
public sealed class ClientMessageDispatcher : IClientMessageDispatcher
{
    /// <summary>Decodes and encodes every message this dispatcher sends or receives.</summary>
    private readonly IPublicEnvelopeCodec codec;

    /// <summary>Applies rename mutations to the durable trust store.</summary>
    private readonly ITrustAdminService trustAdminService;

    /// <summary>Owns the host's client-bound pairing challenge and pending-credential lifecycle.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>The injected, narrow host-owned seam used to request a Skyrim-facing pairing notification.</summary>
    private readonly IPairingAdapterNotifier adapterNotifier;

    /// <summary>Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>The time source used to compute a challenge's remaining code validity.</summary>
    private readonly IClock clock;

    /// <summary>Creates a client message dispatcher.</summary>
    /// <param name="codec">Decodes and encodes every message this dispatcher sends or receives.</param>
    /// <param name="trustAdminService">Applies rename mutations to the durable trust store.</param>
    /// <param name="pairingCoordinator">Owns the host's client-bound pairing challenge and pending-credential lifecycle.</param>
    /// <param name="adapterNotifier">The injected, narrow host-owned seam used to request a Skyrim-facing pairing notification.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</param>
    /// <param name="clock">The time source used to compute a challenge's remaining code validity.</param>
    public ClientMessageDispatcher(
        IPublicEnvelopeCodec codec,
        ITrustAdminService trustAdminService,
        IPairingCoordinator pairingCoordinator,
        IPairingAdapterNotifier adapterNotifier,
        IPlayContextTracker playContextTracker,
        IClock clock)
    {
        this.codec = codec;
        this.trustAdminService = trustAdminService;
        this.pairingCoordinator = pairingCoordinator;
        this.adapterNotifier = adapterNotifier;
        this.playContextTracker = playContextTracker;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public Task<ClientDispatchResult> DispatchAsync(
        ClientId clientId,
        SessionId sessionId,
        IPublicConnectionContext connection,
        PublicEnvelope envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.MessageType)
        {
            case PublicMessageType.Ping:
                return HandlePingAsync(sessionId, connection, envelope);
            case PublicMessageType.RenameRequest:
                return HandleRenameRequestAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingRequest:
                return HandlePairingRequestAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingConfirm:
                return HandlePairingConfirmAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingAck:
                return HandlePairingAckAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingRenotify:
                return HandlePairingRenotifyAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingCancel:
                return HandlePairingCancelAsync(clientId, sessionId, connection, envelope);
            default:
                // Every client-originated message type this dispatcher owns is mapped above. A
                // server-originated type can never actually reach here: the connection handler's
                // per-tier allowlist never authorizes one from a client.
                return Task.FromResult(new ClientDispatchResult());
        }
    }

    /// <summary>Answers a <c>ping</c> with <c>pong</c>.</summary>
    private Task<ClientDispatchResult> HandlePingAsync(
        SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The ping message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        connection.TrySend(Encode(PublicMessageType.Pong, sessionId, envelope.MessageId, new EmptyPayload()));
        return Task.FromResult(new ClientDispatchResult());
    }

    /// <summary>
    /// Answers a <c>rename_request</c> with <c>rename_outcome</c>: maps a successful mutation to
    /// <c>renamed</c>, a shape/length/control-character rejection from <see cref="ITrustAdminService.RenameAsync"/>
    /// to <c>invalid_display_name</c>, an unrecognized-or-not-currently-trusted identity to
    /// <c>not_trusted</c>, and any other failure to a safe correlated <c>internal_error</c> -- never a
    /// raw trust-store exception.
    /// </summary>
    private async Task<ClientDispatchResult> HandleRenameRequestAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out RenameRequestPayload? payload))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The rename_request message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        try
        {
            await trustAdminService.RenameAsync(clientId, payload.DisplayName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            SendRenameOutcome(connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.InvalidDisplayName, null);
            return new ClientDispatchResult();
        }
        catch (KeyNotFoundException)
        {
            SendRenameOutcome(connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.NotTrusted, null);
            return new ClientDispatchResult();
        }
        catch (InvalidOperationException)
        {
            SendRenameOutcome(connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.NotTrusted, null);
            return new ClientDispatchResult();
        }
        catch
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to rename this device.", retryable: true);
            return new ClientDispatchResult();
        }

        SendRenameOutcome(connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.Renamed, payload.DisplayName);
        return new ClientDispatchResult();
    }

    /// <summary>
    /// Answers a <c>pairing_request</c> with <c>pairing_status</c>. Makes challenge creation and first
    /// display one coherent decision: a newly started challenge is displayed through the adapter
    /// before it is ever reported <c>available</c>, and a rejected or timed-out display rolls the
    /// challenge back via <see cref="IPairingCoordinator.Cancel"/> before reporting <c>unavailable</c>,
    /// so no client is ever told a code is available that was never actually shown.
    /// </summary>
    private async Task<ClientDispatchResult> HandlePairingRequestAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_request message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        PairingStartResult start = pairingCoordinator.BeginPairing(clientId);
        switch (start.Outcome)
        {
            case PairingStartOutcome.Started:
                PairingChallenge challenge = start.Challenge!;
                bool accepted = await adapterNotifier.TryNotifyCodeAvailableAsync(challenge.Code, cancellationToken);
                if (!accepted)
                {
                    // The coordinator serializes BeginPairing and Cancel independently of this await, so
                    // this call never holds a coordinator lock while waiting on the adapter, per the
                    // atomic-display contract. Cancel matches by owned-challenge identity, so it can only
                    // roll back exactly the challenge this call just started.
                    pairingCoordinator.Cancel(clientId);
                    SendPairingStatus(connection, sessionId, envelope.MessageId, PairingStatusWireState.Unavailable, null);
                    return new ClientDispatchResult();
                }

                SendPairingStatus(
                    connection, sessionId, envelope.MessageId, PairingStatusWireState.Available, RemainingSeconds(challenge));
                return new ClientDispatchResult();

            case PairingStartOutcome.Resumed:
                PairingChallenge? owned = pairingCoordinator.TryGetOwnedChallenge(clientId);
                SendPairingStatus(
                    connection, sessionId, envelope.MessageId, PairingStatusWireState.InProgress,
                    owned is null ? null : RemainingSeconds(owned));
                return new ClientDispatchResult();

            case PairingStartOutcome.OtherDeviceActive:
                SendPairingStatusOtherDevice(connection, sessionId, envelope.MessageId);
                return new ClientDispatchResult();

            default:
                // GeneratorFailed (secure code generation failed) and Blocked (unreachable in practice:
                // an administratively blocked clientId can never hold the session this dispatch runs on,
                // since Block immediately invalidates it) both fail safely without disclosing which case
                // occurred.
                SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to begin pairing.", retryable: true);
                return new ClientDispatchResult();
        }
    }

    /// <summary>The requesting client's own active challenge's remaining code validity, in whole seconds, rounded up.</summary>
    private int RemainingSeconds(PairingChallenge challenge) => RoundUpSeconds(challenge.ExpiresAtUtc - clock.UtcNow);

    /// <summary>Sends a <c>pairing_status</c> reply for every state except <c>other_device_pairing</c>, correlated to the request it answers.</summary>
    private void SendPairingStatus(
        IPublicConnectionContext connection, SessionId sessionId, string correlationId, PairingStatusWireState state, int? expiresInSeconds)
    {
        var payload = new PairingStatusPayload { State = state, ExpiresInSeconds = expiresInSeconds };
        connection.TrySend(Encode(PublicMessageType.PairingStatus, sessionId, correlationId, payload));
    }

    /// <summary>Sends an <c>other_device_pairing</c> <c>pairing_status</c> reply, omitting <c>expiresInSeconds</c> entirely.</summary>
    private void SendPairingStatusOtherDevice(IPublicConnectionContext connection, SessionId sessionId, string correlationId)
    {
        var payload = new PairingStatusOtherDevicePayload { State = PairingStatusWireState.OtherDevicePairing };
        connection.TrySend(Encode(PublicMessageType.PairingStatus, sessionId, correlationId, payload));
    }

    /// <summary>Sends a <c>rename_outcome</c> reply, correlated to the request it answers.</summary>
    private void SendRenameOutcome(
        IPublicConnectionContext connection, SessionId sessionId, string correlationId, RenameOutcomeWireValue outcome, string? displayName)
    {
        var payload = new RenameOutcomePayload { Outcome = outcome, DisplayName = displayName };
        connection.TrySend(Encode(PublicMessageType.RenameOutcome, sessionId, correlationId, payload));
    }

    /// <summary>Sends a canonical <c>error</c> reply, correlated to the request that triggered it.</summary>
    private void SendError(
        IPublicConnectionContext connection, SessionId sessionId, string correlationId, PublicProtocolErrorCode code, string message, bool retryable = false)
    {
        var payload = new ErrorPayload { Code = code, Message = message, Retryable = retryable };
        connection.TrySend(Encode(PublicMessageType.Error, sessionId, correlationId, payload));
    }

    /// <summary>Encodes a host-originated message stamped with a fresh message id and the current play-context snapshot.</summary>
    private byte[] Encode<TPayload>(PublicMessageType messageType, SessionId sessionId, string? correlationId, TPayload payload)
    {
        PlayContextSnapshot snapshot = playContextTracker.GetSnapshot();
        return codec.Encode(messageType, NewMessageId(), sessionId.ToString(), correlationId, snapshot.Current?.ToString(), null, payload);
    }

    /// <summary>Generates a fresh, cryptographically random host-originated message identifier.</summary>
    private static string NewMessageId() => Guid.NewGuid().ToString();

    /// <summary>
    /// Answers a <c>pairing_confirm</c> with <c>pairing_outcome</c>: maps
    /// <see cref="IPairingCoordinator.ConfirmCode"/>'s outcome directly, best-effort requesting a
    /// wrong-code redisplay when the coordinator's own auto-renotify cooldown permits it and a
    /// no-code attempts-exhausted notification once the hard wrong-attempt limit cancels the
    /// challenge -- neither notification blocks or changes the outcome already decided, and neither
    /// discloses the code through the public response.
    /// </summary>
    private Task<ClientDispatchResult> HandlePairingConfirmAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out PairingConfirmPayload? payload))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_confirm message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        PairingConfirmationResult confirm;
        try
        {
            confirm = pairingCoordinator.ConfirmCode(clientId, payload.Code, payload.DisplayName);
        }
        catch (ArgumentException)
        {
            // The wire schema places no length/control-character constraint on pairing_confirm's
            // displayName, so this coordinator-level rejection is a client input validation failure
            // discovered one layer past envelope decoding, not a distinct pairing_outcome the schema
            // defines a value for.
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The display name is not valid.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }


        switch (confirm.Outcome)
        {
            case PairingConfirmOutcome.CredentialIssued:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload
                {
                    Outcome = PairingOutcomeWireValue.CredentialIssued,
                    Credential = confirm.Credential,
                    DisplayName = confirm.DisplayName,
                });
                return Task.FromResult(new ClientDispatchResult());

            case PairingConfirmOutcome.Invalid:
                if (confirm.ShouldAutoRenotify)
                {
                    string? ownedCode = pairingCoordinator.TryGetOwnedChallenge(clientId)?.Code;
                    if (ownedCode is not null)
                    {
                        FireAndForget(adapterNotifier.NotifyCodeIncorrectAsync(ownedCode, cancellationToken));
                    }
                }

                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.Invalid });
                return Task.FromResult(new ClientDispatchResult());

            case PairingConfirmOutcome.Expired:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.Expired });
                return Task.FromResult(new ClientDispatchResult());

            case PairingConfirmOutcome.PacingLimited:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload
                {
                    Outcome = PairingOutcomeWireValue.PacingLimited,
                    RetryAfterSeconds = RoundUpSeconds(confirm.RetryAfter!.Value),
                });
                return Task.FromResult(new ClientDispatchResult());

            case PairingConfirmOutcome.HardLimitReached:
                FireAndForget(adapterNotifier.NotifyAttemptsExhaustedAsync(cancellationToken));
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.HardLimitReached });
                return Task.FromResult(new ClientDispatchResult());

            default:
                // GeneratorFailed: secure credential generation failed.
                SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to confirm this code.", retryable: true);
                return Task.FromResult(new ClientDispatchResult());
        }
    }

    /// <summary>Sends a <c>pairing_outcome</c> reply, correlated to the request it answers.</summary>
    private void SendPairingOutcome(IPublicConnectionContext connection, SessionId sessionId, string correlationId, PairingOutcomePayload payload) =>
        connection.TrySend(Encode(PublicMessageType.PairingOutcome, sessionId, correlationId, payload));

    /// <summary>Rounds a duration up to the nearest whole second, never negative.</summary>
    private static int RoundUpSeconds(TimeSpan duration) => (int)Math.Ceiling(Math.Max(duration.TotalSeconds, 0));

    /// <summary>
    /// Starts a best-effort adapter notification without awaiting it, observing (and discarding) any
    /// fault so it can never surface as an unobserved task exception. A best-effort notification never
    /// blocks or changes an already-decided client outcome.
    /// </summary>
    private static void FireAndForget(Task task) =>
        task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

    /// <summary>
    /// Answers a <c>pairing_ack</c> with <c>pairing_outcome</c>: maps
    /// <see cref="IPairingCoordinator.CommitPendingAsync"/>'s outcome directly. A <c>trusted</c> or
    /// <c>already_trusted</c> outcome both signal the caller to upgrade this connection's session to
    /// full trust -- per <c>ai/context/protocol/security.md</c>'s trust-tier upgrade point, "the moment
    /// its pairing_confirm resolves to a trusted or already_trusted outcome" -- since both prove the
    /// presented credential is genuinely, currently trusted; only the idempotent-retry framing differs.
    /// </summary>
    private async Task<ClientDispatchResult> HandlePairingAckAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out PairingAckPayload? payload))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_ack message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        PairingCommitResult commit = await pairingCoordinator.CommitPendingAsync(clientId, payload.Credential, cancellationToken);
        switch (commit.Outcome)
        {
            case PairingCommitOutcome.Trusted:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload
                {
                    Outcome = PairingOutcomeWireValue.Trusted,
                    Credential = commit.Credential,
                    ShortId = commit.ShortId,
                    DisplayName = commit.DisplayName,
                });
                return new ClientDispatchResult(UpgradeToFullTrust: true);

            case PairingCommitOutcome.AlreadyTrusted:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload
                {
                    Outcome = PairingOutcomeWireValue.AlreadyTrusted,
                    Credential = commit.Credential,
                    ShortId = commit.ShortId,
                    DisplayName = commit.DisplayName,
                });
                return new ClientDispatchResult(UpgradeToFullTrust: true);

            case PairingCommitOutcome.PendingNotFound:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.PendingNotFound });
                return new ClientDispatchResult();

            case PairingCommitOutcome.PairingInvalidated:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.PairingInvalidated });
                return new ClientDispatchResult();

            default:
                // PersistenceFailed (retryable; the coordinator preserves the pending credential) and
                // GeneratorFailed (short-id generation exhausted) both fail safely without disclosing
                // which occurred.
                SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to commit trust.", retryable: true);
                return new ClientDispatchResult();
        }
    }

    /// <summary>
    /// Answers a <c>pairing_renotify</c> with <c>pairing_outcome</c>. Applies the same atomic-display
    /// rule as <c>pairing_request</c>'s initial display: <see cref="IPairingCoordinator.TryRenotify"/>
    /// only peeks eligibility, the adapter is awaited outside any coordinator lock, and
    /// <see cref="IPairingCoordinator.CommitRenotify"/> -- which alone applies the manual-redisplay
    /// cooldown -- is called only once the adapter accepts. A rejected redisplay leaves the challenge
    /// and cooldown state exactly as they were and reports a retryable error rather than a fabricated
    /// <c>renotified</c> or a silently discarded challenge.
    /// </summary>
    private async Task<ClientDispatchResult> HandlePairingRenotifyAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_renotify message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        PairingRenotifyResult peek = pairingCoordinator.TryRenotify(clientId);
        if (peek.Outcome != PairingRenotifyOutcome.Renotified)
        {
            SendPairingOutcome(connection, sessionId, envelope.MessageId, MapRenotifyOutcome(peek));
            return new ClientDispatchResult();
        }

        string? code = pairingCoordinator.TryGetOwnedChallenge(clientId)?.Code;
        if (code is null)
        {
            // The owned challenge vanished between the peek above and here (for example an
            // administrative cancellation) -- report that fresh reality rather than a stale success.
            SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.AlreadyIdle });
            return new ClientDispatchResult();
        }

        bool accepted = await adapterNotifier.TryNotifyRedisplayAsync(code, cancellationToken);
        if (!accepted)
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to redisplay the pairing code.", retryable: true);
            return new ClientDispatchResult();
        }

        PairingRenotifyResult commit = pairingCoordinator.CommitRenotify(clientId);
        SendPairingOutcome(connection, sessionId, envelope.MessageId, MapRenotifyOutcome(commit));
        return new ClientDispatchResult();
    }

    /// <summary>Maps a <see cref="PairingRenotifyResult"/> -- from either a peek or a commit -- to its wire outcome.</summary>
    private PairingOutcomePayload MapRenotifyOutcome(PairingRenotifyResult result) => result.Outcome switch
    {
        PairingRenotifyOutcome.Renotified => new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.Renotified },
        PairingRenotifyOutcome.Cooldown => new PairingOutcomePayload
        {
            Outcome = PairingOutcomeWireValue.RenotifyCooldown,
            RetryAfterSeconds = RoundUpSeconds(result.RetryAfter!.Value),
        },
        _ => new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.AlreadyIdle },
    };

    /// <summary>Answers a <c>pairing_cancel</c> with <c>pairing_outcome</c>. Never touches persisted trust or the adapter.</summary>
    private Task<ClientDispatchResult> HandlePairingCancelAsync(
        ClientId clientId, SessionId sessionId, IPublicConnectionContext connection, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_cancel message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        PairingCancelOutcome cancelOutcome = pairingCoordinator.Cancel(clientId);
        PairingOutcomeWireValue wireOutcome = cancelOutcome == PairingCancelOutcome.Cancelled
            ? PairingOutcomeWireValue.Cancelled
            : PairingOutcomeWireValue.AlreadyIdle;
        SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = wireOutcome });
        return Task.FromResult(new ClientDispatchResult());
    }
}
