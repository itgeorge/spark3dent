using System.Collections.Concurrent;

namespace Utilities;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public interface ILogger
{
    const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    void Log(LogLevel level, string message);
    void LogError(string message, Exception exception);
    void LogInfo(string message);
    void LogDebug(string message);
    void LogWarning(string message);
}

public abstract class TextLogger : ILogger
{
    protected readonly TextWriter _writer;
    protected readonly LogLevel _minLogLevel;

    protected TextLogger(TextWriter writer, LogLevel minLogLevel = LogLevel.Debug)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _minLogLevel = minLogLevel;
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLogLevel) return;
        var timestamp = DateTime.UtcNow.ToString(ILogger.TimestampFormat);
        _writer.WriteLine($"[{timestamp}, {level}] {message}");
    }

    public void LogError(string message, Exception exception)
    {
        if (LogLevel.Error < _minLogLevel) return;
        var timestamp = DateTime.UtcNow.ToString(ILogger.TimestampFormat);
        _writer.WriteLine($"[{timestamp}, {LogLevel.Error}] {message}, stacktrace:{Environment.NewLine}{exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    public void LogInfo(string message)
    {
        Log(LogLevel.Info, message);
    }

    public void LogDebug(string message)
    {
        Log(LogLevel.Debug, message);
    }

    public void LogWarning(string message)
    {
        Log(LogLevel.Warning, message);
    }
}

public class ConsoleLogger : TextLogger
{
    public ConsoleLogger(LogLevel minLogLevel = LogLevel.Debug)
        : base(Console.Out, minLogLevel)
    {
    }

    /// <summary>
    /// Creates a ConsoleLogger configured for test environments.
    /// Uses Fatal log level to suppress logs but still verify that logging calls don't break.
    /// </summary>
    /// <returns>A ConsoleLogger with Fatal minimum log level</returns>
    public static ConsoleLogger ForTests() => new ConsoleLogger(LogLevel.Fatal);
}

public class FileLogger : TextLogger, IDisposable
{
    public FileLogger(string filePath, LogLevel minLogLevel = LogLevel.Debug)
        : base(new StreamWriter(
            new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            leaveOpen: false) { AutoFlush = true }, minLogLevel)
    {
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}

public class BufferedLogger : ILogger, IDisposable
{
    private readonly ILogger _innerLogger;
    private readonly int _bufferSize;
    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private bool _disposed;

    private record LogEntry(LogLevel Level, string Message, Exception? Exception = null);

    public BufferedLogger(ILogger innerLogger, int bufferSize)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
        _bufferSize = bufferSize > 0 ? bufferSize : throw new ArgumentException("Buffer size must be positive", nameof(bufferSize));
    }

    public void Log(LogLevel level, string message)
    {
        if (_disposed) return;
        _buffer.Enqueue(new LogEntry(level, message));
        CheckAndFlush();
    }

    public void LogError(string message, Exception exception)
    {
        if (_disposed) return;
        _buffer.Enqueue(new LogEntry(LogLevel.Error, message, exception));
        Flush(); // always flush on error as we may not reach disposal
    }

    public void LogInfo(string message) => Log(LogLevel.Info, message);
    public void LogDebug(string message) => Log(LogLevel.Debug, message);
    public void LogWarning(string message) => Log(LogLevel.Warning, message);

    private void CheckAndFlush()
    {
        if (_buffer.Count >= _bufferSize)
        {
            Flush();
        }
    }

    private void Flush()
    {
        while (_buffer.TryDequeue(out var entry))
        {
            if (entry.Exception != null)
            {
                _innerLogger.LogError(entry.Message, entry.Exception);
            }
            else
            {
                _innerLogger.Log(entry.Level, entry.Message);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }
}

/// <summary>
/// Wraps a single logger and swallows any exceptions from logging calls.
/// Ensures logging failures never cause the application or decorated operations to fail.
/// </summary>
public class SafeLogger : ILogger
{
    private readonly ILogger _inner;

    public SafeLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Log(LogLevel level, string message)
    {
        try { _inner.Log(level, message); } catch { /* Ignore logging failures */ }
    }

    public void LogError(string message, Exception exception)
    {
        try { _inner.LogError(message, exception); } catch { /* Ignore logging failures */ }
    }

    public void LogInfo(string message)
    {
        try { _inner.LogInfo(message); } catch { /* Ignore logging failures */ }
    }

    public void LogDebug(string message)
    {
        try { _inner.LogDebug(message); } catch { /* Ignore logging failures */ }
    }

    public void LogWarning(string message)
    {
        try { _inner.LogWarning(message); } catch { /* Ignore logging failures */ }
    }
}

public class SafeMultiLogger : ILogger
{
    private readonly ILogger[] _loggers;

    public SafeMultiLogger(ILogger[] loggers)
    {
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    public void Log(LogLevel level, string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.Log(level, message);
            }
            catch
            {
                // Ignore exceptions to ensure logging doesn't break the application
            }
        }
    }

    public void LogError(string message, Exception exception)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.LogError(message, exception);
            }
            catch
            {
                // Ignore exceptions to ensure logging doesn't break the application
            }
        }
    }

    public void LogInfo(string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.LogInfo(message);
            }
            catch
            {
                // Ignore exceptions to ensure logging doesn't break the application
            }
        }
    }

    public void LogDebug(string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.LogDebug(message);
            }
            catch
            {
                // Ignore exceptions to ensure logging doesn't break the application
            }
        }
    }

    public void LogWarning(string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.LogWarning(message);
            }
            catch
            {
                // Ignore exceptions to ensure logging doesn't break the application
            }
        }
    }
}