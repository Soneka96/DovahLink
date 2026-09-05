using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>
/// An in-memory stand-in for <see cref="ITrustStorePersistence"/> that behaves like a durable
/// backing store shared across separate <see cref="TrustStore"/> instances, so tests can simulate
/// a host restart by constructing a second store over the same fake.
/// </summary>
public sealed class FakeTrustStorePersistence : ITrustStorePersistence
{
    /// <summary>The backing store the fake reads and writes, standing in for a durable file.</summary>
    private IReadOnlyList<TrustRecord> savedRecords = [];

    /// <summary>The most recent set of records passed to <see cref="SaveAsync"/>, for test assertions.</summary>
    public IReadOnlyList<TrustRecord> SavedRecords => savedRecords;

    /// <summary>The number of times <see cref="SaveAsync"/> has been called.</summary>
    public int SaveCallCount { get; private set; }

    /// <summary>When set, <see cref="LoadAsync"/> throws this instead of returning.</summary>
    public Exception? ThrowOnLoad { get; set; }

    /// <summary>When set, <see cref="SaveAsync"/> throws this instead of saving.</summary>
    public Exception? ThrowOnSave { get; set; }

    /// <summary>
    /// Optional asynchronous work awaited before <see cref="SaveAsync"/> stores its records, letting a
    /// test hold one mutation's persistence in flight while a concurrent caller races it against the
    /// owning <see cref="TrustStore"/>'s own mutation lock.
    /// </summary>
    public Func<Task>? BeforeSave { get; set; }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrustRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnLoad is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(savedRecords);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(IReadOnlyList<TrustRecord> records, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave is { } exception)
        {
            throw exception;
        }

        if (BeforeSave is { } beforeSave)
        {
            await beforeSave();
        }

        savedRecords = records;
        SaveCallCount++;
    }
}
