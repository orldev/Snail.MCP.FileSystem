namespace Snail.MCP.FileSystem.Domain;

/// <summary>What one folder holds directly: its files and its folders, by full path.</summary>
public sealed record Contents(IReadOnlyList<string> Files, IReadOnlyList<string> Folders);
