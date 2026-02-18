using System;
using System.Collections.Generic;
using Utilities;

namespace Utilities.Tests;

/// <summary>
/// ILogger implementation that records all log calls in memory.
/// Useful for tests that need to assert that specific log messages were produced.
/// </summary>
public class CapturingLogger : ILogger
{
    private readonly List<string> _infoMessages = [];
    private readonly List<(string Message, Exception Exception)> _errorEntries = [];

    public IReadOnlyList<string> InfoMessages => _infoMessages;
    public IReadOnlyList<(string Message, Exception Exception)> ErrorEntries => _errorEntries;

    public void Log(LogLevel level, string message) => _infoMessages.Add(message);
    public void LogInfo(string message) => _infoMessages.Add(message);
    public void LogDebug(string message) => _infoMessages.Add(message);
    public void LogWarning(string message) => _infoMessages.Add(message);
    public void LogError(string message, Exception exception) => _errorEntries.Add((message, exception));
}
