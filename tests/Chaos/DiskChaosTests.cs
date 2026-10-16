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

        Answer answer = copy.Files.Search("needle", null, null);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Single(answer.Data!["matches"]!.AsArray());
        Assert.Contains("Slow.cs", answer.Data!["unread"]!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>An edit that cannot read its file is refused in the server's own shape, and names why.</summary>
    [Fact]
    public void EditFile_ShareTimesOutOnRead_IsRefusedWithTheReason()
    {
        var disk = new FailingDisk { IsFailing = path => path.EndsWith("App.cs", StringComparison.Ordinal) };
        using var copy = new Copy(disk: disk);
        copy.Write("App.cs", "var a = 1;");

        Answer answer = copy.Files.Edit("App.cs", "var a = 1;", "var a = 2;", all: false);

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

        Answer answer = copy.Files.Write("App.cs", "class New;");

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

        Answer answer = copy.Files.List(null, 1);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Contains("Here.cs", answer.Data!["entries"]!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>The machine's disk, failing where it is told to.</summary>
    private sealed class FailingDisk : IWorkingCopy
    {
        private readonly LocalDisk _disk = new();

        public Func<string, bool> IsFailing { get; init; } = _ => false;

        public Func<string, bool> IsGone { get; init; } = _ => false;

        public bool IsFull { get; init; }

        public bool IsFile(string path) => _disk.IsFile(path);

        public bool IsFolder(string path) => _disk.IsFolder(path);

        public long Length(string path) =>
            IsGone(path) ? throw new FileNotFoundException("the file was deleted a moment ago", path) : _disk.Length(path);

        public IEnumerable<string> Lines(string path) =>
            IsFailing(path) ? throw new IOException("Operation timed out") : _disk.Lines(path);

        public byte[] Bytes(string path) =>
            IsFailing(path) ? throw new IOException("Operation timed out") : _disk.Bytes(path);

        public Contents Contents(string folder) => _disk.Contents(folder);

        public string? LinkTarget(string path) => _disk.LinkTarget(path);

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
