using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Silk.NET.Input;
using System.Text.RegularExpressions;

namespace Prowl.Editor;

public static class Program
{
    public static event Action? OnDrawEditor;
    public static event Action? OnUpdateEditor;

    public static bool IsReloadingExternalAssemblies { get; private set; }
    public static void RegisterReloadOfExternalAssemblies() => IsReloadingExternalAssemblies = true;

    private static bool CreatedDefaultWindows = false;

    public static int Main(string[] args)
    {
        // set global Culture to invariant
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Application.Initialize += () =>
        {
            // Editor-specific initialization code
            EditorGuiManager.Initialize();
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            //new OldProjectsWindow();
            new ProjectsWindow();

            string testShader = @"
Shader ""Example/NewShaderFormat""

Properties
{
	// Material property declarations go here
	_MainTex(""Albedo Map"", TEXTURE2D)
	_NormalTex(""Normal Map"", TEXTURE2D)
	_EmissionTex(""Emissive Map"", TEXTURE2D)
	_SurfaceTex(""Surface Map x:AO y:Rough z:Metal"", TEXTURE2D)
	_OcclusionTex(""Occlusion Map"", TEXTURE2D)

	_EmissiveColor(""Emissive Color"", COLOR)
	_EmissionIntensity(""Emissive Intensity"", FLOAT)
	_MainColor(""Main Color"", COLOR)
}

// Global state or options applied to every pass. If a pass doesn't specify a value, it will use the ones defined here
Global
{
    Tags { ""SomeShaderID"" = ""IsSomeShaderType"", ""SomeOtherValue"" = ""SomeOtherType"" }

    // Blend state- can be predefined state...
    Blend Off
    
    // ...or custom values
    Blend
    {    
        Src Color OneMinusSrcAlpha
        Dest Alpha One

        Mode Alpha SubtractDest
        
        Mask None
    }

    // Stencil state
    Stencil
    {
        Ref 25
        ReadMask 26
        WriteMask 27

        Comparison Front Greater

        Pass Front Keep
        Fail Back Zero
        ZFail Front Replace
    }

    // Depth write
    DepthWrite On
    
    // Comparison kind
    DepthTest LessEqual

    // Rasterizer culling mode
    Cull Back

    // Global includes added to every program
    GlobalInclude 
    {
        // This value would be able to be used in every <Program> block
        vec4 aDefaultValue = vec4(0.5, 0.25, 0.75, 1.0);
    }
}



Pass ""DefaultPass"" 
{
    Tags { ""SomeValue"" = ""CustomPassType"", ""SomeOtherValue"" = ""SomeOtherType"" }

    Blend SingleOverride

    Stencil
    {
        Ref 5
        ReadMask 10
        WriteMask 14

        Comparison Back Greater
        Comparison Front LessEqual

        Pass Front IncrementWrap
        Pass Back Replace

        ZFail Front Zero
    }

    DepthWrite Off
    DepthTest Never
    Cull Off

    // Program vertex stage example
    Program Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
		layout (location = 0) out vec4 someColor; 
		
		void main()
		{
			someColor = aDefaultValue;
            gl_Position = vertexPosition;
		}
    }

    // Program fragment stage example
	Program Fragment
    {
        layout (location = 0) in vec4 someColor; 
    	layout (location = 0) out vec4 outColor; 
		
		void main()
		{
			outColor = someColor;
		}
	}
}

Pass ""AnotherPass""
{
    Tags { ""SomeValue"" = ""CustomPassType"" }

    Blend
    {
        Target 2

        Src Alpha OneMinusSrcAlpha
        Src Color DstColor

        Dest Alpha One
        Dest Color OneMinusBlendFactor

        Mode Alpha Max
        Mode Color SubtractDest
        
        Mask RGB
    }

    DepthWrite On
    DepthTest LessEqual
    Cull Back

    // Program vertex stage example
    Program Vertex
    {
        layout (location = 0) in vec3 vertexPosition;

		layout (location = 0) out vec4 someColor; 
		
		void main()
		{
            #if MY_KEYWORD == 0

            #elif

			someColor = aDefaultValue;
            gl_Position = vertexPosition;
		}
    }

    // Program fragment stage example
	Program Fragment
    {
        layout (location = 0) in vec4 someColor; 

    	layout (location = 0) out vec4 outColor; 
		
		void main()
		{
			outColor = someColor;
		}
	}
}

// If a pass doesn't compile, the whole shader is invalidated. Use a fallback replacement for the entire shader in that case.
// While per-pass fallbacks would be nice, there's no guarantee that the pass will always have a name or the correct index 
Fallback ""Fallback/TestShader""
";

            // Remove Comments
            // Remove single-line comments
            testShader = Regex.Replace(testShader, @"//.*", "");


            Prowl.Editor.VeldridShaderParser.VeldridShaderParser parser = new(testShader);
            var shader = parser.Parse();
            Console.WriteLine(shader.ToString());
        };

        Application.Update += (delta) =>
        {
            //EditorGui.SetupDock();

            AssetDatabase.InternalUpdate();

            if (PlayMode.Current == PlayMode.Mode.Editing) // Dont recompile scripts unless were in editor mode
                CheckReloadingAssemblies();

            // Editor-specific update code
            if (Project.HasProject)
            {
                Physics.Initialize();

                if (!CreatedDefaultWindows)
                {
                    CreatedDefaultWindows = true;
                    //new EditorMainMenubar();
                    var console = EditorGuiManager.DockWindowTo(new ConsoleWindow(), null, Docking.DockZone.Center);
                    var assetbrowser = EditorGuiManager.DockWindowTo(new AssetsBrowserWindow(), console, Docking.DockZone.Center);
                    // Add Asset Tree, When we do this AssetBrowser node will subdivide into two children
                    var assettree = EditorGuiManager.DockWindowTo(new AssetsTreeWindow(), assetbrowser, Docking.DockZone.Left, 0.2f);
                    // So for the Inspector we need to use the Child to dock now
                    var inspector = EditorGuiManager.DockWindowTo(new InspectorWindow(), assetbrowser.Child[1], Docking.DockZone.Right, 0.75f);
                    // Now Asset Browser is Subdivided twice, 
                    assetbrowser = assetbrowser.Child[1].Child[0];
                    var game = EditorGuiManager.DockWindowTo(new GameWindow(), assetbrowser, Docking.DockZone.Top, 0.65f);
                    var scene = EditorGuiManager.DockWindowTo(new SceneViewWindow(), game, Docking.DockZone.Center);

                    // and finally hierarchy on top of asset tree
                    var hierarchy = EditorGuiManager.DockWindowTo(new HierarchyWindow(), assettree, Docking.DockZone.Top, 0.65f);

                    //new ProjectSettingsWindow();
                    //new PreferencesWindow();
                    //new AssetSelectorWindow(typeof(Texture2D), (guid, fileid) => {  });
                }

                Application.DataPath = Project.ProjectDirectory;

                if (GeneralPreferences.Instance.LockFPS)
                {
                    Window.InternalWindow.VSync = false;
                    Window.InternalWindow.FramesPerSecond = GeneralPreferences.Instance.TargetFPS;
                    Window.InternalWindow.UpdatesPerSecond = GeneralPreferences.Instance.TargetFPS;
                }
                else
                {
                    Window.InternalWindow.FramesPerSecond = 0;
                    Window.InternalWindow.UpdatesPerSecond = 0;
                    Window.InternalWindow.VSync = GeneralPreferences.Instance.VSync;
                }

                if (Hotkeys.IsHotkeyDown("SaveSceneAs", new() { Key = Key.S, Ctrl = true, Shift = true }))
                    EditorGuiManager.SaveSceneAs();
                else if (Hotkeys.IsHotkeyDown("SaveScene", new() { Key = Key.S, Ctrl = true }))
                    EditorGuiManager.SaveScene();

                Application.isPlaying = PlayMode.Current == PlayMode.Mode.Playing;


                try
                {
                    bool hasGameWindow = GameWindow.LastFocused != null && GameWindow.LastFocused.IsAlive;
                    // Push GameWindow's input handler
                    if (hasGameWindow) Input.PushHandler((GameWindow.LastFocused.Target as GameWindow).InputHandler);
                    SceneManager.Update();
                    if (hasGameWindow) Input.PopHandler();
                }
                catch (Exception e)
                {
                    Debug.LogError("Scene Update Error: " + e.ToString());
                }
            }
        };

        Application.Render += (delta) =>
        {
            Graphics.StartFrame();

            EditorGuiManager.Update();

            Graphics.EndFrame();

        };

        Application.Quitting += () =>
        {
        };


        Application.Run("Prowl Editor", 1920, 1080, new EditorAssetProvider(), true);

        return 0;
    }


    public static Font font;
    private static GraphicsTexture testImage;

    public static void CheckReloadingAssemblies()
    {
        if (IsReloadingExternalAssemblies)
        {
            IsReloadingExternalAssemblies = false;

            if (Project.HasProject)
            {
                SceneManager.StoreScene();
                SceneManager.Clear();

                try
                {
                    // Unload External Assemblies
                    AssemblyManager.Unload();

                    // Delete everything under Temp\Bin
                    if (Directory.Exists(Path.Combine(Project.TempDirectory, "bin")))
                        Directory.Delete(Path.Combine(Project.TempDirectory, "bin"), true);
                    Directory.CreateDirectory(Path.Combine(Project.TempDirectory, "bin"));

                    // Compile the Projects
                    Project.Compile(Project.Assembly_Proj, new DirectoryInfo(Path.Combine(Project.TempDirectory, "bin", "Debug")));
                    Project.Compile(Project.Editor_Assembly_Proj, new DirectoryInfo(Path.Combine(Project.TempDirectory, "bin", "Debug")));

                    // Reload the External Assemblies
                    AssemblyManager.LoadExternalAssembly(Project.Editor_Assembly_DLL, true);
                    AssemblyManager.LoadExternalAssembly(Project.Assembly_DLL, true);
                }
                catch (Exception e)
                {
                    Runtime.Debug.LogError($"Error reloading assemblies: {e.Message}");
                    Runtime.Debug.LogError(e.StackTrace);
                }
                finally
                {
                    OnAssemblyLoadAttribute.Invoke();

                    SceneManager.RestoreScene();
                    SceneManager.ClearStoredScene();
                }
            }
            else
            {
                Runtime.Debug.LogError("Cannot reload assemblies, No project loaded.");
            }
        }
    }
}
