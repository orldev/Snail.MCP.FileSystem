using System.Text.Json.Nodes;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The index of tools: the words to search by live in it, not in the descriptions every session carries.</summary>
public sealed class ToolIndexTests
{
    private readonly ToolIndex _index = new();

    [Theory]
    [InlineData("change one line in a file", "edit_file")]
    [InlineData("where is this text", "search_text")]
    [InlineData("what is in this folder", "list_directory")]
    [InlineData("which files match", "find_files")]
    [InlineData("save a new file", "write_file")]
    public void Find_QuestionInPlainWords_NamesTheToolThatAnswersIt(string question, string expected) =>
        Assert.Contains(expected, _index.Find(question, 3).Select(entry => entry.Name));

    [Fact]
    public void Answer_QuestionNothingMatches_ReturnsEveryToolAndSaysSo()
    {
        JsonObject answer = _index.Answer("превед медвед", 5);

        Assert.False(answer["isMatched"]!.GetValue<bool>());
        Assert.Equal(ToolIndex.All.Count, answer["tools"]!.AsArray().Count);
    }

    [Fact]
    public void Meant_EveryToolWithOtherWords_IsAToolThatExists()
    {
        var real = ToolIndex.All.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(ToolIndex.Meant(), name => Assert.Contains(name, real));
    }

    [Fact]
    public void Meant_EveryTool_HasOtherWords()
    {
        var known = ToolIndex.Meant().ToHashSet(StringComparer.Ordinal);

        Assert.All(ToolIndex.All, entry => Assert.Contains(entry.Name, known));
    }

    /// <summary>The parameters the SDK fills are not the model's to fill, and the index must not show them.</summary>
    [Fact]
    public void All_RunTools_ListsOnlyTheParametersTheModelFills()
    {
        ToolEntry run = ToolIndex.All.Single(entry => entry.Name == "run_tools");

        Assert.Equal(["steps"], run.Parameters.Select(parameter => parameter.Name));
    }
}
