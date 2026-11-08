using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tests.Support;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>What this server promises outside: names, descriptions and the price they cost every session.</summary>
public sealed class ToolContractTests
{
    private static readonly MethodInfo[] Tools =
    [
        .. typeof(FileTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    [Fact]
    public void Tools_Names_AreWrittenTheProtocolsWay() =>
        Assert.All(Tools, tool => Assert.Matches("^[a-z][a-z0-9_]*$", tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!));

    [Fact]
    public void Tools_Descriptions_SayWhatEachIsFor() =>
        Assert.All(Tools, tool =>
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description), tool.Name));

    /// <summary>A parameter whose meaning the model cannot see is filled with a guess.</summary>
    [Fact]
    public void Tools_ParametersTheModelFills_AreDescribed() =>
        Assert.All(
            Tools.SelectMany(tool => tool.GetParameters()).Where(IsAsked),
            parameter => Assert.False(
                string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<DescriptionAttribute>()?.Description),
                parameter.Name));

    /// <summary>The cancellation token and the request itself are filled by the SDK: they are not in the schema, and nobody needs them described.</summary>
    private static bool IsAsked(ParameterInfo parameter) =>
        parameter.ParameterType != typeof(CancellationToken)
        && parameter.ParameterType != typeof(RequestContext<CallToolRequestParams>);

    [Fact]
    public void Tools_DeclaredSurface_IsExactlyWhatTheAssemblyOffers()
    {
        string[] declared =
        [
            "edit_file", "find_files", "list_directory", "read_file", "search_text", "write_file"
        ];

        string[] actual =
        [
            .. Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal)
        ];

        Assert.Equal(declared, actual);
    }

    /// <summary>
    /// Descriptions are a standing price: a client carries them in every request, whether it uses the tool or not. The
    /// ceiling stands here so that an added sentence is a decision rather than an accident. Measured on the list a session
    /// receives, Snail.Sdk's find_tool and run_tools included: the session pays for theirs as for the server's own.
    /// </summary>
    [Fact]
    public async Task Descriptions_StandingCharge_StaysUnderItsCeiling()
    {
        const int ceiling = 3_000;

        IList<McpClientTool> listed = await ListedAsync();
        int charge = listed.Sum(tool => (tool.ProtocolTool.Description?.Length ?? 0) + Described(tool.ProtocolTool.InputSchema));

        Assert.True(
            charge < ceiling,
            $"The descriptions are {charge} characters against a ceiling of {ceiling}. Shorten what was added, or "
            + "raise the ceiling deliberately and say what the caller gets for the context it costs.");
    }

    /// <summary>Every tool a session is given, the server's and Snail.Sdk's, and nothing else.</summary>
    [Fact]
    public async Task Tools_Listed_AreTheFileToolsAndTheFleetsMachinery()
    {
        string[] expected =
        [
            "edit_file", "find_files", "find_tool", "list_directory", "read_file", "run_tools", "search_text", "write_file"
        ];

        IList<McpClientTool> listed = await ListedAsync();

        Assert.Equal(expected, listed.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The mechanics shared by the whole fleet are named alike by every server.</summary>
    /// <remarks>
    /// find_tool, run_tools and run_program are not about files but about how ANY server is asked what it can do, and how
    /// several of its tools are spent in one call. The platform calls them as "server.name", and a mismatch would mean the
    /// same question asked of three servers in three ways. They are Snail.Sdk's now; checked here on the list a session
    /// receives, because the name of a tool is part of the contract outside, not a detail.
    /// </remarks>
    [Fact]
    public async Task Machinery_OfTheFleet_IsSpelledAlike()
    {
        string[] machinery = ["find_tool", "run_program", "run_tools"];

        var names = (await ListedAsync()).Select(tool => tool.Name).ToList();

        Assert.NotEmpty(names.Intersect(machinery));
        Assert.DoesNotContain(names, name => machinery.Any(known => name.EndsWith("_" + known, StringComparison.Ordinal)));
    }

    private static async Task<IList<McpClientTool>> ListedAsync()
    {
        using var copy = new Copy();
        await using InProcessServer server = await Listening.StartedAsync(copy.Config);
        await using McpClient client = await server.SessionAsync();

        return await client.ListToolsAsync();
    }

    /// <summary>The characters of every description in an input schema, nested ones included.</summary>
    private static int Described(JsonElement schema) =>
        schema.ValueKind switch
        {
            JsonValueKind.Object => schema.EnumerateObject().Sum(property =>
                property.Name == "description" && property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()!.Length
                    : Described(property.Value)),
            JsonValueKind.Array => schema.EnumerateArray().Sum(Described),
            _ => 0
        };
}
