using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.ImGUI;
using Prowl.Runtime;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.OpenGL;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Silk.NET.Input;
using System.Reflection;

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
        Application.Initialize += () =>
        {
            // Editor-specific initialization code
            EditorGui.Initialize();
            ImporterAttribute.GenerateLookUp();

            // Start with the project window open
            //new OldProjectsWindow();
            new ProjectsWindow();
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
                    new HierarchyWindow();
                    new ViewportWindow();
                    new GameWindow();
                    new InspectorWindow();
                    new ConsoleWindow();
                    new AssetsBrowserWindow();
                    new AssetsTreeWindow();
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
                    MainMenuItems.SaveSceneAs();
                else if (Hotkeys.IsHotkeyDown("SaveScene", new() { Key = Key.S, Ctrl = true }))
                    MainMenuItems.SaveScene();
                else if (Hotkeys.IsHotkeyDown("BuildProject", new() { Key = Key.B, Ctrl = true }))
                    Project.BuildProject();

                Application.isPlaying = PlayMode.Current == PlayMode.Mode.Playing;


                try
                {
                    // Only handle input if the game window is focused
                    Input.Enabled = OldGameWindow.IsFocused;
                    SceneManager.Update();
                    Input.Enabled = true;
                }
                catch (Exception e)
                {
                    Debug.LogError("Scene Update Error: " + e.ToString());
                }
            }

            OldGameWindow.IsFocused = false;
        };

        Application.Render += (delta) =>
        {
            Graphics.StartFrame();

            EditorGui.Update();

            Graphics.EndFrame();

            //if (Project.HasProject) {
            //
            //    //                var drawlist = new UIDrawList();
            //    //                drawlist.PushClipRectFullScreen();
            //    //                drawlist.PushTextureID(UIDrawList._fontAtlas.TexID);
            //    //
            //    //                // Test AddLine
            //    //                drawlist.AddLine(new Vector2(10, 10), new Vector2(100, 100), 0xFF0000FF, 2.0f);
            //    //
            //    //                // Test AddRect
            //    //                drawlist.AddRect(new Vector2(200, 50), new Vector2(350, 150), 0xFF00FF00, 5.0f, 0, 2.0f);
            //    //
            //    //                // Test AddRectFilled
            //    //                drawlist.AddRectFilled(new Vector2(400, 50), new Vector2(550, 150), 0xFFFF0000, 10.0f, 0x0F);
            //    //
            //    //                // Test AddRectFilledMultiColor
            //    //                drawlist.AddRectFilledMultiColor(new Vector2(600, 50), new Vector2(750, 150),
            //    //                    0xFF0000FF, 0xFF00FF00,
            //    //                    0xFFFF0000, 0xFF000000);
            //    //
            //    //                // Test AddTriangle
            //    //                drawlist.AddTriangle(new Vector2(50, 200), new Vector2(100, 250), new Vector2(150, 200), 0xFFFF00FF, 2.0f);
            //    //
            //    //                // Test AddTriangleFilled
            //    //                drawlist.AddTriangleFilled(new Vector2(200, 200), new Vector2(250, 250), new Vector2(300, 200), 0xFF00FFFF);
            //    //
            //    //                // Test AddCircle
            //    //                drawlist.AddCircle(new Vector2(400, 225), 50, 0xFFFFFF00, 32, 3.0f);
            //    //
            //    //                // Test AddCircleFilled
            //    //                drawlist.AddCircleFilled(new Vector2(550, 225), 50, 0xFF00FFFF, 32);
            //    //
            //    //                // Test AddPolyline
            //    //                var points = new UIBuffer<Vector2>();
            //    //                points.Add(new Vector2(50, 300));
            //    //                points.Add(new Vector2(100, 350));
            //    //                points.Add(new Vector2(150, 325));
            //    //                points.Add(new Vector2(200, 350));
            //    //                points.Add(new Vector2(250, 300));
            //    //                drawlist.AddPolyline(points, points.Count, 0xFFFF00FF, false, 3.0f, true);
            //    //
            //    //                // Test AddConvexPolyFilled
            //    //                var points2 = new UIBuffer<Vector2>();
            //    //                points2.Add(new Vector2(350, 300));
            //    //                points2.Add(new Vector2(400, 350));
            //    //                points2.Add(new Vector2(450, 325));
            //    //                points2.Add(new Vector2(475, 275));
            //    //                points2.Add(new Vector2(425, 250));
            //    //                drawlist.AddConvexPolyFilled(points2, points2.Count, 0xFF00FF00, true);
            //    //
            //    //                // Test AddBezierCurve
            //    //                drawlist.AddBezierCurve(new Vector2(50, 400), new Vector2(100, 450), new Vector2(150, 350), new Vector2(200, 400), 0xFFFFFFFF, 2.0f, 20);
            //    //
            //    //                if (testImage == null) {
            //    //                    testImage = UIDrawList._fontAtlas.TexID as GraphicsTexture;
            //    //                    //using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.FileIcon.png"))
            //    //                    //    testImage = Texture2DLoader.FromStream(stream).Handle;
            //    //                }
            //    //
            //    //                drawlist.AddImage(testImage, new Vector2(350, 350), new Vector2(650, 650));
            //    //
            //    //                drawlist.AddText(UIDrawList._fontAtlas.Fonts[0], 20f, new Vector2(1375, 300), 0xFFFFFFFF, @"
            //    //Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
            //    //Nam interdum nec ante et condimentum. Aliquam quis viverra 
            //    //odio. Etiam vel tortor in ante lobortis tristique non in 
            //    //mauris. Maecenas massa tellus, aliquet vel massa eget, 
            //    //commodo commodo neque. In at erat ut nisi aliquam 
            //    //condimentum eu vitae quam. Suspendisse tristique euismod 
            //    //libero. Cras non massa nibh.
            //    //
            //    //Suspendisse id justo nibh. Nam ut diam id nunc ultrices 
            //    //aliquam cursus at ipsum. Praesent dapibus mauris gravida 
            //    //massa dapibus, vitae posuere magna finibus. Phasellus 
            //    //dignissim libero metus, vitae tincidunt massa lacinia eget. 
            //    //Cras sed viverra tortor. Vivamus iaculis faucibus ex non 
            //    //suscipit. In fringilla tellus at lorem sollicitudin, ut 
            //    //placerat nibh mollis. Nullam tortor elit, aliquet ac 
            //    //efficitur vel, ornare eget nibh. Vivamus condimentum, dui 
            //    //id vehicula iaculis, velit velit pulvinar nisi, mollis 
            //    //blandit nibh arcu ut magna. Vivamus condimentum in magna in 
            //    //aliquam. Donec vitae elementum neque. Nam ac ipsum id orci 
            //    //finibus fringilla. Nulla non justo a augue congue dictum. 
            //    //Vestibulum in quam id nibh blandit laoreet.
            //    //");
            //    //
            //    //                drawlist.PopClipRect();
            //    //                drawlist.PopTextureID();
            //    //                UIDrawList.Draw(GLDevice.GL, Graphics.Resolution, [drawlist]);
            //
            //    if (font == null)
            //    {
            //        //testImage = UIDrawList._fontAtlas.TexID as GraphicsTexture;
            //        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prowl.Editor.EmbeddedResources.font.ttf"))
            //        {
            //            using (MemoryStream ms = new())
            //            {
            //                stream.CopyTo(ms);
            //                font = Font.CreateFromTTFMemory(ms.ToArray(), 20, 512, 512, [Font.CharacterRange.BasicLatin]);
            //            }
            //        }
            //    }
            //
            //    Runtime.GUI.TestGUI.Test(font);
            //}
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

                try
                {
                    // Unload External Assemblies
                    AssemblyManager.Unload();

                    // Delete everything under Temp\Bin
                    if (Directory.Exists(Path.Combine(Project.TempDirectory, "bin")))
                        Directory.Delete(Path.Combine(Project.TempDirectory, "bin"), true);
                    Directory.CreateDirectory(Path.Combine(Project.TempDirectory, "bin"));

                    // Compile the Projects
                    Project.Compile(Project.Assembly_Proj);
                    Project.Compile(Project.Editor_Assembly_Proj);

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
