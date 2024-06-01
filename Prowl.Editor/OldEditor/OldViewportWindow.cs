using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Rendering.OpenGL;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;
using System.Runtime.CompilerServices;

namespace Prowl.Editor.EditorWindows;

public class OldViewportWindow : OldEditorWindow
{
    public static Camera LastFocusedCamera;

    public static ImGuizmoOperation GizmosOperation = ImGuizmoOperation.Translate;
    public static ImGuizmoMode GizmosSpace = ImGuizmoMode.Local;


    Camera Cam;
    Material gridMat;
    RenderTexture RenderTarget;
    bool IsFocused = false;
    bool IsHovered = false;
    Vector2 WindowCenter;
    Vector2 mouseUV;
    int frames = 0;
    double fpsTimer = 0;
    double fps = 0;
    double moveSpeed = 1;
    bool hasStarted = false;

    enum GridType { None, XZ, XY, YZ }
    GridType gridType = GridType.XZ;

    public OldViewportWindow() : base()
    {
        Title = FontAwesome6.Camera + " Viewport";

        var CamObject = GameObject.CreateSilently();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        CamObject.Transform.position = new Vector3(0, 5, -10);
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

        if (!hasStarted) {
            hasStarted = true;
            ImGui.SetWindowFocus();
        }

        IsFocused = ImGui.IsWindowFocused();
        IsHovered = ImGui.IsWindowHovered();

        var cStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        var renderSize = ImGui.GetContentRegionAvail();
        if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.X, (int)renderSize.Y);

        var view = Matrix4x4.CreateLookToLeftHanded(Cam.GameObject.Transform.position, Cam.GameObject.Transform.forward, Cam.GameObject.Transform.up).ToFloat();
        var projection = Cam.GetProjectionMatrix(renderSize.X, renderSize.Y).ToFloat();

        WindowCenter = ImGui.GetWindowPos() + new System.Numerics.Vector2(windowSize.X / 2, windowSize.Y / 2);

        // Manually Render to the RenderTexture
        Cam.NearClip = SceneViewPreferences.Instance.NearClip;
        Cam.FarClip = SceneViewPreferences.Instance.FarClip;
        Cam.Render((int)renderSize.X, (int)renderSize.Y);
        SceneViewPreferences.Instance.RenderResolution = Math.Clamp(SceneViewPreferences.Instance.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = SceneViewPreferences.Instance.RenderResolution;

        var imagePos = ImGui.GetCursorScreenPos();
        var imageSize = ImGui.GetContentRegionAvail();
        ImGui.Image((IntPtr)(RenderTarget.InternalTextures[0].Handle as GLTexture)!.Handle, imageSize, new Vector2(0, 1), new Vector2(1, 0));
        HandleDragnDrop();

        mouseUV = (ImGui.GetMousePos() - imagePos) / imageSize;
        // Flip Y
        mouseUV.y = 1.0 - mouseUV.y;

        if (ImGui.IsItemClicked() && !ImGuizmo.IsOver()) {
            // If the Scene Camera has no Render Graph, the gBuffer may not be initialized
            if (Cam.gBuffer != null)
            {
                var instanceID = Cam.gBuffer.GetObjectIDAt(mouseUV);
                if (instanceID != 0)
                {
                    // find InstanceID Object
                    var go = EngineObject.FindObjectByID<GameObject>(instanceID);
                    if (go != null)
                    {
                        if (!go.IsPartOfPrefab || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            OldHierarchyWindow.SelectHandler.Select(new WeakReference(go));
                            OldHierarchyWindow.Ping(go);
                        }
                        else
                        {
                            // Find Prefab go.IsPrefab
                            var prefab = go.Transform;
                            while (prefab.parent != null)
                            {
                                prefab = prefab.parent;
                                if (prefab.gameObject.IsPrefab)
                                    break;
                            }

                            OldHierarchyWindow.SelectHandler.Select(new WeakReference(prefab.gameObject));
                            OldHierarchyWindow.Ping(prefab.gameObject);
                        }
                    }
                }
                else
                {
                    OldHierarchyWindow.SelectHandler.Clear();
                }
            }
        }

        ImGuizmo.SetDrawlist();
        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y, windowSize.X, windowSize.Y);

#warning TODO: Camera rendering clears Gizmos untill the rendering overhaul, so gizmos will Flicker here
        Camera.Current = Cam;
        foreach (var activeGO in SceneManager.AllGameObjects)
            if (activeGO.enabledInHierarchy)
            {
                if (activeGO.hideFlags.HasFlag(HideFlags.NoGizmos)) continue;

                foreach (var component in activeGO.GetComponents())
                {
                    component.DrawGizmos();
                    if (OldHierarchyWindow.SelectHandler.IsSelected(new WeakReference(activeGO)))
                        component.DrawGizmosSelected();
                }
            }

        var selectedWeaks = OldHierarchyWindow.SelectHandler.Selected;
        var selectedGOs = new List<GameObject>();
        foreach (var weak in selectedWeaks)
            if (weak.Target is GameObject go)
                selectedGOs.Add(go);

        if (gridType != GridType.None) {
            gridMat ??= new Material(Shader.Find("Defaults/Grid.shader"));
            gridMat.SetTexture("gPositionRoughness", Cam.gBuffer.PositionRoughness);
            gridMat.SetKeyword("GRID_XZ", gridType == GridType.XZ);
            gridMat.SetKeyword("GRID_XY", gridType == GridType.XY);
            gridMat.SetKeyword("GRID_YZ", gridType == GridType.YZ);
            Graphics.Blit(RenderTarget, gridMat, 0, false);
        }

        DrawGizmos(selectedGOs, view, projection);

        Camera.Current = null;

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

        if      (GizmosSpace == ImGuizmoMode.World && ImGui.Button($"{FontAwesome6.Globe}"))
            GizmosSpace = ImGuizmoMode.Local;
        else if (GizmosSpace == ImGuizmoMode.Local && ImGui.Button($"{FontAwesome6.Cube}"))
            GizmosSpace = ImGuizmoMode.World;
        GUIHelper.Tooltip(GizmosSpace.ToString());

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (115), 5));
        ImGui.SetNextItemWidth(23);
        GUIHelper.EnumComboBox("##DebugDraw", $"{FontAwesome6.Eye}", ref Cam.debugDraw, false);
        GUIHelper.Tooltip("Debug Visualization: " + Cam.debugDraw.ToString());


        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (142), 5));
        if (ImGui.Button($"{FontAwesome6.Camera}"))
            OldHierarchyWindow.SelectHandler.SetSelection(new WeakReference(Cam.GameObject));
        GUIHelper.Tooltip("Viewport Camera Settings");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5 + (174), 5));
        ImGui.SetNextItemWidth(23);
        GUIHelper.EnumComboBox("##GridType", $"{FontAwesome6.TableCells}", ref gridType, false);
        GUIHelper.Tooltip("Grid Type");

        ImGui.SetCursorPos(cStart + new System.Numerics.Vector2(5, 25));
        ImGui.Text("FPS: " + fps.ToString("0.00"));

        ImGuizmo.ViewManipulate(ref view, 1, new Vector2(ImGui.GetWindowPos().X + windowSize.X - 75, ImGui.GetWindowPos().Y + 15 + 75), new Vector2(75, -75), 0x10101010);
        System.Numerics.Matrix4x4.Invert(view, out var iview);
        System.Numerics.Matrix4x4.Decompose(iview, out var scale, out var rot, out var pos);
        //Cam.GameObject.Local = iview.ToDouble();
        Cam.GameObject.Transform.localPosition = pos;
        Cam.GameObject.Transform.localRotation = rot;
        //Cam.GameObject.transform.localScale = scale;
    }

    private void DrawGizmos(IEnumerable<GameObject> selectedGOs, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection)
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        Dictionary<GameObject, (Vector3 Position, Vector3 Scale)> originalTransforms = new Dictionary<GameObject, (Vector3 Position, Vector3 Scale)>();
        List<GameObject> gameObjects = new List<GameObject>(selectedGOs);

        // Calculate the center point and store original positions
        foreach (var go in selectedGOs)
        {
            if (go.hideFlags.HasFlag(HideFlags.NoGizmos)) continue;

            center += go.Transform.position;
            originalTransforms[go] = (go.Transform.position, go.Transform.localScale);
            count++;
        }

        // Remove any gameobjects who's parents are also selected
        foreach (var go in selectedGOs)
        {
            if (go.hideFlags.HasFlag(HideFlags.NoGizmos)) continue;

            if (selectedGOs.Any(x => go.IsChildOf(x)))
            {
                gameObjects.Remove(go);
                originalTransforms.Remove(go);
                count--;
            }
        }

        if (count == 0)
        {
            var infinity = Matrix4x4.CreateTranslation(Vector3.infinity).ToFloat();
            unsafe
            {
                ImGuizmo.Manipulate(ref view, ref projection, GizmosOperation, GizmosSpace, ref infinity, null, null, null, null);
            }
            return;
        }
        center /= count;

        var centerMatrix = Matrix4x4.CreateTranslation(center);
        if(count == 1)
        {
            // Only 1 thing use its full transform
            var go = gameObjects.First();
            centerMatrix = Matrix4x4.CreateScale(go.Transform.localScale) * Matrix4x4.CreateFromQuaternion(go.Transform.rotation) * Matrix4x4.CreateTranslation(go.Transform.position);
        }


        unsafe
        {
            float* snap = null;
            float* localBound = null;
            if (ImGui.GetIO().KeyCtrl)
            {
                float[] snaps = { 1, 1, 1 };
                snap = (float*)Unsafe.AsPointer(ref snaps[0]);
            }

            var fmat = centerMatrix.ToFloat();
            if (ImGuizmo.Manipulate(ref view, ref projection, GizmosOperation, GizmosSpace, ref fmat, null, snap, localBound, snap))
            {
                centerMatrix = fmat.ToDouble();
                Matrix4x4.Decompose(centerMatrix, out var scale, out var rot, out var pos);

                if (count == 1)
                {
                    // Only 1 thing use its full transform
                    var go = gameObjects.First();
                    go.Transform.position = pos;
                    go.Transform.rotation = rot;
                    go.Transform.localScale = scale;
                }
                else
                {
                    // Apply transformations to all GameObjects
                    foreach (var go in gameObjects)
                    {
                        if (go.hideFlags.HasFlag(HideFlags.NoGizmos)) continue;

                        var original = originalTransforms[go];

                        // Calculate new position considering the offset and the transformation
                        Vector3 offset = original.Position - center;
                        offset = Vector3.Transform(offset, rot); // Apply rotation to the offset
                        go.Transform.position = pos + offset * scale; // New position based on central transformation and scaled offset

                        go.Transform.rotation *= rot; // Additive rotation
                        go.Transform.localScale = original.Scale * scale; // Apply scale relative to original scale
                    }
                }
            }
        }
    }

    private void HandleDragnDrop()
    {
        if (DragnDrop.Drop<GameObject>(out var original))
        {
            if (original.AssetID == Guid.Empty) return;

            GameObject go = (GameObject)EngineObject.Instantiate(original, true);
            if (go != null)
            {
                go.Transform.position = Cam.GameObject.Transform.position + Cam.GameObject.Transform.forward * 10;
            }
            OldHierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Prefab>(out var prefab))
        {
            var go = prefab.Instantiate();
            var t = go;
            if (t != null)
            {
                t.Transform.position = Cam.GameObject.Transform.position + Cam.GameObject.Transform.forward * 10;
            }
            OldHierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Scene>(out var scene))
        {
            SceneManager.LoadScene(scene);
        }
    }

    protected override void Update()
    {
        if (!IsHovered) return;

        LastFocusedCamera = Cam;

        if (Input.GetMouseButton(1)) {
            ImGui.FocusWindow(ImGUIWindow, ImGuiFocusRequestFlags.None);
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            Vector3 moveDir = Vector3.zero;
            if (Input.GetKey(Key.W)) moveDir += Cam.GameObject.Transform.forward;
            if (Input.GetKey(Key.S)) moveDir -= Cam.GameObject.Transform.forward;
            if (Input.GetKey(Key.A)) moveDir -= Cam.GameObject.Transform.right;
            if (Input.GetKey(Key.D)) moveDir += Cam.GameObject.Transform.right;
            if (Input.GetKey(Key.E)) moveDir += Cam.GameObject.Transform.up;
            if (Input.GetKey(Key.Q)) moveDir -= Cam.GameObject.Transform.up;

            if (moveDir != Vector3.zero) {
                moveDir = Vector3.Normalize(moveDir);
                if (Input.GetKey(Key.ShiftLeft))
                    moveDir *= 2.0f;
                Cam.GameObject.Transform.position += moveDir * (Time.deltaTimeF * 10f) * moveSpeed;

                // Get Exponentially faster
                moveSpeed += Time.deltaTimeF * 0.0001;
                moveSpeed *= 1.0025;
                moveSpeed = Math.Clamp(moveSpeed, 1, 1000);
            } else {
                moveSpeed = 1;
            }

            // Version with fixed gimbal lock
            var mouseDelta = Input.MouseDelta;
            var rot = Cam.GameObject.Transform.eulerAngles;
            rot.y += mouseDelta.x * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
            rot.x += mouseDelta.y * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
            Cam.GameObject.Transform.eulerAngles = rot;

            Input.MousePosition = WindowCenter;
        } else {
            moveSpeed = 1;
            if (Input.GetMouseButton(2)) {

                var mouseDelta = Input.MouseDelta;
                var pos = Cam.GameObject.Transform.position;
                pos -= Cam.GameObject.Transform.right * mouseDelta.x * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                pos += Cam.GameObject.Transform.up * mouseDelta.y * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                Cam.GameObject.Transform.position = pos;

            } else if (Input.MouseWheelDelta != 0) {

                Matrix4x4.Invert(Cam.View, out var viewInv);
                var dir = Vector3.Transform(Cam.gBuffer.GetViewPositionAt(mouseUV), viewInv);
                // Larger distance more zoom, but clamped
                double amount = dir.magnitude * 0.05 * SceneViewPreferences.Instance.ZoomSensitivity;
                if (amount > dir.magnitude * .9) amount = dir.magnitude * .9;
                if (amount < Cam.NearClip * 2) amount = Cam.NearClip * 2;

                if (dir.sqrMagnitude > 0)
                    Cam.GameObject.Transform.position += Vector3.Normalize(dir) * amount * Input.MouseWheelDelta;
                else
                    Cam.GameObject.Transform.position += Cam.GameObject.Transform.forward * 1f * Input.MouseWheelDelta;

            } else if (IsFocused) {

                // If not looking around Viewport Keybinds are used instead
                if      (Input.GetKeyDown(Key.Q)) GizmosOperation = ImGuizmoOperation.Translate;
                else if (Input.GetKeyDown(Key.W)) GizmosOperation = ImGuizmoOperation.Rotate;
                else if (Input.GetKeyDown(Key.E)) GizmosOperation = ImGuizmoOperation.Scale;
                else if (Input.GetKeyDown(Key.R)) GizmosOperation = ImGuizmoOperation.Universal;
                else if (Input.GetKeyDown(Key.T)) GizmosOperation = ImGuizmoOperation.Bounds;

            }

            if (Input.GetKeyDown(Key.F) && OldHierarchyWindow.SelectHandler.Selected.Any())
            {
                float defaultZoomFactor = 2f;
                if (OldHierarchyWindow.SelectHandler.Selected.Count == 1)
                {
                    // If only one object is selected, set the camera position to the center of that object
                    if (OldHierarchyWindow.SelectHandler.Selected.First().Target is GameObject singleObject)
                    {
                        Cam.GameObject.Transform.position = singleObject.Transform.position - (Cam.GameObject.Transform.forward * defaultZoomFactor);
                        return;
                    }
                }

                // Calculate the bounding box based on the positions of selected objects
                Bounds combinedBounds = new Bounds();
                foreach (var obj in OldHierarchyWindow.SelectHandler.Selected)
                {
                    if (obj.Target is GameObject go)
                    {
                        combinedBounds.Encapsulate(go.Transform.position);
                    }
                }

                // Calculate the zoom factor based on the size of the bounding box
                float boundingBoxSize = (float)Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                float zoomFactor = boundingBoxSize * defaultZoomFactor;

                Vector3 averagePosition = combinedBounds.center;
                Cam.GameObject.Transform.position = averagePosition - (Cam.GameObject.Transform.forward * zoomFactor);
            }

        }
    }

}
