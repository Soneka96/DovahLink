using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;

await HostPoc.RunAsync(args);

/// <summary>Runs the non-production TLS 1.3 WebSocket server feasibility experiment.</summary>
internal static class HostPoc
{
    /// <summary>The persisted CNG key name reserved for this feasibility experiment.</summary>
    private const string KeyNamePrefix = "DovahLink.S2.Feasibility.";

    /// <summary>Creates and starts the local WSS server, or removes its disposable key on request.</summary>
    /// <param name="args">Optional command-line arguments; <c>--delete-key</c> removes the POC key.</param>
    public static async Task RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("This feasibility Host uses the Windows CNG provider.");
        }

        string keyName = args
            .FirstOrDefault(argument => argument.StartsWith("--key-name=", StringComparison.Ordinal))?
            .Substring("--key-name=".Length)
            ?? throw new ArgumentException("Pass a unique --key-name for this POC run.", nameof(args));
        if (!keyName.StartsWith(KeyNamePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The POC key name must use its reserved prefix.", nameof(args));
        }

        if (args.Contains("--delete-key", StringComparer.Ordinal))
        {
            DeleteKey(keyName);
            return;
        }

        if (args.Contains("--client-key-proof", StringComparer.Ordinal))
        {
            RunClientKeyProof(keyName);
            return;
        }

        using CngKey key = OpenOrCreateKey(keyName);
        using ECDsaCng ecdsa = new(key);
        using X509Certificate2 certificate = CreateCertificate(ecdsa);
        using X509Certificate2 renewedCertificate = CreateCertificate(ecdsa);
        byte[] spki = ecdsa.ExportSubjectPublicKeyInfo();
        string spkiPin = Convert.ToBase64String(SHA256.HashData(spki))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (certificate.Thumbprint == renewedCertificate.Thumbprint ||
            !CryptographicOperations.FixedTimeEquals(spki, renewedCertificate.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo()))
        {
            throw new CryptographicException("Certificate renewal did not retain the CNG Host key.");
        }

        bool privateKeyExported;
        try
        {
            _ = key.Export(CngKeyBlobFormat.EccPrivateBlob);
            privateKeyExported = true;
        }
        catch (CryptographicException)
        {
            // CNG rejected export as required by the experiment's non-exportable-key check.
            privateKeyExported = false;
        }

        if (privateKeyExported)
        {
            throw new CryptographicException("The POC CNG private key unexpectedly exported.");
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args: []);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
            listen.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = certificate,
                SslProtocols = SslProtocols.Tls13,
                ClientCertificateMode = ClientCertificateMode.NoCertificate,
            })));

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.Map("/ws", HandleWebSocketAsync);
        await app.StartAsync();

        IServerAddressesFeature addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose its bound address.");
        Uri address = addresses.Addresses.Select(value => new Uri(value)).Single();
        Console.WriteLine($"READY wss://127.0.0.1:{address.Port}/ws");
        Console.WriteLine($"SPKI_SHA256_BASE64URL={spkiPin}");
        Console.WriteLine("CNG_PRIVATE_KEY_EXPORT=REJECTED");
        Console.WriteLine("CERTIFICATE_RENEWAL=NEW_CERTIFICATE_SAME_SPKI");
        await app.WaitForShutdownAsync();
    }

    /// <summary>Accepts one text frame and returns the fixed feasibility response.</summary>
    /// <param name="context">The Kestrel request carrying a WebSocket upgrade.</param>
    private static async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ITlsHandshakeFeature? tls = context.Features.Get<ITlsHandshakeFeature>();
        if (tls?.Protocol != SslProtocols.Tls13)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        byte[] buffer = new byte[128];
        ValueWebSocketReceiveResult received = await socket.ReceiveAsync(new Memory<byte>(buffer), CancellationToken.None);
        if (!received.EndOfMessage ||
            received.MessageType != WebSocketMessageType.Text ||
            Encoding.UTF8.GetString(buffer, 0, received.Count) != "hello-s2")
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "unexpected POC message", CancellationToken.None);
            return;
        }

        byte[] reply = Encoding.UTF8.GetBytes("dovahlink-s2-wss-ok");
        await socket.SendAsync(reply, WebSocketMessageType.Text, true, CancellationToken.None);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", CancellationToken.None);
    }

    /// <summary>Proves a persisted non-exportable CNG Client key can sign fresh Host challenges.</summary>
    /// <param name="keyName">The unique, POC-prefixed key name for this process run.</param>
    private static void RunClientKeyProof(string keyName)
    {
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            throw new InvalidOperationException("The Client POC key name must not already exist.");
        }

        try
        {
            byte[] publicKeyInfo;
            using (CngKey createdKey = OpenOrCreateKey(keyName))
            using (ECDsaCng creator = new(createdKey))
            {
                publicKeyInfo = creator.ExportSubjectPublicKeyInfo();
            }

            using CngKey reopenedKey = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            using ECDsaCng clientKey = new(reopenedKey);
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            byte[] transcript = CreateClientProofTranscript(challenge);
            byte[] signature = clientKey.SignData(transcript, HashAlgorithmName.SHA256);
            using ECDsa hostVerifier = ECDsa.Create();
            hostVerifier.ImportSubjectPublicKeyInfo(publicKeyInfo, out int consumedBytes);
            if (consumedBytes != publicKeyInfo.Length)
            {
                throw new CryptographicException("The Client public key had trailing bytes.");
            }

            HashSet<string> consumedChallenges = new(StringComparer.Ordinal);
            string challengeId = Convert.ToHexString(SHA256.HashData(challenge));
            bool firstProofAccepted = consumedChallenges.Add(challengeId) &&
                hostVerifier.VerifyData(transcript, signature, HashAlgorithmName.SHA256);
            bool replayAccepted = consumedChallenges.Add(challengeId) &&
                hostVerifier.VerifyData(transcript, signature, HashAlgorithmName.SHA256);
            byte[] nextTranscript = CreateClientProofTranscript(RandomNumberGenerator.GetBytes(32));
            bool oldProofAcceptedForNewChallenge = hostVerifier.VerifyData(
                nextTranscript, signature, HashAlgorithmName.SHA256);

            bool privateKeyExported;
            try
            {
                _ = reopenedKey.Export(CngKeyBlobFormat.EccPrivateBlob);
                privateKeyExported = true;
            }
            catch (CryptographicException)
            {
                privateKeyExported = false;
            }

            if (!firstProofAccepted || replayAccepted || oldProofAcceptedForNewChallenge || privateKeyExported)
            {
                throw new CryptographicException("The CNG Client proof feasibility checks failed.");
            }

            Console.WriteLine("CLIENT_CNG_PRIVATE_KEY_EXPORT=REJECTED");
            Console.WriteLine("CLIENT_KEY_REOPENED=PASS");
            Console.WriteLine("FRESH_CLIENT_PROOF_ACCEPTED=PASS");
            Console.WriteLine("REPLAYED_CHALLENGE_REJECTED=PASS");
            Console.WriteLine("OLD_PROOF_FOR_NEW_CHALLENGE_REJECTED=PASS");
        }
        finally
        {
            DeleteKey(keyName);
        }
    }

    /// <summary>Builds the POC-only domain-separated Client proof transcript for one challenge.</summary>
    /// <param name="challenge">The fresh random challenge supplied by the Host.</param>
    /// <returns>The bytes signed by the persistent Client key.</returns>
    private static byte[] CreateClientProofTranscript(byte[] challenge) =>
        Encoding.UTF8.GetBytes(
            $"DovahLink.S2.ClientPoP.POC.v1\0host-poc\0client-poc\0{Convert.ToHexString(challenge)}");

    /// <summary>Opens the POC's per-user software CNG key, creating it as non-exportable if absent.</summary>
    /// <param name="keyName">The unique, POC-prefixed key name for this process run.</param>
    /// <returns>The persistent ECDSA P-256 key handle.</returns>
    private static CngKey OpenOrCreateKey(string keyName)
    {
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            return CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        }

        CngKeyCreationParameters parameters = new()
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        return CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters);
    }

    /// <summary>Creates a short-lived self-signed certificate around the supplied persistent key.</summary>
    /// <param name="key">The non-exportable ECDSA key used by Kestrel.</param>
    /// <returns>A self-signed TLS server certificate.</returns>
    private static X509Certificate2 CreateCertificate(ECDsa key)
    {
        CertificateRequest request = new("CN=localhost", key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>Deletes only the named temporary CNG key created by this experiment.</summary>
    /// <param name="keyName">The unique, POC-prefixed key name created by this process run.</param>
    private static void DeleteKey(string keyName)
    {
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            using CngKey key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }
    }
}
