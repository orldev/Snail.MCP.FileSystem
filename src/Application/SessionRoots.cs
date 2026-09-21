using Snail.MCP.FileSystem.Configuration;

namespace Snail.MCP.FileSystem.Application;

/// <summary>The folders this session may touch, and whether a path is one of them.</summary>
/// <remarks>
/// A filesystem server is one call away from every file its process can read, so this is the whole of
/// its security and everything else goes through it. The roots are read from the address the session
/// was opened at — <c>…/mcp?root=/workspaces/task</c> — and not from anything a call carries, because
/// a call can say whatever it likes; a session that names none gets the configured roots, which is how
/// a server started as a program for one caller is confined.
/// Closed by default: a server with no roots at all serves nothing rather than serving the disk.
/// </remarks>
public sealed class SessionRoots(ServerConfig config, ISessionAddress address)
{
    /// <summary>What the address calls the root it hands over; repeat it for more than one.</summary>
    public const string Parameter = "root";

    /// <summary>The roots of this session: the ones its address names, or the configured ones when it names none.</summary>
    /// <remarks>
    /// Either, never both. A shared server is given the whole volume of working copies as its configured
    /// root so that it can serve any of them — adding that to what the session named would hand every
    /// session every other task's files, which is exactly the isolation this exists to keep. A session
    /// that names a root is confined to it and nothing else.
    /// </remarks>
    public IReadOnlyList<string> All => Paths(address.Values(Parameter)) is { Count: > 0 } named ? named : Paths(config.Roots);

    private static IReadOnlyList<string> Paths(IEnumerable<string> roots) =>
    [
        .. roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.Trim()))
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>Of the roots a session names, the ones this machine does not have.</summary>
    /// <remarks>
    /// Asked before a session is let in, so that a worker pointed at the server of another machine hears
    /// about it at once. A root that does not exist here is never an empty result to be worked around: it
    /// means the copy is on a different host, and every answer this session could give would be about the
    /// wrong files.
    /// </remarks>
    public static IReadOnlyList<string> Absent(IEnumerable<string> named) =>
    [
        .. named
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.Trim()))
            .Where(root => !Directory.Exists(root))
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>The path as this server will use it, or why it may not be used.</summary>
    /// <remarks>
    /// Returns the resolved path rather than a yes or no, because every caller needs the resolved one
    /// anyway and resolving it twice is how the check and the use drift apart: the check would pass on
    /// one path and the write land on another.
    /// </remarks>
    public Reached Reach(string? path)
    {
        var roots = All;

        if (roots.Count == 0)
            return Reached.No("this session was given no root, so nothing is reachable",
                "open the session at …?root=/path/to/the/copy, or set SNAIL_MCP_FILESYSTEM_ROOTS__0");

        if (string.IsNullOrWhiteSpace(path))
            return Reached.No("name a path", $"paths are absolute or relative to {roots[0]}");

        var full = Real(Path.IsPathRooted(path.Trim())
            ? Path.GetFullPath(path.Trim())
            : Path.GetFullPath(Path.Combine(roots[0], path.Trim())));

        return roots.Any(root => Under(full, Real(root)))
            ? Reached.Yes(full)
            : Reached.No($"'{path.Trim()}' is outside this session, which works in {string.Join(", ", roots)}");
    }

    private static bool Under(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>The path with every link on it resolved; the nearest existing parent for one not created yet.</summary>
    /// <remarks>
    /// Every link on the path, not just the last one: <c>ResolveLinkTarget</c> answers about the final
    /// component alone, so a link in the middle — <c>/copy/out/secret.txt</c> where <c>out</c> leads
    /// elsewhere — resolves to itself and walks straight out of the root. That is the cheapest escape
    /// there is, and the test that found it is the reason this is written the long way.
    /// The second half is for a file about to be written: it has no link to follow, but the folders
    /// above it do, and refusing every new file would be the other way to get this wrong.
    /// </remarks>
    private static string Real(string path)
    {
        var existing = path;
        var tail = new Stack<string>();

        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(parent) || parent == existing)
                return path;

            tail.Push(Path.GetFileName(existing));
            existing = parent;
        }

        var resolved = Resolved(existing);

        return tail.Count == 0 ? resolved : Path.Combine([resolved, .. tail]);
    }

    /// <summary>An existing path with every link along it followed to where it really goes.</summary>
    private static string Resolved(string existing)
    {
        if (Link(existing) is { } target)
            return Resolved(target);

        var parent = Path.GetDirectoryName(existing);

        if (string.IsNullOrEmpty(parent) || parent == existing)
            return existing;

        var real = Resolved(parent);

        return string.Equals(real, parent, StringComparison.Ordinal)
            ? existing
            : Path.Combine(real, Path.GetFileName(existing));
    }

    private static string? Link(string path) =>
        (Directory.Exists(path)
            ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)
            : new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true))?.FullName;
}

/// <summary>A path this session may use, or the reason it may not.</summary>
public sealed record Reached(string? Path, string? Error, string? Hint)
{
    public bool IsAllowed => Path is not null;

    public static Reached Yes(string path) => new(path, null, null);

    public static Reached No(string error, string? hint = null) => new(null, error, hint);
}
