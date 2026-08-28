using Microsoft.Extensions.Logging;

namespace FlowStock.IntegrationTests.Infrastructure;

/// <summary>
/// Keeps the exceptions the API logged, so a test that gets an unexpected 500 can say what
/// actually went wrong instead of only that the status was not the one it wanted.
/// </summary>
public class CapturedErrors
{
    private readonly List<string> _errors = [];
    private readonly Lock _gate = new();

    public void Add(string error)
    {
        lock (_gate)
        {
            _errors.Add(error);
        }
    }

    /// <summary>Everything logged so far, newest last, as one readable block.</summary>
    public string Report()
    {
        lock (_gate)
        {
            return _errors.Count == 0 ? "(the API logged no errors)" : string.Join("\n---\n", _errors);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _errors.Clear();
        }
    }
}

/// <summary>Feeds every logged error into <see cref="CapturedErrors"/>.</summary>
public class CapturingLoggerProvider(CapturedErrors errors) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, errors);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private sealed class CapturingLogger(string category, CapturedErrors errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            errors.Add($"[{category}] {formatter(state, exception)}\n{exception}");
        }
    }
}
