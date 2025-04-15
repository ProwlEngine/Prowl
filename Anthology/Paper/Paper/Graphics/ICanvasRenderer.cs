using System.Drawing;
using System.Numerics;
using Matrix = System.Numerics.Matrix4x4;
using Texture2D = System.Object;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Specifies the type of rendering operation to perform.
    /// Based on NanoVG's rendering approach.
    /// </summary>
    public enum CallType
    {
        /// <summary>
        /// Fill a complex path using stencil buffer technique.
        /// Used for paths that may contain self-intersections or holes.
        /// </summary>
        Fill,

        /// <summary>
        /// Fill a convex path directly without using stencil buffer.
        /// Optimized for simple shapes that are guaranteed to be convex.
        /// </summary>
        ConvexFill,

        /// <summary>
        /// Stroke a path with configurable width.
        /// </summary>
        Stroke,

        /// <summary>
        /// Render a set of triangles directly.
        /// Used for custom geometry or images.
        /// </summary>
        Triangles
    }

    /// <summary>
    /// Specifies the type of shader to use for rendering.
    /// Determines how colors and textures are applied to geometry.
    /// </summary>
    public enum RenderType
    {
        /// <summary>
        /// Fill with a gradient (linear or radial).
        /// </summary>
        FillGradient,

        /// <summary>
        /// Fill with an image texture.
        /// </summary>
        FillImage,

        /// <summary>
        /// Simple color fill without special effects.
        /// Used primarily for stencil operations.
        /// </summary>
        Simple,

        /// <summary>
        /// Direct image rendering.
        /// Used for rendering image triangles.
        /// </summary>
        Image
    }

    /// <summary>
    /// Contains uniform data sent to shaders for rendering.
    /// Encapsulates all parameters needed for the various shader types.
    /// </summary>
    public struct UniformInfo
    {
        /// <summary>Matrix for transforming scissor coordinates</summary>
        public Matrix scissorMat;

        /// <summary>Matrix for transforming paint coordinates (for gradients and patterns)</summary>
        public Matrix paintMat;

        /// <summary>Inner color for gradients and shapes (RGBA)</summary>
        public Vector4 innerCol;

        /// <summary>Outer color for gradients and shapes (RGBA)</summary>
        public Vector4 outerCol;

        /// <summary>Scissor rectangle extents (width/height)</summary>
        public Vector2 scissorExt;

        /// <summary>Scissor scale factors for anti-aliasing adjustment</summary>
        public Vector2 scissorScale;

        /// <summary>Extent of the paint (used for gradient positioning)</summary>
        public Vector2 extent;

        /// <summary>Radius for radial gradients</summary>
        public float radius;

        /// <summary>Feather amount for gradient edge softening</summary>
        public float feather;

        /// <summary>Stroke multiplication factor for width calculation</summary>
        public float strokeMult;

        /// <summary>Stroke threshold for anti-aliasing</summary>
        public float strokeThr;

        /// <summary>Image texture to use for rendering</summary>
        public Texture2D Image;

        /// <summary>Type of rendering to perform</summary>
        public RenderType type;
    }

    /// <summary>
    /// Contains vertex offset and count information for fill and stroke operations on a path.
    /// Each path may have both fill and stroke vertices in the vertex buffer.
    /// </summary>
    public struct FillStrokeInfo
    {
        /// <summary>Offset into the vertex buffer where fill vertices start</summary>
        public int FillOffset;

        /// <summary>Number of fill vertices</summary>
        public int FillCount;

        /// <summary>Offset into the vertex buffer where stroke vertices start</summary>
        public int StrokeOffset;

        /// <summary>Number of stroke vertices</summary>
        public int StrokeCount;
    }

    /// <summary>
    /// Represents a complete rendering call with all necessary information.
    /// Groups together uniform data, vertex references, and rendering parameters.
    /// </summary>
    public class CallInfo
    {
        /// <summary>Type of rendering operation to perform</summary>
        public CallType Type;

        /// <summary>
        /// Primary uniform data for rendering.
        /// For Fill calls: stencil shader uniforms.
        /// For other calls: main shader uniforms.
        /// </summary>
        public UniformInfo UniformInfo;

        /// <summary>
        /// Secondary uniform data.
        /// For Fill calls: cover shader uniforms.
        /// For Stroke with stencil: cover shader uniforms.
        /// </summary>
        public UniformInfo UniformInfo2;

        /// <summary>
        /// Collection of fill/stroke vertex information for each path in this call.
        /// A single call may render multiple paths of the same type.
        /// </summary>
        public readonly List<FillStrokeInfo> FillStrokeInfos = new List<FillStrokeInfo>();

        /// <summary>Offset into the vertex buffer where triangle vertices start</summary>
        public int TriangleOffset;

        /// <summary>Number of triangle vertices</summary>
        public int TriangleCount;
    }

    /// <summary>
    /// Interface for rendering Paper's Canvas to different rendering backends.
    /// Implementations handle the actual drawing operations using specific graphics APIs.
    /// </summary>
    public interface ICanvasRenderer
    {
        /// <summary>
        /// Creates a texture of the specified dimensions.
        /// </summary>
        /// <param name="width">Width of the texture in pixels</param>
        /// <param name="height">Height of the texture in pixels</param>
        /// <returns>Backend-specific texture object</returns>
        object CreateTexture(int width, int height);

        /// <summary>
        /// Retrieves the dimensions of a texture.
        /// </summary>
        /// <param name="texture">Texture object to query</param>
        /// <returns>Size of the texture in pixels</returns>
        Point GetTextureSize(object texture);

        /// <summary>
        /// Updates a portion of a texture with new pixel data.
        /// </summary>
        /// <param name="texture">Texture to update</param>
        /// <param name="bounds">Rectangle defining the area to update</param>
        /// <param name="data">Raw RGBA pixel data (4 bytes per pixel)</param>
        void SetTextureData(object texture, Rectangle bounds, byte[] data);

        /// <summary>
        /// Executes all pending rendering operations.
        /// </summary>
        /// <param name="devicePixelRatio">Current device pixel ratio for proper scaling</param>
        /// <param name="calls">Collection of render calls to execute</param>
        /// <param name="vertexes">Vertex data for all rendering operations</param>
        void Draw(float devicePixelRatio, IEnumerable<CallInfo> calls, Vertex[] vertexes);
    }
}