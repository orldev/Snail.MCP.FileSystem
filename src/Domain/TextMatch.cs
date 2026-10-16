namespace Snail.MCP.FileSystem.Domain;

/// <summary>One place a search matched, as a reader needs to see it.</summary>
public sealed record TextMatch(string Path, int Line, string Text);
