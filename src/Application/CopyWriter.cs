using System.Text;
using Snail.MCP.FileSystem.Configuration;

namespace Snail.MCP.FileSystem.Application;

/// <summary>Changing a file of the working copy: written whole, or an exact piece of it replaced.</summary>
/// <remarks>
/// Every change is written to the journal, with the file's full path, which names the copy it is in: one server changes the
/// copies of every task of a machine, and nothing said afterwards which change was whose.
/// </remarks>
public sealed class CopyWriter(SessionRoots roots, ServerConfig config, IWorkingCopy disk, ILogger<CopyWriter> journal)
{
    /// <summary>UTF-8 that refuses what is not UTF-8, rather than turning it into U+FFFD.</summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly object[] Stripes = [.. Enumerable.Range(0, 64).Select(_ => new object())];

    /// <summary>Writes a file whole, making the folders above it.</summary>
    public Answer<JsonObject> Write(string? path, string? text) => DiskRefusals.Guarded(path, () => Writing(path, text));

    /// <summary>Replaces an exact piece of a file, and says how many times it did.</summary>
    public Answer<JsonObject> Edit(string? path, string? find, string? replace, bool all) => DiskRefusals.Guarded(path, () => Editing(path, find, replace, all));

    private Answer<JsonObject> Writing(string? path, string? text)
    {
        if (!config.AllowWrite)
        {
            return Answer.Failed<JsonObject>("this server was started read-only", "a role that writes needs a server with writing turned on");
        }

        Reached reached = roots.Reach(path);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        if (disk.IsFolder(reached.Path!))
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(reached.Path!)}' is a folder", "name a file inside it");
        }

        disk.MakeFolder(Path.GetDirectoryName(reached.Path!)!);

        bool existed = disk.IsFile(reached.Path!);
        byte[] bytes = Strict.GetBytes(text ?? string.Empty);

        lock (Stripe(reached.Path!))
        {
            disk.Replace(reached.Path!, bytes);
        }

        Log.Wrote(journal, reached.Path!, bytes.Length, existed ? "replaced" : "created");

        return Answer.Ok(new JsonObject
        {
            ["path"] = roots.Relative(reached.Path!),
            ["isCreated"] = !existed,
            ["bytes"] = disk.Length(reached.Path!)
        });
    }

    /// <remarks>
    /// Exact text rather than a pattern, and the whole file rather than a line number: a line number is
    /// stale the moment anything above it changes, and a pattern matches what nobody meant. A
    /// replacement that matches nothing is refused instead of written, because the caller believed the
    /// file said something it does not.
    /// </remarks>
    private Answer<JsonObject> Editing(string? path, string? find, string? replace, bool all)
    {
        if (!config.AllowWrite)
        {
            return Answer.Failed<JsonObject>("this server was started read-only", "a role that writes needs a server with writing turned on");
        }

        if (string.IsNullOrEmpty(find))
        {
            return Answer.Failed<JsonObject>("name the text to replace", "edit_file replaces exact text, so give enough of it to be unique");
        }

        Reached reached = roots.Reach(path, PathKind.File);

        if (!reached.IsAllowed)
        {
            return reached.As<JsonObject>();
        }

        lock (Stripe(reached.Path!))
        {
            return Replaced(reached.Path!, find, replace ?? string.Empty, all);
        }
    }

    /// <summary>The replacement made in the file as it is now, and written back whole in one step.</summary>
    /// <remarks>
    /// Only the replaced text changes. The file is read as bytes and decoded by <see cref="TextCodec"/>: in the encoding its
    /// byte order mark names, as UTF-8 when it is UTF-8, and otherwise byte for byte as Latin-1, which gives every byte back
    /// exactly as it was. Decoded leniently, every Latin-1 letter of a file became U+FFFD and was written back as such by an
    /// edit of another line. A byte order mark is written back as it was.
    /// <c>read_file</c> shows every line end as <c>\n</c>, so in a file with Windows line ends the <c>\n</c> of the text
    /// to find and of its replacement is the file's <c>\r\n</c>. A file of no bytes is not opened, as the walks do not
    /// open one: a named pipe has a length of zero, and reading it waited for a writer inside the lock of its stripe.
    /// A file past <c>MaxFileBytes</c> is not read either, as <c>read_file</c> does not read one whole: held as bytes and
    /// twice as text, a generated file of a gigabyte or two took the shared server's memory.
    /// </remarks>
    private Answer<JsonObject> Replaced(string path, string find, string replace, bool all)
    {
        long length = disk.Length(path);

        if (length == 0)
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(path)}' does not contain that text", "the file is empty, or is not a file that holds text");
        }

        if (length > config.MaxFileBytes)
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(path)}' is larger than {config.MaxFileBytes} bytes", "write it whole with write_file, or ask whoever runs the server for a larger MaxFileBytes");
        }

        byte[] bytes = disk.Bytes(path);
        var codec = TextCodec.Of(bytes);
        string before = codec.Decode(bytes);

        if (Spelled(before, find) is not { } wanted)
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(path)}' does not contain that text", "read the file first; whitespace counts");
        }

        int found = Occurrences(before, wanted);

        if (found > 1 && !all)
        {
            return Answer.Failed<JsonObject>($"that text appears {found} times in '{roots.Relative(path)}'", "give more of it, or pass all to replace every one");
        }

        string with = EndedLike(before, wanted, replace);
        string after = all ? before.Replace(wanted, with, StringComparison.Ordinal) : ReplaceFirst(before, wanted, with);

        if (codec.Encode(bytes, after) is not { } written)
        {
            return Answer.Failed<JsonObject>($"'{roots.Relative(path)}' is not UTF-8, and the replacement holds letters its bytes cannot hold", "keep the replacement to ASCII, or write the file whole with write_file");
        }

        disk.Replace(path, written);
        Log.Edited(journal, path, all ? found : 1, written.Length);

        return Answer.Ok(new JsonObject
        {
            ["path"] = roots.Relative(path),
            ["replaced"] = all ? found : 1,
            ["bytes"] = disk.Length(path)
        });
    }

    /// <summary>The text to find as the file spells it: as given where it occurs so, else with Windows line ends, else null.</summary>
    /// <remarks>
    /// Chosen by where the text is, not for the whole file: one <c>\r\n</c> in a header made every <c>\n</c> of the text a
    /// <c>\r\n</c>, and a file of mixed line ends refused every multi-line edit of its Unix part.
    /// </remarks>
    private static string? Spelled(string file, string text)
    {
        if (file.Contains(text, StringComparison.Ordinal))
        {
            return text;
        }

        string windows = Windows(text);

        return file.Contains(windows, StringComparison.Ordinal) ? windows : null;
    }

    /// <summary>The replacement with the line ends of the text it replaces, or of the file when that text is one line.</summary>
    private static string EndedLike(string file, string wanted, string replace)
    {
        bool isWindows = wanted.Contains("\r\n", StringComparison.Ordinal)
            || (!wanted.Contains('\n', StringComparison.Ordinal) && file.Contains("\r\n", StringComparison.Ordinal));

        return isWindows ? Windows(replace) : replace;
    }

    private static string Windows(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? text : text.Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>The lock of one path: an edit reads, replaces and writes with nothing of another edit of the same file between.</summary>
    /// <remarks>
    /// A client sends a model's parallel calls in parallel, and two edits of one file each wrote back the file as they had
    /// read it: 17 of 32 parallel edits were lost while every caller heard ok. Paths share 64 locks rather than holding one
    /// each, so the locks never grow with the files touched. The lock ignores case: on a disk that does too, the default
    /// of macOS, <c>slots.txt</c> and <c>SLOTS.TXT</c> took two locks for one file and lost 16 of 32 edits.
    /// </remarks>
    private static object Stripe(string path) => Stripes[(int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(path) % (uint)Stripes.Length)];

    private static int Occurrences(string text, string what)
    {
        int count = 0;

        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string ReplaceFirst(string text, string what, string with)
    {
        int at = text.IndexOf(what, StringComparison.Ordinal);

        return string.Concat(text.AsSpan(0, at), with, text.AsSpan(at + what.Length));
    }
}
