using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime
{
    public static class Graphics
    {

        public static int GLMajorVersion { get; private set; }

        public static int GLMinorVersion { get; private set; }

        public static GL GL { get; internal set; }

        public static bool DepthTest {
            get {
                return GL.IsEnabled(GLEnum.DepthTest);
            }
            set {
                if (value) GL.Enable(GLEnum.DepthTest);
                else GL.Disable(GLEnum.DepthTest);
            }
        }

        public static bool CullFace {
            get {
                return GL.IsEnabled(GLEnum.CullFace);
            }
            set {
                if (value) GL.Enable(GLEnum.CullFace);
                else GL.Disable(GLEnum.CullFace);
            }
        }

        public static bool Blend {
            get {
                return GL.IsEnabled(GLEnum.Blend);
            }
            set {
                if (value) GL.Enable(GLEnum.Blend);
                else GL.Disable(GLEnum.Blend);
            }
        }

        public static event Action UpdateShadowmaps;

        public static Vector2 Resolution;
        public static Matrix4x4 MatView;
        public static Matrix4x4 MatViewTransposed;
        public static Matrix4x4 MatViewInverse;
        public static Matrix4x4 MatViewInverseTransposed;
        public static Matrix4x4 MatProjection;
        public static Matrix4x4 MatProjectionTransposed;
        public static Matrix4x4 MatProjectionInverse;
        public static Matrix4x4 MatProjectionInverseTransposed;
        public static Matrix4x4 OldMatView;
        public static Matrix4x4 MatOldViewTransposed;
        public static Matrix4x4 OldMatProjection;
        public static Matrix4x4 MatOldProjectionTransposed;

        public static Matrix4x4 MatDepthProjection;
        public static Matrix4x4 MatDepthView;

        public static Vector2 Jitter { get; private set; }
        public static Vector2 PreviousJitter { get; private set; }
        public static bool UseJitter;

        private static Material depthMat;
        private static AssetRef<Texture2D> defaultNoise;
        internal static Vector2D<int> FrameBufferSize;
        public readonly static Vector2[] Halton16 =
        [
            new Vector2(0.5f, 0.333333f),
            new Vector2(0.25f, 0.666667f),
            new Vector2(0.75f, 0.111111f),
            new Vector2(0.125f, 0.444444f),
            new Vector2(0.625f, 0.777778f),
            new Vector2(0.375f, 0.222222f),
            new Vector2(0.875f, 0.555556f),
            new Vector2(0.0625f, 0.888889f),
            new Vector2(0.5625f, 0.037037f),
            new Vector2(0.3125f, 0.370370f),
            new Vector2(0.8125f, 0.703704f),
            new Vector2(0.1875f, 0.148148f),
            new Vector2(0.6875f, 0.481481f),
            new Vector2(0.4375f, 0.814815f),
            new Vector2(0.9375f, 0.259259f),
            new Vector2(0.03125f, 0.592593f),
        ];

        static readonly DrawBufferMode[] buffers =
        {
            DrawBufferMode.ColorAttachment0,  DrawBufferMode.ColorAttachment1,  DrawBufferMode.ColorAttachment2,
            DrawBufferMode.ColorAttachment3,  DrawBufferMode.ColorAttachment4,  DrawBufferMode.ColorAttachment5,
            DrawBufferMode.ColorAttachment6,  DrawBufferMode.ColorAttachment7,  DrawBufferMode.ColorAttachment8,
            DrawBufferMode.ColorAttachment9,  DrawBufferMode.ColorAttachment10, DrawBufferMode.ColorAttachment11,
            DrawBufferMode.ColorAttachment12, DrawBufferMode.ColorAttachment13, DrawBufferMode.ColorAttachment14,
            DrawBufferMode.ColorAttachment15, DrawBufferMode.ColorAttachment16, DrawBufferMode.ColorAttachment16,
            DrawBufferMode.ColorAttachment17, DrawBufferMode.ColorAttachment18, DrawBufferMode.ColorAttachment19,
            DrawBufferMode.ColorAttachment20, DrawBufferMode.ColorAttachment21, DrawBufferMode.ColorAttachment22,
            DrawBufferMode.ColorAttachment23, DrawBufferMode.ColorAttachment24, DrawBufferMode.ColorAttachment25,
            DrawBufferMode.ColorAttachment26, DrawBufferMode.ColorAttachment27, DrawBufferMode.ColorAttachment28,
            DrawBufferMode.ColorAttachment29, DrawBufferMode.ColorAttachment30, DrawBufferMode.ColorAttachment31
        };

        public static int MaxRenderbufferSize { get; private set; }
        public static int MaxFramebufferColorAttachments { get; private set; }
        public static int MaxDrawBuffers { get; private set; }
        public static int MaxSamples { get; private set; }

        public static void Initialize()
        {
            GL = GL.GetApi(Window.InternalWindow);

            unsafe {
                GL.DebugMessageCallback(DebugCallback, null);
            }
            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);

            GLMajorVersion = GL.GetInteger(GLEnum.MajorVersion);
            GLMinorVersion = GL.GetInteger(GLEnum.MinorVersion);

            if (GLMajorVersion < 3)
                throw new PlatformNotSupportedException("Burex only supports platforms with OpenGL 3.0 and up.");

            CheckGL();

            // Textures
            MaxSamples = GL.GetInteger(GLEnum.MaxSamples);
            MaxTextureSize = GL.GetInteger(GLEnum.MaxTextureSize);
            MaxTextureBufferSize = GL.GetInteger(GLEnum.MaxTextureBufferSize);
            Max3DTextureSize = GL.GetInteger(GLEnum.Max3DTextureSize);
            MaxCubeMapTextureSize = GL.GetInteger(GLEnum.MaxCubeMapTextureSize);
            MaxArrayTextureLayers = GL.GetInteger(GLEnum.MaxArrayTextureLayers);

            MaximumTextureUnits = GL.GetInteger(GetPName.MaxTextureImageUnits);
            currentlyBoundSlots = new uint[MaximumTextureUnits];
            for (int i = 0; i < MaximumTextureUnits; i++)
                currentlyBoundSlots[i] = 0;

            MaxRenderbufferSize = GL.GetInteger(GLEnum.MaxRenderbufferSize);
            MaxFramebufferColorAttachments = GL.GetInteger(GLEnum.MaxColorAttachments);
            MaxDrawBuffers = GL.GetInteger(GLEnum.MaxDrawBuffers);

            CheckGL();
        }

        private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
        {
            var msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
            Console.WriteLine($"OpenGL Debug Message: {msg}");
        }

        public static void CheckGL()
        {
            var errorCode = GL.GetError();
            while (errorCode != GLEnum.NoError) {
                Console.WriteLine($"OpenGL Error: {errorCode}" + Environment.NewLine + $"StackTrace: " + Environment.StackTrace);
                errorCode = GL.GetError();
            }
        }

        public static void ActivateDrawBuffers(int count)
        {
            GL.DrawBuffers((uint)count, buffers); CheckGL();
        }

        public static void Clear(float r = 0, float g = 0, float b = 0, float a = 1, bool color = true, bool depth = true, bool stencil = true)
        {
            GL.ClearColor(r, g, b, a);
            GL.Clear((uint)(color ? ClearBufferMask.ColorBufferBit : 0) | (uint)(depth ? ClearBufferMask.DepthBufferBit : 0) | (uint)(stencil ? ClearBufferMask.StencilBufferBit : 0));
            CheckGL();
        }

        public static void StartFrame()
        {
            // Halton Jitter
            long n = Time.frameCount % 16;
            var halton = Halton16[n];
            PreviousJitter = Jitter;
            Jitter = new Vector2((halton.X - 0.5f), (halton.Y - 0.5f)) * 2.0;

            Clear();
        }

        public static void EndFrame()
        {

        }

        public static void UpdateAllShadowmaps()
        {
            UpdateShadowmaps?.Invoke();
        }

        public static void DrawMeshNow(Mesh mesh, Matrix4x4 transform, Material material, Matrix4x4? oldTransform = null)
        {
            if (Camera.Current == null) throw new Exception("DrawMeshNow must be called during a rendering context like OnRenderObject()!");
            if (Material.current == null) throw new Exception("Use Material.SetPass first before called DrawMeshNow!");

            oldTransform ??= transform;

            if(defaultNoise.IsAvailable == false) {
                defaultNoise = Application.AssetProvider.LoadAsset<Texture2D>("Defaults/noise.png");
            }

            material.SetTexture("DefaultNoise", defaultNoise);

            if (UseJitter) {
                material.SetVector("Jitter", Jitter / Resolution);
                material.SetVector("PreviousJitter", PreviousJitter / Resolution);
            } else {
                material.SetVector("Jitter", Vector2.Zero);
                material.SetVector("PreviousJitter", Vector2.Zero);
            }

            material.SetVector("Resolution", Graphics.Resolution);
            material.SetFloat("Time", (float)Time.time);
            material.SetInt("Frame", (int)Time.frameCount);
            //material.SetFloat("DeltaTime", Time.deltaTimeF);
            //material.SetInt("RandomSeed", Random.Shared.Next());
            //material.SetInt("ObjectID", mesh.InstanceID);
            material.SetVector("Camera_WorldPosition", Camera.Current.GameObject.GlobalPosition);
            //material.SetVector("Camera_NearFarFOV", new Vector3(Camera.Current.NearClip, Camera.Current.FarClip, Camera.Current.FieldOfView));

            // Upload view and projection matrices(if locations available)
            material.SetMatrix("matView", MatViewTransposed);
            material.SetMatrix("matOldView", MatOldViewTransposed);

            material.SetMatrix("matProjection", MatProjectionTransposed);
            material.SetMatrix("matProjectionInverse", MatProjectionInverseTransposed);
            material.SetMatrix("matOldProjection", MatOldProjectionTransposed);
            // Model transformation matrix is sent to shader
            material.SetMatrix("matModel", Matrix4x4.Transpose(transform));

            material.SetMatrix("matViewInverse", MatViewInverseTransposed);

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
            material.SetMatrix("mvp", Matrix4x4.Transpose(matMVP));
            Matrix4x4.Invert(matMVP, out var mvpInverse);
            material.SetMatrix("mvpInverse", Matrix4x4.Transpose(mvpInverse));
            material.SetMatrix("mvpOld", Matrix4x4.Transpose(oldMatMVP));


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
                Rlgl.rlEnableVertexArray(mesh.vao);
                //Rlgl.rlEnableVertexBuffer(mesh.vbo);
                //Rlgl.rlEnableVertexBufferElement(mesh.ibo);

                Rlgl.rlDrawVertexArrayElements(0, mesh.indices.Length, null);

                Rlgl.rlDisableVertexArray();
                //Rlgl.rlDisableVertexBuffer();
                //Rlgl.rlDisableVertexBufferElement();
            }
        }

        /// <summary>
        /// Draws material with a FullScreen Quad
        /// </summary>
        public static void Blit(Material mat, int pass = 0)
        {
            mat.SetPass(pass);
            DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, mat);
            mat.EndPass();
        }

        /// <summary>
        /// Draws material with a FullScreen Quad onto a RenderTexture
        /// </summary>
        public static void Blit(RenderTexture renderTexture, Material mat, int pass = 0, bool clear = true)
        {
            Rlgl.rlDisableDepthMask();
            Rlgl.rlDisableDepthTest();
            Rlgl.rlDisableBackfaceCulling();
            renderTexture.Begin();
            if (clear)
                Raylib.ClearBackground(new Color(0, 0, 0, 0));
            mat.SetPass(pass);
            DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, mat);
            mat.EndPass();
            renderTexture.End();
            Rlgl.rlEnableDepthMask();
            Rlgl.rlEnableDepthTest();
            Rlgl.rlEnableBackfaceCulling();
        }

        public static void Blit(RenderTexture renderTexture, Texture2D texture, bool clear = true) => Blit(renderTexture, texture.InternalTexture, clear);

        /// <summary>
        /// Draws texture into a RenderTexture Additively
        /// </summary>
        public static void Blit(RenderTexture renderTexture, Raylib_cs.Texture2D texture, bool clear = true)
        {
            Rlgl.rlDisableDepthMask();
            Rlgl.rlDisableDepthTest();
            Rlgl.rlDisableBackfaceCulling();
            renderTexture.Begin();
            if (clear)
                Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Raylib.BeginBlendMode(BlendMode.BLEND_ADDITIVE);
            Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.width, -texture.height), new Rectangle(0, 0, renderTexture.Width, renderTexture.Height), new Vector2(0, 0), 0, Color.white);
            Raylib.EndBlendMode();
            renderTexture.End();
            Rlgl.rlEnableDepthMask();
            Rlgl.rlEnableDepthTest();
            Rlgl.rlEnableBackfaceCulling();
        }


        internal static void Dispose()
        {
            GL.Dispose();
        }
    }
}
