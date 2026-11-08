using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// The disk failing under a call, through the port every file operation goes through: a share that times out, a disk
/// that is full, a file gone between being listed and being looked at. None of them may cost the answer its shape, and
/// none may cost a file that was there before the call.
/// </summary>
public sealed class DiskChaosTests
{
    /// <summary>One file of a search on a share that stops answering: the rest is still searched, and that file is named.</summary>
    [Fact]
    public void SearchText_ShareTimesOutOnOneFile_AnswersWithTheOthersAndNamesIt()
    {
        var disk = new FailingDisk { IsFailing = path => path.EndsWith("Slow.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("Fast.cs", "var needle = 1;");
        copy.Write("Slow.cs", "var needle = 2;");

        Answer<JsonObject> answer = copy.Files.Search("needle", null, null);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Single(answer.Value!["matches"]!.AsArray());
        Assert.Contains("Slow.cs", answer.Value!["unread"]!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>An edit that cannot read its file is refused in the server's own shape, and names why.</summary>
    [Fact]
    public void EditFile_ShareTimesOutOnRead_IsRefusedWithTheReason()
    {
        var disk = new FailingDisk { IsFailing = path => path.EndsWith("App.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("App.cs", "var a = 1;");

        Answer<JsonObject> answer = copy.Files.Edit("App.cs", "var a = 1;", "var a = 2;", all: false);

        Assert.False(answer.IsSuccess);
        Assert.Contains("timed out", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>A disk full in the middle of a write leaves the file as it was: the new text is written beside it first.</summary>
    [Fact]
    public void WriteFile_DiskFullWhileReplacing_LeavesTheOldFileWhole()
    {
        var disk = new FailingDisk { IsFull = true };
        using var copy = new Copy(disk: disk);
        string path = copy.Write("App.cs", "class Old;");

        Answer<JsonObject> answer = copy.Files.Write("App.cs", "class New;");

        Assert.False(answer.IsSuccess);
        Assert.Equal("class Old;", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(copy.Root));
    }

    /// <summary>A file deleted after the folder was listed and before its size was read: the listing still answers.</summary>
    [Fact]
    public void ListDirectory_FileGoneBetweenListingAndLooking_StillAnswersWithTheRest()
    {
        var disk = new FailingDisk { IsGone = path => path.EndsWith("Gone.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("Here.cs", "");
        copy.Write("Gone.cs", "");

        Answer<JsonObject> answer = copy.Files.List(null, 1);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Contains("Here.cs", answer.Value!["entries"]!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A share that stops answering while the path is reached: <c>SessionRoots.Reach</c> ran on the machine's disk whatever
    /// disk the server was given, so no test could fail it, and what the disk threw there was the server's own failure — an
    /// <c>internal</c> "try once more". Reached on the server's disk, it is the fleet's <c>unreadable</c>, with what to do.
    /// </summary>
    [Fact]
    public void ReadFile_ShareTimesOutWhileReached_IsUnreadableWithAHint()
    {
        var disk = new FailingDisk { IsUnanswering = path => path.EndsWith("App.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("App.cs", "var a = 1;");

        Answer<JsonObject> answer = copy.Files.Read("App.cs", null, null);

        Assert.False(answer.IsSuccess);
        Assert.Equal(Reached.Unreadable, answer.Type);
        Assert.Contains("timed out", answer.Error, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(answer.Hint));
    }

    /// <summary>
    /// The server's roots are reached on the disk its files are read on, the one object: a disk a test fails, or a share
    /// that stops answering, fails the reaching of a path too, rather than <c>Reach</c> asking the machine behind its back.
    /// </summary>
    [Fact]
    public async Task Server_RootsAndFiles_ShareOneDisk()
    {
        await using InProcessServer server = await Listening.StartedAsync(new ServerConfig());

        IWorkingCopy disk = server.Services.GetRequiredService<IWorkingCopy>();
        SessionRootsOptions roots = server.Services.GetRequiredService<SessionRootsOptions>();

        Assert.Same(disk, roots.Disk);
    }

    /// <summary>A folder on a share that stops answering while the listing reaches it: the same refusal, never the server's failure.</summary>
    [Fact]
    public void ListDirectory_ShareTimesOutWhileReached_IsUnreadable()
    {
        var disk = new FailingDisk { IsUnanswering = path => path.EndsWith("share", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("share/App.cs", "");

        Answer<JsonObject> answer = copy.Files.List("share", 1);

        Assert.False(answer.IsSuccess);
        Assert.Equal(Reached.Unreadable, answer.Type);
    }

    /// <summary>A file deleted after it was reached and before it was read: the disk's own refusal, in the server's shape.</summary>
    [Fact]
    public void ReadFile_FileGoneBetweenReachingAndReading_IsRefusedWithTheReason()
    {
        var disk = new FailingDisk { IsGone = path => path.EndsWith("Gone.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("Gone.cs", "var a = 1;");

        Answer<JsonObject> answer = copy.Files.Read("Gone.cs", null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("could not be used", answer.Error, StringComparison.Ordinal);
        Assert.Contains("deleted a moment ago", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file in a folder the process may not enter is there: <c>File.Exists</c> answered false for it, and the model was
    /// told "there is no file" and went looking for a name it had right.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void ReadFile_InAFolderThatMayNotBeEntered_IsUnreadableNotAbsent()
    {
        using var copy = new Copy();
        copy.Write("locked/Secret.cs", "var a = 1;");
        string locked = Path.Combine(copy.Root, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            Answer<JsonObject> answer = copy.Files.Read("locked/Secret.cs", null, null);

            Assert.False(answer.IsSuccess);
            Assert.Equal(Reached.Unreadable, answer.Type);
            Assert.DoesNotContain("there is no file", answer.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>The machine's disk, failing where it is told to.</summary>
    private sealed class FailingDisk : IWorkingCopy
    {
        private readonly LocalDisk _disk = new();

        public Func<string, bool> IsFailing { get; init; } = _ => false;

        public Func<string, bool> IsGone { get; init; } = _ => false;

        /// <summary>Paths the disk does not answer for while they are reached: what is there, where a link leads.</summary>
        public Func<string, bool> IsUnanswering { get; init; } = _ => false;

        public bool IsFull { get; init; }

        public bool IsFile(string path) => IsUnanswering(path) ? throw new IOException("Operation timed out") : _disk.IsFile(path);

        public bool IsFolder(string path) => IsUnanswering(path) ? throw new IOException("Operation timed out") : _disk.IsFolder(path);

        public string? LinkTarget(string path) => IsUnanswering(path) ? throw new IOException("Operation timed out") : _disk.LinkTarget(path);

        public long Length(string path) =>
            IsGone(path) ? throw new FileNotFoundException("the file was deleted a moment ago", path) : _disk.Length(path);

        public IEnumerable<string> Lines(string path, int longest) =>
            IsFailing(path) ? throw new IOException("Operation timed out")
            : IsGone(path) ? throw new FileNotFoundException("the file was deleted a moment ago", path)
            : _disk.Lines(path, longest);

        public byte[] Bytes(string path) =>
            IsFailing(path) ? throw new IOException("Operation timed out")
            : IsGone(path) ? throw new FileNotFoundException("the file was deleted a moment ago", path)
            : _disk.Bytes(path);

        public Contents Contents(string folder) => _disk.Contents(folder);

        public void MakeFolder(string path) => _disk.MakeFolder(path);

        public void Replace(string path, byte[] bytes)
        {
            if (IsFull)
            {
                throw new IOException("No space left on device");
            }

            _disk.Replace(path, bytes);
        }
    }
}
