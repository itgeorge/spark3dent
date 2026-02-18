using System;
using Utilities;

namespace Utilities.Tests;

/// <summary>
/// ILogger implementation that throws on every log call.
/// Used by tests to verify that logging failures do not cause operation failures.
/// </summary>
public class ThrowingLogger : ILogger
{
    public void Log(LogLevel level, string message) => throw new InvalidOperationException("Log failed");
    public void LogError(string message, Exception exception) => throw new InvalidOperationException("LogError failed");
    public void LogInfo(string message) => throw new InvalidOperationException("LogInfo failed");
    public void LogDebug(string message) => throw new InvalidOperationException("LogDebug failed");
    public void LogWarning(string message) => throw new InvalidOperationException("LogWarning failed");
}
