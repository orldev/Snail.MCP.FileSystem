using ModelContextProtocol.Client;
using Snail.Toolkit.Mcp.Testing;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>Сервер как программа: настоящий процесс, запущенный так, как его запускает клиент, и каждый инструмент по разу.</summary>
/// <remarks>Нативная сборка ломается не при сборке, а на вызове, которому не хватило сведений о типе: шаги run_tools — такой тип.</remarks>
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
