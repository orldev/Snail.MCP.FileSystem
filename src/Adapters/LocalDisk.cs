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
    public bool IsFile(string path) => Attributes(path) is { } attributes && !attributes.HasFlag(FileAttributes.Directory);

    /// <inheritdoc/>
    public bool IsFolder(string path) => Attributes(path) is { } attributes && attributes.HasFlag(FileAttributes.Directory);

    /// <inheritdoc/>
    /// <remarks>
    /// Read by name, not through <see cref="LinklessDisk"/>: this is how <c>SessionRoots.Reach</c> follows the links of a path
    /// before the path is used, and <see cref="LinklessDisk"/> is what refuses a link found on it afterwards.
    /// </remarks>
    public string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    /// <summary>What is at the path, or null when nothing is; a path the system may not look at throws rather than reads as missing.</summary>
    /// <remarks>
    /// <c>File.Exists</c> and <c>Directory.Exists</c> answer false for a file in a folder this process may not enter, so
    /// <c>Reach</c> told the model "there is no file" about a file that is there, as Snail.Sdk's own disk did before it
    /// answered <c>unreadable</c>. A name under a file is still missing: nothing can be under a file, and the system says so
    /// as a folder not found.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The system may not look at the path.</exception>
    /// <exception cref="IOException">The disk failed while it was asked.</exception>
    private static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            return null;
        }
    }

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
