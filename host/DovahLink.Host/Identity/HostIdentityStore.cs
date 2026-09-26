using System.Text;

namespace DovahLink.Host.Identity;

/// <summary>Loads or atomically creates the persistent identity of this DovahLink Host installation.</summary>
public interface IHostIdentityStore
{
    /// <summary>Returns the persisted Host ID or creates it once when the identity file is absent.</summary>
    /// <returns>The stable Host installation ID.</returns>
    HostId LoadOrCreate();
}

/// <inheritdoc cref="IHostIdentityStore"/>
public sealed class FileHostIdentityStore : IHostIdentityStore
{
    /// <summary>The file that owns this Host installation's persistent identity.</summary>
    private readonly string filePath;

    /// <summary>Creates a store using the Host's per-user identity file.</summary>
    public FileHostIdentityStore()
        : this(Constants.HostIdentityFilePath)
    {
    }

    /// <summary>Creates a store using an explicit file path for isolated tests.</summary>
    /// <param name="filePath">The absolute identity file path.</param>
    /// <exception cref="ArgumentException">The path is empty or not absolute.</exception>
    public FileHostIdentityStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The Host identity path must be absolute.", nameof(filePath));
        }

        this.filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">The identity file exists but does not contain a valid UUID.</exception>
    public HostId LoadOrCreate()
    {
        if (File.Exists(filePath))
        {
            return ReadExisting();
        }

        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        HostId created = HostId.NewId();
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            byte[] bytes = Encoding.ASCII.GetBytes(created.ToString());
            using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, filePath);
                return created;
            }
            catch (IOException) when (File.Exists(filePath))
            {
                // Another Host process created the installation identity first.
                return ReadExisting();
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Reads and validates the existing UUID without silently rotating a corrupt identity.</summary>
    /// <returns>The valid persisted Host ID.</returns>
    /// <exception cref="InvalidDataException">The file is oversized or contains a malformed UUID.</exception>
    private HostId ReadExisting()
    {
        var info = new FileInfo(filePath);
        if (info.Length != 36)
        {
            throw new InvalidDataException("The Host identity file must contain exactly one UUID.");
        }

        string text = File.ReadAllText(filePath, Encoding.ASCII);
        if (!Guid.TryParseExact(text, "D", out Guid value) || value == Guid.Empty)
        {
            throw new InvalidDataException("The Host identity file does not contain a valid UUID.");
        }

        return new HostId(value);
    }
}
