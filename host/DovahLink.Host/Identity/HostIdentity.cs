namespace DovahLink.Host.Identity;

/// <summary>A Host installation ID paired with the computer name currently reported by its operating system.</summary>
/// <param name="HostId">The stable identity persisted for this DovahLink Host installation.</param>
/// <param name="HostName">The current operating-system computer name, or the safe fallback when unavailable.</param>
public sealed record HostIdentity(HostId HostId, string HostName);
