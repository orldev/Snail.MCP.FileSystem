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
public sealed class SessionRoots(ServerConfig config, ISessionAddress address, IWorkingCopy disk)
{
    /// <summary>What the address calls the root it hands over; repeat it for more than one.</summary>
    public const string Parameter = "root";

    /// <summary>The roots of this session: the ones its address names inside the configured volume, or the configured ones when it names none.</summary>
    /// <remarks>
    /// Either, never both. A shared server is given the whole volume of working copies as its configured
    /// root so that it can serve any of them — adding that to what the session named would hand every
    /// session every other task's files, which is exactly the isolation this exists to keep. A session
    /// that names a root is confined to it and nothing else.
    /// </remarks>
    public IReadOnlyList<string> All
    {
        get
        {
            IReadOnlyList<string> configured = Paths(config.Roots);
            IReadOnlyList<string> named = Paths(address.Values(Parameter));

            return named.Count == 0 ? configured : Inside(named, configured);
        }
    }

    /// <summary>Of the roots a session names, the ones inside the configured volume; every one of them when no volume was configured.</summary>
    /// <remarks>
    /// The configured roots are the volume of working copies and a named root is one copy in it. Without this a session
    /// opened at <c>?root=/</c> was served the whole disk of the host, writable by default, by a server listening on every
    /// interface. Both sides are compared with their links resolved: a copy that is a link out of the volume is not in it.
    /// </remarks>
    private IReadOnlyList<string> Inside(IReadOnlyList<string> named, IReadOnlyList<string> configured) =>
        configured.Count == 0 ? named : [.. named.Where(root => IsInside(root, configured))];

    /// <summary>Of the roots a session names, the ones outside the configured volume, to be named when the session is refused.</summary>
    public IReadOnlyList<string> Outside(IEnumerable<string> named, IReadOnlyList<string> configured)
    {
        IReadOnlyList<string> volume = Paths(configured);

        return volume.Count == 0 ? [] : [.. Paths(named).Where(root => !IsInside(root, volume))];
    }

    private bool IsInside(string root, IReadOnlyList<string> volume) =>
        volume.Any(configured => IsUnder(Real(root), Real(configured)));

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
    public IReadOnlyList<string> Absent(IEnumerable<string> named) =>
    [
        .. named
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.Trim()))
            .Where(root => !disk.IsFolder(root))
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>The path as this server will use it, or why it may not be used.</summary>
    /// <remarks>
    /// Returns the resolved path rather than a yes or no, because every caller needs the resolved one
    /// anyway and resolving it twice is how the check and the use drift apart: the check would pass on
    /// one path and the write land on another. A path that cannot be resolved — a link that leads to
    /// itself made the system answer "too many levels of symbolic links" — is a refusal like any other.
    /// </remarks>
    public Reached Reach(string? path)
    {
        IReadOnlyList<string> roots = All;

        if (roots.Count == 0)
        {
            return Reached.No("this session was given no root, so nothing is reachable",
                "open the session at …?root=/path/to/the/copy, or set SNAIL_MCP_FILESYSTEM_ROOTS__0");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return Reached.No("name a path", $"paths are absolute or relative to {roots[0]}");
        }

        string asked = path.Trim();

        try
        {
            string full = Real(Path.IsPathRooted(asked) ? Path.GetFullPath(asked) : Path.GetFullPath(Path.Combine(roots[0], asked)));

            return roots.Any(root => IsUnder(full, Real(root)))
                ? Reached.Yes(full)
                : Reached.No($"'{asked}' is outside this session, which works in {string.Join(", ", roots)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Reached.No($"'{asked}' cannot be followed: {exception.Message}", "a link on the path leads in a circle or somewhere it may not look; name the path it leads to");
        }
    }

    /// <summary>The path as the caller should name it again: relative to the first root when it lies under it, whole otherwise.</summary>
    /// <remarks>
    /// The first root, because a relative path is reached from it: a file under the second root named relative to its own
    /// root read the file of that name under the first when it was handed back. Against the root with its links resolved,
    /// as the path was: a root that is a link, <c>/var</c> on macOS among them, made every answer name the host's
    /// absolute real path.
    /// </remarks>
    public string Relative(string full)
    {
        IReadOnlyList<string> roots = All;

        if (roots.Count == 0)
        {
            return full;
        }

        string first = Real(roots[0]);

        return IsUnder(full, first) ? Path.GetRelativePath(first, full) : full;
    }

    /// <summary>Why a path may not be used, as the answer that says so.</summary>
    public Answer Refusal(string? path)
    {
        Reached reached = Reach(path);

        return Answer.Failed(reached.Error!, reached.Hint);
    }

    private static bool IsUnder(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal)
        || path.StartsWith($"{root.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>The path with every link on it resolved; the nearest existing parent for one not created yet.</summary>
    /// <remarks>
    /// Every link on the path, not just the last one: <c>ResolveLinkTarget</c> answers about the final
    /// component alone, so a link in the middle — <c>/copy/out/secret.txt</c> where <c>out</c> leads
    /// elsewhere — resolves to itself and walks straight out of the root. That is the cheapest escape
    /// there is, and the test that found it is the reason this is written the long way.
    /// The second half is for a file about to be written: it has no link to follow, but the folders
    /// above it do, and refusing every new file would be the other way to get this wrong.
    /// </remarks>
    private string Real(string path)
    {
        string existing = path;
        var tail = new Stack<string>();

        while (!disk.IsFolder(existing) && !disk.IsFile(existing))
        {
            string? parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(parent) || parent == existing)
            {
                return path;
            }

            tail.Push(Path.GetFileName(existing));
            existing = parent;
        }

        string resolved = Resolved(existing);

        return tail.Count == 0 ? resolved : Path.Combine([resolved, .. tail]);
    }

    /// <summary>An existing path with every link along it followed to where it really goes.</summary>
    private string Resolved(string existing)
    {
        if (disk.LinkTarget(existing) is { } target)
        {
            return Resolved(target);
        }

        string? parent = Path.GetDirectoryName(existing);

        if (string.IsNullOrEmpty(parent) || parent == existing)
        {
            return existing;
        }

        string real = Resolved(parent);

        return string.Equals(real, parent, StringComparison.Ordinal)
            ? existing
            : Path.Combine(real, Path.GetFileName(existing));
    }
}
