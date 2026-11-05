using Microsoft.Extensions.Logging;

namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A logger that keeps what it is told, so a test can read the journal of a change.</summary>
public sealed class Journal<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    /// <summary>Every line written, in order.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lines)
        {
            _lines.Add(formatter(state, exception));
        }
    }
}
