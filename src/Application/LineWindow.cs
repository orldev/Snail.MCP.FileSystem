namespace Snail.MCP.FileSystem.Application;

/// <summary>The lines of a file a reader asked for, and how many lines the file has, read in one pass.</summary>
/// <param name="Taken">The lines asked for, as many of them as fit the characters one answer may hold.</param>
/// <param name="Lines">How many lines the whole file has.</param>
internal sealed record LineWindow(IReadOnlyList<string> Taken, int Lines)
{
    /// <summary>Lines <paramref name="first"/> to <paramref name="last"/> of a file, counting from one.</summary>
    /// <remarks>
    /// Streamed, so a file of any size can be read by lines: the size limit refused a large file whole and hinted to read
    /// it by lines, then refused that too. Collecting stops once <paramref name="budget"/> characters are taken, since the
    /// answer is cut there anyway; counting goes on to the end. A file of no bytes is not opened: a named pipe has a length
    /// of zero, and opening it waits for a writer that never comes. The count reads the whole file, so the call's
    /// cancellation is asked at every line: lines 1 to 10 of a log of ten gigabytes read ten gigabytes, and nothing stopped it.
    /// </remarks>
    public static LineWindow Of(IWorkingCopy disk, string path, int first, int last, int budget, CancellationToken cancellationToken)
    {
        if (disk.Length(path) == 0)
        {
            return new LineWindow([], 0);
        }

        var taken = new List<string>();
        int characters = 0;
        int lines = 0;

        foreach (string line in disk.Lines(path, budget + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines++;

            if (lines < first || lines > last || characters > budget)
            {
                continue;
            }

            taken.Add(line);
            characters += line.Length + 1;
        }

        return new LineWindow(taken, lines);
    }
}
