using System.Diagnostics;
using Snail.MCP.FileSystem.Tests.Support;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>
/// Every refusal of the server's own carries a type, the word a program branches on: the error is prose for a person, and
/// a program that matched it broke the first time a sentence was reworded.
/// </summary>
public sealed class RefusalTypeTests
{
    [Fact]
    public void Write_ServerStartedReadOnly_IsReadOnly()
    {
        using var copy = new Copy(config => config.AllowWrite = false);

        Assert.Equal(Refusals.ReadOnly, copy.Files.Write("App.cs", "class App;").Type);
        Assert.Equal(Refusals.ReadOnly, copy.Files.Edit("App.cs", "a", "b", all: false).Type);
    }

    [Fact]
    public void Write_FolderNamed_IsNotAFile()
    {
        using var copy = new Copy();
        copy.Write("src/App.cs", "");

        Assert.Equal(Reached.NotAFile, copy.Files.Write("src", "class App;").Type);
    }

    [Fact]
    public void Edit_NoTextToReplace_IsArguments()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var x = 1;");

        Assert.Equal(ToolFailures.Arguments, copy.Files.Edit("App.cs", "", "y", all: false).Type);
    }

    [Fact]
    public void Edit_TextTheFileDoesNotContain_IsNoMatch()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var x = 1;");
        copy.Write("Empty.cs", "");

        Assert.Equal(Refusals.NoMatch, copy.Files.Edit("App.cs", "var y = 2;", "var y = 3;", all: false).Type);
        Assert.Equal(Refusals.NoMatch, copy.Files.Edit("Empty.cs", "var y = 2;", "var y = 3;", all: false).Type);
    }

    [Fact]
    public void Edit_TextThatAppearsTwice_IsAmbiguous()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "log(); log();");

        Assert.Equal(Refusals.Ambiguous, copy.Files.Edit("App.cs", "log()", "trace()", all: false).Type);
    }

    [Fact]
    public void ReadAndEdit_FilePastMaxFileBytes_IsTooLarge()
    {
        using var copy = new Copy(config => config.MaxFileBytes = 1024);
        copy.Write("Big.cs", new string('a', 2048));

        Assert.Equal(Refusals.TooLarge, copy.Files.Read("Big.cs", null, null).Type);
        Assert.Equal(Refusals.TooLarge, copy.Files.Edit("Big.cs", "a", "b", all: true).Type);
    }

    [Fact]
    public void Edit_ReplacementTheFilesBytesCannotHold_IsUnencodable()
    {
        using var copy = new Copy();
        string path = copy.Write("about.txt", "");
        File.WriteAllBytes(path, [.. "author: Ren"u8, 0xE9, .. "\nversion: 1\n"u8]);

        Assert.Equal(Refusals.Unencodable, copy.Files.Edit("about.txt", "version: 1", "версия: 2", all: false).Type);
    }

    [Fact]
    public void Write_HalfOfASurrogatePair_IsArguments()
    {
        using var copy = new Copy();

        Assert.Equal(ToolFailures.Arguments, copy.Files.Write("App.cs", "a\uD800").Type);
    }

    [Fact]
    public void FindAndSearch_NothingOrSeveralLinesToLookFor_IsArguments()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "var x = 1;");

        Assert.Equal(ToolFailures.Arguments, copy.Files.Find(" ", null).Type);
        Assert.Equal(ToolFailures.Arguments, copy.Files.Search(" ", null, null).Type);
        Assert.Equal(ToolFailures.Arguments, copy.Files.Search("one\ntwo", null, null).Type);
    }

    [Fact]
    public void Find_PatternThatBacktracks_IsTooComplex()
    {
        using var copy = new Copy();
        copy.Write($"{new string('a', 64)}.txt", "x");

        var clock = Stopwatch.StartNew();
        Answer<JsonObject> answer = copy.Files.Find("*a*a*a*a*a*a*a*a*a*a*a*a*a*a*b", null);

        Assert.Equal(Refusals.TooComplex, answer.Type);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"the refusal took {clock.ElapsedMilliseconds} ms");
    }
}
