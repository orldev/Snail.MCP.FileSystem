using System.Text;
using Snail.MCP.FileSystem.Configuration;

namespace Snail.MCP.FileSystem.Application;

/// <summary>Everything the agent may do to the files of its working copy.</summary>
/// <remarks>
/// Every method starts at <see cref="SessionRoots.Reach"/> and works with the path it hands back, so
/// there is one place that decides what is reachable and no way to reach a file around it.
/// The set is deliberately small. Reading, writing, an exact replacement, a listing and two searches
/// are what work on a working copy is made of; everything larger belongs to a server that understands
/// the language, and everything destructive that is not a file — a folder of somebody else's work —
/// is not here at all.
/// </remarks>
public sealed class Files(SessionRoots roots, ServerConfig config)
{
    /// <summary>The text of a file, whole or by lines, with what was cut named.</summary>
    public Answer Read(string? path, int? from, int? to)
    {
        if (roots.Reach(path) is not { IsAllowed: true } reached)
            return Refused(path);

        if (!File.Exists(reached.Path))
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a file", "call list_directory to see what is there");

        if (new FileInfo(reached.Path!).Length > config.MaxFileBytes)
            return Answer.Failed($"'{Shown(reached.Path!)}' is larger than {config.MaxFileBytes} bytes", "read it by lines with from and to");

        var lines = File.ReadAllLines(reached.Path!);
        var first = Math.Max(1, from ?? 1);
        var last = Math.Min(lines.Length, to ?? lines.Length);
        var taken = first > last ? [] : lines[(first - 1)..last];
        var text = string.Join('\n', taken);
        var cut = text.Length > config.OutputLimitChars;

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["from"] = first,
            ["to"] = last,
            ["lines"] = lines.Length,
            ["text"] = cut ? text[..config.OutputLimitChars] : text,
            ["isCut"] = cut
        });
    }

    /// <summary>Writes a file whole, making the folders above it.</summary>
    public Answer Write(string? path, string? text)
    {
        if (!config.AllowWrite)
            return Answer.Failed("this server was started read-only", "a role that writes needs a server with writing turned on");

        if (roots.Reach(path) is not { IsAllowed: true } reached)
            return Refused(path);

        Directory.CreateDirectory(Path.GetDirectoryName(reached.Path!)!);

        var existed = File.Exists(reached.Path);
        File.WriteAllText(reached.Path!, text ?? string.Empty, new UTF8Encoding(false));

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["isCreated"] = !existed,
            ["bytes"] = new FileInfo(reached.Path!).Length
        });
    }

    /// <summary>Replaces an exact piece of a file, and says how many times it did.</summary>
    /// <remarks>
    /// Exact text rather than a pattern, and the whole file rather than a line number: a line number is
    /// stale the moment anything above it changes, and a pattern matches what nobody meant. A
    /// replacement that matches nothing is refused instead of written, because the caller believed the
    /// file said something it does not.
    /// </remarks>
    public Answer Edit(string? path, string? find, string? replace, bool all)
    {
        if (!config.AllowWrite)
            return Answer.Failed("this server was started read-only", "a role that writes needs a server with writing turned on");

        if (string.IsNullOrEmpty(find))
            return Answer.Failed("name the text to replace", "edit_file replaces exact text, so give enough of it to be unique");

        if (roots.Reach(path) is not { IsAllowed: true } reached)
            return Refused(path);

        if (!File.Exists(reached.Path))
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a file");

        var before = File.ReadAllText(reached.Path!);
        var found = Occurrences(before, find);

        if (found == 0)
            return Answer.Failed($"'{Shown(reached.Path!)}' does not contain that text", "read the file first; whitespace and line ends count");

        if (found > 1 && !all)
            return Answer.Failed($"that text appears {found} times in '{Shown(reached.Path!)}'", "give more of it, or pass all to replace every one");

        var after = all
            ? before.Replace(find, replace ?? string.Empty, StringComparison.Ordinal)
            : ReplaceFirst(before, find, replace ?? string.Empty);

        File.WriteAllText(reached.Path!, after, new UTF8Encoding(false));

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["replaced"] = all ? found : 1,
            ["bytes"] = new FileInfo(reached.Path!).Length
        });
    }

    /// <summary>What lies under a folder, to the depth asked for.</summary>
    public Answer List(string? path, int depth)
    {
        var wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
            return Refused(wanted);

        if (!Directory.Exists(reached.Path))
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a folder");

        var entries = Walk(reached.Path!, Math.Clamp(depth, 1, 8), reached.Path!).Take(config.MaxMatches).ToList();

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["entries"] = new JsonArray([.. entries.Select(entry => (JsonNode)new JsonObject
            {
                ["path"] = entry.Path,
                ["isDirectory"] = entry.IsDirectory,
                ["bytes"] = entry.Bytes
            })]),
            ["isCut"] = entries.Count == config.MaxMatches
        });
    }

    /// <summary>Files whose name matches a pattern.</summary>
    public Answer Find(string? pattern, string? path)
    {
        var wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
            return Refused(wanted);

        if (string.IsNullOrWhiteSpace(pattern))
            return Answer.Failed("name the pattern to match", "a pattern is a glob: *.cs, Program.*, **/Tests/*.cs");

        var found = Sources(reached.Path!)
            .Where(file => Matches(Path.GetRelativePath(reached.Path!, file), pattern.Trim()))
            .Take(config.MaxMatches)
            .Select(file => Path.GetRelativePath(reached.Path!, file))
            .ToList();

        return Answer.Ok(new JsonObject
        {
            ["pattern"] = pattern.Trim(),
            ["files"] = new JsonArray([.. found.Select(file => (JsonNode)JsonValue.Create(file))]),
            ["isCut"] = found.Count == config.MaxMatches
        });
    }

    /// <summary>Lines of files that contain a text.</summary>
    public Answer Search(string? query, string? path, string? pattern)
    {
        var wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
            return Refused(wanted);

        if (string.IsNullOrWhiteSpace(query))
            return Answer.Failed("name the text to search for");

        var matches = new List<TextMatch>();

        foreach (var file in Sources(reached.Path!))
        {
            if (matches.Count >= config.MaxMatches)
                break;

            if (pattern is { Length: > 0 } glob && !Matches(Path.GetRelativePath(reached.Path!, file), glob.Trim()))
                continue;

            if (new FileInfo(file).Length > config.MaxFileBytes)
                continue;

            var at = 0;

            foreach (var line in File.ReadLines(file))
            {
                at++;

                if (!line.Contains(query, StringComparison.Ordinal))
                    continue;

                matches.Add(new TextMatch(Path.GetRelativePath(reached.Path!, file), at, Trim(line)));

                if (matches.Count >= config.MaxMatches)
                    break;
            }
        }

        return Answer.Ok(new JsonObject
        {
            ["query"] = query,
            ["matches"] = new JsonArray([.. matches.Select(match => (JsonNode)new JsonObject
            {
                ["path"] = match.Path,
                ["line"] = match.Line,
                ["text"] = match.Text
            })]),
            ["isCut"] = matches.Count >= config.MaxMatches
        });
    }

    /// <summary>The root a call falls back to when it names no path: the first one this session was given.</summary>
    private string? Root()
    {
        var given = roots.All;

        return given.Count == 0 ? null : given[0];
    }

    private Answer Refused(string? path)
    {
        var reached = roots.Reach(path);

        return Answer.Failed(reached.Error!, reached.Hint);
    }

    /// <summary>The path as the caller should name it again: relative to its root where it has one.</summary>
    private string Shown(string full) =>
        roots.All.FirstOrDefault(root => full.StartsWith(root, StringComparison.Ordinal)) is { } root
            ? Path.GetRelativePath(root, full)
            : full;

    private IEnumerable<Entry> Walk(string folder, int depth, string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(folder).OrderBy(name => name, StringComparer.Ordinal))
        {
            if (config.Skip.Contains(Path.GetFileName(directory), StringComparer.Ordinal))
                continue;

            yield return new Entry(Path.GetRelativePath(root, directory), true, 0);

            if (depth <= 1)
                continue;

            foreach (var deeper in Walk(directory, depth - 1, root))
                yield return deeper;
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(name => name, StringComparer.Ordinal))
            yield return new Entry(Path.GetRelativePath(root, file), false, new FileInfo(file).Length);
    }

    /// <summary>Every file under a folder that is not inside one of the folders nobody means.</summary>
    private IEnumerable<string> Sources(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
            yield return file;

        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            if (config.Skip.Contains(Path.GetFileName(directory), StringComparer.Ordinal))
                continue;

            foreach (var deeper in Sources(directory))
                yield return deeper;
        }
    }

    /// <summary>Whether a relative path matches a glob; <c>**</c> crosses folders, <c>*</c> does not.</summary>
    private static bool Matches(string path, string pattern)
    {
        var text = path.Replace(Path.DirectorySeparatorChar, '/');
        var expression = new StringBuilder("^");

        for (var at = 0; at < pattern.Length; at++)
        {
            var letter = pattern[at];

            if (letter == '*' && at + 1 < pattern.Length && pattern[at + 1] == '*')
            {
                expression.Append(".*");
                at++;

                if (at + 1 < pattern.Length && pattern[at + 1] == '/')
                    at++;
            }
            else
            {
                expression.Append(letter switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => System.Text.RegularExpressions.Regex.Escape(letter.ToString())
                });
            }
        }

        expression.Append('$');

        return System.Text.RegularExpressions.Regex.IsMatch(text, expression.ToString(), System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2))
               || System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(text), expression.ToString(), System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2));
    }

    private static int Occurrences(string text, string what)
    {
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    private static string ReplaceFirst(string text, string what, string with)
    {
        var at = text.IndexOf(what, StringComparison.Ordinal);

        return text[..at] + with + text[(at + what.Length)..];
    }

    private static string Trim(string line) => line.Length <= 240 ? line : line[..240];
}
