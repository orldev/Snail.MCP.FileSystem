using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>Что этот сервер обещает наружу: имена, описания и цена, которую они стоят каждому сеансу.</summary>
public sealed class ToolContractTests
{
    private static readonly MethodInfo[] Tools =
    [
        .. typeof(ToolIndex).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    [Fact]
    public void Every_tool_is_named_the_way_a_protocol_name_is_written() =>
        Assert.All(Tools, tool => Assert.Matches("^[a-z][a-z0-9_]*$", tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!));

    [Fact]
    public void Every_tool_says_what_it_is_for() =>
        Assert.All(Tools, tool =>
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description), tool.Name));

    /// <summary>Параметр, смысла которого модель не видит, она заполняет догадкой.</summary>
    [Fact]
    public void Every_parameter_the_model_fills_is_described() =>
        Assert.All(
            Tools.SelectMany(tool => tool.GetParameters()).Where(Asked),
            parameter => Assert.False(
                string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<DescriptionAttribute>()?.Description),
                parameter.Name));

    /// <summary>Токен отмены и сам запрос подставляет SDK: в схеме их нет, и описывать их некому.</summary>
    private static bool Asked(ParameterInfo parameter) =>
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
    /// Описания — постоянная плата: клиент везёт их в каждом обращении, пользуется он инструментом или
    /// нет. Потолок стоит здесь, чтобы добавленное предложение было решением, а не случайностью.
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

    /// <summary>Общая для всего флота механика зовётся одинаково у всех трёх серверов.</summary>
    /// <remarks>
    /// find_tool, run_tools и run_program — это не про файлы, а про то, как у ЛЮБОГО сервера спрашивают,
    /// что он умеет, и как тратят несколько его инструментов за один вызов. Платформа зовёт их как
    /// «сервер.имя», и разнобой означал бы, что один и тот же вопрос задают трём серверам тремя способами.
    /// Проверяется здесь, потому что имя инструмента — часть договора наружу, а не деталь.
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
