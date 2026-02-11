// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime;

/// <summary>
/// Canvas component that provides a UI rendering surface in 3D space.
/// The Canvas has its own RenderTexture and Paper instance for drawing UI.
/// </summary>
public class WorldCanvas : MonoBehaviour, IRenderable
{
    public event Action<Paper>? OnRenderUI;

    // Texture settings
    public int Width = 800;
    public int Height = 600;

    // Input settings
    public Camera? TargetCamera;

    // Rendering
    public Material? Material;

    private RenderTexture? _renderTexture;
    private PaperRenderer? _paperRenderer;
    private Paper? _paper;
    private PropertyState _properties = new();
    private Mesh? _quadMesh;

    // Input state
    private Float2? _lastMousePosition;
    private bool[] _mouseButtonStates = new bool[3];

    public override void OnEnable()
    {
        InitializeCanvas();
    }

    public override void OnDisable()
    {
        CleanupCanvas();
    }

    private void InitializeCanvas()
    {
        // Create the render texture
        _renderTexture = new RenderTexture(Width, Height, false, [TextureImageFormat.Color4b]);

        // Create the Paper renderer and instance
        _paperRenderer = new PaperRenderer();
        _paperRenderer.Initialize(Width, Height);
        _paper = new Paper(_paperRenderer, Width, Height, new Prowl.Quill.FontAtlasSettings());

        // Create a default material if none is provided
        if (Material.IsNotValid())
        {
            Material = new Material(Shader.LoadDefault(DefaultShader.Unlit));
        }

        // Create quad mesh for rendering the canvas
        _quadMesh = Mesh.GetFullscreenQuad();
    }

    private void CleanupCanvas()
    {
        _renderTexture?.Dispose();
        _renderTexture = null;

        _paperRenderer?.Dispose();
        _paperRenderer = null;

        _paper = null;
    }

    public override void Update()
    {
        // Check if we need to recreate the canvas due to size changes
        if (_renderTexture.IsValid() && (_renderTexture.Width != Width || _renderTexture.Height != Height))
        {
            CleanupCanvas();
            InitializeCanvas();
        }

        // Handle input if target camera is assigned
        if (TargetCamera.IsValid() && _paper != null)
        {
            UpdateInput();
        }

        // Render the UI to the texture
        RenderUI();

        // Push this canvas as a renderable
        if (_renderTexture.IsValid() && Material.IsValid() && _quadMesh.IsValid())
        {
            _properties.Clear();
            _properties.SetInt("_ObjectID", InstanceID);
            _properties.SetTexture("_MainTex", _renderTexture.MainTexture);
            GameObject.Scene.PushRenderable(this);
        }
    }

    private void UpdateInput()
    {
        if (_paper == null || TargetCamera.IsNotValid()) return;

        // Get mouse position in screen space
        Int2 mousePos = Input.MousePosition;

        // Cast a ray from the camera through the mouse position
        Float2 screenSize = new(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
        Ray ray = TargetCamera.ScreenPointToRay(new Float2(mousePos.X, mousePos.Y), screenSize);

        // Check if the ray intersects with the canvas quad
        if (RaycastCanvas(ray, out Float2 uv))
        {
            // Convert UV to canvas pixel coordinates
            int canvasX = (int)(uv.X * Width);
            int canvasY = (int)(uv.Y * Height);

            // Update Paper input state with movement
            _paper.SetPointerState(PaperMouseBtn.Unknown, canvasX, canvasY, false, true);
            _lastMousePosition = new Float2(canvasX, canvasY);

            // Handle mouse buttons
            for (int i = 0; i < 3; i++)
            {
                bool currentState = Input.GetMouseButton(i);
                bool previousState = _mouseButtonStates[i];

                PaperMouseBtn button = i switch
                {
                    0 => PaperMouseBtn.Left,
                    1 => PaperMouseBtn.Right,
                    2 => PaperMouseBtn.Middle,
                    _ => PaperMouseBtn.Unknown
                };

                if (currentState && !previousState)
                {
                    _paper.SetPointerState(button, canvasX, canvasY, true, false);
                }
                else if (!currentState && previousState)
                {
                    _paper.SetPointerState(button, canvasX, canvasY, false, false);
                }

                _mouseButtonStates[i] = currentState;
            }

            // Handle mouse wheel
            float wheelDelta = Input.MouseWheelDelta;
            if (wheelDelta != 0)
            {
                _paper.SetPointerWheel(wheelDelta);
            }
        }
        else
        {
            // Ray didn't hit canvas, clear mouse position
            _lastMousePosition = null;
        }
    }

    private bool RaycastCanvas(Ray ray, out Float2 uv)
    {
        uv = Float2.Zero;

        // Get the transform matrix of the canvas
        Float4x4 worldMatrix = Transform.LocalToWorldMatrix;

        // The fullscreen quad has vertices from (-1, -1, 0) to (1, 1, 0) in local space
        // Transform the quad plane to world space
        Float3 worldPos = Transform.Position;
        Float3 worldNormal = Transform.Forward;

        // Plane-ray intersection
        float denom = Float3.Dot(worldNormal, ray.Direction);

        // Check if ray is parallel to plane
        if (Maths.Abs(denom) < 0.0001)
            return false;

        Float3 p0ToOrigin = worldPos - ray.Origin;
        float t = Float3.Dot(p0ToOrigin, worldNormal) / denom;

        // Check if intersection is behind the ray
        if (t < 0)
            return false;

        // Get intersection point in world space
        Float3 hitPoint = ray.Origin + ray.Direction * t;

        // Transform hit point to local space
        Float4x4 worldToLocal = worldMatrix.Invert();
        Float3 localHitPoint = Float4x4.TransformPoint(new Float4(hitPoint, 1.0f), worldToLocal).XYZ;

        // Convert local position to UV coordinates (0 to 1)
        // Fullscreen quad UV mapping:
        // - localHitPoint.X: -1 (left) -> U=0, +1 (right) -> U=1
        // - localHitPoint.Y: -1 (bottom) -> V=0, +1 (top) -> V=1
        uv = new Float2(
            (localHitPoint.X + 1.0f) * 0.5f,      // -1..1 -> 0..1 (left to right)
            (1.0f - localHitPoint.Y) * 0.5f       // -1..1 -> 1..0 (top to bottom for UI)
        );

        return true;
    }

    private void RenderUI()
    {
        if (_renderTexture.IsNotValid() || _paper == null) return;

        // Begin rendering to the render texture
        _renderTexture.Begin();

        // Clear the render texture
        Graphics.Clear(0f, 0f, 0f, 0f, ClearFlags.Color);

        // Begin Paper frame
        _paper.BeginFrame(Time.DeltaTime);

        // Invoke the user's GUI callback
        OnRenderUI?.Invoke(_paper);

        // End Paper frame (this will render to the texture)
        _paper.EndFrame();

        // End rendering to the render texture
        _renderTexture.End();
    }

    // IRenderable implementation
    public Material GetMaterial() => Material ?? new Material(Shader.LoadDefault(DefaultShader.Unlit));

    public int GetLayer() => GameObject.LayerIndex;

    public Float3 GetPosition() => Transform.Position;

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh drawData, out Float4x4 model, out InstanceData[]? instanceData)
    {
        properties = _properties;
        drawData = _quadMesh ?? Mesh.GetFullscreenQuad();
        model = Transform.LocalToWorldMatrix;
        instanceData = null; // Single instance rendering
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = _renderTexture.IsValid() && Material.IsValid();

        // Create bounds for the fullscreen quad (-1 to 1 in local space)
        Float3 min = new(-1, -1, 0);
        Float3 max = new(1, 1, 0);
        AABB localBounds = new(min, max);
        bounds = localBounds.TransformBy(Transform.LocalToWorldMatrix);
    }

    /// <summary>
    /// Gets the Paper instance for manual UI rendering if needed.
    /// </summary>
    public Paper? GetPaper() => _paper;

    /// <summary>
    /// Gets the RenderTexture being drawn to.
    /// </summary>
    public RenderTexture? GetRenderTexture() => _renderTexture;
}
