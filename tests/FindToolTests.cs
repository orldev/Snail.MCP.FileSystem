using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.MCP.FileSystem.Tests.Support;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>
/// find_tool, Snail.Sdk's over this server's tools: the words to search by live in <see cref="ToolMeanings"/>, not in the
/// descriptions every session carries.
/// </summary>
public sealed class FindToolTests : IAsyncLifetime, IDisposable
{
    private readonly Copy _copy = new();

    private InProcessServer _server = null!;

    public async Task InitializeAsync() => _server = await Listening.StartedAsync(_copy.Config);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    public void Dispose() => _copy.Dispose();

    [Theory]
    [InlineData("change one line in a file", "edit_file")]
    [InlineData("where is this text", "search_text")]
    [InlineData("what is in this folder", "list_directory")]
    [InlineData("which files match", "find_files")]
    [InlineData("save a new file", "write_file")]
    public async Task Find_QuestionInPlainWords_NamesTheToolThatAnswersIt(string question, string expected)
    {
        CallToolResult answer = await FindAsync(question, 3);

        Assert.Contains(expected, Names(answer));
    }

    [Fact]
    public async Task Find_QuestionNothingMatches_ReturnsEveryToolAndSaysSo()
    {
        await using McpClient client = await _server.SessionAsync();
        IList<McpClientTool> listed = await client.ListToolsAsync();

        CallToolResult answer = await FindAsync("превед медвед", 5);

        Assert.False(answer.Data()!["isMatched"]!.GetValue<bool>());
        Assert.Equal(listed.Select(tool => tool.Name).Order(StringComparer.Ordinal), Names(answer).Order(StringComparer.Ordinal));
    }

    /// <summary>A tool the session was not granted is not named: the door would refuse the call find_tool told the model to make.</summary>
    [Fact]
    public async Task Find_SessionGrantedOneTool_NamesNoOther()
    {
        await using McpClient client = await _server.SessionAsync($"root={Uri.EscapeDataString(_copy.Root)}&tool=find_tool&tool=read_file");

        CallToolResult answer = await client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = "change one line in a file" });

        Assert.DoesNotContain("edit_file", Names(answer));
    }

    [Fact]
    public async Task Meanings_EveryKey_IsAToolTheServerLists()
    {
        await using McpClient client = await _server.SessionAsync();
        var listed = (await client.ListToolsAsync()).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(ToolMeanings.All.Keys, name => Assert.Contains(name, listed));
    }

    [Fact]
    public async Task Meanings_EveryToolTheServerLists_HasOtherWords()
    {
        await using McpClient client = await _server.SessionAsync();

        IList<McpClientTool> listed = await client.ListToolsAsync();

        Assert.All(listed, tool => Assert.Contains(tool.Name, ToolMeanings.All.Keys));
    }

    /// <summary>The parameters the SDK fills are not the model's to fill, and find_tool must not show them.</summary>
    [Fact]
    public async Task Find_RunTools_ListsOnlyTheParametersTheModelFills()
    {
        CallToolResult answer = await FindAsync("several steps together in one round trip", 8);

        JsonNode run = answer.Data()!["tools"]!.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "run_tools")!;

        Assert.Equal(["steps"], run["parameters"]!.AsArray().Select(parameter => parameter!["name"]!.GetValue<string>()));
    }

    private async Task<CallToolResult> FindAsync(string task, int limit)
    {
        await using McpClient client = await _server.SessionAsync();
        CallToolResult answer = await client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = task, ["limit"] = limit });

        Assert.True(answer.IsOk(), answer.Text());

        return answer;
    }

    private static IReadOnlyList<string> Names(CallToolResult answer) =>
        [.. answer.Data()!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>())];
}
