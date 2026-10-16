namespace Snail.MCP.FileSystem.Application;

/// <summary>Which tools this session may use, read from the address it opened.</summary>
/// <remarks>
/// The platform gives a role a list of tools, and that list used to hold only as long as nobody asked
/// for a tool that calls other tools. A role granted <c>run_tools</c> reached every tool this server has,
/// including the ones the operator deliberately withheld — one grant undoing the whole list. The server
/// could not help: it knew nothing about roles.
/// <para>
/// So the grant travels the same way the root does — in the address the session was opened at:
/// <c>…/mcp?root=/workspaces/task&amp;tool=read_file&amp;tool=search_text</c>. The platform writes it into every
/// request, the model cannot reach it, and it is checked where calls arrive and again for every step
/// <c>run_tools</c> forwards. A session that names no tool may use them all, which is what a server started
/// as a program for one caller has always been.
/// </para>
/// </remarks>
public sealed class SessionGrant(ISessionAddress address)
{
    /// <summary>What the address calls a tool it allows; repeat it for each one.</summary>
    public const string Parameter = "tool";

    /// <summary>The tools this session named, empty when it named none and may use everything.</summary>
    public IReadOnlyList<string> All =>
    [
        .. address.Values(Parameter)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Whether this session may call that tool.</summary>
    public bool Allows(string name)
    {
        IReadOnlyList<string> granted = All;

        return granted.Count == 0 || granted.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The refusal, in the words a caller needs to stop asking.</summary>
    public string Refusal(string name) =>
        $"'{name}' was not granted to this session, which may use {string.Join(", ", All)}";
}
