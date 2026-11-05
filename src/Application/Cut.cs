namespace Snail.MCP.FileSystem.Application;

/// <summary>Text cut for an answer without splitting a character.</summary>
internal static class Cut
{
    /// <summary>The text cut at a length, one character sooner when the length falls between the two halves of a surrogate pair.</summary>
    public static string Whole(string text, int length) =>
        char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
}
