namespace Snail.MCP.FileSystem.Domain;

/// <summary>One entry of a listing.</summary>
public sealed record Entry(string Path, bool IsDirectory, long Bytes);
