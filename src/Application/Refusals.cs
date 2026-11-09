using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Application;

/// <summary>The kinds of the server's own refusals: the <c>type</c> of the answer, a word a program branches on.</summary>
/// <remarks>
/// Snail.Sdk's words where one fits — <see cref="Reached"/>'s for a path (<c>outside</c>, <c>absent</c>, <c>notAFile</c>,
/// <c>unreadable</c> and the rest), <see cref="ToolFailures.Arguments"/> for a call the tool cannot take — and a word of the server's
/// own for the rest, one per kind of failure. The error is prose for a person; a program that matched it broke the first
/// time a sentence was reworded.
/// </remarks>
public static class Refusals
{
    /// <summary>A write or an edit on a server started without writing.</summary>
    public const string ReadOnly = "readOnly";

    /// <summary>A file past <c>MaxFileBytes</c>, read whole or edited.</summary>
    public const string TooLarge = "tooLarge";

    /// <summary>An edit whose text the file does not contain.</summary>
    public const string NoMatch = "noMatch";

    /// <summary>An edit whose text the file contains more than once, without <c>all</c>.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>A replacement holding letters the bytes of a file that is not UTF-8 cannot hold.</summary>
    public const string Unencodable = "unencodable";

    /// <summary>A pattern that backtracks past its time against a name.</summary>
    public const string TooComplex = "tooComplex";

    /// <summary>The disk refused a write or an edit: full, a file this server may not change, a share that did not answer.</summary>
    /// <remarks>A read the disk refused is Snail.Sdk's <see cref="Reached.Unreadable"/>, and a file gone under the call <see cref="Reached.Absent"/>.</remarks>
    public const string Unwritable = "unwritable";
}
