using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tools;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The server as a program: a real process, started the way a client starts it, and every tool called once.</summary>
/// <remarks>A native build breaks not when it is built but on a call that lacks the metadata of a type: the steps of run_tools are such a type.</remarks>
public sealed class StdioContractTests : IDisposable
{
    private readonly string _root = TestFolder.Create();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Tools_CalledOverStdio_EveryOneAnswers()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "первая строка\nвторая строка\n");
        await using McpClient client = await StartedAsync();

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

        IList<McpClientTool> listed = await client.ListToolsAsync();

        Assert.Equal(calls.Select(call => call.Tool).Order(StringComparer.Ordinal), listed.Select(tool => tool.Name).Order(StringComparer.Ordinal));

        foreach ((string? tool, Dictionary<string, object?>? arguments) in calls)
        {
            CallToolResult answer = await client.CallToolAsync(tool, arguments);

            Assert.True(answer.IsError is not true, $"{tool}: {answer.Text()}");
        }

        Assert.Equal("исправлено", await File.ReadAllTextAsync(Path.Combine(_root, "new.txt")));
    }

    /// <summary>
    /// Every tool states the shape of its answer as an output schema, and an answer that went through carries that shape
    /// as structured content: the very object of its text, with no field the schema does not name and none it requires
    /// missing. A client that validates structured content rejects an answer that does not fit, so a field added to an
    /// answer and not to its type fails here rather than at a client.
    /// </summary>
    [Fact]
    public async Task Tools_AnswersThatWentThrough_FitTheirOutputSchema()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "первая строка\nвторая строка\n");
        await using McpClient client = await StartedAsync();
        IList<McpClientTool> listed = await client.ListToolsAsync();
        JsonSerializerOptions strict = new(ToolJson.Options(FileSystemJson.Default))
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };

        foreach ((string? tool, Dictionary<string, object?>? arguments) in Calls())
        {
            CallToolResult answer = await client.CallToolAsync(tool, arguments);
            JsonElement structured = Assert.NotNull(answer.StructuredContent);
            Type shape = SchemaOf(tool);

            Assert.NotNull(listed.Single(listedTool => listedTool.Name == tool).ProtocolTool.OutputSchema);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(answer.Text()), JsonNode.Parse(structured.GetRawText())), $"{tool}: the structured content is not the object of the text");
            Assert.NotNull(JsonSerializer.Deserialize(structured, strict.GetTypeInfo(shape)));
        }
    }

    /// <summary>A refusal is an error result in text alone: it is not the shape of what the tool answers, and no client may check it against that.</summary>
    [Fact]
    public async Task Tools_Refusal_CarriesNoStructuredContent()
    {
        await using McpClient client = await StartedAsync();

        CallToolResult answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "missing.md" });

        Assert.True(answer.IsError);
        Assert.Null(answer.StructuredContent);
    }

    private static (string Tool, Dictionary<string, object?> Arguments)[] Calls() =>
    [
        ("read_file", new() { ["path"] = "notes.md" }),
        ("list_directory", []),
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
                new Dictionary<string, object?> { ["tool"] = "read_file", ["params"] = new Dictionary<string, object?> { ["path"] = "missing.txt" } }
            }
        })
    ];

    /// <summary>The type a tool's output schema is made from, as its attribute names it.</summary>
    private static Type SchemaOf(string tool) =>
        typeof(ToolResponse).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Single(attribute => attribute?.Name == tool)!
            .OutputSchemaType!;

    /// <summary>A client knows the server by the command a person types to start it.</summary>
    [Fact]
    public async Task Initialize_AsAProgram_NamesTheServerByItsCommand()
    {
        await using McpClient client = await StartedAsync();

        Assert.Equal("s3l-mcp-filesystem", client.ServerInfo?.Name);
    }

    /// <summary>
    /// The guard rules of every Snail server, on the list a model receives: names in snake case and listed once, the tool
    /// and every parameter described, read-only or destructive stated, every allowed value named, English.
    /// </summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using McpClient client = await StartedAsync();

        IList<McpClientTool> listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    private Task<McpClient> StartedAsync()
    {
        (string? command, string[]? arguments) = ServerProgram.Command("Snail.MCP.FileSystem", "SNAIL_FILESYSTEM_SERVER");

        return McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = arguments,
            EnvironmentVariables = new Dictionary<string, string?> { [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _root, [$"{ServerConfigExtensions.Prefix}CONFIG"] = ServerProgram.NoSettingsFile }
        }));
    }
}
