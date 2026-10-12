namespace Snail.MCP.FileSystem.Domain;

/// <summary>What a tool answers: the data, or why there is none.</summary>
/// <remarks>
/// A refusal is data here, not an exception. Every reason a call of this server fails — a path outside
/// the session, a file that is not text, a replacement that matches nothing — is something the caller
/// can act on, and something it must be able to read rather than merely observe as a failed call.
/// </remarks>
public sealed record Answer(bool IsSuccess, JsonNode? Data, string? Error, string? Hint)
{
    /// <summary>An answer carrying its data.</summary>
    public static Answer Ok(JsonNode data) => new(true, data, null, null);

    /// <summary>A refusal, with what the caller can do about it.</summary>
    public static Answer Failed(string error, string? hint = null) => new(false, null, error, hint);
}

/// <summary>One place a search matched, as a reader needs to see it.</summary>
public sealed record TextMatch(string Path, int Line, string Text);

/// <summary>One entry of a listing.</summary>
public sealed record Entry(string Path, bool IsDirectory, long Bytes);
