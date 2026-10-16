using System.Text;
using System.Text.RegularExpressions;

namespace Snail.MCP.FileSystem.Application;

/// <summary>A pattern of paths: <c>*</c> stays inside one folder, <c>**</c> crosses them, <c>?</c> is one letter.</summary>
/// <remarks>
/// Built once for a call and matched against every path the call walks; it was built again for every file. A path
/// matches when its whole relative form does or its file name alone does, so <c>*.cs</c> finds a file in any folder.
/// <c>**/</c> is any number of whole folders: written as <c>.*</c> with the slash swallowed, <c>**/Tests/*.cs</c> also
/// matched <c>UnitTests/</c>.
/// </remarks>
internal sealed class Glob
{
    private readonly Regex _expression;

    private Glob(Regex expression) => _expression = expression;

    /// <summary>The pattern as one expression.</summary>
    public static Glob Of(string pattern)
    {
        var expression = new StringBuilder("^");

        for (int at = 0; at < pattern.Length; at++)
        {
            bool isDoubleStar = pattern[at] == '*' && at + 1 < pattern.Length && pattern[at + 1] == '*';
            bool isFolders = isDoubleStar && at + 2 < pattern.Length && pattern[at + 2] == '/';

            if (isFolders)
            {
                expression.Append("(?:.*/)?");
                at += 2;
            }
            else if (isDoubleStar)
            {
                expression.Append(".*");
                at++;
            }
            else
            {
                expression.Append(Piece(pattern[at]));
            }
        }

        expression.Append('$');

        return new Glob(new Regex(expression.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
    }

    /// <summary>Whether a path relative to the folder walked matches, as a whole or by its file name.</summary>
    public bool IsMatch(string relative)
    {
        string path = relative.Replace(Path.DirectorySeparatorChar, '/');

        return _expression.IsMatch(path) || _expression.IsMatch(Path.GetFileName(path));
    }

    private static string Piece(char letter) => letter switch
    {
        '*' => "[^/]*",
        '?' => "[^/]",
        _ => Regex.Escape(letter.ToString())
    };
}
