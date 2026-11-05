using System.Text;

namespace Snail.MCP.FileSystem.Adapters;

/// <summary>The disk of the machine the server runs on.</summary>
public sealed class LocalDisk : IWorkingCopy
{
    /// <summary>How a folder is opened: links are passed by, and so is what the process may not open.</summary>
    /// <remarks>
    /// The roots are checked for the folder a walk starts in, and what the walk then meets is never checked again, so the
    /// walk must not leave the copy: no link is followed. Through one, a search returned the line of a file outside the
    /// copy that <c>read_file</c> refused; one link back to its own folder named the same file 17 times.
    /// </remarks>
    private static readonly EnumerationOptions Walking = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };

    /// <inheritdoc/>
    public bool IsFile(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool IsFolder(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public long Length(string path) => OperatingSystem.IsWindows() ? new FileInfo(path).Length : LinklessDisk.Length(path);

    /// <inheritdoc/>
    /// <remarks>
    /// The rest of a line past <paramref name="longest"/> is read past, not collected: read whole, one line of a 48 MB file
    /// without line ends allocated 352 MB for an answer cut at 131 072 characters. Unbounded, a line is read as a whole
    /// line is read, which the search keeps for its speed: its files are under <c>MaxFileBytes</c> already.
    /// </remarks>
    public IEnumerable<string> Lines(string path, int longest) =>
        longest == int.MaxValue ? Whole(path) : Capped(path, longest);

    private static IEnumerable<string> Whole(string path)
    {
        using var reader = new StreamReader(Reading(path));

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Capped(string path, int longest)
    {
        using var reader = new StreamReader(Reading(path));
        var line = new StringBuilder();
        int next;

        while ((next = reader.Read()) >= 0)
        {
            char letter = (char)next;

            if (letter is '\n' or '\r')
            {
                if (letter == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                yield return line.ToString();
                line.Clear();

                continue;
            }

            if (line.Length < longest)
            {
                line.Append(letter);
            }
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <inheritdoc/>
    public byte[] Bytes(string path)
    {
        using FileStream file = Reading(path);
        byte[] bytes = new byte[file.Length];

        file.ReadExactly(bytes);

        return bytes;
    }

    /// <summary>A file opened for reading; outside Windows with no link followed on its path, as <see cref="Replace"/> writes.</summary>
    private static FileStream Reading(string path) =>
        OperatingSystem.IsWindows() ? File.OpenRead(path) : LinklessDisk.Reading(path);

    /// <inheritdoc/>
    /// <remarks>
    /// Collected into arrays: aimed at <see cref="IReadOnlyList{T}"/> directly, a collection expression built a list and a
    /// wrapper around it for every folder, and the find_files benchmark allocated 149.48 KB instead of 140.07 KB.
    /// </remarks>
    public Contents Contents(string folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return LinklessDisk.Contents(folder);
        }

        string[] files = [.. Directory.EnumerateFiles(folder, "*", Walking)];
        string[] folders = [.. Directory.EnumerateDirectories(folder, "*", Walking)];

        return new Contents(files, folders);
    }

    /// <inheritdoc/>
    public string? LinkTarget(string path) =>
        (Directory.Exists(path)
            ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)
            : new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true))?.FullName;

    /// <inheritdoc/>
    public void MakeFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);

            return;
        }

        LinklessDisk.MakeFolder(path);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Written beside the file and renamed over it, which is atomic on one volume. Writing in place truncated the file
    /// first, and 3 of 32 parallel edits read it while it was empty. The mode of the file it replaces is carried over, or
    /// a script would lose its executable bit. Outside Windows through <see cref="LinklessDisk"/>, so that no link swapped
    /// onto the path after the check carries the write out of the copy.
    /// </remarks>
    public void Replace(string path, byte[] bytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode? mode = File.Exists(path) ? File.GetUnixFileMode(path) : null;

            LinklessDisk.Replace(path, bytes, mode);

            return;
        }

        string beside = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(beside, bytes);
            File.Move(beside, path, overwrite: true);
        }
        finally
        {
            File.Delete(beside);
        }
    }
}
