using System.Diagnostics;

namespace DovahLinkValidationClient.Tests;

/// <summary>Exercises failure-safe process-harness diagnostics.</summary>
public class HarnessProcessTests
{
    /// <summary>Verifies that standard error can be read while its asynchronous producer appends lines.</summary>
    [Fact]
    public async Task StandardErrorSupportsConcurrentReadsAndWrites()
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("for /L %i in (1,1,2000) do @echo diagnostic-%i 1>&2");
        using var harness = new HarnessProcess(startInfo);

        Task<bool> processExited = harness.WaitForExitAsync(TimeSpan.FromSeconds(10));
        int readCount = 0;
        while (!processExited.IsCompleted)
        {
            _ = harness.StandardError;
            readCount++;
            await Task.Yield();
        }
        Assert.True(await processExited);

        string[] lines = harness.StandardError.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(readCount > 0);
        Assert.Equal(2_000, lines.Length);
        Assert.Equal("diagnostic-1", lines[0].TrimEnd());
        Assert.Equal("diagnostic-2000", lines[^1].TrimEnd());
    }
}
