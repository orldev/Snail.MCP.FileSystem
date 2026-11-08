using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>
/// The border between the agent and every file of the machine. It is the only thing a file server has at all:
/// one call separates it from everything the process can read.
/// </summary>
public sealed class EscapeTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("./../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public void Read_PathClimbingOutOfTheRoot_IsRefused(string path)
    {
        using var copy = new Copy();

        Answer<JsonObject> answer = copy.Files.Read(path, null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("outside this session", answer.Error!, StringComparison.Ordinal);
    }

    /// <summary>A neighbour whose name merely starts the same way is the classic way out of the cage.</summary>
    [Fact]
    public void Read_SiblingWhoseNameBeginsTheSame_IsOutside()
    {
        using var copy = new Copy();

        string sibling = copy.Root + "-secrets";
        Directory.CreateDirectory(sibling);

        try
        {
            Assert.False(copy.Files.Read(Path.Combine(sibling, "key.txt"), null, null).IsSuccess);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    /// <summary>A symlink inside the copy is the cheapest way out of it, so links are resolved.</summary>
    [Fact]
    public void Read_LinkLeadingOutOfTheRoot_IsRefused()
    {
        using var copy = new Copy();

        string elsewhere = TestFolder.Create();
        File.WriteAllText(Path.Combine(elsewhere, "secret.txt"), "not yours");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(copy.Root, "out"), elsewhere);

            Answer<JsonObject> answer = copy.Files.Read(Path.Combine(copy.Root, "out", "secret.txt"), null, null);

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// A link whose target is not there yet: a walk that took a missing path for the end of the path judged the link by
    /// its own name, inside the copy, and a write through it created the file outside (Snail.Sdk's RealPath remarks). This
    /// server never wrote through one — its own walk failed on the missing target and refused the path as one that "cannot
    /// be followed", naming the target outside in the refusal — and Snail.Sdk's follows the link to where it leads and
    /// refuses it as outside the session, naming nothing beyond it.
    /// </summary>
    [Fact]
    public void Write_ThroughALinkToAFileNotThereYetOutsideTheRoot_IsRefusedAndCreatesNothing()
    {
        using var copy = new Copy();
        string elsewhere = TestFolder.Create();
        string target = Path.Combine(elsewhere, "planted.txt");

        try
        {
            File.CreateSymbolicLink(Path.Combine(copy.Root, "out.txt"), target);

            Answer<JsonObject> answer = copy.Files.Write("out.txt", "written through the link");

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(elsewhere, answer.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(target), "the write followed a dangling link out of the copy");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>The same through a folder: a link to a folder not there yet, and a file written under it.</summary>
    [Fact]
    public void Write_UnderALinkToAFolderNotThereYetOutsideTheRoot_IsRefusedAndCreatesNothing()
    {
        using var copy = new Copy();
        string elsewhere = TestFolder.Create();
        string target = Path.Combine(elsewhere, "missing");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(copy.Root, "out"), target);

            Answer<JsonObject> answer = copy.Files.Write("out/new.txt", "written through the link");

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(target), "the write made a folder outside the copy through a dangling link");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// A link inside the copy to a file not there yet is followed to where it leads and the file is created there, inside
    /// the copy; the link stays a link. The server's own walk refused such a write as a path that "cannot be followed".
    /// </summary>
    [Fact]
    public void Write_ThroughALinkToAFileNotThereYetInsideTheRoot_CreatesTheFileItLeadsTo()
    {
        using var copy = new Copy();
        string link = Path.Combine(copy.Root, "current.txt");
        File.CreateSymbolicLink(link, Path.Combine(copy.Root, "later.txt"));

        Answer<JsonObject> answer = copy.Files.Write("current.txt", "through the link");

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal("later.txt", answer.Value!["path"]!.GetValue<string>());
        Assert.Equal("through the link", File.ReadAllText(Path.Combine(copy.Root, "later.txt")));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    /// <summary>A write outside spoils somebody else's work, and it is checked on the same path as a read.</summary>
    [Fact]
    public void Write_OutsideTheRoot_IsRefusedBeforeAnythingIsWritten()
    {
        using var copy = new Copy();

        string elsewhere = Path.Combine(TestFolder.Create(), "mine-now.txt");

        Assert.False(copy.Files.Write(elsewhere, "mine now").IsSuccess);
        Assert.False(File.Exists(elsewhere));
    }

    /// <summary>A session without a root gets nothing: closed by default, not open to everything.</summary>
    [Fact]
    public void Session_GivenNoRoot_ReachesNothing()
    {
        var config = new ServerConfig { Roots = [] };
        Files files = Copy.At(config, Address.Of());

        Answer<JsonObject> answer = files.Read("/etc/passwd", null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("no root", answer.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shared server is configured with the whole volume of working copies, or it could serve none of them. Adding
    /// the configured root to the session's would hand every session the files of every task at once, breaking exactly
    /// the isolation all of this is written for. The root of the address does not add to the configured ones, it replaces them.
    /// </summary>
    [Fact]
    public void Session_NamingItsRoot_DoesNotAlsoGetTheConfiguredVolume()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        theirs.Write("secret.txt", "not yours");

        var config = new ServerConfig { Roots = [Path.GetTempPath()] };
        Files files = Copy.At(config, Address.Of(mine.Root));

        Assert.False(files.Read(Path.Combine(theirs.Root, "secret.txt"), null, null).IsSuccess);
    }

    /// <summary>The root from the address is what it is all for: one server behind several agents.</summary>
    [Fact]
    public void Session_RootNamedByTheAddress_IsTheOneServed()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        var config = new ServerConfig { Roots = [Path.GetTempPath()] };
        Files files = Copy.At(config, Address.Of(mine.Root));

        Assert.True(files.Read(Path.Combine(mine.Root, "own.txt"), null, null).IsSuccess);
        Assert.False(files.Read(Path.Combine(theirs.Root, "own.txt"), null, null).IsSuccess);
    }
}
