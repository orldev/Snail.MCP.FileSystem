using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>What this server promises outside: names, descriptions and the price they cost every session.</summary>
public sealed class ToolContractTests
{
    private static readonly MethodInfo[] Tools =
    [
        .. typeof(ToolIndex).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    /// <summary>The index names the tool classes one by one, for the native build; a class missing there would vanish from find_tool.</summary>
    [Fact]
    public void The_index_holds_every_tool_there_is() =>
        Assert.Equal(
            Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal),
            ToolIndex.All.Select(entry => entry.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void Every_tool_is_named_the_way_a_protocol_name_is_written() =>
        Assert.All(Tools, tool => Assert.Matches("^[a-z][a-z0-9_]*$", tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!));

    [Fact]
    public void Every_tool_says_what_it_is_for() =>
        Assert.All(Tools, tool =>
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description), tool.Name));

    /// <summary>A parameter whose meaning the model cannot see is filled with a guess.</summary>
    [Fact]
    public void Every_parameter_the_model_fills_is_described() =>
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
    public void The_declared_surface_is_exactly_what_the_assembly_offers()
    {
        string[] declared =
        [
            "edit_file", "find_files", "find_tool", "list_directory", "read_file", "run_tools", "search_text", "write_file"
        ];

        string[] actual =
        [
            .. Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal)
        ];

        Assert.Equal(declared, actual);
    }

    /// <summary>
    /// Descriptions are a standing price: a client carries them in every request, whether it uses the tool or not. The
    /// ceiling stands here so that an added sentence is a decision rather than an accident.
    /// </summary>
    [Fact]
    public void The_standing_charge_of_the_descriptions_stays_under_its_ceiling()
    {
        const int ceiling = 3_000;

        var charge = Tools.Sum(tool =>
            (tool.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0)
            + tool.GetParameters().Sum(parameter => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0));

        Assert.True(
            charge < ceiling,
            $"The descriptions are {charge} characters against a ceiling of {ceiling}. Shorten what was added, or "
            + "raise the ceiling deliberately and say what the caller gets for the context it costs.");
    }

    /// <summary>The mechanics shared by the whole fleet are named alike by all three servers.</summary>
    /// <remarks>
    /// find_tool, run_tools and run_program are not about files but about how ANY server is asked what it can do, and how
    /// several of its tools are spent in one call. The platform calls them as "server.name", and a mismatch would mean the
    /// same question asked of three servers in three ways. Checked here, because the name of a tool is part of the
    /// contract outside, not a detail.
    /// </remarks>
    [Fact]
    public void The_machinery_of_the_fleet_is_spelled_the_same_way_everywhere()
    {
        string[] machinery = ["find_tool", "run_program", "run_tools"];

        var names = Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).ToList();

        Assert.NotEmpty(names.Intersect(machinery));
        Assert.DoesNotContain(names, name => machinery.Any(known => name.EndsWith("_" + known, StringComparison.Ordinal)));
    }
}
