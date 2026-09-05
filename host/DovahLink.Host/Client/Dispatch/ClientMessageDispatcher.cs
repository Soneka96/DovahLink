using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Sessions;
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
    /// <param name="connectionId">
    /// The connection's own identity, used to re-verify this exact session incarnation is still active
    /// immediately before a narrow, synchronous, client-bound state read or mutation
    /// (<c>pairing_request</c>'s <see cref="IPairingCoordinator.BeginPairing"/>, <c>pairing_cancel</c>'s
    /// <see cref="IPairingCoordinator.Cancel"/>, <c>pairing_renotify</c>'s
    /// <see cref="IPairingCoordinator.TryRenotify"/> peek, <c>pairing_confirm</c>'s
    /// <see cref="IPairingCoordinator.ConfirmCode"/>, and <c>rename_request</c>'s
    /// <see cref="ITrustAdminService.TryCaptureTrustedIncarnation"/>) -- closing the check-then-act gap a
    /// session check performed only once, upstream of this call, would leave open against a concurrent
    /// administrative invalidation.
    /// </param>
    /// <param name="connection">The connection to send the response or error on.</param>
    /// <param name="envelope">The already-decoded, already-authorized client envelope to dispatch.</param>
    /// <param name="cancellationToken">The token used to cancel any awaited service call.</param>
    /// <returns>The dispatch's side effects for the caller to apply.</returns>
    Task<ClientDispatchResult> DispatchAsync(
        ClientId clientId,
        SessionId sessionId,
        ConnectionId connectionId,
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

    /// <summary>
    /// Establishes the authorization linearization point for a narrow, synchronous, client-bound
    /// pairing-state mutation: verifies this exact session incarnation is still active immediately
    /// before the mutation runs, in the same critical section every session invalidation also
    /// serializes on.
    /// </summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>Creates a client message dispatcher.</summary>
    /// <param name="codec">Decodes and encodes every message this dispatcher sends or receives.</param>
    /// <param name="trustAdminService">Applies rename mutations to the durable trust store.</param>
    /// <param name="pairingCoordinator">Owns the host's client-bound pairing challenge and pending-credential lifecycle.</param>
    /// <param name="adapterNotifier">The injected, narrow host-owned seam used to request a Skyrim-facing pairing notification.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</param>
    /// <param name="clock">The time source used to compute a challenge's remaining code validity.</param>
    /// <param name="sessionRegistry">Establishes the authorization linearization point for a client-bound pairing-state mutation.</param>
    public ClientMessageDispatcher(
        IPublicEnvelopeCodec codec,
        ITrustAdminService trustAdminService,
        IPairingCoordinator pairingCoordinator,
        IPairingAdapterNotifier adapterNotifier,
        IPlayContextTracker playContextTracker,
        IClock clock,
        ISessionRegistry sessionRegistry)
    {
        this.codec = codec;
        this.trustAdminService = trustAdminService;
        this.pairingCoordinator = pairingCoordinator;
        this.adapterNotifier = adapterNotifier;
        this.playContextTracker = playContextTracker;
        this.clock = clock;
        this.sessionRegistry = sessionRegistry;
    }

    /// <inheritdoc/>
    public Task<ClientDispatchResult> DispatchAsync(
        ClientId clientId,
        SessionId sessionId,
        ConnectionId connectionId,
        IPublicConnectionContext connection,
        PublicEnvelope envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.MessageType)
        {
            case PublicMessageType.Ping:
                return HandlePingAsync(sessionId, connection, envelope);
            case PublicMessageType.RenameRequest:
                return HandleRenameRequestAsync(clientId, sessionId, connectionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingRequest:
                return HandlePairingRequestAsync(clientId, sessionId, connectionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingConfirm:
                return HandlePairingConfirmAsync(clientId, sessionId, connectionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingAck:
                return HandlePairingAckAsync(clientId, sessionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingRenotify:
                return HandlePairingRenotifyAsync(clientId, sessionId, connectionId, connection, envelope, cancellationToken);
            case PublicMessageType.PairingCancel:
                return HandlePairingCancelAsync(clientId, sessionId, connectionId, connection, envelope);
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
    /// raw trust-store exception. Re-verifies this exact session incarnation is still active through
    /// <see cref="ISessionRegistry.TryExecuteIfActive{T}"/>, capturing
    /// <see cref="ITrustAdminService.TryCaptureTrustedIncarnation"/>'s snapshot inside that same guarded
    /// critical section, before ever calling <see cref="ITrustAdminService.RenameAsync"/>: an
    /// already-invalidated session must never capture a target incarnation at all, let alone one a
    /// concurrent administrative mutation subsequently replaced. A no-record-or-not-currently-Trusted
    /// capture reports <c>not_trusted</c> immediately, without ever calling
    /// <see cref="ITrustAdminService.RenameAsync"/> at all.
    /// </summary>
    private async Task<ClientDispatchResult> HandleRenameRequestAsync(
        ClientId clientId, SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out RenameRequestPayload? payload))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The rename_request message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        if (!sessionRegistry.TryExecuteIfActive(
            sessionId, connectionId, () => trustAdminService.TryCaptureTrustedIncarnation(clientId), out KnownDeviceIncarnationId? expectedIncarnation))
        {
            SendStaleSessionError(connection, sessionId, envelope.MessageId);
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        if (expectedIncarnation is null)
        {
            SendRenameOutcome(connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.NotTrusted, null);
            return new ClientDispatchResult();
        }

        try
        {
            await trustAdminService.RenameAsync(clientId, payload.DisplayName, expectedIncarnation.Value, cancellationToken);
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

        // An empty displayName clears the device's name; the wire contract represents "no name" as
        // null, never an empty string, so a cleared name is normalized here before it is echoed back.
        SendRenameOutcome(
            connection, sessionId, envelope.MessageId, RenameOutcomeWireValue.Renamed,
            string.IsNullOrEmpty(payload.DisplayName) ? null : payload.DisplayName);
        return new ClientDispatchResult();
    }

    /// <summary>
    /// Answers a <c>pairing_request</c> with <c>pairing_status</c>. Makes challenge creation and first
    /// display one coherent decision: a newly started challenge is a reservation only, not yet
    /// publicly resumable or displayed, until <see cref="IPairingCoordinator.CommitInitialDisplay"/>
    /// accepts it by its exact <see cref="PairingChallenge.Id"/>. A rejected display, a thrown
    /// exception, or a cancelled adapter await all roll the reservation back via
    /// <see cref="IPairingCoordinator.RollbackInitialDisplay"/> through one exception-safe
    /// <c>finally</c> guarded by whether the commit actually succeeded, identity-checked the same way,
    /// so a stale acceptance or rejection that arrives after the reservation was already replaced,
    /// cancelled, or expired can never advertise or cancel a later, unrelated challenge -- and a
    /// reservation that did commit is never rolled back afterward by that same <c>finally</c>. Only a
    /// genuine caller/request cancellation -- <paramref name="cancellationToken"/> itself requesting
    /// it -- propagates as <see cref="OperationCanceledException"/>; an adapter fault, including the
    /// adapter throwing its own independent <see cref="OperationCanceledException"/>, maps to a
    /// redacted retryable <c>internal_error</c> instead of leaking the raw exception. Before either
    /// call, re-verifies this exact session incarnation is still active through
    /// <see cref="ISessionRegistry.TryExecuteIfActive{T}"/>, folding <see cref="IPairingCoordinator.BeginPairing"/>
    /// and, only for the <see cref="PairingStartOutcome.Resumed"/> outcome, the follow-up
    /// <see cref="IPairingCoordinator.GetStatusSnapshot"/> into that same guarded critical section: a
    /// session already invalidated by a concurrent administrative mutation can never start a fresh
    /// challenge under post-mutation state, nor resume and read status for a challenge a newer session
    /// incarnation for the same <paramref name="clientId"/> subsequently created.
    /// </summary>
    private async Task<ClientDispatchResult> HandlePairingRequestAsync(
        ClientId clientId, SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_request message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        if (!sessionRegistry.TryExecuteIfActive(
            sessionId,
            connectionId,
            () =>
            {
                PairingStartResult beginResult = pairingCoordinator.BeginPairing(clientId);
                PairingStatusSnapshot? resumedSnapshot = beginResult.Outcome == PairingStartOutcome.Resumed
                    ? pairingCoordinator.GetStatusSnapshot(clientId)
                    : null;
                return (Start: beginResult, ResumedSnapshot: resumedSnapshot);
            },
            out (PairingStartResult Start, PairingStatusSnapshot? ResumedSnapshot) begin))
        {
            SendStaleSessionError(connection, sessionId, envelope.MessageId);
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        PairingStartResult start = begin.Start;
        switch (start.Outcome)
        {
            case PairingStartOutcome.Started:
                PairingChallenge challenge = start.Challenge!;
                bool displayCommitted = false;
                try
                {
                    bool accepted = await adapterNotifier.TryNotifyCodeAvailableAsync(challenge.Code, cancellationToken);
                    if (!accepted)
                    {
                        SendPairingStatus(connection, sessionId, envelope.MessageId, PairingStatusWireState.Unavailable, null);
                        return new ClientDispatchResult();
                    }

                    displayCommitted = pairingCoordinator.CommitInitialDisplay(clientId, challenge.Id);
                    if (!displayCommitted)
                    {
                        // The reservation was replaced, cancelled, or expired while the adapter
                        // acknowledgement was in flight. The code the adapter just accepted no longer
                        // belongs to any current challenge, so nothing is left to report available.
                        SendPairingStatus(connection, sessionId, envelope.MessageId, PairingStatusWireState.Unavailable, null);
                        return new ClientDispatchResult();
                    }

                    SendPairingStatus(
                        connection, sessionId, envelope.MessageId, PairingStatusWireState.Available, RemainingSeconds(challenge));
                    return new ClientDispatchResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // The adapter faulted unexpectedly, or threw its own OperationCanceledException
                    // independent of the caller's own token -- either way this is not a genuine
                    // request cancellation, so it must not propagate a raw exception to the caller.
                    SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to display the pairing code.", retryable: true);
                    return new ClientDispatchResult();
                }
                finally
                {
                    // Every path above except a successful CommitInitialDisplay leaves the reservation
                    // uncommitted -- a rejected display, an exception from the adapter call, or this
                    // await being cancelled (socket death, connection teardown) -- and must release it
                    // rather than let it occupy the global pairing slot until disconnect grace/expiry.
                    if (!displayCommitted)
                    {
                        pairingCoordinator.RollbackInitialDisplay(clientId, challenge.Id);
                    }
                }

            case PairingStartOutcome.Resumed:
                PairingStatusSnapshot snapshot = begin.ResumedSnapshot!;
                switch (snapshot.Kind)
                {
                    case PairingStatusKind.DisplayedChallenge:
                        SendPairingStatus(
                            connection, sessionId, envelope.MessageId, PairingStatusWireState.InProgress,
                            RemainingSeconds(snapshot.Challenge!));
                        break;

                    case PairingStatusKind.PendingCredential:
                        SendPairingStatus(connection, sessionId, envelope.MessageId, PairingStatusWireState.InProgress, null);
                        break;

                    case PairingStatusKind.OtherDeviceActive:
                        // A concurrent operation raced BeginPairing's own ownership check between it
                        // and this status read: a different client won ownership before this snapshot
                        // was taken. Report the same other_device_pairing status the initial
                        // BeginPairing outcome reports for it, never folded into unavailable.
                        SendPairingStatusOtherDevice(connection, sessionId, envelope.MessageId);
                        break;

                    case PairingStatusKind.Idle:
                    case PairingStatusKind.UncommittedDisplayReservation:
                        // Idle: the same ownership race cleared this client's operation entirely
                        // before the snapshot was taken. UncommittedDisplayReservation: never actually
                        // shown to this client yet, so it is not yet a displayable challenge. Neither
                        // is publicly resumable/displayed, so both report the same "nothing to show
                        // yet" status Concept 03 defines for that.
                        SendPairingStatus(connection, sessionId, envelope.MessageId, PairingStatusWireState.Unavailable, null);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.Kind, "Unrecognized pairing status kind.");
                }
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

    /// <summary>
    /// Sends the canonical <c>stale_session</c> error for a client-bound pairing-state mutation
    /// <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> refused to run because this exact session
    /// incarnation was already invalidated -- by a concurrent administrative mutation -- between the
    /// caller's earlier admission check and this dispatch reaching its own authorization linearization
    /// point.
    /// </summary>
    private void SendStaleSessionError(IPublicConnectionContext connection, SessionId sessionId, string correlationId) =>
        SendError(connection, sessionId, correlationId, PublicProtocolErrorCode.StaleSession, "This session is no longer active.");

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
    /// discloses the code through the public response. Re-verifies this exact session incarnation is
    /// still active through <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> immediately before
    /// <see cref="IPairingCoordinator.ConfirmCode"/>: an already-invalidated session must never evaluate
    /// or mutate challenge/pacing/pending state a newer session incarnation for the same
    /// <paramref name="clientId"/> subsequently created. The lock <see cref="ISessionRegistry.TryExecuteIfActive{T}"/>
    /// holds is released through its own exception-safe critical section even when the guarded call
    /// throws, so <see cref="ArgumentException"/> from an invalid <c>displayName</c> still propagates
    /// out to this method's own catch below exactly as before.
    /// </summary>
    private Task<ClientDispatchResult> HandlePairingConfirmAsync(
        ClientId clientId, SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out PairingConfirmPayload? payload))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_confirm message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        if (!IsValidPairingCode(payload.Code))
        {
            // A code that is not exactly six ASCII decimal digits can never match a real challenge; per
            // Concept 02's precedent for trusted_device_credential ("wrong shape is malformed protocol,
            // not failed authentication"), this is rejected before it ever reaches ConfirmCode and
            // consumes pacing/wrong-attempt state as an ordinary wrong code would.
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_confirm message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        PairingConfirmationResult confirm;
        try
        {
            if (!sessionRegistry.TryExecuteIfActive(
                sessionId, connectionId, () => pairingCoordinator.ConfirmCode(clientId, payload.Code, payload.DisplayName), out confirm))
            {
                SendStaleSessionError(connection, sessionId, envelope.MessageId);
                return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
            }
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
                if (confirm.AutoRenotifyCode is { } autoRenotifyCode)
                {
                    // Uses the exact code ConfirmCode evaluated this wrong attempt against, never a
                    // later, separate status read: the challenge could be replaced or cancelled between
                    // this call returning and any later read, which would redisplay a different
                    // challenge's code under this attempt's own "wrong code" presentation.
                    FireAndForget(() => adapterNotifier.NotifyCodeIncorrectAsync(autoRenotifyCode, cancellationToken));
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
                FireAndForget(() => adapterNotifier.NotifyAttemptsExhaustedAsync(cancellationToken));
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.HardLimitReached });
                return Task.FromResult(new ClientDispatchResult());

            case PairingConfirmOutcome.PairingInvalidated:
                SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = PairingOutcomeWireValue.PairingInvalidated });
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
    /// fault so it can never surface as an unobserved task exception. Invokes <paramref name="start"/>
    /// itself inside the same try/catch: an <see cref="IPairingAdapterNotifier"/> implementation that
    /// throws synchronously, before ever returning a <see cref="Task"/>, must be exactly as harmless as
    /// one that returns a faulted <see cref="Task"/> -- a best-effort notification never blocks or
    /// changes an already-decided client outcome, and a bug in the notifier must never prevent this
    /// method's own caller from sending that outcome.
    /// </summary>
    /// <param name="start">Invokes the best-effort notification and returns its <see cref="Task"/>.</param>
    private static void FireAndForget(Func<Task> start)
    {
        Task task;
        try
        {
            task = start();
        }
        catch
        {
            return;
        }

        task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Reports whether a presented <c>pairing_confirm.code</c> is exactly
    /// <see cref="Constants.PairingChallengeCodeDigits"/> ASCII decimal digits, per
    /// <c>protocol/schema/README.md</c>'s "<c>pairing_confirm</c>" section. A caller rejects a value
    /// that fails this check as malformed protocol input before it ever reaches
    /// <see cref="IPairingCoordinator.ConfirmCode"/>.
    /// </summary>
    /// <param name="code">The presented code to validate.</param>
    private static bool IsValidPairingCode(string code) =>
        code.Length == Constants.PairingChallengeCodeDigits && code.All(char.IsAsciiDigit);

    /// <summary>
    /// Answers a <c>pairing_ack</c> with <c>pairing_outcome</c>: maps
    /// <see cref="IPairingCoordinator.CommitPendingAsync"/>'s outcome directly. A <c>trusted</c> or
    /// <c>already_trusted</c> outcome both signal the caller to upgrade this connection's session to
    /// full trust -- per <c>ai/context/protocol/security.md</c>'s trust-tier upgrade point, "the moment
    /// its pairing_ack resolves to a trusted or already_trusted outcome" -- since both prove the
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

        if (!CredentialHasher.IsValidHexCredential(payload.Credential, Constants.PairingCredentialLength))
        {
            // A credential that is not exactly the approved hex length/shape can never match a real
            // pending credential; per Concept 02's precedent for trusted_device_credential, this is
            // rejected before it ever reaches CommitPendingAsync as malformed protocol input, not as an
            // ordinary pending_not_found secret mismatch.
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
    /// grants an exclusive redisplay reservation -- returning the exact challenge identity, reservation
    /// identity, and code together, atomically -- the adapter is awaited outside any coordinator lock,
    /// and <see cref="IPairingCoordinator.CommitRenotify"/> -- which alone applies the manual-redisplay
    /// cooldown, identity-checked against that same reservation -- is called only once the adapter
    /// accepts. Every exit from the adapter await that does not reach <c>CommitRenotify</c> releases the
    /// reservation through <see cref="IPairingCoordinator.RollbackRenotify"/> in a <c>finally</c> block,
    /// so a rejected, faulted, or cancelled redisplay always leaves the reservation retryable rather
    /// than stuck, and never consumes the cooldown for a redisplay that never actually happened. A
    /// rejected redisplay leaves the challenge and cooldown state exactly as they were and reports a
    /// retryable error rather than a fabricated <c>renotified</c> or a silently discarded challenge. A
    /// stale acceptance for a challenge already replaced, cancelled, or expired can never consume a
    /// later, unrelated challenge's cooldown. Only a genuine caller/request cancellation --
    /// <paramref name="cancellationToken"/> itself requesting it -- propagates as
    /// <see cref="OperationCanceledException"/>; an adapter fault, including the adapter throwing its
    /// own independent <see cref="OperationCanceledException"/>, maps to a redacted retryable
    /// <c>internal_error</c> instead of leaking the raw exception. Either way <see cref="IPairingCoordinator.CommitRenotify"/>
    /// is never reached, so the active challenge and its cooldown remain exactly as they were. The
    /// initial <see cref="IPairingCoordinator.TryRenotify"/> peek itself first re-verifies this exact
    /// session incarnation is still active through <see cref="ISessionRegistry.TryExecuteIfActive{T}"/>;
    /// only that narrow, synchronous peek runs inside the guarded critical section -- never the adapter
    /// await, which stays outside it exactly as the rest of this method already requires.
    /// </summary>
    private async Task<ClientDispatchResult> HandlePairingRenotifyAsync(
        ClientId clientId, SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection, PublicEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_renotify message is malformed.");
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        if (!sessionRegistry.TryExecuteIfActive(sessionId, connectionId, () => pairingCoordinator.TryRenotify(clientId), out PairingRenotifyResult peek))
        {
            SendStaleSessionError(connection, sessionId, envelope.MessageId);
            return new ClientDispatchResult(IsProtocolViolation: true);
        }

        if (peek.Outcome != PairingRenotifyOutcome.Renotified)
        {
            SendPairingOutcome(connection, sessionId, envelope.MessageId, MapRenotifyOutcome(peek));
            return new ClientDispatchResult();
        }

        ChallengeId challengeId = peek.ChallengeId!.Value;
        RenotifyClaimId claimId = peek.ClaimId!.Value;
        bool resolved = false;
        try
        {
            bool accepted;
            try
            {
                accepted = await adapterNotifier.TryNotifyRedisplayAsync(peek.Code!, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The adapter faulted unexpectedly, or threw its own OperationCanceledException
                // independent of the caller's own token -- either way this is not a genuine request
                // cancellation, so it must not propagate a raw exception to the caller.
                // CommitRenotify has not run, so the active challenge and its cooldown remain
                // untouched; the `finally` below releases the reservation for an immediate retry.
                SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to redisplay the pairing code.", retryable: true);
                return new ClientDispatchResult();
            }
            if (!accepted)
            {
                SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.InternalError, "Unable to redisplay the pairing code.", retryable: true);
                return new ClientDispatchResult();
            }

            PairingRenotifyResult commit = pairingCoordinator.CommitRenotify(clientId, challengeId, claimId);
            resolved = true;
            SendPairingOutcome(connection, sessionId, envelope.MessageId, MapRenotifyOutcome(commit));
            return new ClientDispatchResult();
        }
        finally
        {
            if (!resolved)
            {
                pairingCoordinator.RollbackRenotify(clientId, challengeId, claimId);
            }
        }
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

    /// <summary>
    /// Answers a <c>pairing_cancel</c> with <c>pairing_outcome</c>. Never touches persisted trust or the
    /// adapter. Re-verifies this exact session incarnation is still active through
    /// <see cref="ISessionRegistry.TryExecuteIfActive{T}"/> immediately before
    /// <see cref="IPairingCoordinator.Cancel"/>: because pairing state is bound to <paramref name="clientId"/>
    /// rather than to any one session, an already-invalidated session must never be able to cancel
    /// pairing state a newer session incarnation for the same client subsequently created.
    /// </summary>
    private Task<ClientDispatchResult> HandlePairingCancelAsync(
        ClientId clientId, SessionId sessionId, ConnectionId connectionId, IPublicConnectionContext connection, PublicEnvelope envelope)
    {
        if (!codec.TryDecodePayload(envelope, out EmptyPayload? _))
        {
            SendError(connection, sessionId, envelope.MessageId, PublicProtocolErrorCode.MalformedMessage, "The pairing_cancel message is malformed.");
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        if (!sessionRegistry.TryExecuteIfActive(sessionId, connectionId, () => pairingCoordinator.Cancel(clientId), out PairingCancelOutcome cancelOutcome))
        {
            SendStaleSessionError(connection, sessionId, envelope.MessageId);
            return Task.FromResult(new ClientDispatchResult(IsProtocolViolation: true));
        }

        PairingOutcomeWireValue wireOutcome = cancelOutcome == PairingCancelOutcome.Cancelled
            ? PairingOutcomeWireValue.Cancelled
            : PairingOutcomeWireValue.AlreadyIdle;
        SendPairingOutcome(connection, sessionId, envelope.MessageId, new PairingOutcomePayload { Outcome = wireOutcome });
        return Task.FromResult(new ClientDispatchResult());
    }
}
