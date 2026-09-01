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
    /// Determines whether <paramref name="requestBytes"/> is a well-formed WebSocket upgrade request
    /// -- a <c>GET ... HTTP/1.1</c> request line, an <c>Upgrade: websocket</c> header, a
    /// <c>Connection</c> header containing the <c>upgrade</c> token, <c>Sec-WebSocket-Version: 13</c>,
    /// and a <c>Sec-WebSocket-Key</c> that decodes as Base64 to exactly <see cref="RequiredKeyBytes"/>
    /// bytes -- and computes the matching <c>Sec-WebSocket-Accept</c> value.
    /// </summary>
    /// <param name="requestBytes">
    /// The complete request line and headers, including the terminating blank line, as received from
    /// the peer. Header names are matched case-insensitively per RFC 7230.
    /// </param>
    /// <param name="acceptKey">The computed <c>Sec-WebSocket-Accept</c> value on success; otherwise empty.</param>
    /// <returns><see langword="true"/> when the request is a valid WebSocket upgrade request.</returns>
    internal static bool TryParseUpgradeRequest(ReadOnlySpan<byte> requestBytes, out string acceptKey)
    {
        acceptKey = string.Empty;

        string request = Encoding.ASCII.GetString(requestBytes);
        string[] lines = request.Split("\r\n");
        if (lines.Length < 2 || !IsValidRequestLine(lines[0]))
        {
            return false;
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
                return false;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        if (!headers.TryGetValue("Upgrade", out string? upgrade) ||
            !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!headers.TryGetValue("Connection", out string? connection) ||
            !connection.Split(',').Any(token => token.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!headers.TryGetValue("Sec-WebSocket-Version", out string? version) || version != "13")
        {
            return false;
        }

        Span<byte> keyBytes = stackalloc byte[RequiredKeyBytes];
        if (!headers.TryGetValue("Sec-WebSocket-Key", out string? key) ||
            !Convert.TryFromBase64String(key, keyBytes, out int keyByteCount) ||
            keyByteCount != RequiredKeyBytes)
        {
            return false;
        }

        acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketAcceptGuid)));
        return true;
    }

    /// <summary>Builds the raw bytes of a <c>101 Switching Protocols</c> response accepting the upgrade.</summary>
    /// <param name="acceptKey">The <c>Sec-WebSocket-Accept</c> value from <see cref="TryParseUpgradeRequest"/>.</param>
    internal static byte[] BuildSwitchingProtocolsResponse(string acceptKey) =>
        Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");

    /// <summary>Whether an HTTP request line is a well-formed <c>GET ... HTTP/1.1</c> line.</summary>
    /// <param name="requestLine">The first line of the request, without its terminating <c>\r\n</c>.</param>
    private static bool IsValidRequestLine(string requestLine)
    {
        string[] parts = requestLine.Split(' ');
        return parts.Length == 3 && parts[0] == "GET" && parts[2] == "HTTP/1.1";
    }
}
