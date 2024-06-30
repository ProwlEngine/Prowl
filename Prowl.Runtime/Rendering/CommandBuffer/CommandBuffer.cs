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

        public void DrawMesh(Mesh mesh, int indexCount = -1, int indexOffset = -1)
        {
            buffer.Add(new DrawCommand()
            {
                Mesh = mesh,
                IndexCount = indexCount,
                IndexOffset = indexOffset
            });
        }

        public void DrawMeshIndirect()
        {
            throw new NotImplementedException();
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
            buffer.Add(new ViewportCommand() { 
                SetFull = false, 
                Index = viewport, 
                X = x, Y = y, Z = z, 
                Width = width, Height = height, Depth = depth
            });
        }

        public void SetViewports(int x, int y, int width, int height, int z, int depth)
        {
            buffer.Add(new ViewportCommand() { 
                SetFull = false, 
                Index = -1, 
                X = x, Y = y, Z = z, 
                Width = width, Height = height, Depth = depth
            });
        }

        public void SetFullViewport(int index = 0)
        {
            buffer.Add(new ViewportCommand() { 
                SetFull = true, 
                Index = index 
            });
        }

        public void SetFullViewports()
        {
            buffer.Add(new ViewportCommand() { 
                SetFull = true, 
                Index = -1 
            });
        }

        public void SetScissorRect(int index, int x, int y, int width, int height)
        {
            buffer.Add(new ScissorCommand() { 
                Disable = false,
                SetFull = false, 
                Index = index,
                X = x, Y = y,
                Width = width, Height = height
            });
        }

        public void SetScissorRects(int x, int y, int width, int height)
        {
            buffer.Add(new ScissorCommand() { 
                Disable = false,
                SetFull = false, 
                Index = -1,
                X = x, Y = y,
                Width = width, Height = height
            });
        }

        public void SetFullScissorRect(int index)
        {
            buffer.Add(new ScissorCommand() { 
                Disable = false,
                SetFull = true, 
                Index = index 
            });
        }

        public void SetFullScissorRects()
        {
            buffer.Add(new ScissorCommand() { 
                Disable = false,
                SetFull = true, 
                Index = -1 
            });
        }

        public void DisableScissorRect()
        {
            buffer.Add(new ScissorCommand() { 
                Disable = true, 
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