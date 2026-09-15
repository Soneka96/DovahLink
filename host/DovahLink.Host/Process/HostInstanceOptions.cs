namespace DovahLink.Host.Process;

/// <summary>
/// The current Host process's identity within its owning Skyrim lifetime -- the one composition-root
/// runtime value every collaborator that needs to name, scope, or verify against that lifetime
/// depends on, rather than each taking a raw <see cref="OwnerLifetimeId"/> parameter of its own.
/// </summary>
/// <param name="OwnerLifetimeId">The owning Skyrim process's lifetime identity.</param>
public sealed record HostInstanceOptions(OwnerLifetimeId OwnerLifetimeId);
