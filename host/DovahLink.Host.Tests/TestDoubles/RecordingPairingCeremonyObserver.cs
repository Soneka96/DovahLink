using System.Collections.Concurrent;
using DovahLink.Host.PairingCeremony;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>Records every ceremony-host notification, and can run a test reaction or throw from any of them.</summary>
internal sealed class RecordingPairingCeremonyObserver : IPairingCeremonyObserver
{
    /// <summary>The attempts whose exposure authorization was requested.</summary>
    public ConcurrentQueue<CeremonyAttemptId> ExposureRequests { get; } = new();

    /// <summary>The SAS comparisons requested.</summary>
    public ConcurrentQueue<SasComparisonRequest> SasRequests { get; } = new();

    /// <summary>The local completions reported, with their attempts.</summary>
    public ConcurrentQueue<(CeremonyAttemptId? Attempt, CeremonyResultSnapshot Result)> Completions { get; } = new();

    /// <summary>The attempts reported ended without a result.</summary>
    public ConcurrentQueue<CeremonyAttemptId> EndedAttempts { get; } = new();

    /// <summary>The failures reported, with whether a process restart is required.</summary>
    public ConcurrentQueue<(PairingCeremonyFailure Failure, bool ProcessRestartRequired)> Failures { get; } = new();

    /// <summary>The managed thread ID of every notification.</summary>
    public ConcurrentBag<int> NotifyingThreads { get; } = [];

    /// <summary>Run after an exposure request is recorded.</summary>
    public Action<CeremonyAttemptId>? OnExposure { get; set; }

    /// <summary>Run after a SAS request is recorded.</summary>
    public Action<SasComparisonRequest>? OnSas { get; set; }

    /// <summary>When set, every notification throws after being recorded.</summary>
    public bool ThrowFromEveryNotification { get; set; }

    /// <inheritdoc/>
    public void OnExposureAuthorizationRequested(CeremonyAttemptId attempt)
    {
        ExposureRequests.Enqueue(attempt);
        Finish();
        OnExposure?.Invoke(attempt);
        ThrowIfConfigured();
    }

    /// <inheritdoc/>
    public void OnSasComparisonRequested(SasComparisonRequest request)
    {
        SasRequests.Enqueue(request);
        Finish();
        OnSas?.Invoke(request);
        ThrowIfConfigured();
    }

    /// <inheritdoc/>
    public void OnCeremonyCompletedLocally(CeremonyAttemptId? attempt, CeremonyResultSnapshot result)
    {
        Completions.Enqueue((attempt, result));
        Finish();
        ThrowIfConfigured();
    }

    /// <inheritdoc/>
    public void OnAttemptEnded(CeremonyAttemptId attempt)
    {
        EndedAttempts.Enqueue(attempt);
        Finish();
        ThrowIfConfigured();
    }

    /// <inheritdoc/>
    public void OnFailed(PairingCeremonyFailure failure, bool processRestartRequired)
    {
        Failures.Enqueue((failure, processRestartRequired));
        Finish();
        ThrowIfConfigured();
    }

    /// <summary>Records the notifying thread.</summary>
    private void Finish() => NotifyingThreads.Add(Environment.CurrentManagedThreadId);

    /// <summary>Throws when configured to.</summary>
    private void ThrowIfConfigured()
    {
        if (ThrowFromEveryNotification)
        {
            throw new InvalidOperationException("Observer failure injected by the test.");
        }
    }
}
