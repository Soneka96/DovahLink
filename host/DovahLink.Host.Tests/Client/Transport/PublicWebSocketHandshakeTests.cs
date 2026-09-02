using System.Text;
using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicWebSocketHandshake"/>.</summary>
public class PublicWebSocketHandshakeTests
{
    /// <summary>Verifies the RFC 6455 section 1.3 example key against its documented accept value.</summary>
    [Fact]
    public void TryParseUpgradeRequest_Rfc6455ExampleKey_ComputesTheDocumentedAcceptValue()
    {
        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(
            BuildRequestBytes(key: "dGhlIHNhbXBsZSBub25jZQ=="), out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that header names are matched case-insensitively.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MixedCaseHeaderNamesAndValues_IsAccepted()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "host: 127.0.0.1\r\n" +
            "upgrade: WebSocket\r\n" +
            "CONNECTION: keep-alive, Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that a non-GET request line is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_NonGetMethod_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "POST / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a non-HTTP/1.1 request line is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_WrongHttpVersion_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.0\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a missing Upgrade header is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MissingUpgradeHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that an Upgrade header with a value other than "websocket" is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_WrongUpgradeValue_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: h2c\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a missing Connection header is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MissingConnectionHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a Connection header without the "upgrade" token is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_ConnectionHeaderWithoutUpgradeToken_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: keep-alive\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>
    /// Verifies that a missing Sec-WebSocket-Version header is rejected as generically Malformed,
    /// distinct from a present-but-unsupported value -- there is no version to call "unsupported"
    /// when none was offered at all.
    /// </summary>
    [Fact]
    public void TryParseUpgradeRequest_MissingVersionHeader_IsRejectedAsMalformed()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>
    /// Verifies that a Sec-WebSocket-Version header present with an unsupported value is rejected
    /// with the distinct UnsupportedVersion reason, not the generic Malformed reason -- this is the
    /// case that maps to an RFC 6455 426 response advertising the supported version.
    /// </summary>
    [Fact]
    public void TryParseUpgradeRequest_UnsupportedVersionValue_IsRejectedAsUnsupportedVersion()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 8\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.UnsupportedVersion, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a supported version value surrounded by header-value whitespace is trimmed and still accepted, not misclassified as unsupported by the newly split version gate.</summary>
    [Fact]
    public void TryParseUpgradeRequest_SupportedVersionWithSurroundingWhitespace_IsAccepted()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version:   13  \r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that a missing Sec-WebSocket-Key is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MissingKey_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that an empty Sec-WebSocket-Key value is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_EmptyKey_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: \r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a header line without a colon separator is rejected as malformed.</summary>
    [Fact]
    public void TryParseUpgradeRequest_HeaderLineWithoutColon_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "not a header line\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a header line consisting only of a colon (no name) is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_HeaderLineWithBareColon_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            ":no-name\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that completely empty input is rejected rather than throwing.</summary>
    [Fact]
    public void TryParseUpgradeRequest_EmptyInput_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(ReadOnlySpan<byte>.Empty, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a key which is not valid Base64 is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MalformedBase64Key_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(BuildRequestBytes(key: "abc"), out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a key decoding to fewer than the required 16 bytes is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_KeyDecodesToFifteenBytes_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(BuildRequestBytes(key: "dGhlIHNhbXBsZSBub25j"), out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a well-formed but short Base64 value (one decoded byte) is rejected, not only obviously malformed input.</summary>
    [Fact]
    public void TryParseUpgradeRequest_KeyDecodesToOneByte_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(BuildRequestBytes(key: "AA=="), out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a key decoding to more than the required 16 bytes is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_KeyDecodesToSeventeenBytes_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(BuildRequestBytes(key: "dGhlIHNhbXBsZSBub25jZWU="), out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a key with invalid Base64 padding is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_KeyWithInvalidPadding_IsRejected()
    {
        Assert.Equal(
            HandshakeRejectReason.Malformed,
            PublicWebSocketHandshake.TryParseUpgradeRequest(BuildRequestBytes(key: "dGhlIHNhbXBsZSBub25jZQ"), out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a valid, distinct 16-byte nonce is accepted, not only the RFC example key.</summary>
    [Fact]
    public void TryParseUpgradeRequest_ValidSixteenByteNonce_IsAccepted()
    {
        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(
            BuildRequestBytes(key: "AAECAwQFBgcICQoLDA0ODw=="), out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.NotEqual(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a valid key surrounded by header-value whitespace is trimmed and still accepted.</summary>
    [Fact]
    public void TryParseUpgradeRequest_ValidKeyWithSurroundingWhitespace_IsAccepted()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key:   dGhlIHNhbXBsZSBub25jZQ==  \r\n\r\n");

        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that a request missing the required Host header is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MissingHostHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that an empty Host header value is rejected.</summary>
    [Fact]
    public void TryParseUpgradeRequest_EmptyHostHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: \r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a repeated Host header is rejected rather than letting the last value silently win.</summary>
    [Fact]
    public void TryParseUpgradeRequest_DuplicateHostHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Host: evil.example\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a repeated Upgrade header is rejected, including when both occurrences agree.</summary>
    [Fact]
    public void TryParseUpgradeRequest_DuplicateUpgradeHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a repeated Sec-WebSocket-Version header is rejected, including when both occurrences agree.</summary>
    [Fact]
    public void TryParseUpgradeRequest_DuplicateVersionHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a repeated Sec-WebSocket-Key header is rejected, including when both occurrences agree.</summary>
    [Fact]
    public void TryParseUpgradeRequest_DuplicateKeyHeader_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that repeated Connection header lines are combined as a token list, not rejected as a duplicate singleton.</summary>
    [Fact]
    public void TryParseUpgradeRequest_ConnectionHeaderRepeatedAcrossTwoLines_TokensAreCombinedAndAccepted()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: keep-alive\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that a request line with an empty target between two spaces is rejected rather than admitted as well-formed.</summary>
    [Fact]
    public void TryParseUpgradeRequest_EmptyRequestTarget_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET  HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>Verifies that a request line with no spaces at all is rejected rather than throwing or being misread.</summary>
    [Fact]
    public void TryParseUpgradeRequest_RequestLineWithNoSpaces_IsRejected()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out _));
    }

    /// <summary>
    /// Verifies that a header outside the singleton set (Host, Upgrade, Sec-WebSocket-Version,
    /// Sec-WebSocket-Key, Origin) may still repeat without being rejected -- the duplicate-header
    /// rejection introduced for those five is deliberately scoped to them, not a blanket ban on any
    /// repeated header.
    /// </summary>
    [Fact]
    public void TryParseUpgradeRequest_UnrelatedHeaderRepeatedTwice_IsStillAccepted()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "X-Custom: first\r\n" +
            "X-Custom: second\r\n\r\n");

        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
    }

    /// <summary>Verifies that a handshake with no Origin header at all -- the normal shape for a native client -- is accepted, matching "native client without Origin proceeds normally."</summary>
    [Fact]
    public void TryParseUpgradeRequest_NoOriginHeader_IsAccepted()
    {
        HandshakeRejectReason result = PublicWebSocketHandshake.TryParseUpgradeRequest(
            BuildRequestBytes(key: "dGhlIHNhbXBsZSBub25jZQ=="), out string acceptKey);

        Assert.Equal(HandshakeRejectReason.None, result);
        Assert.NotEqual(string.Empty, acceptKey);
    }

    /// <summary>Verifies that an arbitrary web Origin is rejected as a disallowed browser-originated request, not as malformed HTTP.</summary>
    [Fact]
    public void TryParseUpgradeRequest_ArbitraryWebOrigin_IsRejectedAsDisallowedOrigin()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Origin: https://evil.example\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.DisallowedOrigin, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that a localhost Origin is rejected the same as any other, so a browser page is never treated as privileged merely because its own origin happens to be localhost.</summary>
    [Fact]
    public void TryParseUpgradeRequest_LocalhostOrigin_IsRejectedAsDisallowedOrigin()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Origin: http://localhost\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.DisallowedOrigin, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that an opaque "null" Origin (as sent by a sandboxed browsing context) is rejected the same as any other Origin value.</summary>
    [Fact]
    public void TryParseUpgradeRequest_NullOrigin_IsRejectedAsDisallowedOrigin()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Origin: null\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.DisallowedOrigin, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that an Origin header with an empty value is still rejected -- the policy is presence-based, not a check of the value's content, unlike Host's explicit non-empty requirement.</summary>
    [Fact]
    public void TryParseUpgradeRequest_EmptyOriginValue_IsRejectedAsDisallowedOrigin()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Origin: \r\n\r\n");

        Assert.Equal(HandshakeRejectReason.DisallowedOrigin, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that the Origin policy applies regardless of the header name's letter case.</summary>
    [Fact]
    public void TryParseUpgradeRequest_MixedCaseOriginHeaderName_IsRejectedAsDisallowedOrigin()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "oRiGiN: https://example.com\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.DisallowedOrigin, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>
    /// Verifies that a duplicated Origin header is rejected through the existing singleton-header
    /// duplicate path -- as <see cref="HandshakeRejectReason.Malformed"/> -- rather than as
    /// <see cref="HandshakeRejectReason.DisallowedOrigin"/>, so header duplication can never be used
    /// to bypass or reclassify the origin policy.
    /// </summary>
    [Fact]
    public void TryParseUpgradeRequest_DuplicateOriginHeader_IsRejectedAsMalformed()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
            "Origin: https://evil.example\r\n" +
            "Origin: https://also-evil.example\r\n\r\n");

        Assert.Equal(HandshakeRejectReason.Malformed, PublicWebSocketHandshake.TryParseUpgradeRequest(request, out string acceptKey));
        Assert.Equal(string.Empty, acceptKey);
    }

    /// <summary>Verifies that the built response contains the expected status line and accept header.</summary>
    [Fact]
    public void BuildSwitchingProtocolsResponse_ContainsTheExpectedStatusLineAndAcceptHeader()
    {
        byte[] response = PublicWebSocketHandshake.BuildSwitchingProtocolsResponse("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

        string text = Encoding.ASCII.GetString(response);
        Assert.StartsWith("HTTP/1.1 101 Switching Protocols\r\n", text);
        Assert.Contains("Upgrade: websocket\r\n", text);
        Assert.Contains("Connection: Upgrade\r\n", text);
        Assert.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    /// <summary>Verifies that the built response is a minimal 400 with no body and no rejection detail.</summary>
    [Fact]
    public void BuildBadRequestResponse_ContainsTheExpectedStatusLineAndHeaders()
    {
        byte[] response = PublicWebSocketHandshake.BuildBadRequestResponse();

        string text = Encoding.ASCII.GetString(response);
        Assert.StartsWith("HTTP/1.1 400 Bad Request\r\n", text);
        Assert.Contains("Connection: close\r\n", text);
        Assert.Contains("Content-Length: 0\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    /// <summary>Verifies that the built response is a minimal 426 advertising the one supported version.</summary>
    [Fact]
    public void BuildUpgradeRequiredResponse_ContainsTheExpectedStatusLineAndSupportedVersion()
    {
        byte[] response = PublicWebSocketHandshake.BuildUpgradeRequiredResponse();

        string text = Encoding.ASCII.GetString(response);
        Assert.StartsWith("HTTP/1.1 426 Upgrade Required\r\n", text);
        Assert.Contains("Connection: close\r\n", text);
        Assert.Contains("Sec-WebSocket-Version: 13\r\n", text);
        Assert.Contains("Content-Length: 0\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    /// <summary>Builds a minimal valid WebSocket upgrade request with the given key.</summary>
    private static byte[] BuildRequestBytes(string key) =>
        Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            $"Sec-WebSocket-Key: {key}\r\n\r\n");
}
