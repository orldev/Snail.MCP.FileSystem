using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>How an answer of this server reaches the caller.</summary>
/// <remarks>
/// Always one JSON object with an <c>ok</c> flag, success or not, because a model reads the shape it
/// was given last and a refusal shaped differently from an answer is a refusal it misreads as an
/// answer. The protocol's own error flag is set as well, for the client that never looks inside.
/// <para>
/// Every tool states the shape of what it answers as its output schema, and an answer that went through carries the same
/// object a second time as structured content, for the client that validates it or hands it on typed. A refusal is an
/// error result in text alone: it is not the shape of what the tool answers, and the protocol asks no structure of it.
/// </para>
/// </remarks>
public static class ToolResponse
{
    /// <summary>How an answer is written: compact, and with no letter escaped that JSON does not require escaping.</summary>
    /// <remarks>
    /// The default encoder escapes every letter outside ASCII and every <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c> and
    /// <c>+</c> for a page that might embed the text in HTML. No page does: the answer is read by a model, which pays for
    /// every escape. Measured on a line of C# with generics and Cyrillic, 59 characters took 154 on the wire, and Cyrillic
    /// alone took 5.6 times its length — against a platform that hands a model 6000 characters of an answer.
    /// </remarks>
    private static readonly JsonSerializerOptions Wire = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The answer as the one JSON text block every call returns.</summary>
    public static CallToolResult From(Answer answer)
    {
        var envelope = new JsonObject
        {
            ["ok"] = answer.IsSuccess,
            ["data"] = answer.Data?.DeepClone(),
            ["error"] = answer.Error,
            ["hint"] = answer.Hint
        };

        return answer.IsSuccess ? Json(envelope) : new() { IsError = true, Content = [new TextContentBlock { Text = envelope.ToJsonString(Wire) }] };
    }

    /// <summary>Data as a successful answer.</summary>
    public static CallToolResult Success(JsonNode data) => From(Answer.Ok(data));

    /// <summary>An answer that went through, as text and as the structured content its tool's output schema describes.</summary>
    public static CallToolResult Json(JsonObject answer)
    {
        string text = answer.ToJsonString(Wire);

        return new() { Content = [new TextContentBlock { Text = text }], StructuredContent = Element(text) };
    }

    /// <summary>The text parsed once more, so the structured content is the very object the text spells.</summary>
    private static JsonElement Element(string text)
    {
        using var document = JsonDocument.Parse(text);

        return document.RootElement.Clone();
    }
}
