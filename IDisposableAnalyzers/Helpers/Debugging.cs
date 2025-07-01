namespace IDisposableAnalyzers;

using System;
using System.Diagnostics;
using System.IO;
using DebugLogging;
using Microsoft.CodeAnalysis.Diagnostics;

internal static class Debugging
{
    private static DebugLogger debugLogger = new();
    private static object lockObject = new();

    /// <summary>
    /// Logs a message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    internal static void Log(string message)
    {
        if (message is not null)
        {
            lock (lockObject)
            {
                debugLogger.Log(message);
            }
        }
    }

    /// <summary>
    /// Returns the shortened version of the text if it exceeds the specified maximum length.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The max length.</param>
    internal static string Shorten(string text, int maxLength = 100)
    {
        if (text is null)
        {
            return string.Empty;
        }
        else if (text.Length <= maxLength)
        {
            return text;
        }

        return $"{text.Substring(0, maxLength)}...";
    }

    /// <summary>
    /// Adds a log entry for the start of handling a syntax node analysis context.
    /// </summary>
    /// <param name="className">The analyzer class name.</param>
    /// <param name="context">The node context.</param>
    /// <param name="stopwatch">A stopwatch upon return to measure duration.</param>
    internal static void LogEntry(string className, SyntaxNodeAnalysisContext context, out Stopwatch stopwatch)
    {
        if (context.Node.ToString() is string nodeText)
        {
            Log($"{className}: handling {Shorten(nodeText)}");
        }
        else
        {
            Log($"{className}: no context to handle");
        }

        stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Adds a log entry for the exit of handling a syntax node analysis context.
    /// </summary>
    /// <param name="className">The analyzer class name.</param>
    /// <param name="stopwatch">The stopwatch used to measure duration.</param>
    internal static void LogExit(string className, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        Log($"{className}: handling done, duration: {stopwatch.ElapsedMilliseconds}ms");
    }
}
