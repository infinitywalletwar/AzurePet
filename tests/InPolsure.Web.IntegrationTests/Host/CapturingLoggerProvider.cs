using System.Collections.Concurrent;
using System.Text;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>
/// Logger provider that records every log entry the host's filters let through, rendered as one text per entry:
/// category, formatted message, all state values, all scope values and the exception. Registered in the test host
/// next to the app's providers, so it sees exactly what the console and OpenTelemetry providers would receive
/// (same <c>Logging:LogLevel</c> rules).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private void Record(string entry) => _entries.Enqueue(entry);

    private static void AppendValues(StringBuilder text, object? state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                text.Append(' ').Append(key).Append('=').Append(value);
            }
        }
        else if (state is not null)
        {
            text.Append(' ').Append(state);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var text = new StringBuilder()
                .Append(logLevel).Append(' ').Append(category).Append(": ").Append(formatter(state, exception));
            AppendValues(text, state);
            provider._scopes.ForEachScope(static (scope, builder) => AppendValues(builder, scope), text);
            if (exception is not null)
            {
                text.Append(' ').Append(exception);
            }

            provider.Record(text.ToString());
        }
    }
}
