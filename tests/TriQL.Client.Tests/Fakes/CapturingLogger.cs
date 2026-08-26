using Microsoft.Extensions.Logging;

namespace TriQL.Client.Tests.Fakes;

/// <summary>A minimal <see cref="ILogger"/> that captures every formatted message for assertions.</summary>
public sealed class CapturingLogger : ILogger
{
    private readonly List<string> _messages = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> Messages
    {
        get { lock (_gate) { return [.. _messages]; } }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_gate)
        {
            _messages.Add(message);
        }
    }
}

/// <summary>An <see cref="ILoggerFactory"/> that always returns the same <see cref="CapturingLogger"/>.</summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    public CapturingLogger Logger { get; } = new();

    public ILogger CreateLogger(string categoryName) => Logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
