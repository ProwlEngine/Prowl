﻿// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;
using Prowl.Runtime.SceneManagement;

using Veldrid;

namespace Prowl.Runtime;

public static class Application
{
    public static bool IsRunning;
    public static bool IsPlaying = false;
    public static bool IsEditor { get; private set; }

    public static string? DataPath = null;

    public static IAssetProvider AssetProvider;

    public static event Action Initialize;
    public static event Action Update;
    public static event Action Render;
    public static event Action Quitting;

    private static readonly TimeData s_appTime = new();

    private static readonly GraphicsBackend[] s_preferredWindowsBackends = // Covers Windows/UWP
    [
        GraphicsBackend.Vulkan,
        GraphicsBackend.OpenGL,
        GraphicsBackend.Direct3D11,
        GraphicsBackend.OpenGLES,
    ];

    private static readonly GraphicsBackend[] s_preferredUnixBackends = // Cover Unix-like (Linux, FreeBSD, OpenBSD)
    [
        GraphicsBackend.Vulkan,
        GraphicsBackend.OpenGL,
        GraphicsBackend.OpenGLES,
    ];

    private static readonly GraphicsBackend[] s_preferredMacBackends = // Covers MacOS/Apple
    [
        GraphicsBackend.Metal,
        GraphicsBackend.OpenGL,
        GraphicsBackend.OpenGLES,
    ];

    public static GraphicsBackend GetBackend()
    {
        if (RuntimeUtils.IsWindows())
        {
            return s_preferredWindowsBackends[0];
        }
        else if (RuntimeUtils.IsMac())
        {
            return s_preferredMacBackends[0];
        }

        return s_preferredUnixBackends[0];
    }

    public static void Run(string title, int width, int height, IAssetProvider assetProvider, bool editor)
    {
        AssetProvider = assetProvider;
        IsEditor = editor;

        Debug.Log("Initializing...");

        Screen.Load += AppInitialize;

        Screen.Update += AppUpdate;

        Screen.Closing += AppClose;

        IsRunning = true;
        IsPlaying = true; // Base application is not the editor, isplaying is always true

        Screen.Start($"{title} - {GetBackend()}", new Vector2Int(width, height), new Vector2Int(100, 100), WindowState.Normal);
    }

    static void AppInitialize()
    {
        Graphics.Initialize(true, GetBackend());
        SceneManager.Initialize();
        AudioSystem.Initialize();

        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

        AssemblyManager.Initialize();

        Initialize?.Invoke();

        Debug.LogSuccess("Initialization complete");
    }

    static void AppUpdate()
    {
        try
        {
            AudioSystem.UpdatePool();

            s_appTime.Update();

            Time.TimeStack.Push(s_appTime);

            Update?.Invoke();
            Render?.Invoke();

            Time.TimeStack.Pop();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    static void AppClose()
    {
        isRunning = false;
        Quitting?.Invoke();
        Graphics.Dispose();
        Physics.Dispose();
        AudioSystem.Dispose();
        AssemblyManager.Dispose();
        Debug.Log("Is terminating...");
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Debug.Log("[Unhandled Exception] " + (e.ExceptionObject as Exception).Message + "\n" + (e.ExceptionObject as Exception).StackTrace);
    }

    public static void Quit()
    {
        Screen.Close();
    }
}
