namespace Snail.MCP.FileSystem.Tools;

/// <summary>One step of a run: the tool to call and the arguments to call it with.</summary>
public sealed record ToolStep(string? Tool, JsonObject? Params);
