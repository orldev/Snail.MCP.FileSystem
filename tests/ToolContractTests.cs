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
    public void Index_AgainstTheAssembly_HoldsEveryTool() =>
        Assert.Equal(
            Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal),
            ToolIndex.All.Select(entry => entry.Name).Order(StringComparer.Ordinal));

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
    public void Descriptions_StandingCharge_StaysUnderItsCeiling()
    {
        const int ceiling = 3_000;

        int charge = Tools.Sum(ToolCharge);

        Assert.True(
            charge < ceiling,
            $"The descriptions are {charge} characters against a ceiling of {ceiling}. Shorten what was added, or "
            + "raise the ceiling deliberately and say what the caller gets for the context it costs.");
    }

    /// <summary>The characters a tool's descriptions cost every session: its own and its parameters'.</summary>
    private static int ToolCharge(MethodInfo tool) =>
        (tool.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0) + tool.GetParameters().Sum(ParameterCharge);

    private static int ParameterCharge(ParameterInfo parameter) => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description?.Length ?? 0;

    /// <summary>The mechanics shared by the whole fleet are named alike by all three servers.</summary>
    /// <remarks>
    /// find_tool, run_tools and run_program are not about files but about how ANY server is asked what it can do, and how
    /// several of its tools are spent in one call. The platform calls them as "server.name", and a mismatch would mean the
    /// same question asked of three servers in three ways. Checked here, because the name of a tool is part of the
    /// contract outside, not a detail.
    /// </remarks>
    [Fact]
    public void Machinery_OfTheFleet_IsSpelledAlike()
    {
        string[] machinery = ["find_tool", "run_program", "run_tools"];

        var names = Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).ToList();

        Assert.NotEmpty(names.Intersect(machinery));
        Assert.DoesNotContain(names, name => machinery.Any(known => name.EndsWith("_" + known, StringComparison.Ordinal)));
    }
}
