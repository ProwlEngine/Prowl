using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Veldrid;
using System.Text.RegularExpressions;

namespace Prowl.Editor;

public static class Program
{
    static string testShader = 
"""
Shader "UI/UI Shader"

Properties
{

}

Pass "DefaultPass"
{
    // Depth-Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite On
        
        // Comparison kind
        DepthTest LessEqual
    }

    Blend
    {    
        Src Color SourceAlpha
        Src Alpha One

        Dest Color InverseSourceAlpha
        Dest Alpha InverseSourceAlpha

        Mode Alpha Add
        Mode Color Add
    }

    // Rasterizer culling mode
    Cull None

    Inputs
    {
        VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
            Colors
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer
            {
                ProjMtx Matrix4x4
            }
        }

        // Set 1
        Set
        {
            // Binding 0-1
            SampledTexture SurfaceTexture
        }
    }

    // Program vertex stage example
    PROGRAM VERTEX
        layout (location = 0) in vec3 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        
        layout(set = 0, binding = 0) uniform ProjBuffer
        {
            mat4 ProjMtx;
        };
        
        layout (location = 0) out vec2 Frag_UV;
        layout (location = 1) out vec4 Frag_Color;

        layout (constant_id = 100) const bool ClipYInverted = true;

        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;

            gl_Position = ProjMtx * vec4(Position, 1.0);

            if (ClipYInverted)
            {
                gl_Position.y = -gl_Position.y;
            }
        }
    ENDPROGRAM

    // Program fragment stage example
	PROGRAM FRAGMENT
        layout (location = 0) in vec2 Frag_UV;
        layout (location = 1) in vec4 Frag_Color;

        layout(set = 1, binding = 0) uniform texture2D SurfaceTexture;
        layout(set = 1, binding = 1) uniform sampler SurfaceSampler;
        
        layout (location = 0) out vec4 Out_Color;

        void main() {
            vec4 color = texture(sampler2D(SurfaceTexture, SurfaceSampler), Frag_UV);
        
            // Gamma Correct
            color = pow(color, vec4(1.0 / 1.43));
        
            Out_Color = Frag_Color * color;
        }
	ENDPROGRAM
}
""";

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
            Runtime.Shader sh = ShaderImporter.CreateShader(testShader);

            Debug.Log(sh.GetStringRepresentation()); 
        
            // Editor-specific initialization code
            EditorGuiManager.Initialize();
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            //new OldProjectsWindow();
            new ProjectsWindow();
        };

        Application.Update += () =>
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

                    // new ProjectSettingsWindow();
                    // new PreferencesWindow();
                    // new AssetSelectorWindow(typeof(Texture2D), (guid, fileid) => {  });
                }

                Application.DataPath = Project.ProjectDirectory;

                if (GeneralPreferences.Instance.LockFPS)
                {
                    Graphics.VSync = false;
                    Screen.FramesPerSecond = GeneralPreferences.Instance.TargetFPS;
                }
                else
                {
                    Graphics.VSync = GeneralPreferences.Instance.VSync;
                    Screen.FramesPerSecond = 0;
                }

                if (Hotkeys.IsHotkeyDown("SaveSceneAs", new() { Key = Runtime.Key.S, Ctrl = true, Shift = true }))
                    EditorGuiManager.SaveSceneAs();
                else if (Hotkeys.IsHotkeyDown("SaveScene", new() { Key = Runtime.Key.S, Ctrl = true }))
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

        Application.Render += () =>
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
                    Debug.LogError($"Error reloading assemblies: {e.Message}");
                    Debug.LogError(e.StackTrace);
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
                Debug.LogError("Cannot reload assemblies, No project loaded.");
            }
        }
    }
}
