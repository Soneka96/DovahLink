using System.Text;
using System.Text.RegularExpressions;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterTrustAdminRequestHandler"/>.</summary>
public class AdapterTrustAdminRequestHandlerTests
{
    // ---- Help ----

    /// <summary>Verifies that Help forwards the trust-admin service's own help text verbatim.</summary>
    [Fact]
    public async Task HandleAsync_Help_ReturnsServiceHelpText()
    {
        var trustAdminService = new FakeTrustAdminService { HelpResult = "DovahLink commands:\n help" };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help));

        Assert.Equal("DovahLink commands:\n help", result);
    }

    // ---- List ----

    /// <summary>Verifies that an empty listing reports no devices for the requested kind.</summary>
    [Theory]
    [InlineData(TrustAdminListScope.All, "No known devices.")]
    [InlineData(TrustAdminListScope.Trust, "No trusted clients.")]
    [InlineData(TrustAdminListScope.Block, "No blocked devices.")]
    public async Task HandleAsync_List_Empty_ReportsNoDevices(TrustAdminListScope scope, string expected)
    {
        var trustAdminService = new FakeTrustAdminService { ListResult = [] };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: scope));

        Assert.Equal(expected, result);
    }

    /// <summary>Verifies that the correct wire scope is translated to the service's own scope string.</summary>
    [Theory]
    [InlineData(TrustAdminListScope.All, "known")]
    [InlineData(TrustAdminListScope.Trust, "trusted")]
    [InlineData(TrustAdminListScope.Block, "blocked")]
    public async Task HandleAsync_List_PassesExpectedScopeString(TrustAdminListScope scope, string expectedScope)
    {
        var trustAdminService = new FakeTrustAdminService();
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: scope));

        Assert.Equal(expectedScope, Assert.Single(trustAdminService.ListScopeCalls));
    }

    /// <summary>Verifies that a known-device listing formats each record's short ID, display name, and state.</summary>
    [Fact]
    public async Task HandleAsync_List_All_FormatsEachRecordWithState()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult =
            [
                BuildRecord("11111", "Alice's PC", KnownDeviceState.Trusted),
                BuildRecord("22222", null, KnownDeviceState.Blocked),
            ],
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        Assert.Equal(
            "2 known devices:\n11111  Alice's PC  trusted\n22222  (no display name)  blocked",
            result);
    }

    /// <summary>Verifies that the trusted-client listing omits the state column.</summary>
    [Fact]
    public async Task HandleAsync_List_Trust_OmitsState()
    {
        var trustAdminService = new FakeTrustAdminService { ListResult = [BuildRecord("11111", "Alice's PC", KnownDeviceState.Trusted)] };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.Trust));

        Assert.Equal("1 trusted client:\n11111  Alice's PC", result);
    }

    /// <summary>
    /// Verifies that a listing large enough to exceed the private-IPC result-text bound truncates
    /// with a trailing "... N more" note instead of producing text the codec would reject, per this
    /// handler's own bounded-result contract.
    /// </summary>
    [Fact]
    public async Task HandleAsync_List_ExceedsResultTextBudget_TruncatesWithMoreCount()
    {
        var records = Enumerable.Range(0, 200)
            .Select(index => BuildRecord($"{index:D5}", $"Device {index}", KnownDeviceState.Trusted))
            .ToList();
        var trustAdminService = new FakeTrustAdminService { ListResult = records };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        int byteLength = Encoding.UTF8.GetByteCount(result);
        Assert.True(byteLength <= Constants.MaxIpcTrustAdminResultTextBytes, $"result was {byteLength} UTF-8 bytes.");
        Assert.StartsWith("200 known devices:", result);
        Assert.Contains("00000  Device 0  trusted", result);
        Assert.DoesNotContain("00199  Device 199  trusted", result);
        Assert.Matches(@"\.\.\. \d+ more known devices\.$", result);
    }

    /// <summary>
    /// Verifies that the truncation bound is computed in UTF-8 bytes, not <see cref="string.Length"/>:
    /// a multi-byte-per-character display name must count against the budget by its true encoded
    /// size, not its character count.
    /// </summary>
    [Fact]
    public async Task HandleAsync_List_NonAsciiDisplayNames_BoundedByUtf8BytesNotCharCount()
    {
        // 60 CJK characters: 1 UTF-16 char but 3 UTF-8 bytes apiece, so a char-length-based bound
        // would (incorrectly) fit roughly 3x too many records before truncating.
        string multiByteName = new string('中', 60);
        var records = Enumerable.Range(0, 100)
            .Select(index => BuildRecord($"{index:D5}", multiByteName, KnownDeviceState.Trusted))
            .ToList();
        var trustAdminService = new FakeTrustAdminService { ListResult = records };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        int byteLength = Encoding.UTF8.GetByteCount(result);
        Assert.True(byteLength <= Constants.MaxIpcTrustAdminResultTextBytes, $"result was {byteLength} UTF-8 bytes.");
        Assert.DoesNotContain("00099", result);
        Assert.Matches(@"\.\.\. \d+ more known devices\.$", result);
    }

    /// <summary>
    /// Verifies the truncation boundary lands at the exact byte it is meant to, in both directions:
    /// the last shown record's own cumulative byte size never exceeds the threshold, and the first
    /// omitted record's cumulative byte size always would have exceeded it -- neither an
    /// off-by-one-record-early nor an off-by-one-record-late truncation. Fixed-width ASCII display
    /// names make every line's UTF-8 byte size identical and precisely computable from the test
    /// itself, so this recomputes the expected boundary independently rather than asserting only
    /// that the total stays under budget.
    /// </summary>
    [Fact]
    public async Task HandleAsync_List_TruncatesExactlyAtTheProjectedByteBoundary()
    {
        const string displayName = "AAAAAAAAAA"; // 10 ASCII bytes.
        var records = Enumerable.Range(0, 1000)
            .Select(index => BuildRecord($"{index:D5}", displayName, KnownDeviceState.Trusted))
            .ToList();
        var trustAdminService = new FakeTrustAdminService { ListResult = records };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        int shown = Regex.Matches(result, @"^\d{5}  AAAAAAAAAA  trusted$", RegexOptions.Multiline).Count;
        Assert.InRange(shown, 1, records.Count - 1); // Confirms truncation genuinely happened, mid-list.

        int headerBytes = Encoding.UTF8.GetByteCount($"{records.Count} known devices:");
        int perLineBytes = Encoding.UTF8.GetByteCount($"\n00000  {displayName}  trusted");
        int cumulativeThroughLastShown = headerBytes + (shown * perLineBytes);
        int cumulativeThroughFirstOmitted = cumulativeThroughLastShown + perLineBytes;

        // Mirrors AdapterTrustAdminRequestHandler's own private TruncationSuffixReserveBytes: not
        // exposed publicly, so this white-box boundary test keeps its own copy, matching the
        // formatter's contract rather than the formatter's implementation detail directly.
        const int truncationSuffixReserveBytes = 64;
        int threshold = Constants.MaxIpcTrustAdminResultTextBytes - truncationSuffixReserveBytes;
        Assert.True(cumulativeThroughLastShown <= threshold,
            $"the last shown record's cumulative byte size ({cumulativeThroughLastShown}) must not exceed the threshold ({threshold}).");
        Assert.True(cumulativeThroughFirstOmitted > threshold,
            $"the first omitted record's cumulative byte size ({cumulativeThroughFirstOmitted}) must exceed the threshold ({threshold}), proving it was correctly excluded rather than coincidentally.");

        int totalBytes = Encoding.UTF8.GetByteCount(result);
        Assert.True(totalBytes <= Constants.MaxIpcTrustAdminResultTextBytes, $"result was {totalBytes} UTF-8 bytes.");
    }

    /// <summary>
    /// Verifies the truncation suffix's own reserved byte budget is actually large enough for the
    /// text it produces even at an extreme omitted count -- a five-digit omitted-record count, far
    /// larger than any plausible Known Device store -- so a sufficiently large store can never make
    /// the suffix itself push the result over <see cref="Constants.MaxIpcTrustAdminResultTextBytes"/>.
    /// </summary>
    [Fact]
    public async Task HandleAsync_List_ExtremeOmittedCount_TruncationSuffixStillFitsWithinBudget()
    {
        var records = Enumerable.Range(0, 100_000)
            .Select(index => BuildRecord($"{index % 100000:D5}", "D", KnownDeviceState.Trusted))
            .ToList();
        var trustAdminService = new FakeTrustAdminService { ListResult = records };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        int totalBytes = Encoding.UTF8.GetByteCount(result);
        Assert.True(totalBytes <= Constants.MaxIpcTrustAdminResultTextBytes, $"result was {totalBytes} UTF-8 bytes.");
        Assert.Matches(@"\.\.\. \d{4,} more known devices\.$", result);
    }

    /// <summary>
    /// Verifies there is no separate maximum-record-count cap to test: the UTF-8 byte budget above
    /// already bounds the result unconditionally regardless of how many records are supplied, so a
    /// record-count cap would be redundant. Proves that claim at a size an order of magnitude past
    /// any plausible real Known Device store.
    /// </summary>
    [Fact]
    public async Task HandleAsync_List_MaximumRealisticRecordCount_StaysWithinBudgetAndReportsEveryOmission()
    {
        var records = Enumerable.Range(0, 500_000)
            .Select(index => BuildRecord($"{index % 100000:D5}", "Device", KnownDeviceState.Trusted))
            .ToList();
        var trustAdminService = new FakeTrustAdminService { ListResult = records };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: TrustAdminListScope.All));

        int totalBytes = Encoding.UTF8.GetByteCount(result);
        Assert.True(totalBytes <= Constants.MaxIpcTrustAdminResultTextBytes, $"result was {totalBytes} UTF-8 bytes.");
        Assert.Matches(@"\.\.\. \d{6} more known devices\.$", result);
    }

    // ---- Revoke ----

    /// <summary>Verifies that a successful revoke reports the device's short ID and display name.</summary>
    [Fact]
    public async Task HandleAsync_Revoke_Changed_ReportsRevoked()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult = [BuildRecord("11111", "Alice's PC", KnownDeviceState.Trusted)],
            RevokeByShortIdResult = TrustMutationOutcome.Changed,
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Revoke, ShortId: "11111"));

        Assert.Equal("Revoked client 11111 (Alice's PC).", result);
        Assert.Equal("11111", Assert.Single(trustAdminService.RevokeByShortIdCalls));
    }

    /// <summary>
    /// Verifies that a device with no display name falls back to the placeholder text in the
    /// success message -- matching the mirrored bridge precedent's own doubled-parenthesis
    /// presentation for this same case, since the placeholder text itself already carries
    /// parentheses before the surrounding "(...)" wrapper is applied.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Revoke_Changed_NoDisplayName_UsesPlaceholder()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult = [BuildRecord("11111", null, KnownDeviceState.Trusted)],
            RevokeByShortIdResult = TrustMutationOutcome.Changed,
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Revoke, ShortId: "11111"));

        Assert.Equal("Revoked client 11111 ((no display name)).", result);
    }

    /// <summary>Verifies that revoking an unrecognized short ID reports not found.</summary>
    [Fact]
    public async Task HandleAsync_Revoke_NotFound_ReportsNoTrustedClient()
    {
        var trustAdminService = new FakeTrustAdminService { RevokeByShortIdResult = TrustMutationOutcome.NotFound };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Revoke, ShortId: "99999"));

        Assert.Equal("No trusted client with id 99999.", result);
    }

    /// <summary>Verifies that revoking a device that is not currently trusted reports not eligible.</summary>
    [Fact]
    public async Task HandleAsync_Revoke_NotEligible_ReportsCannotBeRevoked()
    {
        var trustAdminService = new FakeTrustAdminService { RevokeByShortIdResult = TrustMutationOutcome.NotEligible };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Revoke, ShortId: "11111"));

        Assert.Equal("Client 11111 cannot be revoked (not currently trusted).", result);
    }

    // ---- Block ----

    /// <summary>Verifies that a successful block reports the device's short ID and display name.</summary>
    [Fact]
    public async Task HandleAsync_Block_Changed_ReportsBlocked()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult = [BuildRecord("11111", "Alice's PC", KnownDeviceState.Trusted)],
            BlockByShortIdResult = TrustMutationOutcome.Changed,
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Block, ShortId: "11111"));

        Assert.Equal("Blocked device 11111 (Alice's PC).", result);
    }

    /// <summary>Verifies that blocking an already-blocked device reports its already-in-state text.</summary>
    [Fact]
    public async Task HandleAsync_Block_AlreadyInState_ReportsAlreadyBlocked()
    {
        var trustAdminService = new FakeTrustAdminService { BlockByShortIdResult = TrustMutationOutcome.AlreadyInState };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Block, ShortId: "11111"));

        Assert.Equal("Device 11111 is already blocked.", result);
    }

    /// <summary>Verifies that blocking an unrecognized short ID reports not found.</summary>
    [Fact]
    public async Task HandleAsync_Block_NotFound_ReportsNoKnownDevice()
    {
        var trustAdminService = new FakeTrustAdminService { BlockByShortIdResult = TrustMutationOutcome.NotFound };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Block, ShortId: "99999"));

        Assert.Equal("No known device with id 99999.", result);
    }

    /// <summary>Verifies that blocking an unpaired device reports not eligible.</summary>
    [Fact]
    public async Task HandleAsync_Block_NotEligible_ReportsCannotBeBlocked()
    {
        var trustAdminService = new FakeTrustAdminService { BlockByShortIdResult = TrustMutationOutcome.NotEligible };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Block, ShortId: "11111"));

        Assert.Equal("Device 11111 cannot be blocked (not currently trusted or revoked).", result);
    }

    // ---- Unblock ----

    /// <summary>Verifies that a successful unblock reports the device's short ID and display name.</summary>
    [Fact]
    public async Task HandleAsync_Unblock_Changed_ReportsUnblocked()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult = [BuildRecord("11111", "Alice's PC", KnownDeviceState.Unpaired)],
            UnblockByShortIdResult = TrustMutationOutcome.Changed,
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Unblock, ShortId: "11111"));

        Assert.Equal("Unblocked device 11111 (Alice's PC).", result);
    }

    /// <summary>Verifies that unblocking a device that is not blocked reports its already-in-state text.</summary>
    [Fact]
    public async Task HandleAsync_Unblock_AlreadyInState_ReportsNotBlocked()
    {
        var trustAdminService = new FakeTrustAdminService { UnblockByShortIdResult = TrustMutationOutcome.AlreadyInState };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Unblock, ShortId: "11111"));

        Assert.Equal("Device 11111 is not blocked.", result);
    }

    /// <summary>Verifies that unblocking an unrecognized short ID reports not found.</summary>
    [Fact]
    public async Task HandleAsync_Unblock_NotFound_ReportsNoKnownDevice()
    {
        var trustAdminService = new FakeTrustAdminService { UnblockByShortIdResult = TrustMutationOutcome.NotFound };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Unblock, ShortId: "99999"));

        Assert.Equal("No known device with id 99999.", result);
    }

    // ---- Forget ----

    /// <summary>Verifies that a successful forget reports the device's short ID and display name.</summary>
    [Fact]
    public async Task HandleAsync_Forget_Changed_ReportsForgot()
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ListResult = [BuildRecord("11111", "Alice's PC", KnownDeviceState.Revoked)],
            ForgetByShortIdResult = TrustMutationOutcome.Changed,
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Forget, ShortId: "11111"));

        Assert.Equal("Forgot device 11111 (Alice's PC).", result);
    }

    /// <summary>Verifies that forgetting a still-trusted or blocked device reports not eligible.</summary>
    [Fact]
    public async Task HandleAsync_Forget_NotEligible_ReportsCannotBeForgotten()
    {
        var trustAdminService = new FakeTrustAdminService { ForgetByShortIdResult = TrustMutationOutcome.NotEligible };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Forget, ShortId: "11111"));

        Assert.Equal("Device 11111 cannot be forgotten (revoke or unblock it first).", result);
    }

    /// <summary>Verifies that forgetting an unrecognized short ID reports not found.</summary>
    [Fact]
    public async Task HandleAsync_Forget_NotFound_ReportsNoKnownDevice()
    {
        var trustAdminService = new FakeTrustAdminService { ForgetByShortIdResult = TrustMutationOutcome.NotFound };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Forget, ShortId: "99999"));

        Assert.Equal("No known device with id 99999.", result);
    }

    // ---- ResetTrust ----

    /// <summary>Verifies that Reset Trust reports the exact affected device count, singular and plural.</summary>
    [Theory]
    [InlineData(0, "Reset Trust complete (0 devices revoked).")]
    [InlineData(1, "Reset Trust complete (1 device revoked).")]
    [InlineData(3, "Reset Trust complete (3 devices revoked).")]
    public async Task HandleAsync_ResetTrust_ReportsAffectedCount(int affectedCount, string expected)
    {
        var trustAdminService = new FakeTrustAdminService
        {
            ResetTrustResult = Enumerable.Range(0, affectedCount).Select(_ => ClientId.NewId()).ToList(),
        };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ResetTrust));

        Assert.Equal(expected, result);
    }

    // ---- Reset (Factory Reset begin) ----

    /// <summary>Verifies that starting a Factory Reset reports the challenge code and its rounded-up remaining lifetime.</summary>
    [Fact]
    public async Task HandleAsync_Reset_Started_ReportsCodeAndTtl()
    {
        var clock = new FakeClock();
        var trustResetService = new FakeTrustResetService
        {
            BeginResetResult = new FactoryResetBeginResult(
                FactoryResetBeginOutcome.Started, new FactoryResetChallenge("048372", clock.UtcNow.AddSeconds(60))),
        };
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), trustResetService, clock);

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Reset));

        Assert.Equal("Factory Reset requested. Confirm with code 048372 within 60 seconds to permanently erase all trust.", result);
    }

    /// <summary>Verifies that starting a Factory Reset while one is already in progress reports that instead.</summary>
    [Fact]
    public async Task HandleAsync_Reset_AlreadyInProgress_ReportsAlreadyInProgress()
    {
        var trustResetService = new FakeTrustResetService
        {
            BeginResetResult = new FactoryResetBeginResult(FactoryResetBeginOutcome.AlreadyInProgress, null),
        };
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), trustResetService, new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Reset));

        Assert.Equal("A Factory Reset confirmation is already in progress.", result);
    }

    // ---- ConfirmReset ----

    /// <summary>Verifies that a correct, unexpired confirmation reports completion.</summary>
    [Fact]
    public async Task HandleAsync_ConfirmReset_Confirmed_ReportsComplete()
    {
        var trustResetService = new FakeTrustResetService { ConfirmResetResult = true };
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), trustResetService, new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ConfirmReset, ConfirmationCode: "048372"));

        Assert.Equal("Factory Reset complete.", result);
        Assert.Equal("048372", Assert.Single(trustResetService.ConfirmResetCalls));
    }

    /// <summary>Verifies that a wrong or expired confirmation reports failure without disclosing which.</summary>
    [Fact]
    public async Task HandleAsync_ConfirmReset_NotConfirmed_ReportsFailure()
    {
        var trustResetService = new FakeTrustResetService { ConfirmResetResult = false };
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), trustResetService, new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.ConfirmReset, ConfirmationCode: "000000"));

        Assert.Equal("No Factory Reset confirmation is pending, or the code was wrong. Start over with 'reset'.", result);
    }

    // ---- Unrecognized values ----

    /// <summary>Verifies that an operation value outside the closed set falls back to a safe message.</summary>
    [Fact]
    public async Task HandleAsync_UnrecognizedOperation_ReturnsFallbackText()
    {
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, (TrustAdminOperation)250));

        Assert.Equal("Unrecognized trust-admin operation.", result);
    }

    /// <summary>Verifies that a list scope value outside the closed set falls back to a safe message.</summary>
    [Fact]
    public async Task HandleAsync_List_UnrecognizedScope_ReturnsFallbackText()
    {
        var handler = new AdapterTrustAdminRequestHandler(new FakeTrustAdminService(), new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(
            new IpcTrustAdminRequestMessage(1, TrustAdminOperation.List, ListScope: (TrustAdminListScope)250));

        Assert.Equal("Unrecognized list scope.", result);
    }

    /// <summary>Verifies that an outcome Unblock's own switch does not name explicitly still falls back to its not-blocked text.</summary>
    [Fact]
    public async Task HandleAsync_Unblock_UnnamedOutcome_FallsBackToNotBlockedText()
    {
        var trustAdminService = new FakeTrustAdminService { UnblockByShortIdResult = TrustMutationOutcome.NotEligible };
        var handler = new AdapterTrustAdminRequestHandler(trustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Unblock, ShortId: "11111"));

        Assert.Equal("Device 11111 is not blocked.", result);
    }

    // ---- Redaction ----

    /// <summary>Verifies that an unexpected exception from any underlying service is redacted rather than exposed.</summary>
    [Fact]
    public async Task HandleAsync_UnderlyingServiceThrows_ReturnsRedactedErrorText()
    {
        var throwingTrustAdminService = new ThrowingTrustAdminService();
        var handler = new AdapterTrustAdminRequestHandler(throwingTrustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Help));

        Assert.Equal("An unexpected error occurred while processing the request.", result);
        Assert.DoesNotContain("Secret", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an exception from the pre-mutation display-name lookup (shared by every
    /// short-ID-targeted operation) is redacted the same way as an exception from a leaf method
    /// like <see cref="ITrustAdminService.Help"/> -- not just the mutation call itself.
    /// </summary>
    [Fact]
    public async Task HandleAsync_DisplayNameLookupThrows_ReturnsRedactedErrorText()
    {
        var throwingTrustAdminService = new ThrowingTrustAdminService();
        var handler = new AdapterTrustAdminRequestHandler(throwingTrustAdminService, new FakeTrustResetService(), new FakeClock());

        string result = await handler.HandleAsync(new IpcTrustAdminRequestMessage(1, TrustAdminOperation.Revoke, ShortId: "11111"));

        Assert.Equal("An unexpected error occurred while processing the request.", result);
        Assert.DoesNotContain("Secret", result, StringComparison.Ordinal);
    }

    /// <summary>Builds a representative trust record for a display test.</summary>
    private static TrustRecord BuildRecord(string shortId, string? displayName, KnownDeviceState state) =>
        new(ClientId.NewId(), shortId, displayName, state, CredentialVerifier: "verifier", PairedAtUtc: DateTimeOffset.UtcNow);

    /// <summary>An <see cref="ITrustAdminService"/> whose every member throws, carrying a secret that must never reach the caller.</summary>
    private sealed class ThrowingTrustAdminService : ITrustAdminService
    {
        public IReadOnlyList<TrustRecord> List(string scope = "known") => throw new InvalidOperationException("Secret: DPAPI failure at C:\\Users\\redacted\\trust-store.dat");
        public string Help() => throw new InvalidOperationException("Secret: DPAPI failure at C:\\Users\\redacted\\trust-store.dat");
        public Task RenameAsync(ClientId clientId, string displayName, KnownDeviceIncarnationId expectedIncarnation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public KnownDeviceIncarnationId? TryCaptureTrustedIncarnation(ClientId clientId) => throw new NotSupportedException();
        public Task RevokeAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task BlockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnblockAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ForgetAsync(ClientId clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ClientId>> ResetTrustAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TrustMutationOutcome> RevokeByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TrustMutationOutcome> BlockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TrustMutationOutcome> UnblockByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TrustMutationOutcome> ForgetByShortIdAsync(string shortId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
