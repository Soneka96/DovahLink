namespace DovahLink.Host.Security;

/// <summary>
/// The single, explicit synchronization primitive a client's trust-mutation publication and its
/// session authorization check share, so a trust record changing and the sessions that change
/// affects becoming unauthorized are always observed by every other caller as one indivisible
/// event -- never a moment where one has happened but not the other. Reentrant on the thread that
/// already holds it, so a session-authorized action that itself reads current trust state through
/// the same gate does not deadlock against its own outer acquisition.
/// </summary>
public interface ISecurityStateGate
{
    /// <summary>
    /// Enters the shared critical section, blocking the calling thread until no other thread holds
    /// it. A thread that already holds it may call this again without blocking; each such call must
    /// be matched by its own <see cref="Exit"/>.
    /// </summary>
    void Enter();

    /// <summary>
    /// Exits one level of the critical section previously entered through <see cref="Enter"/>.
    /// </summary>
    /// <exception cref="SynchronizationLockException">
    /// The calling thread does not currently hold the gate -- either <see cref="Enter"/> was never
    /// called on this thread, or every level it entered has already been exited.
    /// </exception>
    void Exit();
}

/// <inheritdoc cref="ISecurityStateGate"/>
public sealed class SecurityStateGate : ISecurityStateGate
{
    /// <summary>The monitor backing this gate's mutual exclusion and per-thread reentrancy.</summary>
    private readonly object monitor = new();

    /// <inheritdoc/>
    public void Enter() => Monitor.Enter(monitor);

    /// <inheritdoc/>
    public void Exit() => Monitor.Exit(monitor);
}
