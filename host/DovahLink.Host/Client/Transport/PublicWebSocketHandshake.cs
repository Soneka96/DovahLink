using System.Security.Cryptography;
using System.Text;

namespace DovahLink.Host.Client.Transport;

/// <summary>
/// Pure parsing and response-building logic for the raw HTTP/1.1 WebSocket upgrade handshake (RFC
/// 6455 section 4), kept independent of socket I/O so it can be tested directly against byte input.
/// <see cref="PublicWebSocketConnection"/> owns reading the request bytes and writing the response
/// this type builds.
/// </summary>
internal static class PublicWebSocketHandshake
{
    /// <summary>The fixed GUID RFC 6455 defines for computing <c>Sec-WebSocket-Accept</c>.</summary>
    private const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>The exact decoded length RFC 6455 requires of <c>Sec-WebSocket-Key</c>.</summary>
    private const int RequiredKeyBytes = 16;

    /// <summary>
    /// The handshake headers this parser requires to appear at most once. A repeated occurrence of
    /// any of these could otherwise let an earlier or later duplicate silently change which value
    /// validation actually saw; <c>Connection</c> is deliberately excluded here because RFC 7230
    /// gives it token-list semantics, so repeated <c>Connection</c> lines are combined instead of
    /// rejected. <c>Origin</c> is included so a duplicate occurrence cannot be used to probe around
    /// the origin policy below -- it is rejected as malformed the same as any other singleton-header
    /// duplicate, never treated as a distinct origin-policy outcome.
    /// </summary>
    private static readonly HashSet<string> SingletonHeaderNames =
        new(StringComparer.OrdinalIgnoreCase) { "Host", "Upgrade", "Sec-WebSocket-Version", "Sec-WebSocket-Key", "Origin" };

    /// <summary>
    /// Determines whether <paramref name="requestBytes"/> is a well-formed, policy-admitted WebSocket
    /// upgrade request -- a <c>GET &lt;non-empty target&gt; HTTP/1.1</c> request line, a required
    /// <c>Host</c> header, an <c>Upgrade: websocket</c> header, a <c>Connection</c> header containing
    /// the <c>upgrade</c> token, <c>Sec-WebSocket-Version: 13</c>, a <c>Sec-WebSocket-Key</c> that
    /// decodes as Base64 to exactly <see cref="RequiredKeyBytes"/> bytes, and no <c>Origin</c> header
    /// -- and computes the matching <c>Sec-WebSocket-Accept</c> value. The public endpoint serves
    /// native DovahLink clients only: an <c>Origin</c> header only ever appears on a browser-originated
    /// request, so its mere presence is rejected regardless of value, with no allowlist.
    /// </summary>
    /// <param name="requestBytes">
    /// The complete request line and headers, including the terminating blank line, as received from
    /// the peer. Header names are matched case-insensitively per RFC 7230.
    /// </param>
    /// <param name="acceptKey">The computed <c>Sec-WebSocket-Accept</c> value on success; otherwise empty.</param>
    /// <returns><see cref="HandshakeRejectReason.None"/> when the request is accepted; otherwise the reason it was rejected.</returns>
    /// <remarks>
    /// This parser never writes an HTTP error response for a rejected request: the caller's
    /// documented behavior for a non-<see cref="HandshakeRejectReason.None"/> result is to close the
    /// connection silently, the same way it treats a peer that never completes its handshake at all.
    /// </remarks>
    internal static HandshakeRejectReason TryParseUpgradeRequest(ReadOnlySpan<byte> requestBytes, out string acceptKey)
    {
        acceptKey = string.Empty;

        string request = Encoding.ASCII.GetString(requestBytes);
        string[] lines = request.Split("\r\n");
        if (lines.Length < 2 || !IsValidRequestLine(lines[0]))
        {
            return HandshakeRejectReason.Malformed;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length == 0)
            {
                break;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                return HandshakeRejectReason.Malformed;
            }

            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            if (SingletonHeaderNames.Contains(name))
            {
                if (!headers.TryAdd(name, value))
                {
                    return HandshakeRejectReason.Malformed;
                }
            }
            else if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase) && headers.TryGetValue(name, out string? existingConnection))
            {
                headers[name] = existingConnection + "," + value;
            }
            else
            {
                headers[name] = value;
            }
        }

        if (!headers.TryGetValue("Host", out string? host) || host.Length == 0)
        {
            return HandshakeRejectReason.Malformed;
        }

        if (!headers.TryGetValue("Upgrade", out string? upgrade) ||
            !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            return HandshakeRejectReason.Malformed;
        }

        if (!headers.TryGetValue("Connection", out string? connection) ||
            !connection.Split(',').Any(token => token.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)))
        {
            return HandshakeRejectReason.Malformed;
        }

        if (!headers.TryGetValue("Sec-WebSocket-Version", out string? version))
        {
            return HandshakeRejectReason.Malformed;
        }

        if (version != "13")
        {
            return HandshakeRejectReason.UnsupportedVersion;
        }

        Span<byte> keyBytes = stackalloc byte[RequiredKeyBytes];
        if (!headers.TryGetValue("Sec-WebSocket-Key", out string? key) ||
            !Convert.TryFromBase64String(key, keyBytes, out int keyByteCount) ||
            keyByteCount != RequiredKeyBytes)
        {
            return HandshakeRejectReason.Malformed;
        }

        if (headers.ContainsKey("Origin"))
        {
            return HandshakeRejectReason.DisallowedOrigin;
        }

        acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketAcceptGuid)));
        return HandshakeRejectReason.None;
    }

    /// <summary>Builds the raw bytes of a <c>101 Switching Protocols</c> response accepting the upgrade.</summary>
    /// <param name="acceptKey">The <c>Sec-WebSocket-Accept</c> value from <see cref="TryParseUpgradeRequest"/>.</param>
    internal static byte[] BuildSwitchingProtocolsResponse(string acceptKey) =>
        Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");

    /// <summary>
    /// Builds the raw bytes of a minimal <c>400 Bad Request</c> response for a malformed or
    /// policy-rejected upgrade request. Carries no body and no detail about what was rejected --
    /// the caller's local diagnostic, not this wire response, records the specific reason.
    /// </summary>
    internal static byte[] BuildBadRequestResponse() =>
        Encoding.ASCII.GetBytes(
            "HTTP/1.1 400 Bad Request\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n\r\n");

    /// <summary>
    /// Builds the raw bytes of a minimal <c>426 Upgrade Required</c> response for a request whose
    /// <c>Sec-WebSocket-Version</c> this transport does not support, advertising the one version it
    /// accepts.
    /// </summary>
    internal static byte[] BuildUpgradeRequiredResponse() =>
        Encoding.ASCII.GetBytes(
            "HTTP/1.1 426 Upgrade Required\r\n" +
            "Connection: close\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Content-Length: 0\r\n\r\n");

    /// <summary>Whether an HTTP request line is a well-formed <c>GET &lt;non-empty target&gt; HTTP/1.1</c> line.</summary>
    /// <param name="requestLine">The first line of the request, without its terminating <c>\r\n</c>.</param>
    private static bool IsValidRequestLine(string requestLine)
    {
        string[] parts = requestLine.Split(' ');
        return parts.Length == 3 && parts[0] == "GET" && parts[1].Length > 0 && parts[2] == "HTTP/1.1";
    }
}
