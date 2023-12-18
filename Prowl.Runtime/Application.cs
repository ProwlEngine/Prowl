using Prowl.Runtime.ImGUI;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Prowl.Runtime;

public abstract class Application
{
    public static bool isRunning { get; protected set; }
    public static bool isPlaying { get; protected set; } = false;
    public static bool isActivelyPlaying { get; protected set; } = false;
    public static bool isEditor { get; protected set; } = false;

    public static IAssetProvider AssetProvider { get; set; }


    
    protected readonly ExternalAssemblyLoadContextManager _AssemblyManager = new();
    public IEnumerable<Assembly> ExternalAssemblies => _AssemblyManager.ExternalAssemblies;

    protected double physicsTimer = 0;

    protected ImGUIController controller;

    public virtual void Initialize()
    {
        Debug.Log("Initializing...");

        // TODO: Load Config Settings from file'
        unsafe
        {
            Raylib.SetTraceLogCallback(&Logging.LogConsole);
        }
        Raylib.SetTraceLogLevel(TraceLogLevel.LOG_ERROR);
        Raylib.SetConfigFlags(ConfigFlags.FLAG_VSYNC_HINT | ConfigFlags.FLAG_WINDOW_RESIZABLE);
        Raylib.InitWindow(1280, 720, "Prowl");
        Raylib.SetTargetFPS(60);

        Raylib.InitAudioDevice();

        controller = new ImGUIController();
        controller.Load(1280, 720);

        SceneManager.Initialize();
        Physics.Initialize();

        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

        Debug.LogSuccess("Initialization complete");
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log the exception, display it, etc
        Console.WriteLine((e.ExceptionObject as Exception).Message);
    }

    public virtual void Run()
    {
        if (isRunning)
            throw new Exception("Application is already running!");
        isRunning = true;

        // starts loops on all threads
        Debug.If(!Raylib.IsWindowReady(), "rendering engine has not yet been initialized or initialization has not been fully completed");
        Debug.If(!Raylib.IsAudioDeviceReady(), "Audio engine has not yet been initialized or initialization has not been fully completed");
        //Console.If(!PhysicsEngine.IsInit, "physics engine has not yet been initialized or initialization has not been fully completed");

        try
        {
            Loop();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    protected virtual void Loop()
    {
        Stopwatch updateTimer = new();
        updateTimer.Start();

        while (isRunning)
        {
            isPlaying = true; // Base application is not the editor, isplaying is always true
            isActivelyPlaying = true; // Base application is not the editor, isActivelyPlaying is always true

            float updateTime = (float)updateTimer.Elapsed.TotalSeconds;
            Time.Update(updateTime);
            updateTimer.Restart();
            Graphics.Update();
            SceneManager.Update();
            Physics.Update();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Raylib_cs.Color.DARKGRAY);

            SceneManager.Draw();
            controller.Draw();

            Raylib.EndDrawing();

            if (Raylib.WindowShouldClose())
                Quit();
        }

        Physics.Dispose();
    }

    public static void Quit()
    {
        isRunning = false;
        Debug.Log("Is terminating...");
    }


    public static void ClearTypeDescriptorCache() {
        var typeConverterAssembly = typeof(TypeConverter).Assembly;
        
        var reflectTypeDescriptionProviderType = typeConverterAssembly.GetType("System.ComponentModel.ReflectTypeDescriptionProvider");
        var reflectTypeDescriptorProviderTable = reflectTypeDescriptionProviderType.GetField("s_attributeCache", BindingFlags.Static | BindingFlags.NonPublic);
        var attributeCacheTable = (Hashtable)reflectTypeDescriptorProviderTable.GetValue(null);
        attributeCacheTable?.Clear();
        
        var reflectTypeDescriptorType = typeConverterAssembly.GetType("System.ComponentModel.TypeDescriptor");
        var reflectTypeDescriptorTypeTable = reflectTypeDescriptorType.GetField("s_defaultProviders", BindingFlags.Static | BindingFlags.NonPublic);
        var defaultProvidersTable = (Hashtable)reflectTypeDescriptorTypeTable.GetValue(null);
        defaultProvidersTable?.Clear();
        
        var providerTableWeakTable = (Hashtable)reflectTypeDescriptorType.GetField("s_providerTable", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        providerTableWeakTable?.Clear();
    }
    
}
