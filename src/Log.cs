namespace Snail.MCP.FileSystem;

/// <summary>What the server says about the sessions it serves and the files it changes.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line costs nothing while its level is off, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A session was turned away: {Reason}")]
    internal static partial void SessionTurnedAway(ILogger logger, string reason);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Wrote {Path}: {Bytes} bytes, {Change}")]
    internal static partial void Wrote(ILogger logger, string path, int bytes, string change);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Edited {Path}: {Replaced} replacement(s), {Bytes} bytes")]
    internal static partial void Edited(ILogger logger, string path, int replaced, int bytes);
}
