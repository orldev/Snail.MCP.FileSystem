using Microsoft.Extensions.Logging.Abstractions;
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
/// The work is split by what it does to the copy — <see cref="CopyReader"/>, <see cref="CopyWriter"/>,
/// <see cref="CopyWalker"/> — and this is the one entry the tools call: in one class of 557 lines the encodings, the
/// locks, the walks and the answers of six tools could not be read apart.
/// </remarks>
public sealed class Files(CopyReader reader, CopyWriter writer, CopyWalker walker)
{
    /// <summary>The files of a session, over a disk, under a configuration, its changes written to a journal or to none.</summary>
    public static Files Of(SessionRoots roots, ServerConfig config, IWorkingCopy disk, ILogger<CopyWriter>? journal = null) =>
        new(new CopyReader(roots, config, disk), new CopyWriter(roots, config, disk, journal ?? NullLogger<CopyWriter>.Instance), new CopyWalker(roots, config, disk));

    /// <summary>The text of a file, whole or by lines, with what was cut named.</summary>
    public Answer<JsonObject> Read(string? path, int? from, int? to, CancellationToken cancellationToken = default) =>
        reader.Read(path, from, to, cancellationToken);

    /// <summary>Writes a file whole, making the folders above it.</summary>
    public Answer<JsonObject> Write(string? path, string? text) => writer.Write(path, text);

    /// <summary>Replaces an exact piece of a file, and says how many times it did.</summary>
    public Answer<JsonObject> Edit(string? path, string? find, string? replace, bool all) => writer.Edit(path, find, replace, all);

    /// <summary>What lies under a folder, to the depth asked for.</summary>
    public Answer<JsonObject> List(string? path, int depth, CancellationToken cancellationToken = default) =>
        walker.List(path, depth, cancellationToken);

    /// <summary>Files whose name matches a pattern.</summary>
    public Answer<JsonObject> Find(string? pattern, string? path, CancellationToken cancellationToken = default) =>
        walker.Find(pattern, path, cancellationToken);

    /// <summary>Lines of files that contain a text.</summary>
    public Answer<JsonObject> Search(string? query, string? path, string? pattern, CancellationToken cancellationToken = default) =>
        walker.Search(query, path, pattern, cancellationToken);
}
