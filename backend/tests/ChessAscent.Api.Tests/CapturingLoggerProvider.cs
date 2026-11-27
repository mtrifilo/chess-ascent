using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ChessAscent.Api.Tests;

/// <summary>
/// Keeps every formatted log message, with the exception text when there is
/// one, from every category, so a test can check what the API logged.
/// </summary>
/// <remarks>
/// Add it with <c>AddProvider</c> and
/// <c>AddFilter&lt;CapturingLoggerProvider&gt;(category: null, LogLevel.Trace)</c>:
/// a filter for this provider alone outranks the application's category
/// filters, so it receives every level.
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception) + exception);
    }
}
