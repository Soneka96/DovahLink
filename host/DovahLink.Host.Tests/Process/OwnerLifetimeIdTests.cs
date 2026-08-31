using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.Process;

/// <summary>Tests for <see cref="OwnerLifetimeId"/>.</summary>
public class OwnerLifetimeIdTests
{
    /// <summary>Verifies the exact little-endian byte layout: process id first, then creation timestamp.</summary>
    [Fact]
    public void ToBytes_KnownValues_MatchesExpectedLittleEndianLayout()
    {
        var id = new OwnerLifetimeId(processId: 0x01020304, creationTime: 0x0102030405060708);

        byte[] bytes = id.ToBytes();

        Assert.Equal(Convert.FromHexString("040302010807060504030201"), bytes);
    }

    /// <summary>Verifies that a value round-trips through ToBytes/FromBytes.</summary>
    [Fact]
    public void RoundTrip_ToBytesFromBytes()
    {
        var original = new OwnerLifetimeId(processId: 4321, creationTime: 9876543210UL);

        var decoded = OwnerLifetimeId.FromBytes(original.ToBytes());

        Assert.Equal(original, decoded);
    }

    /// <summary>Verifies the exact little-endian byte layout in the decode direction: process id first, then creation timestamp.</summary>
    [Fact]
    public void FromBytes_KnownBytes_DecodesExpectedFields()
    {
        var id = OwnerLifetimeId.FromBytes(Convert.FromHexString("040302010807060504030201"));

        Assert.Equal(0x01020304u, id.ProcessId);
        Assert.Equal(0x0102030405060708UL, id.CreationTime);
    }

    /// <summary>Verifies that a value decoded via FromBytes re-formats to the same hex text.</summary>
    [Fact]
    public void FromBytes_ThenFormat_RoundTrips()
    {
        string originalHex = "040302010807060504030201";

        string reformatted = OwnerLifetimeId.FromBytes(Convert.FromHexString(originalHex)).Format();

        Assert.Equal(originalHex, reformatted);
    }

    /// <summary>Verifies that a value round-trips through Format/TryParse.</summary>
    [Fact]
    public void RoundTrip_FormatTryParse()
    {
        var original = new OwnerLifetimeId(processId: 4321, creationTime: 9876543210UL);

        bool parsed = OwnerLifetimeId.TryParse(original.Format(), out OwnerLifetimeId decoded);

        Assert.True(parsed);
        Assert.Equal(original, decoded);
    }

    /// <summary>Verifies that Format produces exactly 24 lowercase hex characters.</summary>
    [Fact]
    public void Format_ProducesExactly24LowercaseHexCharacters()
    {
        string text = new OwnerLifetimeId(1, 2).Format();

        Assert.Equal(24, text.Length);
        Assert.Matches("^[0-9a-f]{24}$", text);
    }

    /// <summary>Verifies that FromBytes rejects a span of the wrong length.</summary>
    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(0)]
    public void FromBytes_WrongLength_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => OwnerLifetimeId.FromBytes(new byte[length]));
    }

    /// <summary>Verifies that TryParse rejects null and text of the wrong length.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0102030405060708090a0b")] // 22 chars
    [InlineData("0102030405060708090a0b0c0d")] // 26 chars
    public void TryParse_NullOrWrongLength_ReturnsFalse(string? text)
    {
        bool parsed = OwnerLifetimeId.TryParse(text, out OwnerLifetimeId result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    /// <summary>Verifies that TryParse rejects uppercase hex, matching the adapter's own strict lowercase parser.</summary>
    [Fact]
    public void TryParse_Uppercase_ReturnsFalse()
    {
        bool parsed = OwnerLifetimeId.TryParse("0102030405060708090A0B0C", out OwnerLifetimeId result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    /// <summary>Verifies that TryParse rejects non-hex characters.</summary>
    [Fact]
    public void TryParse_NonHexCharacters_ReturnsFalse()
    {
        bool parsed = OwnerLifetimeId.TryParse("0102030405060708090a0b0g", out OwnerLifetimeId result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    /// <summary>Verifies that an all-zero and an all-0xFF identity each round-trip through Format/TryParse.</summary>
    [Fact]
    public void RoundTrip_AllZeroAndAllMaxValues()
    {
        var allZero = new OwnerLifetimeId(0, 0);
        var allMax = new OwnerLifetimeId(uint.MaxValue, ulong.MaxValue);

        Assert.True(OwnerLifetimeId.TryParse(allZero.Format(), out OwnerLifetimeId decodedZero));
        Assert.True(OwnerLifetimeId.TryParse(allMax.Format(), out OwnerLifetimeId decodedMax));
        Assert.Equal(allZero, decodedZero);
        Assert.Equal(allMax, decodedMax);
    }

    /// <summary>Verifies mixed boundary combinations: a zero process id with a maximum creation time, and vice versa.</summary>
    [Fact]
    public void RoundTrip_MixedBoundaryValues()
    {
        var zeroProcessId = new OwnerLifetimeId(0, ulong.MaxValue);
        var zeroCreationTime = new OwnerLifetimeId(uint.MaxValue, 0);

        Assert.True(OwnerLifetimeId.TryParse(zeroProcessId.Format(), out OwnerLifetimeId decodedZeroProcessId));
        Assert.True(OwnerLifetimeId.TryParse(zeroCreationTime.Format(), out OwnerLifetimeId decodedZeroCreationTime));
        Assert.Equal(zeroProcessId, decodedZeroProcessId);
        Assert.Equal(zeroCreationTime, decodedZeroCreationTime);
    }
}
