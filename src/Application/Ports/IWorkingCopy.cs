namespace Snail.MCP.FileSystem.Application.Ports;

/// <summary>The disk of the machine, as the server reads and writes it.</summary>
/// <remarks>
/// Everything the server does to a disk goes through here, so the rules of what may be reached and how an answer is made
/// know nothing of how a disk is reached, and a test can hand them a disk that fails: a share that times out, a file that
/// vanishes between two calls. Any member may throw an <see cref="IOException"/> or an
/// <see cref="UnauthorizedAccessException"/>; the callers answer those as refusals.
/// </remarks>
public interface IWorkingCopy
{
    /// <summary>Whether a file is at the path.</summary>
    bool IsFile(string path);

    /// <summary>Whether a folder is at the path.</summary>
    bool IsFolder(string path);

    /// <summary>The length of the file at the path, in bytes.</summary>
    long Length(string path);

    /// <summary>The lines of a file, read as they are asked for, each kept to <paramref name="longest"/> characters.</summary>
    IEnumerable<string> Lines(string path, int longest);

    /// <summary>The bytes of a file.</summary>
    byte[] Bytes(string path);

    /// <summary>The files and folders directly in a folder, with every link left out.</summary>
    Contents Contents(string folder);

    /// <summary>Where the link at a path finally leads, or null when the path is not a link.</summary>
    string? LinkTarget(string path);

    /// <summary>Makes a folder and every folder above it that is missing.</summary>
    void MakeFolder(string path);

    /// <summary>Replaces a file whole in one step, or creates it, keeping the mode of the file it replaces.</summary>
    void Replace(string path, byte[] bytes);
}
