using DovahLink.DovahLinkBuilder.Ui;

namespace DovahLink.DovahLinkBuilder.Tests;

/// <summary>Verifies <see cref="ObservableObject"/>'s change-notification behavior.</summary>
public sealed class ObservableObjectTests
{
    /// <summary>Raises <c>PropertyChanged</c> when a property's value changes.</summary>
    [Fact]
    public void SetPropertyRaisesPropertyChangedWhenTheValueChanges()
    {
        var subject = new TestObservable();
        var raisedPropertyNames = new List<string?>();
        subject.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        subject.Value = "new value";

        Assert.Equal([nameof(TestObservable.Value)], raisedPropertyNames);
    }

    /// <summary>Does not raise <c>PropertyChanged</c> when the assigned value equals the current one.</summary>
    [Fact]
    public void SetPropertyDoesNotRaisePropertyChangedWhenTheValueIsUnchanged()
    {
        var subject = new TestObservable { Value = "same" };
        var raisedPropertyNames = new List<string?>();
        subject.PropertyChanged += (_, args) => raisedPropertyNames.Add(args.PropertyName);

        subject.Value = "same";

        Assert.Empty(raisedPropertyNames);
    }

    /// <summary>Reports whether the value changed through its return value.</summary>
    [Fact]
    public void SetValueReportsWhetherTheValueChanged()
    {
        var subject = new TestObservable { Value = "same" };

        Assert.True(subject.SetValue("different"));
        Assert.False(subject.SetValue("different"));
    }

    /// <summary>A minimal <see cref="ObservableObject"/> subclass exposing one property for testing.</summary>
    private sealed class TestObservable : ObservableObject
    {
        /// <summary>The backing field for <see cref="Value"/>.</summary>
        private string value = "";

        /// <summary>Gets or sets a test value that raises change notifications through <c>SetProperty</c>.</summary>
        public string Value
        {
            get => value;
            set => SetProperty(ref this.value, value);
        }

        /// <summary>Sets <see cref="Value"/> and returns whether <c>SetProperty</c> reported a change.</summary>
        /// <param name="newValue">The value to assign.</param>
        public bool SetValue(string newValue) => SetProperty(ref value, newValue, nameof(Value));
    }
}
