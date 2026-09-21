namespace Snail.MCP.FileSystem.Application.Ports;

/// <summary>What the address of the running session says.</summary>
/// <remarks>
/// A server reached over HTTP is opened at a URL, and the URL is the one place a caller can state
/// something about its whole session rather than about a single call. That is where the working copy
/// comes from: <c>…/mcp?root=/workspaces/&lt;task&gt;</c>. A server speaking stdio has no address,
/// answers nothing here, and is confined by its configuration instead.
/// </remarks>
public interface ISessionAddress
{
    /// <summary>Every value the address gives that parameter, empty when there is no address at all.</summary>
    IReadOnlyList<string> Values(string parameter);
}
