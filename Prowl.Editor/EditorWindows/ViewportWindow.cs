using HexaEngine.ImGuiNET;
using HexaEngine.ImGuizmoNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.ImGUI.Widgets;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor.EditorWindows;

public class ViewportWindow : EditorWindow
{
    public EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    public static Camera LastFocusedCamera;

    Camera Cam;
    RenderTexture RenderTarget;
    bool IsFocused = false;
    bool IsHovered = false;
    Vector2 WindowCenter;
    bool DrawGrid = false;

    public ViewportWindow() : base()
    {
        Title = FontAwesome6.Camera + " Viewport";

        var CamObject = GameObject.CreateSilently();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        CamObject.Position = new Vector3(0, 5, -10);
        Cam = CamObject.AddComponent<Camera>();
        LastFocusedCamera = Cam;

        RefreshRenderTexture(Width, Height);
    }

    public void RefreshRenderTexture(int width, int height)
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(width, height);
        Cam.Target = RenderTarget;
    }

    protected override void PreWindowDraw() =>
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

    protected override void PostWindowDraw() =>
        ImGui.PopStyleVar(1);

    protected override void Draw()
    {
        DrawViewport();
    }

    private void DrawViewport()
    {
        if (!Project.HasProject) return;

        IsFocused = ImGui.IsWindowFocused();
        IsHovered = ImGui.IsWindowHovered();

        var cStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X != RenderTarget.Width || windowSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)windowSize.X, (int)windowSize.Y);

        WindowCenter = ImGui.GetWindowPos() + new System.Numerics.Vector2(windowSize.X / 2, windowSize.Y / 2);

        // Manually Render to the RenderTexture
        Cam.NearClip = Settings.NearClip;
        Cam.FarClip = Settings.FarClip;
        Cam.Render((int)windowSize.X, (int)windowSize.Y);
        Settings.RenderResolution = Math.Clamp(Settings.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = Settings.RenderResolution;

        ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].id, ImGui.GetContentRegionAvail(), new Vector2(0, 1), new Vector2(1, 0));
        HandleDragnDrop();
        ImGuizmo.SetDrawlist();
        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y, windowSize.X, windowSize.Y);

        var view = Matrix4x4.CreateLookToLeftHanded(Cam.GameObject.Position, Cam.GameObject.Forward, Cam.GameObject.Up).ToFloat();
        var projection = Cam.GetProjectionMatrix(windowSize.X, windowSize.Y).ToFloat();

        if (DrawGrid)
        {
            System.Numerics.Matrix4x4 matrix = System.Numerics.Matrix4x4.Identity;
            ImGuizmo.DrawGrid(ref view.M11, ref projection.M11, ref matrix.M11, 10);
        }

        Camera.Current = Cam;
        var mvp = view.ToDouble();
        mvp *= Cam.GetProjectionMatrix(windowSize.X, windowSize.Y);
        var drawList = ImGui.GetWindowDrawList();
        Runtime.Gizmos.Render(drawList, mvp);

        Prowl.Runtime.Gizmos.Clear();

        view.Translation = new System.Numerics.Vector3(0, 0, 0);
        foreach (var activeGO in SceneManager.AllGameObjects)
            if (activeGO.EnabledInHierarchy)
                activeGO.DrawGizmos(view, projection, Selection.IsSelected(activeGO));
        Camera.Current = null;

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5, 5));
        if (ImGui.Button($"{FontAwesome6.ArrowsUpDownLeftRight}")) SceneManager.GizmosOperation = ImGuizmoOperation.Translate;
        GUIHelper.Tooltip("Translate");
        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (27), 5));
        if (ImGui.Button($"{FontAwesome6.ArrowsSpin}")) SceneManager.GizmosOperation = ImGuizmoOperation.Rotate;
        GUIHelper.Tooltip("Rotate");
        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (54), 5));
        if (ImGui.Button($"{FontAwesome6.GroupArrowsRotate}")) SceneManager.GizmosOperation = ImGuizmoOperation.Scale;
        GUIHelper.Tooltip("Scale");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (81), 5));

        if (SceneManager.GizmosSpace == ImGuizmoMode.World && ImGui.Button($"{FontAwesome6.Globe}"))
            SceneManager.GizmosSpace = ImGuizmoMode.Local;
        else if (SceneManager.GizmosSpace == ImGuizmoMode.Local && ImGui.Button($"{FontAwesome6.Cube}"))
            SceneManager.GizmosSpace = ImGuizmoMode.World;
        GUIHelper.Tooltip(SceneManager.GizmosSpace.ToString());

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (115), 5));
        ImGui.SetNextItemWidth(20);
        // Dropdown to pick Camera DebugDraw mode
        if (ImGui.BeginCombo($"##DebugDraw", $"{FontAwesome6.Eye}", ImGuiComboFlags.NoArrowButton))
        {
            if (ImGui.Selectable($"Off", Cam.debugDraw == Camera.DebugDraw.Off))
                Cam.debugDraw = Camera.DebugDraw.Off;
            if (ImGui.Selectable($"Diffuse", Cam.debugDraw == Camera.DebugDraw.Albedo))
                Cam.debugDraw = Camera.DebugDraw.Albedo;
            if (ImGui.Selectable($"Normals", Cam.debugDraw == Camera.DebugDraw.Normals))
                Cam.debugDraw = Camera.DebugDraw.Normals;
            if (ImGui.Selectable($"Depth", Cam.debugDraw == Camera.DebugDraw.Depth))
                Cam.debugDraw = Camera.DebugDraw.Depth;
            if (ImGui.Selectable($"Velocity", Cam.debugDraw == Camera.DebugDraw.Velocity))
                Cam.debugDraw = Camera.DebugDraw.Velocity;
            ImGui.EndCombo();
        }
        GUIHelper.Tooltip("Debug Visualization: " + Cam.debugDraw.ToString());

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (140), 5));
        if (ImGui.Button($"{FontAwesome6.TableCells}"))
            DrawGrid = !DrawGrid;
        GUIHelper.Tooltip("Show Grid: " + DrawGrid.ToString());

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (172), 5));
        if (ImGui.Button($"{FontAwesome6.Camera}"))
            Selection.Select(Cam.GameObject, false);
        GUIHelper.Tooltip("Viewport Camera Settings");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5, 25));
        ImGui.Text("FPS: " + Raylib_cs.Raylib.GetFPS());

        // Show ViewManipulation at the end
        //view *= Matrix4x4.CreateScale(1, -1, 1);
        unsafe
        {
            ImGuizmo.ViewManipulate(ref view, 2, new Vector2(ImGui.GetWindowPos().X + windowSize.X - 75, ImGui.GetWindowPos().Y + 15), new Vector2(75, 75), 0x10101010);
            //view *= Matrix4x4.CreateScale(1, -1, 1); // invert back
            Matrix4x4.Invert(view.ToDouble(), out var iview);
            //Cam.GameObject.Local = iview;
        }
    }

    private void HandleDragnDrop()
    {
        // GameObject from Assets
        if (DragnDrop.ReceiveAsset<GameObject>(out var original))
        {
            GameObject clone = (GameObject)EngineObject.Instantiate(original.Res!, true);
            clone.AssetID = Guid.Empty; // Remove AssetID so it's not a Prefab - These are just GameObjects like Models
            clone.Position = Cam.GameObject.GlobalPosition + Cam.GameObject.Forward * 10;
            clone.Orientation = original.Res!.Orientation;
            clone.Recalculate();
            Selection.Select(clone);
        }
        // Prefab from Assets
        if (DragnDrop.ReceiveAsset<Prefab>(out var prefab))
        {
            var go = prefab.Res.Instantiate();
            go.Position = Cam.GameObject.GlobalPosition + Cam.GameObject.Forward * 10;
            go.Orientation = original.Res!.Orientation;
            go.Recalculate();
            Selection.Select(go);
        }
        // Scene from Assets
        if (DragnDrop.ReceiveAsset<Scene>(out var scene))
            SceneManager.LoadScene(scene);
    }

    protected override void Update()
    {
        if (!IsFocused) return;

        LastFocusedCamera = Cam;

        if (Input.IsMouseButtonDown(Raylib_cs.MouseButton.MOUSE_RIGHT_BUTTON))
        {
            Vector3 moveDir = Vector3.Zero;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_W))
                moveDir += Cam.GameObject.Forward;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_S))
                moveDir -= Cam.GameObject.Forward;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_A))
                moveDir -= Cam.GameObject.Right;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_D))
                moveDir += Cam.GameObject.Right;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_E))
                moveDir += Cam.GameObject.Up;
            if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_Q))
                moveDir -= Cam.GameObject.Up;
            if (moveDir != Vector3.Zero)
            {
                moveDir = Vector3.Normalize(moveDir);
                if (Input.IsKeyDown(Raylib_cs.KeyboardKey.KEY_LEFT_SHIFT))
                    moveDir *= 2.0f;
                Cam.GameObject.Position += moveDir * (Time.deltaTimeF * 10f);
            }

            // Version with fixed gimbal lock
            var mouseDelta = Input.MouseDelta;
            var rot = Cam.GameObject.Rotation;
            rot.X += mouseDelta.X * (Time.deltaTimeF * 5f * Settings.LookSensitivity);
            rot.Y += mouseDelta.Y * (Time.deltaTimeF * 5f * Settings.LookSensitivity);
            Cam.GameObject.Rotation = rot;

            Raylib_cs.Raylib.SetMousePosition((int)WindowCenter.X, (int)WindowCenter.Y);
        }
        else if (Input.IsMouseButtonDown(Raylib_cs.MouseButton.MOUSE_MIDDLE_BUTTON) && IsHovered)
        {
            var mouseDelta = Input.MouseDelta;
            var pos = Cam.GameObject.Position;
            pos -= Cam.GameObject.Right * mouseDelta.X * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
            pos += Cam.GameObject.Up * mouseDelta.Y * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
            Cam.GameObject.Position = pos;
        }
        else
        {
            // If not looking around Viewport Keybinds are used instead
            if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_Q))
            {
                SceneManager.GizmosOperation = ImGuizmoOperation.Translate;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_W))
            {
                SceneManager.GizmosOperation = ImGuizmoOperation.Rotate;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_E))
            {
                SceneManager.GizmosOperation = ImGuizmoOperation.Scale;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_R))
            {
                SceneManager.GizmosOperation = ImGuizmoOperation.Universal;
            }
        }
    }

}
