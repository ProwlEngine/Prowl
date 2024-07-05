using Prowl.Editor.Assets;
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
Shader "Default/Grid"

Pass
{
	Blend
    {    
        Src Color SourceAlpha
        Src Alpha SourceAlpha

        Dest Color InverseSourceAlpha
        Dest Alpha InverseSourceAlpha

        Mode Color Add
        Mode Alpha Add
    }

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite Off
        
        // Comparison kind
        DepthTest Off
    }

    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer
            {
                MvpInverse Matrix4x4
            }
        }

        // Set 1
        Set
        {
            // Binding 0
            Buffer
            {
                ScreenResolution Vector2
				CameraPosition Vector3
				LineWidth Float
				PrimaryGridSize Float
				SecondaryGridSize Float
            }
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		
        layout(location = 0) out vec3 Position;
		layout(location = 1) out vec2 TexCoords;
		
		layout(set = 0, binding = 0) uniform MVPBuffer
		{
			mat4 MvpInverse;
		};

		void main() 
		{
			gl_Position = vec4(vertexPosition, 1.0);
            // vertexPosition is in screen space, convert it into world space
            Position = (MvpInverse * vec4(vertexPosition, 1.0)).xyz;
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT
		layout(location = 0) out vec4 OutputColor;
		
        layout(location = 0) in vec3 Position;
		layout(location = 1) in vec2 TexCoords;

		layout(set = 1, binding = 0) uniform ResourceBuffer
		{
			vec2 ScreenResolution;
			vec3 CameraPosition;

			float LineWidth; 
			float PrimaryGridSize; 
			float SecondaryGridSize; 
		};
		
		// https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
		float pristineGrid(in vec2 uv, vec2 lineWidth)
		{
			vec2 ddx = dFdx(uv);
			vec2 ddy = dFdy(uv);

			vec2 uvDeriv = vec2(length(vec2(ddx.x, ddy.x)), length(vec2(ddx.y, ddy.y)));
			
			bvec2 invertLine = bvec2(lineWidth.x > 0.5, lineWidth.y > 0.5);
			
			vec2 targetWidth = vec2(
				invertLine.x ? 1.0 - lineWidth.x : lineWidth.x,
				invertLine.y ? 1.0 - lineWidth.y : lineWidth.y
			);
			
			vec2 drawWidth = clamp(targetWidth, uvDeriv, vec2(0.5));
			
			vec2 lineAA = uvDeriv * 1.5;
			
			vec2 gridUV = abs(fract(uv) * 2.0 - 1.0);
			
			gridUV.x = invertLine.x ? gridUV.x : 1.0 - gridUV.x;
			gridUV.y = invertLine.y ? gridUV.y : 1.0 - gridUV.y;
			
			vec2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
		
			grid2 *= clamp(targetWidth / drawWidth, 0.0, 1.0);
			grid2 = mix(grid2, targetWidth, clamp(uvDeriv * 2.0 - 1.0, 0.0, 1.0));
			
			grid2.x = invertLine.x ? 1.0 - grid2.x : grid2.x;
			grid2.y = invertLine.y ? 1.0 - grid2.y : grid2.y;
			
			return mix(grid2.x, 1.0, grid2.y);
		}
		
		float Grid(vec3 ro, float scale, vec3 rd, float lineWidth, out float d) 
		{
			ro /= scale;
			
			d = -ro.x / rd.x;
			if (d <= 0.0) return 0.0;
			vec2 p = (ro.zy + rd.zy * d);
			
			return pristineGrid(p, vec2(lineWidth * LineWidth));
		}

		void main()
		{
			float d = 0.0;
			float bd = 0.0;
			float sg = Grid(CameraPosition, PrimaryGridSize, normalize(Position), 0.02, d);
			float bg = Grid(CameraPosition, SecondaryGridSize, normalize(Position), 0.02, bd);
		
			if (abs(dot(normalize(Position), vec3(1.0, 0.0, 0.0))) > 0.005)
			{ 
				OutputColor = vec4(1.0, 1.0, 1.0, sg);
				OutputColor += vec4(1.0, 1.0, 1.0, bg * 0.5);
				//OutputColor *= mix(1.0, 0.0, min(1.0, d / 10000.0));
            }
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

                    PlayMode.GameTime.Update(delta);
                    Time.TimeStack.Push(PlayMode.GameTime);
                    SceneManager.Update();
                    Time.TimeStack.Pop();

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
