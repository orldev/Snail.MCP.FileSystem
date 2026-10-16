using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// The searches walk the copy, and the roots are checked only for the folder a walk starts in. Everything the walk then
/// meets — a link, a loop, a folder it may not open, a file that is not a file — is met without that check and without
/// any guard against the disk saying no.
/// </summary>
public sealed class WalkChaosTests
{
    /// <summary>
    /// Breaks the one promise of the server. <c>read_file</c> resolves every link and refuses <c>vendor/id_rsa</c>, but
    /// <c>search_text</c> enumerates the folder the link points at and returns the lines it finds there: the content of a
    /// file outside the session, through the tool that is not supposed to reach it either.
    /// </summary>
    [Fact]
    public void SearchText_LinkInsideTheCopyLeadsOut_ReturnsNoLineFromOutside()
    {
        using var copy = new Copy();
        using var elsewhere = new Copy();
        elsewhere.Write("id_rsa", "PRIVATE KEY of somebody else");
        Directory.CreateSymbolicLink(Path.Combine(copy.Root, "vendor"), elsewhere.Root);

        Answer answer = copy.Files.Search("PRIVATE KEY", null, null);

        Assert.False(copy.Files.Read("vendor/id_rsa", null, null).IsSuccess);
        Assert.DoesNotContain("somebody else", answer.Data!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A link back to its own folder makes the walk endless: the same files are named again on every turn until the
    /// system stops resolving the path (32 links on macOS) and the enumeration throws.
    /// </summary>
    [Fact]
    public void FindFiles_LinkCycleInsideTheCopy_NamesEveryFileOnce()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "");
        Directory.CreateSymbolicLink(Path.Combine(copy.Root, "src", "loop"), Path.Combine(copy.Root, "src"));

        JsonArray files = copy.Files.Find("App.cs", null).Data!["files"]!.AsArray();

        Assert.Single(files);
    }

    /// <summary>A link to a file that was deleted is the most ordinary leftover of a build; one of them must not cost the whole search.</summary>
    [Fact]
    public void SearchText_DanglingLinkInTheCopy_StillAnswersWithTheMatches()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var needle = 1;");
        File.CreateSymbolicLink(Path.Combine(copy.Root, "gone.cs"), Path.Combine(copy.Root, "deleted.cs"));

        Answer answer = copy.Files.Search("needle", null, null);

        Assert.True(answer.IsSuccess);
        Assert.Single(answer.Data!["matches"]!.AsArray());
    }

    /// <summary>
    /// A folder the process may not open — a volume mounted as root, another user's cache — throws out of the enumeration,
    /// and the exception takes every match already found with it, against the promise that every answer is JSON.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void SearchText_FolderThatMayNotBeOpened_StillAnswersWithTheMatches()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var needle = 1;");
        copy.Write("locked/Secret.cs", "var needle = 2;");
        string locked = Path.Combine(copy.Root, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            Answer answer = copy.Files.Search("needle", null, null);

            Assert.True(answer.IsSuccess);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// A named pipe has a length of zero, so it passes the size check, and opening it for reading waits for a writer that
    /// never comes. Nothing in the call can be cancelled, so the search — and the thread under it — waits forever.
    /// </summary>
    [Fact]
    public async Task SearchText_NamedPipeInTheCopy_AnswersInsteadOfWaitingForever()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var needle = 1;");
        string pipe = Path.Combine(copy.Root, "events.pipe");
        using (var made = Process.Start("mkfifo", pipe))
        {
            await made.WaitForExitAsync();
        }

        Task<Answer> search = Task.Run(() => copy.Files.Search("needle", null, null));
        Task first = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(5)));

        if (first != search)
        {
            await using (new FileStream(pipe, FileMode.Open, FileAccess.Write))
            {
            }

            await search;
        }

        Assert.Same(search, first);
    }

    /// <summary>The same pipe read by name: <c>read_file</c> must answer that it holds nothing rather than wait for a writer.</summary>
    [Fact]
    public async Task ReadFile_NamedPipe_AnswersInsteadOfWaitingForever()
    {
        using var copy = new Copy();
        string pipe = Path.Combine(copy.Root, "events.pipe");
        using (var made = Process.Start("mkfifo", pipe))
        {
            await made.WaitForExitAsync();
        }

        Task<Answer> read = Task.Run(() => copy.Files.Read("events.pipe", null, null));
        Task first = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));

        if (first != read)
        {
            await using (new FileStream(pipe, FileMode.Open, FileAccess.Write))
            {
            }

            await read;
        }

        Assert.Same(read, first);
    }

    /// <summary>The root itself is a path inside the root; writing to it must be refused as data, not thrown.</summary>
    [Fact]
    public void WriteFile_PathIsTheRootFolderItself_IsRefusedAsData()
    {
        using var copy = new Copy();

        Answer answer = copy.Files.Write(".", "text");

        Assert.False(answer.IsSuccess);
    }

    /// <summary>Withdrawn if it passes: a link that leads to itself fails to resolve, and the check must not throw on it.</summary>
    [Fact]
    public void ReadFile_LinkThatLeadsToItself_IsRefusedAsData()
    {
        using var copy = new Copy();
        string loop = Path.Combine(copy.Root, "loop");
        File.CreateSymbolicLink(loop, loop);

        Answer answer = copy.Files.Read("loop", null, null);

        Assert.False(answer.IsSuccess);
    }

    /// <summary>
    /// Withdrawn if it passes: the file tools keep no state of their own, so calls of one session in parallel — which a
    /// client sends whenever a model asks several things at once — should not disturb each other.
    /// </summary>
    [Fact]
    public void Files_ReadsAndSearchesInParallel_EveryOneAnswers()
    {
        using var copy = new Copy();

        for (int file = 0; file < 20; file++)
        {
            copy.Write($"src/Type{file}.cs", $"var needle = {file};");
        }

        var answers = new Answer[256];

        Parallel.For(0, answers.Length, at => answers[at] = (at % 4) switch
        {
            0 => copy.Files.Read($"src/Type{at % 20}.cs", null, null),
            1 => copy.Files.Search("needle", null, "*.cs"),
            2 => copy.Files.Find("**/*.cs", null),
            _ => copy.Files.List(null, 3)
        });

        Assert.All(answers, answer => Assert.True(answer.IsSuccess, answer.Error));
    }
}
