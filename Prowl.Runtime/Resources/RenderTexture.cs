using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime
{
    public sealed class RenderTexture : EngineObject, ISerializable
    {
        public uint fboId { get; private set; }
        public Texture2D MainTexture => InternalTextures[0];
        public Texture2D[] InternalTextures { get; private set; }
        public Texture2D InternalDepth { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }
        private int numTextures;
        private bool hasDepthAttachment;
        private Texture.TextureImageFormat[] textureFormats;

        public RenderTexture() : base("RenderTexture")
        {
            Width = 0;
            Height = 0;
            numTextures = 0;
            hasDepthAttachment = false;
            textureFormats = new Texture.TextureImageFormat[0];
        }

        public RenderTexture(int Width, int Height, int numTextures = 1, bool hasDepthAttachment = true, Texture.TextureImageFormat[]? formats = null) : base("RenderTexture")
        {
            if (numTextures < 0 || numTextures > Graphics.MaxFramebufferColorAttachments)
                throw new Exception("Invalid number of textures! [0-" + Graphics.MaxFramebufferColorAttachments + "]");

            this.Width = Width;
            this.Height = Height;
            this.numTextures = numTextures;
            this.hasDepthAttachment = hasDepthAttachment;

            if (formats == null) {
                this.textureFormats = new Texture.TextureImageFormat[numTextures];
                for (int i = 0; i < numTextures; i++)
                    this.textureFormats[i] = Texture.TextureImageFormat.Float4;
            } else {
                if (formats.Length != numTextures)
                    throw new ArgumentException("Invalid number of texture formats!");
                this.textureFormats = formats;
            }

            // Generate FBO
            fboId = Graphics.GL.GenFramebuffer();
            if (fboId <= 0)
                throw new Exception("RenderTexture: [ID {fboId}] Failed to generate RenderTexture.");

            Graphics.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

            unsafe {
                // Generate textures
                InternalTextures = new Texture2D[numTextures];
                if (numTextures > 0) {
                    for (int i = 0; i < numTextures; i++) {
                        InternalTextures[i] = new Texture2D((uint)Width, (uint)Height, false, this.textureFormats[i]);
                        Graphics.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, (TextureTarget)InternalTextures[i].Type, InternalTextures[i].Handle, 0);
                    }
                    Graphics.ActivateDrawBuffers(numTextures);
                }

                // Generate depth attachment if requested
                if (hasDepthAttachment) {
                    var depth = new Texture2D((uint)Width, (uint)Height, false, Texture.TextureImageFormat.Depth24);
                    InternalDepth = depth;
                    Graphics.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth.Handle, 0);

                }

                if (Graphics.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                    throw new Exception("RenderTexture: [ID {fboId}] RenderTexture object creation failed.");

                // Unbind FBO
                Graphics.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }

            Graphics.CheckGL();
        }

        public void Begin()
        {
            Graphics.GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
            Graphics.GL.Viewport(0, 0, (uint)Width, (uint)Height);
            Graphics.FrameBufferSize = new Vector2D<int>(Width, Height);

            Graphics.ActivateDrawBuffers(Math.Max(1, numTextures));
        }

        public void End()
        {
            Graphics.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            Graphics.GL.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
            Graphics.FrameBufferSize = new Vector2D<int>(Width, Height);
        }

        public override void OnDispose()
        {
            if (fboId <= 0) return;
            foreach (var texture in InternalTextures)
                Graphics.GL.DeleteTexture(texture.Handle);

            //if(hasDepthAttachment) // Should auto dispose of Depth
            //    Graphics.GL.DeleteRenderbuffer(InternalDepth.Handle);
            Graphics.GL.DeleteFramebuffer(fboId);

            Graphics.CheckGL();
        }

        public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag();
            compoundTag.Add("Width", new IntTag(Width));
            compoundTag.Add("Height", new IntTag(Height));
            compoundTag.Add("NumTextures", new IntTag(numTextures));
            compoundTag.Add("HasDepthAttachment", new ByteTag((byte)(hasDepthAttachment ? 1 : 0)));
            ListTag textureFormatsTag = new ListTag();
            foreach (var format in textureFormats)
                textureFormatsTag.Add(new ByteTag((byte)format));
            compoundTag.Add("TextureFormats", textureFormatsTag);
            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            Width = value["Width"].IntValue;
            Height = value["Height"].IntValue;
            numTextures = value["NumTextures"].IntValue;
            hasDepthAttachment = value["HasDepthAttachment"].ByteValue == 1;
            textureFormats = new Texture.TextureImageFormat[numTextures];
            var textureFormatsTag = value.Get<ListTag>("TextureFormats");
            for (int i = 0; i < numTextures; i++)
                textureFormats[i] = (Texture.TextureImageFormat)textureFormatsTag[i].ByteValue;

            var param = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(Texture.TextureImageFormat[]) };
            var values = new object[] { Width, Height, numTextures, hasDepthAttachment, textureFormats };
            typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
        }
    }
}
