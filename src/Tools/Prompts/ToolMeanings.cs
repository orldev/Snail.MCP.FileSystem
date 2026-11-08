namespace Snail.MCP.FileSystem.Tools.Prompts;

/// <summary>The words each tool is asked for by beyond its name, which <c>find_tool</c> weighs as words of the name.</summary>
/// <remarks>
/// Here rather than in the descriptions because a description rides in every session whether it searches or not, and
/// these are read only when one does. Snail.Sdk's <c>find_tool</c> takes them as its meanings; a guard test keeps every key
/// a tool the server lists and every tool it lists a key.
/// </remarks>
public static class ToolMeanings
{
    /// <summary>The words, by the tool's name.</summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["read_file"] = "open show contents source text lines look at",
        ["write_file"] = "create save new whole overwrite put",
        ["edit_file"] = "change one line replace part patch modify fix",
        ["list_directory"] = "tree folder what is here ls dir contents",
        ["find_files"] = "which files glob name pattern where is the file",
        ["search_text"] = "grep contains occurrences string mentions where is",
        ["run_tools"] = "several batch sequence steps together once round trip",
        ["find_tool"] = "search discover which tool name parameters how do i"
    };
}
