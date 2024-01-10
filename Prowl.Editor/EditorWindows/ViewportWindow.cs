using HexaEngine.ImGuiNET;
using HexaEngine.ImGuizmoNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Prowl.Editor.EditorWindows;

public class ViewportWindow : EditorWindow
{
    public EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    public static Camera LastFocusedCamera;

    public static ImGuizmoOperation GizmosOperation = ImGuizmoOperation.Translate;
    public static ImGuizmoMode GizmosSpace = ImGuizmoMode.Local;

    Camera Cam;
    RenderTexture RenderTarget;
    bool IsFocused = false;
    bool IsHovered = false;
    Vector2 WindowCenter;
    Vector2 mouseUV;
    bool DrawGrid = false;
    int frames = 0;
    double fpsTimer = 0;
    double fps = 0;
    double moveSpeed = 1;

    public ViewportWindow() : base()
    {
        Title = FontAwesome6.Camera + " Viewport";

        var CamObject = GameObject.CreateSilently();
        var t = CamObject.AddComponent<Transform>();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        t.Position = new Vector3(0, 5, -10);
        Cam = CamObject.AddComponent<Camera>();
        Cam.ShowGizmos = true;
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
        frames++;
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1.0) {
            fps = frames / fpsTimer;
            frames = 0;
            fpsTimer = 0;
        }

        if (!Project.HasProject) return;

        IsFocused = ImGui.IsWindowFocused();
        IsHovered = ImGui.IsWindowHovered();

        var cStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        var renderSize = ImGui.GetContentRegionAvail();
        if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.X, (int)renderSize.Y);

        var view = Matrix4x4.CreateLookToLeftHanded(Cam.GameObject.Transform!.Position, Cam.GameObject.Transform!.Forward, Cam.GameObject.Transform!.Up).ToFloat();
        var projection = Cam.GetProjectionMatrix(renderSize.X, renderSize.Y).ToFloat();

        WindowCenter = ImGui.GetWindowPos() + new System.Numerics.Vector2(windowSize.X / 2, windowSize.Y / 2);

        // Manually Render to the RenderTexture
        Cam.NearClip = Settings.NearClip;
        Cam.FarClip = Settings.FarClip;
        Cam.Render((int)renderSize.X, (int)renderSize.Y);
        Settings.RenderResolution = Math.Clamp(Settings.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = Settings.RenderResolution;

        var imagePos = ImGui.GetCursorScreenPos();
        var imageSize = ImGui.GetContentRegionAvail();
        ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, imageSize, new Vector2(0, 1), new Vector2(1, 0));
        HandleDragnDrop();

        mouseUV = (ImGui.GetMousePos() - imagePos) / imageSize;
        // Flip Y
        mouseUV.y = 1.0 - mouseUV.y;

        if (ImGui.IsItemClicked() && !ImGuizmo.IsOver()) {
            var instanceID = Cam.gBuffer.GetObjectIDAt(mouseUV);
            if (instanceID != 0) {
                // find InstanceID Object
                var go = EngineObject.FindObjectByID<GameObject>(instanceID);
                if (go != null)
                    HierarchyWindow.SelectHandler.Select(new WeakReference(go));
            }
        }

        ImGuizmo.SetDrawlist();
        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y, windowSize.X, windowSize.Y);

#warning TODO: Camera rendering clears Gizmos untill the rendering overhaul, so gizmos will Flicker here
        Camera.Current = Cam;
        foreach (var activeGO in SceneManager.AllGameObjects)
            if (activeGO.EnabledInHierarchy)
                DrawGizmos(activeGO, view, projection, HierarchyWindow.SelectHandler.IsSelected(new WeakReference(activeGO)));
        Camera.Current = null;

        if (DrawGrid)
        {
            System.Numerics.Matrix4x4 matrix = System.Numerics.Matrix4x4.Identity;
            ImGuizmo.DrawGrid(ref view.M11, ref projection.M11, ref matrix.M11, 10);
        }

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5, 5));
        if (ImGui.Button($"{FontAwesome6.ArrowsUpDownLeftRight}")) GizmosOperation = ImGuizmoOperation.Translate;
        GUIHelper.Tooltip("Translate");
        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (27), 5));
        if (ImGui.Button($"{FontAwesome6.ArrowsSpin}")) GizmosOperation = ImGuizmoOperation.Rotate;
        GUIHelper.Tooltip("Rotate");
        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (54), 5));
        if (ImGui.Button($"{FontAwesome6.GroupArrowsRotate}")) GizmosOperation = ImGuizmoOperation.Scale;
        GUIHelper.Tooltip("Scale");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (81), 5));

        if (GizmosSpace == ImGuizmoMode.World && ImGui.Button($"{FontAwesome6.Globe}"))
            GizmosSpace = ImGuizmoMode.Local;
        else if (GizmosSpace == ImGuizmoMode.Local && ImGui.Button($"{FontAwesome6.Cube}"))
            GizmosSpace = ImGuizmoMode.World;
        GUIHelper.Tooltip(GizmosSpace.ToString());

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
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(Cam.GameObject));
        GUIHelper.Tooltip("Viewport Camera Settings");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5, 25));
        ImGui.Text("FPS: " + fps.ToString("0.00"));

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

    private void DrawGizmos(GameObject go, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, bool isSelected)
    {
        if (go.hideFlags.HasFlag(HideFlags.NoGizmos)) return;

        Transform myTransform = go.GetComponent<Transform>();
        if (isSelected && myTransform != null) {
            var goMatrix = myTransform.Local;

            // Perform ImGuizmo manipulation
            var fmat = goMatrix.ToFloat();
            if (ImGuizmo.Manipulate(ref view, ref projection, GizmosOperation, GizmosSpace, ref fmat)) {
                goMatrix = fmat.ToDouble();
                myTransform.Local = goMatrix;
            }
        }

        foreach (var component in go.GetComponents()) {
            component.CallDrawGizmos();
            if (isSelected) component.CallDrawGizmosSelected();
        }
    }

    private void HandleDragnDrop()
    {
        // GameObject from Assets
        if (DragnDrop.ReceiveAsset<GameObject>(out var original)) {
            GameObject clone = (GameObject)EngineObject.Instantiate(original.Res!, true);
            clone.AssetID = Guid.Empty; // Remove AssetID so it's not a Prefab - These are just GameObjects like Models
            var t = clone.Transform;
            if (t != null) {
                t.Position = Cam.GameObject.Transform!.GlobalPosition + Cam.GameObject.Transform!.Forward * 10;
                t.Recalculate();
            }
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(clone));
        }
        // Prefab from Assets
        if (DragnDrop.ReceiveAsset<Prefab>(out var prefab)) {
            var go = prefab.Res.Instantiate();
            var t = go.Transform;
            if (t != null) {
                t.Position = Cam.GameObject.Transform!.GlobalPosition + Cam.GameObject.Transform!.Forward * 10;
                t.Recalculate();
            }
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        // Scene from Assets
        if (DragnDrop.ReceiveAsset<Scene>(out var scene))
            SceneManager.LoadScene(scene);
    }

    protected override void Update()
    {
        if (!IsHovered) return;

        LastFocusedCamera = Cam;

        if (Input.GetMouseButton(1)) {
            ImGui.FocusWindow(ImGUIWindow, ImGuiFocusRequestFlags.None);

            Vector3 moveDir = Vector3.zero;
            if (Input.GetKey(Key.W)) moveDir += Cam.GameObject.Transform!.Forward;
            if (Input.GetKey(Key.S)) moveDir -= Cam.GameObject.Transform!.Forward;
            if (Input.GetKey(Key.A)) moveDir -= Cam.GameObject.Transform!.Right;
            if (Input.GetKey(Key.D)) moveDir += Cam.GameObject.Transform!.Right;
            if (Input.GetKey(Key.E)) moveDir += Cam.GameObject.Transform!.Up;
            if (Input.GetKey(Key.Q)) moveDir -= Cam.GameObject.Transform!.Up;

            if (moveDir != Vector3.zero) {
                moveDir = Vector3.Normalize(moveDir);
                if (Input.GetKey(Key.ShiftLeft))
                    moveDir *= 2.0f;
                Cam.GameObject.Transform!.Position += moveDir * (Time.deltaTimeF * 10f) * moveSpeed;

                // Get Exponentially faster
                moveSpeed += Time.deltaTimeF * 0.0001;
                moveSpeed *= 1.0025;
                moveSpeed = Math.Clamp(moveSpeed, 1, 1000);
            } else {
                moveSpeed = 1;
            }

            // Version with fixed gimbal lock
            var mouseDelta = Input.MouseDelta;
            var rot = Cam.GameObject.Transform!.Rotation;
            rot.x += mouseDelta.X * (Time.deltaTimeF * 5f * Settings.LookSensitivity);
            rot.y += mouseDelta.Y * (Time.deltaTimeF * 5f * Settings.LookSensitivity);
            Cam.GameObject.Transform!.Rotation = rot;
             
            Input.MousePosition = WindowCenter.ToFloat().ToGeneric();
        } else {
            moveSpeed = 1;
            if (Input.GetMouseButton(2)) {

                var mouseDelta = Input.MouseDelta;
                var pos = Cam.GameObject.Transform!.Position;
                pos -= Cam.GameObject.Transform!.Right * mouseDelta.X * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
                pos += Cam.GameObject.Transform!.Up * mouseDelta.Y * (Time.deltaTimeF * 1f * Settings.PanSensitivity);
                Cam.GameObject.Transform!.Position = pos;

            } else if (Input.MouseWheelDelta != 0) {

                Matrix4x4.Invert(Cam.View, out var viewInv);
                var dir = Vector3.Transform(Cam.gBuffer.GetViewPositionAt(mouseUV), viewInv);
                // Larger distance more zoom, but clamped
                double amount = dir.magnitude * 0.05 * Settings.ZoomSensitivity;
                if (amount > dir.magnitude * .9) amount = dir.magnitude * .9;
                if (amount < Cam.NearClip * 2) amount = Cam.NearClip * 2;

                if (dir.sqrMagnitude > 0)
                    Cam.GameObject.Transform!.GlobalPosition += Vector3.Normalize(dir) * amount * Input.MouseWheelDelta;
                else
                    Cam.GameObject.Transform!.GlobalPosition += Cam.GameObject.Transform!.Forward * 1f * Input.MouseWheelDelta;

            } else if (IsFocused) {

                // If not looking around Viewport Keybinds are used instead
                if      (Input.GetKeyDown(Key.Q)) GizmosOperation = ImGuizmoOperation.Translate;
                else if (Input.GetKeyDown(Key.W)) GizmosOperation = ImGuizmoOperation.Rotate;
                else if (Input.GetKeyDown(Key.E)) GizmosOperation = ImGuizmoOperation.Scale;
                else if (Input.GetKeyDown(Key.R)) GizmosOperation = ImGuizmoOperation.Universal;

            }
        }
    }

}
