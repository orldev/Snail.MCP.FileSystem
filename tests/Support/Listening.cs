using Microsoft.Extensions.Configuration;
using Snail.MCP.FileSystem.Extensions;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>The server at an address, in the test's own process, on a free loopback port.</summary>
public static class Listening
{
    /// <summary>The server built as <c>Program</c> builds it for a port, served by Snail.Sdk's <see cref="InProcessServer"/> on whatever loopback port is free.</summary>
    public static Task<InProcessServer> StartedAsync(ServerConfig config) =>
        InProcessServer.ServeAsync(
            McpServerHost.Listening([], new ConfigurationBuilder().Build(), config, new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:0"), ListensOnEveryInterface: false)),
            McpServerHost.Path);

    /// <summary>The MCP endpoint of the server, with the session's address as its query.</summary>
    public static Uri Endpoint(InProcessServer server, string query) => new($"{server.Endpoint}?{query}");
}
