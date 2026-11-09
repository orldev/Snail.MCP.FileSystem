using Snail.MCP.FileSystem.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Application;

/// <summary>Walking the working copy: what a folder holds, the files a pattern names, the lines that hold a text.</summary>
public sealed class CopyWalker(SessionRoots roots, ServerConfig config, IWorkingCopy disk)
{
    /// <summary>What lies under a folder, to the depth asked for.</summary>
    public Answer<JsonObject> List(string? path, int depth, CancellationToken cancellationToken = default) =>
        DiskRefusals.Guarded(path, Reached.Unreadable, () => Listing(path, depth, cancellationToken));

    /// <summary>Files whose name matches a pattern.</summary>
    public Answer<JsonObject> Find(string? pattern, string? path, CancellationToken cancellationToken = default) =>
        DiskRefusals.Guarded(path, Reached.Unreadable, () => Finding(pattern, path, cancellationToken));

    /// <summary>Lines of files that contain a text.</summary>
    public Answer<JsonObject> Search(string? query, string? path, string? pattern, CancellationToken cancellationToken = default) =>
        DiskRefusals.Guarded(path, Reached.Unreadable, () => Searching(query, path, pattern, cancellationToken));

    private Answer<JsonObject> Listing(string? path, int depth, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        Reached reached = roots.Reach(wanted, PathKind.Folder);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        var unread = new List<string>();
        var entries = Walk(reached.Path, Math.Clamp(depth, 1, 8), reached.Path, unread, cancellationToken).Take(config.MaxMatches + 1).ToList();
        IEnumerable<JsonNode> listed = entries.Take(config.MaxMatches).Select(entry => (JsonNode)new JsonObject
        {
            ["path"] = entry.Path,
            ["isDirectory"] = entry.IsDirectory,
            ["bytes"] = entry.Bytes
        });

        return Answer.Ok(new JsonObject
        {
            ["path"] = roots.Relative(reached.Path),
            ["entries"] = new JsonArray([.. listed]),
            ["isCut"] = entries.Count > config.MaxMatches,
            ["unread"] = Named(unread),
            ["note"] = unread.Count == 0 ? null : $"{unread.Count} folder(s) could not be opened, and nothing in them is listed"
        });
    }

    private Answer<JsonObject> Finding(string? pattern, string? path, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        Reached reached = roots.Reach(wanted, PathKind.Folder);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Answer.Failed("name the pattern to match", "a pattern is a glob: *.cs, Program.*, **/Tests/*.cs", ToolFailures.Arguments);
        }

        var glob = Glob.Of(pattern.Trim());
        var unread = new List<string>();
        var found = Sources(reached.Path, reached.Path, unread, cancellationToken)
            .Select(file => Path.GetRelativePath(reached.Path, file))
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

    private Answer<JsonObject> Searching(string? query, string? path, string? pattern, CancellationToken cancellationToken)
    {
        string? wanted = string.IsNullOrWhiteSpace(path) ? Root() : path;

        Reached reached = roots.Reach(wanted, PathKind.Folder);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Answer.Failed("name the text to search for", type: ToolFailures.Arguments);
        }

        if (query.Contains('\n', StringComparison.Ordinal))
        {
            return Answer.Failed("search_text matches within one line, and this text spans several", "search for one line of it, then read_file around the match", ToolFailures.Arguments);
        }

        Glob? glob = pattern is { Length: > 0 } given ? Glob.Of(given.Trim()) : null;
        var matches = new List<TextMatch>();
        var unread = new List<string>();

        foreach (string file in Sources(reached.Path, reached.Path, unread, cancellationToken))
        {
            if (matches.Count > config.MaxMatches)
            {
                break;
            }

            string relative = Path.GetRelativePath(reached.Path, file);

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

            foreach (string line in disk.Lines(file, int.MaxValue))
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

    /// <summary>The line cut at its length for an answer, on a whole character.</summary>
    private static string Trim(string line) => line.Length <= 240 ? line : Cut.Whole(line, 240);

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
}
