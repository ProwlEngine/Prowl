using Prowl.Runtime.Audio;
using Prowl.Runtime.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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

    public virtual void Initialize()
    {
        Debug.Log("Initializing...");

        // TODO: Load Config Settings from file'

        Window.InitWindow("Prowl", 1920, 1080, Silk.NET.Windowing.WindowState.Normal, true);

        Window.Load += () => {
            SceneManager.Initialize();
            Physics.Initialize();
            AudioSystem.Initialize();

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            Debug.LogSuccess("Initialization complete");
        };

        Window.Update += (delta) => {
            try
            {
                AudioSystem.UpdatePool();
                Time.Update(delta);

                Physics.Update();
                SceneManager.Update();
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        };

        Window.Render += (delta) => {
            Graphics.StartFrame();

            SceneManager.Draw();

            Graphics.EndFrame();
        };

        Window.Closing += () => {
            isRunning = false;
            Physics.Dispose();
            Debug.Log("Is terminating...");
        };

        isRunning = true;
        isPlaying = true; // Base application is not the editor, isplaying is always true
        isActivelyPlaying = true; // Base application is not the editor, isActivelyPlaying is always true
        Window.Start();
    }

    protected static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log the exception, display it, etc
        Console.WriteLine((e.ExceptionObject as Exception).Message);
    }

    public static void Quit()
    {
        Window.Stop();
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
