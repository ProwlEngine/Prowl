using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class CommandBuffer
    {
        public string Name;

        // Holds a list of structs which implement RenderingCommand.ExecuteCommand() to avoid filling it with anonymous lambdas.
        // TODO: While the struct-based approach is better than lambdas, there is still some overhead when the structs get boxed, which is not ideal. 
        private List<RenderingCommand> buffer = new();

        public IEnumerable<RenderingCommand> Buffer => buffer;

        public CommandBuffer()
        {
            Name = "New Command Buffer";
        }

        public CommandBuffer(string name)
        {
            this.Name = name;
        }

        public void SetRenderTarget(Framebuffer framebuffer)
        {
            buffer.Add(new SetFramebufferCommand() { 
                Framebuffer = framebuffer
            });
        }

        public void SetRenderTarget(RenderTexture renderTarget)
        {
            buffer.Add(new SetFramebufferCommand() { 
                Framebuffer = renderTarget.Framebuffer
            });
        }

        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, int attachment = -1, float depth = 1, byte stencil = 0)
        {  
            buffer.Add(new ClearCommand()
            {
                ClearDepthStencil = clearDepth,
                ClearColor = clearColor,
                ColorAttachment = attachment,
                Depth = depth,
                Stencil = stencil,
                BackgroundColor = backgroundColor
            });
        }

        public void SetMaterial(Material material, int pass = 0)
        {
            buffer.Add(new SetMaterialCommand()
            {
                Material = material,
                Pass = pass,
            });
        }

        public void Blit()
        {
            throw new NotImplementedException();
        }

        public void DrawSingle(IGeometryDrawData drawData, int indexCount = -1, uint indexOffset = 0)
        {
            SetDrawData(drawData);
            UploadResourceSets();
            ManualDraw((uint)(indexCount <= 0 ? drawData.IndexCount : indexCount), indexOffset, 1, 0, 0);
        }

        public void DrawMeshIndirect()
        {
            throw new NotImplementedException();
        }

        public void SetDrawData(IGeometryDrawData drawData)
        {
            buffer.Add(new SetDrawDataCommand() {
                DrawData = drawData,
            });
        }

        public void ManualDraw(uint indexCount, uint indexOffset, uint instanceCount, uint instanceStart, int vertexOffset)
        {
            buffer.Add(new ManualDrawCommand() {
                IndexCount = indexCount,
                IndexOffset = indexOffset,
                InstanceCount = instanceCount,
                InstanceStart = instanceStart,
                VertexOffset = vertexOffset
            });
        }

        public void SetPipeline(ShaderPass pass, ShaderVariant variant)
        {
            buffer.Add(new SetPipelineCommand() {
                Pass = pass,
                Variant = variant
            });
        }

        public void UploadResourceSet(uint slot)
        {
            buffer.Add(new SetResourceCommand() {
                Slot = slot
            });
        }

        public void UploadResourceSets()
        {
            buffer.Add(new SetResourcesCommand());
        }

        public void PushDebugGroup(string name)
        {
            buffer.Add(new DebugGroupCommand() { 
                Name = name, 
                Pop = false 
            });
        }

        public void PopDebugGroup()
        {
            buffer.Add(new DebugGroupCommand() { 
                Pop = true 
            });
        }

        public void ResolveMultisampledTexture(RenderTexture src, RenderTexture dest)
        {
            buffer.Add(new ResolveCommand() { 
                RTResolve = true, 
                RTSource = src, 
                RTDestination = dest 
            });
        }

        public void ResolveMultisampledTexture(Texture src, Texture dest)
        {
            buffer.Add(new ResolveCommand() { 
                RTResolve = false, 
                Source = src, 
                Destination = dest 
            });
        }

        public void SetViewport(int viewport, int x, int y, int width, int height, int z, int depth)
        {
            buffer.Add(new SetViewportCommand() { 
                SetFull = false, 
                Index = viewport, 
                X = x, Y = y, Z = z, 
                Width = width, Height = height, Depth = depth
            });
        }

        public void SetViewports(int x, int y, int width, int height, int z, int depth)
        {
            buffer.Add(new SetViewportCommand() { 
                SetFull = false, 
                Index = -1, 
                X = x, Y = y, Z = z, 
                Width = width, Height = height, Depth = depth
            });
        }

        public void SetFullViewport(int index = 0)
        {
            buffer.Add(new SetViewportCommand() { 
                SetFull = true, 
                Index = index 
            });
        }

        public void SetFullViewports()
        {
            buffer.Add(new SetViewportCommand() { 
                SetFull = true, 
                Index = -1 
            });
        }

        public void SetScissorRect(int index, int x, int y, int width, int height)
        {
            buffer.Add(new ScissorCommand() { 
                SetFull = false, 
                Index = index,
                X = x, Y = y,
                Width = width, Height = height
            });
        }

        public void SetScissorRects(int x, int y, int width, int height)
        {
            buffer.Add(new ScissorCommand() { 
                SetFull = false, 
                Index = -1,
                X = x, Y = y,
                Width = width, Height = height
            });
        }

        public void SetFullScissorRect(int index)
        {
            buffer.Add(new ScissorCommand() {
                SetFull = true, 
                Index = index 
            });
        }

        public void SetFullScissorRects()
        {
            buffer.Add(new ScissorCommand() { 
                SetFull = true, 
                Index = -1 
            });
        }

        public void SetScissor(bool active)
        {
            buffer.Add(new ScissorCommand() {
                SetActive = active
            });
        }

        public void SetKeyword(string keyword, string value)
        {
            buffer.Add(new SetKeywordCommand() {
                Name = keyword,
                Value = value
            });
        }

        public void SetWireframe(bool wireframe)
        {
            buffer.Add(new SetFillCommand()
            {
                Wireframe = wireframe
            });
        }

        public void SetTexture(string name, Texture texture)
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

        public void Clear()
        {
            buffer.Clear();
        }
    }
}