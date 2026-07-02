using Prowl.Quill.External.LibTessDotNet;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prowl.Quill
{
    /// <summary>
    /// Specifies the type of brush used for filling shapes.
    /// </summary>
    public enum BrushType
    {
        /// <summary>
        /// No brush; uses solid color from vertex data.
        /// </summary>
        None = 0,

        /// <summary>
        /// Linear gradient brush that transitions between two colors along a line.
        /// </summary>
        Linear = 1,

        /// <summary>
        /// Radial gradient brush that transitions between two colors in a circular pattern.
        /// </summary>
        Radial = 2,

        /// <summary>
        /// Box gradient brush that transitions between two colors with rounded corners.
        /// </summary>
        Box = 3
    }

    /// <summary>
    /// Specifies the winding rule used to determine the interior of complex paths.
    /// </summary>
    public enum WindingMode
    {
        /// <summary>
        /// A point is inside the shape if a ray from that point crosses an odd number of path segments.
        /// Also known as the even-odd fill rule.
        /// </summary>
        OddEven,

        /// <summary>
        /// A point is inside the shape if the winding number is non-zero.
        /// Also known as the non-zero fill rule.
        /// </summary>
        NonZero
    }

    /// <summary>
    /// Represents a single draw call containing rendering state and the number of elements to draw.
    /// </summary>
    public struct DrawCall
    {
        /// <summary>
        /// The number of index elements (vertices * 3) in this draw call.
        /// </summary>
        public int ElementCount;

        /// <summary>
        /// The brush state for this draw call, containing gradient and texture information.
        /// </summary>
        public Brush Brush;

        internal Transform2D scissor;
        internal Float2 scissorExtent;
        internal int stateHash;
        internal object? fontAtlas;

        /// <summary>
        /// Gets the texture from the brush. Returns null if no texture is set.
        /// </summary>
        public object? Texture => Brush.Texture;

        /// <summary>
        /// The font atlas texture for text in this draw call, or null if it contains no text.
        /// Backends bind this to a dedicated sampler unit; the default shader samples it for text
        /// fragments (UV >= 2) while shapes sample <see cref="Texture"/>.
        /// </summary>
        public object? FontAtlas => fontAtlas;

        /// <summary>
        /// Gets the custom shader from the brush. Returns null if no custom shader is set (uses default).
        /// </summary>
        public object? Shader => Brush.Shader;

        /// <summary>
        /// Gets the shader uniforms from the brush. Returns null if no custom uniforms are set.
        /// </summary>
        public ShaderUniforms? ShaderUniforms => Brush.Uniforms;

        public void GetScissor(out Float4x4 matrix, out Float2 extent)
        {
            if (scissorExtent.X < -0.5f || scissorExtent.Y < -0.5f)
            {
                // Invalid scissor - disable it
                // Extent must be negative so the shader's early-out (scissorExt < 0) triggers
                matrix = new Float4x4();
                extent = new Float2(-1, -1);
            }
            else
            {
                // Set up scissor transform and dimensions
                matrix = scissor.Inverse().ToMatrix();
                extent = new Float2(scissorExtent.X, scissorExtent.Y);
            }
        }
    }

    /// <summary>
    /// Holds custom uniform values for a shader. Used for batching - shapes with the same
    /// shader and uniform values will be batched together into a single draw call.
    /// </summary>
    public class ShaderUniforms
    {
        private readonly Dictionary<string, object> _uniforms = new Dictionary<string, object>();
        private int _cachedHash;
        private bool _hashDirty = true;

        /// <summary>
        /// Gets the uniform values as a read-only dictionary.
        /// </summary>
        public IReadOnlyDictionary<string, object> Values => _uniforms;

        /// <summary>
        /// Sets a uniform value. Supported types: float, int, Float2, Float3, Float4, Float4x4.
        /// </summary>
        public void Set(string name, object value)
        {
            _uniforms[name] = value;
            _hashDirty = true;
        }

        /// <summary>
        /// Removes a uniform by name.
        /// </summary>
        public void Remove(string name)
        {
            if (_uniforms.Remove(name))
                _hashDirty = true;
        }

        /// <summary>
        /// Clears all uniforms.
        /// </summary>
        public void Clear()
        {
            _uniforms.Clear();
            _hashDirty = true;
        }

        /// <summary>
        /// Creates a shallow clone of this ShaderUniforms instance.
        /// </summary>
        internal ShaderUniforms Clone()
        {
            var clone = new ShaderUniforms();
            foreach (var kvp in _uniforms)
                clone._uniforms[kvp.Key] = kvp.Value;
            clone._cachedHash = _cachedHash;
            clone._hashDirty = _hashDirty;
            return clone;
        }

        internal int ComputeHash()
        {
            if (!_hashDirty) return _cachedHash;

            unchecked
            {
                int hash = 17;
                // Sort keys for consistent hashing
                foreach (var key in _uniforms.Keys.OrderBy(k => k))
                {
                    hash = hash * 31 + key.GetHashCode();
                    hash = hash * 31 + (_uniforms[key]?.GetHashCode() ?? 0);
                }
                _cachedHash = hash;
            }
            _hashDirty = false;
            return _cachedHash;
        }
    }

    /// <summary>
    /// Represents a vertex in the canvas rendering system with position, UV coordinates, and color.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        // 8 bytes position + 8 bytes uv + 4 bytes color = 20 bytes
        public static int SizeInBytes => 20;

        /// <summary>Gets the position of the vertex in screen space.</summary>
        public Float2 Position => new Float2(x, y);

        /// <summary>Gets the texture/UV coordinates of the vertex.</summary>
        public Float2 UV => new Float2(u, v);

        /// <summary>Gets the color of the vertex.</summary>
        public Color32 Color => Color32.FromArgb(a, r, g, b);

        // ---- Core fields (20 bytes) ----
        /// <summary>The X position in screen space.</summary>
        public float x;
        /// <summary>The Y position in screen space.</summary>
        public float y;
        /// <summary>The U texture coordinate.</summary>
        public float u;
        /// <summary>The V texture coordinate.</summary>
        public float v;
        /// <summary>The red color component.</summary>
        public byte r;
        /// <summary>The green color component.</summary>
        public byte g;
        /// <summary>The blue color component.</summary>
        public byte b;
        /// <summary>The alpha (transparency) component.</summary>
        public byte a;

        /// <summary>Creates a vertex.</summary>
        public Vertex(in Float2 position, in Float2 UV, in Color32 color)
        {
            x = (float)position.X;
            y = (float)position.Y;
            u = (float)UV.X;
            v = (float)UV.Y;
            r = color.R;
            g = color.G;
            b = color.B;
            a = color.A;
        }
    }

    /// <summary>
    /// Represents a brush used for filling shapes with gradients, textures, or custom shaders.
    /// </summary>
    public struct Brush
    {
        /// <summary>
        /// Gets the inverse transformation matrix for the brush gradient.
        /// </summary>
        public Float4x4 BrushMatrix => Transform.Inverse().ToMatrix();

        /// <summary>
        /// Gets the inverse transformation matrix for texture mapping.
        /// </summary>
        public Float4x4 TextureMatrix => TextureTransform.Inverse().ToMatrix();

        /// <summary>
        /// The transformation applied to the brush gradient coordinates.
        /// </summary>
        public Transform2D Transform;

        /// <summary>
        /// The transformation applied to texture coordinates.
        /// </summary>
        public Transform2D TextureTransform;

        /// <summary>
        /// The type of brush (None, Linear, Radial, or Box gradient).
        /// </summary>
        public BrushType Type;

        /// <summary>
        /// The first color of the gradient (inner color for radial, start color for linear).
        /// </summary>
        public Color32 Color1;

        /// <summary>
        /// The second color of the gradient (outer color for radial, end color for linear).
        /// </summary>
        public Color32 Color2;

        /// <summary>
        /// The first point of the gradient (center for radial, start point for linear).
        /// </summary>
        public Float2 Point1;

        /// <summary>
        /// The second point of the gradient. For linear gradients this is the end point.
        /// For radial gradients, X and Y contain inner and outer radius.
        /// For box gradients, this contains the half-size.
        /// </summary>
        public Float2 Point2;

        /// <summary>
        /// The corner radius for box gradients.
        /// </summary>
        public float CornerRadii;

        /// <summary>
        /// The feather amount for box gradients, controlling the softness of edges.
        /// </summary>
        public float Feather;

        /// <summary>
        /// Backdrop blur radius in pixels. When greater than zero, any shape filled with this brush
        /// is composited over a blurred copy of the framebuffer behind it (frosted glass). This is
        /// orthogonal to <see cref="Type"/>, so it combines with solid, gradient, or textured fills.
        /// Backends without backdrop blur support ignore it and draw the fill normally.
        /// </summary>
        public float BackdropBlur;

        /// <summary>
        /// The texture to apply to shapes, or null for no texture.
        /// </summary>
        public object? Texture;

        /// <summary>
        /// Custom shader object (backend-specific). When null, the default shader is used.
        /// When set, the renderer will use this shader and only apply user-provided uniforms.
        /// </summary>
        public object? Shader;

        /// <summary>
        /// Custom uniforms to pass to the shader. Only used when Shader is not null.
        /// </summary>
        public ShaderUniforms? Uniforms;

        internal int ComputeHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)Type;
                hash = hash * 31 + Color1.GetHashCode();
                hash = hash * 31 + Color2.GetHashCode();
                hash = hash * 31 + Point1.GetHashCode();
                hash = hash * 31 + Point2.GetHashCode();
                hash = hash * 31 + CornerRadii.GetHashCode();
                hash = hash * 31 + Feather.GetHashCode();
                hash = hash * 31 + BackdropBlur.GetHashCode();
                hash = hash * 31 + Transform.GetHashCode();
                hash = hash * 31 + (Texture?.GetHashCode() ?? 0);
                hash = hash * 31 + TextureTransform.GetHashCode();
                if (Shader != null)
                {
                    hash = hash * 31 + Shader.GetHashCode();
                    hash = hash * 31 + (Uniforms?.ComputeHash() ?? 0);
                }
                return hash;
            }
        }
    }

    internal struct ProwlCanvasState
    {
        internal Transform2D transform;

        internal Color32 strokeColor;
        internal JointStyle strokeJoint;
        internal EndCapStyle strokeStartCap;
        internal EndCapStyle strokeEndCap;
        internal float strokeWidth;
        internal float strokeScale;
        internal float miterLimit;
        internal float tess_tol;
        internal float roundingMinDistance;

        internal Transform2D scissor;
        internal Float2 scissorExtent;
        internal Brush brush;


        internal Color32 fillColor;
        internal WindingMode fillMode;

        internal void Reset()
        {
            transform = Transform2D.Identity;
            strokeColor = Color32.FromArgb(255, 0, 0, 0); // Default stroke color (black)
            strokeJoint = JointStyle.Bevel; // Default joint style
            strokeStartCap = EndCapStyle.Butt; // Default start cap style
            strokeEndCap = EndCapStyle.Butt; // Default end cap style
            strokeWidth = 1f; // Default stroke width
            strokeScale = 1f; // Default stroke scale
            miterLimit = 4; // Default miter limit
            tess_tol = 0.5f; // Default tessellation tolerance
            roundingMinDistance = 3; //Default _state.roundingMinDistance
            scissor = Transform2D.Identity;
            scissorExtent.X = -1.0f;
            scissorExtent.Y = -1.0f;
            brush = new Brush();
            brush.Transform = Transform2D.Identity;
            brush.TextureTransform = Transform2D.Identity;
            brush.Texture = null;
            brush.Shader = null;
            brush.Uniforms = null;
            fillColor = Color32.FromArgb(255, 0, 0, 0); // Default fill color (black)
            fillMode = WindingMode.OddEven; // Default winding mode
        }
    }

    /// <summary>
    /// A hardware-accelerated 2D vector graphics canvas for drawing shapes, text, and images.
    /// Provides an API similar to HTML5 Canvas for creating paths, stroking, filling, and rendering.
    /// </summary>
    public partial class Canvas
    {
        internal class SubPath
        {
            internal List<Float2> Points { get; }

            public SubPath(List<Float2> points)
            {
                Points = points;
            }
        }

        /// <summary>
        /// Gets the list of draw calls accumulated during rendering.
        /// </summary>
        public IReadOnlyList<DrawCall> DrawCalls => _drawCalls.AsReadOnly();

        /// <summary>
        /// Gets the list of triangle indices for all accumulated geometry.
        /// </summary>
        public IReadOnlyList<uint> Indices => _indices.AsReadOnly();

        /// <summary>
        /// Gets the list of vertices for all accumulated geometry.
        /// </summary>
        public IReadOnlyList<Vertex> Vertices => _vertices.AsReadOnly();

        /// <summary>
        /// Gets the current point of the active path, or Zero if no path is active.
        /// </summary>
        public Float2 CurrentPoint => _currentSubPath != null && _currentSubPath.Points.Count > 0 ? CurrentPointInternal : Float2.Zero;

        internal Float2 CurrentPointInternal => _currentSubPath.Points[_currentSubPath.Points.Count - 1];
        internal ICanvasRenderer _renderer;

        internal bool _isNewDrawCallRequested = false;
        internal List<DrawCall> _drawCalls = new List<DrawCall>();
        internal Stack<object> _textureStack = new Stack<object>();

        private int _currentDrawStateHash;
        private bool _drawStateDirty = true;

        // The font atlas texture bound to the dedicated font sampler unit. Persistent canvas state
        // (not per-save/restore) that only changes when the atlas is (re)allocated, so text batches
        // with shapes. Part of the draw-state hash so a change cleanly splits the batch.
        private object? _currentFontAtlas;

        internal List<uint> _indices = new List<uint>();
        internal List<Vertex> _vertices = new List<Vertex>();

        private readonly List<SubPath> _subPaths = new List<SubPath>();
        private SubPath? _currentSubPath = null;
        private bool _isPathReady = false;

        private readonly Stack<ProwlCanvasState> _savedStates = new Stack<ProwlCanvasState>();
        private ProwlCanvasState _state;
        private float _globalAlpha;

        private TextRenderer _scribeRenderer;

        // Half-pixel offsets (in logical units) for edge-aligned AA.
        private float _pixelWidth = 1.0f;
        private float _pixelHalf = 0.5f;

        private float _framebufferScale = 1.0f;
        private float _width = 0.0f;
        private float _height = 0.0f;

        private IMarkdownImageProvider? _markdownImageProvider = null;

        /// <summary>
        /// Gets the framebuffer scale (physical pixels per logical pixel). All coordinates passed
        /// to the canvas are in logical units; the canvas emits pixel-space vertices by
        /// multiplying them by this factor, and font atlases are rasterized at this density for
        /// crisp HiDPI output.
        /// </summary>
        public float FramebufferScale => _framebufferScale;

        /// <summary>
        /// Gets the canvas width in logical units (framebuffer width / FramebufferScale).
        /// </summary>
        public float Width => _width;

        /// <summary>
        /// Gets the canvas height in logical units (framebuffer height / FramebufferScale).
        /// </summary>
        public float Height => _height;

        /// <summary>
        /// Gets the size of one physical pixel in logical units (= 1 / FramebufferScale).
        /// </summary>
        public float PixelFraction => _pixelWidth;

        /// <summary>
        /// Gets the text renderer for drawing text and accessing font functionality.
        /// </summary>
        public TextRenderer Text => _scribeRenderer;

        /// <summary>
        /// Creates a new Canvas with the specified renderer backend and font settings.
        /// </summary>
        /// <param name="renderer">The renderer backend implementation for drawing.</param>
        /// <param name="fontAtlasSettings">Settings for the font atlas used for text rendering.</param>
        /// <exception cref="ArgumentNullException">Thrown when renderer is null.</exception>
        public Canvas(ICanvasRenderer renderer, FontAtlasSettings fontAtlasSettings)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer), "Renderer cannot be null.");

            _renderer = renderer;
            _scribeRenderer = new TextRenderer(this, fontAtlasSettings);
            UpdatePixelCalculations();
            Clear();
        }

        /// <summary>
        /// Begins a new frame.
        /// </summary>
        /// <remarks>
        /// All drawing coordinates are in logical units. The framebuffer scale tells the canvas
        /// how many physical pixels correspond to one logical unit.
        /// </remarks>
        /// <param name="width">Window width in logical units.</param>
        /// <param name="height">Window height in logical units.</param>
        /// <param name="framebufferScale">Ratio of physical pixels to logical pixels (1.0 = standard, 2.0 = Retina/HiDPI).</param>
        public void BeginFrame(float width, float height, float framebufferScale = 1.0f)
        {
            if (framebufferScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(framebufferScale), "Framebuffer scale must be greater than zero.");

            _framebufferScale = framebufferScale;
            _width = width;
            _height = height;

            UpdatePixelCalculations();
            Clear();
        }

        #region Coordinate Conversion

        /// <summary>
        /// Converts a point from physical pixel coordinates to logical units.
        /// Handy for converting mouse/touch positions supplied by the host.
        /// </summary>
        public Float2 PixelToLogical(Float2 pixelPoint) => pixelPoint / _framebufferScale;

        /// <summary>
        /// Converts a value from physical pixels to logical units.
        /// </summary>
        public float PixelToLogical(float pixelValue) => pixelValue / _framebufferScale;

        /// <summary>
        /// Converts a point from logical units to physical pixel coordinates.
        /// </summary>
        public Float2 LogicalToPixel(Float2 logicalPoint) => logicalPoint * _framebufferScale;

        /// <summary>
        /// Converts a value from logical units to physical pixels.
        /// </summary>
        public float LogicalToPixel(float logicalValue) => logicalValue * _framebufferScale;

        #endregion

        private void UpdatePixelCalculations()
        {
            _pixelWidth = 1.0f / _framebufferScale;
            _pixelHalf = _pixelWidth * 0.5f;
        }

        /// <summary>
        /// Approximate uniform scale (in logical units) of the current transform, taken from the
        /// square root of the linear part's determinant. The hardcoded "Filled" primitives inset/
        /// outset their anti-aliasing fringe in logical space before transforming, so dividing the
        /// half-pixel fringe by this keeps it a constant ~1 physical pixel on screen at any zoom
        /// (matching the polyline/stroke fringe, which is already built in pixel space).
        /// </summary>
        private float FringeHalfLogical()
        {
            var t = _state.transform;
            double det = t.A * t.D - t.B * t.C;
            double scale = Math.Sqrt(Math.Abs(det));
            return scale > 1e-6 ? _pixelHalf / (float)scale : _pixelHalf;
        }

        /// <summary>
        /// Clears all accumulated geometry, draw calls, and resets the canvas state.
        /// </summary>
        internal void Clear()
        {
            _drawCalls.Clear();
            _textureStack.Clear();

            _indices.Clear();
            _vertices.Clear();

            _savedStates.Clear();
            _state = new ProwlCanvasState();
            _state.Reset();

            _subPaths.Clear();
            _currentSubPath = null;
            _isPathReady = true;

            _globalAlpha = 1f;
            _drawStateDirty = true;
        }


        #region State

        private void InvalidateDrawState() => _drawStateDirty = true;

        private int ComputeDrawStateHash()
        {
            if (!_drawStateDirty)
                return _currentDrawStateHash;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _state.scissorExtent.GetHashCode();
                hash = hash * 31 + _state.scissor.GetHashCode();
                hash = hash * 31 + _state.brush.ComputeHash();
                hash = hash * 31 + (_currentFontAtlas?.GetHashCode() ?? 0);
                _currentDrawStateHash = hash;
            }
            _drawStateDirty = false;
            return _currentDrawStateHash;
        }

        /// <summary>
        /// Saves the current canvas state (transform, stroke, fill, scissor, brush) to a stack.
        /// </summary>
        public void SaveState() => _savedStates.Push(_state);

        /// <summary>
        /// Restores the most recently saved canvas state from the stack.
        /// Does nothing if no state has been saved.
        /// </summary>
        public void RestoreState()
        {
            if (_savedStates.Count == 0)
                return;
            _state = _savedStates.Pop();
            InvalidateDrawState();
        }

        /// <summary>
        /// Resets the canvas state to default values without clearing the state stack.
        /// </summary>
        public void ResetState() { _state.Reset(); InvalidateDrawState(); }

        /// <summary>
        /// Sets the color used for stroking paths.
        /// </summary>
        /// <param name="color">The stroke color.</param>
        public void SetStrokeColor(Color32 color) => _state.strokeColor = color;

        /// <summary>
        /// Sets the joint style used when stroking paths at corners.
        /// </summary>
        /// <param name="joint">The joint style (Bevel, Miter, or Round).</param>
        public void SetStrokeJoint(JointStyle joint) => _state.strokeJoint = joint;

        /// <summary>
        /// Sets the cap style for both start and end of open paths.
        /// </summary>
        /// <param name="cap">The cap style (Butt, Square, Round, or Bevel).</param>
        public void SetStrokeCap(EndCapStyle cap)
        {
            _state.strokeStartCap = cap;
            _state.strokeEndCap = cap;
        }

        /// <summary>
        /// Sets the cap style for the start of open paths.
        /// </summary>
        /// <param name="cap">The cap style (Butt, Square, Round, or Bevel).</param>
        public void SetStrokeStartCap(EndCapStyle cap) => _state.strokeStartCap = cap;

        /// <summary>
        /// Sets the cap style for the end of open paths.
        /// </summary>
        /// <param name="cap">The cap style (Butt, Square, Round, or Bevel).</param>
        public void SetStrokeEndCap(EndCapStyle cap) => _state.strokeEndCap = cap;

        /// <summary>
        /// Sets the width of stroked paths in logical units.
        /// </summary>
        /// <param name="width">The stroke width. Default is 2.</param>
        public void SetStrokeWidth(float width = 2f) => _state.strokeWidth = width;

        /// <summary>
        /// Sets a scale factor applied to stroke width.
        /// </summary>
        /// <param name="scale">The scale factor.</param>
        public void SetStrokeScale(float scale) => _state.strokeScale = scale;


        /// <summary>
        /// Sets the miter limit for mitered joints. When the miter length would exceed this ratio of stroke width, the joint falls back to bevel.
        /// </summary>
        /// <param name="limit">The miter limit ratio. Default is 4.</param>
        public void SetMiterLimit(float limit = 4) => _state.miterLimit = limit;

        /// <summary>
        /// Sets the tessellation tolerance for curve approximation. Lower values produce smoother curves with more triangles.
        /// </summary>
        /// <param name="tolerance">The tessellation tolerance. Default is 0.5.</param>
        public void SetTessellationTolerance(float tolerance = 0.5f) => _state.tess_tol = tolerance;

        /// <summary>
        /// Sets the minimum distance between points when approximating curves and arcs.
        /// </summary>
        /// <param name="distance">The minimum distance in logical units. Default is 3.</param>
        public void SetRoundingMinDistance(float distance = 3) => _state.roundingMinDistance = distance;

        /// <summary>
        /// Sets a texture on the current brush. The texture will be applied to all shapes drawn with this brush.
        /// Use SetBrushTextureTransform to control how the texture maps to world coordinates.
        /// </summary>
        /// <param name="texture">The texture to apply, or null to clear the brush texture.</param>
        public void SetBrushTexture(object? texture)
        {
            _state.brush.Texture = texture;
            // Default texture transform: 1 pixel = 1 texel, starting at origin
            if (texture != null && _state.brush.TextureTransform == Transform2D.Identity)
            {
                var size = _renderer.GetTextureSize(texture);
                _state.brush.TextureTransform = Transform2D.CreateScale(1.0f / size.X, 1.0f / size.Y);
            }
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets the texture transform for the current brush. This controls how world coordinates map to texture coordinates.
        /// The transform is applied to world-space coordinates before sampling the texture.
        /// </summary>
        /// <param name="transform">The transformation to apply. Use scale to control texture size, rotation to rotate texture, translation to offset it.</param>
        public void SetBrushTextureTransform(Transform2D transform)
        {
            _state.brush.TextureTransform = _state.transform * transform;
            InvalidateDrawState();
        }

        /// <summary>
        /// Clears the brush texture, reverting to solid color or gradient rendering.
        /// </summary>
        public void ClearBrushTexture()
        {
            _state.brush.Texture = null;
            _state.brush.TextureTransform = Transform2D.Identity;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets the font atlas texture used by text draws. This is kept as a dedicated canvas-level
        /// state (bound to a separate sampler unit by the backend) rather than the brush texture, so
        /// text and shapes share a draw call and batch together. Text vertices are flagged by their
        /// UV (>= 2) and sample this atlas; shapes sample the brush texture. It only changes when the
        /// atlas is (re)allocated, so it rarely breaks a batch.
        /// </summary>
        internal void SetFontAtlas(object? texture)
        {
            if (ReferenceEquals(_currentFontAtlas, texture))
                return;
            _currentFontAtlas = texture;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets a custom shader on the current brush. When a custom shader is set,
        /// the default brush uniforms will NOT be set by the renderer - the user is responsible
        /// for setting all required uniforms via SetShaderUniform() or SetShaderUniforms().
        /// </summary>
        /// <param name="shader">The backend-specific shader object, or null to use the default shader.</param>
        public void SetCustomShader(object? shader)
        {
            _state.brush.Shader = shader;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets a custom uniform value for the current shader.
        /// Supported types: float, int, Float2, Float3, Float4, Float4x4.
        /// </summary>
        /// <param name="name">The uniform name in the shader.</param>
        /// <param name="value">The uniform value.</param>
        public void SetShaderUniform(string name, object value)
        {
            _state.brush.Uniforms ??= new ShaderUniforms();
            _state.brush.Uniforms.Set(name, value);
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets multiple shader uniforms at once.
        /// </summary>
        /// <param name="uniforms">Dictionary of uniform names to values.</param>
        public void SetShaderUniforms(Dictionary<string, object> uniforms)
        {
            _state.brush.Uniforms ??= new ShaderUniforms();
            foreach (var kvp in uniforms)
                _state.brush.Uniforms.Set(kvp.Key, kvp.Value);
            InvalidateDrawState();
        }

        /// <summary>
        /// Clears the custom shader and uniforms, reverting to the default shader.
        /// </summary>
        public void ClearCustomShader()
        {
            _state.brush.Shader = null;
            _state.brush.Uniforms = null;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets a linear gradient brush for filling shapes.
        /// </summary>
        /// <param name="x1">The X coordinate of the gradient start point.</param>
        /// <param name="y1">The Y coordinate of the gradient start point.</param>
        /// <param name="x2">The X coordinate of the gradient end point.</param>
        /// <param name="y2">The Y coordinate of the gradient end point.</param>
        /// <param name="color1">The color at the start point.</param>
        /// <param name="color2">The color at the end point.</param>
        public void SetLinearBrush(float x1, float y1, float x2, float y2, Color32 color1, Color32 color2)
        {
            // Premultiply
            color1 = Color32.FromArgb(
                (byte)(color1.A),
                (byte)(color1.R * (color1.A / 255f)),
                (byte)(color1.G * (color1.A / 255f)),
                (byte)(color1.B * (color1.A / 255f)));
            color2 = Color32.FromArgb(
                (byte)(color2.A),
                (byte)(color2.R * (color2.A / 255f)),
                (byte)(color2.G * (color2.A / 255f)),
                (byte)(color2.B * (color2.A / 255f)));

            _state.brush.Type = BrushType.Linear;
            _state.brush.Color1 = color1;
            _state.brush.Color2 = color2;
            _state.brush.Point1 = new Float2(x1, y1);
            _state.brush.Point2 = new Float2(x2, y2);

            _state.brush.Transform = _state.transform;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets a radial gradient brush for filling shapes.
        /// </summary>
        /// <param name="centerX">The X coordinate of the gradient center.</param>
        /// <param name="centerY">The Y coordinate of the gradient center.</param>
        /// <param name="innerRadius">The radius at which the inner color ends.</param>
        /// <param name="outerRadius">The radius at which the outer color begins.</param>
        /// <param name="innerColor">The color at the center.</param>
        /// <param name="outerColor">The color at the outer edge.</param>
        public void SetRadialBrush(float centerX, float centerY, float innerRadius, float outerRadius, Color32 innerColor, Color32 outerColor)
        {
            // Premultiply
            innerColor = Color32.FromArgb(
                (byte)(innerColor.A),
                (byte)(innerColor.R * (innerColor.A / 255f)),
                (byte)(innerColor.G * (innerColor.A / 255f)),
                (byte)(innerColor.B * (innerColor.A / 255f)));
            outerColor = Color32.FromArgb(
                (byte)(outerColor.A),
                (byte)(outerColor.R * (outerColor.A / 255f)),
                (byte)(outerColor.G * (outerColor.A / 255f)),
                (byte)(outerColor.B * (outerColor.A / 255f)));

            _state.brush.Type = BrushType.Radial;
            _state.brush.Color1 = innerColor;
            _state.brush.Color2 = outerColor;
            _state.brush.Point1 = new Float2(centerX, centerY);
            _state.brush.Point2 = new Float2(innerRadius, outerRadius); // Store radius

            _state.brush.Transform = _state.transform;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets a box gradient brush for filling shapes with rounded corners and feathered edges.
        /// </summary>
        /// <param name="centerX">The X coordinate of the box center.</param>
        /// <param name="centerY">The Y coordinate of the box center.</param>
        /// <param name="width">The width of the box.</param>
        /// <param name="height">The height of the box.</param>
        /// <param name="radi">The corner radius of the box.</param>
        /// <param name="feather">The feather amount for soft edges.</param>
        /// <param name="innerColor">The color inside the box.</param>
        /// <param name="outerColor">The color outside the box.</param>
        public void SetBoxBrush(float centerX, float centerY, float width, float height, float radi, float feather, Color32 innerColor, Color32 outerColor)
        {
            // Premultiply
            innerColor = Color32.FromArgb(
                (byte)(innerColor.A),
                (byte)(innerColor.R * (innerColor.A / 255f)),
                (byte)(innerColor.G * (innerColor.A / 255f)),
                (byte)(innerColor.B * (innerColor.A / 255f)));
            outerColor = Color32.FromArgb(
                (byte)(outerColor.A),
                (byte)(outerColor.R * (outerColor.A / 255f)),
                (byte)(outerColor.G * (outerColor.A / 255f)),
                (byte)(outerColor.B * (outerColor.A / 255f)));

            _state.brush.Type = BrushType.Box;
            _state.brush.Color1 = innerColor;
            _state.brush.Color2 = outerColor;
            _state.brush.Point1 = new Float2(centerX, centerY);
            _state.brush.Point2 = new Float2(width / 2, height / 2); // Store half-size
            _state.brush.CornerRadii = radi;
            _state.brush.Feather = feather;

            _state.brush.Transform = _state.transform;
            InvalidateDrawState();
        }

        /// <summary>
        /// Clears the current brush, reverting to solid color fills.
        /// </summary>
        public void ClearBrush()
        {
            _state.brush.Type = BrushType.None;
            InvalidateDrawState();
        }

        /// <summary>
        /// Enables backdrop blur for subsequent fills. Any shape drawn while this is active is
        /// composited over a blurred copy of the framebuffer behind it, turning the shape into
        /// frosted glass. The shape's own fill (solid, gradient, or texture) is layered on top as a
        /// tint, so use a translucent fill for glass. This is orthogonal to the brush, toggled like
        /// any other draw state, and applies to any fillable shape. Call with 0 (or
        /// <see cref="ClearBackdropBlur"/>) to disable. Backends without support draw fills normally.
        /// </summary>
        /// <param name="radius">The blur radius in pixels. Zero disables backdrop blur.</param>
        public void SetBackdropBlur(float radius)
        {
            _state.brush.BackdropBlur = radius;
            InvalidateDrawState();
        }

        /// <summary>
        /// Disables backdrop blur for subsequent fills.
        /// </summary>
        public void ClearBackdropBlur()
        {
            _state.brush.BackdropBlur = 0f;
            InvalidateDrawState();
        }

        /// <summary>
        /// Sets the color used for filling shapes.
        /// </summary>
        /// <param name="color">The fill color.</param>
        public void SetFillColor(Color32 color) => _state.fillColor = color;


        #region Scissor Methods
        /// <summary>
        /// Sets the scissor rectangle for clipping
        /// </summary>
        public void Scissor(float x, float y, float w, float h)
        {
            w = Maths.Max(0.0f, w);
            h = Maths.Max(0.0f, h);
            // Work in logical space - conversion to pixels happens in TransformPoint
            _state.scissor = _state.transform * Transform2D.CreateTranslation(x + w * 0.5f, y + h * 0.5f);
            _state.scissorExtent.X = (w * 0.5f) * _framebufferScale;
            _state.scissorExtent.Y = (h * 0.5f) * _framebufferScale;
            InvalidateDrawState();
        }

        /// <summary>
        /// Intersects the current scissor rectangle with another rectangle
        /// </summary>
        public void IntersectScissor(float x, float y, float w, float h)
        {
            if (_state.scissorExtent.X < 0)
            {
                Scissor(x, y, w, h);
                return;
            }

            var pxform = _state.scissor;
            // Convert extents from pixel space back to logical space for intersection math
            var ex = _state.scissorExtent.X / _framebufferScale;
            var ey = _state.scissorExtent.Y / _framebufferScale;
            var invxorm = _state.transform.Inverse();
            pxform = invxorm * pxform;

            var tex = ex * Maths.Abs(pxform.A) + ey * Maths.Abs(pxform.C);
            var tey = ex * Maths.Abs(pxform.B) + ey * Maths.Abs(pxform.D);

            var rect = IntersectionOfRects(pxform.E - tex, pxform.F - tey, tex * 2, tey * 2, x, y, w, h);
            Scissor(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
        }

        /// <summary>
        /// Calculates the intersection of two rectangles
        /// </summary>
        private static Rect IntersectionOfRects(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
        {
            var minx = Maths.Max(ax, bx);
            var miny = Maths.Max(ay, by);
            var maxx = Maths.Min(ax + aw, bx + bw);
            var maxy = Maths.Min(ay + ah, by + bh);

            // Rect constructor takes (minX, minY, maxX, maxY), not (x, y, w, h)
            // Clamp so min <= max
            return new Rect(minx, miny, Maths.Max(minx, maxx), Maths.Max(miny, maxy));
        }

        /// <summary>
        /// Returns the current clip region as an axis-aligned rectangle in the active transform's
        /// local space (the same space drawing coordinates are given in). When no scissor is set the
        /// whole viewport is used. Returns false only when there is nothing to clip against (unset
        /// viewport), meaning callers should not cull. Consumers such as Paper use this to skip
        /// elements that fall entirely outside the clip.
        /// </summary>
        public bool GetCurrentClipRect(out Rect rect)
        {
            rect = default;
            var inv = _state.transform.Inverse();

            if (_state.scissorExtent.X < 0)
            {
                // No scissor: the clip is the whole viewport. Map its screen corners into local space.
                if (_width <= 0 || _height <= 0)
                    return false;
                rect = LocalBounds(inv, 0, 0, _width, _height);
                return true;
            }

            // The scissor is an axis-aligned box in the space it was set in; bring it into the current
            // local space and take its extents (mirrors the IntersectScissor math above).
            var pxform = inv * _state.scissor;
            float ex = _state.scissorExtent.X / _framebufferScale;
            float ey = _state.scissorExtent.Y / _framebufferScale;
            float tex = ex * Maths.Abs(pxform.A) + ey * Maths.Abs(pxform.C);
            float tey = ex * Maths.Abs(pxform.B) + ey * Maths.Abs(pxform.D);
            rect = new Rect(pxform.E - tex, pxform.F - tey, pxform.E + tex, pxform.F + tey);
            return true;
        }

        // Axis-aligned bounds of a screen-space rectangle mapped through 'inv' into local space.
        private static Rect LocalBounds(Transform2D inv, float minX, float minY, float maxX, float maxY)
        {
            var p0 = inv.TransformPoint(new Float2(minX, minY));
            var p1 = inv.TransformPoint(new Float2(maxX, minY));
            var p2 = inv.TransformPoint(new Float2(maxX, maxY));
            var p3 = inv.TransformPoint(new Float2(minX, maxY));
            float lx = Maths.Min(Maths.Min(p0.X, p1.X), Maths.Min(p2.X, p3.X));
            float ly = Maths.Min(Maths.Min(p0.Y, p1.Y), Maths.Min(p2.Y, p3.Y));
            float hx = Maths.Max(Maths.Max(p0.X, p1.X), Maths.Max(p2.X, p3.X));
            float hy = Maths.Max(Maths.Max(p0.Y, p1.Y), Maths.Max(p2.Y, p3.Y));
            return new Rect(lx, ly, hx, hy);
        }

        /// <summary>
        /// Resets the scissor rectangle
        /// </summary>
        public void ResetScissor()
        {
            _state.scissor = Transform2D.Identity;
            _state.scissorExtent.X = -1.0f;
            _state.scissorExtent.Y = -1.0f;
            InvalidateDrawState();
        }
        #endregion

        /// <summary>
        /// Sets the global alpha (transparency) applied to all subsequent drawing operations.
        /// </summary>
        /// <param name="alpha">The alpha value from 0 (fully transparent) to 1 (fully opaque).</param>
        public void SetGlobalAlpha(float alpha) => _globalAlpha = alpha;

        #endregion

        #region Transformation

        /// <summary>
        /// Multiplies the current transformation matrix by the specified transform.
        /// </summary>
        /// <param name="t">The transformation to apply.</param>
        public void TransformBy(Transform2D t) => _state.transform = _state.transform * t;

        /// <summary>
        /// Resets the current transformation to the identity matrix.
        /// </summary>
        public void ResetTransform() => _state.transform = Transform2D.Identity;

        /// <summary>
        /// Sets the current transformation matrix directly.
        /// </summary>
        /// <param name="xform">The transformation matrix to set.</param>
        public void CurrentTransform(Transform2D xform) => _state.transform = xform;

        /// <summary>
        /// Transforms a point from logical units to pixel coordinates, applying the current transformation.
        /// </summary>
        /// <param name="unitPoint">The point in logical units.</param>
        /// <returns>The transformed point in pixel coordinates.</returns>
        public Float2 TransformPoint(in Float2 unitPoint)
        {
            // Apply transform in logical space, then convert to pixels
            Float2 transformedUnitPoint = _state.transform.TransformPoint(unitPoint);
            return transformedUnitPoint * _framebufferScale;
        }

        /// <summary>
        /// Gets the current transformation matrix.
        /// </summary>
        /// <returns>The current transformation matrix.</returns>
        public Transform2D GetTransform() => _state.transform;

        #endregion

        #region Draw Calls

        /// <summary>
        /// Ensure that future commands are not batched as part of any existing draw call.
        /// </summary>
        public void RequestNewDrawCall()
        {
            _isNewDrawCallRequested = true;
        }

        /// <summary>
        /// Adds a vertex to the vertex buffer, applying global alpha and premultiplied alpha.
        /// </summary>
        /// <param name="vertex">The vertex to add.</param>
        public void AddVertex(Vertex vertex)
        {
            _vertices.Add(Premultiply(vertex, _globalAlpha));
        }

        /// <summary>
        /// Adds a batch of vertices at once, applying global alpha and premultiplied alpha to each.
        /// Reserves the buffer once so large meshes (strokes, glyph runs) don't grow it repeatedly.
        /// </summary>
        public void AddVertices(List<Vertex> verts)
        {
            float globalAlpha = _globalAlpha;
            Reserve(_vertices, verts.Count);
            for (int i = 0; i < verts.Count; i++)
                _vertices.Add(Premultiply(verts[i], globalAlpha));
        }

        private static Vertex Premultiply(Vertex vertex, float globalAlpha)
        {
            if (globalAlpha != 1.0f)
                vertex.a = (byte)(vertex.a * globalAlpha);

            if (vertex.a != 255)
            {
                float alpha = vertex.a / 255f;
                vertex.r = (byte)(vertex.r * alpha);
                vertex.g = (byte)(vertex.g * alpha);
                vertex.b = (byte)(vertex.b * alpha);
            }

            return vertex;
        }

        // netstandard2.1 has no List.EnsureCapacity, so grow via the Capacity setter instead.
        private static void Reserve<T>(List<T> list, int additional)
        {
            int needed = list.Count + additional;
            if (list.Capacity < needed)
                list.Capacity = needed;
        }

        /// <summary>
        /// Adds a triangle using the last three vertices added to the vertex buffer.
        /// </summary>
        public void AddTriangle() => AddTriangle(_vertices.Count - 3, _vertices.Count - 2, _vertices.Count - 1);

        /// <summary>
        /// Adds a triangle with the specified vertex indices.
        /// </summary>
        /// <param name="v1">Index of the first vertex.</param>
        /// <param name="v2">Index of the second vertex.</param>
        /// <param name="v3">Index of the third vertex.</param>
        public void AddTriangle(int v1, int v2, int v3) => AddTriangle((uint)v1, (uint)v2, (uint)v3);

        /// <summary>
        /// Adds a triangle with the specified vertex indices.
        /// </summary>
        /// <param name="v1">Index of the first vertex.</param>
        /// <param name="v2">Index of the second vertex.</param>
        /// <param name="v3">Index of the third vertex.</param>
        public void AddTriangle(uint v1, uint v2, uint v3)
        {
            // Add the triangle indices to the list
            _indices.Add(v1);
            _indices.Add(v2);
            _indices.Add(v3);

            AddTriangleCount(1);
        }

        private void AddTriangleCount(int count)
        {
            int currentHash = ComputeDrawStateHash();

            if (_drawCalls.Count == 0)
            {
                _drawCalls.Add(new DrawCall());
            }

            DrawCall lastDrawCall = _drawCalls[_drawCalls.Count - 1];

            bool isDrawStateSame = lastDrawCall.stateHash == currentHash;

            if (!isDrawStateSame || _isNewDrawCallRequested)
            {
                // If draw state has changed and the last draw call has already been used, add a new draw call
                if (lastDrawCall.ElementCount != 0)
                    _drawCalls.Add(new DrawCall());

                lastDrawCall = _drawCalls[_drawCalls.Count - 1];
                lastDrawCall.scissor = _state.scissor;
                lastDrawCall.scissorExtent = _state.scissorExtent;
                lastDrawCall.Brush = _state.brush;
                lastDrawCall.fontAtlas = _currentFontAtlas;
                // Clone uniforms to avoid reference sharing between draw calls
                if (lastDrawCall.Brush.Uniforms != null)
                    lastDrawCall.Brush.Uniforms = lastDrawCall.Brush.Uniforms.Clone();
                lastDrawCall.stateHash = currentHash;

                _isNewDrawCallRequested = false;
            }

            lastDrawCall.ElementCount += count * 3;
            _drawCalls[_drawCalls.Count - 1] = lastDrawCall;
        }

        /// <summary>
        /// Renders all accumulated draw calls using the renderer backend.
        /// Call this at the end of each frame after all drawing operations are complete.
        /// </summary>
        public void Render()
        {
            _renderer.RenderCalls(this, _drawCalls);
        }

        #endregion

        #region Path

        /// <summary>
        /// Begins a new path by emptying the list of sub-paths. Call this method when you want to create a new path.
        /// </summary>
        /// <remarks>
        /// When you call <see cref="BeginPath"/>, all previous paths are cleared and a new path is started.
        /// </remarks>
        public void BeginPath()
        {
            _subPaths.Clear();
            _currentSubPath = null;
            _isPathReady = true;
        }

        /// <summary>
        /// Moves the current position to the specified point without drawing a line.
        /// </summary>
        /// <param name="x">The x-coordinate of the point to move to.</param>
        /// <param name="y">The y-coordinate of the point to move to.</param>
        /// <remarks>
        /// This method moves the "pen" to the specified point without drawing anything.
        /// It begins a new sub-path if one doesn't already exist. Subsequent calls to
        /// <see cref="LineTo"/> will draw lines from this position.
        /// </remarks>
        public void MoveTo(float x, float y)
        {
            if (!_isPathReady)
                BeginPath();

            _currentSubPath = new SubPath(new List<Float2>());
            _currentSubPath.Points.Add(new Float2(x, y));
            _subPaths.Add(_currentSubPath);
        }

        /// <summary>
        /// Draws a line from the current position to the specified point.
        /// </summary>
        /// <param name="x">The x-coordinate of the ending point.</param>
        /// <param name="y">The y-coordinate of the ending point.</param>
        /// <remarks>
        /// This method draws a straight line from the current position to the specified position.
        /// After the line is drawn, the current position is updated to the ending point.
        /// If no position has been set previously, this method act as <see cref="MoveTo"/> with the specified coordinates.
        /// </remarks>
        public void LineTo(float x, float y)
        {
            if (_currentSubPath == null)
            {
                // HTML Canvas spec: If no current point exists, it's equivalent to a moveTo(x, y)
                MoveTo(x, y);
            }
            else
            {
                _currentSubPath.Points.Add(new Float2(x, y));
            }
        }

        /// <summary>
        /// Closes the current path by drawing a straight line from the current position to the starting point.
        /// </summary>
        /// <remarks>
        /// This method attempts to draw a line from the current position to the first point in the current path.
        /// If the path contains fewer than two points, no action is taken.
        /// After closing the path, the current position is updated to the starting point of the path.
        /// </remarks>
        public void ClosePath()
        {
            if (_currentSubPath != null && _currentSubPath.Points.Count >= 2)
            {
                // Move to the first point of the current subpath to start a new one
                Float2 firstPoint = _currentSubPath.Points[0];
                //MoveTo(firstPoint.X, firstPoint.Y);
                LineTo(firstPoint.X, firstPoint.Y);
            }
        }

        /// <summary>
        /// Sets the solidity order for the currently active path.
        /// </summary>
        public void SetSolidity(WindingMode solidity) => _state.fillMode = solidity;

        /// <summary>
        /// Adds an arc to the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the arc.</param>
        /// <param name="y">The y-coordinate of the center of the arc.</param>
        /// <param name="radius">The radius of the arc.</param>
        /// <param name="startAngle">The starting angle of the arc, in radians.</param>
        /// <param name="endAngle">The ending angle of the arc, in radians.</param>
        /// <param name="counterclockwise">If true, draws the arc counter-clockwise; otherwise, draws it clockwise.</param>
        /// <remarks>
        /// This method adds an arc to the current path, centered at the specified position with the given radius.
        /// The arc starts at startAngle and ends at endAngle, measured in radians.
        /// By default, the arc is drawn clockwise, but can be drawn counter-clockwise by setting the counterclockwise parameter to true.
        /// If no path has been started, this method will first move to the starting point of the arc.
        /// </remarks>
        public void Arc(float x, float y, float radius, float startAngle, float endAngle, bool counterclockwise = false)
        {
            Float2 center = new Float2(x, y);

            // Calculate number of segments based on radius size
            float distance = CalculateArcLength(radius, startAngle, endAngle);
            int segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));

            if (counterclockwise && startAngle < endAngle)
            {
                startAngle += Maths.PI * 2;
            }
            else if (!counterclockwise && startAngle > endAngle)
            {
                endAngle += Maths.PI * 2;
            }

            float step = counterclockwise ?
                (startAngle - endAngle) / segments :
                (endAngle - startAngle) / segments;

            // If no path has started yet, move to the first point of the arc
            if (!_isPathReady)
            {
                float firstX = x + Maths.Cos(startAngle) * radius;
                float firstY = y + Maths.Sin(startAngle) * radius;
                MoveTo(firstX, firstY);
            }

            float startX = x + Maths.Cos(startAngle) * radius;
            float startY = y + Maths.Sin(startAngle) * radius;
            LineTo(startX, startY);

            // Add arc points
            for (int i = 1; i <= segments; i++)
            {
                float angle = counterclockwise ?
                    startAngle - i * step :
                    startAngle + i * step;

                float pointX = x + Maths.Cos(angle) * radius;
                float pointY = y + Maths.Sin(angle) * radius;

                LineTo(pointX, pointY);
            }
        }

        /// <summary>
        /// Adds an arc to the path with the specified control points and radius.
        /// </summary>
        /// <param name="x1">The x-coordinate of the first control point.</param>
        /// <param name="y1">The y-coordinate of the first control point.</param>
        /// <param name="x2">The x-coordinate of the second control point.</param>
        /// <param name="y2">The y-coordinate of the second control point.</param>
        /// <param name="radius">The radius of the arc.</param>
        /// <remarks>
        /// This method creates an arc that is tangent to both the line from the current position to (x1,y1)
        /// and the line from (x1,y1) to (x2,y2) with the specified radius.
        /// If the path has not been started, this method will move to the position (x1,y1).
        /// </remarks>
        public void ArcTo(float x1, float y1, float x2, float y2, float radius)
        {
            if (!_isPathReady)
            {
                MoveTo(x1, y1);
                return;
            }

            Float2 p0 = CurrentPointInternal;
            Float2 p1 = new Float2(x1, y1);
            Float2 p2 = new Float2(x2, y2);

            // Calculate direction vectors
            Float2 v1 = p0 - p1;
            Float2 v2 = p2 - p1;

            // Normalize vectors
            float len1 = Maths.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
            float len2 = Maths.Sqrt(v2.X * v2.X + v2.Y * v2.Y);

            if (len1 < 0.0001 || len2 < 0.0001)
            {
                LineTo(x1, y1);
                return;
            }

            v1 /= len1;
            v2 /= len2;

            // Calculate angle and tangent points
            float angle = Maths.Acos(v1.X * v2.X + v1.Y * v2.Y);
            float tan = radius * Maths.Tan(angle / 2);

            if (float.IsNaN(tan) || tan < 0.0001)
            {
                LineTo(x1, y1);
                return;
            }

            // Calculate tangent points
            Float2 t1 = p1 + v1 * tan;
            Float2 t2 = p1 + v2 * tan;

            // Draw line to first tangent point
            LineTo(t1.X, t1.Y);

            // Calculate arc center and angles
            float d = radius / Maths.Sin(angle / 2);
            Float2 middle = (v1 + v2);
            middle /= Maths.Sqrt(middle.X * middle.X + middle.Y * middle.Y);
            Float2 center = p1 + middle * d;

            // Calculate angles for the arc
            Float2 a1 = t1 - center;
            Float2 a2 = t2 - center;
            float startAngle = Maths.Atan2(a1.Y, a1.X);
            float endAngle = Maths.Atan2(a2.Y, a2.X);

            // Draw the arc
            Arc(center.X, center.Y, radius, startAngle, endAngle, (v1.X * v2.Y - v1.Y * v2.X) < 0);
        }

        /// <summary>
        /// Adds an elliptical arc to the path with the specified control points and radius.
        /// </summary>
        /// <param name="rx">The x-axis radius of the ellipse.</param>
        /// <param name="ry">The y-axis radius of the ellipse.</param>
        /// <param name="xAxisRotation">The x-coordinate of the second control point.</param>
        /// <param name="largeArcFlag">If largeArcFlag is '1', then one of the two larger arc sweeps will be chosen; otherwise, if largeArcFlag is '0', one of the smaller arc sweeps will be chosen.</param>
        /// <param name="sweepFlag">If sweepFlag is '1', then the arc will be drawn in a "positive-angle" direction. A value of 0 causes the arc to be drawn in a "negative-angle" direction</param>
        /// <param name="x">The x-coordinate of the endpoint.</param>
        /// <param name="y">The y-coordinate of the endpoint.</param>
        /// <remarks>
        /// This method creates an elliptical arc with radii (rx,ry) from current point to (x_end,y_end)
        /// </remarks>
        public void EllipticalArcTo(float rx, float ry, float xAxisRotationDegrees, bool largeArcFlag, bool sweepFlag, float x_end, float y_end)
        {
            float x = CurrentPointInternal.X;
            float y = CurrentPointInternal.Y;

            // Ensure radii are positive
            float rx_abs = Maths.Abs(rx);
            float ry_abs = Maths.Abs(ry);

            // If rx or ry is zero, or if start and end points are the same, treat as a line segment (or do nothing if start=end)
            if (rx_abs == 0 || ry_abs == 0)
            {
                LineTo(x_end, y_end);
                return;
            }

            if (x == x_end && y == y_end)
            {
                // No arc to draw, points are identical
                return;
            }

            float phi = xAxisRotationDegrees * (Maths.PI / 180.0f); // Convert degrees to radians
            float cosPhi = Maths.Cos(phi);
            float sinPhi = Maths.Sin(phi);

            // Step 1: Compute (x1', y1') - coordinates of p1 transformed relative to p_end
            float dx_half = (x - x_end) / 2.0f;
            float dy_half = (y - y_end) / 2.0f;

            float x1_prime = cosPhi * dx_half + sinPhi * dy_half;
            float y1_prime = -sinPhi * dx_half + cosPhi * dy_half;

            // Step 2: Ensure radii are large enough
            float rx_sq = rx_abs * rx_abs;
            float ry_sq = ry_abs * ry_abs;
            float x1_prime_sq = x1_prime * x1_prime;
            float y1_prime_sq = y1_prime * y1_prime;

            float radii_check = (x1_prime_sq / rx_sq) + (y1_prime_sq / ry_sq);
            if (radii_check > 1.0)
            {
                float scaleFactor = Maths.Sqrt(radii_check);
                rx_abs *= scaleFactor;
                ry_abs *= scaleFactor;
                rx_sq = rx_abs * rx_abs; // Update squared radii
                ry_sq = ry_abs * ry_abs;
            }

            // Step 3: Compute (cx', cy') - center of ellipse in transformed (prime) coordinates
            float term_numerator = (rx_sq * ry_sq) - (rx_sq * y1_prime_sq) - (ry_sq * x1_prime_sq);
            float term_denominator = (rx_sq * y1_prime_sq) + (ry_sq * x1_prime_sq);

            float term_sqrt_arg = 0;
            if (term_denominator != 0) // Avoid division by zero
                term_sqrt_arg = term_numerator / term_denominator;

            term_sqrt_arg = Maths.Max(0, term_sqrt_arg); // Clamp to avoid issues with floating point inaccuracies

            float sign_coef = (largeArcFlag == sweepFlag) ? -1.0f : 1.0f;
            float coef = sign_coef * Maths.Sqrt(term_sqrt_arg);

            float cx_prime = coef * ((rx_abs * y1_prime) / ry_abs);
            float cy_prime = coef * -((ry_abs * x1_prime) / rx_abs);

            // Step 4: Compute (cx, cy) - center of ellipse in original coordinates
            float x_mid = (x + x_end) / 2.0f;
            float y_mid = (y + y_end) / 2.0f;

            float cx = cosPhi * cx_prime - sinPhi * cy_prime + x_mid;
            float cy = sinPhi * cx_prime + cosPhi * cy_prime + y_mid;

            // Step 5: Compute startAngle (theta1) and extentAngle (deltaTheta)
            float vec_start_x = (x1_prime - cx_prime) / rx_abs;
            float vec_start_y = (y1_prime - cy_prime) / ry_abs;
            float vec_end_x = (-x1_prime - cx_prime) / rx_abs;
            float vec_end_y = (-y1_prime - cy_prime) / ry_abs;

            float theta1 = CalculateVectorAngle(1, 0, vec_start_x, vec_start_y);
            float deltaTheta = CalculateVectorAngle(vec_start_x, vec_start_y, vec_end_x, vec_end_y);

            if (!sweepFlag && deltaTheta > 0)
            {
                deltaTheta -= 2 * Maths.PI;
            }
            else if (sweepFlag && deltaTheta < 0)
            {
                deltaTheta += 2 * Maths.PI;
            }

            // Step 6: Draw the arc using line segments
            float estimatedArcLength = Maths.Abs(deltaTheta) * (rx_abs + ry_abs) / 2.0f;
            int segments = Maths.Max(1, (int)Maths.Ceiling(estimatedArcLength / _state.roundingMinDistance));
            if (Maths.Abs(deltaTheta) > 1e-9 && segments == 0) segments = 1; // Ensure at least one segment for tiny arcs

            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = theta1 + deltaTheta * t;

                float cosAngle = Maths.Cos(angle);
                float sinAngle = Maths.Sin(angle);

                float ellipse_pt_x_prime = rx_abs * cosAngle;
                float ellipse_pt_y_prime = ry_abs * sinAngle;

                float final_x = cosPhi * ellipse_pt_x_prime - sinPhi * ellipse_pt_y_prime + cx;
                float final_y = sinPhi * ellipse_pt_x_prime + cosPhi * ellipse_pt_y_prime + cy;

                if (i == segments)
                {
                    LineTo(x_end, y_end); // Ensure final point is exact
                }
                else
                {
                    LineTo(final_x, final_y);
                }
            }
        }

        /// <summary>
        /// Adds a cubic Bézier curve to the path from the current position to the specified end point.
        /// </summary>
        /// <param name="cp1x">The x-coordinate of the first control point.</param>
        /// <param name="cp1y">The y-coordinate of the first control point.</param>
        /// <param name="cp2x">The x-coordinate of the second control point.</param>
        /// <param name="cp2y">The y-coordinate of the second control point.</param>
        /// <param name="x">The x-coordinate of the end point.</param>
        /// <param name="y">The y-coordinate of the end point.</param>
        /// <remarks>
        /// This method adds a cubic Bézier curve to the current path, using the specified control points.
        /// The curve starts at the current position and ends at (x,y).
        /// If no current position exists, this method will move to the end point without drawing a curve.
        /// </remarks>
        public void BezierCurveTo(float cp1x, float cp1y, float cp2x, float cp2y, float x, float y)
        {
            if (!_isPathReady)
            {
                MoveTo(x, y);
                return;
            }

            //Float2 p1 = _currentSubPath!.Points[^1];
            Float2 p1 = CurrentPointInternal;
            Float2 p2 = new Float2(cp1x, cp1y);
            Float2 p3 = new Float2(cp2x, cp2y);
            Float2 p4 = new Float2(x, y);

            PathBezierToCasteljau(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, p4.X, p4.Y, _state.tess_tol, 0);
        }

        private void PathBezierToCasteljau(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, float tess_tol, int level)
        {
            float dx = x4 - x1;
            float dy = y4 - y1;
            float d2 = (x2 - x4) * dy - (y2 - y4) * dx;
            float d3 = (x3 - x4) * dy - (y3 - y4) * dx;

            d2 = d2 >= 0 ? d2 : -d2;
            d3 = d3 >= 0 ? d3 : -d3;
            if ((d2 + d3) * (d2 + d3) < tess_tol * (dx * dx + dy * dy))
            {
                _currentSubPath.Points.Add(new Float2(x4, y4));
            }
            else if (level < 10)
            {
                float x12 = (x1 + x2) * 0.5f, y12 = (y1 + y2) * 0.5f;
                float x23 = (x2 + x3) * 0.5f, y23 = (y2 + y3) * 0.5f;
                float x34 = (x3 + x4) * 0.5f, y34 = (y3 + y4) * 0.5f;
                float x123 = (x12 + x23) * 0.5f, y123 = (y12 + y23) * 0.5f;
                float x234 = (x23 + x34) * 0.5f, y234 = (y23 + y34) * 0.5f;
                float x1234 = (x123 + x234) * 0.5f, y1234 = (y123 + y234) * 0.5f;

                PathBezierToCasteljau(x1, y1, x12, y12, x123, y123, x1234, y1234, tess_tol, level + 1);
                PathBezierToCasteljau(x1234, y1234, x234, y234, x34, y34, x4, y4, tess_tol, level + 1);
            }
        }

        /// <summary>
        /// Adds a quadratic Bézier curve to the path from the current position to the specified end point.
        /// </summary>
        /// <param name="cpx">The x-coordinate of the control point.</param>
        /// <param name="cpy">The y-coordinate of the control point.</param>
        /// <param name="x">The x-coordinate of the end point.</param>
        /// <param name="y">The y-coordinate of the end point.</param>
        /// <remarks>
        /// This method adds a quadratic Bézier curve to the current path, using the specified control point.
        /// The curve starts at the current position and ends at (x,y).
        /// If no current position exists, this method will move to the end point without drawing a curve.
        /// Internally, this method converts the quadratic Bézier curve to a cubic Bézier curve.
        /// </remarks>
        public void QuadraticCurveTo(float cpx, float cpy, float x, float y)
        {
            if (!_isPathReady)
            {
                MoveTo(x, y);
                return;
            }

            Float2 p1 = CurrentPointInternal;
            Float2 p2 = new Float2(cpx, cpy);
            Float2 p3 = new Float2(x, y);

            // Convert quadratic curve to cubic bezier
            float cp1x = p1.X + 2.0f / 3.0f * (p2.X - p1.X);
            float cp1y = p1.Y + 2.0f / 3.0f * (p2.Y - p1.Y);
            float cp2x = p3.X + 2.0f / 3.0f * (p2.X - p3.X);
            float cp2y = p3.Y + 2.0f / 3.0f * (p2.Y - p3.Y);

            BezierCurveTo(cp1x, cp1y, cp2x, cp2y, x, y);
        }

        #endregion

        /// <summary>
        /// Fills the current path using the current fill color.
        /// Uses a simple fan-based fill algorithm suitable for convex shapes.
        /// </summary>
        public void Fill()
        {
            if (_subPaths.Count == 0)
                return;

            // Fill all sub-paths individually
            foreach (var subPath in _subPaths)
                FillSubPath(subPath);
        }

        /// <summary>
        /// Fills complex paths with anti-aliasing using tessellation.
        /// Combines FillComplex with an outline stroke for smooth edges.
        /// </summary>
        public void FillComplexAA()
        {
            FillComplex();

            // Stroke with same color as Fill
            SaveState();
            SetStrokeColor(_state.fillColor);
            SetStrokeWidth(1);
            SetStrokeScale(1f);
            SetStrokeJoint(JointStyle.Bevel);
            SetStrokeCap(EndCapStyle.Butt);

            Stroke();

            RestoreState();
        }

        /// <summary>
        /// Fills complex or self-intersecting paths using LibTess tessellation.
        /// Supports both OddEven and NonZero winding rules.
        /// </summary>
        public void FillComplex()
        {
            if (_subPaths.Count == 0)
                return;

            var tess = new Tess();
            foreach (var path in _subPaths)
            {
                var copy = path.Points.ToArray();
                for (int i = 0; i < copy.Length; i++)
                    // True pixel-space positions: geometric-fringe AA centres coverage on the real
                    // edge, so no half-pixel grid nudge is needed (and it would offset the fill
                    // relative to strokes and convex fills, which use true positions too).
                    copy[i] = TransformPoint(copy[i]);
                var points = copy.Select(v => new ContourVertex() { Position = new Vec3() { X = v.X, Y = v.Y } }).ToArray();

                tess.AddContour(points, ContourOrientation.Original);
            }
            tess.Tessellate(_state.fillMode == WindingMode.OddEven ? WindingRule.EvenOdd : WindingRule.NonZero, ElementType.Polygons, 3);

            var indices = tess.Elements;
            var vertices = tess.Vertices;

            // Create vertices and triangles
            uint startVertexIndex = (uint)_vertices.Count;
            Reserve(_vertices, vertices.Length);
            Reserve(_indices, indices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                Float2 pos = new Float2(vertex.Position.X, vertex.Position.Y);
                // uv.x = 1 -> full coverage. FillComplex itself has no fringe; FillComplexAA adds a
                // fringed outline via Stroke().
                AddVertex(new Vertex(pos, new Float2(1f, 1f), _state.fillColor));
            }
            // Create triangles
            for (int i = 0; i < indices.Length; i += 3)
            {
                uint v1 = (uint)(startVertexIndex + indices[i]);
                uint v2 = (uint)(startVertexIndex + indices[i + 1]);
                uint v3 = (uint)(startVertexIndex + indices[i + 2]);
                AddTriangle(v1, v3, v2);
            }
        }


        private void FillSubPath(SubPath subPath)
        {
            if (subPath.Points.Count < 3)
                return;

            _fillRing.Clear();
            for (int i = 0; i < subPath.Points.Count; i++)
                _fillRing.Add(TransformPoint(subPath.Points[i]));

            EmitConvexFillAA(_fillRing, _state.fillColor);
        }

        // Reusable scratch buffers for the convex fringe-fill helper (avoid per-call allocation).
        private readonly List<Float2> _fillRing = new List<Float2>();
        private readonly List<Float2> _fillMiters = new List<Float2>();
        private readonly List<Float2> _fillEdges = new List<Float2>();

        /// <summary>
        /// Emits an anti-aliased convex (star-convex) fill for a ring of points already in
        /// physical-pixel space. The solid core is inset by half a pixel (coverage 1) and a
        /// one-pixel fringe ribbon fades to coverage 0 at the outer edge. Coverage is carried in
        /// uv.x and multiplied in by the shader after the brush, so gradient and textured fills
        /// stay anti-aliased.
        /// </summary>
        private void EmitConvexFillAA(List<Float2> ring, Color32 color)
        {
            int n = ring.Count;
            // Drop any duplicated closing point(s) the caller left in.
            while (n >= 2 && Float2.LengthSquared(ring[0] - ring[n - 1]) < 1e-6f)
                n--;
            if (n < 3)
                return;

            const float halfPixel = 0.5f; // inset/outset -> 1px fringe centred on the nominal edge

            Float2 centroid = Float2.Zero;
            for (int i = 0; i < n; i++)
                centroid += ring[i];
            centroid /= (float)n;

            // Precompute each edge's unit direction once (edge i goes from ring[i] to ring[i+1]),
            // so each vertex reuses its two adjacent edges instead of normalizing both afresh.
            var edges = _fillEdges;
            edges.Clear();
            for (int i = 0; i < n; i++)
                edges.Add(NormalizeSafe(ring[(i + 1) % n] - ring[i]));

            // Per-vertex outward miter offset. miter = (nPrev + nNext) / (1 + dot(nPrev, nNext))
            // keeps a constant perpendicular offset along both adjacent edges (nPrev/nNext are unit
            // edge normals).
            var miters = _fillMiters;
            miters.Clear();
            for (int i = 0; i < n; i++)
            {
                Float2 dPrev = edges[(i - 1 + n) % n];
                Float2 dNext = edges[i];
                Float2 nPrev = new Float2(-dPrev.Y, dPrev.X);
                Float2 nNext = new Float2(-dNext.Y, dNext.X);

                Float2 m = nPrev + nNext;
                double denom = 1.0 + (nPrev.X * nNext.X + nPrev.Y * nNext.Y);
                if (denom > 1e-3)
                    m /= (float)denom;
                else
                    m = nPrev; // near 180deg fold: fall back to a single edge normal

                double mLen = Float2.Length(m);
                if (mLen > 4.0)
                    m *= (float)(4.0 / mLen); // clamp so sharp corners don't spike the fringe
                miters.Add(m);
            }

            // Orient the miters outward (consistent handedness across the convex ring): test the
            // vertex farthest from the centroid, where outward unambiguously points away from it.
            int far = 0; double farDist = -1;
            for (int i = 0; i < n; i++)
            {
                double d = Float2.LengthSquared(ring[i] - centroid);
                if (d > farDist) { farDist = d; far = i; }
            }
            if (miters[far].X * (ring[far].X - centroid.X) + miters[far].Y * (ring[far].Y - centroid.Y) < 0)
                for (int i = 0; i < n; i++)
                    miters[i] = -miters[i];

            uint baseIndex = (uint)_vertices.Count;
            Float2 coreUV = new Float2(1f, 0f);
            Float2 fringeUV = new Float2(0f, 0f);

            // Centroid fan apex (full coverage), then per-vertex inner (core) and outer (fringe).
            AddVertex(new Vertex(centroid, coreUV, color));
            for (int i = 0; i < n; i++)
            {
                Float2 offset = miters[i] * halfPixel;
                AddVertex(new Vertex(ring[i] - offset, coreUV, color));   // inner: base + 1 + 2i
                AddVertex(new Vertex(ring[i] + offset, fringeUV, color)); // outer: base + 2 + 2i
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                uint inner0 = baseIndex + 1 + (uint)(i * 2);
                uint outer0 = baseIndex + 2 + (uint)(i * 2);
                uint inner1 = baseIndex + 1 + (uint)(next * 2);
                uint outer1 = baseIndex + 2 + (uint)(next * 2);

                // Core fan triangle.
                _indices.Add(baseIndex);
                _indices.Add(inner0);
                _indices.Add(inner1);

                // Fringe ribbon quad (inner0 -> outer0 -> outer1 -> inner1).
                _indices.Add(inner0);
                _indices.Add(outer0);
                _indices.Add(outer1);
                _indices.Add(inner0);
                _indices.Add(outer1);
                _indices.Add(inner1);
            }

            AddTriangleCount(n * 3);
        }

        private static Float2 NormalizeSafe(Float2 v)
        {
            double len2 = v.X * v.X + v.Y * v.Y;
            if (len2 < 1e-12)
                return Float2.Zero;
            double inv = 1.0 / Maths.Sqrt(len2);
            return new Float2((float)(v.X * inv), (float)(v.Y * inv));
        }

        /// <summary>
        /// Strokes the current path using the current stroke color, width, and style settings.
        /// </summary>
        public void Stroke()
        {
            if (_subPaths.Count == 0)
                return;

            // Stroke all sub-paths
            foreach (var subPath in _subPaths)
                StrokeSubPath(subPath);
        }

        private void StrokeSubPath(SubPath subPath)
        {
            if (subPath.Points.Count < 2)
                return;

            _strokePoints.Clear();
            for (int i = 0; i < subPath.Points.Count; i++)
                _strokePoints.Add(TransformPoint(subPath.Points[i]));

            // Geometry is in physical pixels, so the fringe is one physical pixel wide.
            float pixelStrokeWidth = (_state.strokeWidth * _state.strokeScale) * _framebufferScale;
            PolylineMesher.Create(_strokePoints, pixelStrokeWidth, 1.0f, _state.strokeColor,
                _state.strokeJoint, _state.miterLimit, _state.strokeStartCap, _state.strokeEndCap,
                _state.roundingMinDistance * _framebufferScale, out var verts, out var idxs);

            if (idxs.Count == 0)
                return;

            uint startVertexIndex = (uint)_vertices.Count;
            AddVertices(verts);
            Reserve(_indices, idxs.Count);
            for (int i = 0; i < idxs.Count; i++)
                _indices.Add(startVertexIndex + idxs[i]);

            AddTriangleCount(idxs.Count / 3);
        }

        // Reusable scratch buffer for stroke points in physical-pixel space.
        private readonly List<Float2> _strokePoints = new List<Float2>();

        /// <summary>
        /// Fills and then strokes the current path using the current fill and stroke settings.
        /// </summary>
        public void FillAndStroke()
        {
            Fill();
            Stroke();
        }

        #region Primitives (Path-Based)

        /// <summary>
        /// Creates a Closed Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        public void Rect(float x, float y, float width, float height)
        {
            if (width <= 0 || height <= 0)
                return;

            BeginPath();
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Rounded Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="radius">The radius of the corners.</param>
        public void RoundedRect(float x, float y, float width, float height, float radius)
        {
            RoundedRect(x, y, width, height, radius, radius, radius, radius);
        }

        /// <summary>
        /// Creates a Closed Rounded Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="tlRadii">The radius of the top-left corner.</param>
        /// <param name="trRadii">The radius of the top-right corner.</param>
        /// <param name="brRadii">The radius of the bottom-right corner.</param>
        /// <param name="blRadii">The radius of the bottom-left corner.</param>
        public void RoundedRect(float x, float y, float width, float height, float tlRadii, float trRadii, float brRadii, float blRadii)
        {
            if (width <= 0 || height <= 0)
                return;

            // Clamp radii to half of the smaller dimension to prevent overlap
            float maxRadius = Maths.Min(width, height) / 2;
            tlRadii = Maths.Min(tlRadii, maxRadius);
            trRadii = Maths.Min(trRadii, maxRadius);
            brRadii = Maths.Min(brRadii, maxRadius);
            blRadii = Maths.Min(blRadii, maxRadius);

            BeginPath();
            // Top-left corner
            MoveTo(x + tlRadii, y);
            // Top edge and top-right corner
            LineTo(x + width - trRadii, y);
            Arc(x + width - trRadii, y + trRadii, trRadii, -Maths.PI / 2, 0, false);
            // Right edge and bottom-right corner
            LineTo(x + width, y + height - brRadii);
            Arc(x + width - brRadii, y + height - brRadii, brRadii, 0, Maths.PI / 2, false);
            // Bottom edge and bottom-left corner
            LineTo(x + blRadii, y + height);
            Arc(x + blRadii, y + height - blRadii, blRadii, Maths.PI / 2, Maths.PI, false);
            // Left edge and top-left corner
            LineTo(x, y + tlRadii);
            Arc(x + tlRadii, y + tlRadii, tlRadii, Maths.PI, 3 * Maths.PI / 2, false);
            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Circle Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        public void Circle(float x, float y, float radius, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                float distance = Maths.PI * 2 * radius;
                segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 3)
                return;

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                float angle = 2 * Maths.PI * i / segments;
                float vx = x + radius * Maths.Cos(angle);
                float vy = y + radius * Maths.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Ellipse Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="rx">The x-axis radius of the ellipse.</param>
        /// <param name="ry">The y-axis radius of the ellipse.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        public void Ellipse(float x, float y, float rx, float ry, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                float distance = Maths.PI * 2 * Maths.Max(rx, ry);
                segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));
            }

            if (rx <= 0 || ry <= 0 || segments < 3)
                return;

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                float angle = 2 * Maths.PI * i / segments;
                float vx = x + rx * Maths.Cos(angle);
                float vy = y + ry * Maths.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Pie Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the pie.</param>
        /// <param name="y">The y-coordinate of the center of the pie.</param>
        /// <param name="radius">The radius of the pie.</param>
        /// <param name="startAngle">The starting angle in radians.</param>
        /// <param name="endAngle">The ending angle in radians.</param>
        /// <param name="segments">The number of segments used to approximate the curved edge. Higher values create smoother curves.</param>
        public void Pie(float x, float y, float radius, float startAngle, float endAngle, int segments = -1)
        {
            if (segments == -1)
            {
                float distance = CalculateArcLength(radius, startAngle, endAngle);
                segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 1)
                return;

            // Ensure angles are ordered correctly
            if (endAngle < startAngle)
                endAngle += 2 * Maths.PI;

            // Calculate angle range
            float angleRange = endAngle - startAngle;
            float segmentAngle = angleRange / segments;

            // Start path
            BeginPath();
            MoveTo(x, y);

            // Generate vertices around the arc plus the two radial endpoints
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + i * segmentAngle;
                float vx = x + radius * Maths.Cos(angle);
                float vy = y + radius * Maths.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        #endregion


        #region Primitives (Shader-Based AA)

        /// <summary>
        /// Paints a Hardware-accelerated rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rectangle.</remarks>
        public void RectFilled(float x, float y, float width, float height, Color32 color)
        {
            if (width <= 0 || height <= 0)
                return;

            // Dedicated fast path (no sqrt/centroid/scratch lists): an inset solid core plus a
            // one-pixel fringe frame. The core is inset half a pixel and the fringe extends half a
            // pixel outside the edge; coverage rides in uv.x (1 = core, 0 = outer fringe). The inset
            // is clamped to the rect's half-extent so sub-pixel rects collapse instead of inverting.
            // The fringe is expressed in logical units scaled down by the transform so it stays ~1
            // physical pixel on screen at any zoom (see FringeHalfLogical).
            float hp = FringeHalfLogical();
            float hpx = Maths.Min(hp, width * 0.5f);
            float hpy = Maths.Min(hp, height * 0.5f);

            Float2 i0 = TransformPoint(new Float2(x + hpx, y + hpy));
            Float2 i1 = TransformPoint(new Float2(x + width - hpx, y + hpy));
            Float2 i2 = TransformPoint(new Float2(x + width - hpx, y + height - hpy));
            Float2 i3 = TransformPoint(new Float2(x + hpx, y + height - hpy));
            Float2 o0 = TransformPoint(new Float2(x - hp, y - hp));
            Float2 o1 = TransformPoint(new Float2(x + width + hp, y - hp));
            Float2 o2 = TransformPoint(new Float2(x + width + hp, y + height + hp));
            Float2 o3 = TransformPoint(new Float2(x - hp, y + height + hp));

            uint b = (uint)_vertices.Count;
            Float2 core = new Float2(1f, 0f);
            Float2 fringe = new Float2(0f, 0f);
            AddVertex(new Vertex(i0, core, color));
            AddVertex(new Vertex(i1, core, color));
            AddVertex(new Vertex(i2, core, color));
            AddVertex(new Vertex(i3, core, color));
            AddVertex(new Vertex(o0, fringe, color));
            AddVertex(new Vertex(o1, fringe, color));
            AddVertex(new Vertex(o2, fringe, color));
            AddVertex(new Vertex(o3, fringe, color));

            // Solid core (2 triangles).
            _indices.Add(b); _indices.Add(b + 1); _indices.Add(b + 2);
            _indices.Add(b); _indices.Add(b + 2); _indices.Add(b + 3);
            // Fringe frame (4 edges, 2 triangles each).
            for (uint e = 0; e < 4; e++)
            {
                uint nx = (e + 1) & 3;
                uint inE = b + e, inN = b + nx, outE = b + 4 + e, outN = b + 4 + nx;
                _indices.Add(inE); _indices.Add(outE); _indices.Add(outN);
                _indices.Add(inE); _indices.Add(outN); _indices.Add(inN);
            }

            AddTriangleCount(10);
        }

        /// <summary>
        /// Paints a Hardware-accelerated rounded rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="width">The width of the rounded rectangle.</param>
        /// <param name="height">The height of the rounded rectangle.</param>
        /// <param name="radius">The radius of the corners.</param>
        /// <param name="color">The color of the rounded rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rounded rectangle.</remarks>
        public void RoundedRectFilled(float x, float y, float width, float height,
                                     float radius, Color32 color)
        {
            RoundedRectFilled(x, y, width, height, radius, radius, radius, radius, color);
        }

        /// <summary>
        /// Paints a Hardware-accelerated rounded rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="width">The width of the rounded rectangle.</param>
        /// <param name="height">The height of the rounded rectangle.</param>
        /// <param name="tlRadii">The radius of the top-left corner.</param>
        /// <param name="trRadii">The radius of the top-right corner.</param>
        /// <param name="brRadii">The radius of the bottom-right corner.</param>
        /// <param name="blRadii">The radius of the bottom-left corner.</param>
        /// <param name="color">The color of the rounded rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rounded rectangle.</remarks>
        public void RoundedRectFilled(float x, float y, float width, float height,
                                     float tlRadii, float trRadii, float brRadii, float blRadii,
                                     Color32 color)
        {
            if (width <= 0 || height <= 0)
                return;

            // Clamp radii to half of the smaller dimension to prevent overlap
            float maxRadius = Maths.Min(width, height) / 2;
            tlRadii = Maths.Min(tlRadii, maxRadius);
            trRadii = Maths.Min(trRadii, maxRadius);
            brRadii = Maths.Min(brRadii, maxRadius);
            blRadii = Maths.Min(blRadii, maxRadius);

            // Dedicated fast path. Every outline vertex sits on a corner arc whose outward direction
            // is its radial (cos, sin) - or, for a square corner, the diagonal - so no per-vertex
            // normalize is needed. The solid core is inset half a pixel and a one-pixel fringe ribbon
            // fades to coverage 0 (carried in uv.x). Positions use a precomputed transformed basis, so
            // only a few full matrix transforms are needed. The fringe is scaled down by the transform
            // so it stays ~1 physical pixel on screen at any zoom (see FringeHalfLogical).
            float hp = FringeHalfLogical();

            int tlSegments = tlRadii > 0 ? Maths.Max(1, (int)Maths.Ceiling(Maths.PI * tlRadii / 2 / _state.roundingMinDistance)) : 0;
            int trSegments = trRadii > 0 ? Maths.Max(1, (int)Maths.Ceiling(Maths.PI * trRadii / 2 / _state.roundingMinDistance)) : 0;
            int brSegments = brRadii > 0 ? Maths.Max(1, (int)Maths.Ceiling(Maths.PI * brRadii / 2 / _state.roundingMinDistance)) : 0;
            int blSegments = blRadii > 0 ? Maths.Max(1, (int)Maths.Ceiling(Maths.PI * blRadii / 2 / _state.roundingMinDistance)) : 0;

            // Transform basis about the rect centre (affine: T(p) = c + (p - centre) . [ex, ey]).
            float ccx = x + width / 2, ccy = y + height / 2;
            Float2 c = TransformPoint(new Float2(ccx, ccy));
            Float2 ex = TransformPoint(new Float2(ccx + 1, ccy)) - c;
            Float2 ey = TransformPoint(new Float2(ccx, ccy + 1)) - c;

            Float2 coreUV = new Float2(1f, 0f);
            Float2 fringeUV = new Float2(0f, 0f);
            uint b = (uint)_vertices.Count;
            int ringCount = 0;

            Float2 ToPx(double px, double py) => c + ex * (float)(px - ccx) + ey * (float)(py - ccy);

            // Appends one corner's (inner core, outer fringe) vertex pairs and advances ringCount.
            void EmitCorner(double cxc, double cyc, double radius, double startAngle, int segs,
                            double sharpX, double sharpY, float sgnX, float sgnY)
            {
                if (radius > 0)
                {
                    double innerR = radius - hp; if (innerR < 0) innerR = 0;
                    double outerR = radius + hp;
                    double da = (Math.PI / 2) / segs;
                    double dgx = Math.Cos(startAngle), dgy = Math.Sin(startAngle);
                    double cda = Math.Cos(da), sda = Math.Sin(da);
                    for (int j = 0; j <= segs; j++)
                    {
                        AddVertex(new Vertex(ToPx(cxc + innerR * dgx, cyc + innerR * dgy), coreUV, color));
                        AddVertex(new Vertex(ToPx(cxc + outerR * dgx, cyc + outerR * dgy), fringeUV, color));
                        ringCount++;
                        double ndgx = dgx * cda - dgy * sda, ndgy = dgx * sda + dgy * cda;
                        dgx = ndgx; dgy = ndgy;
                    }
                }
                else
                {
                    // Square corner: half a pixel along each axis gives a 0.5px offset to both edges.
                    AddVertex(new Vertex(ToPx(sharpX - sgnX * hp, sharpY - sgnY * hp), coreUV, color));
                    AddVertex(new Vertex(ToPx(sharpX + sgnX * hp, sharpY + sgnY * hp), fringeUV, color));
                    ringCount++;
                }
            }

            AddVertex(new Vertex(c, coreUV, color)); // index 0: fan apex

            // Corners in ring order (TL -> TR -> BR -> BL), each arc sweeping +90 degrees.
            EmitCorner(x + tlRadii, y + tlRadii, tlRadii, Maths.PI, tlSegments, x, y, -1f, -1f);
            EmitCorner(x + width - trRadii, y + trRadii, trRadii, Maths.PI * 1.5f, trSegments, x + width, y, 1f, -1f);
            EmitCorner(x + width - brRadii, y + height - brRadii, brRadii, 0f, brSegments, x + width, y + height, 1f, 1f);
            EmitCorner(x + blRadii, y + height - blRadii, blRadii, Maths.PI * 0.5f, blSegments, x, y + height, -1f, 1f);

            for (int k = 0; k < ringCount; k++)
            {
                int next = (k + 1) % ringCount;
                uint inner0 = b + 1 + (uint)(k * 2);
                uint outer0 = b + 2 + (uint)(k * 2);
                uint inner1 = b + 1 + (uint)(next * 2);
                uint outer1 = b + 2 + (uint)(next * 2);

                _indices.Add(b); _indices.Add(inner0); _indices.Add(inner1);          // core fan
                _indices.Add(inner0); _indices.Add(outer0); _indices.Add(outer1);     // fringe
                _indices.Add(inner0); _indices.Add(outer1); _indices.Add(inner1);
            }

            AddTriangleCount(ringCount * 3);
        }

        /// <summary>
        /// Paints a circle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="color">The color of the circle.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        /// <remarks>This is significantly faster than using the path API to draw a circle.</remarks>
        public void CircleFilled(float x, float y, float radius, Color32 color, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                float distance = Maths.PI * 2 * radius;
                segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 3)
                return;

            // Dedicated fast path: the outward direction at each vertex is just the radial unit
            // vector, so no per-vertex normalize is needed. The solid core is inset half a pixel and
            // a one-pixel fringe ribbon fades to coverage 0 (carried in uv.x). Positions use a
            // precomputed transformed basis, so only the centre needs a full matrix transform. The
            // fringe is scaled down by the transform so it stays ~1 physical pixel at any zoom.
            float hp = FringeHalfLogical();
            float innerR = Maths.Max(0f, radius - hp);
            float outerR = radius + hp;

            Float2 center = TransformPoint(new Float2(x, y));
            Float2 ex = TransformPoint(new Float2(x + 1, y)) - center; // transformed +X axis (px/unit)
            Float2 ey = TransformPoint(new Float2(x, y + 1)) - center; // transformed +Y axis (px/unit)

            uint b = (uint)_vertices.Count;
            Float2 coreUV = new Float2(1f, 0f);
            Float2 fringeUV = new Float2(0f, 0f);

            AddVertex(new Vertex(center, coreUV, color)); // index 0: fan apex

            double step = 2 * Math.PI / segments;
            double cs = Math.Cos(step), sn = Math.Sin(step);
            double dx = 1.0, dy = 0.0;
            for (int i = 0; i < segments; i++)
            {
                Float2 radial = ex * (float)dx + ey * (float)dy; // outward direction in pixel space
                AddVertex(new Vertex(center + radial * innerR, coreUV, color));   // 1 + 2i
                AddVertex(new Vertex(center + radial * outerR, fringeUV, color)); // 2 + 2i
                double ndx = dx * cs - dy * sn;
                double ndy = dx * sn + dy * cs;
                dx = ndx; dy = ndy;
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                uint inner0 = b + 1 + (uint)(i * 2);
                uint outer0 = b + 2 + (uint)(i * 2);
                uint inner1 = b + 1 + (uint)(next * 2);
                uint outer1 = b + 2 + (uint)(next * 2);

                _indices.Add(b); _indices.Add(inner0); _indices.Add(inner1);          // core fan
                _indices.Add(inner0); _indices.Add(outer0); _indices.Add(outer1);     // fringe
                _indices.Add(inner0); _indices.Add(outer1); _indices.Add(inner1);
            }

            AddTriangleCount(segments * 3);
        }

        /// <summary>
        /// Paints a Hardware-accelerated pie (circle sector) on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the pie.</param>
        /// <param name="y">The y-coordinate of the center of the pie.</param>
        /// <param name="radius">The radius of the pie.</param>
        /// <param name="startAngle">The starting angle in radians.</param>
        /// <param name="endAngle">The ending angle in radians.</param>
        /// <param name="color">The color of the pie.</param>
        /// <param name="segments">The number of segments used to approximate the curved edge. Higher values create smoother curves.</param>
        public void PieFilled(float x, float y, float radius, float startAngle, float endAngle, Color32 color, int segments = -1)
        {
            if (segments == -1)
            {
                float distance = CalculateArcLength(radius, startAngle, endAngle);
                segments = Maths.Max(1, (int)Maths.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 1)
                return;

            // Ensure angles are ordered correctly
            if (endAngle < startAngle)
            {
                endAngle += 2 * Maths.PI;
            }

            // Calculate angle range and segment size
            float angleRange = endAngle - startAngle;
            float segmentAngle = angleRange / segments;

            // Outline ring = apex (circle centre) followed by the arc points. The fringe (half a
            // pixel inside and outside the edge) is added by EmitConvexFillAA.
            _fillRing.Clear();
            _fillRing.Add(TransformPoint(new Float2(x, y)));
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + i * segmentAngle;
                float vx = x + radius * Maths.Cos(angle);
                float vy = y + radius * Maths.Sin(angle);
                _fillRing.Add(TransformPoint(new Float2(vx, vy)));
            }

            EmitConvexFillAA(_fillRing, color);
        }
        #endregion

        #region Image

        /// <summary>
        /// Draws a textured rectangle on the canvas. Respects the current transform, scissor, and global alpha.
        /// </summary>
        /// <param name="texture">The texture to draw (backend-specific texture object).</param>
        /// <param name="x">The x-coordinate of the top-left corner.</param>
        /// <param name="y">The y-coordinate of the top-left corner.</param>
        /// <param name="width">The width of the image rectangle.</param>
        /// <param name="height">The height of the image rectangle.</param>
        /// <param name="tint">Optional tint color. Defaults to white (no tint).</param>
        public void DrawImage(object texture, float x, float y, float width, float height, Color32? tint = null)
        {
            if (width <= 0 || height <= 0 || texture == null)
                return;

            var color = tint ?? new Color32(255, 255, 255, 255);

            // Save current brush state
            var savedBrush = _state.brush;

            // Configure brush to draw the texture mapped to this rectangle
            _state.brush.Texture = texture;
            _state.brush.TextureTransform = _state.transform * Transform2D.CreateTranslation(x, y) * Transform2D.CreateScale(width, height);
            _state.brush.Type = BrushType.None;
            _state.brush.Shader = null;
            _state.brush.Uniforms = null;
            InvalidateDrawState();

            // Draw the rectangle (handles transforms, AA, scissor, etc.)
            RectFilled(x, y, width, height, color);

            // Restore previous brush state
            _state.brush = savedBrush;
            InvalidateDrawState();
        }

        /// <summary>
        /// Draws a textured rectangle on the canvas, using the texture's native size.
        /// Respects the current transform, scissor, and global alpha.
        /// </summary>
        /// <param name="texture">The texture to draw (backend-specific texture object).</param>
        /// <param name="x">The x-coordinate of the top-left corner.</param>
        /// <param name="y">The y-coordinate of the top-left corner.</param>
        /// <param name="tint">Optional tint color. Defaults to white (no tint).</param>
        public void DrawImage(object texture, float x, float y, Color32? tint = null)
        {
            if (texture == null)
                return;

            var size = _renderer.GetTextureSize(texture);
            DrawImage(texture, x, y, size.X, size.Y, tint);
        }

        #endregion

        #region Text

        /// <summary>
        /// Adds a fallback font to use when glyphs are missing from the primary font.
        /// </summary>
        /// <param name="font">The fallback font to add.</param>
        public void AddFallbackFont(FontFile font) => _scribeRenderer.FontEngine.AddFallbackFont(font);

        /// <summary>
        /// Enumerates all fonts available on the system.
        /// </summary>
        /// <returns>An enumerable of system fonts.</returns>
        public IEnumerable<FontFile> EnumerateSystemFonts() => _scribeRenderer.FontEngine.EnumerateSystemFonts();

        /// <summary>
        /// Scales dimensional fields of TextLayoutSettings from logical units to physical pixels
        /// so they can be fed to the font engine (which always works in physical pixels).
        /// </summary>
        private TextLayoutSettings ScaleSettings(TextLayoutSettings settings)
        {
            settings.PixelSize *= _framebufferScale;
            settings.LetterSpacing *= _framebufferScale;
            settings.WordSpacing *= _framebufferScale;
            if (settings.MaxWidth > 0)
                settings.MaxWidth *= _framebufferScale;
            return settings;
        }

        /// <summary>
        /// Measures text. <paramref name="pixelSize"/> and <paramref name="letterSpacing"/> are in
        /// logical units; the returned size is also in logical units.
        /// </summary>
        public Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0f)
        {
            float actualPixelSize = pixelSize * _framebufferScale;
            float actualLetterSpacing = letterSpacing * _framebufferScale;
            Float2 pixelResult = (Float2)_scribeRenderer.FontEngine.MeasureText(text, actualPixelSize, font, actualLetterSpacing);
            return pixelResult / _framebufferScale;
        }

        /// <summary>
        /// Measures text using custom layout settings. All dimensional fields in
        /// <paramref name="settings"/> are in logical units; the returned size is in logical units.
        /// </summary>
        public Float2 MeasureText(string text, TextLayoutSettings settings)
        {
            var scaled = ScaleSettings(settings);
            Float2 pixelResult = (Float2)_scribeRenderer.FontEngine.MeasureText(text, scaled);
            return pixelResult / _framebufferScale;
        }

        /// <summary>
        /// Draws text at the specified logical-space position. <paramref name="pixelSize"/> and
        /// <paramref name="letterSpacing"/> are in logical units; internally the canvas rasterizes
        /// glyphs at <c>size × FramebufferScale</c> for HiDPI crispness.
        /// </summary>
        public void DrawText(string text, float x, float y, Color32 color, float pixelSize, FontFile font, float letterSpacing = 0f, Float2? origin = null, FontQuality quality = FontQuality.Normal)
        {
            // Route through the settings-based overload so the atlas quality threads down to glyph
            // creation (the simple FontEngine.DrawText overload is quality-independent / Normal).
            var settings = TextLayoutSettings.Default;
            settings.PixelSize = pixelSize;
            settings.Font = font;
            settings.LetterSpacing = letterSpacing;
            settings.Quality = quality;
            DrawText(text, x, y, color, settings, origin);
        }

        /// <summary>
        /// Draws text using custom TextLayoutSettings. All coordinates and dimensional fields are
        /// in logical units.
        /// </summary>
        public void DrawText(string text, float x, float y, Color32 color, TextLayoutSettings settings, Float2? origin = null)
        {
            var scaled = ScaleSettings(settings);
            Float2 position = new Float2(x, y);
            if (origin.HasValue)
            {
                var textSize = _scribeRenderer.FontEngine.MeasureText(text, scaled);
                position.X -= (textSize.X / _framebufferScale) * origin.Value.X;
                position.Y -= (textSize.Y / _framebufferScale) * origin.Value.Y;
            }
            Float2 pixelPosition = position * _framebufferScale;
            _scribeRenderer.FontEngine.DrawText(text, pixelPosition, new FontColor(color.R, color.G, color.B, color.A), scaled);
        }

        /// <summary>
        /// Creates a text layout. Input settings are in logical units; the returned layout
        /// is in pixel space (use <see cref="PixelToLogical(Float2)"/> to convert cursor positions
        /// back to logical units).
        /// </summary>
        public TextLayout CreateLayout(string text, TextLayoutSettings settings) => _scribeRenderer.FontEngine.CreateLayout(text, ScaleSettings(settings));

        /// <summary>
        /// Draws a pre-created text layout at the given logical-space position.
        /// The layout must have been created via <see cref="CreateLayout"/>.
        /// </summary>
        public void DrawLayout(TextLayout layout, float x, float y, Color32 color, Float2? origin = null)
        {
            Float2 position = new Float2(x, y);
            if (origin.HasValue)
            {
                var layoutSize = layout.Size;
                position.X -= (layoutSize.X / _framebufferScale) * origin.Value.X;
                position.Y -= (layoutSize.Y / _framebufferScale) * origin.Value.Y;
            }
            Float2 pixelPosition = position * _framebufferScale;
            _scribeRenderer.FontEngine.DrawLayout(layout, pixelPosition, new FontColor(color.R, color.G, color.B, color.A));
        }

        #region Markdown

        /// <summary>
        /// Represents a parsed and laid out markdown document ready for rendering.
        /// </summary>
        public struct QuillMarkdown
        {
            internal MarkdownLayoutSettings Settings;
            internal MarkdownDisplayList List;

            /// <summary>
            /// Gets the size of the laid out markdown content.
            /// </summary>
            public readonly Float2 Size => (Float2)List.Size;

            internal QuillMarkdown(MarkdownLayoutSettings settings, MarkdownDisplayList list)
            {
                Settings = settings;
                List = list;
            }
        }

        /// <summary>
        /// Sets the image provider for loading images referenced in markdown content.
        /// </summary>
        /// <param name="provider">The image provider to use.</param>
        public void SetMarkdownImageProvider(IMarkdownImageProvider provider)
        {
            _markdownImageProvider = provider;
        }

        /// <summary>
        /// Parses and lays out markdown text for rendering.
        /// </summary>
        /// <param name="markdown">The markdown text to parse.</param>
        /// <param name="settings">The layout settings for rendering.</param>
        /// <returns>A QuillMarkdown object ready for drawing.</returns>
        public QuillMarkdown CreateMarkdown(string markdown, MarkdownLayoutSettings settings)
        {
            var doc = Markdown.Parse(markdown);

            QuillMarkdown md = new QuillMarkdown() {
                Settings = settings,
                List = MarkdownLayoutEngine.Layout(doc, _scribeRenderer.FontEngine, settings, _markdownImageProvider)
            };

            return md;
        }

        /// <summary>
        /// Draws a parsed markdown document at the specified logical-space position.
        /// </summary>
        public void DrawMarkdown(QuillMarkdown markdown, Float2 position)
        {
            Float2 pixelPosition = position * _framebufferScale;
            MarkdownLayoutEngine.Render(markdown.List, _scribeRenderer.FontEngine, _scribeRenderer, (Float2)pixelPosition, markdown.Settings);
        }

        /// <summary>
        /// Checks if a point is over a link in the markdown content and returns the link URL.
        /// </summary>
        /// <param name="markdown">The markdown document to check.</param>
        /// <param name="renderOffset">The offset where the markdown was rendered.</param>
        /// <param name="point">The point to check in logical units.</param>
        /// <param name="useScissor">Whether to respect the current scissor region.</param>
        /// <param name="href">When returning true, contains the URL of the link at the point.</param>
        /// <returns>True if a link was found at the point, false otherwise.</returns>
        public bool GetMarkdownLinkAt(QuillMarkdown markdown, Float2 renderOffset, Float2 point, bool useScissor, out string href)
        {
            if (useScissor && _state.scissorExtent.X > 0)
            {
                var transformedPoint = _state.scissor.Inverse().TransformPoint(point);

                var distanceFromEdges = new Float2(
                    Maths.Abs(transformedPoint.X) - _state.scissorExtent.X,
                    Maths.Abs(transformedPoint.Y) - _state.scissorExtent.Y
                );

                if (distanceFromEdges.X > 0.5 || distanceFromEdges.Y > 0.5)
                {
                    href = null;
                    return false;
                }
            }

            Float2 pixelPoint = point * _framebufferScale;
            Float2 pixelRenderOffset = renderOffset * _framebufferScale;
            return MarkdownLayoutEngine.TryGetLinkAt(markdown.List, (Float2)pixelPoint, (Float2)pixelRenderOffset, out href);
        }

        #endregion

        #region Rich Text

        /// <summary>
        /// A parsed and laid-out rich-text block ready for animated rendering.
        /// Wraps a Scribe <see cref="RichTextLayout"/> with the framebuffer scale captured at
        /// creation, so <see cref="Size"/> and hit testing report values in logical units.
        /// </summary>
        public struct QuillRichText
        {
            internal RichTextLayout Layout;
            internal float CreationScale;

            /// <summary>Layout size in logical units.</summary>
            public readonly Float2 Size => CreationScale > 0f ? Layout.Size / CreationScale : Layout.Size;

            /// <summary>Visible text with all tags stripped (useful for accessibility / clipboard).</summary>
            public readonly string VisibleText => Layout?.VisibleText ?? string.Empty;

            /// <summary>Re-anchors animation start time on the next draw - replays typewriter etc.</summary>
            public readonly void Reset() => Layout?.Reset();
        }

        /// <summary>
        /// Clones a <see cref="RichTextLayoutSettings"/> and scales its dimensional fields from
        /// logical units to physical pixels. Tag values like <c>&lt;size=24&gt;</c> are scaled via
        /// <see cref="RichTextLayoutSettings.AbsoluteSizeScale"/> so source text stays in logical
        /// units. Frequencies / speeds / phases are unitless and pass through unchanged.
        /// </summary>
        private RichTextLayoutSettings ScaleRichSettings(RichTextLayoutSettings src)
        {
            return new RichTextLayoutSettings {
                RegularFont = src.RegularFont,
                BoldFont = src.BoldFont,
                ItalicFont = src.ItalicFont,
                BoldItalicFont = src.BoldItalicFont,
                MonoFont = src.MonoFont,

                PixelSize = src.PixelSize * _framebufferScale,
                LineHeight = src.LineHeight,
                LetterSpacing = src.LetterSpacing * _framebufferScale,
                WordSpacing = src.WordSpacing * _framebufferScale,
                TabSize = src.TabSize,
                DefaultColor = src.DefaultColor,
                Quality = src.Quality,

                MaxWidth = src.MaxWidth > 0 ? src.MaxWidth * _framebufferScale : 0f,
                WrapMode = src.WrapMode,
                Alignment = src.Alignment,
                AbsoluteSizeScale = _framebufferScale,

                // Pixel-valued effect amplitudes scale; time/relative ones don't.
                DefaultShakeAmp = src.DefaultShakeAmp * _framebufferScale,
                DefaultShakeFreq = src.DefaultShakeFreq,
                DefaultWaveAmp = src.DefaultWaveAmp * _framebufferScale,
                DefaultWaveFreq = src.DefaultWaveFreq,
                DefaultWavePhase = src.DefaultWavePhase,
                DefaultRainbowSpeed = src.DefaultRainbowSpeed,
                DefaultRainbowSpread = src.DefaultRainbowSpread,
                DefaultRainbowSat = src.DefaultRainbowSat,
                DefaultRainbowValue = src.DefaultRainbowValue,
                DefaultPulseSpeed = src.DefaultPulseSpeed,
                DefaultPulseAmp = src.DefaultPulseAmp, // relative scale, not pixels
                DefaultFadeSpeed = src.DefaultFadeSpeed,
                DefaultJitterAmp = src.DefaultJitterAmp * _framebufferScale,
                DefaultJitterFreq = src.DefaultJitterFreq,
                DefaultTypewriterSpeed = src.DefaultTypewriterSpeed,
                DefaultTypewriterFadeIn = src.DefaultTypewriterFadeIn,
            };
        }

        /// <summary>
        /// Parses a Unity-style rich-text source and lays it out for animated rendering. All
        /// dimensional fields in <paramref name="settings"/> are in logical units and are
        /// scaled internally for HiDPI. The returned object is reusable across frames.
        /// </summary>
        public QuillRichText CreateRichText(string source, RichTextLayoutSettings settings)
        {
            var scaled = ScaleRichSettings(settings);
            var rt = new RichTextLayout(source, scaled);
            rt.Update(_scribeRenderer.FontEngine);
            return new QuillRichText { Layout = rt, CreationScale = _framebufferScale };
        }

        /// <summary>
        /// Measures a rich-text source. Returns size in logical units. Convenience for laying out
        /// once just to get the size; if you'll also draw, prefer
        /// <see cref="CreateRichText"/> + <see cref="QuillRichText.Size"/>.
        /// </summary>
        public Float2 MeasureRichText(string source, RichTextLayoutSettings settings)
            => CreateRichText(source, settings).Size;

        /// <summary>
        /// Draws a rich-text block at the given logical-space position.
        /// <paramref name="currentTime"/> is in seconds; the first draw after creation or
        /// <see cref="QuillRichText.Reset"/> anchors animation start to that value.
        /// </summary>
        public void DrawRichText(QuillRichText text, Float2 position, double currentTime, Float2? origin = null)
        {
            if (text.Layout == null) return;

            Float2 pos = position;
            if (origin.HasValue)
            {
                var sz = text.Size;
                pos.X -= sz.X * origin.Value.X;
                pos.Y -= sz.Y * origin.Value.Y;
            }
            Float2 pixelPos = pos * _framebufferScale;
            text.Layout.Draw(_scribeRenderer.FontEngine, _scribeRenderer, pixelPos, currentTime);
        }

        /// <summary>
        /// Hit-tests a logical-space point against link spans in the rich text.
        /// </summary>
        /// <param name="text">The rich text block to query.</param>
        /// <param name="renderOffset">The logical position passed to the matching <c>DrawRichText</c> call.</param>
        /// <param name="point">The query point in logical units.</param>
        /// <param name="useScissor">If true, return false when the point is outside the active scissor.</param>
        /// <param name="href">When the method returns true, contains the href of the link.</param>
        public bool GetRichTextLinkAt(QuillRichText text, Float2 renderOffset, Float2 point, bool useScissor, out string href)
        {
            href = null;
            if (text.Layout == null) return false;

            if (useScissor && _state.scissorExtent.X > 0)
            {
                var transformedPoint = _state.scissor.Inverse().TransformPoint(point);
                var distanceFromEdges = new Float2(
                    Maths.Abs(transformedPoint.X) - _state.scissorExtent.X,
                    Maths.Abs(transformedPoint.Y) - _state.scissorExtent.Y
                );
                if (distanceFromEdges.X > 0.5 || distanceFromEdges.Y > 0.5) return false;
            }

            // Layout coordinates are in physical pixels; convert the logical query point.
            Float2 local = (point - renderOffset) * _framebufferScale;
            href = text.Layout.HitLink(local);
            return href != null;
        }

        #endregion

        #endregion

        #region Helpers

        internal static float CalculateArcLength(float radius, float startAngle, float endAngle)
        {
            // Make sure end angle is greater than start angle
            if (endAngle < startAngle)
                endAngle += 2 * Maths.PI;
            return radius * (endAngle - startAngle);
        }

        // Helper function to calculate the signed angle from vector u to vector v
        internal static float CalculateVectorAngle(float ux, float uy, float vx, float vy)
        {
            float dot = ux * vx + uy * vy;
            float det = ux * vy - uy * vx; // 2D cross product
            return Maths.Atan2(det, dot); // Returns angle in radians from -PI to PI
        }

        #endregion

        /// <summary>
        /// Disposes the canvas and releases the underlying renderer resources.
        /// </summary>
        public void Dispose()
        {
            _renderer?.Dispose();
        }
    }
}
