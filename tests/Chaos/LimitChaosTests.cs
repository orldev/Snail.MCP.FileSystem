using System.Text.Json.Nodes;
using Snail.MCP.FileSystem.Tests.Support;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// The limits are where an answer stops being the whole truth, and an answer that says nothing about where it stopped
/// is the "nothing matched" that means "nothing was looked at".
/// </summary>
public sealed class LimitChaosTests
{
    /// <summary>
    /// The refusal of a file over <c>MaxFileBytes</c> says "read it by lines with from and to", and the size is checked
    /// before from and to are looked at: the hint sends the model into a refusal it can never get past.
    /// </summary>
    [Fact]
    public void ReadFile_LargerThanTheLimitReadByLines_ReturnsThoseLinesAsTheHintPromises()
    {
        using var copy = new Copy(config => config.MaxFileBytes = 1024);
        copy.Write("big.log", string.Concat(Enumerable.Range(1, 200).Select(line => $"line {line}\n")));

        Answer refused = copy.Files.Read("big.log", null, null);
        Answer answer = copy.Files.Read("big.log", 1, 1);

        Assert.Contains("by lines", refused.Hint, StringComparison.Ordinal);
        Assert.True(answer.IsSuccess, answer.Error);
    }

    /// <summary>
    /// A file over the limit is skipped by <c>search_text</c> without a word: the answer is "no matches", and the text is
    /// in the file. The guidelines' "nothing matched" and "nothing was searched" are different facts.
    /// </summary>
    [Fact]
    public void SearchText_MatchOnlyInAFileOverTheLimit_SaysThatFileWasNotSearched()
    {
        using var copy = new Copy(config => config.MaxFileBytes = 1024);
        copy.Write("big.log", string.Concat(Enumerable.Range(1, 200).Select(line => $"line {line}\n")) + "needle\n");

        Answer answer = copy.Files.Search("needle", null, null);

        Assert.Contains("big.log", answer.Data!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>Exactly as many entries as the limit is everything there is, and the answer calls it cut.</summary>
    [Fact]
    public void FindFiles_ExactlyAsManyFilesAsTheLimit_IsNotReportedCut()
    {
        using var copy = new Copy(config => config.MaxMatches = 3);
        copy.Write("a.txt", "");
        copy.Write("b.txt", "");
        copy.Write("c.txt", "");

        Answer answer = copy.Files.Find("*.txt", null);

        Assert.Equal(3, answer.Data!["files"]!.AsArray().Count);
        Assert.False(answer.Data!["isCut"]!.GetValue<bool>());
    }

    /// <summary>
    /// <c>**/</c> is written as <c>.*</c> with the slash swallowed, so <c>**/Tests/*.cs</c> is <c>^.*Tests/[^/]*\.cs$</c>
    /// and matches every folder whose name merely ends in Tests.
    /// </summary>
    [Fact]
    public void FindFiles_DoubleStarBeforeAFolder_DoesNotMatchAFolderThatOnlyEndsTheSame()
    {
        using var copy = new Copy();
        copy.Write("src/Tests/A.cs", "");
        copy.Write("src/UnitTests/B.cs", "");

        var files = copy.Files.Find("**/Tests/*.cs", null).Data!["files"]!.AsArray()
            .Select(file => file!.GetValue<string>().Replace('\\', '/'))
            .ToList();

        Assert.Equal(["src/Tests/A.cs"], files);
    }

    /// <summary>The cut is made at a character count, and a character count can fall between the two halves of an emoji: the model gets half a character.</summary>
    [Fact]
    public void ReadFile_CutFallsInsideAnEmoji_ReturnsWholeCharactersOnly()
    {
        using var copy = new Copy(config => config.OutputLimitChars = 5);
        copy.Write("notes.md", "abcd😀efgh");

        string text = copy.Files.Read("notes.md", null, null).Data!["text"]!.GetValue<string>();

        Assert.False(char.IsHighSurrogate(text[^1]), "the answer ends with half of a surrogate pair");
    }

    /// <summary>Withdrawn if it passes: line numbers at the ends of int are clamped, not trusted.</summary>
    [Theory]
    [InlineData(int.MaxValue, null)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(null, int.MinValue)]
    [InlineData(5, 2)]
    [InlineData(0, int.MaxValue)]
    public void ReadFile_ExtremeLineNumbers_AnswerWithoutThrowing(int? from, int? to)
    {
        using var copy = new Copy();
        copy.Write("App.cs", "one\ntwo\nthree\n");

        Assert.True(copy.Files.Read("App.cs", from, to).IsSuccess);
    }

    /// <summary>
    /// The answer is serialised with the default encoder, which escapes every non-ASCII letter and every <c>&lt;</c>,
    /// <c>&gt;</c>, <c>&amp;</c>, <c>'</c> and <c>+</c> as a six-character escape: <c>List&lt;string&gt;</c> reaches the
    /// model with both brackets escaped, a Cyrillic letter as six characters. The model reads, and pays for, the escapes.
    /// </summary>
    [Fact]
    public void ReadFile_CodeWithGenericsAndCyrillic_ReachesTheWireAsWritten()
    {
        using var copy = new Copy();
        const string code = "List<string> names = ['первая строка']; if (a && b) c += 1;";
        copy.Write("App.cs", code);

        string wire = ToolResponse.From(copy.Files.Read("App.cs", null, null)).Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        string text = JsonNode.Parse(wire)!["data"]!["text"]!.GetValue<string>();

        Assert.Equal(code, text);
        Assert.True(wire.Contains(code, StringComparison.Ordinal), $"{code.Length} characters of code, {wire.Length} on the wire: {wire}");
    }
}
