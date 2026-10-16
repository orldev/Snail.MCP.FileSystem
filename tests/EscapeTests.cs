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

        Answer answer = copy.Files.Read(path, null, null);

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

            Answer answer = copy.Files.Read(Path.Combine(copy.Root, "out", "secret.txt"), null, null);

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
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
        Files files = Copy.At(config, new Address());

        Answer answer = files.Read("/etc/passwd", null, null);

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
        Files files = Copy.At(config, new Address(mine.Root));

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

        var config = new ServerConfig { Roots = [] };
        Files files = Copy.At(config, new Address(mine.Root));

        Assert.True(files.Read(Path.Combine(mine.Root, "own.txt"), null, null).IsSuccess);
        Assert.False(files.Read(Path.Combine(theirs.Root, "own.txt"), null, null).IsSuccess);
    }
}
