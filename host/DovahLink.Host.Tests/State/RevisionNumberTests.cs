using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="RevisionNumber"/>.</summary>
public class RevisionNumberTests
{
    /// <summary>Verifies that the initial revision wraps zero.</summary>
    [Fact]
    public void Initial_WrapsZero()
    {
        Assert.Equal(0UL, RevisionNumber.Initial.Value);
    }

    /// <summary>Verifies that Next() returns a revision one greater than the current one.</summary>
    [Fact]
    public void Next_ReturnsOneGreaterThanCurrent()
    {
        RevisionNumber current = new(5);

        Assert.Equal(6UL, current.Next().Value);
    }

    /// <summary>Verifies that calling Next() does not mutate the original revision (it is immutable).</summary>
    [Fact]
    public void Next_DoesNotMutateOriginal()
    {
        RevisionNumber original = RevisionNumber.Initial;

        original.Next();

        Assert.Equal(0UL, original.Value);
    }
}
