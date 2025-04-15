using Prowl.PaperUI.Utilities;
using Prowl.PaperUI;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Manages the buffering and organization of rendering data for PaperUI.
    /// Acts as an intermediate cache between rendering commands and actual drawing.
    /// </summary>
    /// <remarks>
    /// Creates a new render cache with specified stencil stroke setting
    /// </remarks>
    /// <param name="stencilStrokes">Whether to use stencil buffer for stroke rendering</param>
    internal class RenderCache(bool stencilStrokes)
    {
        /// <summary>Maximum number of vertices that can be stored in the cache</summary>
        private const int MAX_VERTICES = 8192;

        /// <summary>Whether to use stencil buffer for stroke rendering</summary>
        private readonly bool _stencilStrokes = stencilStrokes;

        /// <summary>Vertex buffer containing all vertices to be rendered</summary>
        public readonly ArrayBuffer<Vertex> VertexArray = new ArrayBuffer<Vertex>(MAX_VERTICES);

        /// <summary>List of rendering calls to be executed</summary>
        public readonly List<CallInfo> Calls = new List<CallInfo>();

        /// <summary>Current device pixel ratio for proper scaling</summary>
        public float DevicePixelRatio;

        /// <summary>Current number of vertices in the buffer</summary>
        public int VertexCount => VertexArray.Count;

        /// <summary>Whether stroke operations should use stencil buffer technique</summary>
        public bool StencilStrokes => _stencilStrokes;

        /// <summary>
        /// Clears all vertex and call data from the cache
        /// </summary>
        public void Reset()
        {
            VertexArray.Clear();
            Calls.Clear();
        }

        /// <summary>
        /// Adds a new vertex to the buffer with specified position and texture coordinates
        /// </summary>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="u">U texture coordinate</param>
        /// <param name="v">V texture coordinate</param>
        public void AddVertex(float x, float y, float u, float v) => VertexArray.Add(new Vertex(x, y, u, v));

        /// <summary>
        /// Adds a pre-constructed vertex to the buffer
        /// </summary>
        /// <param name="v">Vertex to add</param>
        public void AddVertex(Vertex v) => VertexArray.Add(v);

        /// <summary>
        /// Builds uniform data for shader from paint and scissor settings
        /// </summary>
        /// <param name="paint">Paint settings to use</param>
        /// <param name="scissor">Scissor settings to use</param>
        /// <param name="width">Stroke width or other relevant width</param>
        /// <param name="fringe">Size of anti-aliasing fringe</param>
        /// <param name="strokeThr">Stroke threshold</param>
        /// <param name="uniform">Output uniform structure to populate</param>
        private static void BuildUniform(ref Brush paint, ref ScissorUniform scissor, float width, float fringe, float strokeThr, ref UniformInfo uniform)
        {
            // Set colors from paint
            uniform.innerCol = paint.InnerColor.ToVector4(true);
            uniform.outerCol = paint.OuterColor.ToVector4(true);

            // Handle scissor settings
            if (scissor.Extent.X < -0.5f || scissor.Extent.Y < -0.5f)
            {
                // Invalid scissor - disable it
                uniform.scissorMat.MakeZero();
                uniform.scissorExt.X = 1.0f;
                uniform.scissorExt.Y = 1.0f;
                uniform.scissorScale.X = 1.0f;
                uniform.scissorScale.Y = 1.0f;
            }
            else
            {
                // Set up scissor transform and dimensions
                uniform.scissorMat = scissor.Transform.Inverse().ToMatrix4x4();
                uniform.scissorExt.X = scissor.Extent.X;
                uniform.scissorExt.Y = scissor.Extent.Y;

                // Calculate scissor scale factors based on fringe
                uniform.scissorScale.X = (float)Math.Sqrt(scissor.Transform.T1 * scissor.Transform.T1 +
                                                     scissor.Transform.T3 * scissor.Transform.T3) / fringe;
                uniform.scissorScale.Y = (float)Math.Sqrt(scissor.Transform.T2 * scissor.Transform.T2 +
                                                     scissor.Transform.T4 * scissor.Transform.T4) / fringe;
            }

            // Set paint parameters
            uniform.extent = paint.Extent;
            uniform.strokeMult = (width * 0.5f + fringe * 0.5f) / fringe;
            uniform.strokeThr = strokeThr;
            uniform.Image = paint.Image;

            // Determine render type based on paint configuration
            if (paint.Image != null)
            {
                uniform.type = RenderType.FillImage;
            }
            else
            {
                uniform.type = (int)RenderType.FillGradient;
                uniform.radius = paint.Radius;
                uniform.feather = paint.Feather;
            }

            // Store inverse paint transform for shader calculations
            uniform.paintMat = paint.Transform.Inverse().ToMatrix4x4();
        }

        /// <summary>
        /// Creates a render call for filling one or more paths
        /// </summary>
        /// <param name="paint">Paint settings to use</param>
        /// <param name="scissor">Scissor settings to use</param>
        /// <param name="fringe">Size of anti-aliasing fringe</param>
        /// <param name="bounds">Bounding rectangle of paths</param>
        /// <param name="paths">Collection of paths to fill</param>
        public void RenderFill(ref Brush paint, ref ScissorUniform scissor, float fringe, Rect bounds, IReadOnlyList<Path> paths)
        {
            var call = new CallInfo {
                Type = CallType.Fill
            };

            // Optimize for single convex path case
            if (paths.Count == 1 && paths[0].Convex)
            {
                call.Type = CallType.ConvexFill;
            }

            // Add fill/stroke info for each path
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];

                var drawCallInfo = new FillStrokeInfo {
                    FillOffset = path.FillOffset,
                    FillCount = path.FillCount,
                    StrokeOffset = path.StrokeOffset,
                    StrokeCount = path.StrokeCount,
                };

                call.FillStrokeInfos.Add(drawCallInfo);
            }

            // Setup shader uniforms based on call type
            if (call.Type == CallType.Fill)
            {
                // Create quad covering bounds for stencil-based fill
                call.TriangleOffset = VertexArray.Count;
                call.TriangleCount = 4;

                // Add vertices for the quad (covering the entire path bounds)
                VertexArray.Add(new Vertex(bounds.Max.X, bounds.Max.Y, 0.5f, 1.0f));
                VertexArray.Add(new Vertex(bounds.Max.X, bounds.Min.Y, 0.5f, 1.0f));
                VertexArray.Add(new Vertex(bounds.Min.X, bounds.Max.Y, 0.5f, 1.0f));
                VertexArray.Add(new Vertex(bounds.Min.X, bounds.Min.Y, 0.5f, 1.0f));

                // Setup stencil shader
                call.UniformInfo.strokeThr = -1.0f;
                call.UniformInfo.type = RenderType.Simple;

                // Setup fill shader
                BuildUniform(ref paint, ref scissor, fringe, fringe, -1.0f, ref call.UniformInfo2);
            }
            else
            {
                // For convex fills, we only need the fill shader
                BuildUniform(ref paint, ref scissor, fringe, fringe, -1.0f, ref call.UniformInfo);
            }

            Calls.Add(call);
        }

        /// <summary>
        /// Creates a render call for stroking one or more paths
        /// </summary>
        /// <param name="paint">Paint settings to use</param>
        /// <param name="scissor">Scissor settings to use</param>
        /// <param name="fringe">Size of anti-aliasing fringe</param>
        /// <param name="strokeWidth">Width of stroke</param>
        /// <param name="paths">Collection of paths to stroke</param>
        public void RenderStroke(ref Brush paint, ref ScissorUniform scissor, float fringe, float strokeWidth, IReadOnlyList<Path> paths)
        {
            var call = new CallInfo {
                Type = CallType.Stroke,
            };

            // Add stroke info for each path
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];

                var drawCallInfo = new FillStrokeInfo {
                    StrokeOffset = path.StrokeOffset,
                    StrokeCount = path.StrokeCount,
                };

                call.FillStrokeInfos.Add(drawCallInfo);
            }

            // Setup shader uniforms based on stencil mode
            if (_stencilStrokes)
            {
                // When using stencil mode, we need two uniforms:
                // One for the stencil pass and one for the cover pass
                BuildUniform(ref paint, ref scissor, strokeWidth, fringe, -1.0f, ref call.UniformInfo);
                BuildUniform(ref paint, ref scissor, strokeWidth, fringe, 1.0f - 0.5f / 255.0f, ref call.UniformInfo2);
            }
            else
            {
                // For non-stencil mode, we only need one uniform
                BuildUniform(ref paint, ref scissor, strokeWidth, fringe, -1.0f, ref call.UniformInfo);
            }

            Calls.Add(call);
        }

        /// <summary>
        /// Creates a render call for drawing triangles directly
        /// </summary>
        /// <param name="paint">Paint settings to use</param>
        /// <param name="scissor">Scissor settings to use</param>
        /// <param name="fringe">Size of anti-aliasing fringe</param>
        /// <param name="triangleOffset">Starting vertex index</param>
        /// <param name="triangleCount">Number of triangles to draw</param>
        public void RenderTriangles(ref Brush paint, ref ScissorUniform scissor, float fringe, int triangleOffset, int triangleCount)
        {
            var call = new CallInfo {
                Type = CallType.Triangles,
                TriangleOffset = triangleOffset,
                TriangleCount = triangleCount
            };

            // Setup shader uniforms for triangle rendering
            BuildUniform(ref paint, ref scissor, 1.0f, fringe, -1.0f, ref call.UniformInfo);
            call.UniformInfo.type = RenderType.Image;

            Calls.Add(call);
        }
    }
}