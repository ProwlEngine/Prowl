using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime
{

    public static class Graphics
    {
        public static GraphicsDevice Device { get; internal set; }

        public static int GLMajorVersion { get; internal set; }
        public static int GLMinorVersion { get; internal set; }

        public static BlendingFactor CustomBlendSrcFactor { get; set; }
        public static BlendingFactor CustomBlendDstFactor { get; set; }
        public static BlendEquationModeEXT CustomBlendEquation { get; set; }


        public static Vector2 Resolution;
        public static Matrix4x4 MatView;
        public static Matrix4x4 MatViewInverse;
        public static Matrix4x4 MatProjection;
        public static Matrix4x4 MatProjectionInverse;
        public static Matrix4x4 OldMatView;
        public static Matrix4x4 OldMatProjection;

        public static Matrix4x4 MatDepthProjection;
        public static Matrix4x4 MatDepthView;

        public static Vector2 Jitter { get; set; }
        public static Vector2 PreviousJitter { get; set; }
        public static bool UseJitter;

        private static Material defaultMat;
        private static AssetRef<Texture2D> defaultNoise;
        internal static Vector2D<int> FrameBufferSize;
        private static uint activeProgram;

        static readonly GLEnum[] buffers =
        {
            GLEnum.ColorAttachment0,  GLEnum.ColorAttachment1,  GLEnum.ColorAttachment2,
            GLEnum.ColorAttachment3,  GLEnum.ColorAttachment4,  GLEnum.ColorAttachment5,
            GLEnum.ColorAttachment6,  GLEnum.ColorAttachment7,  GLEnum.ColorAttachment8,
            GLEnum.ColorAttachment9,  GLEnum.ColorAttachment10, GLEnum.ColorAttachment11,
            GLEnum.ColorAttachment12, GLEnum.ColorAttachment13, GLEnum.ColorAttachment14,
            GLEnum.ColorAttachment15, GLEnum.ColorAttachment16, GLEnum.ColorAttachment16,
            GLEnum.ColorAttachment17, GLEnum.ColorAttachment18, GLEnum.ColorAttachment19,
            GLEnum.ColorAttachment20, GLEnum.ColorAttachment21, GLEnum.ColorAttachment22,
            GLEnum.ColorAttachment23, GLEnum.ColorAttachment24, GLEnum.ColorAttachment25,
            GLEnum.ColorAttachment26, GLEnum.ColorAttachment27, GLEnum.ColorAttachment28,
            GLEnum.ColorAttachment29, GLEnum.ColorAttachment30, GLEnum.ColorAttachment31
        };

        public static int MaxTextureSize { get; internal set; }
        public static int MaxCubeMapTextureSize { get; internal set; }
        public static int MaxArrayTextureLayers { get; internal set; }

        public static int MaxRenderbufferSize { get; internal set; }
        public static int MaxFramebufferColorAttachments { get; internal set; }
        public static int MaxDrawBuffers { get; internal set; }
        public static int MaxSamples { get; internal set; }

        public static void Initialize()
        {
            Device = new GLDevice();
            Device.Initialize(true);
        }

        public static IDisposable UseBlendMode(BlendMode mode) => new ActiveBlendMode(mode);
        public static IDisposable UseFaceCull(TriangleFace face) => new ActiveFaceCull(face);
        //public static IDisposable UseMaterial(Material material, int pass = 0) => new ActiveMaterial(material, pass);
        //public static IDisposable UseRenderTexture(RenderTexture target) => new ActiveRenderTexture(target);

        public static IDisposable UseDepthTest(bool doTest) => new ActiveDepthTest(doTest);
        public static IDisposable UseColorBlend(bool doBlend) => new ActiveColorBlend(doBlend);
        public static IDisposable UseCulling(bool doCulling) => new ActiveCullFace(doCulling);

        public static void UseProgram(uint program)
        {
            if (activeProgram != program)
            {
                Device.UseProgram(program);
                activeProgram = program;
            }
        }

        public static void Viewport(int width, int height)
        {
            Device.Viewport(0, 0, (uint)width, (uint)height);
            Resolution = new Vector2(width, height);
        }

        public static void ActivateDrawBuffers(int count)
        {
            Device.DrawBuffers((uint)count, buffers);
        }

        public static void Clear(float r = 0, float g = 0, float b = 0, float a = 1, bool color = true, bool depth = true, bool stencil = true)
        {
            ClearFlags flags = 0;
            if (color) flags |= ClearFlags.Color;
            if (depth) flags |= ClearFlags.Depth;
            if (stencil) flags |= ClearFlags.Stencil;
            Device.Clear(r, g, b, a, flags);
        }

        public static void StartFrame()
        {
            Device.DepthFunc(DepthFunction.Lequal);

            Device.Enable(EnableCap.DepthTest);
            Device.Enable(EnableCap.Blend);
            Device.Enable(EnableCap.CullFace);

            ActiveDepthTest.Stack.Clear();
            ActiveDepthTest.SetDefault();

            ActiveColorBlend.Stack.Clear();
            ActiveColorBlend.SetDefault();

            ActiveCullFace.Stack.Clear();
            ActiveCullFace.SetDefault();


            ActiveBlendMode.Stack.Clear();
            ActiveBlendMode.SetDefault();

            ActiveFaceCull.Stack.Clear();
            ActiveFaceCull.SetDefault();

            Device.FrontFace(FrontFaceDirection.CW); // Front face are defined clockwise (default)

            Clear();
            Viewport(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
        }

        public static void EndFrame()
        {

        }

        public static void DrawMeshNow(Mesh mesh, Matrix4x4 transform, Material material, Matrix4x4? oldTransform = null)
        {
            if (Camera.Current == null) throw new Exception("DrawMeshNow must be called during a rendering context like OnRenderObject()!");
            if (Material.current == null) throw new Exception("Use Material.SetPass first before called DrawMeshNow!");

            oldTransform ??= transform;

            if (defaultNoise.IsAvailable == false)
            {
                defaultNoise = Application.AssetProvider.LoadAsset<Texture2D>("Defaults/noise.png");
            }

            material.SetTexture("DefaultNoise", defaultNoise);

            if (UseJitter)
            {
                material.SetVector("Jitter", Jitter);
                material.SetVector("PreviousJitter", PreviousJitter);
            }
            else
            {
                material.SetVector("Jitter", Vector2.zero);
                material.SetVector("PreviousJitter", Vector2.zero);
            }

            material.SetVector("Resolution", Graphics.Resolution);
            //material.SetVector("ScreenResolution", new Vector2(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y));
            material.SetFloat("Time", (float)Time.time);
            material.SetInt("Frame", (int)Time.frameCount);
            //material.SetFloat("DeltaTime", Time.deltaTimeF);
            //material.SetInt("RandomSeed", Random.Shared.Next());
            //material.SetInt("ObjectID", mesh.InstanceID);
            material.SetVector("Camera_WorldPosition", Camera.Current.GameObject.transform.position);
            //material.SetVector("Camera_NearFarFOV", new Vector3(Camera.Current.NearClip, Camera.Current.FarClip, Camera.Current.FieldOfView));

            // Upload view and projection matrices(if locations available)
            material.SetMatrix("matView", MatView);
            material.SetMatrix("matOldView", OldMatView);

            material.SetMatrix("matProjection", MatProjection);
            material.SetMatrix("matProjectionInverse", MatProjectionInverse);
            material.SetMatrix("matOldProjection", OldMatProjection);
            // Model transformation matrix is sent to shader
            material.SetMatrix("matModel", transform);

            material.SetMatrix("matViewInverse", MatViewInverse);

            Matrix4x4 matMVP = Matrix4x4.Identity;
            matMVP = Matrix4x4.Multiply(matMVP, transform);
            matMVP = Matrix4x4.Multiply(matMVP, MatView);
            matMVP = Matrix4x4.Multiply(matMVP, MatProjection);

            Matrix4x4 oldMatMVP = Matrix4x4.Identity;
            oldMatMVP = Matrix4x4.Multiply(oldMatMVP, oldTransform.Value);
            oldMatMVP = Matrix4x4.Multiply(oldMatMVP, OldMatView);
            oldMatMVP = Matrix4x4.Multiply(oldMatMVP, OldMatProjection);

            // Send combined model-view-projection matrix to shader
            //material.SetMatrix("mvp", matModelViewProjection);
            material.SetMatrix("mvp", matMVP);
            Matrix4x4.Invert(matMVP, out var mvpInverse);
            material.SetMatrix("mvpInverse", mvpInverse);
            material.SetMatrix("mvpOld", oldMatMVP);

            // Mesh data can vary between meshes, so we need to let the shaders know which attributes are in use
            material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
            material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
            material.SetKeyword("HAS_UV", mesh.HasUV);
            material.SetKeyword("HAS_UV2", mesh.HasUV2);
            material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);

            material.SetKeyword("HAS_BONEINDICES", mesh.HasBoneIndices);
            material.SetKeyword("HAS_BONEWEIGHTS", mesh.HasBoneWeights);

            // All material uniforms have been assigned, its time to properly set them
            MaterialPropertyBlock.Apply(material.PropertyBlock, Material.current.Value);

            DrawMeshNowDirect(mesh);
        }

        public static void DrawMeshNowDirect(Mesh mesh)
        {
            if (Camera.Current == null) throw new Exception("DrawMeshNow must be called during a rendering context like OnRenderObject()!");
            if (Material.current == null) throw new Exception("Use Material.SetPass first before called DrawMeshNow!");

            mesh.Upload();

            unsafe
            {
                Device.BindVertexArray(mesh.VertexArrayObject);
                Device.DrawElements(PrimitiveType.Triangles, (uint)mesh.IndexCount, mesh.IndexFormat == IndexFormat.UInt16 ? DrawElementsType.UnsignedShort : DrawElementsType.UnsignedInt, null);
                Device.BindVertexArray(0);
            }
        }

        /// <summary>
        /// Draws material with a FullScreen Quad
        /// </summary>
        public static void Blit(Material mat, int pass = 0)
        {
            using (UseDepthTest(false))
            {
                using (UseCulling(false))
                {
                    mat.SetPass(pass);
                    DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, mat);
                }
            }
        }

        /// <summary>
        /// Draws material with a FullScreen Quad onto a RenderTexture
        /// </summary>
        public static void Blit(RenderTexture? renderTexture, Material mat, int pass = 0, bool clear = true)
        {
            Graphics.Device.DepthMask(false);
            using (UseDepthTest(false))
            {
                using (UseCulling(false))
                {
                    renderTexture?.Begin();
                    if (clear)
                        Clear(0, 0, 0, 0);
                    mat.SetPass(pass);
                    DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, mat);
                    renderTexture?.End();
                }
            }
            Graphics.Device.DepthMask(true);
        }

        /// <summary>
        /// Draws texture into a RenderTexture Additively
        /// </summary>
        public static void Blit(RenderTexture? renderTexture, Texture2D texture, bool clear = true)
        {
            defaultMat ??= new Material(Shader.Find("Defaults/Basic.shader"));
            defaultMat.SetTexture("texture0", texture);
            defaultMat.SetPass(0);

            Graphics.Device.DepthMask(false);
            using (UseDepthTest(false))
            {
                using (UseCulling(false))
                {
                    renderTexture?.Begin();
                    if (clear) Clear(0, 0, 0, 0);
                    DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, defaultMat);
                    renderTexture?.End();
                }
            }
            Graphics.Device.DepthMask(true);
        }

        internal static void Dispose()
        {
            Device.Dispose();
        }

        internal static void BlitDepth(RenderTexture source, RenderTexture? destination)
        {
            Device.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source.fboId);
            Device.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination?.fboId ?? 0);
            Graphics.Device.BlitFramebuffer(0, 0, source.Width, source.Height,
                                        0, 0, destination?.Width ?? (int)Graphics.Resolution.x, destination?.Height ?? (int)Graphics.Resolution.y,
                                        ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest
                                        );
            Device.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            Device.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        }
    }
}
