using Prowl.PaperUI.Graphics;
using OpenTK.Graphics.OpenGL;
using System.Drawing;
using System.Numerics;

namespace OpenTKSample
{
    internal class PaperRenderer : ICanvasRenderer
    {
        private const int MAX_VERTICES = 8192;

        private readonly Shader _shader;
        private BufferObject<Vertex> _vertexBuffer;
        private readonly VertexArrayObject _vao;
        private readonly bool _edgeAntiAlias, _stencilStrokes;
        private readonly int[] _viewPortValues = new int[4];

        public bool EdgeAntiAlias => _edgeAntiAlias;

        public unsafe PaperRenderer(bool edgeAntiAlias = true, bool stencilStrokes = true)
        {
            _edgeAntiAlias = edgeAntiAlias;
            _stencilStrokes = stencilStrokes;

            var defines = new Dictionary<string, string>();
            if (edgeAntiAlias)
            {
                defines["EDGE_AA"] = "1";
            }

            _shader = new Shader("shader.vert", "shader.frag", defines);
            _vertexBuffer = new BufferObject<Vertex>(MAX_VERTICES, BufferTarget.ArrayBuffer, true);
            _vao = new VertexArrayObject(sizeof(Vertex));
        }

        ~PaperRenderer() => Dispose(false);

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _vao.Dispose();
            _vertexBuffer.Dispose();
            _shader.Dispose();
        }

        public object CreateTexture(int width, int height) => new Texture(width, height);

        public Point GetTextureSize(object texture)
        {
            var t = (Texture)texture;
            return new Point(t.Width, t.Height);
        }

        public void SetTextureData(object texture, Rectangle bounds, byte[] data)
        {
            var t = (Texture)texture;
            t.SetData(bounds, data);
        }

        static int _scissorMatLoc = 0;
        static int _paintMatLoc = 0;
        static int _innerColLoc = 0;
        static int _outerColLoc = 0;
        static int _scissorExtLoc = 0;
        static int _scissorScaleLoc = 0;
        static int _extentLoc = 0;
        static int _radiusLoc = 0;
        static int _featherLoc = 0;
        static int _typeLoc = 0;
        static int _strokeMultLoc = 0;
        static int _strokeThrLoc = 0;
        static int _textureLoc = 0;
        static int _transformMatLoc = 0;
        static bool _isInit = false;

        private void SetUniform(ref UniformInfo uniform)
        {
            _shader.SetUniform(_scissorMatLoc, uniform.scissorMat);
            _shader.SetUniform(_paintMatLoc, uniform.paintMat);
            _shader.SetUniform(_innerColLoc, uniform.innerCol);
            _shader.SetUniform(_outerColLoc, uniform.outerCol);
            _shader.SetUniform(_scissorExtLoc, uniform.scissorExt);
            _shader.SetUniform(_scissorScaleLoc, uniform.scissorScale);
            _shader.SetUniform(_extentLoc, uniform.extent);
            _shader.SetUniform(_radiusLoc, uniform.radius);
            _shader.SetUniform(_featherLoc, uniform.feather);
            _shader.SetUniform(_typeLoc, (int)uniform.type);

            if (uniform.Image != null)
            {
                var texture = (Texture)uniform.Image;
                texture.Bind();
            }
            else
            {
                GL.BindTexture(TextureTarget.Texture2d, 0);
            }

            if (_edgeAntiAlias)
            {
                _shader.SetUniform(_strokeMultLoc, uniform.strokeMult);
                _shader.SetUniform(_strokeThrLoc, uniform.strokeThr);
            }
        }

        private void ProcessFill(CallInfo call)
        {
            // Draw shapes
            GL.Enable(EnableCap.StencilTest);
            GL.StencilMask(0xff);
            GL.StencilFunc(StencilFunction.Always, 0, 0xff);
            GL.ColorMask(false, false, false, false);

            SetUniform(ref call.UniformInfo);

            // set bindpoint for solid loc
            GL.StencilOpSeparate(TriangleFace.Front, StencilOp.Keep, StencilOp.Keep, StencilOp.IncrWrap);
            GL.StencilOpSeparate(TriangleFace.Back, StencilOp.Keep, StencilOp.Keep, StencilOp.DecrWrap);

            GL.Disable(EnableCap.CullFace);

            for (var i = 0; i < call.FillStrokeInfos.Count; i++)
            {
                call.FillStrokeInfos[i].DrawFill(PrimitiveType.TriangleFan);
            }

            GL.Enable(EnableCap.CullFace);

            // Draw anti-aliased pixels
            GL.ColorMask(true, true, true, true);

            SetUniform(ref call.UniformInfo2);

            if (_edgeAntiAlias)
            {
                GL.StencilFunc(StencilFunction.Equal, 0, 0xff);
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);

                // Draw fringes
                for (var i = 0; i < call.FillStrokeInfos.Count; i++)
                {
                    call.FillStrokeInfos[i].DrawStroke(PrimitiveType.TriangleStrip);
                }
            }

            // Draw fill
            GL.StencilFunc(StencilFunction.Notequal, 0, 0xff);
            GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);

            call.DrawTriangles(PrimitiveType.TriangleStrip);

            GL.Disable(EnableCap.StencilTest);
        }

        private void ProcessConvexFill(CallInfo call)
        {
            SetUniform(ref call.UniformInfo);

            for (var i = 0; i < call.FillStrokeInfos.Count; i++)
            {
                var fillStrokeInfo = call.FillStrokeInfos[i];

                fillStrokeInfo.DrawFill(PrimitiveType.TriangleFan);
                fillStrokeInfo.DrawStroke(PrimitiveType.TriangleStrip);
            }
        }

        private void ProcessStroke(CallInfo call)
        {
            if (_stencilStrokes)
            {
                // Fill the stroke base without overlap
                GL.Enable(EnableCap.StencilTest);
                GL.StencilMask(0xff);
                GL.StencilFunc(StencilFunction.Equal, 0, 0xff);
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Incr);

                SetUniform(ref call.UniformInfo2);

                for (var i = 0; i < call.FillStrokeInfos.Count; i++)
                {
                    call.FillStrokeInfos[i].DrawStroke(PrimitiveType.TriangleStrip);
                }

                // Draw anti-aliased pixels.
                SetUniform(ref call.UniformInfo);

                GL.StencilFunc(StencilFunction.Equal, 0, 0xff);
                GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);

                for (var i = 0; i < call.FillStrokeInfos.Count; i++)
                {
                    call.FillStrokeInfos[i].DrawStroke(PrimitiveType.TriangleStrip);
                }

                // Clear stencil buffer.
                GL.ColorMask(false, false, false, false);

                GL.StencilFunc(StencilFunction.Always, 0, 0xff);
                GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);

                for (var i = 0; i < call.FillStrokeInfos.Count; i++)
                {
                    call.FillStrokeInfos[i].DrawStroke(PrimitiveType.TriangleStrip);
                }

                GL.ColorMask(true, true, true, true);

                GL.Disable(EnableCap.StencilTest);
            }
            else
            {
                SetUniform(ref call.UniformInfo);

                for (var i = 0; i < call.FillStrokeInfos.Count; i++)
                {
                    call.FillStrokeInfos[i].DrawStroke(PrimitiveType.TriangleStrip);
                }
            }
        }

        private void ProcessTriangles(CallInfo call)
        {
            SetUniform(ref call.UniformInfo);

            call.DrawTriangles(PrimitiveType.Triangles);
        }

        public void Draw(float devicePixelRatio, IEnumerable<CallInfo> calls, Vertex[] vertexes)
        {

            if (!_isInit)
            {
                _isInit = true;
                _scissorMatLoc = _shader.GetUniformLoc("scissorMat");
                _paintMatLoc = _shader.GetUniformLoc("paintMat");
                _innerColLoc = _shader.GetUniformLoc("innerCol");
                _outerColLoc = _shader.GetUniformLoc("outerCol");
                _scissorExtLoc = _shader.GetUniformLoc("scissorExt");
                _scissorScaleLoc = _shader.GetUniformLoc("scissorScale");
                _extentLoc = _shader.GetUniformLoc("extent");
                _radiusLoc = _shader.GetUniformLoc("radius");
                _featherLoc = _shader.GetUniformLoc("feather");
                _typeLoc = _shader.GetUniformLoc("type");
                _strokeMultLoc = _shader.GetUniformLoc("strokeMult");
                _strokeThrLoc = _shader.GetUniformLoc("strokeThr");
                _textureLoc = _shader.GetUniformLoc("tex");
                _transformMatLoc = _shader.GetUniformLoc("transformMat");
            }

            // Setup required GL state
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
            GL.FrontFace(FrontFaceDirection.Ccw);
            GL.Enable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.ColorMask(true, true, true, true);
            GL.StencilMask(0xffffffff);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            GL.StencilFunc(StencilFunction.Always, 0, 0xffffffff);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2d, 0);

            // Bind and update vertex buffer
            if (_vertexBuffer.Size < vertexes.Length)
            {
                _vertexBuffer = new BufferObject<Vertex>(vertexes.Length, BufferTarget.ArrayBuffer, true);
            }

            _vertexBuffer.Bind();
            _vertexBuffer.SetData(vertexes, 0, vertexes.Length);

            // Setup vao
            _vao.Bind();
            var location = _shader.GetAttribLocation("vertex");
            _vao.VertexAttribPointer((uint)location, 2, VertexAttribPointerType.Float, false, 0);

            location = _shader.GetAttribLocation("tcoord");
            _vao.VertexAttribPointer((uint)location, 2, VertexAttribPointerType.Float, false, 8);

            // Setup shader
            _shader.Use();
            _shader.SetUniform(_textureLoc, 0);

            GL.GetInteger(GetPName.Viewport, _viewPortValues);

            var transform = Matrix4x4.CreateOrthographicOffCenter(0, _viewPortValues[2], _viewPortValues[3], 0, 0, -1);
            _shader.SetUniform(_transformMatLoc, transform);

            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

            // Process calls
            foreach (var call in calls)
            {
                switch (call.Type)
                {
                    case CallType.Fill:
                        ProcessFill(call);
                        break;
                    case CallType.ConvexFill:
                        ProcessConvexFill(call);
                        break;
                    case CallType.Stroke:
                        ProcessStroke(call);
                        break;
                    case CallType.Triangles:
                        ProcessTriangles(call);
                        break;
                }
            }

            GLUtility.CheckError();
        }
    }
}
