using System.Net;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>
/// The server over HTTP as a program — the build beside the tests, or the native publication SNAIL_FILESYSTEM_SERVER names —
/// on a free loopback port, configured with a volume of working copies: each session names its copy in the address, and
/// the tools it was granted.
/// </summary>
public sealed class HttpContractTests : IAsyncLifetime
{
    private readonly string _volume = TestFolder.Create();

    private HttpServerProgram _server = null!;

    private string Mine => Path.Combine(_volume, "mine");

    private string Theirs => Path.Combine(_volume, "theirs");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Mine);
        Directory.CreateDirectory(Theirs);
        await File.WriteAllTextAsync(Path.Combine(Mine, "own.txt"), "mine");
        await File.WriteAllTextAsync(Path.Combine(Theirs, "own.txt"), "theirs");

        _server = await HttpServerProgram.StartAsync(
            "Snail.MCP.FileSystem",
            "SNAIL_FILESYSTEM_SERVER",
            new Dictionary<string, string?> { [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _volume });
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_volume, recursive: true);
    }

    [Fact]
    public async Task Tools_OverHttp_KeepTheGuardRules()
    {
        await using McpClient client = await _server.SessionAsync(Root(Mine));

        IList<McpClientTool> listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// <summary>One server, and every session working in its own copy of the volume, writing included.</summary>
    [Fact]
    public async Task Sessions_OverHttp_EachWorkInTheirOwnCopy()
    {
        await using McpClient mine = await _server.SessionAsync(Root(Mine));
        await using McpClient theirs = await _server.SessionAsync(Root(Theirs));

        CallToolResult read = await mine.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "own.txt" });
        CallToolResult written = await theirs.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "new.txt", ["text"] = "написано" });
        CallToolResult across = await mine.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = Path.Combine(Theirs, "own.txt") });

        Assert.Equal("mine", read.Data()!["text"]!.GetValue<string>());
        Assert.True(written.IsOk(), written.Text());
        Assert.Equal("написано", await File.ReadAllTextAsync(Path.Combine(Theirs, "new.txt")));
        Assert.False(across.IsOk());
        Assert.Contains("outside this request", across.Error(), StringComparison.Ordinal);
        Assert.Equal(Reached.Outside, across.Type());
    }

    /// <summary>A role given some of the tools sees those alone, and a call or a run_tools step beyond them is refused in the envelope.</summary>
    [Fact]
    public async Task Tools_GrantedSome_ListThoseAloneAndRefuseTheRest()
    {
        await using McpClient client = await _server.SessionAsync($"{Root(Mine)}&tool=read_file&tool=run_tools");

        IList<McpClientTool> listed = await client.ListToolsAsync();
        CallToolResult direct = await client.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "a.txt", ["text"] = "x" });
        CallToolResult forwarded = await client.CallToolAsync("run_tools", new Dictionary<string, object?>
        {
            ["steps"] = new object[]
            {
                new Dictionary<string, object?> { ["tool"] = "write_file", ["arguments"] = new Dictionary<string, object?> { ["path"] = "b.txt", ["text"] = "x" } }
            }
        });

        Assert.Equal(["read_file", "run_tools"], listed.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.True(direct.IsError);
        Assert.Contains("\"type\":\"notGranted\"", direct.Text(), StringComparison.Ordinal);
        Assert.True(forwarded.IsError);
        Assert.Contains("'write_file' was not granted", forwarded.Error(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Mine, "a.txt")));
        Assert.False(File.Exists(Path.Combine(Mine, "b.txt")));
    }

    /// <summary>
    /// A parameter read_file does not take — offset where it takes from — was dropped without a word and the whole file
    /// came back; now the call is refused naming it, before the tool runs.
    /// </summary>
    [Fact]
    public async Task ReadFile_ParameterItDoesNotTake_IsRefusedNamingIt()
    {
        await using McpClient client = await _server.SessionAsync(Root(Mine));

        CallToolResult answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "own.txt", ["offset"] = 2 });

        Assert.True(answer.IsError);
        Assert.Equal("arguments", answer.Type());
        Assert.Contains("offset", answer.Error(), StringComparison.Ordinal);
    }

    /// <summary>A copy this machine lacks, or a folder outside the volume, is turned away at the door, before any session.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("outside")]
    public async Task Session_RootItCannotServe_IsTurnedAwayAtTheDoor(string which)
    {
        string root = which == "missing" ? Path.Combine(_volume, "not-on-this-machine") : Path.GetTempPath();
        using var http = new HttpClient();

        using HttpResponseMessage answer = await http.GetAsync(new Uri($"{_server.Endpoint}?{Root(root)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    /// <summary>A write through a link to a file not there yet, leading out of the copy, is refused and creates nothing.</summary>
    [Fact]
    public async Task WriteFile_ThroughADanglingLinkOutOfTheCopy_IsRefused()
    {
        string outside = TestFolder.Create();
        string target = Path.Combine(outside, "planted.txt");
        File.CreateSymbolicLink(Path.Combine(Mine, "out.txt"), target);

        try
        {
            await using McpClient client = await _server.SessionAsync(Root(Mine));

            CallToolResult answer = await client.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "out.txt", ["text"] = "planted" });

            Assert.True(answer.IsError);
            Assert.Contains("outside this request", answer.Error(), StringComparison.Ordinal);
            Assert.Equal(Reached.Outside, answer.Type());
            Assert.False(File.Exists(target));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Endpoint_RequestFromABrowserPage_IsRefused() =>
        Assert.Empty(await HttpGuards.ProblemsAsync(new Uri($"{_server.Endpoint}?{Root(Mine)}")));

    private static string Root(string folder) => $"root={Uri.EscapeDataString(folder)}";
}
