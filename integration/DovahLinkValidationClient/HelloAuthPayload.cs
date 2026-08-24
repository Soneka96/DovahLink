using System.Text.Json.Nodes;

namespace DovahLinkValidationClient;

/// <summary>
/// The nested <c>hello.auth</c> object (<c>protocol/schema/README.md</c>'s <c>hello</c>).
/// Encode-only: this client never decodes its own <c>hello</c>.
/// </summary>
/// <param name="Method">The auth method presented in <c>auth.method</c>.</param>
/// <param name="Token">The hex-encoded credential or token, when <paramref name="Method"/> requires
/// one. Omitted from the encoded object entirely (not merely <see langword="null"/>) when absent,
/// per <c>hello</c>'s wire shape.</param>
public sealed record HelloAuthPayload(string Method, string? Token = null)
{
    /// <summary>
    /// Encodes this auth payload as a JSON object.
    /// </summary>
    /// <returns>The encoded <c>auth</c> object.</returns>
    public JsonObject Encode()
    {
        var obj = new JsonObject { ["method"] = Method };
        if (Token is not null)
        {
            obj["token"] = Token;
        }
        return obj;
    }
}
