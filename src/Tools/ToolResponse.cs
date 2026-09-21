using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>How an answer of this server reaches the caller.</summary>
/// <remarks>
/// Always one JSON object with an <c>ok</c> flag, success or not, because a model reads the shape it
/// was given last and a refusal shaped differently from an answer is a refusal it misreads as an
/// answer. The protocol's own error flag is set as well, for the client that never looks inside.
/// </remarks>
public static class ToolResponse
{
    private static readonly JsonSerializerOptions Wire = new() { WriteIndented = false };

    public static CallToolResult From(Answer answer) =>
        new()
        {
            IsError = !answer.IsSuccess,
            Content =
            [
                new TextContentBlock
                {
                    Text = new JsonObject
                    {
                        ["ok"] = answer.IsSuccess,
                        ["data"] = answer.Data?.DeepClone(),
                        ["error"] = answer.Error,
                        ["hint"] = answer.Hint
                    }.ToJsonString(Wire)
                }
            ]
        };

    public static CallToolResult Success(JsonNode data) => From(Answer.Ok(data));
}
