using Prowl.Runtime.Rendering;
using Silk.NET.Maths;
using System;

namespace Prowl.Runtime
{
    public sealed class RenderTexture : EngineObject, ISerializable
    {
        public GraphicsFrameBuffer frameBuffer { get; private set; }
        public Texture2D MainTexture => InternalTextures[0];
        public Texture2D[] InternalTextures { get; private set; }
        public Texture2D InternalDepth { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }
        private int numTextures;
        private bool hasDepthAttachment;
        private TextureImageFormat[] textureFormats;

        public RenderTexture() : base("RenderTexture")
        {
            Width = 0;
            Height = 0;
            numTextures = 0;
            hasDepthAttachment = false;
            textureFormats = new TextureImageFormat[0];
        }

        public RenderTexture(int Width, int Height, int numTextures = 1, bool hasDepthAttachment = true, TextureImageFormat[]? formats = null) : base("RenderTexture")
        {
            if (numTextures < 0 || numTextures > Graphics.MaxFramebufferColorAttachments)
                throw new Exception("Invalid number of textures! [0-" + Graphics.MaxFramebufferColorAttachments + "]");

            this.Width = Width;
            this.Height = Height;
            this.numTextures = numTextures;
            this.hasDepthAttachment = hasDepthAttachment;

            if (formats == null) {
                this.textureFormats = new TextureImageFormat[numTextures];
                for (int i = 0; i < numTextures; i++)
                    this.textureFormats[i] = TextureImageFormat.Color4b;
            } else {
                if (formats.Length != numTextures)
                    throw new ArgumentException("Invalid number of texture formats!");
                this.textureFormats = formats;
            }

            GraphicsFrameBuffer.Attachment[] attachments = new GraphicsFrameBuffer.Attachment[numTextures + (hasDepthAttachment ? 1 : 0)];
            InternalTextures = new Texture2D[numTextures];
            for (int i = 0; i < numTextures; i++)
            {
                InternalTextures[i] = new Texture2D((uint)Width, (uint)Height, false, this.textureFormats[i]);
                InternalTextures[i].SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
                InternalTextures[i].SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
                attachments[i] = new GraphicsFrameBuffer.Attachment { texture = InternalTextures[i].Handle, isDepth = false };
            }

            if (hasDepthAttachment)
            {
                InternalDepth = new Texture2D((uint)Width, (uint)Height, false, TextureImageFormat.Depth24);
                attachments[numTextures] = new GraphicsFrameBuffer.Attachment { texture = InternalDepth.Handle, isDepth = true };
            }

            frameBuffer = Graphics.Device.CreateFramebuffer(attachments);
        }

        public void Begin()
        {
            Graphics.Device.BindFramebuffer(frameBuffer);
            Graphics.Viewport(Width, Height);
            Graphics.FrameBufferSize = new Vector2D<int>(Width, Height);
        }

        public void End()
        {
            Graphics.Device.UnbindFramebuffer();
            Graphics.Viewport(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
            Graphics.FrameBufferSize = new Vector2D<int>(Width, Height);
        }

        public override void OnDispose()
        {
            if (frameBuffer == null) return;
            foreach (var texture in InternalTextures)
                texture.Dispose();

            //if(hasDepthAttachment) // Should auto dispose of Depth
            //    Graphics.GL.DeleteRenderbuffer(InternalDepth.Handle);
            frameBuffer.Dispose();
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();
            compoundTag.Add("Width", new(Width));
            compoundTag.Add("Height", new(Height));
            compoundTag.Add("NumTextures", new(numTextures));
            compoundTag.Add("HasDepthAttachment", new((byte)(hasDepthAttachment ? 1 : 0)));
            SerializedProperty textureFormatsTag = SerializedProperty.NewList();
            foreach (var format in textureFormats)
                textureFormatsTag.ListAdd(new((byte)format));
            compoundTag.Add("TextureFormats", textureFormatsTag);
            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Width = value["Width"].IntValue;
            Height = value["Height"].IntValue;
            numTextures = value["NumTextures"].IntValue;
            hasDepthAttachment = value["HasDepthAttachment"].ByteValue == 1;
            textureFormats = new TextureImageFormat[numTextures];
            var textureFormatsTag = value.Get("TextureFormats");
            for (int i = 0; i < numTextures; i++)
                textureFormats[i] = (TextureImageFormat)textureFormatsTag[i].ByteValue;

            var param = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(TextureImageFormat[]) };
            var values = new object[] { Width, Height, numTextures, hasDepthAttachment, textureFormats };
            typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
        }
    }
}
