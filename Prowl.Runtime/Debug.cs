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

    public static void Log(string message, bool showNotification = false)
    {
        if (!Configuration.DoDebugLogs)
            return;
        Log("", message, ConsoleColor.White, LogSeverity.Normal);
        if (showNotification)
            ImGuiNotify.InsertNotification(new ImGuiToast() { Title = message, Type = ImGuiToastType.Info });
    }

    public static void LogWarning(string message, bool showNotification = false)
    {
        if (!Configuration.DoDebugWarnings)
            return;
        Log("Warning: ", message, ConsoleColor.Yellow, LogSeverity.Warning);
        if (showNotification)
            ImGuiNotify.InsertNotification(new ImGuiToast() { Title = message, Type = ImGuiToastType.Warning });
    }

    public static void LogError(string message, bool showNotification = false)
    {
        if (!Configuration.DoDebugErrors)
            return;
        Log("Error: ", message, ConsoleColor.Red, LogSeverity.Error);
        if (showNotification)
            ImGuiNotify.InsertNotification(new ImGuiToast() { Title = message, Type = ImGuiToastType.Error });
    }

    public static void LogSuccess(string message, bool showNotification = false)
    {
        if (!Configuration.DoDebugSuccess)
            return;
        Log("Success: ", message, ConsoleColor.Green, LogSeverity.Success);
        if (showNotification)
            ImGuiNotify.InsertNotification(new ImGuiToast() { Title = message, Type = ImGuiToastType.Success });
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
