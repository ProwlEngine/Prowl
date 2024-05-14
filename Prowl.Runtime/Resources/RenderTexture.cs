using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Primitives;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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

            if (formats == null)
            {
                this.textureFormats = new TextureImageFormat[numTextures];
                for (int i = 0; i < numTextures; i++)
                    this.textureFormats[i] = TextureImageFormat.Color4b;
            }
            else
            {
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
            Graphics.Viewport(Window.InternalWindow.Size.X, Window.InternalWindow.Size.Y);
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

        #region Pool

        private struct RenderTextureKey(int width, int height, TextureImageFormat[] format)
        {
            public int Width = width;
            public int Height = height;
            public TextureImageFormat[] Format = format;

            public override bool Equals([NotNullWhen(true)] object? obj)
            {
                if (obj is RenderTextureKey key)
                {
                    if (Width == key.Width && Height == key.Height && Format.Length == key.Format.Length)
                    {
                        for (int i = 0; i < Format.Length; i++)
                            if (Format[i] != key.Format[i])
                                return false;
                        return true;
                    }
                }
                return false;
            }
            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + Width.GetHashCode();
                hash = hash * 23 + Height.GetHashCode();
                foreach (var format in Format)
                    hash = hash * 23 + ((int)format).GetHashCode();
                return hash;
            }
            public static bool operator ==(RenderTextureKey left, RenderTextureKey right) => left.Equals(right);
            public static bool operator !=(RenderTextureKey left, RenderTextureKey right) => !(left == right);
        }

        private static Dictionary<RenderTextureKey, List<(RenderTexture, long frameCreated)>> pool = [];
        private const int MaxUnusedFrames = 10;

        public static RenderTexture GetTemporaryRT(int width, int height, TextureImageFormat[] format)
        {
            var key = new RenderTextureKey(width, height, format);

            if (pool.TryGetValue(key, out var list) && list.Count > 0)
            {
                int i = list.Count - 1;
                RenderTexture renderTexture = list[i].Item1;
                list.RemoveAt(i);
                return renderTexture;
            }

            return new RenderTexture(width, height, 1, false, format);
        }

        public static void ReleaseTemporaryRT(RenderTexture renderTexture)
        {
            var key = new RenderTextureKey(renderTexture.Width, renderTexture.Height, renderTexture.InternalTextures.Select(t => t.ImageFormat).ToArray());

            if (!pool.TryGetValue(key, out var list))
            {
                list = [];
                pool[key] = list;
            }

            list.Add((renderTexture, Time.frameCount));
        }

        public static void UpdatePool()
        {
            var disposableTextures = new List<RenderTexture>();
            foreach (var pair in pool)
            {
                for (int i = pair.Value.Count - 1; i >= 0; i--)
                {
                    var (renderTexture, frameCreated) = pair.Value[i];
                    if (Time.frameCount - frameCreated > MaxUnusedFrames)
                    {
                        disposableTextures.Add(renderTexture);
                        pair.Value.RemoveAt(i);
                    }
                }
            }

            foreach (var renderTexture in disposableTextures)
                renderTexture.Destroy();
        }

        #endregion

    }
}
