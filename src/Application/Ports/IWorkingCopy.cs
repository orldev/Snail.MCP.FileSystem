namespace Snail.MCP.FileSystem.Application.Ports;

/// <summary>The disk of the machine, as the server reads and writes it.</summary>
/// <remarks>
/// Everything the server does to a disk goes through here, so the rules of what may be reached and how an answer is made
/// know nothing of how a disk is reached, and a test can hand them a disk that fails: a share that times out, a file that
/// vanishes between two calls. Any member may throw an <see cref="IOException"/> or an
/// <see cref="UnauthorizedAccessException"/>; the callers answer those as refusals.
/// <para>
/// What is at a path and where a link leads are <see cref="IRootsDisk"/>'s, the questions <c>SessionRoots.Reach</c> asks, and
/// the server hands this disk to its roots as theirs: the chaos tests failed this port everywhere but at <c>Reach</c>, which
/// ran on the real disk whatever disk the test gave the server. One disk, so a share that times out fails the reaching
/// of a path as it fails the reading of the file.
/// </para>
/// </remarks>
public interface IWorkingCopy : IRootsDisk
{
    /// <summary>The length of the file at the path, in bytes.</summary>
    long Length(string path);

    /// <summary>The lines of a file, read as they are asked for, each kept to <paramref name="longest"/> characters.</summary>
    IEnumerable<string> Lines(string path, int longest);

    /// <summary>The bytes of a file.</summary>
    byte[] Bytes(string path);

    /// <summary>The files and folders directly in a folder, with every link left out.</summary>
    Contents Contents(string folder);

    /// <summary>Makes a folder and every folder above it that is missing.</summary>
    void MakeFolder(string path);

    /// <summary>Replaces a file whole in one step, or creates it, keeping the mode of the file it replaces.</summary>
    void Replace(string path, byte[] bytes);
}
