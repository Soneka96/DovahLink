// Manual validation entry point for a real DovahLink bridge connection.

using System.Text.Json.Nodes;
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

string clientId = Guid.NewGuid().ToString();
var helloPayload = new JsonObject
{
    ["endpoint"] = "client",
    ["clientId"] = clientId,
    ["auth"] = new JsonObject
    {
        ["method"] = "one_time_local_token",
        ["token"] = token,
    },
};
await connection.SendAsync(new Envelope("hello", Guid.NewGuid().ToString(), null, null, helloPayload));

Envelope helloAck = await connection.ReceiveAsync();
if (helloAck.MessageType != "hello_ack")
{
    Console.Error.WriteLine($"Bridge rejected hello: {helloAck.MessageType} {helloAck.Payload}");
    return 1;
}
string sessionId = helloAck.SessionId ?? throw new InvalidOperationException("hello_ack carried no sessionId.");
Console.WriteLine($"hello_ack: sessionId={sessionId} bridgeVersion={helloAck.Payload["bridgeVersion"]}");

Envelope capabilities = await connection.ReceiveAsync();
Console.WriteLine($"capabilities: {capabilities.Payload}");

var subscribePayload = new JsonObject { ["stateAreas"] = new JsonArray("character") };
await connection.SendAsync(new Envelope("subscribe", Guid.NewGuid().ToString(), sessionId, null, subscribePayload));

Envelope subscriptionAck = await connection.ReceiveAsync();
Console.WriteLine($"subscription_ack: {subscriptionAck.Payload}");

Envelope snapshot = await connection.ReceiveAsync();
Console.WriteLine($"state_snapshot: {snapshot.Payload}");

Console.WriteLine("Press Enter to disconnect.");
Console.ReadLine();
return 0;
