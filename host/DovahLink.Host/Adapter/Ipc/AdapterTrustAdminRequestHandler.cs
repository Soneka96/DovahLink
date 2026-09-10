using System.Text;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Maps one adapter-originated trust-administration request to <see cref="ITrustAdminService"/>/
/// <see cref="ITrustResetService"/> and formats its typed result into the bounded, display-ready
/// text a Skyrim-facing console surface shows verbatim. Owns no trust logic of its own: every
/// operation forwards to the same authoritative services the public administration surface uses,
/// and every result is redacted before it reaches this seam's caller.
/// </summary>
public interface IAdapterTrustAdminRequestHandler
{
    /// <summary>
    /// Handles one trust-administration request and returns its formatted result text. Propagates
    /// an <see cref="OperationCanceledException"/> from <paramref name="cancellationToken"/> firing
    /// rather than formatting it into a result: the caller uses that to distinguish a cancelled
    /// request (which should be dropped silently) from a genuinely errored one. An implementation
    /// must honor <paramref name="cancellationToken"/> promptly -- propagating it into every
    /// downstream async call rather than only checking it once at entry -- since the owning
    /// connection's teardown cancels this token and then waits only a bounded time for this call to
    /// actually return before abandoning the wait and completing teardown anyway; an implementation
    /// that ignores the token can keep running past that point with no further way for the host to
    /// observe or bound it.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence writes.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the request finished.</exception>
    Task<string> HandleAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdapterTrustAdminRequestHandler"/>
public sealed class AdapterTrustAdminRequestHandler : IAdapterTrustAdminRequestHandler
{
    /// <summary>The reusable Known Device administration authority this handler forwards to.</summary>
    private readonly ITrustAdminService trustAdminService;

    /// <summary>The Factory Reset authority this handler forwards to.</summary>
    private readonly ITrustResetService trustResetService;

    /// <summary>The time source used to compute a Factory Reset challenge's remaining lifetime.</summary>
    private readonly IClock clock;

    /// <summary>Creates a trust-admin request handler.</summary>
    /// <param name="trustAdminService">The reusable Known Device administration authority to forward to.</param>
    /// <param name="trustResetService">The Factory Reset authority to forward to.</param>
    /// <param name="clock">The time source used to compute a Factory Reset challenge's remaining lifetime.</param>
    public AdapterTrustAdminRequestHandler(ITrustAdminService trustAdminService, ITrustResetService trustResetService, IClock clock)
    {
        this.trustAdminService = trustAdminService;
        this.trustResetService = trustResetService;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public async Task<string> HandleAsync(IpcTrustAdminRequestMessage request, CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Operation switch
            {
                TrustAdminOperation.Help => trustAdminService.Help(),
                TrustAdminOperation.List => FormatList(request.ListScope!.Value),
                TrustAdminOperation.Revoke => await RevokeAsync(request.ShortId!, cancellationToken),
                TrustAdminOperation.Block => await BlockAsync(request.ShortId!, cancellationToken),
                TrustAdminOperation.Unblock => await UnblockAsync(request.ShortId!, cancellationToken),
                TrustAdminOperation.Forget => await ForgetAsync(request.ShortId!, cancellationToken),
                TrustAdminOperation.ResetTrust => await ResetTrustAsync(cancellationToken),
                TrustAdminOperation.Reset => StartFactoryReset(),
                TrustAdminOperation.ConfirmReset => await ConfirmFactoryResetAsync(request.ConfirmationCode!, cancellationToken),
                _ => "Unrecognized trust-admin operation.",
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never expose a raw persistence or infrastructure exception to the Skyrim-facing
            // console surface, per this concept's "without exposing credentials or persistence
            // exceptions" contract. A genuine cancellation is deliberately let through instead of
            // being formatted here: the caller distinguishes a cancelled request (drop silently,
            // no reply) from an errored one (reply with the message above), and collapsing both
            // into this same formatted string would make that distinction impossible to observe.
            return "An unexpected error occurred while processing the request.";
        }
    }

    /// <summary>Formats the known-device listing for the requested scope.</summary>
    private string FormatList(TrustAdminListScope scope) => scope switch
    {
        TrustAdminListScope.All => FormatKnownDeviceListing(trustAdminService.List("known"), "known device", includeState: true),
        TrustAdminListScope.Trust => FormatKnownDeviceListing(trustAdminService.List("trusted"), "trusted client", includeState: false),
        TrustAdminListScope.Block => FormatKnownDeviceListing(trustAdminService.List("blocked"), "blocked device", includeState: true),
        _ => "Unrecognized list scope.",
    };

    /// <summary>
    /// The UTF-8 byte budget reserved for a truncation suffix ("... N more Xs."). Comfortably larger
    /// than any suffix this handler actually produces, so <see cref="FormatKnownDeviceListing"/> never
    /// needs to compute the suffix's exact size while deciding whether one more record still fits.
    /// </summary>
    private const int TruncationSuffixReserveBytes = 64;

    /// <summary>
    /// Formats an already-scoped, already-deduplicated device listing, truncating (with a trailing
    /// "... N more" note) before the result would exceed <see cref="Constants.MaxIpcTrustAdminResultTextBytes"/>
    /// UTF-8 bytes, so a sufficiently large known-device store can never turn a valid list request into
    /// an encoding failure that tears down the private IPC connection instead of returning a controlled,
    /// bounded result.
    /// </summary>
    /// <param name="records">The records to format, in display order.</param>
    /// <param name="deviceKind">The singular noun describing one listed record.</param>
    /// <param name="includeState">Whether each line includes the record's trust state.</param>
    private static string FormatKnownDeviceListing(IReadOnlyList<TrustRecord> records, string deviceKind, bool includeState)
    {
        if (records.Count == 0)
        {
            return $"No {deviceKind}s.";
        }

        var builder = new StringBuilder();
        builder.Append(records.Count).Append(' ').Append(deviceKind).Append(records.Count == 1 ? "" : "s").Append(':');

        int shown = 0;
        foreach (TrustRecord record in records)
        {
            string displayName = string.IsNullOrEmpty(record.DisplayName) ? "(no display name)" : record.DisplayName;
            var line = new StringBuilder().Append('\n').Append(record.ShortId).Append("  ").Append(displayName);
            if (includeState)
            {
                line.Append("  ").Append(StateLabel(record.State));
            }

            string lineText = line.ToString();
            int projectedBytes = Encoding.UTF8.GetByteCount(builder.ToString()) + Encoding.UTF8.GetByteCount(lineText);
            if (projectedBytes > Constants.MaxIpcTrustAdminResultTextBytes - TruncationSuffixReserveBytes)
            {
                break;
            }

            builder.Append(lineText);
            shown++;
        }

        int omitted = records.Count - shown;
        if (omitted > 0)
        {
            builder.Append('\n').Append("... ").Append(omitted).Append(" more ").Append(deviceKind).Append(omitted == 1 ? "" : "s").Append('.');
        }

        return builder.ToString();
    }

    /// <summary>The stable, lower-case presentation label for a known-device state.</summary>
    private static string StateLabel(KnownDeviceState state) => state switch
    {
        KnownDeviceState.Trusted => "trusted",
        KnownDeviceState.Revoked => "revoked",
        KnownDeviceState.Blocked => "blocked",
        KnownDeviceState.Unpaired => "unpaired",
        _ => "unknown",
    };

    /// <summary>Captures a known device's display name before a mutation that may remove its record.</summary>
    /// <param name="shortId">The short ID to look up.</param>
    private string CaptureDisplayName(string shortId) =>
        trustAdminService.List("known").FirstOrDefault(record => record.ShortId == shortId) is { DisplayName: { Length: > 0 } displayName }
            ? displayName
            : "(no display name)";

    /// <summary>Revokes a trusted device by short ID and formats the outcome.</summary>
    private async Task<string> RevokeAsync(string shortId, CancellationToken cancellationToken)
    {
        string displayName = CaptureDisplayName(shortId);
        TrustMutationOutcome outcome = await trustAdminService.RevokeByShortIdAsync(shortId, cancellationToken);
        return outcome switch
        {
            TrustMutationOutcome.Changed => $"Revoked client {shortId} ({displayName}).",
            TrustMutationOutcome.NotFound => $"No trusted client with id {shortId}.",
            _ => $"Client {shortId} cannot be revoked (not currently trusted).",
        };
    }

    /// <summary>Blocks a known device by short ID and formats the outcome.</summary>
    private async Task<string> BlockAsync(string shortId, CancellationToken cancellationToken)
    {
        string displayName = CaptureDisplayName(shortId);
        TrustMutationOutcome outcome = await trustAdminService.BlockByShortIdAsync(shortId, cancellationToken);
        return outcome switch
        {
            TrustMutationOutcome.Changed => $"Blocked device {shortId} ({displayName}).",
            TrustMutationOutcome.AlreadyInState => $"Device {shortId} is already blocked.",
            TrustMutationOutcome.NotFound => $"No known device with id {shortId}.",
            _ => $"Device {shortId} cannot be blocked (not currently trusted or revoked).",
        };
    }

    /// <summary>Unblocks a known device by short ID and formats the outcome.</summary>
    private async Task<string> UnblockAsync(string shortId, CancellationToken cancellationToken)
    {
        string displayName = CaptureDisplayName(shortId);
        TrustMutationOutcome outcome = await trustAdminService.UnblockByShortIdAsync(shortId, cancellationToken);
        return outcome switch
        {
            TrustMutationOutcome.Changed => $"Unblocked device {shortId} ({displayName}).",
            TrustMutationOutcome.NotFound => $"No known device with id {shortId}.",
            _ => $"Device {shortId} is not blocked.",
        };
    }

    /// <summary>Forgets an eligible known device by short ID and formats the outcome.</summary>
    private async Task<string> ForgetAsync(string shortId, CancellationToken cancellationToken)
    {
        string displayName = CaptureDisplayName(shortId);
        TrustMutationOutcome outcome = await trustAdminService.ForgetByShortIdAsync(shortId, cancellationToken);
        return outcome switch
        {
            TrustMutationOutcome.Changed => $"Forgot device {shortId} ({displayName}).",
            TrustMutationOutcome.NotFound => $"No known device with id {shortId}.",
            _ => $"Device {shortId} cannot be forgotten (revoke or unblock it first).",
        };
    }

    /// <summary>Applies Reset Trust to every trusted device and formats the affected count.</summary>
    private async Task<string> ResetTrustAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientId> affected = await trustAdminService.ResetTrustAsync(cancellationToken);
        return $"Reset Trust complete ({affected.Count} device{(affected.Count == 1 ? "" : "s")} revoked).";
    }

    /// <summary>Starts a Factory Reset confirmation challenge and formats its code and remaining lifetime.</summary>
    private string StartFactoryReset()
    {
        FactoryResetBeginResult result = trustResetService.BeginReset();
        if (result.Outcome == FactoryResetBeginOutcome.AlreadyInProgress || result.Challenge is null)
        {
            return "A Factory Reset confirmation is already in progress.";
        }

        int ttlSeconds = Math.Max(0, (int)Math.Ceiling((result.Challenge.ExpiresAtUtc - clock.UtcNow).TotalSeconds));
        return $"Factory Reset requested. Confirm with code {result.Challenge.Code} within {ttlSeconds} seconds to permanently erase all trust.";
    }

    /// <summary>Confirms a pending Factory Reset challenge and formats the outcome.</summary>
    private async Task<string> ConfirmFactoryResetAsync(string confirmationCode, CancellationToken cancellationToken)
    {
        bool confirmed = await trustResetService.ConfirmResetAsync(confirmationCode, cancellationToken);
        return confirmed
            ? "Factory Reset complete."
            : "No Factory Reset confirmation is pending, or the code was wrong. Start over with 'reset'.";
    }
}
