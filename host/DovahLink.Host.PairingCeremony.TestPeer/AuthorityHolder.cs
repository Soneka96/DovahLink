using SasPairing;

namespace DovahLink.Host.PairingCeremony.TestPeer;

/// <summary>
/// Registers one pairing authority scope in this process and holds it until standard input closes,
/// so a test can observe cross-process authority ownership through the public package API.
/// </summary>
/// <param name="nativeLibraryPath">The absolute native library path.</param>
/// <param name="authorityScope">The scope to register.</param>
/// <param name="output">Where the outcome line is written.</param>
/// <param name="input">Read until it closes, which releases the authority.</param>
internal sealed class AuthorityHolder(string nativeLibraryPath, byte[] authorityScope, TextWriter output, TextReader input)
{
    /// <summary>Registers and holds the scope, reporting <c>HELD</c> or <c>UNAVAILABLE &lt;status&gt;</c>.</summary>
    /// <returns>The process exit code: 0 when held then released, 5 when the scope was unavailable.</returns>
    public int Run()
    {
        using SasPairingRuntime runtime = SasPairingRuntime.Create(nativeLibraryPath);
        SasPairingAuthority authority;
        try
        {
            authority = runtime.RegisterAuthority(authorityScope);
        }
        catch (SasPairingNativeException exception)
        {
            output.WriteLine($"UNAVAILABLE {exception.KnownStatus}");
            return 5;
        }

        using (authority)
        {
            output.WriteLine("HELD");
            input.ReadToEnd();
        }

        output.WriteLine("RELEASED");
        return 0;
    }
}
