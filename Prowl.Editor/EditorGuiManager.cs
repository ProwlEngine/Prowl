// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Editor.Docking;
using Prowl.Editor.Preferences;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

public static class EditorGuiManager
{
    public static System.Numerics.Vector4 SelectedColor => new System.Numerics.Vector4(0.06f, 0.53f, 0.98f, 1.00f);

    public static Gui Gui;
    public static DockContainer Container;
    public static EditorWindow DraggingWindow;
    public static DockNode? DragSplitter;
    private static Vector2 m_DragPos;
    private static double m_StartSplitPos;

    public static WeakReference? FocusedWindow;

    public static List<EditorWindow> Windows = [];

    static readonly List<EditorWindow> WindowsToRemove = [];

    public static void Initialize()
    {
        Gui = new(EditorPreferences.Instance.AntiAliasing);
        Input.OnKeyEvent += Gui.SetKeyState;
        Input.OnMouseEvent += Gui.SetPointerState;
        Gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
        Gui.OnCursorVisibilitySet += (visible) => { Input.CursorVisible = visible; };
    }

    public static void FocusWindow(EditorWindow editorWindow)
    {
        if (FocusedWindow != null && FocusedWindow.Target != editorWindow)
            Gui.ClearFocus();

        Windows.Remove(editorWindow);
        Windows.Add(editorWindow);
        FocusedWindow = new WeakReference(editorWindow);

        if (editorWindow.IsDocked && editorWindow.Leaf is not null)
            editorWindow.Leaf.WindowNum = editorWindow.Leaf.LeafWindows.IndexOf(editorWindow);
    }

    internal static void Remove(EditorWindow editorWindow)
    {
        if (editorWindow != null)
            WindowsToRemove.Add(editorWindow);
    }

    public static DockNode? DockWindowTo(EditorWindow window, DockNode? node, DockZone zone, double split = 0.5f)
    {
        if (node != null)
            return Container.AttachWindow(window, node, zone, split);
        else
            return Container.AttachWindow(window, Container.Root, DockZone.Center, split);
    }

    public static void Update()
    {
        if (FocusedWindow != null && FocusedWindow.Target != null && FocusedWindow.Target is EditorWindow editorWindow)
            FocusWindow(editorWindow); // Ensure focused window is always on top (But below floating windows if docked)

        // Sort by docking as well, Docked windows are guranteed to come first
        Windows.Sort((a, b) => b.IsDocked.CompareTo(a.IsDocked));

        Rect screenRect = new Rect(0, 0, Graphics.TargetResolution.x, Graphics.TargetResolution.y);

        Vector2 framebufferAndInputScale = new((float)Graphics.TargetResolution.x / Screen.Size.x, Graphics.TargetResolution.y / (float)Screen.Size.y);

        Gui.PointerWheel = Input.MouseWheelDelta;
        double scale = EditorStylePrefs.Instance.Scale;

        Veldrid.CommandList commandList = Graphics.GetCommandList();
        commandList.Name = "GUI Command Buffer";

        commandList.SetFramebuffer(Graphics.ScreenTarget);
        commandList.ClearColorTarget(0, Veldrid.RgbaFloat.Black);
        commandList.ClearDepthStencil(1.0f, 0);

        Gui.ProcessFrame(commandList, screenRect, (float)scale, framebufferAndInputScale, EditorPreferences.Instance.AntiAliasing, (g) =>
        {
            // Draw Background
            g.Draw2D.DrawRectFilled(g.ScreenRect, EditorStylePrefs.Instance.Background);

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.ScaleChildren();

            if (Project.HasProject)
                DrawHeaderBar(g);

            DrawContent(g);
        }
        );

        foreach (var window in WindowsToRemove)
        {
            if (window.IsDocked)

                Container.DetachWindow(window);
            if (FocusedWindow != null && FocusedWindow.Target == window)
                FocusedWindow = null;

            Windows.Remove(window);
        }

        WindowsToRemove.Clear();

        Graphics.SubmitCommandList(commandList);

        commandList.Dispose();
    }


    private static void DrawHeaderBar(Gui g)
    {
        double padding = EditorStylePrefs.Instance.DockSpacing;
        double padx2 = padding * 2;

        using (g.Node("Main_Header").ExpandWidth().MaxHeight(EditorStylePrefs.Instance.ItemSize + (padding * 3)).Padding(padding * 2, padx2, padding, padx2).Enter())
        {
            using (g.Node("MenuBar").ExpandHeight().FitContentWidth().Layout(LayoutType.Row).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness);
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Borders, 2, (float)EditorStylePrefs.Instance.WindowRoundness);

                MenuItem.DrawMenuRoot("File", true, Font.DefaultFont.CalcTextSize("File", 0).x + 20);
                MenuItem.DrawMenuRoot("Edit", true, Font.DefaultFont.CalcTextSize("Edit", 0).x + 20);
                MenuItem.DrawMenuRoot("Assets", true, Font.DefaultFont.CalcTextSize("Assets", 0).x + 20);
                MenuItem.DrawMenuRoot("Create", true, Font.DefaultFont.CalcTextSize("Create", 0).x + 20);
                MenuItem.DrawMenuRoot("Windows", true, Font.DefaultFont.CalcTextSize("Windows", 0).x + 20);
            }

            using (g.Node("PlayMode").ExpandHeight().FitContentWidth().Layout(LayoutType.Row).Enter())
            {
                g.CurrentNode.Left(Offset.Percentage(0.5f, -(g.CurrentNode.LayoutData.Scale.x / 2)));

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness);
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Borders, 2, (float)EditorStylePrefs.Instance.WindowRoundness);

                switch (PlayMode.Current)
                {
                    case PlayMode.Mode.Editing:
                        if (EditorGUI.StyledButton(FontAwesome6.Play, EditorStylePrefs.Instance.ItemSize, EditorStylePrefs.Instance.ItemSize, false, tooltip: "Play"))
                            PlayMode.Start();
                        break;
                    case PlayMode.Mode.Playing:
                        if (EditorGUI.StyledButton(FontAwesome6.Pause, EditorStylePrefs.Instance.ItemSize, EditorStylePrefs.Instance.ItemSize, false, tooltip: "Pause"))
                            PlayMode.Pause();
                        if (EditorGUI.StyledButton(FontAwesome6.Stop, EditorStylePrefs.Instance.ItemSize, EditorStylePrefs.Instance.ItemSize, false, EditorStylePrefs.Red, tooltip: "Stop"))
                            PlayMode.Stop();
                        break;
                    case PlayMode.Mode.Paused:
                        if (EditorGUI.StyledButton(FontAwesome6.Play, EditorStylePrefs.Instance.ItemSize, EditorStylePrefs.Instance.ItemSize, false, tooltip: "Play"))
                            PlayMode.Resume();
                        if (EditorGUI.StyledButton(FontAwesome6.Stop, EditorStylePrefs.Instance.ItemSize, EditorStylePrefs.Instance.ItemSize, false, EditorStylePrefs.Red, tooltip: "Stop"))
                            PlayMode.Stop();
                        break;

                }
            }
        }
    }


    private static void DrawContent(Gui g)
    {
        using (g.Node("Main_Content").ExpandWidth().Enter())
        {
            Container ??= new();
            Rect rect = g.CurrentNode.LayoutData.Rect;
            //rect.Expand(-(float)EditorStylePrefs.Instance.DockSpacing);
            rect.Min.x += (float)EditorStylePrefs.Instance.DockSpacing;
            //rect.Min.y += (float)EditorStylePrefs.Instance.DockSpacing;
            rect.Max.x -= (float)EditorStylePrefs.Instance.DockSpacing;
            rect.Max.y -= (float)EditorStylePrefs.Instance.DockSpacing;
            //rect.Min.y = (float)EditorStylePrefs.Instance.DockSpacing; // Top needs no padding
            Container.Update(rect);


            if (DragSplitter != null)
            {
                DragSplitter.GetSplitterBounds(out var bmins, out var bmaxs, 4);

                g.SetZIndex(11000);
                g.Draw2D.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
                g.SetZIndex(0);

                if (!g.IsPointerDown(MouseButton.Left))
                    DragSplitter = null;
            }

            if (DraggingWindow == null)
            {
                Vector2 cursorPos = g.PointerPos;
                if (!g.IsPointerMoving && (g.ActiveID == 0 || g.ActiveID == null) && DragSplitter == null)
                {
                    if (!Gui.IsBlockedByInteractable(cursorPos))
                    {
                        DockNode? node = Container.Root.TraceSeparator(cursorPos.x, cursorPos.y);
                        if (node != null)
                        {
                            node.GetSplitterBounds(out var bmins, out var bmaxs, 4);

                            g.SetZIndex(11000);
                            g.Draw2D.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
                            g.SetZIndex(0);

                            if (g.IsPointerDown(MouseButton.Left))
                            {
                                m_DragPos = cursorPos;
                                DragSplitter = node;
                                if (DragSplitter.Type == DockNode.NodeType.SplitVertical)
                                    m_StartSplitPos = MathD.Lerp(DragSplitter.Mins.x, DragSplitter.Maxs.x, DragSplitter.SplitDistance);
                                else
                                    m_StartSplitPos = MathD.Lerp(DragSplitter.Mins.y, DragSplitter.Maxs.y, DragSplitter.SplitDistance);
                            }
                        }
                    }
                }
                else if (g.IsPointerMoving && DragSplitter != null)
                {
                    Vector2 dragDelta = cursorPos - m_DragPos;

                    const double minSize = 100;
                    if (DragSplitter.Type == DockNode.NodeType.SplitVertical)
                    {
                        double w = DragSplitter.Maxs.x - DragSplitter.Mins.x;
                        double split = m_StartSplitPos + dragDelta.x;
                        split -= DragSplitter.Mins.x;
                        split = Math.Floor(split);
                        split = Math.Clamp(split, minSize, w - minSize);
                        split /= w;

                        DragSplitter.SplitDistance = split;
                    }
                    else if (DragSplitter.Type == DockNode.NodeType.SplitHorizontal)
                    {
                        double h = DragSplitter.Maxs.y - DragSplitter.Mins.y;
                        double split = m_StartSplitPos + dragDelta.y;
                        split -= DragSplitter.Mins.y;
                        split = Math.Floor(split);
                        split = Math.Clamp(split, minSize, h - minSize);
                        split /= h;

                        DragSplitter.SplitDistance = split;
                    }
                }
            }

            // Focus Windows first
            var windowList = new List<EditorWindow>(Windows);
            for (int i = 0; i < windowList.Count; i++)
            {
                EditorWindow window = windowList[i];
                if (g.IsPointerHovering(window.Rect) && (g.IsPointerClick(MouseButton.Left) || g.IsPointerClick(MouseButton.Right)))
                    if (!g.IsBlockedByInteractable(g.PointerPos, window.MaxZ))
                        FocusWindow(window);
            }

            // Draw/Update Windows
            for (int i = 0; i < windowList.Count; i++)
            {
                EditorWindow window = windowList[i];
                if (!window.IsDocked || window.Leaf?.LeafWindows[window.Leaf.WindowNum] == window)
                {
                    g.SetZIndex(i * 100);
                    g.PushID((ulong)window._id);
                    window.ProcessFrame();
                    g.PopID();
                }

            }
            g.SetZIndex(0);
        }
    }


    #region MenuBar



    [MenuItem("File/New Scene")]
    public static void NewScene()
    {
        SceneManager.Clear();
        SceneManager.InstantiateNewScene();
    }

    [MenuItem("File/Save Scene")]
    public static void SaveScene()
    {
        Scene scene = SceneManager.MainScene;
        if (scene.AssetID == Guid.Empty || !AssetDatabase.Contains(scene.AssetID))
        {
            SaveSceneAs();
            return;
        }

        if (AssetDatabase.TryGetFile(scene.AssetID, out var file) && file is not null)
        {
            AssetDatabase.Delete(file);

            var allGameObjects = SceneManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
            scene = Scene.Create(allGameObjects);
            StringTagConverter.WriteToFile(Serializer.Serialize(scene), file);
            AssetDatabase.Update();
            AssetDatabase.Ping(file);
        }
    }

    [MenuItem("File/Save Scene As")]
    public static void SaveSceneAs()
    {
        FileDialogContext imFileDialogInfo = new FileDialogContext()
        {
            title = "Save As",
            resultName = "New Scene.scene",
            parentDirectory = Project.Active.AssetDirectory,
            type = FileDialogType.SaveFile,
            OnComplete = (path) =>
            {
                // Make sure path is relative to ProjectAssetDirectory
                var file = new FileInfo(path);
                if (!AssetDatabase.FileIsInProject(file))
                    return;

                if (File.Exists(path))
                    AssetDatabase.Delete(file);

                // If no extension (or wrong extension) add .scene
                if (!file.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                    file = new FileInfo(file.FullName + ".scene");

                var allGameObjects = SceneManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
                Scene scene = Scene.Create(allGameObjects);
                var tag = Serializer.Serialize(scene);
                StringTagConverter.WriteToFile(tag, file);
                AssetDatabase.Update();
                AssetDatabase.Ping(file);
            }
        };
        FileDialog.Open(imFileDialogInfo);
    }

    [MenuItem("File/Build Project")] public static void File_BuildProject() => new BuildWindow();


    #region Templates

    static Vector3 GetPosition()
    {
        // Last Focused Editor camera
        var cam = SceneViewWindow.LastFocusedCamera;
        // get position 10 units infront
        var t = cam.GameObject;
        return t.Transform.position + t.Transform.forward * 10;
    }

    [MenuItem("Create/3D/Cube")] public static void Create_3D_Cube() => CreateDefaultModel("Cube", null);
    [MenuItem("Create/3D/Sphere")] public static void Create_3D_Sphere() => CreateDefaultModel("Sphere", null);
    [MenuItem("Create/3D/Cylinder")] public static void Create_3D_Cylinder() => CreateDefaultModel("Cylinder", null);
    [MenuItem("Create/3D/Capsule")] public static void Create_3D_Capsule() => CreateDefaultModel("Capsule", null);
    [MenuItem("Create/3D/Plane")] public static void Create_3D_Plane() => CreateDefaultModel("Plane", null);
    [MenuItem("Create/3D/Quad")] public static void Create_3D_Quad() => CreateDefaultModel("Quad", null);

    private static void CreateDefaultModel(string name, Type? component)
    {
        var original = Application.AssetProvider.LoadAsset<GameObject>($"Defaults/{name}.obj");
        if (original.IsAvailable)
        {
            var go = GameObject.Instantiate(original.Res!);
            go.Transform.position = GetPosition();
            if (component != null)
                go.AddComponent(component);
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
    }

    [MenuItem("Create/Light/Directional Light")]
    public static void Create_Light_DirectionalLight()
    {
        var go = new GameObject("Directional Light");
        go.AddComponent<DirectionalLight>();
        go.Transform.position = GetPosition();
        go.Transform.localEulerAngles = new System.Numerics.Vector3(45, 70, 0);
        HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
    }

    [MenuItem("Create/Light/Point Light")]
    public static void Create_Light_PointLight()
    {
        var go = new GameObject("Point Light");
        go.AddComponent<PointLight>();
        go.Transform.position = GetPosition();
        HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
    }

    [MenuItem("Create/Light/Spot Light")]
    public static void Create_Light_SpotLight()
    {
        var go = new GameObject("Spot Light");
        go.AddComponent<SpotLight>();
        go.Transform.position = GetPosition();
        HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
    }

    #endregion


    #region Assets
    public static DirectoryInfo? Directory { get; set; }
    public static bool fromAssetBrowser = false;

    [MenuItem("Assets/Create/Folder")]
    public static void CreateDir()
    {
        Directory ??= Project.Active.AssetDirectory;

        DirectoryInfo dir = new(Path.Combine(Directory.FullName, "New Folder"));
        AssetDatabase.GenerateUniqueAssetPath(ref dir);
        dir.Create();
        if (fromAssetBrowser)
            AssetsBrowserWindow.StartRename(dir.FullName);
        else
            AssetsTreeWindow.StartRename(dir.FullName);
    }

    [MenuItem("Assets/Create/Material")]
    public static void CreateMaterial()
    {
        Directory ??= Project.Active.AssetDirectory;

        FileInfo file = new FileInfo(Path.Combine(Directory.FullName, $"New Material.mat"));
        AssetDatabase.GenerateUniqueAssetPath(ref file);

        Material mat = new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Standard.shader"));
        StringTagConverter.WriteToFile(Serializer.Serialize(mat), file);
        if (fromAssetBrowser)
            AssetsBrowserWindow.StartRename(file.FullName);
        else
            AssetsTreeWindow.StartRename(file.FullName);

        AssetDatabase.Update();
        AssetDatabase.Ping(file);
    }

    [MenuItem("Assets/Create/Script")]
    public static void CreateScript()
    {
        Directory ??= Project.Active.AssetDirectory;

        FileInfo file = new FileInfo(Path.Combine(Directory.FullName, $"New Script.cs"));
        AssetDatabase.GenerateUniqueAssetPath(ref file);

        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt") ?? throw new Exception();
        using StreamReader reader = new StreamReader(stream);
        string script = reader.ReadToEnd();
        script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(Path.GetFileNameWithoutExtension(file.Name)));
        File.WriteAllText(file.FullName, script);
        if (fromAssetBrowser)
            AssetsBrowserWindow.StartRename(file.FullName);
        else
            AssetsTreeWindow.StartRename(file.FullName);

        AssetDatabase.Update();
        AssetDatabase.Ping(file);
    }

    [MenuItem("Assets/Refresh Cache")]
    public static void RefreshCache() => AssetDatabase.Update(true, true);
    [MenuItem("Assets/Reimport All")]
    public static void ReimportAll() => AssetDatabase.ReimportAll();
    [MenuItem("Assets/Recompile")]
    public static void Recompile() => Program.RegisterReloadOfExternalAssemblies();


    #endregion

    [MenuItem("Edit/Project Settings")] public static void Edit_ProjectSettings() => new ProjectSettingsWindow();
    [MenuItem("Edit/Editor Preferences")] public static void Edit_Preferences() => new PreferencesWindow();

    [MenuItem("Windows/Package Manager")] public static void Edit_PackageManager() => new PackageManagerWindow();
    [MenuItem("Windows/Scene View")] public static void Window_SceneView() => new SceneViewWindow();
    [MenuItem("Windows/Game View")] public static void Window_GameView() => new GameWindow();
    [MenuItem("Windows/Hierarchy")] public static void Window_Hierarchy() => new HierarchyWindow();
    [MenuItem("Windows/Inspector")] public static void Window_Inspector() => new InspectorWindow();
    [MenuItem("Windows/Assets Browser")] public static void Window_AssetsBrowser() => new AssetsBrowserWindow();
    [MenuItem("Windows/Assets Tree")] public static void Window_AssetsTree() => new AssetsTreeWindow();
    [MenuItem("Windows/Console")] public static void Window_Console() => new ConsoleWindow();
    [MenuItem("Windows/Project Settings")] public static void Window_ProjectSettings() => new ProjectSettingsWindow();
    [MenuItem("Windows/Editor Preferences")] public static void Window_Preferences() => new PreferencesWindow();
    // [MenuItem("Windows/Render Graph")] public static void Window_RenderGraph() => new RenderGraphWindow();



    #endregion



}
