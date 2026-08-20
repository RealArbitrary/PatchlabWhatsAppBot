using Microsoft.Data.SqlClient;

namespace PatchlabWhatsAppBot.Logging;

public class DatabaseLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _connectionString;

    public DatabaseLogger(string categoryName, string connectionString)
    {
        _categoryName = categoryName;
        _connectionString = connectionString;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Only Warning and above — this table is for the admin dashboard,
    // not a full trace log. Information-level noise stays out of it.
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);

        // Fire-and-forget on purpose: a logging failure must never throw
        // back into application code or block the request pipeline.
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO ErrorLogs (Severity, Source, Message, StackTrace, CreatedAt)
                    VALUES (@Severity, @Source, @Message, @StackTrace, SYSUTCDATETIME())";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Severity", logLevel.ToString());
                cmd.Parameters.AddWithValue("@Source", _categoryName);
                cmd.Parameters.AddWithValue("@Message", message);
                cmd.Parameters.AddWithValue("@StackTrace", (object?)exception?.ToString() ?? DBNull.Value);
            }
            catch
            {
                // Deliberately swallowed — logging the logger's own failure
                // would risk an infinite loop, and there's nowhere safer to
                // put it. This is the one place in the app allowed to fail silently.
            }
        });
    }
}