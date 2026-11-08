using System.Runtime.Versioning;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// A path is checked once, as text with its links resolved, and then used. Whatever makes two strings name one file, or
/// one string name another file a moment later, passes between the check and the use.
/// </summary>
public sealed class PathChaosTests
{
    /// <summary>
    /// <c>Reach</c> resolves every link and hands back the real path; the write then opened that path by name, following
    /// whatever was there by then. Anything else that writes the copy — a shell of the same agent, a build, a hostile
    /// dependency — could swap a folder on the path for a link between the two, and the write landed outside the session
    /// within three to six writes. On the Swarm stand the server runs as root.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Write_FolderSwappedForALinkDuringTheCall_NeverLandsOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();

        int attempts = await RacedAsync(copy, outside, () =>
        {
            copy.Files.Write("real/x.txt", "escaped");

            return Directory.GetFileSystemEntries(outside).Length > 0;
        });

        Assert.True(Directory.GetFileSystemEntries(outside).Length == 0,
            $"after {attempts} writes the server wrote {string.Join(", ", Directory.GetFileSystemEntries(outside).Select(Path.GetFileName))} outside the session");
    }

    /// <summary>The folders above a new file are made one by one; a link swapped in among them made the next one outside the copy.</summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Write_NewFoldersUnderAFolderSwappedForALink_AreNeverMadeOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();

        int attempts = await RacedAsync(copy, outside, () =>
        {
            copy.Files.Write("real/made/deeper/x.txt", "escaped");

            return Directory.GetFileSystemEntries(outside).Length > 0;
        });

        Assert.True(Directory.GetFileSystemEntries(outside).Length == 0,
            $"after {attempts} writes the server made {string.Join(", ", Directory.GetFileSystemEntries(outside).Select(Path.GetFileName))} outside the session");
    }

    /// <summary>
    /// The read follows the same path: <c>read_file</c> and <c>search_text</c> opened the checked path by name, and a folder
    /// swapped for a link in between showed the model a file of another task, or of the host.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Read_FolderSwappedForALinkDuringTheCall_NeverShowsAFileOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(outside, "notes.txt"), "secret of another task");
        copy.Write("real/notes.txt", "own notes");
        bool isShown = false;

        int attempts = await RacedAsync(copy, outside, () =>
        {
            Answer<JsonObject> answer = copy.Files.Read("real/notes.txt", null, null);
            isShown = answer.IsSuccess && answer.Value!["text"]!.GetValue<string>().Contains("secret", StringComparison.Ordinal);

            return isShown;
        });

        Assert.False(isShown, $"read_file showed a file outside the session after {attempts} reads");
    }

    /// <summary>The search reads every file it walks by name, and showed the lines of a file outside the session the same way.</summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Search_FolderSwappedForALinkDuringTheCall_NeverShowsALineOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(outside, "notes.txt"), "secret of another task");
        copy.Write("real/notes.txt", "own notes");
        bool isShown = false;

        int attempts = await RacedAsync(copy, outside, () =>
        {
            Answer<JsonObject> answer = copy.Files.Search("secret", null, null);
            isShown = answer.IsSuccess && answer.Value!["matches"]!.AsArray().Count > 0;

            return isShown;
        });

        Assert.False(isShown, $"search_text showed a line of a file outside the session after {attempts} searches");
    }

    /// <summary>
    /// <c>list_directory</c> enumerated each folder by name, and a folder swapped for a link out of the copy between the
    /// check and the enumeration named the files outside the session, and their sizes.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task List_FolderSwappedForALinkDuringTheCall_NeverNamesAFileOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(outside, "secret-of-another-task.txt"), "secret");
        copy.Write("real/notes.txt", "own notes");
        string shown = "";

        int attempts = await RacedAsync(copy, outside, () =>
        {
            Answer<JsonObject> answer = copy.Files.List(null, 2);
            shown = answer.Value?.ToJsonString() ?? "";

            return shown.Contains("secret-of-another-task", StringComparison.Ordinal);
        });

        Assert.False(shown.Contains("secret-of-another-task", StringComparison.Ordinal), $"list_directory named a file outside the session after {attempts} listings");
    }

    /// <summary><c>find_files</c> walks the same way and named the files outside the session the same way.</summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Find_FolderSwappedForALinkDuringTheCall_NeverNamesAFileOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(outside, "secret-of-another-task.txt"), "secret");
        copy.Write("real/notes.txt", "own notes");
        string shown = "";

        int attempts = await RacedAsync(copy, outside, () =>
        {
            Answer<JsonObject> answer = copy.Files.Find("**/*.txt", null);
            shown = answer.Value?.ToJsonString() ?? "";

            return shown.Contains("secret-of-another-task", StringComparison.Ordinal);
        });

        Assert.False(shown.Contains("secret-of-another-task", StringComparison.Ordinal), $"find_files named a file outside the session after {attempts} searches");
    }

    /// <summary>
    /// A Linux without <c>openat2</c> — an old kernel, gVisor, an amd64 image under Rosetta — refused every read and write:
    /// the image built for amd64 answered <c>write_file</c> with "Function not implemented". There the path is opened one
    /// folder at a time, which must still write and read, and still never through a link swapped in.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_OnASystemWithoutOpenat2_WritesEditsAndReads()
    {
        using var copy = new Copy();

        Answer<JsonObject>[] answers = FolderByFolder<Answer<JsonObject>[]>(() =>
        [
            copy.Files.Write("made/deeper/notes.txt", "own notes"),
            copy.Files.Edit("made/deeper/notes.txt", "own", "edited", all: false),
            copy.Files.Read("made/deeper/notes.txt", null, null)
        ]);

        Assert.All(answers, answer => Assert.True(answer.IsSuccess, answer.Error));
        Assert.Contains("edited notes", answers[2].Value!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>Opened one folder at a time, a folder swapped for a link out of the copy is refused as <c>openat2</c> refuses it.</summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Write_FolderSwappedForALinkOnASystemWithoutOpenat2_NeverLandsOutside()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();

        int attempts = await RacedAsync(copy, outside, () =>
        {
            FolderByFolder(() => copy.Files.Write("real/x.txt", "escaped"));

            return Directory.GetFileSystemEntries(outside).Length > 0;
        });

        Assert.True(Directory.GetFileSystemEntries(outside).Length == 0,
            $"after {attempts} writes the server wrote {string.Join(", ", Directory.GetFileSystemEntries(outside).Select(Path.GetFileName))} outside the session");
    }

    /// <summary>A file read through a link on a system without <c>openat2</c> is refused, not read.</summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FolderThatIsALinkOnASystemWithoutOpenat2_IsRefused()
    {
        using var copy = new Copy();
        string outside = TestFolder.Create();
        File.WriteAllText(Path.Combine(outside, "notes.txt"), "secret of another task");
        Directory.CreateSymbolicLink(Path.Combine(copy.Root, "real"), outside);

        Assert.ThrowsAny<IOException>(() => FolderByFolder(() => copy.Disk.Bytes(Path.Combine(Real(copy.Root), "real", "notes.txt"))));
    }

    /// <summary>
    /// An answer names a path relative to the session's root, so the model can hand it back. The comparison was made
    /// against the root as configured, the path against the root with its links resolved: a root that is a link — a home
    /// folder on another volume, <c>/tmp</c> and <c>/var</c> on macOS, a mounted workspace — made every answer name the
    /// host's absolute real path instead.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_RootThatIsALink_NamesThePathRelativeToIt()
    {
        using var copy = new Copy();
        copy.Write("a.txt", "x");
        string link = Path.Combine(TestFolder.Create(), "copy");
        Directory.CreateSymbolicLink(link, copy.Root);
        Files files = Copy.At(new ServerConfig { Roots = [link] }, Address.Of());

        Answer<JsonObject> answer = files.Read("a.txt", null, null);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal("a.txt", answer.Value!["path"]!.GetValue<string>());
    }

    /// <summary>
    /// A relative path is reached from the first root, and a file under the second was named relative to its own: handed
    /// back, <c>a.txt</c> of the second copy read <c>a.txt</c> of the first.
    /// </summary>
    [Fact]
    public void Read_FileUnderTheSecondRoot_NamesAPathThatReachesItAgain()
    {
        string volume = Real(TestFolder.Create());
        string first = Path.Combine(volume, "alpha");
        string second = Path.Combine(volume, "beta");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "a.txt"), "first");
        File.WriteAllText(Path.Combine(second, "a.txt"), "second");
        Files files = Copy.At(new ServerConfig { Roots = [Path.GetTempPath()] }, Address.Of(first, second));

        string named = files.Read(Path.Combine(second, "a.txt"), null, null).Value!["path"]!.GetValue<string>();
        Answer<JsonObject> again = files.Read(named, null, null);

        Assert.Equal("second", again.Value!["text"]!.GetValue<string>());
    }

    /// <summary>
    /// The lock of an edit was chosen by the path as the caller spelled it. On a disk that ignores case — the default of
    /// macOS, where the stdio server runs — <c>slots.txt</c> and <c>SLOTS.TXT</c> are one file under two locks, and 16 of 32
    /// parallel edits were lost while every caller heard ok. On a disk that keeps case the test proves nothing and passes.
    /// </summary>
    [Fact]
    public async Task Edit_ParallelEditsNamingOneFileInTwoCases_LoseNoReplacement()
    {
        using var copy = new Copy();
        const int slots = 32;
        string path = copy.Write("slots.txt", string.Concat(Enumerable.Range(0, slots).Select(slot => $"slot{slot}\n")));

        if (!File.Exists(Path.Combine(copy.Root, "SLOTS.TXT")))
        {
            return;
        }

        await Task.WhenAll(Enumerable.Range(0, slots).Select(slot => Task.Run(() =>
            copy.Files.Edit(slot % 2 == 0 ? "slots.txt" : "SLOTS.TXT", $"slot{slot}\n", $"done{slot}\n", all: false))));

        int left = (await File.ReadAllLinesAsync(path)).Count(line => line.StartsWith("slot", StringComparison.Ordinal));
        Assert.True(left == 0, $"{left} of {slots} edits are gone, and every caller heard ok");
    }

    /// <summary>Runs a step with paths opened one folder at a time, as on a Linux without <c>openat2</c>.</summary>
    /// <remarks>The switch is shared by every test running at once; both ways refuse the same paths, so none of them notices.</remarks>
    [UnsupportedOSPlatform("windows")]
    private static T FolderByFolder<T>(Func<T> step)
    {
        Adapters.LinklessDisk.WalksFolderByFolder = true;

        try
        {
            return step();
        }
        finally
        {
            Adapters.LinklessDisk.WalksFolderByFolder = false;
        }
    }

    /// <summary>Calls the server for five seconds, or until a call reports it reached outside, while the folder <c>real</c> keeps turning into a link out of the copy and back.</summary>
    private static async Task<int> RacedAsync(Copy copy, string outside, Func<bool> hasReachedOutside)
    {
        string folder = Path.Combine(copy.Root, "real");
        string parked = Path.Combine(copy.Root, "parked");
        Directory.CreateDirectory(folder);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var swapping = Task.Run(() => Swap(folder, parked, outside, stop.Token));
        int attempts = 0;
        bool isOutside = false;

        while (!stop.IsCancellationRequested && !isOutside)
        {
            isOutside = hasReachedOutside();
            attempts++;
        }

        await stop.CancelAsync();
        await swapping;

        return attempts;
    }

    /// <summary>Turns the folder into a link out of the copy and back, as fast as the disk allows, until stopped.</summary>
    private static void Swap(string folder, string parked, string outside, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            Quietly(() => Directory.Move(folder, parked));
            Quietly(() => Directory.CreateSymbolicLink(folder, outside));
            Quietly(() => Directory.Delete(folder));
            Quietly(() => Directory.Move(parked, folder));
            Quietly(() =>
            {
                if (Directory.Exists(parked) && Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            });
        }
    }

    /// <summary>The folder with every link on its path resolved, as the server resolves a root: on macOS the temporary folder is under the link <c>/var</c>.</summary>
    private static string Real(string folder) => RealPath.Of(folder)!;

    private static void Quietly(Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
