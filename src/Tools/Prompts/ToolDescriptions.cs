namespace Snail.MCP.FileSystem.Tools.Prompts;

/// <summary>What the model reads before it calls; a wire contract, edited with caution.</summary>
public static class ToolDescriptions
{
    /// <summary>The <c>path</c> parameter of the file tools.</summary>
    public const string Path = "Path of the file, absolute or relative to the session's root.";

    /// <summary>What <c>read_file</c> does.</summary>
    public const string Read =
        "Reads a file as text, whole or by lines. Says how many lines it has and whether the answer was cut. "
        + "A file too large to read whole is read by lines.";

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
        "Replaces exact text in a file; nothing else in it changes, its encoding and line ends included. Refuses when "
        + "the text is not there, when it is there more than once without 'all', and when the file is too large to read "
        + "whole: write such a file with write_file.";

    /// <summary>The <c>find</c> parameter of <c>edit_file</c>.</summary>
    public const string EditFind = "The exact text to replace, with its indentation; a line end may be written as \\n.";

    /// <summary>The <c>replace</c> parameter of <c>edit_file</c>.</summary>
    public const string EditReplace = "What to put in its place; empty removes it.";

    /// <summary>The <c>all</c> parameter of <c>edit_file</c>.</summary>
    public const string EditAll = "Replace every occurrence rather than refusing when there is more than one.";

    /// <summary>What <c>list_directory</c> does.</summary>
    public const string List =
        "Lists what is under a folder, to the depth asked for. Build output, .git and links are never walked into.";

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
        "Names the lines that contain a text, with the file and the line number of each. Exact text, not a pattern. "
        + "Names the files it could not search.";

    /// <summary>The <c>query</c> parameter of <c>search_text</c>.</summary>
    public const string SearchQuery = "The text to look for, matched exactly within one line.";

    /// <summary>The <c>pattern</c> parameter of <c>search_text</c>.</summary>
    public const string SearchPattern = "Only search files whose path matches this glob.";
}
