namespace Snail.MCP.FileSystem.Tools.Prompts;

/// <summary>What the client is told about this server when it connects.</summary>
public static class ServerGuidance
{
    public const string Instructions =
        """
        This server works only in the folder this session was given — the working copy named in the address
        it opened, or the one the deployment configured. A path outside it is refused, and so is a link
        that leads out of it. Paths may be relative to that folder; the answers name them that way.

        Read before you write: read_file gives the text with its line count, search_text says where a string
        is, find_files says what exists. Change a part of a file with edit_file, which replaces exact text
        and refuses when that text is absent or ambiguous; write_file replaces the whole file.

        Do not know which tool: call find_tool with the task in words. Several questions that do not depend
        on each other's answers: call run_tools once with them as steps, instead of a round trip each.

        Every answer is JSON with an ok flag. When ok is false, read error and hint and fix the call
        instead of repeating it.
        """;
}
