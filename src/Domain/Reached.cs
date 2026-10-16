namespace Snail.MCP.FileSystem.Domain;

/// <summary>A path this session may use, or the reason it may not.</summary>
public sealed record Reached(string? Path, string? Error, string? Hint)
{
    /// <summary>Whether the path may be used; <see cref="Path"/> is then the resolved one.</summary>
    public bool IsAllowed => Path is not null;

    /// <summary>The resolved path, allowed.</summary>
    public static Reached Yes(string path) => new(path, null, null);

    /// <summary>A refusal, with what the caller can do about it.</summary>
    public static Reached No(string error, string? hint = null) => new(null, error, hint);
}
