using System.Text.Json;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>One decoded <c>state_snapshot</c> a <see cref="LivePipelineClient"/> received.</summary>
/// <param name="StateArea">The state area the snapshot belongs to.</param>
/// <param name="Revision">The snapshot's revision.</param>
/// <param name="Data">The snapshot's <c>data</c> object.</param>
/// <param name="StateAuthorityId">The envelope's state-authority id.</param>
/// <param name="PlayContextId">The envelope's play-context id.</param>
public sealed record ReceivedSnapshot(
    string StateArea,
    ulong Revision,
    JsonElement Data,
    string? StateAuthorityId,
    string? PlayContextId);
