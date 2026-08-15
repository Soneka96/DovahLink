namespace DovahLinkValidationClient;

/// <summary>
/// This process's logical client identity, established once at process start and reused for its
/// entire lifetime. Not derived from any socket or session, so it survives a reconnect within the
/// same run; a fresh run gets a fresh identity.
/// </summary>
public static class ClientIdentity
{
    /// <summary>The stable client identifier for this process's lifetime.</summary>
    public static readonly Guid Current = Guid.NewGuid();
}
