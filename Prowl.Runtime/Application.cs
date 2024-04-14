using Prowl.Runtime.Audio;
using Prowl.Runtime.SceneManagement;
using System;

namespace Prowl.Runtime;

public static class Application
{
    public static bool isRunning;
    public static bool isPlaying = false;
    public static bool isActivelyPlaying = false;
    public static bool isEditor { get; private set; }

    public static string? DataPath = null;

    public static IAssetProvider AssetProvider;

    public static event Action Initialize;
    public static event Action<double> Update;
    public static event Action<double> Render;
    public static event Action Quitting;

    public static void Run(string title, int width, int height, IAssetProvider assetProvider, bool editor)
    {
        AssetProvider = assetProvider;
        isEditor = editor;

        Debug.Log("Initializing...");

        Window.InitWindow(title, width, height, Silk.NET.Windowing.WindowState.Normal, true);

        Window.Load += () => {
            SceneManager.Initialize();
            AudioSystem.Initialize();

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            AssemblyManager.Initialize();

            Initialize?.Invoke();

            Debug.LogSuccess("Initialization complete");
        };

        Window.Update += (delta) => {
            try
            {
                AudioSystem.UpdatePool();
                Time.Update(delta);

                Update?.Invoke(delta);
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        };

        Window.Render += (delta) => {
            Render?.Invoke(delta);
        };

        Window.Closing += () => {
            isRunning = false;
            Quitting?.Invoke();
            Physics.Dispose();
            AudioSystem.Dispose();
            AssemblyManager.Dispose();
            Debug.Log("Is terminating...");
        };

        isRunning = true;
        isPlaying = true; // Base application is not the editor, isplaying is always true
        isActivelyPlaying = true; // Base application is not the editor, isActivelyPlaying is always true
        Window.Start();
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Debug.Log("[Unhandled Exception] " + (e.ExceptionObject as Exception).Message + "\n" + (e.ExceptionObject as Exception).StackTrace);
    }

    public static void Quit()
    {
        Window.Stop();
    }
    
}
