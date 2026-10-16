namespace Snail.MCP.FileSystem.Configuration;

/// <summary>Everything the server is allowed to do, and where.</summary>
/// <remarks>
/// The roots are the whole security model: a filesystem server is one call away from every file the
/// process can read, so it refuses every path that is not inside one of them. A session states its own
/// root in the address it opens — that is what lets one server stand behind several agents — and these
/// stand when it states none, which is how a server started as a program for one caller is confined.
/// </remarks>
public sealed class ServerConfig
{
    /// <summary>The configuration section the settings are bound from.</summary>
    public const string SectionName = "Snail";

    /// <summary>Absolute paths the agent may read and write; nothing outside them is reachable.</summary>
    public IReadOnlyList<string> Roots { get; set; } = [];

    /// <summary>Port to answer on; none means the server is started as a program and speaks stdio.</summary>
    /// <remarks>
    /// One knob decides the whole shape of the process, because the two shapes serve different callers:
    /// at an address it stands behind every worker of a machine and each session names its own copy, as a
    /// program it belongs to one caller and is confined by <see cref="Roots"/>.
    /// </remarks>
    public int Port { get; set; }

    /// <summary>Whether writing exists at all; a role that only reads is given a server that only reads.</summary>
    public bool AllowWrite { get; set; } = true;

    /// <summary>Characters of file text one call may return before it is cut; the answer says when it was.</summary>
    public int OutputLimitChars { get; set; } = 131072;

    /// <summary>Bytes past which a file is not read as text at all: it is not one.</summary>
    public int MaxFileBytes { get; set; } = 8_388_608;

    /// <summary>Matches one search may return; a query that matches everything answers nothing useful.</summary>
    public int MaxMatches { get; set; } = 200;

    /// <summary>Steps one run_tools call may take.</summary>
    public int StepLimit { get; set; } = 20;

    /// <summary>Folder names never walked into, because nobody means them and they are enormous.</summary>
    public IReadOnlyList<string> Skip { get; set; } = [".git", "bin", "obj", "node_modules", ".vs", ".idea"];
}
