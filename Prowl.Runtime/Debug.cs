// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;

namespace Prowl.Runtime;

public enum LogSeverity
{
    Success = 1 << 0,
    Normal = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
    Exception = 1 << 4
}

public delegate void OnLog(string message, StackTrace? stackTrace, LogSeverity logSeverity);

public static class Debug
{
    public static event OnLog? OnLog;

    public static void Log(object message)
        => Log(message.ToString(), ConsoleColor.White, LogSeverity.Normal);


    public static void Log(string message)
        => Log(message, ConsoleColor.White, LogSeverity.Normal);


    public static void LogWarning(object message)
        => Log(message.ToString(), ConsoleColor.Yellow, LogSeverity.Warning);


    public static void LogWarning(string message)
        => Log(message, ConsoleColor.Yellow, LogSeverity.Warning);


    public static void LogError(object message)
        => Log(message.ToString(), ConsoleColor.Red, LogSeverity.Error);


    public static void LogError(string message)
        => Log(message, ConsoleColor.Red, LogSeverity.Error);

    public static void LogSuccess(object message)
        => Log(message.ToString(), ConsoleColor.Green, LogSeverity.Success);


    public static void LogSuccess(string message)
        => Log(message, ConsoleColor.Green, LogSeverity.Success);


    public static void LogException(Exception exception)
        => Log(exception.Message, ConsoleColor.Red, LogSeverity.Exception, new StackTrace(exception, true));


    // NOTE : StackTrace is pretty fast on modern .NET, so it's nice to keep it on by default, since it gives useful line numbers for debugging purposes.
    // For reference, getting a stack trace on a modern machine takes around 15 μs at a depth of 15.
    public static void Log(string message, ConsoleColor color, LogSeverity logSeverity, StackTrace? customTrace = null)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prevColor;

        OnLog?.Invoke(message, customTrace ?? new StackTrace(2, true), logSeverity);
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


    public static void Assert(bool condition, string? message)
        => System.Diagnostics.Debug.Assert(condition, message);


    public static void Assert(bool condition)
        => System.Diagnostics.Debug.Assert(condition);
}
