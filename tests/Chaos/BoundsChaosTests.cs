using System.Diagnostics;
using System.Runtime.Versioning;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// One process serves every agent of a machine, so one call that takes unbounded memory or time takes it from all of
/// them. The limits of the configuration guarded some ways to the disk and not others.
/// </summary>
public sealed class BoundsChaosTests
{
    /// <summary>
    /// Reading by lines is allowed past <c>MaxFileBytes</c> on purpose, and the window stops collecting once the output
    /// limit is taken — but only between lines. A file of one line and no line end (a minified bundle, a log written
    /// without newlines, a binary) was one string of the whole file: 48 MB of it read as line 1 allocated 352 MB for an
    /// answer cut at 131 072 characters.
    /// </summary>
    [Fact]
    public void Read_OneLineFarPastTheOutputLimit_AllocatesAboutTheLimit()
    {
        using var copy = new Copy();
        string path = copy.Write("bundle.min.js", string.Empty);

        using (FileStream file = File.OpenWrite(path))
        {
            byte[] block = new byte[1024 * 1024];
            Array.Fill(block, (byte)'a');

            for (int megabyte = 0; megabyte < 48; megabyte++)
            {
                file.Write(block);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        Answer answer = copy.Files.Read("bundle.min.js", 1, 1);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.True(allocated < 16L * 1024 * 1024, $"one line of a 48 MB file allocated {allocated / (1024 * 1024)} MB for an answer of {copy.Config.OutputLimitChars} characters");
        Assert.True(answer.Data!["isCut"]!.GetValue<bool>());
    }

    /// <summary>
    /// The window counts the lines of the whole file so that the answer can say how many there are: <c>read_file</c> of lines
    /// 1 to 10 of a log of ten gigabytes read ten gigabytes, and nothing could stop it — the call took no cancellation.
    /// </summary>
    [Fact]
    public void Read_CallCancelled_StopsReading()
    {
        using var copy = new Copy();
        copy.Write("build.log", string.Concat(Enumerable.Repeat("a line of the log\n", 10_000)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => copy.Files.Read("build.log", 1, 10, cancelled.Token));
    }

    /// <summary>
    /// <c>MaxFileBytes</c> is "bytes past which a file is not read as text at all", and <c>read_file</c> and
    /// <c>search_text</c> held to it. <c>edit_file</c> read the file whole, as bytes and then as a string twice over, with
    /// no size asked: a generated 1.5 GB file was several gigabytes of one call, and the shared server was killed for memory.
    /// </summary>
    [Fact]
    public void Edit_FileLargerThanMaxFileBytes_IsRefused()
    {
        using var copy = new Copy(config => config.MaxFileBytes = 1024);
        copy.Write("big.txt", $"{new string('x', 4096)}\nneedle\n");

        Answer answer = copy.Files.Edit("big.txt", "needle", "pin", all: false);

        Assert.False(answer.IsSuccess, "edit_file read and rewrote a file four times larger than MaxFileBytes");
        Assert.Contains("needle", File.ReadAllText(Path.Combine(copy.Root, "big.txt")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A named pipe has a length of zero and is a file to <c>File.Exists</c>. The walks and <c>read_file</c> do not open
    /// it, <c>edit_file</c> did: the read waited for a writer that never comes — inside the lock of its stripe, so every
    /// later edit of every path hashed to the same stripe waited behind it, for the life of the process.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Edit_NamedPipe_AnswersWithoutWaitingForAWriter()
    {
        using var copy = new Copy();
        string pipe = Path.Combine(copy.Root, "pipe");

        using (var made = Process.Start("mkfifo", pipe))
        {
            await made.WaitForExitAsync();
        }

        Task<Answer> edit = Task.Run(() => copy.Files.Edit("pipe", "a", "b", all: false));

        try
        {
            Answer answer = await edit.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(answer.IsSuccess);
        }
        finally
        {
            if (!edit.IsCompleted)
            {
                Release(pipe);
                await Task.WhenAny(edit, Task.Delay(TimeSpan.FromSeconds(3)));
            }
        }
    }

    /// <summary>
    /// A glob becomes a backtracking regular expression with a timeout. A pattern of many stars against a long name
    /// backtracked past it, and the <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> was not one the
    /// answer guard knew: it left the tool as the SDK's "An error occurred invoking 'find_files'", the shape the guard
    /// exists to prevent, after two seconds of a core.
    /// </summary>
    [Fact]
    public void Find_PatternThatBacktracks_IsRefusedInTheServersWords()
    {
        using var copy = new Copy();
        copy.Write($"{new string('a', 64)}.txt", "x");

        var clock = Stopwatch.StartNew();
        Answer? answer = null;
        Exception? thrown = Record.Exception(() => answer = copy.Files.Find("*a*a*a*a*a*a*a*a*a*a*a*a*a*a*b", null));

        Assert.True(thrown is null, $"find_files threw {thrown?.GetType().Name} after {clock.ElapsedMilliseconds} ms");
        Assert.False(answer!.IsSuccess);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"the refusal took {clock.ElapsedMilliseconds} ms of a core");
    }

    /// <summary>Opens the pipe for writing, which lets the reader blocked on it go; called only while that reader waits.</summary>
    private static void Release(string pipe)
    {
        try
        {
            using FileStream writer = new(pipe, FileMode.Open, FileAccess.Write);
        }
        catch (IOException)
        {
        }
    }
}
