using Microsoft.Extensions.Logging;

namespace Scada.Api.Services;

internal sealed class RecentLogProvider : ILoggerProvider
{
    private readonly IRecentLogStore _store;

    public RecentLogProvider(IRecentLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new RecentLogger(categoryName, _store);

    public void Dispose()
    {
    }

    private sealed class RecentLogger : ILogger
    {
        private readonly string _category;
        private readonly IRecentLogStore _store;

        public RecentLogger(string category, IRecentLogStore store)
        {
            _category = category;
            _store = store;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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

            _store.Add(new RecentLogEntry(
                DateTime.UtcNow,
                logLevel.ToString(),
                _category,
                formatter(state, exception),
                exception?.ToString()));
        }
    }
}
