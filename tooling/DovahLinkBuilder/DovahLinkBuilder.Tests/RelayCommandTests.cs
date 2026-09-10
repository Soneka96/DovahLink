using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="RelayCommand"/>'s execution and eligibility behavior.</summary>
public sealed class RelayCommandTests
{
    /// <summary>Invokes the supplied execute delegate when executed.</summary>
    [Fact]
    public void ExecuteInvokesTheSuppliedDelegate()
    {
        var executed = false;
        var command = new RelayCommand(() => executed = true);

        command.Execute(null);

        Assert.True(executed);
    }

    /// <summary>Reports eligible to execute when no <c>canExecute</c> predicate was supplied.</summary>
    [Fact]
    public void CanExecuteReturnsTrueWhenNoPredicateWasSupplied()
    {
        var command = new RelayCommand(() => { });

        Assert.True(command.CanExecute(null));
    }

    /// <summary>Reports the supplied <c>canExecute</c> predicate's result.</summary>
    /// <param name="predicateResult">The value the predicate returns.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanExecuteReturnsThePredicateResult(bool predicateResult)
    {
        var command = new RelayCommand(() => { }, () => predicateResult);

        Assert.Equal(predicateResult, command.CanExecute(null));
    }

    /// <summary>Raises <see cref="RelayCommand.CanExecuteChanged"/> when requested.</summary>
    [Fact]
    public void RaiseCanExecuteChangedRaisesTheEvent()
    {
        var command = new RelayCommand(() => { });
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }

    /// <summary>Rejects a <see langword="null"/> execute delegate at construction rather than failing later inside <see cref="RelayCommand.Execute"/>.</summary>
    [Fact]
    public void ConstructorRejectsANullExecuteDelegate()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }
}
