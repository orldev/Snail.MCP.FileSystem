using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>The server at an address, as whoever can reach the port meets it.</summary>
public sealed class DoorChaosTests
{
    /// <summary>
    /// Given a port, the server listened on every interface, with no credential of any kind: the only refusal is of a
    /// request carrying <c>Origin</c>, which a browser sends and <c>curl</c> does not. That is the container's shape — the
    /// published port is the boundary — but the same setting on a developer's machine served the configured roots,
    /// writable by default, to the whole network. Every interface is now a container's alone, and no setting opens it elsewhere.
    /// </summary>
    [Fact]
    public async Task Listening_ByDefault_AnswersOnLoopbackOnly()
    {
        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

        string[] bound = [.. server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses];

        Assert.All(bound, address => Assert.True(IsLoopback(address), $"listening at {address}"));
    }

    /// <summary>
    /// "Every answer is JSON … the same shape either way, because a model reads the shape it saw last." Every refusal of
    /// the tools kept that shape; the refusal at the door — a tool the session was not granted — was a bare sentence.
    /// </summary>
    [Fact]
    public async Task Call_ToolNotGranted_AnswersInTheServersJson()
    {
        using var copy = new Copy();
        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig { Roots = [Path.GetTempPath()] });

        await using HttpClientTransport transport = new(new HttpClientTransportOptions { Endpoint = Listening.Endpoint(server, $"root={Uri.EscapeDataString(copy.Root)}&tool=read_file") });
        await using McpClient client = await McpClient.CreateAsync(transport);
        CallToolResult refused = await client.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "a.txt", ["text"] = "x" });

        Assert.True(refused.IsError);
        Assert.StartsWith("{\"ok\":false", refused.Text(), StringComparison.Ordinal);
        Assert.Contains("\"type\":\"notGranted\"", refused.Text(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(copy.Root, "a.txt")));
    }

    private static bool IsLoopback(string address)
    {
        string host = new Uri(address.Replace("://*:", "://any:", StringComparison.Ordinal).Replace("://+:", "://any:", StringComparison.Ordinal)).Host;

        return host == "localhost" || (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? ip) && IPAddress.IsLoopback(ip));
    }
}
