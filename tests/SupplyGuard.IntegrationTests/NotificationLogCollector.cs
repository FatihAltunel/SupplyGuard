using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SupplyGuard.IntegrationTests;

internal sealed class NotificationLogCollector<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Text, Dictionary<string, object?> Fields)> Entries { get; } = new();
    public TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Quarantined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailNotificationProcessing { get; set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        if (FailNotificationProcessing && text.StartsWith("[Notification Sent]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Simulated notification processing failure");
        }

        var fields = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, object?>();
        Entries.Enqueue((logLevel, text, fields));
        if (logLevel == LogLevel.Debug && text.EndsWith("acknowledged", StringComparison.Ordinal))
        {
            Acknowledged.TrySetResult();
        }
        if (logLevel == LogLevel.Warning && text.Contains("quarantined after", StringComparison.Ordinal))
        {
            Quarantined.TrySetResult();
        }
    }
}
