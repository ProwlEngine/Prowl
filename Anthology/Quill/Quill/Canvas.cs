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

        /// <summary>
        /// Gets the texture from the brush. Returns null if no texture is set.
        /// </summary>
        public object? Texture => Brush.Texture;

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
                matrix = new Float4x4();
                extent = new Float2(1, 1);
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
        public static int SizeInBytes => 20; //Marshal.SizeOf<Vertex>();

        /// <summary>
        /// Gets the position of the vertex in screen space.
        /// </summary>
        public Float2 Position => new Float2(x, y);

        /// <summary>
        /// Gets the texture/UV coordinates of the vertex.
        /// </summary>
        public Float2 UV => new Float2(u, v);

        /// <summary>
        /// Gets the color of the vertex.
        /// </summary>
        public Color32 Color => Color32.FromArgb(a, r, g, b);

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

        /// <summary>
        /// Creates a new vertex with the specified position, UV coordinates, and color.
        /// </summary>
        /// <param name="position">The position in screen space.</param>
        /// <param name="UV">The texture coordinates.</param>
        /// <param name="color">The vertex color.</param>
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
        internal List<float> strokeDashPattern;
        internal float strokeDashOffset;
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
            strokeDashPattern = null; // Default: solid line
            strokeDashOffset = 0.0f;   // Default: no offset
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

        internal List<uint> _indices = new List<uint>();
        internal List<Vertex> _vertices = new List<Vertex>();

        private readonly List<SubPath> _subPaths = new List<SubPath>();
        private SubPath? _currentSubPath = null;
        private bool _isPathReady = false;

        private readonly Stack<ProwlCanvasState> _savedStates = new Stack<ProwlCanvasState>();
        private ProwlCanvasState _state;
        private float _globalAlpha;

        private TextRenderer _scribeRenderer;

        private float _pixelWidth = 1.0f;
        private float _pixelHalf = 0.5f;

        private float _scale = 1.0f;
        private float _width = 0.0f;
        private float _height = 0.0f;

        private IMarkdownImageProvider? _markdownImageProvider = null;

        /// <summary>
        /// Gets the current device pixel ratio (DPI scale factor).
        /// </summary>
        public float Scale => _scale;

        /// <summary>
        /// Gets the logical width of the canvas in units.
        /// </summary>
        public float Width => _width;

        /// <summary>
        /// Gets the logical height of the canvas in units.
        /// </summary>
        public float Height => _height;

        /// <summary>
        /// Gets the size of one pixel in logical units.
        /// </summary>
        public float PixelFraction => 1.0f / _scale;

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
        /// Begins a new frame with the specified logical dimensions and pixel ratio.
        /// </summary>
        /// <remarks>
        /// Call this at the start of each frame before any drawing operations.
        /// All drawing coordinates are in logical units. The pixel ratio determines
        /// how logical units map to physical pixels for high-DPI rendering.
        ///
        /// Example usage:
        /// <code>
        /// // Each frame:
        /// canvas.BeginFrame(GetScreenWidth(), GetScreenHeight(), GetWindowScaleDPI().X);
        ///
        /// // Draw using logical coordinates:
        /// canvas.DrawRect(0, 0, canvas.Width, canvas.Height);
        /// canvas.DrawText("Hello", 10, 10, color, 16, font); // 16 logical pixels
        /// </code>
        /// </remarks>
        /// <param name="width">Logical width of the canvas in units.</param>
        /// <param name="height">Logical height of the canvas in units.</param>
        /// <param name="pixelRatio">Device pixel ratio (1.0 = standard DPI, 2.0 = Retina/HiDPI).</param>
        public void BeginFrame(float width, float height, float pixelRatio = 1.0f)
        {
            if (pixelRatio <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelRatio), "Pixel ratio must be greater than zero.");

            _width = width;
            _height = height;
            _scale = pixelRatio;
            UpdatePixelCalculations();
            Clear();
        }

        #region Coordinate Conversion

        /// <summary>
        /// Converts a point from physical pixel coordinates to logical units.
        /// </summary>
        /// <remarks>
        /// Use this to convert mouse/touch positions from the windowing system
        /// (which are typically in physical pixels) to logical canvas coordinates.
        /// </remarks>
        /// <param name="pixelPoint">Point in physical pixel coordinates.</param>
        /// <returns>Point in logical canvas units.</returns>
        public Float2 PixelToLogical(Float2 pixelPoint) => pixelPoint / _scale;

        /// <summary>
        /// Converts a value from physical pixels to logical units.
        /// </summary>
        /// <param name="pixelValue">Value in physical pixels.</param>
        /// <returns>Value in logical units.</returns>
        public float PixelToLogical(float pixelValue) => pixelValue / _scale;

        /// <summary>
        /// Converts a point from logical units to physical pixel coordinates.
        /// </summary>
        /// <remarks>
        /// Use this when you need to map logical canvas coordinates back to
        /// physical pixel positions (e.g., for setting cursor position).
        /// </remarks>
        /// <param name="logicalPoint">Point in logical canvas units.</param>
        /// <returns>Point in physical pixel coordinates.</returns>
        public Float2 LogicalToPixel(Float2 logicalPoint) => logicalPoint * _scale;

        /// <summary>
        /// Converts a value from logical units to physical pixels.
        /// </summary>
        /// <param name="logicalValue">Value in logical units.</param>
        /// <returns>Value in physical pixels.</returns>
        public float LogicalToPixel(float logicalValue) => logicalValue * _scale;

        #endregion

        private void UpdatePixelCalculations()
        {
            _pixelWidth = 1.0f / _scale;
            _pixelHalf = _pixelWidth * 0.5f;
        }

        /// <summary>
        /// Clears all accumulated geometry, draw calls, and resets the canvas state.
        /// </summary>
        public void Clear()
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
        /// Sets the dash pattern for strokes.
        /// </summary>
        /// <param name="pattern">A list of floats representing the lengths of dashes and gaps (e.g., [dash1_len, gap1_len, dash2_len, ...]). 
        /// If null or empty, a solid line will be drawn. If the number of elements in the array is odd, the elements of the array get copied and concatenated.</param>
        /// <param name="offset">The offset at which to start the dash pattern along the path.</param>
        public void SetStrokeDash(List<float> pattern, float offset = 0.0f)
        {
            int patternCount = pattern?.Count ?? 0;

            // if the count is odd, duplicate the entire pattern and concatenate it
            if (patternCount > 0 && patternCount % 2 != 0)
            {
                var newPattern = new List<float>(pattern);
                newPattern.AddRange(pattern);
                pattern = newPattern;
            }

            _state.strokeDashPattern = pattern;
            _state.strokeDashOffset = offset;
        }

        /// <summary>
        /// Clears any previously set stroke dash pattern, reverting to a solid line.
        /// </summary>
        public void ClearStrokeDash()
        {
            _state.strokeDashPattern = null;
            _state.strokeDashOffset = 0.0f;
        }

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
        /// Internal method for setting a texture that will be sampled using vertex UVs.
        /// Used by TextRenderer for font atlas textures.
        /// </summary>
        internal void SetFontTexture(object? texture)
        {
            _state.brush.Texture = texture;
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
            // Work in unit space - conversion to pixels happens in TransformPoint
            _state.scissor = _state.transform * Transform2D.CreateTranslation(x + w * 0.5f, y + h * 0.5f);
            _state.scissorExtent.X = (w * 0.5f) * _scale;
            _state.scissorExtent.Y = (h * 0.5f) * _scale;
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
            var ex = _state.scissorExtent.X;
            var ey = _state.scissorExtent.Y;
            var invxorm = _state.transform.Inverse();
            pxform = invxorm * pxform; // Or pxform * invxorm?

            // Calculate extent in current transform space
            var tex = ex * Maths.Abs(pxform.A) + ey * Maths.Abs(pxform.C);
            var tey = ex * Maths.Abs(pxform.B) + ey * Maths.Abs(pxform.D);

            // Find the intersection - work in unit space
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

            return new Rect(minx, miny, Maths.Max(0.0f, maxx - minx), Maths.Max(0.0f, maxy - miny));
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
            // Apply transform in unit space, then convert to pixels
            Float2 transformedUnitPoint = _state.transform.TransformPoint(unitPoint);
            return transformedUnitPoint * _scale;
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
            if (_globalAlpha != 1.0f)
                vertex.a = (byte)(vertex.a * _globalAlpha);

            // Premultiply
            if (vertex.a != 255)
            {
                var alpha = vertex.a / 255f;
                vertex.r = (byte)(vertex.r * alpha);
                vertex.g = (byte)(vertex.g * alpha);
                vertex.b = (byte)(vertex.b * alpha);
            }


            // Add the vertex to the list
            _vertices.Add(vertex);
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
                    // TransformPoint converts to physical pixels; 0.5 offset aligns with pixel grid for AA (DPI-independent in pixel space)
                    copy[i] = TransformPoint(copy[i]) + new Float2(0.5f, 0.5f);
                var points = copy.Select(v => new ContourVertex() { Position = new Vec3() { X = v.X, Y = v.Y } }).ToArray();

                tess.AddContour(points, ContourOrientation.Original);
            }
            tess.Tessellate(_state.fillMode == WindingMode.OddEven ? WindingRule.EvenOdd : WindingRule.NonZero, ElementType.Polygons, 3);

            var indices = tess.Elements;
            var vertices = tess.Vertices;

            // Create vertices and triangles
            uint startVertexIndex = (uint)_vertices.Count;
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                Float2 pos = new Float2(vertex.Position.X, vertex.Position.Y);
                AddVertex(new Vertex(pos, new Float2(0.5f, 0.5f), _state.fillColor));
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

            // Transform each point
            Float2 center = Float2.Zero;
            var copy = subPath.Points.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                var point = copy[i];
                // TransformPoint converts to physical pixels; 0.5 offset aligns with pixel grid for AA (DPI-independent in pixel space)
                point = TransformPoint(point) + new Float2(0.5f, 0.5f);
                center += point;
                copy[i] = point;
            }
            center /= copy.Length;

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
            AddVertex(new Vertex(center, new Float2(0.5f, 0.5f), _state.fillColor));

            // Generate vertices around the path
            // Note: copy[] is in physical pixel space after TransformPoint, so use 1.0 for one physical pixel expansion
            int segments = copy.Length;
            for (int i = 0; i < segments; i++) // Edge vertices have UV at 0,0 for anti-aliasing
            {
                Float2 dirToPoint = Float2.Normalize(copy[i] - center);
                AddVertex(new Vertex(copy[i] + dirToPoint, new Float2(0, 0), _state.fillColor));
            }

            // Create triangles (fan from center to edges)
            // Check orientation with just the first triangle
            uint centerIdx = (uint)startVertexIndex;
            uint first = (uint)(startVertexIndex + 1);
            uint second = (uint)(startVertexIndex + 2);

            Float2 centerPos = _vertices[(int)centerIdx].Position;
            Float2 firstPos = _vertices[(int)first].Position;
            Float2 secondPos = _vertices[(int)second].Position;

            float cross = ((firstPos.X - centerPos.X) * (secondPos.Y - centerPos.Y)) -
                           ((firstPos.Y - centerPos.Y) * (secondPos.X - centerPos.X));

            bool clockwise = cross <= 0;

            // Use the determined orientation for all triangles
            for (int i = 0; i < segments; i++)
            {
                uint current = (uint)(startVertexIndex + 1 + i);
                uint next = (uint)(startVertexIndex + 1 + ((i + 1) % segments));

                if (clockwise)
                {
                    _indices.Add(centerIdx);
                    _indices.Add(current);
                    _indices.Add(next);
                }
                else
                {
                    _indices.Add(centerIdx);
                    _indices.Add(next);
                    _indices.Add(current);
                }

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments);
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

            var copy = new List<Float2>(subPath.Points.Count);
            // Transform each point
            for (int i = 0; i < subPath.Points.Count; i++)
                copy.Add(TransformPoint(subPath.Points[i]));

            List<float> dashPattern = null;
            if (_state.strokeDashPattern != null)
            {
                dashPattern = new List<float>(_state.strokeDashPattern);
                for (int i = 0; i < dashPattern.Count; i++)
                {
                    // Convert dash pattern from units to pixels
                    dashPattern[i] = (dashPattern[i] * _state.strokeScale) * _scale;
                }
            }

            // Convert stroke width and dash offset from units to pixels
            float pixelStrokeWidth = (_state.strokeWidth * _state.strokeScale) * _scale;
            float pixelDashOffset = (_state.strokeDashOffset * _state.strokeScale) * _scale;
            var triangles = PolylineMesher.Create(copy, pixelStrokeWidth, _pixelWidth, _state.strokeColor, _state.strokeJoint, _state.miterLimit, false, _state.strokeStartCap, _state.strokeEndCap, dashPattern, pixelDashOffset);


            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;
            foreach (var triangle in triangles)
            {
                var color = triangle.Color;
                AddVertex(new Vertex(triangle.V1, triangle.UV1, color));
                AddVertex(new Vertex(triangle.V2, triangle.UV2, color));
                AddVertex(new Vertex(triangle.V3, triangle.UV3, color));
            }

            // Add triangle _indices
            for (uint i = 0; i < triangles.Count; i++)
            {
                _indices.Add(startVertexIndex + (i * 3));
                _indices.Add(startVertexIndex + (i * 3) + 1);
                _indices.Add(startVertexIndex + (i * 3) + 2);
                //AddTriangleCount(1);
            }

            AddTriangleCount(triangles.Count);
        }

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

            // Center it so it scales and sits properly with AA
            // Convert pixel adjustments to unit space since coordinates are in units
            float unitPixelHalf = _pixelHalf / _scale;
            float unitPixelWidth = _pixelWidth / _scale;
            x -= unitPixelHalf;
            y -= unitPixelHalf;
            width += unitPixelWidth;
            height += unitPixelWidth;

            // Apply transform to the four corners of the rectangle
            Float2 topLeft = TransformPoint(new Float2(x, y));
            Float2 topRight = TransformPoint(new Float2(x, y + height));
            Float2 bottomRight = TransformPoint(new Float2(x + width, y + height));
            Float2 bottomLeft = TransformPoint(new Float2(x + width, y));

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Add all vertices with the transformed coordinates
            AddVertex(new Vertex(topLeft, new Float2(0, 0), color));
            AddVertex(new Vertex(topRight, new Float2(0, 1), color));
            AddVertex(new Vertex(bottomRight, new Float2(1, 1), color));
            AddVertex(new Vertex(bottomLeft, new Float2(1, 0), color));

            // Add indexes for fill
            _indices.Add(startVertexIndex);
            _indices.Add(startVertexIndex + 1);
            _indices.Add(startVertexIndex + 2);

            _indices.Add(startVertexIndex);
            _indices.Add(startVertexIndex + 2);
            _indices.Add(startVertexIndex + 3);

            AddTriangleCount(2);
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

            // Adjust for proper AA
            // Convert pixel adjustments to unit space since coordinates are in units
            float unitPixelHalf = _pixelHalf / _scale;
            float unitPixelWidth = _pixelWidth / _scale;
            x -= unitPixelHalf;
            y -= unitPixelHalf;
            width += unitPixelWidth;
            height += unitPixelWidth;

            // Calculate segment counts for each corner based on radius size
            int tlSegments = Maths.Max(1, (int)Maths.Ceiling(Maths.PI * tlRadii / 2 / _state.roundingMinDistance));
            int trSegments = Maths.Max(1, (int)Maths.Ceiling(Maths.PI * trRadii / 2 / _state.roundingMinDistance));
            int brSegments = Maths.Max(1, (int)Maths.Ceiling(Maths.PI * brRadii / 2 / _state.roundingMinDistance));
            int blSegments = Maths.Max(1, (int)Maths.Ceiling(Maths.PI * blRadii / 2 / _state.roundingMinDistance));

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Calculate the center point of the rectangle
            Float2 center = TransformPoint(new Float2(x + width / 2, y + height / 2));

            // Add center vertex with UV at 0.5,0.5 (no AA)
            AddVertex(new Vertex(center, new Float2(0.5f, 0.5f), color));

            List<Float2> points = new List<Float2>();

            // Top-left corner
            if (tlRadii > 0)
            {
                Float2 tlCenter = new Float2(x + tlRadii, y + tlRadii);
                for (int i = 0; i <= tlSegments; i++)
                {
                    float angle = Maths.PI + (Maths.PI / 2) * i / tlSegments;
                    float vx = tlCenter.X + tlRadii * Maths.Cos(angle);
                    float vy = tlCenter.Y + tlRadii * Maths.Sin(angle);
                    points.Add(new Float2(vx, vy));
                }
            }
            else
            {
                points.Add(new Float2(x, y));
            }

            // Top-right corner
            if (trRadii > 0)
            {
                Float2 trCenter = new Float2(x + width - trRadii, y + trRadii);
                for (int i = 0; i <= trSegments; i++)
                {
                    float angle = Maths.PI * 3 / 2 + (Maths.PI / 2) * i / trSegments;
                    float vx = trCenter.X + trRadii * Maths.Cos(angle);
                    float vy = trCenter.Y + trRadii * Maths.Sin(angle);
                    points.Add(new Float2(vx, vy));
                }
            }
            else
            {
                points.Add(new Float2(x + width, y));
            }

            // Bottom-right corner
            if (brRadii > 0)
            {
                Float2 brCenter = new Float2(x + width - brRadii, y + height - brRadii);
                for (int i = 0; i <= brSegments; i++)
                {
                    float angle = 0 + (Maths.PI / 2) * i / brSegments;
                    float vx = brCenter.X + brRadii * Maths.Cos(angle);
                    float vy = brCenter.Y + brRadii * Maths.Sin(angle);
                    points.Add(new Float2(vx, vy));
                }
            }
            else
            {
                points.Add(new Float2(x + width, y + height));
            }

            // Bottom-left corner
            if (blRadii > 0)
            {
                Float2 blCenter = new Float2(x + blRadii, y + height - blRadii);
                for (int i = 0; i <= blSegments; i++)
                {
                    float angle = Maths.PI / 2 + (Maths.PI / 2) * i / blSegments;
                    float vx = blCenter.X + blRadii * Maths.Cos(angle);
                    float vy = blCenter.Y + blRadii * Maths.Sin(angle);
                    points.Add(new Float2(vx, vy));
                }
            }
            else
            {
                points.Add(new Float2(x, y + height));
            }

            // Add all edge vertices
            for (int i = 0; i < points.Count; i++)
            {
                Float2 transformedPoint = TransformPoint(points[i]);
                AddVertex(new Vertex(transformedPoint, new Float2(0, 0), color));
            }

            // Create triangles (fan from center to edges)
            for (int i = 0; i < points.Count; i++)
            {
                uint current = (uint)(startVertexIndex + 1 + i);
                uint next = (uint)(startVertexIndex + 1 + ((i + 1) % points.Count));

                _indices.Add((uint)startVertexIndex);  // Center
                _indices.Add(next);                    // Next edge vertex
                _indices.Add(current);                 // Current edge vertex

                //AddTriangleCount(1);
            }
            AddTriangleCount(points.Count);
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

            // Center it so it scales and sits properly with AA
            // Convert pixel adjustments to unit space since coordinates are in units
            radius += _pixelHalf / _scale;

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            Float2 transformedCenter = TransformPoint(new Float2(x, y));

            // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
            AddVertex(new Vertex(transformedCenter, new Float2(0.5f, 0.5f), color));

            // Generate vertices around the circle
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2 * Maths.PI * i / segments;
                float vx = x + radius * Maths.Cos(angle);
                float vy = y + radius * Maths.Sin(angle);

                Float2 transformedPoint = TransformPoint(new Float2(vx, vy));

                // Edge vertices have UV at 0,0 for anti-aliasing
                AddVertex(new Vertex(
                    transformedPoint,
                    new Float2(0, 0),  // UV at edge for AA
                    color
                ));
            }

            // Create triangles (fan from center to edges)
            for (int i = 0; i < segments; i++)
            {
                _indices.Add((uint)startVertexIndex);                  // Center
                _indices.Add((uint)(startVertexIndex + 1 + ((i + 1) % segments))); // Next edge vertex
                _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments);
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

            // Calculate the centroid of the pie section
            // For a pie section, the centroid is not at the circle center but at
            // a position ~2/3 toward the arc's midpoint
            float midAngle = startAngle + angleRange / 2;
            float centroidDistance = radius * 2 / 3 * Maths.Sin(angleRange / 2) / (angleRange / 2);
            float centroidX = x + centroidDistance * Maths.Cos(midAngle);
            float centroidY = y + centroidDistance * Maths.Sin(midAngle);

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            Float2 transformedCenter = TransformPoint(new Float2(x, y));
            Float2 transformedCentroid = TransformPoint(new Float2(centroidX, centroidY));

            // Add centroid vertex with UV at 0.5,0.5 (fully opaque, no AA)
            AddVertex(new Vertex(transformedCentroid, new Float2(0.5f, 0.5f), color));

            // Start path
            AddVertex(new Vertex(transformedCenter, new Float2(0.0f, 0.0f), color));

            // Generate vertices around the arc plus the two radial endpoints
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + i * segmentAngle;
                float vx = x + radius * Maths.Cos(angle);
                float vy = y + radius * Maths.Sin(angle);

                Float2 transformedPoint = TransformPoint(new Float2(vx, vy));

                // Offset for AA
                var direction = Float2.Normalize(transformedPoint - transformedCenter);
                transformedPoint += direction * _pixelWidth;

                // Edge vertices have UV at 0,0 for anti-aliasing
                AddVertex(new Vertex(transformedPoint, new Float2(0, 0), color));
            }

            // Close path
            AddVertex(new Vertex(transformedCenter, new Float2(0.0f, 0.0f), color));

            // Create triangles (fan from centroid to each pair of edge points)
            for (int i = 0; i < segments + 2; i++)
            {
                _indices.Add(startVertexIndex);                  // Centroid
                _indices.Add((uint)(startVertexIndex + 1 + i + 1));      // Next edge vertex
                _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments + 2);
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
        /// Measures the size of text when rendered with the specified settings.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="pixelSize">The font size in logical units.</param>
        /// <param name="font">The font to use.</param>
        /// <param name="letterSpacing">Additional spacing between letters.</param>
        /// <returns>The size of the text in logical units.</returns>
        public Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0f)
        {
            // Measure at scaled pixel size for accuracy, then convert back to logical units
            float actualPixelSize = pixelSize * _scale;
            float actualLetterSpacing = letterSpacing * _scale;
            Float2 pixelResult = (Float2)_scribeRenderer.FontEngine.MeasureText(text, actualPixelSize, font, actualLetterSpacing);
            return pixelResult / _scale;
        }

        /// <summary>
        /// Measures the size of text using custom layout settings.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <param name="settings">The layout settings to use.</param>
        /// <returns>The size of the text.</returns>
        public Float2 MeasureText(string text, TextLayoutSettings settings) => (Float2)_scribeRenderer.FontEngine.MeasureText(text, settings);

        /// <summary>
        /// Draws text at the specified position.
        /// </summary>
        /// <param name="text">The text to draw.</param>
        /// <param name="x">The X coordinate.</param>
        /// <param name="y">The Y coordinate.</param>
        /// <param name="color">The text color.</param>
        /// <param name="pixelSize">The font size in logical units.</param>
        /// <param name="font">The font to use.</param>
        /// <param name="letterSpacing">Additional spacing between letters.</param>
        /// <param name="origin">Optional origin point for alignment (0-1 range, e.g., 0.5,0.5 for center).</param>
        public void DrawText(string text, float x, float y, Color32 color, float pixelSize, FontFile font, float letterSpacing = 0f, Float2? origin = null)
        {
            Float2 position = new Float2(x, y);
            // Scale font size for crisp high-DPI rendering
            float actualPixelSize = pixelSize * _scale;
            float actualLetterSpacing = letterSpacing * _scale;
            if (origin.HasValue)
            {
                // Measure at scaled size, convert offset back to logical units
                var textSize = _scribeRenderer.FontEngine.MeasureText(text, actualPixelSize, font, actualLetterSpacing);
                position.X -= (textSize.X / _scale) * origin.Value.X;
                position.Y -= (textSize.Y / _scale) * origin.Value.Y;
            }
            // Scale position to pixel space - DrawQuads will convert back for transform application
            Float2 pixelPosition = position * _scale;
            _scribeRenderer.FontEngine.DrawText(text, pixelPosition, new FontColor(color.R, color.G, color.B, color.A), actualPixelSize, font, actualLetterSpacing);
        }

        /// <summary>
        /// Draws text using custom TextLayoutSettings.
        /// </summary>
        /// <remarks>
        /// Note: For proper DPI scaling, the font size in TextLayoutSettings should be
        /// pre-multiplied by the pixel ratio. Use the other DrawText overload for automatic scaling.
        /// </remarks>
        public void DrawText(string text, float x, float y, Color32 color, TextLayoutSettings settings, Float2? origin = null)
        {
            Float2 position = new Float2(x, y);
            if (origin.HasValue)
            {
                var textSize = _scribeRenderer.FontEngine.MeasureText(text, settings);
                // Settings use pixel units, so convert back to logical for offset
                position.X -= (textSize.X / _scale) * origin.Value.X;
                position.Y -= (textSize.Y / _scale) * origin.Value.Y;
            }
            // Scale position to pixel space - DrawQuads will convert back for transform application
            Float2 pixelPosition = position * _scale;
            _scribeRenderer.FontEngine.DrawText(text, pixelPosition, new FontColor(color.R, color.G, color.B, color.A), settings);
        }

        /// <summary>
        /// Creates a text layout for later rendering.
        /// </summary>
        /// <remarks>
        /// Note: For proper DPI scaling, the font size in TextLayoutSettings should be
        /// pre-multiplied by the pixel ratio.
        /// </remarks>
        public TextLayout CreateLayout(string text, TextLayoutSettings settings) => _scribeRenderer.FontEngine.CreateLayout(text, settings);

        /// <summary>
        /// Draws a pre-created text layout.
        /// </summary>
        /// <remarks>
        /// Note: The layout should have been created with DPI-scaled font sizes for proper rendering.
        /// </remarks>
        public void DrawLayout(TextLayout layout, float x, float y, Color32 color, Float2? origin = null)
        {
            Float2 position = new Float2(x, y);
            if (origin.HasValue)
            {
                var layoutSize = layout.Size;
                // Layout size is in pixels, convert back to logical for offset
                position.X -= (layoutSize.X / _scale) * origin.Value.X;
                position.Y -= (layoutSize.Y / _scale) * origin.Value.Y;
            }
            // Scale position to pixel space - DrawQuads will convert back for transform application
            Float2 pixelPosition = position * _scale;
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
        /// Draws a parsed markdown document at the specified position.
        /// </summary>
        /// <param name="markdown">The markdown to render.</param>
        /// <param name="position">The position to render at in logical units.</param>
        public void DrawMarkdown(QuillMarkdown markdown, Float2 position)
        {
            // Convert units to pixels for position
            Float2 pixelPosition = position * _scale;
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
            // Check if point is within scissor rect if enabled
            if (useScissor && _state.scissorExtent.X > 0)
            {
                // Transform point to scissor space
                var transformedPoint = _state.scissor.Inverse().TransformPoint(point);
                //var transformedPoint = new Float2(
                //    (float)(scissorMatrix.M11 * point.X + scissorMatrix.M12 * point.Y + scissorMatrix.M14),
                //    (float)(scissorMatrix.M21 * point.X + scissorMatrix.M22 * point.Y + scissorMatrix.M24)
                //);

                // Check if the point is within the scissor extent
                var distanceFromEdges = new Float2(
                    Maths.Abs(transformedPoint.X) - _state.scissorExtent.X,
                    Maths.Abs(transformedPoint.Y) - _state.scissorExtent.Y
                );

                // If either distance is positive, we're outside the scissor region
                if (distanceFromEdges.X > 0.5 || distanceFromEdges.Y > 0.5)
                {
                    href = null;
                    return false;
                }
            }


            // Convert units to pixels for point and render offset
            Float2 pixelPoint = point * _scale;
            Float2 pixelRenderOffset = renderOffset * _scale;
            return MarkdownLayoutEngine.TryGetLinkAt(markdown.List, (Float2)pixelPoint, (Float2)pixelRenderOffset, out href);
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
