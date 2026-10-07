namespace DovahLink.Host.State;

/// <summary>
/// The atomic outcome of one <see cref="IAuthoritativeStateStore.Apply{TState}"/>,
/// <see cref="IAuthoritativeStateStore.ApplyResynchronizationBaseline{TState}"/>, or
/// <see cref="IAuthoritativeStateStore.ApplyEvent{TState}"/> call, computed inside the same lock that
/// decides acceptance, assigns the revision, and commits the area's replay Snapshot -- so a caller
/// deciding what to do next never needs to re-read the revision or the replay state after the fact,
/// which could otherwise race a concurrent capture for the same area.
/// </summary>
/// <param name="Accepted">
/// Whether the value was accepted from the current adapter and captured play context. An accepted
/// value is already committed as the area's current replay state.
/// </param>
/// <param name="Changed">
/// Whether the accepted value actually changed the state area's authoritative value, and therefore
/// advanced <see cref="Revision"/> past <see cref="BaseRevision"/>. Always <see langword="false"/>
/// when <see cref="Accepted"/> is <see langword="false"/>.
/// </param>
/// <param name="BaseRevision">The state area's revision immediately before this call, or <see cref="RevisionNumber.Initial"/> when rejected.</param>
/// <param name="Revision">The state area's revision immediately after this call: equal to <see cref="BaseRevision"/> unless <see cref="Changed"/> is <see langword="true"/>, or <see cref="RevisionNumber.Initial"/> when rejected.</param>
public readonly record struct StateApplyResult(bool Accepted, bool Changed, RevisionNumber BaseRevision, RevisionNumber Revision)
{
    /// <summary>The result for a call rejected before any revision could be read.</summary>
    public static StateApplyResult Rejected { get; } = new(Accepted: false, Changed: false, RevisionNumber.Initial, RevisionNumber.Initial);
}
