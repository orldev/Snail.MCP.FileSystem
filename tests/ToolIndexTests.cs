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
    public void A_question_in_plain_words_names_the_tool_that_answers_it(string question, string expected) =>
        Assert.Contains(expected, _index.Find(question, 3).Select(entry => entry.Name));

    [Fact]
    public void A_question_nothing_matches_returns_every_tool_and_says_so()
    {
        var answer = _index.Answer("превед медвед", 5);

        Assert.False(answer["isMatched"]!.GetValue<bool>());
        Assert.Equal(ToolIndex.All.Count, answer["tools"]!.AsArray().Count);
    }

    [Fact]
    public void Every_tool_the_index_knows_other_words_for_is_a_tool_that_exists()
    {
        var real = ToolIndex.All.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(ToolIndex.Meant(), name => Assert.Contains(name, real));
    }

    [Fact]
    public void Every_tool_has_other_words_the_index_knows_it_by()
    {
        var known = ToolIndex.Meant().ToHashSet(StringComparer.Ordinal);

        Assert.All(ToolIndex.All, entry => Assert.Contains(entry.Name, known));
    }

    /// <summary>The parameters the SDK fills are not the model's to fill, and the index must not show them.</summary>
    [Fact]
    public void The_index_lists_only_the_parameters_the_model_fills()
    {
        var run = ToolIndex.All.Single(entry => entry.Name == "run_tools");

        Assert.Equal(["steps"], run.Parameters.Select(parameter => parameter.Name));
    }
}
