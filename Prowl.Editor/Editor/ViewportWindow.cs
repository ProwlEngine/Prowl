using Hexa.NET.ImGuizmo;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Rendering.OpenGL;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;

namespace Prowl.Editor;

public class ViewportWindow : EditorWindow
{
    public static Camera LastFocusedCamera;

    //public static ImGuizmoOperation GizmosOperation = ImGuizmoOperation.Translate;
    //public static ImGuizmoMode GizmosSpace = ImGuizmoMode.Local;


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

    public ViewportWindow() : base()
    {
        Title = FontAwesome6.Camera + " Viewport";

        var CamObject = GameObject.CreateSilently();
        CamObject.Name = "Editor-Camera";
        CamObject.hideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;
        CamObject.Transform.position = new Vector3(0, 5, -10);
        Cam = CamObject.AddComponent<Camera>();
        Cam.ShowGizmos = true;
        LastFocusedCamera = Cam;
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

        IsHovered = g.IsHovering(g.CurrentNode.LayoutData.Rect);

        var renderSize = g.CurrentNode.LayoutData.Rect.Size;
        if (renderSize.x == 0 || renderSize.y == 0) return;

        if (RenderTarget == null || (int)renderSize.x != RenderTarget.Width || (int)renderSize.y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.x, (int)renderSize.y);

        var view = Matrix4x4.CreateLookToLeftHanded(Cam.GameObject.Transform.position, Cam.GameObject.Transform.forward, Cam.GameObject.Transform.up).ToFloat();
        var projection = Cam.GetProjectionMatrix((float)renderSize.x, (float)renderSize.y).ToFloat();

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

        if (gridType != GridType.None)
        {
            gridMat ??= new Material(Shader.Find("Defaults/Grid.shader"));
            gridMat.SetTexture("gPositionRoughness", Cam.gBuffer.PositionRoughness);
            gridMat.SetKeyword("GRID_XZ", gridType == GridType.XZ);
            gridMat.SetKeyword("GRID_XY", gridType == GridType.XY);
            gridMat.SetKeyword("GRID_YZ", gridType == GridType.YZ);
            Graphics.Blit(RenderTarget, gridMat, 0, false);
        }

        //DrawGizmos(selectedGOs, view, projection);

        Camera.Current = null;


        mouseUV = (g.PointerPos - imagePos) / imageSize;
        // Flip Y
        mouseUV.y = 1.0 - mouseUV.y;

        var viewportInteractable = g.GetInteractable();

        HandleDragnDrop();

        if (viewportInteractable.IsHovered())
        {
            if (g.IsPointerClick(Silk.NET.Input.MouseButton.Left))// && !ImGuizmo.IsOver())
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
                    // Version with fixed gimbal lock
                    var mouseDelta = Input.MouseDelta;
                    var rot = Cam.GameObject.Transform.eulerAngles;
                    rot.y += mouseDelta.x * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    rot.x += mouseDelta.y * (Time.deltaTimeF * 5f * SceneViewPreferences.Instance.LookSensitivity);
                    Cam.GameObject.Transform.eulerAngles = rot;

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
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Prefab>(out var prefab))
        {
            var go = prefab.Instantiate();
            var t = go;
            if (t != null)
            {
                t.Transform.position = Cam.GameObject.Transform.position + Cam.GameObject.Transform.forward * 10;
            }
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Scene>(out var scene))
        {
            SceneManager.LoadScene(scene);
        }
    }

}
