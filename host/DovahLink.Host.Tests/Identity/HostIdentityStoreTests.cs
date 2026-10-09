using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests durable creation, reuse, and validation of the Host installation ID.</summary>
public sealed class HostIdentityStoreTests : IDisposable
{
    /// <summary>The isolated identity file used by each test.</summary>
    private readonly string filePath = Path.Combine(Path.GetTempPath(), $"dovahlink-host-id-{Guid.NewGuid():N}", "host-id.dat");

    /// <summary>Verifies first use generates and persists a reusable UUID.</summary>
    [Fact]
    public void LoadOrCreate_NoFile_GeneratesAndPersistsId()
    {
        var store = new FileHostIdentityStore(filePath);

        HostId first = store.LoadOrCreate();
        HostId afterRestart = new FileHostIdentityStore(filePath).LoadOrCreate();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.Equal(first, afterRestart);
        Assert.Equal(first.ToString(), File.ReadAllText(filePath));
    }

    /// <summary>Verifies deliberately recreating the identity file creates a new Host ID.</summary>
    [Fact]
    public void LoadOrCreate_AfterIdentityFileRemoval_GeneratesNewId()
    {
        var store = new FileHostIdentityStore(filePath);
        HostId first = store.LoadOrCreate();
        File.Delete(filePath);

        HostId recreated = store.LoadOrCreate();

        Assert.NotEqual(first, recreated);
    }

    /// <summary>Verifies concurrent Host processes converge on the one atomically created ID.</summary>
    [Fact]
    public void LoadOrCreate_ConcurrentStores_UseOneId()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<HostId>();

        Parallel.For(0, 16, _ => ids.Add(new FileHostIdentityStore(filePath).LoadOrCreate()));

        Assert.Single(ids.Distinct());
    }

    /// <summary>
    /// Verifies the existing ID is still read while another handle with delete access is open on the
    /// file, as during another Host process's atomic rename of its newly created identity file.
    /// </summary>
    [Fact]
    public void LoadOrCreate_FileOpenWithDeleteAccess_StillReadsExistingId()
    {
        HostId created = new FileHostIdentityStore(filePath).LoadOrCreate();

        HostId read;
        using (new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose))
        {
            read = new FileHostIdentityStore(filePath).LoadOrCreate();
        }

        Assert.Equal(created, read);
    }

    /// <summary>Verifies malformed persisted identity fails closed instead of silently rotating the ID.</summary>
    [Fact]
    public void LoadOrCreate_MalformedFile_ThrowsInvalidData()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, new string('a', 36));

        Assert.Throws<InvalidDataException>(() => new FileHostIdentityStore(filePath).LoadOrCreate());
    }

    /// <summary>Verifies the empty UUID is rejected even though it has a valid UUID shape.</summary>
    [Fact]
    public void LoadOrCreate_EmptyUuidFile_ThrowsInvalidData()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, Guid.Empty.ToString("D"));

        Assert.Throws<InvalidDataException>(() => new FileHostIdentityStore(filePath).LoadOrCreate());
    }

    /// <summary>Verifies an oversized identity file is rejected before parsing.</summary>
    [Fact]
    public void LoadOrCreate_OversizedFile_ThrowsInvalidData()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, new string('a', 4097));

        Assert.Throws<InvalidDataException>(() => new FileHostIdentityStore(filePath).LoadOrCreate());
    }

    /// <summary>Verifies the store rejects a missing or relative identity path.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("relative-host-id.dat")]
    public void Constructor_InvalidPath_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => new FileHostIdentityStore(path));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
