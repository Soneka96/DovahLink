namespace DovahLink.Host.Client.Transport;

/// <summary>
/// Host-local observability for one public WebSocket connection's abnormal or security-relevant
/// termination. A later concept supplies the real implementation (a logging/telemetry sink); this
/// concept owns only the reporting contract and the point at which each report is emitted.
/// </summary>
public interface IPublicWebSocketTransportDiagnostics
{
    /// <summary>
    /// Reports the single authoritative root-cause reason one connection is ending abnormally. Never
    /// called for normal lifecycle events -- a normal peer close, host shutdown, external
    /// cancellation, or a caller-requested <see cref="IPublicWebSocketConnection.RequestClose"/> --
    /// since those are not security or abnormal events. Called at most once per connection instance:
    /// the connection itself owns exactly-once root-cause attribution, so an implementation does not
    /// need to defend against duplicate or conflicting reports for the same connection. Never passed
    /// raw payload bytes, credentials, pairing codes, filesystem paths, or exception details -- the
    /// structured reason alone is the entire report. An implementation must not block or throw: this
    /// is called on the connection's own read/write path during teardown.
    /// </summary>
    /// <param name="reason">The structured, non-sensitive reason enforcement occurred.</param>
    void ReportAbnormalEnd(PublicWebSocketConnectionEndReason reason);
}
