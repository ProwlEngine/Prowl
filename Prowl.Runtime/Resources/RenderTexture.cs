using Prowl.Runtime.Serialization;
using Prowl.Runtime.Utils;
using Raylib_cs;
using System;

namespace Prowl.Runtime.Resources
{
    public sealed class RenderTexture : EngineObject, ISerializable
    {
        public uint fboId { get; private set; }
        public Raylib_cs.Texture2D[] InternalTextures { get; private set; }
        public Raylib_cs.Texture2D InternalDepth { get; private set; }

        public int Width;
        public int Height;
        private int numTextures;
        private bool hasDepthAttachment;
        private PixelFormat[] textureFormats;

        public RenderTexture() : base("RenderTexture") 
        { 
            Width = 0;
            Height = 0;
            numTextures = 0;
            hasDepthAttachment = false;
            textureFormats = new PixelFormat[0];
        }

        public RenderTexture(int Width, int Height, int numTextures = 1, bool hasDepthAttachment = true, PixelFormat[]? formats = null) : base("RenderTexture")
        {
            if (numTextures < 0 || numTextures > 8)
                throw new Exception("Invalid number of textures! [0-8]");

            this.Width = Width;
            this.Height = Height;
            this.numTextures = numTextures;
            this.hasDepthAttachment = hasDepthAttachment;

            if (formats == null)
            {
                textureFormats = new PixelFormat[numTextures];
                for (int i = 0; i < numTextures; i++)
                    textureFormats[i] = PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
            }
            else
            {
                if (formats.Length != numTextures)
                    throw new ArgumentException("Invalid number of texture formats!");
                textureFormats = formats;
            }

            // Generate FBO
            fboId = Rlgl.rlLoadFramebuffer(Width, Height);
            if (fboId <= 0)
                throw new Exception("RenderTexture: [ID {fboId}] Failed to generate RenderTexture.");

            Rlgl.rlEnableFramebuffer(fboId);

            unsafe
            {
                // Generate textures
                InternalTextures = new Raylib_cs.Texture2D[numTextures];
                for (int i = 0; i < numTextures; i++)
                {
                    InternalTextures[i].id = Rlgl.rlLoadTexture(null, Width, Height, textureFormats[i], 1);
                    if (InternalTextures[i].id <= 0) throw new Exception("RenderTexture: [ID {fboId}] Failed to generate Texture for RenderTexture.");
                    InternalTextures[i].format = textureFormats[i];
                    InternalTextures[i].width = Width;
                    InternalTextures[i].height = Height;
                    InternalTextures[i].mipmaps = 1;
                    Raylib.SetTextureFilter(InternalTextures[i], TextureFilter.TEXTURE_FILTER_BILINEAR);
                    Raylib.SetTextureWrap(InternalTextures[i], TextureWrap.TEXTURE_WRAP_CLAMP);

                    Rlgl.rlFramebufferAttach(fboId, InternalTextures[i].id, FramebufferAttachType.RL_ATTACHMENT_COLOR_CHANNEL0 + i, FramebufferAttachTextureType.RL_ATTACHMENT_TEXTURE2D, 0);
                }
                Rlgl.rlActiveDrawBuffers(numTextures);

                // Generate depth attachment if requested
                if (hasDepthAttachment)
                {
                    var depth = new Raylib_cs.Texture2D();
                    depth.id = Rlgl.rlLoadTextureDepth(Width, Height, false);
                    depth.format = (PixelFormat)19;
                    depth.width = Width;
                    depth.height = Height;
                    depth.mipmaps = 1;
                    InternalDepth = depth;
                    //Raylib.SetTextureFilter(depth, TextureFilter.TEXTURE_FILTER_POINT);
                    // 0x2800 = GL_TEXTURE_MAG_FILTER
                    // 0x2801 = GL_TEXTURE_MIN_FILTER
                    // 0x2802 = L_TEXTURE_WRAP_S
                    // 0x2803 = L_TEXTURE_WRAP_T
                    // 0x2600 = GL_NEAREST
                    // 0x812F = GL_CLAMP_TO_EDGE
                    Rlgl.rlTextureParameters(depth.id, 0x2800, 0x2600);
                    Rlgl.rlTextureParameters(depth.id, 0x2801, 0x2600);
                    Rlgl.rlTextureParameters(depth.id, 0x2802, 0x812F);
                    Rlgl.rlTextureParameters(depth.id, 0x2803, 0x812F);

                    Rlgl.rlFramebufferAttach(fboId, InternalDepth.id, FramebufferAttachType.RL_ATTACHMENT_DEPTH, FramebufferAttachTextureType.RL_ATTACHMENT_TEXTURE2D, 0);
                }

                if (!Rlgl.rlFramebufferComplete(fboId))
                    throw new Exception("RenderTexture: [ID {fboId}] RenderTexture object creation failed.");

                // Unbind FBO
                Rlgl.rlDisableFramebuffer();
            }
        }

        public void Begin()
        {
            if(numTextures != 0)
            {
                Raylib.BeginTextureMode(new RenderTexture2D() { id = fboId, texture = InternalTextures[0], depth = InternalDepth });
            }
            else if(hasDepthAttachment)
            {
                Raylib.BeginTextureMode(new RenderTexture2D() { id = fboId, texture = InternalDepth, depth = InternalDepth });
            }

            Rlgl.rlActiveDrawBuffers(numTextures);
        }

        public void End()
        {
            Raylib.EndTextureMode();
        }

        public override void OnDispose()
        {
            if (fboId <= 0) return;
            foreach (var texture in InternalTextures)
                Rlgl.rlUnloadTexture(texture.id);

            // Depth should be automatically unloaded
            Rlgl.rlUnloadFramebuffer(fboId);
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
            textureFormats = new PixelFormat[numTextures];
            var textureFormatsTag = value.Get<ListTag>("TextureFormats");
            for (int i = 0; i < numTextures; i++)
                textureFormats[i] = (PixelFormat)textureFormatsTag[i].ByteValue;

            var param = new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(PixelFormat[]) };
            var values = new object[] { Width, Height, numTextures, hasDepthAttachment, textureFormats };
            typeof(RenderTexture).GetConstructor(param).Invoke(this, values);
        }
    }
}
