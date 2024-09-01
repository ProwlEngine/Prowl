using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Veldrid;

namespace Prowl.Runtime
{
    public class CommandBuffer
    {
        private CommandList _commandList;
        private Framebuffer _activeFramebuffer;
        private KeywordState _keywordState;

        private ShaderPass _activePass => _pipelineDescription.pass;
        private ShaderVariant _activeVariant => _pipelineDescription.variant;

        private IGeometryDrawData _activeDrawData;

        private GraphicsPipelineDescription _pipelineDescription;

        private PolygonFillMode _fill;
        private PrimitiveTopology _topology;
        private bool _scissor;

        private GraphicsPipeline _graphicsPipeline;
        private BindableResourceSet _pipelineResources;
        private Pipeline _actualActivePipeline;


        public string Name
        {
            get => _commandList.Name;
            set => _commandList.Name = value;
        }

        public CommandBuffer()
        {
            Name = "New Command Buffer";
            _keywordState = KeywordState.Default;
        }

        public CommandBuffer(string name)
        {
            this.Name = name;
        }

        public void SetRenderTarget(Framebuffer framebuffer)
        {
            _activeFramebuffer = framebuffer;
            _pipelineDescription.output = _activeFramebuffer.OutputDescription;
            _commandList.SetFramebuffer(framebuffer);
        }

        public void SetRenderTarget(RenderTexture renderTarget)
        {
            SetRenderTarget(renderTarget.Framebuffer);
        }

        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, int attachment = -1, float depth = 1, byte stencil = 0)
        {
            if (clearDepth)
                _commandList.ClearDepthStencil(depth, stencil);

            RgbaFloat colorF = new RgbaFloat(backgroundColor);

            if (clearColor)
            {
                if (attachment < 0)
                {
                    for (uint i = 0; i < _activeFramebuffer.ColorTargets.Length; i++)
                        _commandList.ClearColorTarget(i, colorF);
                }
                else
                {
                    _commandList.ClearColorTarget((uint)attachment, colorF);
                }
            }
        }

        public void SetMaterial(Material material, int pass = 0)
        {
            _pipelineDescription.pass = material.Shader.Res.GetPass(pass);
            _pipelineDescription.variant = _pipelineDescription.pass.GetVariant(_keywordState);
        }

        public void DrawSingle(IGeometryDrawData drawData, int indexCount = -1, uint indexOffset = 0)
        {
            SetDrawData(drawData);
            BindResources();
            ManualDraw((uint)(indexCount <= 0 ? drawData.IndexCount : indexCount), indexOffset, 1, 0, 0);
        }

        public void SetDrawData(IGeometryDrawData drawData)
        {
            _activeDrawData = drawData;
        }

        public void ManualDraw(uint indexCount, uint indexOffset, uint instanceCount, uint instanceStart, int vertexOffset)
        {
            _commandList.DrawIndexed(indexCount, instanceCount, indexOffset, vertexOffset, instanceStart);
        }

        public void SetPass(ShaderPass pass)
        {
            _pipelineDescription.pass = pass;
            _pipelineDescription.variant = _pipelineDescription.pass.GetVariant(_keywordState);
        }

        public void BindResources()
        {
            _pipelineResources.Bind(_commandList);
        }

        public void UpdateBuffer(string name)
        {
            _pipelineResources.UpdateBuffer(_commandList, name);
        }

        public void PushDebugGroup(string name)
        {
            _commandList.PushDebugGroup(name);
        }

        public void PopDebugGroup()
        {
            _commandList.PopDebugGroup();
        }

        public void ResolveMultisampledTexture(Texture src, Texture dest)
        {
            if (!src.Equals(dest, false))
                throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

            _commandList.ResolveTexture(src.InternalTexture, dest.InternalTexture);
        }

        public void ResolveMultisampledTexture(RenderTexture src, RenderTexture dest)
        {
            if (!src.FormatEquals(dest, false))
                throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

            for (int i = 0; i < src.ColorBuffers.Length; i++)
                _commandList.ResolveTexture(src.ColorBuffers[i].InternalTexture, dest.ColorBuffers[i].InternalTexture);
        }

        public void SetViewport(uint viewport, int x, int y, int width, int height, int z, int depth)
        {
            _commandList.SetViewport(viewport, new Viewport(x, y, width, height, z, depth));
        }

        public void SetViewports(int x, int y, int width, int height, int z, int depth)
        {
            _commandList.SetViewports(new Viewport(x, y, width, height, z, depth));
        }

        public void SetFullViewport(uint index = 0)
        {
            _commandList.SetFullViewport(index);
        }

        public void SetFullViewports()
        {
            _commandList.SetFullViewports();
        }

        public void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            _commandList.SetScissorRect(index, x, y, width, height);
        }

        public void SetScissorRects(uint x, uint y, uint width, uint height)
        {
            _commandList.SetScissorRects(x, y, width, height);
        }

        public void SetFullScissorRect(uint index)
        {
            _commandList.SetFullScissorRect(index);
        }

        public void SetFullScissorRects()
        {
            _commandList.SetFullScissorRects();
        }

        public void SetScissor(bool active)
        {
            _scissor = active;
        }

        public void SetKeyword(string keyword, string value)
        {
            _keywordState.SetKey(keyword, value);
            _pipelineDescription.variant = _pipelineDescription.pass.GetVariant(_keywordState);
        }

        public void SetWireframe(bool wireframe)
        {
            _fill = wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid;
        }



        public void SetTexture(string name, AssetRef<Texture> texture)
        {
            buffer.Add(new SetTexturePropertyCommand()
            {
                Name = name,
                TextureValue = texture,
            });
        }

        public void SetFloat(string name, float value)
        {
            buffer.Add(new SetPropertyCommand()
            {
                Name = name,
                Value = new Vector4(value)
            });
        }

        public void SetVector(string name, Vector4 value)
        {
            buffer.Add(new SetPropertyCommand()
            {
                Name = name,
                Value = value
            });
        }

        public void SetVectorArray(string name, Vector4[] values)
        {
            buffer.Add(new SetPropertyArrayCommand()
            {
                Name = name,
                Value = values
            });
        }

        public void SetVector(string name, Vector3 value)
        {
            buffer.Add(new SetPropertyCommand()
            {
                Name = name,
                Value = new(value.x, value.y, value.z, 0)
            });
        }

        public void SetVector(string name, Vector2 value)
        {
            buffer.Add(new SetPropertyCommand()
            {
                Name = name,
                Value = new(value.x, value.y, 0, 0)
            });
        }

        public void SetColor(string name, Color color)
        {
            buffer.Add(new SetPropertyCommand()
            {
                Name = name,
                Value = color
            });
        }

        public void SetMatrix(string name, Matrix4x4 matrix)
        {
            buffer.Add(new SetMatrixPropertyCommand()
            {
                Name = name,
                MatrixValue = matrix
            });
        }

        public void SetMatrixArray(string name, Matrix4x4[] matrices)
        {
            buffer.Add(new SetMatrixArrayPropertyCommand()
            {
                Name = name,
                MatrixValue = matrices
            });
        }
    }
}
