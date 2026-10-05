using System.Net;
using System.Net.Sockets;
using SasPairing;

namespace DovahLink.Host.PairingCeremony.TestPeer;

/// <summary>
/// Runs one real sas-pairing Initiator ceremony against the Host's loopback pairing listener through
/// the public SasPairing API only: no frame is built by hand and no cryptography is done here. The
/// harness approves or rejects the SAS over standard input after comparing displays.
/// </summary>
/// <param name="nativeLibraryPath">The absolute native library path.</param>
/// <param name="authorityScope">This peer's own authority scope, distinct from the Host's.</param>
/// <param name="local">This peer's Initiator Bootstrap.</param>
/// <param name="hostPort">The Host pairing listener's loopback port.</param>
/// <param name="output">Where status lines are written.</param>
/// <param name="input">Where the harness's decision is read.</param>
internal sealed class InitiatorPeer(
    string nativeLibraryPath, byte[] authorityScope, SasPairingBootstrap local, int hostPort, TextWriter output, TextReader input)
{
    /// <summary>The longest the whole ceremony may take.</summary>
    private static readonly TimeSpan CeremonyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Runs the ceremony and reports each milestone on <c>output</c>.</summary>
    /// <returns>The process exit code: 0 after a local result, 3 when the run ended without one, 4 on timeout.</returns>
    public int Run()
    {
        using SasPairingRuntime runtime = SasPairingRuntime.Create(nativeLibraryPath);
        using SasPairingAuthority authority = runtime.RegisterAuthority(authorityScope);
        using SasPairingHost host = authority.CreateHost();
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int ownPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        using (SasPairingWindowsListenerSocket token = SasPairingWindowsListenerSocket.FromSocket(listener))
        {
            host.AttachWindowsListener(token, local);
        }

        using var relay = new ByteRelay(ownPort, hostPort);
        DateTime deadline = DateTime.UtcNow + CeremonyTimeout;
        SasPairingRun? run = null;
        bool exposed = false;
        bool decided = false;
        bool finishEmitted = false;
        while (DateTime.UtcNow < deadline)
        {
            SasPairingDriveBatch batch = host.Drive();
            foreach (SasPairingEvent evt in batch.Events)
            {
                if (evt.Kind == SasPairingEventKind.ConnectionAccepted && run is null && evt.Connection is not null)
                {
                    run = evt.Connection.StartInitiator(local).Run;
                }

                if (evt.Result is { } result)
                {
                    SasPairingResultData data = result.Read();
                    result.Dispose();
                    output.WriteLine($"RESULT {Convert.ToHexStringLower(data.CeremonyIdentity.Bytes)} {data.PeerRole}");
                    return 0;
                }

                if (run is not null && evt.Run == run && run.IsEnded)
                {
                    output.WriteLine($"ENDED {evt.Kind}/{evt.ProtocolEvent}/{evt.Reason}");
                    return 3;
                }

                if (run is null || evt.Run != run || evt.StepKind != SasPairingStepKind.Inbound)
                {
                    continue;
                }

                if (evt.ProtocolEvent == SasPairingProtocolEvent.Accept && !exposed)
                {
                    run.AuthorizeExposure();
                    Retry(host, () => run.ExposeKey());
                    exposed = true;
                }
                else if (evt.ProtocolEvent == SasPairingProtocolEvent.ResponderKey && !decided)
                {
                    SasPairingSasPresentation presentation = run.Presentation()!;
                    output.WriteLine($"SAS {presentation.DecimalDisplay}");
                    string? decision = input.ReadLine();
                    decided = true;
                    if (decision == "APPROVE")
                    {
                        run.ApproveSas(presentation.CeremonyIdentity);
                        Retry(host, () => run.EmitBootstrapMac());
                    }
                    else
                    {
                        // Rejecting ends the run locally; no later event names it.
                        run.RejectSas(presentation.CeremonyIdentity);
                        host.Drive();
                        output.WriteLine("ENDED rejected");
                        return 3;
                    }
                }
                else if (evt.ProtocolEvent == SasPairingProtocolEvent.BootstrapMacAuthenticated && !finishEmitted)
                {
                    Retry(host, () => run.EmitInitiatorFinish());
                    finishEmitted = true;
                }
            }

            if (batch.Failure is not null)
            {
                output.WriteLine($"ERROR drive failure {batch.Failure.KnownStatus}");
                return 2;
            }
        }

        output.WriteLine("TIMEOUT");
        return 4;
    }

    /// <summary>Runs a ceremony step, driving in between while an earlier frame is still waiting to be written.</summary>
    /// <param name="host">The native host to drive.</param>
    /// <param name="step">The step.</param>
    private static void Retry(SasPairingHost host, Func<SasPairingLocalAction> step)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                step();
                return;
            }
            catch (SasPairingNativeException exception) when (exception.KnownStatus == SasPairingStatus.WritePending && attempt < 50)
            {
                host.Drive();
            }
        }
    }
}
