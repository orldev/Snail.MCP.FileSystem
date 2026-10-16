using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Configuration;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Tools;

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
/// It does not widen the reach of a grant. A step is invoked on the tool object itself, past the call filter that checks
/// the grant at the door, so the grant is checked again for every step: a role given <c>read_file</c> and
/// <c>run_tools</c> wrote files through it until it was.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class SequenceTools(IEnumerable<McpServerTool> tools, SessionGrant grant, ServerConfig config)
{
    private const string Name = "run_tools";

    /// <summary>Several tools of this server in one call, in order, stopping at the first failure.</summary>
    [McpServerTool(Name = Name, Destructive = true)]
    [Description(ToolDescriptions.RunTools)]
    public async Task<CallToolResult> RunToolsAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.RunToolsSteps)] ToolStep[] steps,
        CancellationToken cancellationToken = default) =>
        ToolResponse.Json(await RunAsync(context, steps, cancellationToken));

    private async Task<JsonObject> RunAsync(RequestContext<CallToolRequestParams> context, ToolStep[] steps, CancellationToken cancellationToken)
    {
        if (steps.Length == 0)
        {
            return Stopped([], 0, "name the steps to run", "a step is {\"tool\": \"read_file\", \"params\": {\"path\": \"src/App.cs\"}}");
        }

        int limit = Math.Max(1, config.StepLimit);

        if (steps.Length > limit)
        {
            return Stopped([], 0, $"a run takes at most {limit} steps, not {steps.Length}", "split the work into several calls");
        }

        var results = new JsonArray();
        int budget = config.OutputLimitChars;
        int omitted = 0;

        for (int at = 0; at < steps.Length; at++)
        {
            if (Named(steps[at]) is not { } tool)
            {
                return Stopped(results, omitted,
                    $"step {at + 1} does not name a tool of this server",
                    "call find_tool to see what this server has");
            }

            if (Refusal(tool) is { } refusal)
            {
                return Stopped(results, omitted, $"step {at + 1}: {refusal}", null);
            }

            CallToolResult result = await Invoke(context, tool, steps[at].Params, cancellationToken);
            string text = Text(result);

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

    /// <summary>Why a step may not run, or null when it may: the grant of the session holds here as it does at the door.</summary>
    /// <remarks>A run inside a run is refused: twenty steps of twenty steps is four hundred calls from one.</remarks>
    private string? Refusal(McpServerTool tool)
    {
        string name = tool.ProtocolTool.Name;

        if (string.Equals(name, Name, StringComparison.Ordinal))
        {
            return "run_tools does not run itself";
        }

        return grant.Allows(name) ? null : grant.Refusal(name);
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
