using ModelContextProtocol.Client;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The server as a program: a real process, started the way a client starts it, and every tool called once.</summary>
/// <remarks>A native build breaks not when it is built but on a call that lacks the metadata of a type: the steps of run_tools are such a type.</remarks>
public sealed class StdioContractTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("snail-mcp-filesystem-stdio").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Every_tool_answers_over_stdio()
    {
        File.WriteAllText(Path.Combine(_root, "notes.md"), "первая строка\nвторая строка\n");
        await using var client = await StartedAsync();

        var calls = new (string Tool, Dictionary<string, object?> Arguments)[]
        {
            ("read_file", new() { ["path"] = "notes.md" }),
            ("list_directory", new()),
            ("find_files", new() { ["pattern"] = "*.md" }),
            ("search_text", new() { ["query"] = "вторая" }),
            ("write_file", new() { ["path"] = "new.txt", ["text"] = "написано" }),
            ("edit_file", new() { ["path"] = "new.txt", ["find"] = "написано", ["replace"] = "исправлено" }),
            ("find_tool", new() { ["task"] = "change one line in a file" }),
            ("run_tools", new()
            {
                ["steps"] = new object[]
                {
                    new Dictionary<string, object?> { ["tool"] = "read_file", ["params"] = new Dictionary<string, object?> { ["path"] = "new.txt" } },
                    new Dictionary<string, object?> { ["tool"] = "find_files", ["params"] = new Dictionary<string, object?> { ["pattern"] = "*.txt" } }
                }
            })
        };

        var listed = await client.ListToolsAsync();

        Assert.Equal(calls.Select(call => call.Tool).Order(StringComparer.Ordinal), listed.Select(tool => tool.Name).Order(StringComparer.Ordinal));

        foreach (var (tool, arguments) in calls)
        {
            var answer = await client.CallToolAsync(tool, arguments);

            Assert.True(answer.IsError is not true, $"{tool}: {ServerProgram.Text(answer)}");
        }

        Assert.Equal("исправлено", File.ReadAllText(Path.Combine(_root, "new.txt")));
    }

    /// <summary>A client knows the server by the command a person types to start it.</summary>
    [Fact]
    public async Task Initialize_AsAProgram_NamesTheServerByItsCommand()
    {
        await using var client = await StartedAsync();

        Assert.Equal("s3l-mcp-filesystem", client.ServerInfo.Name);
    }

    /// <summary>
    /// The guard rules of every Snail server, on the list a model receives: names in snake case and listed once, the tool
    /// and every parameter described, read-only or destructive stated, every allowed value named, English.
    /// </summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    private Task<McpClient> StartedAsync()
    {
        var (command, arguments) = ServerProgram.Command("Snail.MCP.FileSystem", "SNAIL_FILESYSTEM_SERVER");

        return McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = arguments,
            EnvironmentVariables = new Dictionary<string, string?> { [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _root }
        }));
    }
}
