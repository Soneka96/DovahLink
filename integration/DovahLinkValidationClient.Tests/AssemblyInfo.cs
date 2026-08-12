using Xunit;

// Every scenario test launches its own bridge harness bound to the same
// fixed, documented port (bridge/README.md's "Default loopback port" --
// there is no configurable port in Phase 1). xUnit parallelizes across test
// classes by default; two harnesses racing for the same port means one
// fails to bind and exits immediately with no output, which is exactly what
// broke NegotiationScenarioTests when CapabilitiesScenarioTests was added.
// Every current and future scenario file needs this, so it's disabled for
// the whole assembly rather than per test class.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
