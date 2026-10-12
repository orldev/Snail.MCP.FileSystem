using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Configuration;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>One step of a run: the tool to call and the arguments to call it with.</summary>
public sealed record ToolStep(string? Tool, JsonObject? Params);

/// <summary>Reaching the other tools of this server in one call instead of one round trip each.</summary>
/// <remarks>
/// Work on a solution is rarely one question: load it, ask for the errors, read the file they point at.
/// Each of those is a round trip through the model, and the answer of each rides back into the window
/// whether the step needed to read it or not.
/// <para>
/// The steps are the server's own tools, invoked through the same objects the protocol invokes — not a
/// second catalog beside them. A tool added tomorrow is a step today, its arguments are checked by the
/// same schema, and nothing here can ask for something no tool offers.
/// </para>
/// <para>
/// What it does widen is the reach of one grant: a role given <c>run_tools</c> can call every tool of
/// this server, including the ones it was not given. The server's own boundary — the session's roots —
/// holds all the same, but a deployment that hands out tools one by one should know that this one is
/// not one of those.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class SequenceTools(IEnumerable<McpServerTool> tools, ServerConfig config)
{
    [McpServerTool(Name = "run_tools")]
    [Description(ToolDescriptions.RunTools)]
    public async Task<JsonObject> RunToolsAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.RunToolsSteps)] ToolStep[] steps,
        CancellationToken cancellationToken = default)
    {
        if (steps.Length == 0)
            return Stopped([], 0, "name the steps to run", "a step is {\"tool\": \"get_diagnostics\", \"params\": {\"solutionId\": \"…\"}}");

        var limit = Math.Max(1, config.StepLimit);

        if (steps.Length > limit)
            return Stopped([], 0, $"a run takes at most {limit} steps, not {steps.Length}", "split the work into several calls");

        var results = new JsonArray();
        var budget = config.OutputLimitChars;
        var omitted = 0;

        for (var at = 0; at < steps.Length; at++)
        {
            if (Named(steps[at]) is not { } tool)
            {
                return Stopped(results, omitted,
                    $"step {at + 1} does not name a tool of this server",
                    "call find_tool to see what this server has");
            }

            var result = await Invoke(context, tool, steps[at].Params, cancellationToken);
            var text = Text(result);

            if (result.IsError is true)
            {
                results.Add((JsonNode)new JsonObject { ["tool"] = tool.ProtocolTool.Name, ["ok"] = false, ["error"] = text });

                return Stopped(results, omitted, $"step {at + 1} of {steps.Length} ({tool.ProtocolTool.Name}) failed: {text}", null);
            }

            if (text.Length <= budget)
            {
                budget -= text.Length;
                results.Add((JsonNode)new JsonObject { ["tool"] = tool.ProtocolTool.Name, ["ok"] = true, ["data"] = Parsed(text) });
            }
            else
            {
                omitted++;
                results.Add((JsonNode)new JsonObject { ["tool"] = tool.ProtocolTool.Name, ["ok"] = true, ["isOmitted"] = true });
            }
        }

        return Record(results, omitted, true, null, null);
    }

    /// <summary>The tool a step names, or null when this server has no such tool.</summary>
    private McpServerTool? Named(ToolStep step) =>
        string.IsNullOrWhiteSpace(step.Tool)
            ? null
            : tools.FirstOrDefault(tool => string.Equals(tool.ProtocolTool.Name, step.Tool.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// Runs one step as the protocol would run it: the same tool object, the same argument binding, the
    /// same session behind it.
    /// </summary>
    private static ValueTask<CallToolResult> Invoke(
        RequestContext<CallToolRequestParams> context,
        McpServerTool tool,
        JsonObject? arguments,
        CancellationToken cancellationToken) =>
        tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(
                context.Server,
                context.JsonRpcRequest,
                new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = Given(arguments) })
            {
                MatchedPrimitive = tool
            },
            cancellationToken);

    private static IDictionary<string, JsonElement>? Given(JsonObject? arguments) =>
        arguments?.ToDictionary(
            pair => pair.Key,
            pair => JsonElement.Parse(pair.Value?.ToJsonString() ?? "null"),
            StringComparer.Ordinal);

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    /// <summary>The step's answer as the object it was, or as text when it was never JSON.</summary>
    private static JsonNode? Parsed(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    private static JsonObject Stopped(JsonArray results, int omitted, string error, string? hint) =>
        Record(results, omitted, false, error, hint);

    private static JsonObject Record(JsonArray results, int omitted, bool ok, string? error, string? hint) => new()
    {
        ["ok"] = ok,
        ["ran"] = results.Count,
        ["steps"] = results,
        ["omitted"] = omitted,
        ["note"] = omitted == 0 ? null : $"{omitted} answer(s) were left out to stay inside the output budget; call those tools on their own",
        ["error"] = error,
        ["hint"] = hint
    };
}
