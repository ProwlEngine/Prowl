using Prowl.Runtime.Audio;
using Prowl.Runtime.SceneManagement;
using Veldrid;
using System;

namespace Prowl.Runtime;

public static class Application
{
    public static bool isRunning;
    public static bool isPlaying = false;
    public static bool isEditor { get; private set; }

    public static string? DataPath = null;

    public static IAssetProvider AssetProvider;

    public static event Action Initialize;
    public static event Action Update;
    public static event Action Render;
    public static event Action Quitting;

    public static void Run(string title, int width, int height, IAssetProvider assetProvider, bool editor)
    {
        AssetProvider = assetProvider;
        isEditor = editor;

        Debug.Log("Initializing...");

        Screen.Load += AppInitialize;

        Screen.Update += AppUpdate;

        Screen.Closing += AppClose;

        isRunning = true;
        isPlaying = true; // Base application is not the editor, isplaying is always true
        
        Screen.Start(title, new Vector2Int(width, height), new Vector2Int(100, 100), WindowState.Normal);
    }

    static void AppInitialize()
    {
        Input.Initialize();
        Graphics.Initialize(true, GraphicsBackend.OpenGL);
        SceneManager.Initialize();
        AudioSystem.Initialize();
        Time.Initialize();

        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

        AssemblyManager.Initialize();

        //if (UIDrawList.DefaultFont == null)
        //    UIDrawList.CreateDeviceResources(GLDevice.GL);

        Initialize?.Invoke();

        Debug.LogSuccess("Initialization complete");
    }

    static void AppUpdate()
    {
        try
        {
            AudioSystem.UpdatePool();
            Time.Update();

            Update?.Invoke();
        
            Input.LateUpdate();

            Render?.Invoke();
        } catch (Exception e) {
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
        Screen.Stop();
    }
}
