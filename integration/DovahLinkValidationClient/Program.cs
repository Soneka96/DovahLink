// Manual validation entry point for a real DovahLink bridge connection.

using DovahLinkValidationClient;

string host = Environment.GetEnvironmentVariable("DOVAHLINK_BRIDGE_HOST") ?? "127.0.0.1";
string port = Environment.GetEnvironmentVariable("DOVAHLINK_BRIDGE_PORT") ?? "58231";
string? token = Environment.GetEnvironmentVariable("DOVAHLINK_BRIDGE_TOKEN");

if (string.IsNullOrEmpty(token))
{
    Console.Error.WriteLine("Set DOVAHLINK_BRIDGE_TOKEN to the bridge's hex-encoded one-time token before running.");
    return 1;
}

var uri = new Uri($"ws://{host}:{port}/");
Console.WriteLine($"Connecting to {uri} ...");
await using var connection = new BridgeConnection();
await connection.ConnectAsync(uri);
Console.WriteLine("Connected.");

int exitCode = await ValidationRun.ExecuteAsync(connection, token, Console.Out);
if (exitCode != 0)
{
    return exitCode;
}

Console.WriteLine("Press Enter to disconnect.");
Console.ReadLine();
return 0;
