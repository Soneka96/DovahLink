using System.Text;

namespace DovahLink.Host.Identity;

/// <summary>A Host installation ID paired with the computer name currently reported by its operating system.</summary>
public sealed record HostIdentity
{
    /// <summary>The stable identity persisted for this DovahLink Host installation.</summary>
    public HostId HostId { get; }

    /// <summary>The current operating-system computer name, or the safe fallback when unavailable.</summary>
    public string HostName { get; }

    /// <summary>Creates a validated snapshot of the Host installation ID and current computer name.</summary>
    /// <param name="hostId">The non-empty persistent Host installation ID.</param>
    /// <param name="hostName">The non-empty, bounded current operating-system computer name.</param>
    /// <exception cref="ArgumentException">The ID is empty or the name is empty, contains controls, or exceeds its byte limit.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="hostName"/> is <see langword="null"/>.</exception>
    public HostIdentity(HostId hostId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(hostName);
        if (hostId.Value == Guid.Empty || string.IsNullOrWhiteSpace(hostName) ||
            hostName.Any(char.IsControl) || Encoding.UTF8.GetByteCount(hostName) > Constants.MaxDisplayNameLengthBytes)
        {
            throw new ArgumentException("The Host identity values are invalid.");
        }

        HostId = hostId;
        HostName = hostName;
    }
}
