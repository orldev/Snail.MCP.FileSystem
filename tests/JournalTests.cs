using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>One server changes the copies of every task of a machine; the journal says which change was made where.</summary>
public sealed class JournalTests
{
    [Fact]
    public void Write_NewFile_IsJournaledWithItsFullPathAndSize()
    {
        using var copy = new Copy();

        copy.Files.Write("notes.txt", "hello");

        string line = Assert.Single(copy.Journal.Lines);
        Assert.Contains(copy.Roots.Reach("notes.txt").Path!, line, StringComparison.Ordinal);
        Assert.Contains("5 bytes, created", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_EveryOccurrence_IsJournaledWithHowManyWereReplaced()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "a(); a();\n");

        copy.Files.Edit("App.cs", "a()", "b()", all: true);

        Assert.Contains("2 replacement(s)", Assert.Single(copy.Journal.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_Refused_IsNotJournaled()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "a();\n");

        copy.Files.Edit("App.cs", "missing", "b()", all: false);

        Assert.Empty(copy.Journal.Lines);
    }
}
