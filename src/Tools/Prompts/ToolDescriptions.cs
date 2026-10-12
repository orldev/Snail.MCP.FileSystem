namespace Snail.MCP.FileSystem.Tools.Prompts;

/// <summary>What the model reads before it calls; a wire contract, edited with caution.</summary>
public static class ToolDescriptions
{
    /// <summary>The <c>path</c> parameter of the file tools.</summary>
    public const string Path = "Path of the file, absolute or relative to the session's root.";

    /// <summary>What <c>read_file</c> does.</summary>
    public const string Read =
        "Reads a file as text, whole or by lines. Says how many lines it has and whether the answer was cut.";

    /// <summary>The <c>from</c> parameter of <c>read_file</c>.</summary>
    public const string ReadFrom = "First line to return, counting from one; omit for the start of the file.";

    /// <summary>The <c>to</c> parameter of <c>read_file</c>.</summary>
    public const string ReadTo = "Last line to return; omit for the end of the file.";

    /// <summary>What <c>write_file</c> does.</summary>
    public const string Write =
        "Writes a file whole, creating the folders above it. Overwrites without asking: read it first, "
        + "or use edit_file to change a part.";

    /// <summary>The <c>text</c> parameter of <c>write_file</c>.</summary>
    public const string WriteText = "The whole new contents of the file.";

    /// <summary>What <c>edit_file</c> does.</summary>
    public const string Edit =
        "Replaces exact text in a file. Refuses when the text is not there, and when it is there more "
        + "than once without 'all' — a replacement the caller did not mean is worse than no replacement.";

    /// <summary>The <c>find</c> parameter of <c>edit_file</c>.</summary>
    public const string EditFind = "The exact text to replace, including its indentation and line ends.";

    /// <summary>The <c>replace</c> parameter of <c>edit_file</c>.</summary>
    public const string EditReplace = "What to put in its place; empty removes it.";

    /// <summary>The <c>all</c> parameter of <c>edit_file</c>.</summary>
    public const string EditAll = "Replace every occurrence rather than refusing when there is more than one.";

    /// <summary>What <c>list_directory</c> does.</summary>
    public const string List =
        "Lists what is under a folder, to the depth asked for. Build output and .git are never walked into.";

    /// <summary>The <c>path</c> parameter of the tools that work in a folder.</summary>
    public const string ListPath = "Folder to work in; omit for the session's root.";

    /// <summary>The <c>depth</c> parameter of <c>list_directory</c>.</summary>
    public const string ListDepth = "How many folders deep to go; two by default, eight at most.";

    /// <summary>What <c>find_files</c> does.</summary>
    public const string Find =
        "Names the files whose path matches a glob: *.cs, **/Tests/*.cs, appsettings.*.json.";

    /// <summary>The <c>pattern</c> parameter of <c>find_files</c>.</summary>
    public const string FindPattern = "The glob to match. '*' stays inside one folder, '**' crosses them.";

    /// <summary>What <c>search_text</c> does.</summary>
    public const string Search =
        "Names the lines that contain a text, with the file and the line number of each. Exact text, not a pattern.";

    /// <summary>The <c>query</c> parameter of <c>search_text</c>.</summary>
    public const string SearchQuery = "The text to look for, matched exactly.";

    /// <summary>The <c>pattern</c> parameter of <c>search_text</c>.</summary>
    public const string SearchPattern = "Only search files whose path matches this glob.";

    /// <summary>What <c>find_tool</c> does.</summary>
    public const string FindTool =
        "Names the tools that answer a task described in words, with the parameters each takes.";

    /// <summary>The <c>task</c> parameter of <c>find_tool</c>.</summary>
    public const string FindToolTask = "What you are trying to do, in words: 'change one line', 'where is this text'.";

    /// <summary>The <c>limit</c> parameter of <c>find_tool</c>.</summary>
    public const string FindToolLimit = "How many tools to name; five by default.";

    /// <summary>What <c>run_tools</c> does.</summary>
    public const string RunTools =
        "Runs several of this server's tools in one call, in order, stopping at the first failure. "
        + "For steps that do not depend on reading the previous answer. Each step is an ordinary tool call.";

    /// <summary>The <c>steps</c> parameter of <c>run_tools</c>.</summary>
    public const string RunToolsSteps =
        "Steps in order: {\"tool\": \"read_file\", \"params\": {\"path\": \"src/App.cs\"}}. Call find_tool for what a tool takes.";
}
