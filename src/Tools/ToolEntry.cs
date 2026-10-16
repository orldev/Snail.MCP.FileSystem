namespace Snail.MCP.FileSystem.Tools;

/// <summary>One tool as the index knows it.</summary>
public sealed record ToolEntry(string Name, string Description, IReadOnlyList<ToolArgument> Parameters);
