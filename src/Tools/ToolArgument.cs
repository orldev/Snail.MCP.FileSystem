namespace Snail.MCP.FileSystem.Tools;

/// <summary>One argument of a tool, with the words the model reads before filling it.</summary>
public sealed record ToolArgument(string Name, string Type, bool IsRequired, string Description);
