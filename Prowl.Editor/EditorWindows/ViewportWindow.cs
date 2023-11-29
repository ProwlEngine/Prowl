using HexaEngine.ImGuiNET;
using HexaEngine.ImGuizmoNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Components;
using Prowl.Runtime.Components.ImageEffects;
using Prowl.Runtime.Resources;
using System.Diagnostics;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;

namespace Prowl.Editor.EditorWindows;

public class ViewportWindow : EditorWindow
{

    public class ViewportSettings : IProjectSetting
    {
        [Text("Settings for all Viewports.")]
        [Space]
        [Text("Controls:")]
        public float LookSensitivity = 1f;
        public float PanSensitivity = 1f;
        [Space]
        [Text("Rendering Settings:")]
        [Seperator]
        public float NearClip = 0.02f;
        public float FarClip = 10000f;
        [Space]
        public float RenderResolution = 1f;
    }

    public ViewportSettings Settings => Project.ProjectSettings.GetSetting<ViewportSettings>();

    Camera Cam;
    RenderTexture RenderTarget;
    bool IsFocused = false;
    Vector2 WindowCenter;
    bool DrawGrid = false;

    private string[] operationNames = Enum.GetNames<ImGuizmoOperation>();
    private ImGuizmoOperation[] operations = Enum.GetValues<ImGuizmoOperation>();

    private string[] modeNames = Enum.GetNames<ImGuizmoMode>();
    private ImGuizmoMode[] modes = Enum.GetValues<ImGuizmoMode>();

    private ImGuizmoOperation operation = ImGuizmoOperation.Universal;
    private ImGuizmoMode mode = ImGuizmoMode.Local;

    public ViewportWindow()
    {
        Title = "Viewport";

        var CamObject = GameObject.CreateSilently();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave;
        CamObject.Position = new Vector3(0, 5, -10);
        Cam = CamObject.AddComponent<Camera>();
        var dof = CamObject.AddComponent<DOFEffect>();
        dof.OnEnable();

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

    ImGuizmoOperation manipulateOp = ImGuizmoOperation.Translate;

    
    private void DrawViewport()
    {
        if (!Project.HasProject) return;

        IsFocused = ImGui.IsWindowFocused();

        var cursorStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X != RenderTarget.Width || windowSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)windowSize.X, (int)windowSize.Y);

        WindowCenter = ImGui.GetWindowPos() + new Vector2(windowSize.X / 2, windowSize.Y / 2);

        // Manually Render to the RenderTexture
        Cam.NearClip = Settings.NearClip;
        Cam.FarClip = Settings.FarClip;
        Settings.RenderResolution = Math.Clamp(Settings.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = Settings.RenderResolution;

        ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].id, ImGui.GetContentRegionAvail(), new Vector2(0, 1), new Vector2(1, 0));
        ImGuizmo.SetDrawlist();
        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y, windowSize.X, windowSize.Y);

        Matrix4x4 mvp = Cam.GameObject.View;
        mvp *= Cam.GetProjectionMatrix(windowSize.X, windowSize.Y);
        var drawList = ImGui.GetWindowDrawList();
        Runtime.Gizmos.Render(drawList, mvp);

        Prowl.Runtime.Gizmos.Clear();

        var view = Cam.GameObject.View;
        var projectionM11 = Cam.GetProjectionMatrix(windowSize.X, windowSize.Y);

        if (DrawGrid)
        {
            Matrix4x4 matrix = Matrix4x4.Identity;
            ImGuizmo.DrawGrid(ref view.M11, ref projectionM11.M11, ref matrix.M11, 10);
        }


        if (Selection.Current != null && Selection.Current is GameObject go)
        {
            var goMatrix = go.Local;
            if (ImGuizmo.Manipulate(ref view.M11, ref projectionM11.M11, manipulateOp, ImGuizmoMode.Local, ref goMatrix.M11))
            {
                go.Local = goMatrix;
            }
        }

        // Set Cursor Pos back to the start so that the Gizmos exist relative to the Viewport
        ImGui.SetCursorPos(cursorStart + new Vector2(2));

        // TODO: Custom Gizmos, Allow for custom Gizmos to be drawn by components hooking into the RenderCustomGizmos action
        // This event could also be used to draw UI elements in the viewport

        // Draw Tooltip
        int X = 0;
        if (ImGui.Button($"{FontAwesome6.ArrowsUpDownLeftRight}"))
            manipulateOp = ImGuizmoOperation.Translate;
        X += 23; ImGui.SameLine(X);
        if (ImGui.Button($"{FontAwesome6.ArrowsSpin}"))
            manipulateOp = ImGuizmoOperation.Rotate;
        X += 21; ImGui.SameLine(X);
        if (ImGui.Button($"{FontAwesome6.GroupArrowsRotate}"))
            manipulateOp = ImGuizmoOperation.Scale;
        X += 23; ImGui.SameLine(X);
        ImGui.Text("FPS: " + (1.0f / (float)Time.deltaTimeF).ToString("0.00"));
        X += 58; ImGui.SameLine(X);

        ImGui.SetNextItemWidth(85);
        int modeIndex = Array.IndexOf(modes, mode);
        if (ImGui.Combo("##Mode", ref modeIndex, modeNames, modeNames.Length))
            mode = modes[modeIndex];
        X += 88; ImGui.SameLine(X);

        ImGui.SetNextItemWidth(85);
        // Dropdown to pick Camera DebugDraw mode
        if (ImGui.BeginCombo($"##DebugDraw", $"{FontAwesome6.Eye + Cam.debugDraw.ToString()}"))
        {
            if (ImGui.Selectable($"Off", Cam.debugDraw == Camera.DebugDraw.Off))
                Cam.debugDraw = Camera.DebugDraw.Off;
            if (ImGui.Selectable($"Diffuse", Cam.debugDraw == Camera.DebugDraw.Diffuse))
                Cam.debugDraw = Camera.DebugDraw.Diffuse;
            if (ImGui.Selectable($"Normals", Cam.debugDraw == Camera.DebugDraw.Normals))
                Cam.debugDraw = Camera.DebugDraw.Normals;
            if (ImGui.Selectable($"Depth", Cam.debugDraw == Camera.DebugDraw.Depth))
                Cam.debugDraw = Camera.DebugDraw.Depth;
            if (ImGui.Selectable($"Lighting", Cam.debugDraw == Camera.DebugDraw.Lighting))
                Cam.debugDraw = Camera.DebugDraw.Lighting;
            if (ImGui.Selectable($"Velocity", Cam.debugDraw == Camera.DebugDraw.Velocity))
                Cam.debugDraw = Camera.DebugDraw.Velocity;
            ImGui.EndCombo();
        }
        X += 88; ImGui.SameLine(X);
        if (ImGui.Button($"{FontAwesome6.TableCells}"))
            DrawGrid = !DrawGrid;


        // Show ViewManipulation at the end
        view = Matrix4x4.Transpose(view);
        ImGuizmo.ViewManipulate(ref view.M11, 10, new Vector2(ImGui.GetWindowPos().X + windowSize.X - 75, ImGui.GetWindowPos().Y + 15), new Vector2(75, 75), 0x10101010);
        // TODO: Allow Setting the View Matrix
    }

    protected override void Update()
    {
        if (!IsFocused) return;

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
        else if (Input.IsMouseButtonDown(Raylib_cs.MouseButton.MOUSE_MIDDLE_BUTTON))
        {
            var mouseDelta = Input.MouseDelta;
            var pos = Cam.GameObject.Position;
            pos += Cam.GameObject.Right * mouseDelta.X * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
            pos += Cam.GameObject.Up * mouseDelta.Y * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
            Cam.GameObject.Position = pos;
        }
        else
        {
            // If not looking around Viewport Keybinds are used instead
            if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_Q))
            {
                manipulateOp = ImGuizmoOperation.Translate;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_W))
            {
                manipulateOp = ImGuizmoOperation.Rotate;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_E))
            {
                manipulateOp = ImGuizmoOperation.Scale;
            }
            else if (Input.IsKeyPressed(Raylib_cs.KeyboardKey.KEY_R))
            {
                manipulateOp = ImGuizmoOperation.Universal;
            }
        }
    }

}