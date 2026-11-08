using System.Text;
using System.Text.RegularExpressions;

namespace Snail.MCP.FileSystem.Application;

/// <summary>What the disk throws, answered as the refusal it is.</summary>
internal static class DiskRefusals
{
    /// <summary>An exception of the disk as the refusal it is, in the disk's own words and with what to do instead.</summary>
    /// <remarks>
    /// Thrown out of a tool, an exception would be answered by <c>ExplainFailures</c> as a failure nobody foresaw, type
    /// <c>internal</c> with "try once more" — the wrong advice for a file gone between listing and reading, a folder where
    /// a file was named, a permission. The disk failing is not impossible, so it is an answer.
    /// </remarks>
    public static Answer<JsonObject> Guarded(string? path, Func<Answer<JsonObject>> answer)
    {
        try
        {
            return answer();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Answer.Failed($"{Quoted(path)} could not be used: {exception.Message}", "the disk refused it; read the error, then try another path or ask whoever runs the server");
        }
        catch (EncoderFallbackException)
        {
            return Answer.Failed($"the text for {Quoted(path)} holds half of a character", "send whole characters; a surrogate pair is never split");
        }
        catch (RegexMatchTimeoutException)
        {
            return Answer.Failed("the pattern takes too long to match", "use fewer stars: *.cs, src/**/*.cs");
        }
    }

    private static string Quoted(string? path) => string.IsNullOrWhiteSpace(path) ? "the session's root" : $"'{path}'";
}
