using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
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

    /// <summary>Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Creates a client message dispatcher.</summary>
    /// <param name="codec">Decodes and encodes every message this dispatcher sends or receives.</param>
    /// <param name="trustAdminService">Applies rename mutations to the durable trust store.</param>
    /// <param name="playContextTracker">Supplies the <c>playContextId</c> stamped onto every host-originated envelope.</param>
    public ClientMessageDispatcher(
        IPublicEnvelopeCodec codec,
        ITrustAdminService trustAdminService,
        IPlayContextTracker playContextTracker)
    {
        this.codec = codec;
        this.trustAdminService = trustAdminService;
        this.playContextTracker = playContextTracker;
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
            default:
                // Every pairing_* message type is authorized by the connection handler's per-tier
                // allowlist but mapped to its owning service by a later step of this same concept.
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
}
