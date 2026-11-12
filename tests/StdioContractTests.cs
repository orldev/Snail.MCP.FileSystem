using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tests.Support;
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
                    new Dictionary<string, object?> { ["tool"] = "read_file", ["arguments"] = new Dictionary<string, object?> { ["path"] = "new.txt" } },
                    new Dictionary<string, object?> { ["tool"] = "find_files", ["arguments"] = new Dictionary<string, object?> { ["pattern"] = "*.txt" } }
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
    /// Every file tool states the shape of its answer as an output schema, and an answer that went through carries that shape
    /// as structured content: the very object of its text, with no field the schema does not name and none it requires
    /// missing. A client that validates structured content rejects an answer that does not fit, so a field added to an
    /// answer and not to its type fails here rather than at a client. Checked twice: against the listed schema, as a
    /// validating client reads it (Snail.Sdk's <c>OutputSchemas.Problems</c>), and against the record it was made from.
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

            Tool declared = listed.Single(listedTool => listedTool.Name == tool).ProtocolTool;

            Assert.NotNull(declared.OutputSchema);
            Assert.Empty(OutputSchemas.Problems(declared, answer));
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
    ];

    /// <summary>
    /// Every tool a session is given states an output schema — the server's promise, find_tool and run_tools included —
    /// so a client that validates structured content can hold every answer to one. run_tools runs only this server's tools,
    /// none of which reaches beyond the working copy, so it does not claim the open world either.
    /// </summary>
    [Fact]
    public async Task Tools_Listed_EveryOneStatesAnOutputSchema()
    {
        await using McpClient client = await StartedAsync();

        IList<McpClientTool> listed = await client.ListToolsAsync();

        Assert.All(listed, tool => Assert.True(tool.ProtocolTool.OutputSchema is not null, $"{tool.Name} states no output schema"));
        Assert.False(listed.Single(tool => tool.Name == "run_tools").ProtocolTool.Annotations?.OpenWorldHint);
    }

    /// <summary>find_tool and run_tools answer the whole envelope as their structured content, as the file tools do, and it fits the schema they list.</summary>
    [Fact]
    public async Task Machinery_AnswersThatWentThrough_FitTheirOutputSchema()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "первая строка\n");
        await using McpClient client = await StartedAsync();
        IList<McpClientTool> listed = await client.ListToolsAsync();

        CallToolResult found = await client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = "change one line in a file" });
        CallToolResult ran = await client.CallToolAsync("run_tools", new Dictionary<string, object?>
        {
            ["steps"] = new object[]
            {
                new Dictionary<string, object?> { ["tool"] = "read_file", ["arguments"] = new Dictionary<string, object?> { ["path"] = "notes.md" } },
                new Dictionary<string, object?> { ["tool"] = "find_files", ["arguments"] = new Dictionary<string, object?> { ["pattern"] = "*.md" } }
            }
        });

        foreach ((string tool, CallToolResult answer) in new[] { ("find_tool", found), ("run_tools", ran) })
        {
            JsonElement structured = Assert.NotNull(answer.StructuredContent);

            Assert.True(answer.IsOk(), answer.Text());
            Assert.Empty(OutputSchemas.Problems(listed.Single(listedTool => listedTool.Name == tool).ProtocolTool, answer));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(answer.Text()), JsonNode.Parse(structured.GetRawText())), $"{tool}: the structured content is not the object of the text");
        }
    }

    /// <summary>
    /// The fleet's machinery, Snail.Sdk's, in the envelope. A run that stops at a failed step is a failure, flagged and in
    /// text alone, carrying the steps before it as its data.
    /// </summary>
    [Fact]
    public async Task Machinery_OverStdio_AnswersTheEnvelope()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "первая строка\n");
        await using McpClient client = await StartedAsync();

        CallToolResult found = await client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = "change one line in a file" });
        CallToolResult stopped = await client.CallToolAsync("run_tools", new Dictionary<string, object?>
        {
            ["steps"] = new object[]
            {
                new Dictionary<string, object?> { ["tool"] = "read_file", ["arguments"] = new Dictionary<string, object?> { ["path"] = "notes.md" } },
                new Dictionary<string, object?> { ["tool"] = "read_file", ["arguments"] = new Dictionary<string, object?> { ["path"] = "missing.txt" } }
            }
        });

        Assert.True(found.IsOk(), found.Text());
        Assert.Equal("edit_file", found.Data()!["tools"]![0]!["name"]!.GetValue<string>());
        Assert.True(stopped.IsError);
        Assert.Null(stopped.StructuredContent);
        Assert.Contains("step 2 of 2 (read_file) failed", stopped.Error(), StringComparison.Ordinal);
        Assert.Equal(2, stopped.Data()!["ran"]!.GetValue<int>());
    }

    /// <summary>A refusal of the server's own names its kind at the top of the envelope, the word a program branches on, as the program answers it.</summary>
    [Fact]
    public async Task Refusals_OverStdio_CarryTheirType()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "log(); log();\n");
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        await using McpClient client = await StartedAsync();

        CallToolResult absent = await client.CallToolAsync("edit_file", new Dictionary<string, object?> { ["path"] = "notes.md", ["find"] = "trace()", ["replace"] = "x" });
        CallToolResult twice = await client.CallToolAsync("edit_file", new Dictionary<string, object?> { ["path"] = "notes.md", ["find"] = "log()", ["replace"] = "x" });
        CallToolResult folder = await client.CallToolAsync("write_file", new Dictionary<string, object?> { ["path"] = "src", ["text"] = "x" });
        CallToolResult lines = await client.CallToolAsync("search_text", new Dictionary<string, object?> { ["query"] = "one\ntwo" });

        Assert.Equal("noMatch", absent.Type());
        Assert.Equal("ambiguous", twice.Type());
        Assert.Equal(Reached.NotAFile, folder.Type());
        Assert.Equal(ToolFailures.Arguments, lines.Type());
    }

    /// <summary>The type a tool's output schema is made from, as its attribute names it.</summary>
    private static Type SchemaOf(string tool) =>
        typeof(FileTools).Assembly.GetTypes()
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

    /// <summary>
    /// The program refuses to open its keyless endpoint beyond loopback outside a container, and says which of the
    /// operator's variables asked for it, rather than serving the roots to the network: one line and exit code 1, no
    /// crash with a stack trace.
    /// </summary>
    [OutsideAContainerFact]
    public async Task Start_EveryInterfaceOutsideAContainer_StopsNamingTheVariable()
    {
        var environment = new Dictionary<string, string?>
        {
            [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _root,
            [$"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE"] = "true"
        };

        ServerExitedException exited = await Assert.ThrowsAsync<ServerExitedException>(() => HttpServerProgram.StartAsync("Snail.MCP.FileSystem", "SNAIL_FILESYSTEM_SERVER", environment, $"{ServerConfigExtensions.Prefix}PORT"));
        string stderr = exited.Stderr;

        Assert.Equal(1, exited.ExitCode);
        Assert.Contains($"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
    }

    /// <summary>A limit the server cannot run on stops it in one line naming the variable, with exit code 1 and no trace.</summary>
    [Fact]
    public async Task Start_StepLimitOfZero_RefusesTheStartInOneLine()
    {
        var environment = new Dictionary<string, string?>
        {
            [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _root,
            [$"{ServerConfigExtensions.Prefix}STEP_LIMIT"] = "0"
        };

        (int exitCode, string stderr) = await RefusedStart.RunAsync(environment);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"{ServerConfigExtensions.Prefix}STEP_LIMIT (or StepLimit in the settings file) is '0'; it is a number of steps above zero.",
            stderr.Trim());
    }

    /// <summary>
    /// A setting that is not even of its type stops the start in one line naming the variable, exit code 1, no trace: the
    /// settings are bound before there is a host, and that binding refuses instead of crashing with 134.
    /// </summary>
    [Fact]
    public async Task Start_StepLimitThatIsNoNumber_RefusesTheStartInOneLine()
    {
        var environment = new Dictionary<string, string?>
        {
            [$"{ServerConfigExtensions.Prefix}ROOTS__0"] = _root,
            [$"{ServerConfigExtensions.Prefix}STEP_LIMIT"] = "abc"
        };

        (int exitCode, string output) = await RefusedStart.RunAsync(environment);
        string stderr = output.Trim();

        Assert.Equal(1, exitCode);
        Assert.Contains($"{ServerConfigExtensions.Prefix}STEP_LIMIT", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', stderr);
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
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
