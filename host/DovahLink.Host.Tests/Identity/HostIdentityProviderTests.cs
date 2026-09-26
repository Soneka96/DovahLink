using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests Host ID stability and mutable OS-derived machine-name metadata.</summary>
public class HostIdentityProviderTests
{
    /// <summary>Verifies the provider combines the persisted Host ID with the OS computer name.</summary>
    [Fact]
    public void GetCurrent_ReturnsPersistedIdAndProvidedMachineName()
    {
        HostId id = new(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));
        var store = new FakeHostIdentityStore(id);
        var provider = new HostIdentityProvider(store, new FakeHostMachineNameProvider("GONCALO-DESKTOP"));

        HostIdentity identity = provider.GetCurrent();

        Assert.Equal(id, identity.HostId);
        Assert.Equal("GONCALO-DESKTOP", identity.HostName);
        Assert.Equal(1, store.LoadCount);
    }

    /// <summary>Verifies machine rename changes display metadata while preserving the persisted Host ID.</summary>
    [Fact]
    public void GetCurrent_MachineNameChanges_LeavesHostIdUnchanged()
    {
        HostId id = new(Guid.Parse("81869993-955c-4ba3-a7d0-d35ca86078ea"));
        var store = new FakeHostIdentityStore(id);
        var first = new HostIdentityProvider(store, new FakeHostMachineNameProvider("DESKTOP-A19F2"));
        var renamed = new HostIdentityProvider(store, new FakeHostMachineNameProvider("GONCALO-DESKTOP"));

        HostIdentity before = first.GetCurrent();
        HostIdentity after = renamed.GetCurrent();

        Assert.Equal(before.HostId, after.HostId);
        Assert.Equal("DESKTOP-A19F2", before.HostName);
        Assert.Equal("GONCALO-DESKTOP", after.HostName);
    }

    /// <summary>Verifies the UTF-8 byte bound accepts its exact limit and rejects a multibyte overflow.</summary>
    [Fact]
    public void GetCurrent_MachineNameAtUtf8Limit_IsAcceptedButOverflowFallsBack()
    {
        var store = new FakeHostIdentityStore(HostId.NewId());
        string atLimit = new('é', Constants.MaxDisplayNameLengthBytes / 2);
        var valid = new HostIdentityProvider(store, new FakeHostMachineNameProvider(atLimit));
        var oversized = new HostIdentityProvider(store, new FakeHostMachineNameProvider($"{atLimit}é"));

        Assert.Equal(atLimit, valid.GetCurrent().HostName);
        Assert.Equal("Skyrim PC", oversized.GetCurrent().HostName);
    }

    /// <summary>Verifies unavailable or unsafe names use a bounded fallback without blocking identity lookup.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid\nname")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void GetCurrent_InvalidMachineName_UsesSafeFallback(string name)
    {
        var provider = new HostIdentityProvider(new FakeHostIdentityStore(HostId.NewId()), new FakeHostMachineNameProvider(name));

        HostIdentity identity = provider.GetCurrent();

        Assert.Equal("Skyrim PC", identity.HostName);
    }

    /// <summary>Verifies a failing OS-name provider does not prevent Host identity creation.</summary>
    [Fact]
    public void GetCurrent_MachineNameProviderThrows_UsesSafeFallback()
    {
        var provider = new HostIdentityProvider(
            new FakeHostIdentityStore(HostId.NewId()), new FakeHostMachineNameProvider(exception: new InvalidOperationException()));

        Assert.Equal("Skyrim PC", provider.GetCurrent().HostName);
    }

    /// <summary>Verifies failure to load persistent identity is not hidden by the machine-name fallback.</summary>
    [Fact]
    public void GetCurrent_IdentityStoreThrows_PropagatesFailure()
    {
        var provider = new HostIdentityProvider(
            new ThrowingHostIdentityStore(), new FakeHostMachineNameProvider("GONCALO-DESKTOP"));

        Assert.Throws<InvalidDataException>(() => provider.GetCurrent());
    }

    /// <summary>A controllable Host ID store for provider tests.</summary>
    private sealed class FakeHostIdentityStore(HostId id) : IHostIdentityStore
    {
        /// <summary>The number of calls made to <see cref="LoadOrCreate"/>.</summary>
        public int LoadCount { get; private set; }

        /// <inheritdoc/>
        public HostId LoadOrCreate()
        {
            LoadCount++;
            return id;
        }
    }

    /// <summary>A controllable computer-name provider for provider tests.</summary>
    private sealed class FakeHostMachineNameProvider(string? name = null, Exception? exception = null) : IHostMachineNameProvider
    {
        /// <inheritdoc/>
        public string GetMachineName() => exception is null ? name! : throw exception;
    }

    /// <summary>A store that exposes a persistence failure to the provider test.</summary>
    private sealed class ThrowingHostIdentityStore : IHostIdentityStore
    {
        /// <inheritdoc/>
        public HostId LoadOrCreate() => throw new InvalidDataException("Invalid Host identity data.");
    }
}
