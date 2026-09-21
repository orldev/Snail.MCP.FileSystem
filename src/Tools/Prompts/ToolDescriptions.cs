namespace Snail.MCP.FileSystem.Tools.Prompts;

/// <summary>What the model reads before it calls; a wire contract, edited with caution.</summary>
public static class ToolDescriptions
{
    public const string Path = "Path of the file, absolute or relative to the session's root.";

    public const string Read =
        "Reads a file as text, whole or by lines. Says how many lines it has and whether the answer was cut.";

    public const string ReadFrom = "First line to return, counting from one; omit for the start of the file.";

    public const string ReadTo = "Last line to return; omit for the end of the file.";

    public const string Write =
        "Writes a file whole, creating the folders above it. Overwrites without asking: read it first, "
        + "or use edit_file to change a part.";

    public const string WriteText = "The whole new contents of the file.";

    public const string Edit =
        "Replaces exact text in a file. Refuses when the text is not there, and when it is there more "
        + "than once without 'all' — a replacement the caller did not mean is worse than no replacement.";

    public const string EditFind = "The exact text to replace, including its indentation and line ends.";

    public const string EditReplace = "What to put in its place; empty removes it.";

    public const string EditAll = "Replace every occurrence rather than refusing when there is more than one.";

    public const string List =
        "Lists what is under a folder, to the depth asked for. Build output and .git are never walked into.";

    public const string ListPath = "Folder to work in; omit for the session's root.";

    public const string ListDepth = "How many folders deep to go; two by default, eight at most.";

    public const string Find =
        "Names the files whose path matches a glob: *.cs, **/Tests/*.cs, appsettings.*.json.";

    public const string FindPattern = "The glob to match. '*' stays inside one folder, '**' crosses them.";

    public const string Search =
        "Names the lines that contain a text, with the file and the line number of each. Exact text, not a pattern.";

    public const string SearchQuery = "The text to look for, matched exactly.";

    public const string SearchPattern = "Only search files whose path matches this glob.";

    public const string FindTool =
        "Names the tools that answer a task described in words, with the parameters each takes.";

    public const string FindToolTask = "What you are trying to do, in words: 'change one line', 'where is this text'.";

    public const string FindToolLimit = "How many tools to name; five by default.";

    public const string RunTools =
        "Runs several of this server's tools in one call, in order, stopping at the first failure. "
        + "For steps that do not depend on reading the previous answer. Each step is an ordinary tool call.";

    public const string RunToolsSteps =
        "Steps in order: {\"tool\": \"read_file\", \"params\": {\"path\": \"src/App.cs\"}}. Call find_tool for what a tool takes.";
}
