namespace DovahLink.Host.Process;

/// <summary>
/// Publishes the host's currently bound private-IPC loopback port and peer-proof token to a
/// discovery location, so an adapter belonging to the same Skyrim lifetime can find a candidate
/// endpoint to adopt. Discovery only, never authentication: a stale or forged rendezvous record
/// can only ever name a candidate endpoint to attempt -- the adapter still must pass the full
/// mutual Hello/HelloAck handshake before treating it as its host.
/// </summary>
public interface IHostRendezvousPublisher
{
    /// <summary>Publishes the current port and peer-proof token, replacing any previous content.</summary>
    /// <param name="port">The host's currently bound private-IPC loopback port.</param>
    /// <param name="peerProofToken">The host's own peer-ownership proof token.</param>
    void Publish(int port, byte[] peerProofToken);
}

/// <inheritdoc cref="IHostRendezvousPublisher"/>
public sealed class FileHostRendezvousPublisher : IHostRendezvousPublisher
{
    /// <summary>The rendezvous file this instance publishes to.</summary>
    private readonly string filePath;

    /// <summary>Creates a publisher that writes to an explicit file path.</summary>
    /// <param name="filePath">
    /// The rendezvous file to write to, typically <see cref="Constants.RendezvousFilePath"/> for the
    /// current Skyrim lifetime.
    /// </param>
    public FileHostRendezvousPublisher(string filePath)
    {
        this.filePath = filePath;
    }

    /// <inheritdoc/>
    public void Publish(int port, byte[] peerProofToken)
    {
        ArgumentNullException.ThrowIfNull(peerProofToken);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string content = $"PORT {port}{Environment.NewLine}PROOF {Convert.ToHexStringLower(peerProofToken)}{Environment.NewLine}";
        string temporaryFilePath = $"{filePath}.{Path.GetRandomFileName()}.tmp";
        try
        {
            // Write beside the target so replacing it is a same-volume rename: readers see either
            // the previous complete record or this complete record, never a partially written one.
            File.WriteAllText(temporaryFilePath, content);
            File.Move(temporaryFilePath, filePath, overwrite: true);
        }
        finally
        {
            // Move removes the temporary path on success; this also cleans it up after a failed
            // write or replacement attempt.
            File.Delete(temporaryFilePath);
        }
    }
}
