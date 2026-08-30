namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The result of an <see cref="IAdapterIpcSession"/> decision: zero or more messages for the
/// connection to send the peer, and whether the connection must close afterward.
/// </summary>
/// <param name="MessagesToSend">The messages to write to the peer, in order, before any close.</param>
/// <param name="ShouldClose">Whether the connection must close after sending <see cref="MessagesToSend"/>.</param>
public sealed record AdapterIpcOutcome(IReadOnlyList<IpcMessage> MessagesToSend, bool ShouldClose)
{
    /// <summary>An outcome that sends nothing and keeps the connection open.</summary>
    public static readonly AdapterIpcOutcome None = new([], ShouldClose: false);

    /// <summary>An outcome that sends nothing and closes the connection.</summary>
    public static readonly AdapterIpcOutcome Close = new([], ShouldClose: true);

    /// <summary>Creates an outcome that sends one message and keeps the connection open.</summary>
    /// <param name="message">The message to send.</param>
    public static AdapterIpcOutcome Send(IpcMessage message) => new([message], ShouldClose: false);

    /// <summary>Creates an outcome that sends one message and then closes the connection.</summary>
    /// <param name="message">The message to send before closing.</param>
    public static AdapterIpcOutcome SendAndClose(IpcMessage message) => new([message], ShouldClose: true);
}
