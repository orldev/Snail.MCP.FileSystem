using System.Text.Json.Nodes;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>What the server does to the files of the copy when the path is allowed to it.</summary>
public sealed class FileTests
{
    [Fact]
    public void Read_WholeFile_SaysHowManyLinesItHas()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "one\ntwo\nthree\n");

        Answer answer = copy.Files.Read("src/App.cs", null, null);

        Assert.True(answer.IsSuccess);
        Assert.Equal(3, answer.Data!["lines"]!.GetValue<int>());
        Assert.Contains("two", answer.Data!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>A file not needed whole is read by lines: the answer says which lines it holds.</summary>
    [Fact]
    public void Read_ByLines_ReturnsThoseLinesAndNamesThem()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "one\ntwo\nthree\nfour\n");

        Answer answer = copy.Files.Read("src/App.cs", 2, 3);

        Assert.Equal("two\nthree", answer.Data!["text"]!.GetValue<string>());
        Assert.Equal(2, answer.Data!["from"]!.GetValue<int>());
        Assert.Equal(3, answer.Data!["to"]!.GetValue<int>());
    }

    [Fact]
    public void Write_NewFileInNewFolders_SaysItWasCreated()
    {
        using var copy = new Copy();

        Answer answer = copy.Files.Write("deep/inside/New.cs", "class New;");

        Assert.True(answer.IsSuccess);
        Assert.True(answer.Data!["isCreated"]!.GetValue<bool>());
        Assert.Equal("class New;", File.ReadAllText(Path.Combine(copy.Root, "deep/inside/New.cs")));
    }

    /// <summary>
    /// A replacement the file does not contain is the caller believing what the file does not say. Writing it would
    /// agree with the mistake; the refusal sends the caller to read the file.
    /// </summary>
    [Fact]
    public void Edit_TextTheFileDoesNotContain_IsRefused()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var x = 1;");

        Answer answer = copy.Files.Edit("App.cs", "var y = 2;", "var y = 3;", all: false);

        Assert.False(answer.IsSuccess);
        Assert.Contains("does not contain", answer.Error, StringComparison.Ordinal);
        Assert.Equal("var x = 1;", File.ReadAllText(Path.Combine(copy.Root, "App.cs")));
    }

    /// <summary>An ambiguous replacement is two edits instead of one; only the caller knows which of them was meant.</summary>
    [Fact]
    public void Edit_TextThatAppearsTwice_IsRefusedUntilAllIsPassed()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "log(); log();");

        Assert.False(copy.Files.Edit("App.cs", "log()", "trace()", all: false).IsSuccess);

        Answer both = copy.Files.Edit("App.cs", "log()", "trace()", all: true);

        Assert.True(both.IsSuccess);
        Assert.Equal(2, both.Data!["replaced"]!.GetValue<int>());
        Assert.Equal("trace(); trace();", File.ReadAllText(Path.Combine(copy.Root, "App.cs")));
    }

    [Fact]
    public void List_FolderWithBuildOutput_NamesTheRestAndSkipsIt()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "");
        copy.Write("obj/Debug/App.g.cs", "");

        var paths = copy.Files.List(null, 3).Data!["entries"]!.AsArray()
            .Select(entry => entry!["path"]!.GetValue<string>())
            .ToList();

        Assert.Contains(paths, path => path.EndsWith("App.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("obj", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("*.cs", "App.cs")]
    [InlineData("**/Tests/*.cs", "src/Tests/AppTests.cs")]
    [InlineData("appsettings.*.json", "appsettings.Development.json")]
    public void Find_Glob_NamesTheFilesOfThatShape(string pattern, string expected)
    {
        using var copy = new Copy();
        copy.Write("App.cs", "");
        copy.Write("src/Tests/AppTests.cs", "");
        copy.Write("appsettings.Development.json", "{}");

        var found = copy.Files.Find(pattern, null).Data!["files"]!.AsArray()
            .Select(file => file!.GetValue<string>().Replace('\\', '/'))
            .ToList();

        Assert.Contains(expected, found);
    }

    [Fact]
    public void Search_TextInAFile_NamesTheFileAndTheLine()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "using System;\nvar wanted = 1;\n");

        JsonArray matches = copy.Files.Search("wanted", null, null).Data!["matches"]!.AsArray();

        JsonNode? first = Assert.Single(matches);

        Assert.Equal(2, first!["line"]!.GetValue<int>());
        Assert.Contains("wanted", first!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>A role given a read-only server does not write, and hears why.</summary>
    [Fact]
    public void Write_ServerStartedReadOnly_IsRefused()
    {
        using var copy = new Copy(config => config.AllowWrite = false);

        Answer answer = copy.Files.Write("App.cs", "class App;");

        Assert.False(answer.IsSuccess);
        Assert.Contains("read-only", answer.Error, StringComparison.Ordinal);
    }
}
