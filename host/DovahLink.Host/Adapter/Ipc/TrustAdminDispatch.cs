namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// One admitted trust-admin request's cancellation handle and its own dispatch task, tracked
/// together so <see cref="AdapterIpcConnection"/>'s teardown can both cancel every still-admitted
/// request and await its dispatch actually finishing, rather than only requesting cancellation and
/// hoping it lands before the connection is gone.
/// </summary>
/// <param name="Cancellation">Cancelled to end this request's dispatch early.</param>
/// <param name="Completion">
/// The task this request's own dispatch runs as. Never faults: its body contains every exception at
/// the source, so awaiting this task can only ever complete successfully, never throw.
/// </param>
internal sealed record TrustAdminDispatch(CancellationTokenSource Cancellation, Task Completion);
