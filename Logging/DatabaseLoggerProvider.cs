using System.Collections.Concurrent;

namespace PatchlabWhatsAppBot.Logging;

public class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();

    public DatabaseLoggerProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(name, _connectionString));

    public void Dispose() => _loggers.Clear();
}