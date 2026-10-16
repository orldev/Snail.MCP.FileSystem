using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Extensions;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The server at an address: where it learns the root of a session from, and whom it turns away at the door.</summary>
public sealed class AddressTests
{
    [Fact]
    public void Address_NamingTwoRoots_HandsOverBoth()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?root=/workspaces/one&root=/workspaces/two");

        var address = new HttpAddress(Accessor(context));

        Assert.Equal(["/workspaces/one", "/workspaces/two"], address.Values(SessionRoots.Parameter));
    }

    /// <summary>Outside a request there is no address at all, and nothing to invent a root from.</summary>
    [Fact]
    public void Address_OutsideARequest_SaysNothing() => Assert.Empty(new HttpAddress(Accessor(null)).Values(SessionRoots.Parameter));

    [Fact]
    public void Absent_RootOnThisMachine_IsNotReported()
    {
        using var copy = new Copy();

        Assert.Empty(copy.Roots.Absent([copy.Root]));
    }

    [Fact]
    public void Absent_RootNotOnThisMachine_IsNamed()
    {
        string elsewhere = Path.Combine(TestFolder.Create(), "not-on-this-machine");

        Assert.Equal([elsewhere], new SessionRoots(new ServerConfig(), new Address(), new LocalDisk()).Absent([elsewhere]));
    }

    /// <summary>
    /// The worst failure of servers at addresses: discovery hands the worker of machine B the server of machine A. A call
    /// does not show it: the server answers, only about somebody else's empty folder. So the session is not opened at all,
    /// and the refusal names the root.
    /// </summary>
    [Fact]
    public async Task Session_OpenedAtARootThisMachineLacks_IsRefusedNamingIt()
    {
        string elsewhere = Path.Combine(TestFolder.Create(), "not-on-this-machine");

        await using WebApplication server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        HttpResponseMessage answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(elsewhere)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains(elsewhere, await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>What it is all for: one server, and every session working in its own copy.</summary>
    [Fact]
    public async Task Sessions_OfOneServer_EachSeeTheirOwnCopyOnly()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        await using WebApplication server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        Assert.Equal("mine", Text(await Read(server, mine.Root, "own.txt")));
        Assert.Equal("theirs", Text(await Read(server, theirs.Root, "own.txt")));
        Assert.Contains("outside this session", await Read(server, mine.Root, Path.Combine(theirs.Root, "own.txt")), StringComparison.Ordinal);
    }

    private static string Text(string answer) => JsonNode.Parse(answer)!["data"]!["text"]!.GetValue<string>();

    private static async Task<string> Read(WebApplication server, string root, string path)
    {
        await using HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(root)}")
        });
        await using McpClient client = await McpClient.CreateAsync(transport);

        CallToolResult answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = path });

        return string.Join(string.Empty, answer.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    /// <summary>
    /// A session without a root at a server configured with none would get nothing anyway. Hearing it once at the door
    /// beats hearing it from every call for the whole step.
    /// </summary>
    [Fact]
    public async Task Session_NamingNoRootAtAServerWithNone_IsTurnedAway()
    {
        await using WebApplication server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        HttpResponseMessage answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains("named no root", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The allow-list of a role held only until it was given a tool that calls other tools: run_tools reached everything
    /// the server has, including what the operator deliberately did not grant. Now the same address that names the root
    /// names what is allowed, and it is checked at the door of every call.
    /// </summary>
    [Fact]
    public async Task Call_ToolNotGranted_IsRefused()
    {
        using var copy = new Copy();

        await using WebApplication server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        string address = $"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(copy.Root)}&tool=read_file";

        await using HttpClientTransport transport = new(new HttpClientTransportOptions { Endpoint = new Uri(address) });
        await using McpClient client = await McpClient.CreateAsync(transport);

        CallToolResult refused = await client.CallToolAsync("list_directory", new Dictionary<string, object?>());

        Assert.Equal(true, refused.IsError);
        Assert.Contains("was not granted",
            string.Join(string.Empty, refused.Content.OfType<TextContentBlock>().Select(block => block.Text)),
            StringComparison.Ordinal);
    }

    /// <summary>The server listens on every interface; "::" cannot be called, so the port is taken with loopback.</summary>
    private static string Address(WebApplication server)
    {
        string bound = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return $"http://127.0.0.1:{new Uri(bound.Replace("://*:", "://localhost:", StringComparison.Ordinal)).Port}";
    }

    private static IHttpContextAccessor Accessor(HttpContext? context) => new HttpContextAccessor { HttpContext = context };
}
