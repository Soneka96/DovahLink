using System.Reflection;
using DovahLink.Host.PairingCeremony.Native;
using SasPairing;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>Tests ordered translation, resource release, and ended-run cleanup for native event batches.</summary>
public sealed class SasPairingNativeSessionTests
{
    /// <summary>Verifies every event translates before flagged connections are released and only ended runs are forgotten.</summary>
    [Fact]
    public void TranslateAndCleanupBatch_TranslatesAllEventsBeforeReleasingConnections()
    {
        var firstResult = new TrackingResource();
        var secondResult = new TrackingResource();
        var thirdResult = new TrackingResource();
        var firstConnection = new TrackingResource();
        var secondConnection = new TrackingResource();
        var thirdConnection = new TrackingResource();
        var liveConnection = new TrackingResource();
        var events = new[]
        {
            new BatchEntry("first", "ended-a", true, firstResult, true, firstConnection),
            new BatchEntry("second", "ended-a", true, secondResult, true, secondConnection),
            new BatchEntry("third", "ended-b", true, thirdResult, true, thirdConnection),
            new BatchEntry("live", "live", false, null, false, liveConnection),
        };
        var trackedRuns = new HashSet<string>(["ended-a", "ended-b", "live"]);
        var translated = new List<string>();
        TrackingResource[] connections = [firstConnection, secondConnection, thirdConnection, liveConnection];

        List<string> output = SasPairingNativeSession.TranslateAndCleanupBatch(
            events,
            evt => Translate(evt, translated, connections),
            evt => evt.Result,
            evt => evt.DisposeConnection,
            evt => evt.Connection,
            evt => evt.Ended,
            evt =>
            {
                Assert.Equal(events.Length, translated.Count);
                trackedRuns.Remove(evt.RunId);
            },
            resource =>
            {
                if (connections.Any(connection => ReferenceEquals(connection, resource)))
                {
                    Assert.Equal(events.Length, translated.Count);
                }

                Release(resource);
            });

        Assert.Equal(["first", "second", "third", "live"], output);
        Assert.Equal(["first", "second", "third", "live"], translated);
        Assert.Equal(1, firstResult.DisposeCount);
        Assert.Equal(1, secondResult.DisposeCount);
        Assert.Equal(1, thirdResult.DisposeCount);
        Assert.Equal(1, firstConnection.DisposeCount);
        Assert.Equal(1, secondConnection.DisposeCount);
        Assert.Equal(1, thirdConnection.DisposeCount);
        Assert.Equal(0, liveConnection.DisposeCount);
        Assert.Equal(["live"], trackedRuns);
    }

    /// <summary>Verifies a mid-batch result-read failure still releases remaining resources and forgets ended runs.</summary>
    [Fact]
    public void TranslateAndCleanupBatch_WhenTranslationFails_CleansRemainingEvents()
    {
        var failedResult = new TrackingResource();
        var laterResult = new TrackingResource();
        var failedConnection = new TrackingResource();
        var laterConnection = new TrackingResource();
        var liveConnection = new TrackingResource();
        var events = new[]
        {
            new BatchEntry("failed", "ended-a", true, failedResult, true, failedConnection),
            new BatchEntry("later", "ended-b", true, laterResult, true, laterConnection),
            new BatchEntry("live", "live", false, null, false, liveConnection),
        };
        var trackedRuns = new HashSet<string>(["ended-a", "ended-b", "live"]);
        var translated = new List<string>();

        Assert.Throws<InvalidOperationException>(() => SasPairingNativeSession.TranslateAndCleanupBatch(
            events,
            evt =>
            {
                if (evt.Result is not null)
                {
                    Assert.All(events.Where(entry => entry.DisposeConnection), entry => Assert.False(entry.Connection!.IsDisposed));
                    try
                    {
                        translated.Add(evt.Name);
                        throw new InvalidOperationException("Injected result read failure.");
                    }
                    finally
                    {
                        evt.Result.Dispose();
                    }
                }

                translated.Add(evt.Name);
                return evt.Name;
            },
            evt => evt.Result,
            evt => evt.DisposeConnection,
            evt => evt.Connection,
            evt => evt.Ended,
            evt => trackedRuns.Remove(evt.RunId),
            Release));

        Assert.Equal(["failed"], translated);
        Assert.Equal(1, failedResult.DisposeCount);
        Assert.Equal(1, laterResult.DisposeCount);
        Assert.Equal(1, failedConnection.DisposeCount);
        Assert.Equal(1, laterConnection.DisposeCount);
        Assert.Equal(0, liveConnection.DisposeCount);
        Assert.Equal(["live"], trackedRuns);
    }

    /// <summary>Verifies expected native disposal failures do not stop later cleanup or ended-run forgetting.</summary>
    [Fact]
    public void TranslateAndCleanupBatch_WhenReleaseReportsExpectedFailures_ContinuesCleanup()
    {
        var failedResult = new TrackingResource { Failure = CreateCleanupException(isContractViolation: false) };
        var laterResult = new TrackingResource();
        var failedConnection = new TrackingResource { Failure = CreateCleanupException(isContractViolation: true) };
        var laterConnection = new TrackingResource();
        var liveConnection = new TrackingResource();
        var events = new[]
        {
            new BatchEntry("failed-release", "ended", true, failedResult, true, failedConnection),
            new BatchEntry("later", "ended", true, laterResult, true, laterConnection),
            new BatchEntry("live", "live", false, null, false, liveConnection),
        };
        var trackedRuns = new HashSet<string>(["ended", "live"]);

        List<string> output = SasPairingNativeSession.TranslateAndCleanupBatch(
            events,
            evt =>
            {
                if (evt.Result is not null)
                {
                    SasPairingNativeSession.ReleaseQuietly(evt.Result);
                }

                return evt.Name;
            },
            evt => evt.Result,
            evt => evt.DisposeConnection,
            evt => evt.Connection,
            evt => evt.Ended,
            evt => trackedRuns.Remove(evt.RunId),
            SasPairingNativeSession.ReleaseQuietly);

        Assert.Equal(["failed-release", "later", "live"], output);
        Assert.Equal(1, failedResult.DisposeCount);
        Assert.Equal(1, laterResult.DisposeCount);
        Assert.Equal(1, failedConnection.DisposeCount);
        Assert.Equal(1, laterConnection.DisposeCount);
        Assert.Equal(0, liveConnection.DisposeCount);
        Assert.Equal(["live"], trackedRuns);
    }

    /// <summary>Verifies a run omitted from the current event batch is still retired when its tracked object reports ended.</summary>
    [Fact]
    public void ForgetEndedRuns_RemovesOnlyEndedRunsFromAllMaps()
    {
        var endedRun = new TestRun("ended", true);
        var liveRun = new TestRun("live", false);
        var handlesByRun = new Dictionary<TestRun, string> { [endedRun] = "ended-handle", [liveRun] = "live-handle" };
        var runsByHandle = new Dictionary<string, TestRun>
        {
            ["ended-handle"] = endedRun,
            ["live-handle"] = liveRun,
        };
        var presentedIdentities = new HashSet<string>(["ended-handle", "live-handle"]);

        SasPairingNativeSession.ForgetEndedRuns(
            runsByHandle.Values,
            run => run.IsEnded,
            run =>
            {
                string handle = handlesByRun[run];
                handlesByRun.Remove(run);
                runsByHandle.Remove(handle);
                presentedIdentities.Remove(handle);
            });

        Assert.Equal(new Dictionary<TestRun, string> { [liveRun] = "live-handle" }, handlesByRun);
        Assert.Equal(new Dictionary<string, TestRun> { ["live-handle"] = liveRun }, runsByHandle);
        Assert.Equal(["live-handle"], presentedIdentities);
    }

    /// <summary>Translates one test event while verifying its connection remains alive through translation.</summary>
    /// <param name="evt">The event.</param>
    /// <param name="translated">The translation order.</param>
    /// <param name="connections">Every event connection whose lifetime is checked.</param>
    /// <returns>The event name.</returns>
    private static string Translate(
        BatchEntry evt, ICollection<string> translated, IReadOnlyCollection<TrackingResource> connections)
    {
        Assert.All(connections, connection => Assert.False(connection.IsDisposed));
        if (evt.Result is not null)
        {
            try
            {
                translated.Add(evt.Name);
                return evt.Name;
            }
            finally
            {
                evt.Result.Dispose();
            }
        }

        translated.Add(evt.Name);
        return evt.Name;
    }

    /// <summary>Releases one optional test resource.</summary>
    /// <param name="resource">The resource, if any.</param>
    private static void Release(IDisposable? resource) => resource?.Dispose();

    /// <summary>Creates one package cleanup exception through its internal constructor for disposal-path tests.</summary>
    /// <param name="isContractViolation">Whether to create a contract-violation exception.</param>
    /// <returns>The package cleanup exception.</returns>
    private static Exception CreateCleanupException(bool isContractViolation)
    {
        Type type = isContractViolation ? typeof(SasPairingContractException) : typeof(SasPairingNativeException);
        object?[] arguments = isContractViolation ? ["dispose", "injected"] : ["dispose", 1, "injected"];
        ConstructorInfo constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (Exception)constructor.Invoke(arguments);
    }

    /// <summary>One test event with its owned resources and run lifecycle.</summary>
    /// <param name="Name">The event name.</param>
    /// <param name="RunId">The tracked run ID.</param>
    /// <param name="Ended">Whether its run ended in this batch.</param>
    /// <param name="Result">The result resource.</param>
    /// <param name="DisposeConnection">Whether the connection is flagged for release.</param>
    /// <param name="Connection">The connection resource.</param>
    private sealed record BatchEntry(
        string Name,
        string RunId,
        bool Ended,
        TrackingResource? Result,
        bool DisposeConnection,
        TrackingResource? Connection);

    /// <summary>A tracked run whose ended state is checked after a drive batch.</summary>
    /// <param name="Id">The test run identity.</param>
    /// <param name="IsEnded">Whether the package has observed that the run ended.</param>
    private sealed record TestRun(string Id, bool IsEnded);

    /// <summary>Counts disposal calls for one batch resource.</summary>
    private sealed class TrackingResource : IDisposable
    {
        /// <summary>The exception to throw after recording disposal, if any.</summary>
        public Exception? Failure { get; init; }

        /// <summary>The number of times this resource was disposed.</summary>
        public int DisposeCount { get; private set; }

        /// <summary>Whether this resource has been disposed.</summary>
        public bool IsDisposed => DisposeCount > 0;

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeCount++;
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
