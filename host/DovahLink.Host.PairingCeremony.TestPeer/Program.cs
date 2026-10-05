using DovahLink.Host.PairingCeremony.TestPeer;
using SasPairing;

// Test infrastructure only. Usage:
//   initiator <native-dll> <scope-hex> <application-identity-hex> <key-algorithm-hex> <public-key-hex> <shared-context-hex> <host-port>
//   hold-authority <native-dll> <scope-hex>
var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
try
{
    return args switch
    {
        ["initiator", string dll, string scope, string identity, string algorithm, string key, string context, string port] =>
            new InitiatorPeer(
                dll,
                Convert.FromHexString(scope),
                new SasPairingBootstrap(
                    Convert.FromHexString(identity), Convert.FromHexString(algorithm), Convert.FromHexString(key), Convert.FromHexString(context)),
                int.Parse(port, System.Globalization.CultureInfo.InvariantCulture),
                output,
                Console.In).Run(),
        ["hold-authority", string dll, string scope] =>
            new AuthorityHolder(dll, Convert.FromHexString(scope), output, Console.In).Run(),
        _ => throw new ArgumentException("Unknown test-peer command line."),
    };
}
catch (Exception exception)
{
    output.WriteLine($"ERROR {exception.GetType().Name}: {exception.Message}");
    return 2;
}
