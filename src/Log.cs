namespace Snail.MCP.FileSystem;

/// <summary>What the server says about the sessions it serves.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line costs nothing while its level is off, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A session was turned away: {Reason}")]
    internal static partial void SessionTurnedAway(ILogger logger, string reason);
}
