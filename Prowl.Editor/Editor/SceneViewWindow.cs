using Hexa.NET.ImGuizmo;
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

    //public static ImGuizmoOperation GizmosOperation = ImGuizmoOperation.Translate;
    //public static ImGuizmoMode GizmosSpace = ImGuizmoMode.Local;


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
        mode |= TransformGizmoMode.ScaleX | TransformGizmoMode.ScaleY | TransformGizmoMode.ScaleZ;

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
        g.CurrentNode.Padding(5);

        var renderSize = g.CurrentNode.LayoutData.Rect.Size;
        if (renderSize.x == 0 || renderSize.y == 0) return;

        if (RenderTarget == null || (int)renderSize.x != RenderTarget.Width || (int)renderSize.y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.x, (int)renderSize.y);

        WindowCenter = g.CurrentNode.LayoutData.Rect.Center;

        // Manually Render to the RenderTexture
        Cam.NearClip = SceneViewPreferences.Instance.NearClip;
        Cam.FarClip = SceneViewPreferences.Instance.FarClip;
        Cam.Render((int)renderSize.x, (int)renderSize.y);
        SceneViewPreferences.Instance.RenderResolution = Math.Clamp(SceneViewPreferences.Instance.RenderResolution, 0.1f, 8.0f);
        Cam.RenderResolution = SceneViewPreferences.Instance.RenderResolution;

        var imagePos = g.CurrentNode.LayoutData.Rect.Position;
        var imageSize = g.CurrentNode.LayoutData.Rect.Size;
        g.DrawImage(RenderTarget.InternalTextures[0], imagePos, imageSize, Color.white);

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
        //DrawGizmos(selectedGOs, view, projection);

        gizmo.UpdateCamera(g.CurrentNode.LayoutData.Rect, view, projection, Cam.GameObject.Transform.up, Cam.GameObject.Transform.forward, Cam.GameObject.Transform.right);

        gizmo.Snapping = Input.GetKey(Key.ControlLeft);
        gizmo.SnapDistance = SceneViewPreferences.Instance.SnapDistance;
        gizmo.SnapAngle = SceneViewPreferences.Instance.SnapAngle;

        Ray mouseRay = Cam.ScreenPointToRay(g.PointerPos - imagePos);
        //Ray mouseRay = new Ray(Cam.GameObject.Transform.position, Cam.GameObject.Transform.forward);

        for (int i = 0; i < selectedGOs.Count; i++)
        {
            var selectedGO = selectedGOs[i];

            gizmo.SetTransform(selectedGO.Transform.position, selectedGO.Transform.rotation, selectedGO.Transform.localScale);
            var result = gizmo.Update(mouseRay, g.PointerPos);

            if (result.HasValue)
            {

                if (result.Value.TranslationDelta.HasValue)
                    selectedGO.Transform.position += result.Value.TranslationDelta.Value;
                if (result.Value.RotationAxis.HasValue)
                {
                    var axis = result.Value.RotationAxis.Value;
                    var delta = result.Value.RotationDelta.Value;
                    selectedGO.Transform.rotation = Quaternion.AngleAxis(delta, axis) * selectedGO.Transform.rotation;
                    //selectedGO.Transform.rotation = result.Value.Rotation.Value;
                }
                if (result.Value.Scale.HasValue)
                    selectedGO.Transform.localScale = result.Value.StartScale.Value * result.Value.Scale.Value;
            }

            gizmo.Draw();

            // Draw Gizmo
            // var model = selectedGO.Transform.localToWorldMatrix.ToFloat();
            //var mvp = (model * view * projection);

            //g.Draw3D.Setup3DObject(mvp.ToDouble(), g.CurrentNode.LayoutData.Rect);
            //g.Draw3D.Arc(1, 0, 90f, new Runtime.GUI.Stroke() { Color = Color.green, Thickness = 4f, AntiAliased = true });

            //g.Draw3D.Arrow(Vector3.zero, Vector3.up, new Runtime.GUI.Stroke() { Color = Color.green, Thickness = 54f, AntiAliased = true });
            //g.Draw3D.FilledCircle(1f, new Runtime.GUI.Stroke() { Color = Color.red, Thickness = 4f, AntiAliased = true });
        }

        Camera.Current = null;

        Rect rect = g.CurrentNode.LayoutData.Rect;
        rect.width = 100;
        rect.height = 100;
        rect.x = g.CurrentNode.LayoutData.Rect.x + g.CurrentNode.LayoutData.Rect.width - rect.width - 10;
        rect.y = g.CurrentNode.LayoutData.Rect.y + 10;
        viewManipulator.SetRect(rect);
        viewManipulator.SetCamera(Cam.Transform.forward, Cam.Transform.up, Cam.projectionType == Camera.ProjectionType.Orthographic);

        if (viewManipulator.Update(out Vector3 newForward, out bool isOrtho))
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


        mouseUV = (g.PointerPos - imagePos) / imageSize;
        // Flip Y
        mouseUV.y = 1.0 - mouseUV.y;

        var viewportInteractable = g.GetInteractable();

        HandleDragnDrop();

        if (viewportInteractable.IsHovered())
        {
            if (g.IsPointerClick(Silk.NET.Input.MouseButton.Left) && !gizmo.IsOver && !viewManipulator.IsOver)
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
                            if (!go.IsPartOfPrefab || g.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
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
            else if (g.IsPointerDown(Silk.NET.Input.MouseButton.Right))
            {
                Vector3 moveDir = Vector3.zero;
                if (g.IsKeyDown(Key.W)) moveDir += Cam.GameObject.Transform.forward;
                if (g.IsKeyDown(Key.S)) moveDir -= Cam.GameObject.Transform.forward;
                if (g.IsKeyDown(Key.A)) moveDir -= Cam.GameObject.Transform.right;
                if (g.IsKeyDown(Key.D)) moveDir += Cam.GameObject.Transform.right;
                if (g.IsKeyDown(Key.E)) moveDir += Cam.GameObject.Transform.up;
                if (g.IsKeyDown(Key.Q)) moveDir -= Cam.GameObject.Transform.up;

                if (moveDir != Vector3.zero)
                {
                    moveDir = Vector3.Normalize(moveDir);
                    if (g.IsKeyDown(Key.ShiftLeft))
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

                if (g.IsPointerMoving)
                {
                    var mouseDelta = Input.MouseDelta;
                    if (SceneViewPreferences.Instance.InvertLook)
                        mouseDelta.y = -mouseDelta.y;
                    camY += mouseDelta.x * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    camX += mouseDelta.y * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    camX = Mathf.Clamp(camX, -89.9f, 89.9f);
                    Cam.GameObject.Transform.eulerAngles = new Vector3(camX, camY, 0);

                    Input.MousePosition = WindowCenter;
                }
            }
            else
            {
                moveSpeed = 1;
                if (g.IsPointerDown(MouseButton.Middle))
                {

                    var mouseDelta = Input.MouseDelta;
                    var pos = Cam.GameObject.Transform.position;
                    pos -= Cam.GameObject.Transform.right * mouseDelta.x * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                    pos += Cam.GameObject.Transform.up * mouseDelta.y * (Time.deltaTimeF * 1f * SceneViewPreferences.Instance.PanSensitivity);
                    Cam.GameObject.Transform.position = pos;

                }
                else if (Input.MouseWheelDelta != 0)
                {
                    // Larger distance more zoom, but clamped
                    double amount = 1f * SceneViewPreferences.Instance.ZoomSensitivity;
                    Cam.GameObject.Transform.position += mouseRay.direction * amount * Input.MouseWheelDelta;

                }
                else
                {

                    // If not looking around Viewport Keybinds are used instead
                    //if (g.IsKeyDown(Key.Q)) GizmosOperation = ImGuizmoOperation.Translate;
                    //else if (g.IsKeyDown(Key.W)) GizmosOperation = ImGuizmoOperation.Rotate;
                    //else if (g.IsKeyDown(Key.E)) GizmosOperation = ImGuizmoOperation.Scale;
                    //else if (g.IsKeyDown(Key.R)) GizmosOperation = ImGuizmoOperation.Universal;
                    //else if (g.IsKeyDown(Key.T)) GizmosOperation = ImGuizmoOperation.Bounds;

                }

                if (g.IsKeyDown(Key.F) && HierarchyWindow.SelectHandler.Selected.Any())
                {
                    float defaultZoomFactor = 2f;
                    if (HierarchyWindow.SelectHandler.Selected.Count == 1)
                    {
                        // If only one object is selected, set the camera position to the center of that object
                        if (HierarchyWindow.SelectHandler.Selected.First().Target is GameObject singleObject)
                        {
                            Cam.GameObject.Transform.position = singleObject.Transform.position - (Cam.GameObject.Transform.forward * defaultZoomFactor);
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
                    float boundingBoxSize = (float)Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                    float zoomFactor = boundingBoxSize * defaultZoomFactor;

                    Vector3 averagePosition = combinedBounds.center;
                    Cam.GameObject.Transform.position = averagePosition - (Cam.GameObject.Transform.forward * zoomFactor);
                }
            }
        }

        DrawViewportSettings();
        DrawPlayMode();

        if (SceneViewPreferences.Instance.ShowFPS)
        {
            //g.DrawRectFilled(imagePos + new Vector2(5, imageSize.y - 25), new Vector2(75, 20), new Color(0, 0, 0, 0.25f));
            g.DrawText($"FPS: {fps:0.0}", imagePos + new Vector2(10, imageSize.y - 22));
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
    }

    private void DrawPlayMode()
    {
        using (g.Node("PSP").FitContentWidth().Height(GuiStyle.ItemHeight).Top(5).Layout(LayoutType.Row).Enter())
        {
            // Center
            g.CurrentNode.Left(Offset.Percentage(0.5f, -(g.CurrentNode.LayoutData.Rect.width / 2)));

            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new Color(0.1f, 0.1f, 0.1f, 0.5f), 10f);

            switch (PlayMode.Current)
            {
                case PlayMode.Mode.Editing:
                    if (EditorGUI.QuickButton(FontAwesome6.Play, GuiStyle.ItemHeight, GuiStyle.ItemHeight, false))
                        PlayMode.Start();
                    break;
                case PlayMode.Mode.Playing:
                    if (EditorGUI.QuickButton(FontAwesome6.Pause, GuiStyle.ItemHeight, GuiStyle.ItemHeight, false))
                        PlayMode.Pause();
                    if (EditorGUI.QuickButton(FontAwesome6.Stop, GuiStyle.ItemHeight, GuiStyle.ItemHeight, false, GuiStyle.Red))
                        PlayMode.Stop();
                    break;
                case PlayMode.Mode.Paused:
                    if (EditorGUI.QuickButton(FontAwesome6.Play, GuiStyle.ItemHeight, GuiStyle.ItemHeight, false))
                        PlayMode.Resume();
                    if (EditorGUI.QuickButton(FontAwesome6.Stop, GuiStyle.ItemHeight, GuiStyle.ItemHeight, false, GuiStyle.Red))
                        PlayMode.Stop();
                    break;

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

        using (g.Node("VpSettings").TopLeft(5).Scale(width, height).Padding(GuiStyle.ItemPadding).Layout(vertical ? LayoutType.Column : LayoutType.Row).Enter())
        {
            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new Color(0.1f, 0.1f, 0.1f, 0.5f), 10f);

            using (g.ButtonNode("EditorCam", out var pressed, out var hovered).Scale(buttonSize).Enter())
            {
                if (pressed)
                    GlobalSelectHandler.Select(new WeakReference(Cam.GameObject));

                g.TextNode("Label", FontAwesome6.Camera).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (hovered)
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            g.SimpleTooltip("Select Editor Camera");

            var gridType = SceneViewPreferences.Instance.GridType;
            int gridTypeIndex = (int)gridType;
            GuiStyle style = new();
            style.WidgetColor = Color.clear;
            style.BorderThickness = 0;
            if (g.Combo("GridType", "_GridTypePopup", ref gridTypeIndex, Enum.GetNames(typeof(GridType)), 0, 0, buttonSize, buttonSize, style, FontAwesome6.TableCells))
                SceneViewPreferences.Instance.GridType = (GridType)gridTypeIndex;

            using (g.ButtonNode("GizmoMode", out var pressed, out var hovered).Scale(buttonSize).Enter())
            {
                if (pressed)
                    gizmo.Orientation = (TransformGizmo.GizmoOrientation)((int)gizmo.Orientation == 1 ? 0 : 1);

                g.TextNode("Label", gizmo.Orientation == 0 ? FontAwesome6.Globe : FontAwesome6.Cube).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (hovered)
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            g.SimpleTooltip("Gizmo Mode: " + (gizmo.Orientation == 0 ? "World" : "Local"));

            using (g.ButtonNode("OpenPreferences", out var pressed, out var hovered).Scale(buttonSize).Enter())
            {
                if (pressed)
                    new PreferencesWindow(typeof(SceneViewPreferences));

                g.TextNode("Label", FontAwesome6.Gear).Expand();
                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (hovered)
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, hovCol, 10);
            }
            g.SimpleTooltip("Open SceneView Preferences");
        }
    }
}