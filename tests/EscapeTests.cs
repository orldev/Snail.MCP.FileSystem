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
    public void A_path_that_climbs_out_of_the_root_is_refused(string path)
    {
        using var copy = new Copy();

        var answer = copy.Files.Read(path, null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("outside this session", answer.Error!, StringComparison.Ordinal);
    }

    /// <summary>A neighbour whose name merely starts the same way is the classic way out of the cage.</summary>
    [Fact]
    public void A_sibling_whose_name_merely_begins_the_same_way_is_outside()
    {
        using var copy = new Copy();

        var sibling = copy.Root + "-secrets";
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
    public void A_symlink_that_leads_out_of_the_root_does_not_pass_as_a_path_inside_it()
    {
        using var copy = new Copy();

        var elsewhere = Directory.CreateTempSubdirectory("snail-mcp-filesystem-elsewhere").FullName;
        File.WriteAllText(Path.Combine(elsewhere, "secret.txt"), "not yours");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(copy.Root, "out"), elsewhere);

            var answer = copy.Files.Read(Path.Combine(copy.Root, "out", "secret.txt"), null, null);

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>A write outside spoils somebody else's work, and it is checked on the same path as a read.</summary>
    [Fact]
    public void A_write_outside_the_root_is_refused_before_anything_is_written()
    {
        using var copy = new Copy();

        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}.txt");

        Assert.False(copy.Files.Write(elsewhere, "mine now").IsSuccess);
        Assert.False(File.Exists(elsewhere));
    }

    /// <summary>A session without a root gets nothing: closed by default, not open to everything.</summary>
    [Fact]
    public void A_session_given_no_root_reaches_nothing()
    {
        var config = new ServerConfig { Roots = [] };
        var files = new Files(new SessionRoots(config, new Address()), config);

        var answer = files.Read("/etc/passwd", null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("no root", answer.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shared server is configured with the whole volume of working copies, or it could serve none of them. Adding
    /// the configured root to the session's would hand every session the files of every task at once, breaking exactly
    /// the isolation all of this is written for. The root of the address does not add to the configured ones, it replaces them.
    /// </summary>
    [Fact]
    public void A_session_that_names_its_root_does_not_also_get_the_volume_the_server_was_configured_with()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        theirs.Write("secret.txt", "not yours");

        var config = new ServerConfig { Roots = [Path.GetTempPath()] };
        var files = new Files(new SessionRoots(config, new Address(mine.Root)), config);

        Assert.False(files.Read(Path.Combine(theirs.Root, "secret.txt"), null, null).IsSuccess);
    }

    /// <summary>The root from the address is what it is all for: one server behind several agents.</summary>
    [Fact]
    public void A_root_named_by_the_address_is_the_one_served()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        var config = new ServerConfig { Roots = [] };
        var files = new Files(new SessionRoots(config, new Address(mine.Root)), config);

        Assert.True(files.Read(Path.Combine(mine.Root, "own.txt"), null, null).IsSuccess);
        Assert.False(files.Read(Path.Combine(theirs.Root, "own.txt"), null, null).IsSuccess);
    }
}
