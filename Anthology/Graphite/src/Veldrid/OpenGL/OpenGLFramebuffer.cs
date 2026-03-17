using static Veldrid.OpenGL.OpenGLUtil;
using Silk.NET.OpenGL;
using GLFramebufferAttachment = Silk.NET.OpenGL.FramebufferAttachment;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLFramebuffer : Framebuffer, OpenGLDeferredResource
    {
        private readonly OpenGLGraphicsDevice _gd;
        private GL _gl => _gd.GL;
        private uint _framebuffer;

        private string _name;
        private bool _nameChanged;
        private bool _disposeRequested;
        private bool _disposed;

        public override string Name { get => _name; set { _name = value; _nameChanged = true; } }

        public uint Framebuffer => _framebuffer;

        public bool Created { get; private set; }

        public override bool IsDisposed => _disposeRequested;

        public OpenGLFramebuffer(OpenGLGraphicsDevice gd, ref FramebufferDescription description)
            : base(description.DepthTarget, description.ColorTargets)
        {
            _gd = gd;
        }

        public void EnsureResourcesCreated()
        {
            if (!Created)
            {
                CreateGLResources();
            }
            if (_nameChanged)
            {
                _nameChanged = false;
                if (_gd.Extensions.KHR_Debug)
                {
                    SetObjectLabel(ObjectIdentifier.Framebuffer, _framebuffer, _name);
                }
            }
        }

        public void CreateGLResources()
        {
            _framebuffer = _gl.GenFramebuffer();
            CheckLastError();

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            CheckLastError();

            uint colorCount = (uint)ColorTargets.Count;

            if (colorCount > 0)
            {
                for (int i = 0; i < colorCount; i++)
                {
                    FramebufferAttachment colorAttachment = ColorTargets[i];
                    OpenGLTexture glTex = Util.AssertSubtype<Texture, OpenGLTexture>(colorAttachment.Target);
                    glTex.EnsureResourcesCreated();

                    _gd.TextureSamplerManager.SetTextureTransient(glTex.TextureTarget, glTex.Texture);
                    CheckLastError();

                    TextureTarget textureTarget = GetTextureTarget (glTex, colorAttachment.ArrayLayer);

                    if (glTex.ArrayLayers == 1)
                    {
                        _gl.FramebufferTexture2D(
                            FramebufferTarget.Framebuffer,
                            GLFramebufferAttachment.ColorAttachment0 + i,
                            textureTarget,
                            glTex.Texture,
                            (int)colorAttachment.MipLevel);
                        CheckLastError();
                    }
                    else
                    {
                        _gl.FramebufferTextureLayer(
                            FramebufferTarget.Framebuffer,
                            GLFramebufferAttachment.ColorAttachment0 + i,
                            (uint)glTex.Texture,
                            (int)colorAttachment.MipLevel,
                            (int)colorAttachment.ArrayLayer);
                        CheckLastError();
                    }
                }

                DrawBufferMode* bufs = stackalloc DrawBufferMode[(int)colorCount];
                for (int i = 0; i < colorCount; i++)
                {
                    bufs[i] = DrawBufferMode.ColorAttachment0 + i;
                }
                _gl.DrawBuffers(colorCount, bufs);
                CheckLastError();
            }

            uint depthTextureID = 0;
            TextureTarget depthTarget = TextureTarget.Texture2D;
            if (DepthTarget != null)
            {
                OpenGLTexture glDepthTex = Util.AssertSubtype<Texture, OpenGLTexture>(DepthTarget.Value.Target);
                glDepthTex.EnsureResourcesCreated();
                depthTarget = glDepthTex.TextureTarget;

                depthTextureID = glDepthTex.Texture;

                _gd.TextureSamplerManager.SetTextureTransient(depthTarget, glDepthTex.Texture);
                CheckLastError();

                depthTarget = GetTextureTarget (glDepthTex, DepthTarget.Value.ArrayLayer);

                GLFramebufferAttachment framebufferAttachment = GLFramebufferAttachment.DepthAttachment;
                if (FormatHelpers.IsStencilFormat(glDepthTex.Format))
                {
                    framebufferAttachment = GLFramebufferAttachment.DepthStencilAttachment;
                }

                if (glDepthTex.ArrayLayers == 1)
                {
                    _gl.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        framebufferAttachment,
                        depthTarget,
                        depthTextureID,
                        (int)DepthTarget.Value.MipLevel);
                    CheckLastError();
                }
                else
                {
                    _gl.FramebufferTextureLayer(
                        FramebufferTarget.Framebuffer,
                        framebufferAttachment,
                        glDepthTex.Texture,
                        (int)DepthTarget.Value.MipLevel,
                        (int)DepthTarget.Value.ArrayLayer);
                    CheckLastError();
                }

            }

            FramebufferStatus errorCode = (FramebufferStatus)_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            CheckLastError();
            if (errorCode != FramebufferStatus.Complete)
            {
                throw new VeldridException("Framebuffer was not successfully created: " + errorCode);
            }

            Created = true;
        }

        public override void Dispose()
        {
            if (!_disposeRequested)
            {
                _disposeRequested = true;
                _gd.EnqueueDisposal(this);
            }
        }

        public void DestroyGLResources()
        {
            if (!_disposed)
            {
                _disposed = true;
                _gl.DeleteFramebuffer(_framebuffer);
                CheckLastError();
            }
        }
    }
}
