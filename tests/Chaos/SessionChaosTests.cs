using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.MCP.FileSystem.Extensions;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// The server at an address, listening on every interface with no key: the URL of each request is the session's whole
/// identity — its roots and its grant.
/// </summary>
public sealed class SessionChaosTests
{
    /// <summary>
    /// The grant is a call-tool filter, and <c>run_tools</c> calls <see cref="ModelContextProtocol.Server.McpServerTool.InvokeAsync"/>
    /// itself, past the filter. A role given <c>read_file</c> and <c>run_tools</c> writes files — exactly the hole
    /// <see cref="SessionGrant"/>'s remarks say the grant was made to close.
    /// </summary>
    [Fact]
    public async Task RunTools_SessionNotGrantedWriteFile_CannotWriteThroughIt()
    {
        using var copy = new Copy();
        await using WebApplication server = await StartedAsync(new ServerConfig());
        await using HttpClientTransport transport = Transport(server, $"root={Uri.EscapeDataString(copy.Root)}&tool=read_file&tool=run_tools");
        await using McpClient client = await McpClient.CreateAsync(transport);

        CallToolResult direct = await client.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "direct.txt", ["text"] = "x" });
        await client.CallToolAsync("run_tools", new Dictionary<string, object?>
        {
            ["steps"] = new object[]
            {
                new Dictionary<string, object?> { ["tool"] = "write_file", ["params"] = new Dictionary<string, object?> { ["path"] = "forwarded.txt", ["text"] = "written around the grant" } }
            }
        });

        Assert.True(direct.IsError);
        Assert.False(File.Exists(Path.Combine(copy.Root, "forwarded.txt")), "run_tools wrote a file the session was not granted to write");
    }

    /// <summary>
    /// The configured roots are the volume of working copies, yet a session that names a root is given that root and
    /// nothing checks it lies inside the volume: any folder of the host that exists is served, <c>?root=/</c> included,
    /// writable by default, to anyone who reaches the port.
    /// </summary>
    [Fact]
    public async Task Session_RootOutsideTheConfiguredVolume_IsNotServed()
    {
        using var volume = new Copy();
        using var outside = new Copy();
        outside.Write("secret.txt", "not yours");
        await using WebApplication server = await StartedAsync(new ServerConfig { Roots = [volume.Root] });

        string text = await ReadOrRefusedAsync(server, $"root={Uri.EscapeDataString(outside.Root)}", "secret.txt");

        Assert.DoesNotContain("not yours", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The address of each request is all a session of this server is, which holds only while the transport keeps no
    /// sessions: with one, a request of an open session could be posted to another address and the root would have to be
    /// kept from the request that opened it. The audit suspected exactly that; the transport is stateless, the SDK's
    /// default since the 2026-07-28 revision, and this keeps it so until somebody decides otherwise.
    /// </summary>
    [Fact]
    public async Task Server_AtAnAddressOpenedWithInitialize_KeepsNoSession()
    {
        using var copy = new Copy();
        await using WebApplication server = await StartedAsync(new ServerConfig());
        await using HttpClientTransport transport = Transport(server, $"root={Uri.EscapeDataString(copy.Root)}");
        await using McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" });

        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);
    }

    /// <summary>
    /// <c>Snail.Sdk.Mcp.Http</c> refuses every request that carries an <c>Origin</c>, because a browser always sends one and
    /// an agent never does. This server builds its own host on every interface and keeps no such refusal: a page that
    /// rebinds a name to the machine reaches the endpoint.
    /// </summary>
    [Fact]
    public async Task Session_RequestFromABrowserPage_IsRefused()
    {
        using var copy = new Copy();
        await using WebApplication server = await StartedAsync(new ServerConfig());
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(server, $"root={Uri.EscapeDataString(copy.Root)}"))
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"page","version":"1"}}}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Accept", "application/json, text/event-stream");

        using HttpResponseMessage answer = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
    }

    /// <summary>
    /// <c>run_tools</c> is a step of itself. Twenty steps of twenty steps of twenty is eight thousand calls from one, bounded
    /// only by how deep the JSON may nest; the step limit counts one level.
    /// </summary>
    [Fact]
    public async Task RunTools_StepThatIsRunToolsAgain_IsRefused()
    {
        using var copy = new Copy();
        await using WebApplication server = await StartedAsync(new ServerConfig());
        await using HttpClientTransport transport = Transport(server, $"root={Uri.EscapeDataString(copy.Root)}");
        await using McpClient client = await McpClient.CreateAsync(transport);

        var inner = new Dictionary<string, object?> { ["tool"] = "find_tool", ["params"] = new Dictionary<string, object?> { ["task"] = "read" } };
        var nested = new Dictionary<string, object?> { ["tool"] = "run_tools", ["params"] = new Dictionary<string, object?> { ["steps"] = new object[] { inner } } };
        CallToolResult answer = await client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["steps"] = new object[] { nested } });

        Assert.Contains("\"ok\":false", answer.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// "Every answer is JSON", says the README, "because a model reads the shape it saw last". An exception of the disk
    /// leaves the tool, and the SDK answers in its own words instead: no <c>ok</c>, no <c>error</c>, no <c>hint</c>.
    /// </summary>
    [Fact]
    public async Task ReadFile_DiskThrowsUnderTheCall_AnswerIsStillTheJsonOfTheServer()
    {
        using var copy = new Copy();
        string loop = Path.Combine(copy.Root, "loop");
        File.CreateSymbolicLink(loop, loop);
        await using WebApplication server = await StartedAsync(new ServerConfig());
        await using HttpClientTransport transport = Transport(server, $"root={Uri.EscapeDataString(copy.Root)}");
        await using McpClient client = await McpClient.CreateAsync(transport);

        CallToolResult answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "loop" });

        Assert.StartsWith("{\"ok\":false", answer.Text(), StringComparison.Ordinal);
    }

    private static async Task<WebApplication> StartedAsync(ServerConfig config)
    {
        WebApplication server = McpServerHost.Listening([], config);

        await server.StartAsync();

        return server;
    }

    private static async Task<string> ReadOrRefusedAsync(WebApplication server, string query, string path)
    {
        try
        {
            await using HttpClientTransport transport = Transport(server, query);
            await using McpClient client = await McpClient.CreateAsync(transport);
            CallToolResult answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = path });

            return answer.Text();
        }
        catch (HttpRequestException refused)
        {
            return refused.Message;
        }
    }

    private static HttpClientTransport Transport(WebApplication server, string query) =>
        new(new HttpClientTransportOptions { Endpoint = Endpoint(server, query) });

    private static Uri Endpoint(WebApplication server, string query)
    {
        string bound = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        int port = new Uri(bound.Replace("://*:", "://localhost:", StringComparison.Ordinal)).Port;

        return new Uri($"http://127.0.0.1:{port}{McpServerHost.Path}?{query}");
    }
}
