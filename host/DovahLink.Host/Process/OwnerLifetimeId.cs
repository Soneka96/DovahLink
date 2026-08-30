using System.Buffers.Binary;

namespace DovahLink.Host.Process;

/// <summary>
/// The owning Skyrim process's lifetime identity (process id and creation timestamp), received
/// from the adapter at launch as a structured process argument. Scopes the rendezvous file, the
/// shutdown-request named event, and the Hello handshake's lifetime check to the intended Skyrim
/// lifetime. Not itself a cryptographic ownership proof: it is not secret, and only prevents two
/// concurrently running Skyrim lifetimes from colliding over the same discovery data -- the
/// handshake's HostProof is what proves ownership.
/// </summary>
public readonly record struct OwnerLifetimeId
{
    /// <summary>The owning Skyrim process's OS process id.</summary>
    public uint ProcessId { get; }

    /// <summary>The owning Skyrim process's creation timestamp, a Win32 <c>FILETIME</c> value.</summary>
    public ulong CreationTime { get; }

    /// <summary>Creates an identity wrapping an existing process id and creation timestamp.</summary>
    /// <param name="processId">The owning Skyrim process's OS process id.</param>
    /// <param name="creationTime">The owning Skyrim process's creation timestamp.</param>
    public OwnerLifetimeId(uint processId, ulong creationTime)
    {
        ProcessId = processId;
        CreationTime = creationTime;
    }

    /// <summary>
    /// Encodes this identity as its fixed 12-byte wire representation: a 4-byte little-endian
    /// process id, then an 8-byte little-endian creation timestamp -- matching the adapter's own
    /// encoding.
    /// </summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Constants.IpcOwnerLifetimeIdBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), ProcessId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4, 8), CreationTime);
        return bytes;
    }

    /// <summary>Decodes a fixed 12-byte wire representation, as produced by <see cref="ToBytes"/> or the adapter's own encoding.</summary>
    /// <param name="bytes">Exactly <see cref="Constants.IpcOwnerLifetimeIdBytes"/> bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not exactly <see cref="Constants.IpcOwnerLifetimeIdBytes"/> bytes.</exception>
    public static OwnerLifetimeId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Constants.IpcOwnerLifetimeIdBytes)
        {
            throw new ArgumentException(
                $"An owner lifetime id must be exactly {Constants.IpcOwnerLifetimeIdBytes} bytes.", nameof(bytes));
        }

        uint processId = BinaryPrimitives.ReadUInt32LittleEndian(bytes[..4]);
        ulong creationTime = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(4, 8));
        return new OwnerLifetimeId(processId, creationTime);
    }

    /// <summary>Formats this identity as 24 lowercase hex characters, matching the adapter's own format.</summary>
    public string Format() => Convert.ToHexStringLower(ToBytes());

    /// <summary>Parses a lifetime identity previously produced by <see cref="Format"/> or the adapter's own formatting.</summary>
    /// <param name="text">The candidate hex text to parse, or <see langword="null"/>.</param>
    /// <param name="result">The parsed identity when this returns <see langword="true"/>; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is exactly 24 lowercase hex characters.</returns>
    public static bool TryParse(string? text, out OwnerLifetimeId result)
    {
        result = default;
        if (text is null || text.Length != Constants.IpcOwnerLifetimeIdBytes * 2)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            return false;
        }

        // Convert.FromHexString accepts uppercase too; enforce the canonical lowercase form the
        // adapter emits, matching the adapter's own parser rejecting uppercase.
        if (text != Convert.ToHexStringLower(bytes))
        {
            return false;
        }

        result = FromBytes(bytes);
        return true;
    }
}
