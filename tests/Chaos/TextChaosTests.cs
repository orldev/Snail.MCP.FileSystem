using System.Text;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// <c>read_file</c> and <c>edit_file</c> decoded a file in two different ways: the read through the runtime's reader,
/// which honours every byte order mark and every kind of line end, the edit as UTF-8 or Latin-1 with one guess about line
/// ends for the whole file. Where the two disagreed, the model saw text it could not then edit, and was told the file did
/// not contain it.
/// </summary>
public sealed class TextChaosTests
{
    /// <summary>
    /// The line ends of an edit were chosen for the whole file: one <c>\r\n</c> anywhere made every <c>\n</c> of the text
    /// to find a <c>\r\n</c>. A file of mixed line ends — a Windows header pasted into a Unix file, the common state of a
    /// repository without .gitattributes — refused every multi-line edit of its <c>\n</c> part, though the text was exactly
    /// what <c>read_file</c> showed.
    /// </summary>
    [Fact]
    public void Edit_MultiLineTextInTheLfPartOfAMixedFile_IsReplaced()
    {
        using var copy = new Copy();
        string path = copy.Write("mixed.cs", "// header\r\nfirst();\nsecond();\n");

        string shown = copy.Files.Read("mixed.cs", 2, 3).Value!["text"]!.GetValue<string>();
        Answer<JsonObject> answer = copy.Files.Edit("mixed.cs", shown, "both();\nthird();", all: false);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal("// header\r\nboth();\nthird();\n", File.ReadAllText(path));
    }

    /// <summary>A replacement of one line takes the line ends of the file it lands in, as it did before the ends were chosen by place.</summary>
    [Fact]
    public void Edit_OneLineOfACrlfFile_WritesTheReplacementWithItsLineEnds()
    {
        using var copy = new Copy();
        string path = copy.Write("App.cs", "first();\r\nsecond();\r\n");

        Answer<JsonObject> answer = copy.Files.Edit("App.cs", "first();", "one();\ntwo();", all: false);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal("one();\r\ntwo();\r\nsecond();\r\n", File.ReadAllText(path));
    }

    /// <summary>
    /// A UTF-16 file with its byte order mark — a Windows resource script, a PowerShell file saved by an old editor, a
    /// SQL export — read as plain text. The edit found no UTF-8 in it, took it byte for byte as Latin-1, where every letter
    /// is followed by a zero, and answered "does not contain that text" about the text it had just shown. The model's way
    /// out was <c>write_file</c>, which rewrote the file as UTF-8.
    /// </summary>
    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void Edit_FileWithAByteOrderMarkOfAnotherEncoding_ReplacesTheTextReadFileShows(string name)
    {
        using var copy = new Copy();
        var encoding = Encoding.GetEncoding(name);
        string path = copy.Write("resource.rc", string.Empty);
        File.WriteAllText(path, "caption = \"hello\"\n", encoding);

        string shown = copy.Files.Read("resource.rc", null, null).Value!["text"]!.GetValue<string>();
        Answer<JsonObject> answer = copy.Files.Edit("resource.rc", "hello", "bye", all: false);

        Assert.Equal("caption = \"hello\"", shown);
        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal([.. encoding.Preamble, .. encoding.GetBytes("caption = \"bye\"\n")], File.ReadAllBytes(path));
    }

    /// <summary>
    /// <c>search_text</c> matches line by line, so a query with a line end in it could never match — and the answer was the
    /// same <c>ok</c> with no matches and no note that an absent text gets. The model concluded the text was not there.
    /// </summary>
    [Fact]
    public void Search_QuerySpanningTwoLines_SaysWhyNothingMatched()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "first();\nsecond();\n");

        Answer<JsonObject> answer = copy.Files.Search("first();\nsecond();", null, null);

        bool isExplained = !answer.IsSuccess || answer.Value!["note"] is not null;
        Assert.True(isExplained, "a query across lines answered as if the text were absent");
    }
}
