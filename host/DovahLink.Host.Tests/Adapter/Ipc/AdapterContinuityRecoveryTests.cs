using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterContinuityRecovery"/>.</summary>
public class AdapterContinuityRecoveryTests
{
    /// <summary>Verifies that recovery closes the active connection for the matching generation.</summary>
    [Fact]
    public void RequestRecovery_MatchingGeneration_RequestsClose()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { ConnectionGeneration = 7 };
        var recovery = new AdapterContinuityRecovery();
        recovery.SetCurrentConnection(connection);

        recovery.RequestRecovery(7);

        Assert.Equal(1, connection.RequestCloseCalls);
    }

    /// <summary>Verifies that a stale generation cannot close a newer active connection.</summary>
    [Fact]
    public void RequestRecovery_StaleGeneration_DoesNotRequestClose()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { ConnectionGeneration = 8 };
        var recovery = new AdapterContinuityRecovery();
        recovery.SetCurrentConnection(connection);

        recovery.RequestRecovery(7);

        Assert.Equal(0, connection.RequestCloseCalls);
    }
}
