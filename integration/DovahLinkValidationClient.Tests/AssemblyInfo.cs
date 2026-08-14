using Xunit;

// Scenario harnesses share a fixed loopback port, so the suite runs serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
