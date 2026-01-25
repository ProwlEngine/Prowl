using UnityEngine;
using UnityEngine.Rendering;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using QuillCanvas = Prowl.Quill.Canvas;
using QuillColor32 = Prowl.Vector.Color32;

namespace Quill.Unity
{
    /// <summary>
    /// Defines how the Quill canvas is rendered.
    /// </summary>
    public enum QuillRenderMode
    {
        /// <summary>
        /// Renders as a screen-space overlay using camera command buffers.
        /// </summary>
        Screen,

        /// <summary>
        /// Renders as a 3D quad in world space using Graphics.DrawMesh.
        /// </summary>
        World
    }

    /// <summary>
    /// MonoBehaviour that manages a Quill Canvas and renders vector graphics in Unity.
    /// Supports both screen-space overlay and world-space 3D quad rendering modes.
    /// </summary>
    [ExecuteAlways]
    public class QuillCanvasBehaviour : MonoBehaviour
    {
        [Header("Render Mode")]
        [Tooltip("Screen: Overlay on camera. World: 3D quad in scene.")]
        public QuillRenderMode renderMode = QuillRenderMode.Screen;

        [Header("Screen Mode Settings")]
        [Tooltip("The camera to render to. If null, uses Camera.main.")]
        public new Camera camera;

        [Tooltip("When to render the canvas relative to the camera.")]
        public CameraEvent cameraEvent = CameraEvent.AfterEverything;

        [Header("World Mode Settings")]
        [Tooltip("Pixel width of the render texture.")]
        public int pixelWidth = 1024;

        [Tooltip("Pixel height of the render texture.")]
        public int pixelHeight = 1024;

        [Header("Demo")]
        [Tooltip("Enable the built-in demo to test rendering.")]
        public bool enableDemo = true;

        private QuillCanvas _canvas;
        private QuillCanvasRenderer _renderer;

        // Screen mode
        private CommandBuffer _commandBuffer;
        private Camera _currentCamera;
        private CameraEvent _currentCameraEvent;
        private int _lastWidth;
        private int _lastHeight;

        // World mode
        private RenderTexture _renderTexture;
        private Material _worldMaterial;
        private Mesh _quadMesh;

        /// <summary>
        /// The Quill Canvas instance. Use this to draw vector graphics.
        /// </summary>
        public QuillCanvas Canvas => _canvas;

        /// <summary>
        /// The renderer backend. Advanced users can access this for custom rendering.
        /// </summary>
        public QuillCanvasRenderer Renderer => _renderer;

        /// <summary>
        /// The render texture used in World mode. Null in Screen mode.
        /// </summary>
        public RenderTexture RenderTexture => _renderTexture;

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Initialize()
        {
            // Always cleanup first to ensure clean state
            Cleanup();

            if (renderMode == QuillRenderMode.Screen)
            {
                InitializeScreenMode();
            }
            else
            {
                InitializeWorldMode();
            }
        }

        private void InitializeScreenMode()
        {
            var cam = camera != null ? camera : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("QuillCanvasBehaviour: No camera available for Screen mode.");
                return;
            }

            int width = cam.pixelWidth;
            int height = cam.pixelHeight;

            _renderer = new QuillCanvasRenderer();
            _renderer.Initialize(width, height, null, cam);

            _canvas = new QuillCanvas(_renderer, new FontAtlasSettings());

            _commandBuffer = new CommandBuffer { name = "Quill Canvas" };

            _currentCamera = cam;
            _currentCameraEvent = cameraEvent;
            _currentCamera.AddCommandBuffer(_currentCameraEvent, _commandBuffer);

            _lastWidth = width;
            _lastHeight = height;
        }

        private void InitializeWorldMode()
        {
            // Create render texture
            _renderTexture = new RenderTexture(pixelWidth, pixelHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Quill World Canvas RT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();

            // Initialize renderer with render texture size
            _renderer = new QuillCanvasRenderer();
            _renderer.Initialize(pixelWidth, pixelHeight, null, null);

            _canvas = new QuillCanvas(_renderer, new FontAtlasSettings());

            // Create quad mesh
            _quadMesh = CreateQuadMesh();

            // Create material for displaying the render texture
            _worldMaterial = new Material(Shader.Find("Unlit/Transparent"))
            {
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = _renderTexture
            };

            _lastWidth = pixelWidth;
            _lastHeight = pixelHeight;
        }

        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "Quill World Quad",
                hideFlags = HideFlags.HideAndDontSave
            };

            // Height is 1 meter, width is based on aspect ratio
            float aspect = (float)pixelWidth / pixelHeight;
            float halfW = aspect / 2f;
            float halfH = 0.5f;

            mesh.vertices = new Vector3[]
            {
                new Vector3(-halfW, -halfH, 0),
                new Vector3(halfW, -halfH, 0),
                new Vector3(halfW, halfH, 0),
                new Vector3(-halfW, halfH, 0)
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void RemoveCommandBufferFromCamera()
        {
            if (_currentCamera != null && _commandBuffer != null)
            {
                try
                {
                    _currentCamera.RemoveCommandBuffer(_currentCameraEvent, _commandBuffer);
                }
                catch (System.Exception)
                {
                    // Camera might already be destroyed or command buffer not attached
                }
            }
            _currentCamera = null;
        }

        private void Cleanup()
        {
            // Always remove command buffer from camera first
            RemoveCommandBufferFromCamera();

            if (_commandBuffer != null)
            {
                _commandBuffer.Dispose();
                _commandBuffer = null;
            }

            // World mode cleanup
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                DestroyImmediate(_renderTexture);
                _renderTexture = null;
            }

            if (_worldMaterial != null)
            {
                DestroyImmediate(_worldMaterial);
                _worldMaterial = null;
            }

            if (_quadMesh != null)
            {
                DestroyImmediate(_quadMesh);
                _quadMesh = null;
            }

            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }

            _canvas = null;
        }

        private void Update()
        {
            if (_canvas == null || _renderer == null)
                return;

            if (renderMode == QuillRenderMode.Screen)
            {
                UpdateScreenMode();
            }
            else
            {
                UpdateWorldMode();
            }
        }

        private void UpdateScreenMode()
        {
            var cam = camera != null ? camera : Camera.main;
            if (cam == null)
                return;

            // Handle camera or camera event change
            if (cam != _currentCamera || cameraEvent != _currentCameraEvent)
            {
                RemoveCommandBufferFromCamera();

                if (_commandBuffer == null)
                {
                    _commandBuffer = new CommandBuffer { name = "Quill Canvas" };
                }

                _currentCamera = cam;
                _currentCameraEvent = cameraEvent;
                _currentCamera.AddCommandBuffer(_currentCameraEvent, _commandBuffer);
            }

            // Handle resize
            int width = cam.pixelWidth;
            int height = cam.pixelHeight;
            if (width != _lastWidth || height != _lastHeight)
            {
                _renderer.UpdateSize(width, height);
                _lastWidth = width;
                _lastHeight = height;
            }

            // Clear and begin new frame
            _canvas.Clear();
            _canvas.BeginFrame(width, height, 1);

            // Let users draw via the OnQuillRender callback or override
            OnQuillRender(_canvas, width, height);

            // Built-in demo
            if (enableDemo)
            {
                DrawDemo(_canvas, width, height);
            }

            // Clear command buffer and issue draw commands
            _commandBuffer.Clear();
            _renderer.RenderCalls(_commandBuffer, _canvas, _canvas.DrawCalls);
        }

        private void UpdateWorldMode()
        {
            // Make sure command buffer is removed when in world mode
            RemoveCommandBufferFromCamera();

            // Handle size changes
            if (pixelWidth != _lastWidth || pixelHeight != _lastHeight)
            {
                // Recreate render texture with new size
                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    DestroyImmediate(_renderTexture);
                }

                _renderTexture = new RenderTexture(pixelWidth, pixelHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Quill World Canvas RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _renderTexture.Create();

                if (_worldMaterial != null)
                {
                    _worldMaterial.mainTexture = _renderTexture;
                }
                _renderer.UpdateSize(pixelWidth, pixelHeight);

                _lastWidth = pixelWidth;
                _lastHeight = pixelHeight;
            }

            // Update quad mesh if world size changed
            UpdateQuadMesh();

            // Clear and begin new frame
            _canvas.Clear();
            _canvas.BeginFrame(pixelWidth, pixelHeight, 1);

            // Let users draw via the OnQuillRender callback or override
            OnQuillRender(_canvas, pixelWidth, pixelHeight);

            // Built-in demo
            if (enableDemo)
            {
                DrawDemo(_canvas, pixelWidth, pixelHeight);
            }

            // Render to texture
            RenderToTexture();

            // Draw the quad in world space
            if (_quadMesh != null && _worldMaterial != null)
            {
                Graphics.DrawMesh(_quadMesh, transform.localToWorldMatrix, _worldMaterial, gameObject.layer);
            }
        }

        private void UpdateQuadMesh()
        {
            if (_quadMesh == null) return;

            // Height is 1 meter, width is based on aspect ratio
            float aspect = (float)pixelWidth / pixelHeight;
            float halfW = aspect / 2f;
            float halfH = 0.5f;

            _quadMesh.vertices = new Vector3[]
            {
                new Vector3(-halfW, -halfH, 0),
                new Vector3(halfW, -halfH, 0),
                new Vector3(halfW, halfH, 0),
                new Vector3(-halfW, halfH, 0)
            };
            _quadMesh.RecalculateBounds();
        }

        private void RenderToTexture()
        {
            if (_renderTexture == null || _canvas.DrawCalls.Count == 0)
                return;

            // Create a temporary command buffer for rendering to texture
            var cmd = new CommandBuffer { name = "Quill World Render" };

            // Set render target
            cmd.SetRenderTarget(_renderTexture);
            cmd.ClearRenderTarget(true, true, UnityEngine.Color.clear);

            // Render the canvas
            _renderer.RenderCalls(cmd, _canvas, _canvas.DrawCalls);

            // Execute and release
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();
        }

        /// <summary>
        /// Override this method to draw custom vector graphics.
        /// Called every frame after canvas is cleared and before demo rendering.
        /// </summary>
        /// <param name="canvas">The canvas to draw on.</param>
        /// <param name="width">Logical width of the canvas in pixels.</param>
        /// <param name="height">Logical height of the canvas in pixels.</param>
        protected virtual void OnQuillRender(QuillCanvas canvas, float width, float height)
        {
            // Override in derived class to draw custom graphics
        }

        private float _time;

        private void DrawDemo(QuillCanvas canvas, float width, float height)
        {
            _time += Time.deltaTime;

            // Draw some shapes
            float centerX = width / 2;
            float centerY = height / 2;

            // Animated rotating rectangle
            canvas.SaveState();
            canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateTranslation(centerX, centerY));
            canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateRotation(_time * 30f));

            // Filled rectangle with gradient
            canvas.SetLinearBrush(-50, -50, 50, 50,
                QuillColor32.FromArgb(255, 255, 100, 100),
                QuillColor32.FromArgb(255, 100, 100, 255));
            canvas.RectFilled(-50, -50, 100, 100, QuillColor32.FromArgb(255, 255, 255));

            // Stroked rectangle
            canvas.SetStrokeColor(QuillColor32.FromArgb(255, 255, 255, 255));
            canvas.SetStrokeWidth(3);
            canvas.Rect(-50, -50, 100, 100);
            canvas.Stroke();

            canvas.RestoreState();

            // Draw circles
            canvas.ClearBrush();

            // Filled circle
            canvas.SetRadialBrush(centerX - 150, centerY, 0, 40,
                QuillColor32.FromArgb(255, 100, 255, 100),
                QuillColor32.FromArgb(255, 0, 100, 0));
            canvas.CircleFilled(centerX - 150, centerY, 40, QuillColor32.FromArgb(255, 255, 255));

            // Stroked circle
            canvas.SetStrokeColor(QuillColor32.FromArgb(255, 255, 200, 100));
            canvas.SetStrokeWidth(4);
            canvas.Circle(centerX + 150, centerY, 40);
            canvas.Stroke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Reinitialize when properties change in editor
            if (isActiveAndEnabled)
            {
                // Delay to avoid issues during serialization
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled)
                    {
                        Initialize();
                    }
                };
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (renderMode == QuillRenderMode.World)
            {
                // Draw world quad outline in editor
                Gizmos.color = UnityEngine.Color.cyan;
                Gizmos.matrix = transform.localToWorldMatrix;

                // Height is 1 meter, width is based on aspect ratio
                float aspect = (float)pixelWidth / pixelHeight;
                float halfW = aspect / 2f;
                float halfH = 0.5f;

                Vector3 bl = new Vector3(-halfW, -halfH, 0);
                Vector3 br = new Vector3(halfW, -halfH, 0);
                Vector3 tr = new Vector3(halfW, halfH, 0);
                Vector3 tl = new Vector3(-halfW, halfH, 0);

                Gizmos.DrawLine(bl, br);
                Gizmos.DrawLine(br, tr);
                Gizmos.DrawLine(tr, tl);
                Gizmos.DrawLine(tl, bl);
            }
        }
#endif
    }
}
