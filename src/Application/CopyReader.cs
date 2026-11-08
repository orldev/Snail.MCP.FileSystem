using Snail.MCP.FileSystem.Configuration;

namespace Snail.MCP.FileSystem.Application;

/// <summary>Reading a file of the working copy, whole or by lines, with what was cut named.</summary>
public sealed class CopyReader(SessionRoots roots, ServerConfig config, IWorkingCopy disk)
{
    /// <summary>The text of a file, whole or by lines, with what was cut named.</summary>
    public Answer<JsonObject> Read(string? path, int? from, int? to, CancellationToken cancellationToken = default) =>
        DiskRefusals.Guarded(path, () => Reading(path, from, to, cancellationToken));

    private Answer<JsonObject> Reading(string? path, int? from, int? to, CancellationToken cancellationToken)
    {
        Reached reached = roots.Reach(path, PathKind.File);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        bool isWhole = from is null && to is null;

        if (isWhole && disk.Length(reached.Path!) > config.MaxFileBytes)
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(reached.Path!)}' is larger than {config.MaxFileBytes} bytes", "read it by lines with from and to");
        }

        int first = Math.Max(1, from ?? 1);
        var window = LineWindow.Of(disk, reached.Path!, first, to ?? int.MaxValue, config.OutputLimitChars, cancellationToken);
        string text = string.Join('\n', window.Taken);
        bool cut = text.Length > config.OutputLimitChars;

        return Answer.Ok(new JsonObject
        {
            ["path"] = roots.Relative(reached.Path!),
            ["from"] = first,
            ["to"] = Math.Min(window.Lines, to ?? window.Lines),
            ["lines"] = window.Lines,
            ["text"] = cut ? Cut.Whole(text, config.OutputLimitChars) : text,
            ["isCut"] = cut
        });
    }
}
