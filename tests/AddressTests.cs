using System.Text.Json.Nodes;
using System.Net;
using ModelContextProtocol.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Extensions;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The server at an address: where it learns the root of a session from, and whom it turns away at the door.</summary>
public sealed class AddressTests
{
    [Fact]
    public void The_address_hands_over_every_root_it_names()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?root=/workspaces/one&root=/workspaces/two");

        var address = new HttpAddress(Accessor(context));

        Assert.Equal(["/workspaces/one", "/workspaces/two"], address.Values(SessionRoots.Parameter));
    }

    /// <summary>Outside a request there is no address at all, and nothing to invent a root from.</summary>
    [Fact]
    public void Outside_a_request_the_address_says_nothing()
    {
        Assert.Empty(new HttpAddress(Accessor(null)).Values(SessionRoots.Parameter));
    }

    [Fact]
    public void A_root_that_is_on_this_machine_is_not_reported_absent()
    {
        using var copy = new Copy();

        Assert.Empty(SessionRoots.Absent([copy.Root]));
    }

    [Fact]
    public void A_root_that_is_not_on_this_machine_is_named()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}");

        Assert.Equal([elsewhere], SessionRoots.Absent([elsewhere]));
    }

    /// <summary>
    /// The worst failure of servers at addresses: discovery hands the worker of machine B the server of machine A. A call
    /// does not show it: the server answers, only about somebody else's empty folder. So the session is not opened at all,
    /// and the refusal names the root.
    /// </summary>
    [Fact]
    public async Task A_session_opened_at_a_root_this_machine_does_not_have_is_refused_and_told_which()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}");

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        var answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(elsewhere)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains(elsewhere, await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>What it is all for: one server, and every session working in its own copy.</summary>
    [Fact]
    public async Task Two_sessions_of_one_server_each_see_their_own_copy_and_not_the_other()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        Assert.Equal("mine", Text(await Read(server, mine.Root, "own.txt")));
        Assert.Equal("theirs", Text(await Read(server, theirs.Root, "own.txt")));
        Assert.Contains("outside this session", await Read(server, mine.Root, Path.Combine(theirs.Root, "own.txt")), StringComparison.Ordinal);
    }

    private static string Text(string answer) => JsonNode.Parse(answer)!["data"]!["text"]!.GetValue<string>();

    private static async Task<string> Read(WebApplication server, string root, string path)
    {
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(root)}")
        }));

        var answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = path });

        return string.Join(string.Empty, answer.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }


    /// <summary>
    /// A session without a root at a server configured with none would get nothing anyway. Hearing it once at the door
    /// beats hearing it from every call for the whole step.
    /// </summary>
    [Fact]
    public async Task A_session_that_names_no_root_at_a_server_configured_with_none_is_turned_away()
    {
        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        var answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains("named no root", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }


    /// <summary>
    /// The allow-list of a role held only until it was given a tool that calls other tools: run_tools reached everything
    /// the server has, including what the operator deliberately did not grant. Now the same address that names the root
    /// names what is allowed, and it is checked at the door of every call.
    /// </summary>
    [Fact]
    public async Task A_tool_the_session_was_not_granted_is_refused()
    {
        using var copy = new Copy();

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        var address = $"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(copy.Root)}&tool=read_file";

        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address) }));

        var refused = await client.CallToolAsync("list_directory", new Dictionary<string, object?>());

        Assert.Equal(true, refused.IsError);
        Assert.Contains("was not granted",
            string.Join(string.Empty, refused.Content.OfType<TextContentBlock>().Select(block => block.Text)),
            StringComparison.Ordinal);
    }

    /// <summary>The server listens on every interface; "::" cannot be called, so the port is taken with loopback.</summary>
    private static string Address(WebApplication server)
    {
        var bound = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return $"http://127.0.0.1:{new Uri(bound.Replace("://*:", "://localhost:", StringComparison.Ordinal)).Port}";
    }

    private static IHttpContextAccessor Accessor(HttpContext? context) => new HttpContextAccessor { HttpContext = context };
}
