using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterPairingNotifier"/>.</summary>
public class AdapterPairingNotifierTests
{
    // ---- TryNotifyCodeAvailableAsync ----

    /// <summary>Verifies that a missing adapter connection is reported as unavailable rather than throwing.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_AdapterAbsent_ReturnsFalse()
    {
        var listener = new FakeAdapterIpcListener { CurrentConnection = null };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>Verifies that a request the connection could not enqueue never awaits an acknowledgement.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_SendFails_ReturnsFalseWithoutAwaitingAck()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingDisplayResult = false };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);

        Assert.False(result);
        Assert.Empty(connection.AwaitPairingDisplayAckAsyncCalls);
    }

    /// <summary>Verifies that an accepted acknowledgement is reported as available.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_AckAccepted_ReturnsTrue()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream())
        {
            TrySendPairingDisplayResult = true,
            TrySendPairingDisplayCorrelationId = 9,
            AwaitPairingDisplayAckAsyncResult = (_, _, _) => Task.FromResult(true),
        };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>Verifies that a declined, timed-out, or cancelled acknowledgement is reported as unavailable, identically.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_AckNotAccepted_ReturnsFalse()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream())
        {
            TrySendPairingDisplayResult = true,
            TrySendPairingDisplayCorrelationId = 9,
            AwaitPairingDisplayAckAsyncResult = (_, _, _) => Task.FromResult(false),
        };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyCodeAvailableAsync("123456", CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>Verifies that the code and the initial display mode are forwarded to the connection unchanged.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_PassesCodeAndInitialMode()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingDisplayResult = true };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.TryNotifyCodeAvailableAsync("048372", CancellationToken.None);

        (string code, PairingDisplayMode mode) = Assert.Single(connection.PairingDisplayCalls);
        Assert.Equal("048372", code);
        Assert.Equal(PairingDisplayMode.Initial, mode);
    }

    /// <summary>Verifies that the enqueued correlation id, the configured acknowledgement timeout, and the caller's cancellation token are forwarded to the await call unchanged.</summary>
    [Fact]
    public async Task TryNotifyCodeAvailableAsync_PassesCorrelationIdTimeoutAndCancellationTokenToAwait()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream())
        {
            TrySendPairingDisplayResult = true,
            TrySendPairingDisplayCorrelationId = 9,
        };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);
        using var cancellation = new CancellationTokenSource();

        await notifier.TryNotifyCodeAvailableAsync("123456", cancellation.Token);

        (ulong correlationId, TimeSpan timeout, CancellationToken cancellationToken) = Assert.Single(connection.AwaitPairingDisplayAckAsyncCalls);
        Assert.Equal(9UL, correlationId);
        Assert.Equal(Constants.PairingDisplayAckTimeout, timeout);
        Assert.Equal(cancellation.Token, cancellationToken);
    }

    // ---- TryNotifyRedisplayAsync ----

    /// <summary>Verifies that a missing adapter connection is reported as unavailable rather than throwing.</summary>
    [Fact]
    public async Task TryNotifyRedisplayAsync_AdapterAbsent_ReturnsFalse()
    {
        var listener = new FakeAdapterIpcListener { CurrentConnection = null };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyRedisplayAsync("123456", CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>Verifies that an accepted acknowledgement is reported as redisplayed.</summary>
    [Fact]
    public async Task TryNotifyRedisplayAsync_AckAccepted_ReturnsTrue()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream())
        {
            TrySendPairingDisplayResult = true,
            AwaitPairingDisplayAckAsyncResult = (_, _, _) => Task.FromResult(true),
        };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        bool result = await notifier.TryNotifyRedisplayAsync("123456", CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>Verifies that the manual-redisplay mode, not the initial-display mode, is used.</summary>
    [Fact]
    public async Task TryNotifyRedisplayAsync_UsesManualRedisplayMode()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingDisplayResult = true };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.TryNotifyRedisplayAsync("123456", CancellationToken.None);

        (_, PairingDisplayMode mode) = Assert.Single(connection.PairingDisplayCalls);
        Assert.Equal(PairingDisplayMode.ManualRedisplay, mode);
    }

    // ---- NotifyCodeIncorrectAsync ----

    /// <summary>Verifies that a missing adapter connection completes without throwing.</summary>
    [Fact]
    public async Task NotifyCodeIncorrectAsync_AdapterAbsent_CompletesWithoutThrowing()
    {
        var listener = new FakeAdapterIpcListener { CurrentConnection = null };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyCodeIncorrectAsync("123456", CancellationToken.None);
    }

    /// <summary>Verifies that a declined acknowledgement completes without throwing -- this notification is best effort.</summary>
    [Fact]
    public async Task NotifyCodeIncorrectAsync_AckDeclined_CompletesWithoutThrowing()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream())
        {
            TrySendPairingDisplayResult = true,
            AwaitPairingDisplayAckAsyncResult = (_, _, _) => Task.FromResult(false),
        };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyCodeIncorrectAsync("123456", CancellationToken.None);
    }

    /// <summary>Verifies that a request the connection could not enqueue completes without throwing -- this notification is best effort.</summary>
    [Fact]
    public async Task NotifyCodeIncorrectAsync_SendFails_CompletesWithoutThrowing()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingDisplayResult = false };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyCodeIncorrectAsync("123456", CancellationToken.None);

        Assert.Empty(connection.AwaitPairingDisplayAckAsyncCalls);
    }

    /// <summary>Verifies that the wrong-code-redisplay mode is used, not initial display or manual redisplay.</summary>
    [Fact]
    public async Task NotifyCodeIncorrectAsync_UsesWrongCodeRedisplayMode()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingDisplayResult = true };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyCodeIncorrectAsync("123456", CancellationToken.None);

        (_, PairingDisplayMode mode) = Assert.Single(connection.PairingDisplayCalls);
        Assert.Equal(PairingDisplayMode.WrongCodeRedisplay, mode);
    }

    // ---- NotifyAttemptsExhaustedAsync ----

    /// <summary>Verifies that a missing adapter connection completes without throwing.</summary>
    [Fact]
    public async Task NotifyAttemptsExhaustedAsync_AdapterAbsent_CompletesWithoutThrowing()
    {
        var listener = new FakeAdapterIpcListener { CurrentConnection = null };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyAttemptsExhaustedAsync(CancellationToken.None);
    }

    /// <summary>Verifies that a connected adapter is sent exactly one attempts-exhausted notification.</summary>
    [Fact]
    public async Task NotifyAttemptsExhaustedAsync_AdapterConnected_SendsExactlyOneNotification()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream());
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyAttemptsExhaustedAsync(CancellationToken.None);

        Assert.Equal(1, connection.PairingAttemptsExhaustedCalls);
        Assert.Empty(connection.PairingDisplayCalls);
    }

    /// <summary>Verifies that the connection refusing the send still completes without throwing -- this notification is best effort.</summary>
    [Fact]
    public async Task NotifyAttemptsExhaustedAsync_ConnectionRefuses_CompletesWithoutThrowing()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendPairingAttemptsExhaustedResult = false };
        var listener = new FakeAdapterIpcListener { CurrentConnection = connection };
        var notifier = new AdapterPairingNotifier(listener);

        await notifier.NotifyAttemptsExhaustedAsync(CancellationToken.None);

        Assert.Equal(1, connection.PairingAttemptsExhaustedCalls);
    }
}
