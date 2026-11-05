using System.Text;
using System.Text.RegularExpressions;

namespace Snail.MCP.FileSystem.Application;

/// <summary>What the disk throws, answered as the refusal it is.</summary>
internal static class DiskRefusals
{
    /// <summary>An exception of the disk as the refusal it is, since every answer of this server is its own JSON.</summary>
    /// <remarks>
    /// Thrown out of a tool, an exception reached the model as the SDK's "An error occurred invoking 'read_file'." — no
    /// reason and no hint, in a shape the model had never seen from this server. The disk failing is not impossible, so
    /// it is an answer: a file gone between listing and reading, a folder where a file was named, a permission.
    /// </remarks>
    public static Answer Guarded(string? path, Func<Answer> answer)
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
