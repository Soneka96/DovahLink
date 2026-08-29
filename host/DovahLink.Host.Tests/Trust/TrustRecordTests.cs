using DovahLink.Host.Identity;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Trust;

/// <summary>Tests for <see cref="TrustRecord"/>.</summary>
public class TrustRecordTests
{
    /// <summary>Verifies that a constructed record exposes the values it was given.</summary>
    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        ClientId clientId = ClientId.NewId();
        DateTimeOffset pairedAt = DateTimeOffset.UtcNow;

        var record = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", pairedAt);

        Assert.Equal(clientId, record.ClientId);
        Assert.Equal("AB12", record.ShortId);
        Assert.Equal("Living Room PC", record.DisplayName);
        Assert.Equal(KnownDeviceState.Trusted, record.State);
        Assert.Equal("deadbeef", record.CredentialVerifier);
        Assert.Equal(pairedAt, record.PairedAtUtc);
    }

    /// <summary>Verifies that two records with identical field values are equal.</summary>
    [Fact]
    public void Equals_SameValues_AreEqual()
    {
        ClientId clientId = ClientId.NewId();
        DateTimeOffset pairedAt = DateTimeOffset.UtcNow;

        var first = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", pairedAt);
        var second = new TrustRecord(clientId, "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", pairedAt);

        Assert.Equal(first, second);
    }

    /// <summary>Verifies that a `with`-expression changing only the state leaves every other field unchanged.</summary>
    [Fact]
    public void With_ChangesOnlyTheStatedField()
    {
        var original = new TrustRecord(
            ClientId.NewId(), "AB12", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow);

        TrustRecord revoked = original with { State = KnownDeviceState.Revoked };

        Assert.Equal(KnownDeviceState.Revoked, revoked.State);
        Assert.Equal(original.ClientId, revoked.ClientId);
        Assert.Equal(original.ShortId, revoked.ShortId);
        Assert.Equal(original.DisplayName, revoked.DisplayName);
        Assert.Equal(original.CredentialVerifier, revoked.CredentialVerifier);
        Assert.Equal(original.PairedAtUtc, revoked.PairedAtUtc);
    }
}
