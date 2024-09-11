// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;

namespace Prowl.Runtime;

public enum LogSeverity
{
    Success,
    Normal,
    Warning,
    Error,
    Exception
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


    internal static void LogCompilerError(object message)
        => Log(message.ToString(), ConsoleColor.Red, LogSeverity.Error);


    // NOTE : StackTrace is pretty fast on modern .NET, so it's safe to keep it on by default, since it gives useful line numbers for debugging purposes, etc.
    // For those concerned, rendering the console text takes comparatively longer than collecting a stack trace.
    private static void Log(string message, ConsoleColor color, LogSeverity logSeverity)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = prevColor;

        OnLog?.Invoke(message, new StackTrace(2, true), logSeverity);
    }


    public static void LogException(Exception exception)
    {
        ConsoleColor prevColor = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(exception.ToString());
        Console.ForegroundColor = prevColor;

        StackFrame frame = new StackFrame("my/file/path", 10, 4);
        StackTrace stack = new StackTrace(frame);
        StackFrame myFrame = stack.GetFrame(0);

        OnLog?.Invoke(exception.Message, new StackTrace(exception, true), LogSeverity.Exception);
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
