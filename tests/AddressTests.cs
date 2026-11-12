using System.Net;
using System.Runtime.Versioning;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The server at an address: where it learns the root of a session from, and whom it turns away at the door.</summary>
public sealed class AddressTests
{
    [Fact]
    public void Absent_RootOnThisMachine_IsNotReported()
    {
        using var copy = new Copy();

        Assert.Empty(new SessionRoots(Address.Of(copy.Root), []).Absent);
    }

    [Fact]
    public void Absent_RootNotOnThisMachine_IsNamed()
    {
        string elsewhere = Path.Combine(TestFolder.Create(), "not-on-this-machine");

        Assert.Equal([elsewhere], new SessionRoots(Address.Of(elsewhere), []).Absent);
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

        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

        using var client = new HttpClient();
        HttpResponseMessage answer = await client.GetAsync(Listening.Endpoint(server, $"root={Uri.EscapeDataString(elsewhere)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains(elsewhere, await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A server with no volume once served whatever copy a request named, the whole disk to anyone who could reach the
    /// port. Now a root that is on this machine is still turned away at the door, and the refusal names the variable that
    /// gives the server a volume.
    /// </summary>
    [Fact]
    public async Task Session_NamingARootThisMachineHasAtAServerWithNoVolume_IsRefusedNamingTheVariable()
    {
        using var copy = new Copy();

        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

        using var client = new HttpClient();
        HttpResponseMessage answer = await client.GetAsync(Listening.Endpoint(server, $"root={Uri.EscapeDataString(copy.Root)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal(
            $"this server is configured with no root, so it serves none a request names, {copy.Root} among them: set {ServerConfigExtensions.RootsSetting}",
            await answer.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A root inside a folder the server may not enter may well be there: taken for missing, the session was told "this
    /// machine has no" copy it had right, and went to look for another host. The door now says the server may not read it.
    /// </summary>
    [NotAsRootFact]
    [UnsupportedOSPlatform("windows")]
    public async Task Session_OpenedAtARootTheServerMayNotRead_IsRefusedSayingSo()
    {
        using var copy = new Copy();
        copy.Write("locked/task/App.cs", "");
        string locked = Path.Combine(copy.Root, "locked");
        string root = Path.Combine(locked, "task");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

            using var client = new HttpClient();
            HttpResponseMessage answer = await client.GetAsync(Listening.Endpoint(server, $"root={Uri.EscapeDataString(root)}"));
            string refusal = await answer.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
            Assert.Contains($"this server may not read {root}", refusal, StringComparison.Ordinal);
            Assert.DoesNotContain("this machine has no", refusal, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>The server reaches a path on its own disk: a file in a folder it may not enter is answered <c>unreadable</c>, not absent.</summary>
    [NotAsRootFact]
    [UnsupportedOSPlatform("windows")]
    public async Task Call_FileInAFolderTheServerMayNotEnter_IsUnreadable()
    {
        using var copy = new Copy();
        copy.Write("locked/Secret.cs", "");
        string locked = Path.Combine(copy.Root, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            await using InProcessServer server = await Listening.StartedAsync(new ServerConfig { Roots = [Path.GetTempPath()] });

            string answer = await Read(server, copy.Root, "locked/Secret.cs");

            Assert.StartsWith("{\"ok\":false", answer, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"unreadable\"", answer, StringComparison.Ordinal);
            Assert.Contains("\"hint\":", answer, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>What it is all for: one server, and every session working in its own copy.</summary>
    [Fact]
    public async Task Sessions_OfOneServer_EachSeeTheirOwnCopyOnly()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig { Roots = [Path.GetTempPath()] });

        Assert.Equal("mine", Text(await Read(server, mine.Root, "own.txt")));
        Assert.Equal("theirs", Text(await Read(server, theirs.Root, "own.txt")));
        Assert.Contains("outside this request", await Read(server, mine.Root, Path.Combine(theirs.Root, "own.txt")), StringComparison.Ordinal);
    }

    private static string Text(string answer) => JsonNode.Parse(answer)!["data"]!["text"]!.GetValue<string>();

    private static async Task<string> Read(InProcessServer server, string root, string path)
    {
        await using HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = Listening.Endpoint(server, $"root={Uri.EscapeDataString(root)}")
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
        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

        using var client = new HttpClient();
        HttpResponseMessage answer = await client.GetAsync(server.Endpoint);

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

        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig { Roots = [Path.GetTempPath()] });

        await using HttpClientTransport transport = new(new HttpClientTransportOptions { Endpoint = Listening.Endpoint(server, $"root={Uri.EscapeDataString(copy.Root)}&tool=read_file") });
        await using McpClient client = await McpClient.CreateAsync(transport);

        CallToolResult refused = await client.CallToolAsync("list_directory", new Dictionary<string, object?>());

        Assert.Equal(true, refused.IsError);
        Assert.Contains("was not granted", refused.Error(), StringComparison.Ordinal);
        Assert.Contains("\"type\":\"notGranted\"", refused.Text(), StringComparison.Ordinal);
    }
}
