using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Identity;

/// <summary>Tests for <see cref="ChallengeId"/>.</summary>
public class ChallengeIdTests
{
    /// <summary>Verifies that each call to <see cref="ChallengeId.NewId"/> produces a distinct value.</summary>
    [Fact]
    public void NewId_ReturnsDistinctValues()
    {
        ChallengeId first = ChallengeId.NewId();
        ChallengeId second = ChallengeId.NewId();

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies that two identifiers wrapping the same value are equal.</summary>
    [Fact]
    public void Equals_SameUnderlyingValue_AreEqual()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new ChallengeId(value), new ChallengeId(value));
    }

    /// <summary>Verifies that <see cref="ChallengeId.ToString"/> round-trips the underlying value.</summary>
    [Fact]
    public void ToString_ReturnsUnderlyingValueText()
    {
        Guid value = Guid.NewGuid();
        var challengeId = new ChallengeId(value);

        Assert.Equal(value.ToString(), challengeId.ToString());
    }
}
