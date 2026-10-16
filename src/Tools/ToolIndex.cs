using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Application;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>Every tool of this server as an entry: what it is called, what it takes, what it is for.</summary>
/// <remarks>
/// Read from the tools themselves rather than written a second time beside them, because a catalog kept
/// by hand is wrong by the second release. The words a caller searches by live here and are read only
/// when someone searches, instead of riding in every description of every session.
/// </remarks>
public sealed class ToolIndex
{
    /// <summary>Every tool, by the class that declares it and then in declaration order.</summary>
    public static readonly IReadOnlyList<ToolEntry> All = Read();

    private static readonly Dictionary<string, string> Meanings = new(StringComparer.Ordinal)
    {
        ["read_file"] = "open show contents source text lines look at",
        ["write_file"] = "create save new whole overwrite put",
        ["edit_file"] = "change one line replace part patch modify fix",
        ["list_directory"] = "tree folder what is here ls dir contents",
        ["find_files"] = "which files glob name pattern where is the file",
        ["search_text"] = "grep contains occurrences string mentions where is",
        ["run_tools"] = "several batch sequence steps together once round trip",
        ["find_tool"] = "search discover which tool name parameters how do i"
    };

    /// <summary>The tools that answer a question, best first; every one of them when none does.</summary>
    public IReadOnlyList<ToolEntry> Find(string? task, int limit)
    {
        IEnumerable<(ToolEntry Entry, double Score)> answering = All
            .Select(entry => (Entry: entry, Score: Vocabulary.Score(task ?? string.Empty, entry.Name, Meant(entry.Name), entry.Description)))
            .Where(scored => scored.Score > 0);

        IEnumerable<(ToolEntry Entry, double Score)> best = answering
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Entry.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, All.Count));

        return [.. best.Select(scored => scored.Entry)];
    }

    /// <summary>The answer <c>find_tool</c> gives: what matched, or the whole catalog when nothing did.</summary>
    public JsonObject Answer(string? task, int limit)
    {
        IReadOnlyList<ToolEntry> found = Find(task, limit);

        return new JsonObject
        {
            ["task"] = task,
            ["isMatched"] = found.Count > 0,
            ["note"] = found.Count > 0 ? null : "nothing in this server's words matches; these are all of its tools",
            ["tools"] = new JsonArray([.. (found.Count > 0 ? found : All).Select(Describe)])
        };
    }

    /// <summary>The other words this tool is asked for by, or none when the index knows no others.</summary>
    public static string? Meant(string name) => Meanings.GetValueOrDefault(name);

    /// <summary>The tools the index knows other words for; a guard test keeps these the tools that exist.</summary>
    public static IEnumerable<string> Meant() => Meanings.Keys;

    private static JsonNode Describe(ToolEntry entry) => new JsonObject
    {
        ["name"] = entry.Name,
        ["description"] = entry.Description,
        ["parameters"] = new JsonArray([.. entry.Parameters.Select(parameter => (JsonNode)new JsonObject
        {
            ["name"] = parameter.Name,
            ["type"] = parameter.Type,
            ["isRequired"] = parameter.IsRequired,
            ["description"] = parameter.Description
        })])
    };

    /// <remarks>
    /// The classes are named rather than looked for in the assembly: a native build keeps the methods of a type it is
    /// told about and nothing it would have to find. A guard test keeps the list every tool class there is.
    /// </remarks>
    private static IReadOnlyList<ToolEntry> Read() =>
    [
        .. Of(typeof(DiscoveryTools)),
        .. Of(typeof(FileTools)),
        .. Of(typeof(SequenceTools))
    ];

    private static IEnumerable<ToolEntry> Of([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(Entry);

    private static ToolEntry Entry(MethodInfo tool) => new(
        tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!,
        tool.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
        [.. tool.GetParameters().Where(IsFillable).Select(Argument)]);

    /// <summary>The parameters the model fills; the request and the cancellation token are the SDK's to fill.</summary>
    private static bool IsFillable(ParameterInfo parameter) =>
        parameter.ParameterType != typeof(CancellationToken)
        && parameter.ParameterType != typeof(McpServer)
        && parameter.ParameterType != typeof(RequestContext<CallToolRequestParams>);

    private static ToolArgument Argument(ParameterInfo parameter) => new(
        parameter.Name!,
        Shape(parameter.ParameterType),
        !parameter.HasDefaultValue,
        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty);

    /// <summary>The JSON shape of a parameter, as a caller filling it has to write it.</summary>
    private static string Shape(Type type) =>
        Nullable.GetUnderlyingType(type) is { } inner ? Shape(inner)
        : type == typeof(string) ? "string"
        : type == typeof(bool) ? "boolean"
        : type == typeof(int) ? "integer"
        : type == typeof(ToolStep[]) ? "step[]"
        : "object";
}
