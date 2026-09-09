namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies temporary directory creation and lock-tolerant removal.</summary>
public sealed class TemporaryDirectoryTests
{
    /// <summary>Removes an unlocked directory immediately.</summary>
    [Fact]
    public void DisposeRemovesAnUnlockedDirectory()
    {
        string path;
        using (var temporaryDirectory = new TemporaryDirectory())
        {
            path = temporaryDirectory.Path;
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path));
    }

    /// <summary>Retries past a transient file lock and succeeds once it clears within the retry budget.</summary>
    [Fact]
    public void DisposeRetriesAndSucceedsOnceTheLockClearsWithinTheRetryBudget()
    {
        var temporaryDirectory = new TemporaryDirectory();
        string lockedFilePath = Path.Combine(temporaryDirectory.Path, "locked.txt");
        var lockedStream = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Delay(TimeSpan.FromMilliseconds(100)).ContinueWith(_ => lockedStream.Dispose());

        temporaryDirectory.Dispose();

        Assert.False(Directory.Exists(temporaryDirectory.Path));
    }

    /// <summary>Throws once every retry attempt still finds the directory locked, instead of silently giving up.</summary>
    [Fact]
    public void DisposeThrowsWhenTheDirectoryStaysLockedForEveryAttempt()
    {
        var temporaryDirectory = new TemporaryDirectory();
        string lockedFilePath = Path.Combine(temporaryDirectory.Path, "locked.txt");
        using var lockedStream = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        try
        {
            Assert.Throws<IOException>(() => temporaryDirectory.Dispose());
        }
        finally
        {
            lockedStream.Dispose();
            if (Directory.Exists(temporaryDirectory.Path))
            {
                Directory.Delete(temporaryDirectory.Path, recursive: true);
            }
        }
    }

    /// <summary>Propagates a non-<see cref="IOException"/> failure immediately, without spending the retry budget.</summary>
    [Fact]
    public void DisposeDoesNotRetryANonIOExceptionFailure()
    {
        var temporaryDirectory = new TemporaryDirectory();
        string readOnlyFilePath = Path.Combine(temporaryDirectory.Path, "readonly.txt");
        File.WriteAllText(readOnlyFilePath, "content");
        File.SetAttributes(readOnlyFilePath, FileAttributes.ReadOnly);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => temporaryDirectory.Dispose());
        }
        finally
        {
            File.SetAttributes(readOnlyFilePath, FileAttributes.Normal);
            if (Directory.Exists(temporaryDirectory.Path))
            {
                Directory.Delete(temporaryDirectory.Path, recursive: true);
            }
        }
    }

    /// <summary>Is safe to call more than once.</summary>
    [Fact]
    public void DisposeIsIdempotentWhenCalledTwice()
    {
        var temporaryDirectory = new TemporaryDirectory();

        temporaryDirectory.Dispose();
        temporaryDirectory.Dispose();

        Assert.False(Directory.Exists(temporaryDirectory.Path));
    }
}
