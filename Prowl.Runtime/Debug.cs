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

    public static void Log(string message)
    {
        if (!Configuration.DoDebugLogs)
            return;
        Log("", message, ConsoleColor.White, LogSeverity.Normal);
    }

    public static void LogWarning(string message)
    {
        if (!Configuration.DoDebugWarnings)
            return;
        Log("Warning: ", message, ConsoleColor.Yellow, LogSeverity.Warning);
    }

    public static void LogError(string message)
    {
        if (!Configuration.DoDebugErrors)
            return;
        Log("Error: ", message, ConsoleColor.Red, LogSeverity.Error);
    }

    public static void LogSuccess(string message)
    {
        if (!Configuration.DoDebugSuccess)
            return;
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
}
