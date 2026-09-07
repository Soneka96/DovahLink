namespace DovahLink.Host.Tests;

/// <summary>
/// Groups every xUnit test class carrying a real-wall-clock assertion sensitive to scheduler
/// contention from concurrently-running real-socket or real-process work, so xUnit never
/// schedules them to run in parallel with each other -- every other test class in this assembly
/// still runs in parallel as usual, per xUnit's own default collection parallelism.
/// <see cref="Client.Transport.PublicWebSocketConnectionTests"/>'s fragment-assembly-deadline test
/// asserts a real <see cref="System.Threading.CancellationTokenSource"/> timer fires within a
/// narrow real-wall-clock window; <see cref="ProgramCompositionTests"/>'s
/// shutdown-racing-admission stress test repeatedly composes a real Host instance and races real
/// socket connects against its own shutdown. Running either concurrently with the other's real
/// socket/process work risks delaying the timer-sensitive assertion past its own margin under a
/// loaded scheduler, without either test's own logic being at fault.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RealSocketAndProcessTestCollection
{
    /// <summary>The collection name every member test class references via <see cref="Xunit.CollectionAttribute"/>.</summary>
    public const string Name = "RealSocketAndProcessTests";
}
