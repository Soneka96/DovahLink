using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.Process;

/// <summary>Tests for <see cref="FileHostRendezvousPublisher"/> and <see cref="Constants.RendezvousFilePath"/>.</summary>
public class HostRendezvousTests : IDisposable
{
    /// <summary>A temporary file path this test's publisher writes to, cleaned up after the test.</summary>
    private readonly string tempFilePath = Path.Combine(Path.GetTempPath(), $"dovahlink-rendezvous-test-{Guid.NewGuid():N}.dat");

    /// <summary>Verifies that Publish writes the port, proof token, and HostProof key in the expected text format.</summary>
    [Fact]
    public void Publish_WritesPortAndProofTokenInExpectedFormat()
    {
        var publisher = new FileHostRendezvousPublisher(tempFilePath);

        publisher.Publish(12345, [0xA0, 0xB1, 0xC2], [0xD3, 0xE4]);

        string content = File.ReadAllText(tempFilePath);
        Assert.Equal(
            $"PORT 12345{Environment.NewLine}PROOF a0b1c2{Environment.NewLine}HOSTPROOF d3e4{Environment.NewLine}", content);
    }

    /// <summary>Verifies that a second Publish overwrites the first, rather than appending.</summary>
    [Fact]
    public void Publish_Repeated_OverwritesPreviousContent()
    {
        var publisher = new FileHostRendezvousPublisher(tempFilePath);
        publisher.Publish(1111, [0x01, 0x02, 0x03], [0x04]);

        publisher.Publish(2222, [0x02], [0x05]);

        string content = File.ReadAllText(tempFilePath);
        Assert.Equal(
            $"PORT 2222{Environment.NewLine}PROOF 02{Environment.NewLine}HOSTPROOF 05{Environment.NewLine}", content);
    }

    /// <summary>Verifies that Publish creates its containing directory when it does not already exist.</summary>
    [Fact]
    public void Publish_MissingDirectory_CreatesIt()
    {
        string nestedPath = Path.Combine(Path.GetTempPath(), $"dovahlink-rendezvous-test-dir-{Guid.NewGuid():N}", "rendezvous.dat");
        var publisher = new FileHostRendezvousPublisher(nestedPath);

        try
        {
            publisher.Publish(1, [0xFF], [0xEE]);

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            string directory = Path.GetDirectoryName(nestedPath)!;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that publishing leaves no temporary rendezvous artifact beside the target.</summary>
    [Fact]
    public void Publish_LeavesNoTemporaryArtifact()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dovahlink-rendezvous-test-dir-{Guid.NewGuid():N}");
        string targetPath = Path.Combine(directory, "rendezvous.dat");
        var publisher = new FileHostRendezvousPublisher(targetPath);

        try
        {
            publisher.Publish(1234, [0xAB, 0xCD], [0xEF]);

            Assert.Equal([targetPath], Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that a failed replacement propagates its error and removes the temporary record.</summary>
    [Fact]
    public void Publish_FailedReplacement_CleansTemporaryArtifact()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dovahlink-rendezvous-test-dir-{Guid.NewGuid():N}");
        string targetPath = Path.Combine(directory, "rendezvous.dat");
        Directory.CreateDirectory(targetPath);
        var publisher = new FileHostRendezvousPublisher(targetPath);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => publisher.Publish(1234, [0xAB], [0xCD]));

            Assert.Empty(Directory.GetFiles(directory));
            Assert.True(Directory.Exists(targetPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Verifies that a null proof token is rejected before any file is created.</summary>
    [Fact]
    public void Publish_NullProofToken_Throws()
    {
        var publisher = new FileHostRendezvousPublisher(tempFilePath);

        Assert.Throws<ArgumentNullException>(() => publisher.Publish(1234, null!, [0xAB]));
        Assert.False(File.Exists(tempFilePath));
    }

    /// <summary>Verifies that a null HostProof key is rejected before any file is created.</summary>
    [Fact]
    public void Publish_NullHostProofKey_Throws()
    {
        var publisher = new FileHostRendezvousPublisher(tempFilePath);

        Assert.Throws<ArgumentNullException>(() => publisher.Publish(1234, [0xAB], null!));
        Assert.False(File.Exists(tempFilePath));
    }

    /// <summary>Verifies that two different owner-lifetime-ids resolve to two different rendezvous file paths.</summary>
    [Fact]
    public void RendezvousFilePath_DifferentOwnerLifetimeIds_NeverCollide()
    {
        var first = new OwnerLifetimeId(1111, 2222);
        var second = new OwnerLifetimeId(3333, 4444);

        string firstPath = Constants.RendezvousFilePath(first);
        string secondPath = Constants.RendezvousFilePath(second);

        Assert.NotEqual(firstPath, secondPath);
    }

    /// <summary>Verifies that the same owner-lifetime-id always resolves to the same rendezvous file path.</summary>
    [Fact]
    public void RendezvousFilePath_SameOwnerLifetimeId_IsStable()
    {
        var id = new OwnerLifetimeId(555, 666);

        Assert.Equal(Constants.RendezvousFilePath(id), Constants.RendezvousFilePath(id));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }
}
