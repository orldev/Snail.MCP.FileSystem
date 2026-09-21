namespace Snail.MCP.FileSystem.Adapters;

/// <summary>A server started as a program has no address, and says so by answering nothing.</summary>
internal sealed class StdioAddress : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) => [];
}
