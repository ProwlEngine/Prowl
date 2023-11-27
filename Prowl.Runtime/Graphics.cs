using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime
{
    public static class Graphics
    {
        static Stack<IntPtr> pointers = new ();
        internal static unsafe sbyte* ToPtr(string text)
        {
            pointers.Push(Marshal.StringToCoTaskMemUTF8(text));
            return (sbyte*)pointers.Peek();
        }

        static unsafe void DisposeText()
        {
            while (pointers.TryPop(out var ptr))
                Marshal.ZeroFreeCoTaskMemUTF8(ptr);
        }

        public static void BindTexture(Raylib_cs.Texture2D texture, int i)
        {
            Rlgl.rlActiveTextureSlot(i);
            Rlgl.rlEnableTexture(texture.id);
        }

        public static Matrix4x4 MatView;
        public static Matrix4x4 MatViewTransposed;
        public static Matrix4x4 MatViewInverse;
        public static Matrix4x4 MatViewInverseTransposed;
        public static Matrix4x4 MatProjection;
        public static Matrix4x4 MatProjectionTransposed;
        public static Matrix4x4 OldMatView;
        public static Matrix4x4 OldMatViewTransposed;
        public static Matrix4x4 OldMatProjection;
        public static Matrix4x4 OldMatProjectionTransposed;

        public static Matrix4x4 MatDepthProjection;
        public static Matrix4x4 MatDepthView;

        private static Material depthMat;
        public static event Action UpdateShadowmaps;

        public static Material DepthMat
        {
            get
            {
                if (depthMat == null)
                    depthMat = new Material(Resources.Shader.Find("Defaults/Depth.shader"));
                return depthMat;
            }
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

            material.SetVector("Resolution", new Vector2(Rlgl.rlGetFramebufferWidth(), Rlgl.rlGetFramebufferHeight()));
            //material.SetFloat("Time", (float)Time.time);
            //material.SetFloat("DeltaTime", Time.deltaTimeF);
            //material.SetInt("RandomSeed", Random.Shared.Next());
            //material.SetInt("ObjectID", mesh.InstanceID);
            material.SetVector("Camera_WorldPosition", Camera.Current.GameObject.Position);
            //material.SetVector("Camera_NearFarFOV", new Vector3(Camera.Current.NearClip, Camera.Current.FarClip, Camera.Current.FieldOfView));

            // Upload view and projection matrices(if locations available)
            material.SetMatrix("matView", MatViewTransposed);
            material.SetMatrix("matProjection", MatProjectionTransposed);
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

        private static Dictionary<string, int> attribCache = new();

        private unsafe static int GetAttribLocation(string attributeName)
        {
            var key = Material.current.Value.id + attributeName;
            if (!attribCache.TryGetValue(key, out int location))
            {
                location = Rlgl.rlGetLocationAttrib(Material.current.Value.id, ToPtr(attributeName));
                attribCache[key] = location;
            }
            return location;
        }

        public static void DrawMeshNowDirect(Mesh mesh)
        {
            if (Camera.Current == null) throw new Exception("DrawMeshNow must be called during a rendering context like OnRenderObject()!");
            if (Material.current == null) throw new Exception("Use Material.SetPass first before called DrawMeshNow!");

            if (mesh.vaoId <= 0)
                mesh.Upload(false);

            unsafe
            {
                int RL_FLOAT = 0x1406;
                int RL_UNSIGNED_BYTE = 0x1401;

                int vertexLoc = GetAttribLocation("vertexPosition");
                int texLoc = GetAttribLocation("vertexTexCoord");
                int normalLoc = GetAttribLocation("vertexNormal");
                int colorLoc = GetAttribLocation("vertexColor");
                int tangentLoc = GetAttribLocation("vertexTangent");
                int tex2Loc = GetAttribLocation("vertexTexCoord2");
                DisposeText();
                if (!Rlgl.rlEnableVertexArray(mesh.vaoId))
                {

                    // Bind mesh VBO data: vertex position (shader-location = 0)
                    Rlgl.rlEnableVertexBuffer(mesh.vboId[0]);
                    Rlgl.rlSetVertexAttribute((uint)vertexLoc, 3, RL_FLOAT, 0, 0, null);
                    Rlgl.rlEnableVertexAttribute((uint)vertexLoc);

                    // Bind mesh VBO data: vertex texcoords (shader-location = 1)
                    Rlgl.rlEnableVertexBuffer(mesh.vboId[1]);
                    Rlgl.rlSetVertexAttribute((uint)texLoc, 2, RL_FLOAT, 0, 0, null);
                    Rlgl.rlEnableVertexAttribute((uint)texLoc);

                    if (normalLoc != -1)
                    {
                        // Bind mesh VBO data: vertex normals (shader-location = 2)
                        Rlgl.rlEnableVertexBuffer(mesh.vboId[2]);
                        Rlgl.rlSetVertexAttribute((uint)normalLoc, 3, RL_FLOAT, 0, 0, null);
                        Rlgl.rlEnableVertexAttribute((uint)normalLoc);
                    }

                    // Bind mesh VBO data: vertex colors (shader-location = 3, if available)
                    if (colorLoc != -1)
                    {
                        if (mesh.vboId[3] != 0)
                        {
                            Rlgl.rlEnableVertexBuffer(mesh.vboId[3]);
                            Rlgl.rlSetVertexAttribute((uint)colorLoc, 4, RL_UNSIGNED_BYTE, 1, 0, null);
                            Rlgl.rlEnableVertexAttribute((uint)colorLoc);
                        }
                        else
                        {
                            // Set default value for defined vertex attribute in shader but not provided by mesh
                            // WARNING: It could result in GPU undefined behaviour
                            float[] value = { 1.0f, 1.0f, 1.0f, 1.0f };
                            fixed (float* ptr = value)
                                Rlgl.rlSetVertexAttributeDefault(colorLoc, ptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC4, 4);
                            Rlgl.rlDisableVertexAttribute((uint)colorLoc);
                        }
                    }

                    // Bind mesh VBO data: vertex tangents (shader-location = 4, if available)
                    if (tangentLoc != -1)
                    {
                        Rlgl.rlEnableVertexBuffer(mesh.vboId[4]);
                        Rlgl.rlSetVertexAttribute((uint)tangentLoc, 4, RL_FLOAT, 0, 0, null);
                        Rlgl.rlEnableVertexAttribute((uint)tangentLoc);
                    }

                    // Bind mesh VBO data: vertex texcoords2 (shader-location = 5, if available)
                    if (tex2Loc != -1)
                    {
                        Rlgl.rlEnableVertexBuffer(mesh.vboId[5]);
                        Rlgl.rlSetVertexAttribute((uint)tex2Loc, 2, RL_FLOAT, 0, 0, null);
                        Rlgl.rlEnableVertexAttribute((uint)tex2Loc);
                    }

                    if (mesh.triangles != null) Rlgl.rlEnableVertexBufferElement(mesh.vboId[6]);
                }

                // WARNING: Disable vertex attribute color input if mesh can not provide that data (despite location being enabled in shader)
                if (mesh.vboId[3] == 0) Rlgl.rlDisableVertexAttribute((uint)colorLoc);

                // Draw mesh
                if (mesh.triangles != null) Rlgl.rlDrawVertexArrayElements(0, mesh.triangleCount * 3, null);
                else Rlgl.rlDrawVertexArray(0, mesh.vertexCount);

                // Disable all possible vertex array objects (or VBOs)
                Rlgl.rlDisableVertexArray();
                Rlgl.rlDisableVertexBuffer();
                Rlgl.rlDisableVertexBufferElement();
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
            renderTexture.Begin();
            if (clear)
                Raylib.ClearBackground(new Color(0, 0, 0, 0));
            mat.SetPass(pass, true);
            DrawMeshNow(Mesh.GetFullscreenQuad(), Matrix4x4.Identity, mat);
            mat.EndPass();
            renderTexture.End();
        }

    }
}
