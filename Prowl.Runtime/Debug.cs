using System;

namespace Prowl.Runtime;

public enum LogSeverity
{
    Success,
    Normal,
    Warning,
    Error
}

public delegate void OnLog(string message, LogSeverity logSeverity);

public static class Debug
{

    public static event OnLog? OnLog;

    public static void Log(object message)
    {
        Log("", message.ToString(), ConsoleColor.White, LogSeverity.Normal);
    }

    public static void Log(string message)
    {
        Log("", message, ConsoleColor.White, LogSeverity.Normal);
    }

    public static void LogWarning(object message)
    {
        Log("Warning: ", message.ToString(), ConsoleColor.Yellow, LogSeverity.Warning);
    }

    public static void LogWarning(string message)
    {
        Log("Warning: ", message, ConsoleColor.Yellow, LogSeverity.Warning);
    }

    public static void LogError(object message, Exception exception = null)
    {
        Log("Error: ", message.ToString(), ConsoleColor.Red, LogSeverity.Error);

        if (exception != null)
            Log("", exception.ToString(), ConsoleColor.Red, LogSeverity.Error);
    }

    public static void LogError(string message, Exception exception = null)
    {
        Log("Error: ", message, ConsoleColor.Red, LogSeverity.Error);

        if (exception != null)
            Log("", exception.ToString(), ConsoleColor.Red, LogSeverity.Error);
    }

    public static void LogSuccess(object message)
    {
        Log("Success: ", message.ToString(), ConsoleColor.Green, LogSeverity.Success);
    }

    public static void LogSuccess(string message)
    {
        Log("Success: ", message, ConsoleColor.Green, LogSeverity.Success);
    }

    private static void Log(string prefix, string message, ConsoleColor color, LogSeverity logSeverity)
    {
        ConsoleColor prevColor = System.Console.ForegroundColor;
        System.Console.ForegroundColor = color;
        System.Console.WriteLine($"{prefix}{message}");
        System.Console.ForegroundColor = prevColor;
        OnLog?.Invoke(message, logSeverity);
    }

    public static void If(bool condition, string message = "")
    {
        if (condition)
            throw new Exception(message);
    }

    public static void IfNull(object value, string message = "")
    {
        if (value is null)
            throw new Exception(message);
    }

    public static void IfNullOrEmpty(string value, string message = "")
    {
        if (string.IsNullOrEmpty(value))
            throw new Exception(message);
    }

    internal static void ErrorGuard(Action value)
    {
        try
        {
            value();
        }
        catch (Exception e)
        {
            LogError(e.Message);
        }
    }

    internal static void Assert(bool condition, string? message)
    {
        System.Diagnostics.Debug.Assert(condition, message);
    }

    internal static void Assert(bool condition)
    {
        System.Diagnostics.Debug.Assert(condition);
    }
}
