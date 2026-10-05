using System.Collections.Concurrent;
using System.Diagnostics;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Launches the separate sas-pairing test-peer process built beside these tests, passes its arguments
/// as structured process data, and exchanges line messages with it over standard input and output.
/// </summary>
internal sealed class TestPeerProcess : IDisposable
{
    /// <summary>The peer process.</summary>
    private readonly System.Diagnostics.Process process;

    /// <summary>The lines the peer printed, in order.</summary>
    private readonly BlockingCollection<string> lines = [];

    /// <summary>Starts the peer with the given arguments.</summary>
    /// <param name="arguments">The peer's command line, as separate arguments.</param>
    public TestPeerProcess(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        process = System.Diagnostics.Process.Start(startInfo)!;
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null)
            {
                lines.CompleteAdding();
            }
            else
            {
                lines.Add(line.Data);
            }
        };
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null && !lines.IsAddingCompleted)
            {
                lines.Add($"STDERR {line.Data}");
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// The peer executable, built into its own project output for the same configuration and target as
    /// these tests.
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            string testOutput = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string targetFramework = Path.GetFileName(testOutput);
            string configuration = Path.GetFileName(Path.GetDirectoryName(testOutput))!;
            string path = Path.Combine(
                SasPairingTestArtifacts.RepositoryRoot, "host", "DovahLink.Host.PairingCeremony.TestPeer", "bin", configuration, targetFramework,
                "DovahLink.Host.PairingCeremony.TestPeer.exe");
            Assert.True(File.Exists(path), $"The sas-pairing test peer was not built: {path}");
            return path;
        }
    }

    /// <summary>Waits for the next line the peer prints.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The line.</returns>
    public string ReadLine(TimeSpan timeout)
    {
        Assert.True(lines.TryTake(out string? line, timeout), "The test peer printed nothing within the timeout.");
        return line!;
    }

    /// <summary>Sends one line to the peer.</summary>
    /// <param name="line">The line.</param>
    public void WriteLine(string line)
    {
        process.StandardInput.WriteLine(line);
        process.StandardInput.Flush();
    }

    /// <summary>Closes the peer's standard input.</summary>
    public void CloseInput() => process.StandardInput.Close();

    /// <summary>Waits for the peer to exit.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The exit code.</returns>
    public int WaitForExit(TimeSpan timeout)
    {
        Assert.True(process.WaitForExit(timeout), "The test peer did not exit within the timeout.");
        return process.ExitCode;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }

        process.Dispose();
        lines.Dispose();
    }
}
