using System;

namespace TornadoEos.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A single timestamped log line surfaced to the UI.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public static LogEntry Now(LogLevel level, string message) =>
        new(DateTimeOffset.Now, level, message);
}
