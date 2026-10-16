using System.Text;
using System.Text.Unicode;
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
public sealed class Files(SessionRoots roots, ServerConfig config, IWorkingCopy disk)
{
    /// <summary>UTF-8 that refuses what is not UTF-8, rather than turning it into U+FFFD.</summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Latin-1, which maps every byte to one character and back: a file that is not UTF-8 is edited byte for byte.</summary>
    private static readonly Encoding Bytewise = Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static readonly object[] Stripes = [.. Enumerable.Range(0, 64).Select(_ => new object())];

    /// <summary>The text of a file, whole or by lines, with what was cut named.</summary>
    public Answer Read(string? path, int? from, int? to) => Guarded(path, () => Reading(path, from, to));

    /// <summary>Writes a file whole, making the folders above it.</summary>
    public Answer Write(string? path, string? text) => Guarded(path, () => Writing(path, text));

    /// <summary>Replaces an exact piece of a file, and says how many times it did.</summary>
    public Answer Edit(string? path, string? find, string? replace, bool all) => Guarded(path, () => Editing(path, find, replace, all));

    /// <summary>What lies under a folder, to the depth asked for.</summary>
    public Answer List(string? path, int depth, CancellationToken cancellationToken = default) =>
        Guarded(path, () => Listing(path, depth, cancellationToken));

    /// <summary>Files whose name matches a pattern.</summary>
    public Answer Find(string? pattern, string? path, CancellationToken cancellationToken = default) =>
        Guarded(path, () => Finding(pattern, path, cancellationToken));

    /// <summary>Lines of files that contain a text.</summary>
    public Answer Search(string? query, string? path, string? pattern, CancellationToken cancellationToken = default) =>
        Guarded(path, () => Searching(query, path, pattern, cancellationToken));

    /// <summary>An exception of the disk as the refusal it is, since every answer of this server is its own JSON.</summary>
    /// <remarks>
    /// Thrown out of a tool, an exception reached the model as the SDK's "An error occurred invoking 'read_file'." — no
    /// reason and no hint, in a shape the model had never seen from this server. The disk failing is not impossible, so
    /// it is an answer: a file gone between listing and reading, a folder where a file was named, a permission.
    /// </remarks>
    private static Answer Guarded(string? path, Func<Answer> answer)
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
    }

    private static string Quoted(string? path) => string.IsNullOrWhiteSpace(path) ? "the session's root" : $"'{path}'";

    private Answer Reading(string? path, int? from, int? to)
    {
        if (roots.Reach(path) is not { IsAllowed: true } reached)
        {
            return Refused(path);
        }

        if (!disk.IsFile(reached.Path!))
        {
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a file", "call list_directory to see what is there");
        }

        bool isWhole = from is null && to is null;

        if (isWhole && disk.Length(reached.Path!) > config.MaxFileBytes)
        {
            return Answer.Failed($"'{Shown(reached.Path!)}' is larger than {config.MaxFileBytes} bytes", "read it by lines with from and to");
        }

        int first = Math.Max(1, from ?? 1);
        var window = LineWindow.Of(disk, reached.Path!, first, to ?? int.MaxValue, config.OutputLimitChars);
        string text = string.Join('\n', window.Taken);
        bool cut = text.Length > config.OutputLimitChars;

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["from"] = first,
            ["to"] = Math.Min(window.Lines, to ?? window.Lines),
            ["lines"] = window.Lines,
            ["text"] = cut ? Whole(text, config.OutputLimitChars) : text,
            ["isCut"] = cut
        });
    }

    /// <summary>The text cut at a length, one character sooner when the length falls between the two halves of a surrogate pair.</summary>
    private static string Whole(string text, int length) =>
        char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];

    /// <summary>The line cut at its length for an answer, on a whole character.</summary>
    private static string Trim(string line) => line.Length <= 240 ? line : Whole(line, 240);

    private Answer Writing(string? path, string? text)
    {
        if (!config.AllowWrite)
        {
            return Answer.Failed("this server was started read-only", "a role that writes needs a server with writing turned on");
        }

        if (roots.Reach(path) is not { IsAllowed: true } reached)
        {
            return Refused(path);
        }

        if (disk.IsFolder(reached.Path!))
        {
            return Answer.Failed($"'{Shown(reached.Path!)}' is a folder", "name a file inside it");
        }

        disk.MakeFolder(Path.GetDirectoryName(reached.Path!)!);

        bool existed = disk.IsFile(reached.Path!);

        lock (Stripe(reached.Path!))
        {
            disk.Replace(reached.Path!, Strict.GetBytes(text ?? string.Empty));
        }

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["isCreated"] = !existed,
            ["bytes"] = disk.Length(reached.Path!)
        });
    }

    /// <remarks>
    /// Exact text rather than a pattern, and the whole file rather than a line number: a line number is
    /// stale the moment anything above it changes, and a pattern matches what nobody meant. A
    /// replacement that matches nothing is refused instead of written, because the caller believed the
    /// file said something it does not.
    /// </remarks>
    private Answer Editing(string? path, string? find, string? replace, bool all)
    {
        if (!config.AllowWrite)
        {
            return Answer.Failed("this server was started read-only", "a role that writes needs a server with writing turned on");
        }

        if (string.IsNullOrEmpty(find))
        {
            return Answer.Failed("name the text to replace", "edit_file replaces exact text, so give enough of it to be unique");
        }

        if (roots.Reach(path) is not { IsAllowed: true } reached)
        {
            return Refused(path);
        }

        if (!disk.IsFile(reached.Path!))
        {
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a file");
        }

        lock (Stripe(reached.Path!))
        {
            return Replaced(reached.Path!, find, replace ?? string.Empty, all);
        }
    }

    /// <summary>The replacement made in the file as it is now, and written back whole in one step.</summary>
    /// <remarks>
    /// Only the replaced text changes. The file is read as bytes: as UTF-8 when it is UTF-8, and otherwise byte for byte as
    /// Latin-1, which gives every byte back exactly as it was. Decoded leniently, every Latin-1 letter of a file became
    /// U+FFFD and was written back as such by an edit of another line. A byte order mark is written back as it was.
    /// <c>read_file</c> shows every line end as <c>\n</c>, so in a file with Windows line ends the <c>\n</c> of the text
    /// to find and of its replacement is the file's <c>\r\n</c>.
    /// </remarks>
    private Answer Replaced(string path, string find, string replace, bool all)
    {
        byte[] bytes = disk.Bytes(path);
        int mark = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        Encoding encoding = IsUtf8(bytes, mark) ? Strict : Bytewise;
        string before = encoding.GetString(bytes, mark, bytes.Length - mark);

        string wanted = Ended(before, find);
        int found = Occurrences(before, wanted);

        if (found == 0)
        {
            return Answer.Failed($"'{Shown(path)}' does not contain that text", "read the file first; whitespace counts");
        }

        if (found > 1 && !all)
        {
            return Answer.Failed($"that text appears {found} times in '{Shown(path)}'", "give more of it, or pass all to replace every one");
        }

        string with = Ended(before, replace);
        string after = all ? before.Replace(wanted, with, StringComparison.Ordinal) : ReplaceFirst(before, wanted, with);

        if (Encoded(encoding, after) is not { } written)
        {
            return Answer.Failed($"'{Shown(path)}' is not UTF-8, and the replacement holds letters its bytes cannot hold", "keep the replacement to ASCII, or write the file whole with write_file");
        }

        disk.Replace(path, [.. bytes.AsSpan(0, mark), .. written]);

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(path),
            ["replaced"] = all ? found : 1,
            ["bytes"] = disk.Length(path)
        });
    }

    private static bool IsUtf8(byte[] bytes, int mark) => Utf8.IsValid(bytes.AsSpan(mark));

    /// <summary>The text as the bytes of that encoding, or null when it holds a letter the encoding has no bytes for.</summary>
    private static byte[]? Encoded(Encoding encoding, string text)
    {
        try
        {
            return encoding.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The text with its <c>\n</c> written as the file writes its line ends.</summary>
    private static string Ended(string file, string text) =>
        file.Contains("\r\n", StringComparison.Ordinal) && !text.Contains("\r\n", StringComparison.Ordinal)
            ? text.Replace("\n", "\r\n", StringComparison.Ordinal)
            : text;

    /// <summary>The lock of one path: an edit reads, replaces and writes with nothing of another edit of the same file between.</summary>
    /// <remarks>
    /// A client sends a model's parallel calls in parallel, and two edits of one file each wrote back the file as they had
    /// read it: 17 of 32 parallel edits were lost while every caller heard ok. Paths share 64 locks rather than holding one
    /// each, so the locks never grow with the files touched.
    /// </remarks>
    private static object Stripe(string path) => Stripes[(int)((uint)StringComparer.Ordinal.GetHashCode(path) % (uint)Stripes.Length)];

    private Answer Listing(string? path, int depth, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
        {
            return Refused(wanted);
        }

        if (!disk.IsFolder(reached.Path!))
        {
            return Answer.Failed($"'{Shown(reached.Path!)}' is not a folder");
        }

        var unread = new List<string>();
        var entries = Walk(reached.Path!, Math.Clamp(depth, 1, 8), reached.Path!, unread, cancellationToken).Take(config.MaxMatches + 1).ToList();
        IEnumerable<JsonNode> listed = entries.Take(config.MaxMatches).Select(entry => (JsonNode)new JsonObject
        {
            ["path"] = entry.Path,
            ["isDirectory"] = entry.IsDirectory,
            ["bytes"] = entry.Bytes
        });

        return Answer.Ok(new JsonObject
        {
            ["path"] = Shown(reached.Path!),
            ["entries"] = new JsonArray([.. listed]),
            ["isCut"] = entries.Count > config.MaxMatches,
            ["unread"] = Named(unread),
            ["note"] = unread.Count == 0 ? null : $"{unread.Count} folder(s) could not be opened, and nothing in them is listed"
        });
    }

    private Answer Finding(string? pattern, string? path, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
        {
            return Refused(wanted);
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Answer.Failed("name the pattern to match", "a pattern is a glob: *.cs, Program.*, **/Tests/*.cs");
        }

        var glob = Glob.Of(pattern.Trim());
        var unread = new List<string>();
        var found = Sources(reached.Path!, reached.Path!, unread, cancellationToken)
            .Select(file => Path.GetRelativePath(reached.Path!, file))
            .Where(glob.IsMatch)
            .Take(config.MaxMatches + 1)
            .ToList();

        return Answer.Ok(new JsonObject
        {
            ["pattern"] = pattern.Trim(),
            ["files"] = Named(found.Take(config.MaxMatches)),
            ["isCut"] = found.Count > config.MaxMatches,
            ["unread"] = Named(unread),
            ["note"] = unread.Count == 0 ? null : $"{unread.Count} folder(s) could not be opened, and nothing in them was matched"
        });
    }

    private Answer Searching(string? query, string? path, string? pattern, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        if (roots.Reach(wanted) is not { IsAllowed: true } reached)
        {
            return Refused(wanted);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Answer.Failed("name the text to search for");
        }

        Glob? glob = pattern is { Length: > 0 } given ? Glob.Of(given.Trim()) : null;
        var matches = new List<TextMatch>();
        var unread = new List<string>();

        foreach (string file in Sources(reached.Path!, reached.Path!, unread, cancellationToken))
        {
            if (matches.Count > config.MaxMatches)
            {
                break;
            }

            string relative = Path.GetRelativePath(reached.Path!, file);

            if (glob is not null && !glob.IsMatch(relative))
            {
                continue;
            }

            if (!TrySearch(file, relative, query, matches))
            {
                unread.Add(relative);
            }
        }

        IEnumerable<JsonNode> shown = matches.Take(config.MaxMatches).Select(match => (JsonNode)new JsonObject
        {
            ["path"] = match.Path,
            ["line"] = match.Line,
            ["text"] = match.Text
        });

        return Answer.Ok(new JsonObject
        {
            ["query"] = query,
            ["matches"] = new JsonArray([.. shown]),
            ["isCut"] = matches.Count > config.MaxMatches,
            ["unread"] = Named(unread),
            ["note"] = unread.Count == 0 ? null : $"{unread.Count} file(s) or folder(s) were not searched: larger than {config.MaxFileBytes} bytes, or not readable"
        });
    }

    /// <summary>The root a call falls back to when it names no path: the first one this session was given.</summary>
    private string? Root()
    {
        IReadOnlyList<string> given = roots.All;

        return given.Count == 0 ? null : given[0];
    }

    private Answer Refused(string? path)
    {
        Reached reached = roots.Reach(path);

        return Answer.Failed(reached.Error!, reached.Hint);
    }

    /// <summary>The path as the caller should name it again: relative to its root where it has one.</summary>
    private string Shown(string full) =>
        roots.All.FirstOrDefault(root => full.StartsWith(root, StringComparison.Ordinal)) is { } root
            ? Path.GetRelativePath(root, full)
            : full;

    /// <summary>Searches one file and says whether it could; its matches are added as they are found.</summary>
    /// <remarks>
    /// A file of no bytes holds no text and is not opened: a named pipe has a length of zero, and opening it waits for a
    /// writer that never comes. A file that cannot be read — a link to nothing, no permission, gone since it was listed —
    /// is not searched rather than failing the search; the answer names it.
    /// </remarks>
    private bool TrySearch(string file, string relative, string query, List<TextMatch> matches)
    {
        try
        {
            long length = disk.Length(file);

            if (length == 0)
            {
                return true;
            }

            if (length > config.MaxFileBytes)
            {
                return false;
            }

            int at = 0;

            foreach (string line in disk.Lines(file))
            {
                at++;

                if (!line.Contains(query, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(new TextMatch(relative, at, Trim(line)));

                if (matches.Count > config.MaxMatches)
                {
                    break;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IEnumerable<Entry> Walk(string folder, int depth, string root, List<string> unread, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Contents contents = Opened(folder, root, unread);

        foreach (string? directory in contents.Folders.Order(StringComparer.Ordinal))
        {
            if (config.Skip.Contains(Path.GetFileName(directory), StringComparer.Ordinal))
            {
                continue;
            }

            yield return new Entry(Path.GetRelativePath(root, directory), true, 0);

            if (depth <= 1)
            {
                continue;
            }

            foreach (Entry deeper in Walk(directory, depth - 1, root, unread, cancellationToken))
            {
                yield return deeper;
            }
        }

        foreach (string? file in contents.Files.Order(StringComparer.Ordinal))
        {
            if (Sized(file) is { } bytes)
            {
                yield return new Entry(Path.GetRelativePath(root, file), false, bytes);
            }
        }
    }

    /// <summary>The size of a file, or null when it was deleted after its folder was listed and is not there to be named.</summary>
    /// <remarks>One file deleted by a build while a folder was being listed made the whole listing fail.</remarks>
    private long? Sized(string file)
    {
        try
        {
            return disk.Length(file);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Every file under a folder that is not inside one of the folders nobody means.</summary>
    private IEnumerable<string> Sources(string folder, string root, List<string> unread, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Contents contents = Opened(folder, root, unread);

        foreach (string file in contents.Files)
        {
            yield return file;
        }

        foreach (string directory in contents.Folders)
        {
            if (config.Skip.Contains(Path.GetFileName(directory), StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string deeper in Sources(directory, root, unread, cancellationToken))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>What one folder holds, or nothing and the folder named when it cannot be opened.</summary>
    /// <remarks>A folder the process may not open threw out of the enumeration and took every match already found with it.</remarks>
    private Contents Opened(string folder, string root, List<string> unread)
    {
        try
        {
            return disk.Contents(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            unread.Add(Path.GetRelativePath(root, folder));

            return new Contents([], []);
        }
    }

    private static JsonArray Named(IEnumerable<string> paths) => new([.. paths.Select(path => (JsonNode)JsonValue.Create(path))]);

    private static int Occurrences(string text, string what)
    {
        int count = 0;

        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReplaceFirst(string text, string what, string with)
    {
        int at = text.IndexOf(what, StringComparison.Ordinal);

        return string.Concat(text.AsSpan(0, at), with, text.AsSpan(at + what.Length));
    }
}
