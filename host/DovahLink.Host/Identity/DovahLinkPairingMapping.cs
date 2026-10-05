using System.Text;

namespace DovahLink.Host.Identity;

/// <summary>
/// Pure encoders for the byte values that identify a DovahLink installation to the pairing ceremony.
/// Every value is built only from local installation state: a role and an installation UUID. No
/// address, port, process, session, display name, or peer-supplied value ever enters one.
/// </summary>
public static class DovahLinkPairingMapping
{
    /// <summary>
    /// Writes an installation UUID as its 16 RFC 9562 network-order bytes: the order of the hex digits
    /// of its canonical text form. This is never the mixed-endian layout of <see cref="Guid.ToByteArray()"/>.
    /// </summary>
    /// <param name="installationId">The non-empty installation UUID.</param>
    /// <returns>The 16 network-order bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="installationId"/> is the nil UUID.</exception>
    public static byte[] EncodeUuid(Guid installationId)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("The nil UUID is not a DovahLink installation identity.", nameof(installationId));
        }

        byte[] bytes = new byte[16];
        if (!installationId.TryWriteBytes(bytes, bigEndian: true, out int written) || written != bytes.Length)
        {
            throw new InvalidOperationException("The installation UUID could not be written in network order.");
        }

        return bytes;
    }

    /// <summary>
    /// Builds a pairing application identity: the versioned ASCII domain, the role byte, and the
    /// installation UUID in RFC 9562 order, exactly 50 bytes.
    /// </summary>
    /// <param name="role">The installation's role.</param>
    /// <param name="installationId">The non-empty installation UUID: the Host ID or the client ID.</param>
    /// <returns>The 50-byte application identity.</returns>
    /// <exception cref="ArgumentException">The UUID is nil.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="role"/> is not a defined role.</exception>
    public static byte[] EncodeApplicationIdentity(DovahLinkPairingRole role, Guid installationId) =>
        EncodeRoleScoped(Constants.PairingApplicationIdentityDomain, role, installationId);

    /// <summary>
    /// Builds a pairing authority scope: its own versioned ASCII domain, the role byte, and the
    /// installation UUID in RFC 9562 order, exactly 47 bytes.
    /// </summary>
    /// <param name="role">The installation's role.</param>
    /// <param name="installationId">The non-empty installation UUID from stable local state.</param>
    /// <returns>The 47-byte authority scope.</returns>
    /// <exception cref="ArgumentException">The UUID is nil.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="role"/> is not a defined role.</exception>
    public static byte[] EncodeAuthorityScope(DovahLinkPairingRole role, Guid installationId) =>
        EncodeRoleScoped(Constants.PairingAuthorityScopeDomain, role, installationId);

    /// <summary>Builds the pairing authority scope of this Host installation.</summary>
    /// <param name="hostId">The persisted Host installation ID.</param>
    /// <returns>The 47-byte Host authority scope.</returns>
    /// <exception cref="ArgumentException"><paramref name="hostId"/> is the default, empty ID.</exception>
    public static byte[] EncodeHostAuthorityScope(HostId hostId) =>
        EncodeAuthorityScope(DovahLinkPairingRole.Host, hostId.Value);

    /// <summary>Concatenates an ASCII domain, a role byte, and network-order UUID bytes.</summary>
    /// <param name="domain">The ASCII domain.</param>
    /// <param name="role">The installation's role.</param>
    /// <param name="installationId">The non-empty installation UUID.</param>
    /// <returns>The encoded value.</returns>
    private static byte[] EncodeRoleScoped(string domain, DovahLinkPairingRole role, Guid installationId)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "The pairing role is not defined.");
        }

        byte[] uuid = EncodeUuid(installationId);
        byte[] encoded = new byte[domain.Length + 1 + uuid.Length];
        int written = Encoding.ASCII.GetBytes(domain, encoded);
        encoded[written] = (byte)role;
        uuid.CopyTo(encoded, written + 1);
        return encoded;
    }
}
