using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Widgets.Gizmo;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;

namespace Prowl.Editor;

public class SceneViewWindow : EditorWindow
{
    public static Camera LastFocusedCamera;
    private static bool LastFocusedCameraChanged;

    Camera Cam;
    Material gridMat;
    RenderTexture RenderTarget;
    Vector2 WindowCenter;
    Vector2 mouseUV;
    int frames = 0;
    double fpsTimer = 0;
    double fps = 0;
    double moveSpeed = 1;
    bool hasStarted = false;
    double camX, camY;

    TransformGizmo gizmo;
    ViewManipulatorGizmo viewManipulator;

    public enum GridType { None, XZ, XY, YZ }

    public SceneViewWindow() : base()
    {
        Title = FontAwesome6.Camera + " Viewport";

        var CamObject = GameObject.CreateSilently();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        CamObject.Transform.position = new Vector3(0, 5, -10);
        Cam = CamObject.AddComponent<Camera>();
        Cam.ShowGizmos = true;
        LastFocusedCamera = Cam;

        TransformGizmoMode mode = TransformGizmoMode.TranslateX | TransformGizmoMode.TranslateY | TransformGizmoMode.TranslateZ | TransformGizmoMode.TranslateXY | TransformGizmoMode.TranslateXZ | TransformGizmoMode.TranslateYZ | TransformGizmoMode.TranslateView;
        mode |= TransformGizmoMode.RotateX | TransformGizmoMode.RotateY | TransformGizmoMode.RotateZ | TransformGizmoMode.RotateView;
        mode |= TransformGizmoMode.ScaleX | TransformGizmoMode.ScaleY | TransformGizmoMode.ScaleZ | TransformGizmoMode.ScaleUniform;

        gizmo = new TransformGizmo(EditorGuiManager.Gui, mode);
        viewManipulator = new ViewManipulatorGizmo(EditorGuiManager.Gui);
    }

    public void RefreshRenderTexture(int width, int height)
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(width, height);
        Cam.Target = RenderTarget;
    }

    protected override void Draw()
    {
        frames++;
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1.0)
        {
            fps = frames / fpsTimer;
            frames = 0;
            fpsTimer = 0;
        }

        if (!Project.HasProject) return;
        gui.CurrentNode.Padding(5);

        var renderSize = gui.CurrentNode.LayoutData.Rect.Size;
        if (renderSize.x == 0 || renderSize.y == 0) return;

        if (RenderTarget == null || (int)renderSize.x != RenderTarget.Width || (int)renderSize.y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.x, (int)renderSize.y);

        WindowCenter = gui.CurrentNode.LayoutData.Rect.Center;

        // Manually Render to the RenderTexture
        Cam.NearClip = SceneViewPreferences.Instance.NearClip;
        Cam.FarClip = SceneViewPreferences.Instance.FarClip;
        Cam.Render((int)renderSize.x, (int)renderSize.y);
        SceneViewPreferences.Instance.RenderResolution = Math.Clamp(SceneViewPreferences.Instance.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = SceneViewPreferences.Instance.RenderResolution;

        var imagePos = gui.CurrentNode.LayoutData.Rect.Position;
        var imageSize = gui.CurrentNode.LayoutData.Rect.Size;
        gui.Draw2D.DrawImage(RenderTarget.InternalTextures[0], imagePos, imageSize, Color.white);

#warning TODO: Camera rendering clears Gizmos untill the rendering overhaul, so gizmos will Flicker here
        Camera.Current = Cam;
        foreach (var activeGO in SceneManager.AllGameObjects)
            if (activeGO.enabledInHierarchy)
            {
                if (activeGO.hideFlags.HasFlag(HideFlags.NoGizmos)) continue;

                foreach (var component in activeGO.GetComponents())
                {
                    component.DrawGizmos();
                    if (HierarchyWindow.SelectHandler.IsSelected(new WeakReference(activeGO)))
                        component.DrawGizmosSelected();
                }
            }

        var selectedWeaks = HierarchyWindow.SelectHandler.Selected;
        var selectedGOs = new List<GameObject>();
        foreach (var weak in selectedWeaks)
            if (weak.Target is GameObject go)
                selectedGOs.Add(go);

        if (SceneViewPreferences.Instance.GridType != GridType.None)
        {
            gridMat ??= new Material(Shader.Find("Defaults/Grid.shader"));
            gridMat.SetTexture("gPositionRoughness", Cam.gBuffer.PositionRoughness);
            gridMat.SetKeyword("GRID_XZ", SceneViewPreferences.Instance.GridType == GridType.XZ);
            gridMat.SetKeyword("GRID_XY", SceneViewPreferences.Instance.GridType == GridType.XY);
            gridMat.SetKeyword("GRID_YZ", SceneViewPreferences.Instance.GridType == GridType.YZ);
            Graphics.Blit(RenderTarget, gridMat, 0, false);
        }

        var view = Matrix4x4.CreateLookToLeftHanded(Cam.GameObject.Transform.position, Cam.GameObject.Transform.forward, Cam.GameObject.Transform.up);
        var projection = Cam.GetProjectionMatrix((float)renderSize.x, (float)renderSize.y);

        Ray mouseRay = Cam.ScreenPointToRay(gui.PointerPos - imagePos);

        bool blockPicking = gui.IsBlockedByInteractable(gui.PointerPos);
        HandleGizmos(selectedGOs, mouseRay, view, projection, blockPicking);

        Camera.Current = null;

        Rect rect = gui.CurrentNode.LayoutData.Rect;
        rect.width = 100;
        rect.height = 100;
        rect.x = gui.CurrentNode.LayoutData.Rect.x + gui.CurrentNode.LayoutData.Rect.width - rect.width - 10;
        rect.y = gui.CurrentNode.LayoutData.Rect.y + 10;
        viewManipulator.SetRect(rect);
        viewManipulator.SetCamera(Cam.Transform.forward, Cam.Transform.up, Cam.projectionType == Camera.ProjectionType.Orthographic);

        if (viewManipulator.Update(blockPicking, out Vector3 newForward, out bool isOrtho))
        {
            //Cam.Transform.forward = newForward;
            if (newForward != Vector3.zero)
            {
                if (newForward == Vector3.up)
                    Cam.GameObject.Transform.LookAt(Cam.GameObject.Transform.position + newForward, Vector3.forward);
                else if (newForward == Vector3.down)
                    Cam.GameObject.Transform.LookAt(Cam.GameObject.Transform.position + newForward, -Vector3.forward);
                else
                    Cam.GameObject.Transform.LookAt(Cam.GameObject.Transform.position + newForward, Vector3.up);

                camX = Cam.GameObject.Transform.eulerAngles.x;
                camY = Cam.GameObject.Transform.eulerAngles.y;
            }
            Cam.projectionType = isOrtho ? Camera.ProjectionType.Orthographic : Camera.ProjectionType.Perspective;
        }


        mouseUV = (gui.PointerPos - imagePos) / imageSize;
        // Flip Y
        mouseUV.y = 1.0 - mouseUV.y;

        var viewportInteractable = gui.GetInteractable();

        HandleDragnDrop();
        gui.SetCursorVisibility(true);
        if (IsFocused && viewportInteractable.IsHovered())
        {
            if (gui.IsPointerClick(Silk.NET.Input.MouseButton.Left) && !gizmo.IsOver && !viewManipulator.IsOver)
            {
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
                            if (!go.IsPartOfPrefab || gui.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                            {
                                HierarchyWindow.SelectHandler.Select(new WeakReference(go));
                                HierarchyWindow.Ping(go);
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

                                HierarchyWindow.SelectHandler.Select(new WeakReference(prefab.gameObject));
                                HierarchyWindow.Ping(prefab.gameObject);
                            }
                        }
                    }
                    else
                    {
                        HierarchyWindow.SelectHandler.Clear();
                    }
                }
            }
            else if (gui.IsPointerDown(Silk.NET.Input.MouseButton.Right))
            {
                gui.SetCursorVisibility(false);
                Vector3 moveDir = Vector3.zero;
                if (gui.IsKeyDown(Key.W)) moveDir += Cam.GameObject.Transform.forward;
                if (gui.IsKeyDown(Key.S)) moveDir -= Cam.GameObject.Transform.forward;
                if (gui.IsKeyDown(Key.A)) moveDir -= Cam.GameObject.Transform.right;
                if (gui.IsKeyDown(Key.D)) moveDir += Cam.GameObject.Transform.right;
                if (gui.IsKeyDown(Key.E)) moveDir += Cam.GameObject.Transform.up;
                if (gui.IsKeyDown(Key.Q)) moveDir -= Cam.GameObject.Transform.up;

                if (moveDir != Vector3.zero)
                {
                    moveDir = Vector3.Normalize(moveDir);
                    if (gui.IsKeyDown(Key.ShiftLeft))
                        moveDir *= 2.0f;
                    Cam.GameObject.Transform.position += moveDir * (Time.deltaTimeF * 10f) * moveSpeed;

                    // Get Exponentially faster
                    moveSpeed += Time.deltaTimeF * 0.0001;
                    moveSpeed *= 1.0025;
                    moveSpeed = Math.Clamp(moveSpeed, 1, 1000);
                }
                else
                {
                    moveSpeed = 1;
                }

                if (gui.IsPointerMoving)
                {
                    if (LastFocusedCameraChanged)
                    {
                        camX = Cam.GameObject.Transform.eulerAngles.x;
                        camY = Cam.GameObject.Transform.eulerAngles.y;
                        LastFocusedCameraChanged = false;
                    }

                    var mouseDelta = gui.PointerDelta;
                    if (SceneViewPreferences.Instance.InvertLook)
                        mouseDelta.y = -mouseDelta.y;
                    camY += mouseDelta.x * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    camX += mouseDelta.y * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    camX = MathD.Clamp(camX, -89.9f, 89.9f);
                    Cam.GameObject.Transform.eulerAngles = new Vector3(camX, camY, 0);

                    gui.PointerPos = WindowCenter;
                    // Input.MousePosition = WindowCenter;
                }
            }
            else
            {
                moveSpeed = 1;
                if (gui.IsPointerDown(MouseButton.Middle))
                {
                    gui.SetCursorVisibility(false);
                    var mouseDelta = gui.PointerDelta;
                    var pos = Cam.GameObject.Transform.position;
                    pos -= Cam.GameObject.Transform.right * mouseDelta.x * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                    pos += Cam.GameObject.Transform.up * mouseDelta.y * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                    Cam.GameObject.Transform.position = pos;
                    gui.PointerPos = WindowCenter;

                }

                if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                    HierarchyWindow.DuplicateSelected();

                if (gui.IsKeyDown(Key.F) && HierarchyWindow.SelectHandler.Selected.Any())
                {
                    float defaultZoomFactor = 2f;
                    if (HierarchyWindow.SelectHandler.Selected.Count == 1)
                    {
                        // If only one object is selected, set the camera position to the center of that object
                        if (HierarchyWindow.SelectHandler.Selected.First().Target is GameObject singleObject)
                        {
                            Cam.GameObject.Transform.position = singleObject.Transform.position -
                                                                (Cam.GameObject.Transform.forward * defaultZoomFactor);
                            return;
                        }
                    }

                    // Calculate the bounding box based on the positions of selected objects
                    Bounds combinedBounds = new Bounds();
                    foreach (var obj in HierarchyWindow.SelectHandler.Selected)
                    {
                        if (obj.Target is GameObject go)
                        {
                            combinedBounds.Encapsulate(go.Transform.position);
                        }
                    }

                    // Calculate the zoom factor based on the size of the bounding box
                    float boundingBoxSize = (float)MathD.Max(combinedBounds.size.x, combinedBounds.size.y,
                        combinedBounds.size.z);
                    float zoomFactor = boundingBoxSize * defaultZoomFactor;

                    Vector3 averagePosition = combinedBounds.center;
                    Cam.GameObject.Transform.position =
                        averagePosition - (Cam.GameObject.Transform.forward * zoomFactor);
                }
            }
            
            if (gui.PointerWheel != 0)
            {
                // Larger distance more zoom, but clamped
                double amount = 1f * SceneViewPreferences.Instance.ZoomSensitivity;
                Cam.GameObject.Transform.position += mouseRay.direction * amount * gui.PointerWheel;

            }
        }

        DrawViewportSettings();

        if (SceneViewPreferences.Instance.ShowFPS)
        {
            //g.DrawRectFilled(imagePos + new Vector2(5, imageSize.y - 25), new Vector2(75, 20), new Color(0, 0, 0, 0.25f));
            gui.Draw2D.DrawText($"FPS: {fps:0.0}", imagePos + new Vector2(10, imageSize.y - 22));
        }
    }

    private void HandleGizmos(List<GameObject> selectedGOs, Ray mouseRay, Matrix4x4 view, Matrix4x4 projection, bool blockPicking)
    {
        gizmo.UpdateCamera(gui.CurrentNode.LayoutData.Rect, view, projection, Cam.GameObject.Transform.up, Cam.GameObject.Transform.forward, Cam.GameObject.Transform.right);

        gizmo.Snapping = Input.GetKey(Key.ControlLeft);
        gizmo.SnapDistance = SceneViewPreferences.Instance.SnapDistance;
        gizmo.SnapAngle = SceneViewPreferences.Instance.SnapAngle;
        
        Vector3 centerOfAll = Vector3.zero;
        
        for (int i = 0; i < selectedGOs.Count; i++)
        {
            var selectedGo = selectedGOs[i];
            centerOfAll += selectedGo.Transform.position;
        }
        
        centerOfAll /= selectedGOs.Count;
        
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;
        if (selectedGOs.Count == 1)
        {
            rotation = selectedGOs[0].Transform.rotation;
            scale = selectedGOs[0].Transform.localScale;
        }
        
        gizmo.SetTransform(centerOfAll, rotation, scale);
        var result = gizmo.Update(mouseRay, gui.PointerPos, blockPicking);
        if (result.HasValue)
        {
            foreach (var selectedGo in selectedGOs)
            {
                if (result.Value.TranslationDelta.HasValue)
                    selectedGo.Transform.position += result.Value.TranslationDelta.Value;

                if (result.Value.RotationDelta.HasValue && result.Value.RotationAxis.HasValue)
                {
                    Vector3 centerToSelected = selectedGo.Transform.position - centerOfAll;
                    Vector3 rotated =
                        Quaternion.AngleAxis(result.Value.RotationDelta.Value, result.Value.RotationAxis.Value) *
                        centerToSelected;
                    selectedGo.Transform.position = centerOfAll + rotated;
                    Quaternion rotationDelta = Quaternion.AngleAxis(result.Value.RotationDelta.Value,
                        result.Value.RotationAxis.Value);

                    selectedGo.Transform.rotation = rotationDelta * selectedGo.Transform.rotation;
                }

                if (result.Value.ScaleDelta.HasValue)
                    selectedGo.Transform.localScale *= result.Value.ScaleDelta.Value;
            }
        }
        gizmo.Draw();
    }

    private void HandleDragnDrop()
    {
        if (DragnDrop.Drop<GameObject>(out var original))
        {
            if (original.AssetID == Guid.Empty) return;

            GameObject go = (GameObject)EngineObject.Instantiate(original, true);
            if (go != null)
            {
                var pos = Cam.gBuffer.GetViewPositionAt(mouseUV);
                if (pos == Vector3.zero)
                    go.Transform.position = Cam.GameObject.Transform.position + Cam.GameObject.Transform.forward * 10;
                else
                    go.Transform.position = Cam.Transform.TransformPoint(pos);
            }
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Prefab>(out var prefab))
        {
            var go = prefab.Instantiate();
            var t = go;
            if (t != null)
            {
                var pos = Cam.gBuffer.GetViewPositionAt(mouseUV);
                if (pos == Vector3.zero)
                    t.Transform.position = Cam.GameObject.Transform.position + Cam.GameObject.Transform.forward * 10;
                else
                    go.Transform.position = Cam.Transform.TransformPoint(pos);
            }
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Scene>(out var scene))
        {
            SceneManager.LoadScene(scene);
        }
        else if (DragnDrop.Drop<Material>(out var material))
        {
            if (Cam.gBuffer != null)
            {
                var instanceID = Cam.gBuffer.GetObjectIDAt(mouseUV);
                if (instanceID != 0)
                {
                    // find InstanceID Object
                    var go = EngineObject.FindObjectByID<GameObject>(instanceID);
                    if (go != null)
                    {
                        // Look for a MeshRenderer
                        var renderer = go.GetComponent<MeshRenderer>();
                        if (renderer != null)
                            renderer.Material = material;
                    }
                }
            }
        }
    }

    private void DrawViewportSettings()
    {
        // TODO: Support custom Viewport Settings for tooling like A Terrain Editor having Brush Size, Strength, etc all in the Viewport

        int buttonCount = 4;
        double buttonSize = GuiStyle.ItemHeight;

        bool vertical = true;

        double width = (vertical ? buttonSize : buttonSize * buttonCount) + GuiStyle.ItemPadding * 2;
        double height = (vertical ? buttonSize * buttonCount : buttonSize) + GuiStyle.ItemPadding * 2;

        using (gui.Node("VpSettings").TopLeft(5).Scale(width, height).Padding(GuiStyle.ItemPadding).Layout(vertical ? LayoutType.Column : LayoutType.Row).Enter())
        {
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new Color(0.1f, 0.1f, 0.1f, 0.5f), 10f);

            using (gui.Node("EditorCam").Scale(buttonSize).Enter())
            {
                if (gui.IsNodePressed())
                    GlobalSelectHandler.Select(new WeakReference(Cam.GameObject));

                gui.TextNode("Label", FontAwesome6.Camera).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            gui.Tooltip("Select Editor Camera");

            var gridType = SceneViewPreferences.Instance.GridType;
            int gridTypeIndex = (int)gridType;
            GuiStyle style = new();
            style.WidgetColor = Color.clear;
            style.BorderThickness = 0;
            if (gui.Combo("GridType", "_GridTypePopup", ref gridTypeIndex, Enum.GetNames(typeof(GridType)), 0, 0, buttonSize, buttonSize, style, FontAwesome6.TableCells))
                SceneViewPreferences.Instance.GridType = (GridType)gridTypeIndex;

            using (gui.Node("GizmoMode").Scale(buttonSize).Enter())
            {
                if (gui.IsNodePressed())
                    gizmo.Orientation = (TransformGizmo.GizmoOrientation)((int)gizmo.Orientation == 1 ? 0 : 1);

                gui.TextNode("Label", gizmo.Orientation == 0 ? FontAwesome6.Globe : FontAwesome6.Cube).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            gui.Tooltip("Gizmo Mode: " + (gizmo.Orientation == 0 ? "World" : "Local"));

            using (gui.Node("OpenPreferences").Scale(buttonSize).Enter())
            {
                if (gui.IsNodePressed())
                    new PreferencesWindow(typeof(SceneViewPreferences));

                gui.TextNode("Label", FontAwesome6.Gear).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            gui.Tooltip("Open Editor Preferences");
        }
    }

    internal static void SetCamera(Vector3 position, Quaternion rotation)
    {
        LastFocusedCamera.GameObject.Transform.position = position;
        LastFocusedCamera.GameObject.Transform.rotation = rotation;
        LastFocusedCameraChanged = true;
    }
}