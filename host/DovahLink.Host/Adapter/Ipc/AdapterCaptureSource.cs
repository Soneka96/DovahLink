using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Identifies the exact adapter connection that received one capture result. Passed in by the
/// receiving <see cref="AdapterIpcSession"/>, which already knows its own identity, rather than
/// rediscovered inside <see cref="LiveCaptureSink"/> from mutable global availability state that may
/// have since moved on to a newer connection.
/// </summary>
/// <param name="InstanceId">The adapter instance this capture result was received from.</param>
/// <param name="ConnectionGeneration">The connection generation the capture result was received on.</param>
public readonly record struct AdapterCaptureSource(AdapterInstanceId InstanceId, long ConnectionGeneration);
