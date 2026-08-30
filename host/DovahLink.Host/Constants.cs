namespace DovahLink.Host;

// ---- Trust ----

/// <summary>Small, cross-cutting constant values shared across the host, grouped by area.</summary>
public static class Constants
{
    /// <summary>The default per-Windows-user file the trust store is persisted to.</summary>
    public static string TrustStoreFilePath
    {
        get
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("The current Windows user has no local application-data directory.");
            }

            return Path.Combine(localApplicationData, "DovahLink", "host", "trust-store.dat");
        }
    }

    /// <summary>The number of decimal digits in a generated factory-reset confirmation code.</summary>
    public const int FactoryResetChallengeCodeDigits = 6;

    /// <summary>How long a factory-reset challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan FactoryResetChallengeLifetime = TimeSpan.FromSeconds(60);

    // ---- Pairing ----

    /// <summary>The number of digits in a generated pairing challenge code.</summary>
    public const int PairingChallengeCodeDigits = 6;

    /// <summary>How long a pairing challenge remains confirmable after it is issued.</summary>
    public static readonly TimeSpan PairingChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The number of hex characters in a newly issued pairing credential.</summary>
    public const int PairingCredentialLength = 32;

    /// <summary>The number of decimal digits in a newly paired device's short id.</summary>
    public const int PairingShortIdDigits = 5;

    /// <summary>The maximum number of short-id candidates tried before pairing fails closed.</summary>
    public const int PairingMaxShortIdGenerationAttempts = 20;

    /// <summary>How long an owner may be disconnected before its pairing challenge expires.</summary>
    public static readonly TimeSpan PairingReconnectGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>The minimum interval between manual or automatic pairing-code redisplays.</summary>
    public static readonly TimeSpan PairingRenotifyCooldown = TimeSpan.FromSeconds(5);

    /// <summary>The minimum interval between evaluated pairing-code attempts.</summary>
    public static readonly TimeSpan PairingConfirmPacingInterval = TimeSpan.FromSeconds(1);

    /// <summary>The number of evaluated wrong codes allowed before the challenge is cancelled.</summary>
    public const int PairingMaxWrongAttempts = 5;

    /// <summary>How long an issued credential may remain pending final confirmation.</summary>
    public static readonly TimeSpan PairingPendingCredentialLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The maximum UTF-8 byte length of a trusted device display name.</summary>
    public const int MaxDisplayNameLengthBytes = 64;

    // ---- Authentication ----

    /// <summary>The number of hex characters in a generated one-time local connection token.</summary>
    public const int LocalConnectionTokenLength = 32;

    /// <summary>
    /// How long a one-time local connection token remains consumable after it is issued, per
    /// <c>ai/context/protocol/security.md</c>'s "Phase 1 exposure".
    /// </summary>
    public static readonly TimeSpan LocalConnectionTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The maximum number of failed token attempts allowed within
    /// <see cref="LocalConnectionTokenFailureWindow"/>, per
    /// <c>ai/context/protocol/security.md</c>'s "Phase 1 exposure".
    /// </summary>
    public const int LocalConnectionTokenMaxFailuresPerWindow = 5;

    /// <summary>The rolling window over which failed token attempts are counted.</summary>
    public static readonly TimeSpan LocalConnectionTokenFailureWindow = TimeSpan.FromSeconds(60);

    // ---- Sessions ----

    /// <summary>The maximum number of active client sessions admitted by the first host proof.</summary>
    public const int MaxActiveSessions = 1;

    // ---- Adapter IPC ----

    /// <summary>The fixed byte length of an IPC frame header (kind and correlation id).</summary>
    public const int IpcFrameHeaderBytes = 9;

    /// <summary>
    /// The maximum total byte length (header plus payload) of one private IPC frame. Approved as a
    /// provisional value for this concept's small control-only messages; a later concept that needs
    /// to carry a larger payload over this channel may revise it with the same documented approval
    /// <c>ai/context/protocol/security.md</c>'s own limits require.
    /// </summary>
    public const int MaxIpcFrameBytes = 65536;

    /// <summary>The maximum byte length of an <see cref="Adapter.Ipc.IpcHelloMessage"/> peer-ownership proof token.</summary>
    public const int MaxIpcPeerProofTokenBytes = 64;

    /// <summary>
    /// The bounded capacity later concepts must enforce for a private IPC send/receive queue. Not
    /// itself enforced by this contract's codec.
    /// </summary>
    public const int MaxIpcQueuedMessages = 256;

    /// <summary>
    /// The maximum inbound private IPC message rate later concepts must enforce, per connected peer.
    /// Not itself enforced by this contract's codec.
    /// </summary>
    public const int MaxIpcMessagesPerSecond = 200;

    /// <summary>The rolling window used for the private IPC inbound message-rate limit.</summary>
    public static readonly TimeSpan IpcMessageRateWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The configured loopback TCP port for the host's private adapter listener. Zero asks the
    /// operating system to assign an available port. This is distinct from the public client
    /// transport's own endpoint.
    /// </summary>
    public const int AdapterIpcLoopbackPort = 0;

    /// <summary>
    /// How long the private IPC listener's accept loop waits before retrying after a failed accept
    /// caused by something other than the listening socket being disposed, so a persistent failure
    /// cannot spin the loop without bound.
    /// </summary>
    public static readonly TimeSpan AdapterIpcAcceptRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long a newly accepted private IPC connection may take to send its Hello before the
    /// connection is closed, so a peer that withholds its first frame cannot hold the listener's one
    /// served-connection slot indefinitely.
    /// </summary>
    public static readonly TimeSpan AdapterIpcHandshakeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The byte length of the adapter-generated random challenge carried in
    /// <see cref="Adapter.Ipc.IpcHelloMessage"/>, and of the host's resulting HMAC-SHA256
    /// <c>HostProof</c> in <see cref="Adapter.Ipc.IpcHelloAckMessage"/>.
    /// </summary>
    public const int IpcChallengeBytes = 32;

    /// <summary>
    /// The byte length of the owning Skyrim process's lifetime identity (<c>OwnerLifetimeId</c>)
    /// carried in <see cref="Adapter.Ipc.IpcHelloMessage"/>: a 4-byte process id and an 8-byte
    /// process creation timestamp.
    /// </summary>
    public const int IpcOwnerLifetimeIdBytes = 12;

    /// <summary>The byte length of <see cref="Adapter.Ipc.IpcHelloAckMessage"/>'s HMAC-SHA256 <c>HostProof</c>.</summary>
    public const int IpcHostProofBytes = 32;

    /// <summary>
    /// The fixed byte length of the message <c>HostProof</c> is computed over: <c>Challenge
    /// (IpcChallengeBytes) || CorrelationId (8) || AdapterInstanceId (16) || OwnerLifetimeId
    /// (IpcOwnerLifetimeIdBytes)</c>.
    /// </summary>
    public const int IpcHostProofMessageBytes = IpcChallengeBytes + 8 + 16 + IpcOwnerLifetimeIdBytes;
}
