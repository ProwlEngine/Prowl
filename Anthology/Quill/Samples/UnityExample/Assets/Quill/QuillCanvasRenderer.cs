using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using QuillCanvas = Prowl.Quill.Canvas;
using QuillColor32 = Prowl.Vector.Color32;

namespace Quill.Unity
{
    /// <summary>
    /// Local vertex struct that mirrors Quill's Vertex layout for Unity compatibility.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    internal struct UnityVertex
    {
        public float x;
        public float y;
        public float u;
        public float v;
        public byte r;
        public byte g;
        public byte b;
        public byte a;
    }

    /// <summary>
    /// Unity renderer backend for Prowl.Quill vector graphics canvas.
    /// Implements ICanvasRenderer to provide GPU-accelerated rendering using Unity's mesh system.
    /// </summary>
    public sealed class QuillCanvasRenderer : ICanvasRenderer
    {
        private Shader _shader;
        private Material _material;
        private Mesh _mesh;
        private Texture2D _defaultTexture;
        private MaterialPropertyBlock _propertyBlock;
        private Camera _camera;

        // Shader property IDs
        private static readonly int _MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int _ScissorMatID = Shader.PropertyToID("_ScissorMat");
        private static readonly int _ScissorExtID = Shader.PropertyToID("_ScissorExt");
        private static readonly int _BrushMatID = Shader.PropertyToID("_BrushMat");
        private static readonly int _BrushTypeID = Shader.PropertyToID("_BrushType");
        private static readonly int _BrushColor1ID = Shader.PropertyToID("_BrushColor1");
        private static readonly int _BrushColor2ID = Shader.PropertyToID("_BrushColor2");
        private static readonly int _BrushParamsID = Shader.PropertyToID("_BrushParams");
        private static readonly int _BrushParams2ID = Shader.PropertyToID("_BrushParams2");
        private static readonly int _BrushTextureMatID = Shader.PropertyToID("_BrushTextureMat");
        private static readonly int _DpiScaleID = Shader.PropertyToID("_DpiScale");

        // Vertex layout: Position (2 floats) + UV (2 floats) + Color (4 bytes packed as 1 uint)
        private static readonly VertexAttributeDescriptor[] s_vertexAttributes = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        private const MeshUpdateFlags NoMeshChecks = MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds
            | MeshUpdateFlags.DontValidateIndices;

        private int _width;
        private int _height;

        /// <summary>
        /// Initialize the renderer with window dimensions.
        /// </summary>
        /// <param name="width">The viewport width in pixels.</param>
        /// <param name="height">The viewport height in pixels.</param>
        /// <param name="defaultTexture">Optional default white texture. If null, one will be created.</param>
        /// <param name="camera">Optional camera to use for rendering. If null, uses Camera.main.</param>
        public void Initialize(int width, int height, Texture2D defaultTexture = null, Camera camera = null)
        {
            _width = width;
            _height = height;
            _camera = camera;

            // Load or create shader
            _shader = Shader.Find("Quill/CanvasShader");
            if (_shader == null)
            {
                Debug.LogError("QuillCanvasRenderer: Could not find 'Quill/CanvasShader'. Make sure QuillShader.shader is in your project.");
                return;
            }

            // Create material
            _material = new Material(_shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            // Create mesh
            _mesh = new Mesh
            {
                name = "Quill Canvas Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            _mesh.MarkDynamic();

            // Create property block for per-draw-call properties
            _propertyBlock = new MaterialPropertyBlock();

            // Create or use default texture
            if (defaultTexture != null)
            {
                _defaultTexture = defaultTexture;
            }
            else
            {
                _defaultTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "Quill Default White",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _defaultTexture.SetPixel(0, 0, UnityEngine.Color.white);
                _defaultTexture.Apply();
            }
        }

        /// <summary>
        /// Update the viewport dimensions when the screen size changes.
        /// </summary>
        public void UpdateSize(int width, int height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>
        /// Creates a new texture with the specified dimensions.
        /// </summary>
        public object CreateTexture(uint width, uint height)
        {
            var texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return texture;
        }

        /// <summary>
        /// Gets the dimensions of a texture.
        /// </summary>
        public Int2 GetTextureSize(object texture)
        {
            if (texture is not Texture2D tex)
                throw new ArgumentException("Invalid texture type - expected Texture2D", nameof(texture));

            return new Int2(tex.width, tex.height);
        }

        /// <summary>
        /// Updates a region of a texture with new pixel data.
        /// </summary>
        public void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is not Texture2D tex)
                throw new ArgumentException("Invalid texture type - expected Texture2D", nameof(texture));

            // Unity's SetPixelData expects data for the entire texture or we use SetPixels32 for a region
            var colors = new UnityEngine.Color32[data.Length / 4];
            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i * 4;
                colors[i] = new UnityEngine.Color32(data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
            }

            // SetPixels for the specific region
            tex.SetPixels32(bounds.Min.X, bounds.Min.Y, bounds.Size.X, bounds.Size.Y, colors);
            tex.Apply(false);
        }

        /// <summary>
        /// Renders the accumulated draw calls.
        /// </summary>
        public void RenderCalls(QuillCanvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            if (drawCalls.Count == 0)
                return;

            var vertices = canvas.Vertices;
            var indices = canvas.Indices;

            if (vertices.Count == 0 || indices.Count == 0)
                return;

            // Clear mesh and set up buffers
            _mesh.Clear(true);
            _mesh.SetVertexBufferParams(vertices.Count, s_vertexAttributes);
            _mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);

            // Upload vertex data
            UploadVertices(vertices);

            // Upload index data
            UploadIndices(indices);

            // Set submesh count AFTER buffer setup (buffer setup can reset it)
            _mesh.subMeshCount = drawCalls.Count;

            // Define submeshes
            int indexOffset = 0;
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];
                var descriptor = new SubMeshDescriptor
                {
                    topology = MeshTopology.Triangles,
                    indexStart = indexOffset,
                    indexCount = drawCall.ElementCount,
                    baseVertex = 0,
                };
                _mesh.SetSubMesh(i, descriptor, NoMeshChecks);
                indexOffset += drawCall.ElementCount;
            }

            _mesh.UploadMeshData(false);

            // Set up orthographic projection matrix
            var projectionMatrix = Matrix4x4.Ortho(0, _width, _height, 0, -1, 1);

            // Set up GL matrices for immediate mode rendering
            GL.PushMatrix();
            GL.LoadProjectionMatrix(projectionMatrix);
            GL.modelview = Matrix4x4.identity;

            // Render each draw call
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];

                // Set texture
                var texture = drawCall.Texture as Texture2D ?? _defaultTexture;
                _material.SetTexture(_MainTexID, texture);

                // Set DPI scale
                _material.SetFloat(_DpiScaleID, canvas.Scale);

                // Set scissor parameters
                drawCall.GetScissor(out var scissorMat, out var scissorExt);
                _material.SetMatrix(_ScissorMatID, ToUnityMatrix(scissorMat));
                _material.SetVector(_ScissorExtID, new Vector2((float)scissorExt.X, (float)scissorExt.Y));

                // Set brush parameters
                var brush = drawCall.Brush;
                _material.SetMatrix(_BrushMatID, ToUnityMatrix(brush.BrushMatrix));
                _material.SetInt(_BrushTypeID, (int)brush.Type);
                _material.SetVector(_BrushColor1ID, ToUnityColor(brush.Color1));
                _material.SetVector(_BrushColor2ID, ToUnityColor(brush.Color2));
                _material.SetVector(_BrushParamsID, new Vector4(
                    (float)brush.Point1.X, (float)brush.Point1.Y,
                    (float)brush.Point2.X, (float)brush.Point2.Y));
                _material.SetVector(_BrushParams2ID, new Vector2(brush.CornerRadii, brush.Feather));
                _material.SetMatrix(_BrushTextureMatID, ToUnityMatrix(brush.TextureMatrix));

                // Set the material pass and draw
                _material.SetPass(0);
                Graphics.DrawMeshNow(_mesh, Matrix4x4.identity, i);
            }

            GL.PopMatrix();
        }

        /// <summary>
        /// Renders the canvas using a CommandBuffer (for use with camera render callbacks or SRP).
        /// </summary>
        public void RenderCalls(CommandBuffer cmd, QuillCanvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            if (drawCalls.Count == 0)
                return;

            var vertices = canvas.Vertices;
            var indices = canvas.Indices;

            if (vertices.Count == 0 || indices.Count == 0)
                return;

            // Clear mesh
            _mesh.Clear(true);

            // Convert vertices to Unity format using standard arrays
            var positions = new Vector3[vertices.Count];
            var uvs = new Vector2[vertices.Count];
            var colors = new UnityEngine.Color32[vertices.Count];

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                positions[i] = new Vector3(v.x, v.y, 0);
                uvs[i] = new Vector2(v.u, v.v);
                colors[i] = new UnityEngine.Color32(v.r, v.g, v.b, v.a);
            }

            // Convert indices to int array
            var indexArray = new int[indices.Count];
            for (int i = 0; i < indices.Count; i++)
            {
                indexArray[i] = (int)indices[i];
            }

            // Set mesh data using standard API
            _mesh.vertices = positions;
            _mesh.uv = uvs;
            _mesh.colors32 = colors;

            // Set submeshes
            _mesh.subMeshCount = drawCalls.Count;
            int indexOffset = 0;
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];
                var subIndices = new int[drawCall.ElementCount];
                System.Array.Copy(indexArray, indexOffset, subIndices, 0, drawCall.ElementCount);
                _mesh.SetTriangles(subIndices, i);
                indexOffset += drawCall.ElementCount;
            }

            // Set bounds to prevent culling
            _mesh.bounds = new Bounds(new Vector3(_width / 2f, _height / 2f, 0), new Vector3(_width, _height, 1));

            // Set viewport
            cmd.SetViewport(new UnityEngine.Rect(0, 0, _width, _height));

            // Set up orthographic projection (matching ImGui approach)
            // Small offset improves text rendering
            var viewMatrix = Matrix4x4.Translate(new Vector3(0.5f / _width, 0.5f / _height, 0f));
            var projectionMatrix = Matrix4x4.Ortho(0, _width, _height, 0, -1, 1);
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

            // Render each draw call
            for (int i = 0; i < drawCalls.Count; i++)
            {
                var drawCall = drawCalls[i];

                // Set texture
                var texture = drawCall.Texture as Texture2D ?? _defaultTexture;
                _propertyBlock.SetTexture(_MainTexID, texture);

                // Set DPI scale
                _propertyBlock.SetFloat(_DpiScaleID, canvas.Scale);

                // Set scissor parameters
                drawCall.GetScissor(out var scissorMat, out var scissorExt);
                _propertyBlock.SetMatrix(_ScissorMatID, ToUnityMatrix(scissorMat));
                _propertyBlock.SetVector(_ScissorExtID, new Vector2((float)scissorExt.X, (float)scissorExt.Y));

                // Set brush parameters
                var brush = drawCall.Brush;
                _propertyBlock.SetMatrix(_BrushMatID, ToUnityMatrix(brush.BrushMatrix));
                _propertyBlock.SetInt(_BrushTypeID, (int)brush.Type);
                _propertyBlock.SetVector(_BrushColor1ID, ToUnityColor(brush.Color1));
                _propertyBlock.SetVector(_BrushColor2ID, ToUnityColor(brush.Color2));
                _propertyBlock.SetVector(_BrushParamsID, new Vector4(
                    (float)brush.Point1.X, (float)brush.Point1.Y,
                    (float)brush.Point2.X, (float)brush.Point2.Y));
                _propertyBlock.SetVector(_BrushParams2ID, new Vector2(brush.CornerRadii, brush.Feather));
                _propertyBlock.SetMatrix(_BrushTextureMatID, ToUnityMatrix(brush.TextureMatrix));

                // Draw the submesh
                cmd.DrawMesh(_mesh, Matrix4x4.identity, _material, i, -1, _propertyBlock);
            }
        }

        private void UploadVertices(IReadOnlyList<Vertex> vertices)
        {
            // Create native array with our local vertex struct for Unity compatibility
            var nativeVertices = new NativeArray<UnityVertex>(vertices.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                nativeVertices[i] = new UnityVertex
                {
                    x = v.x,
                    y = v.y,
                    u = v.u,
                    v = v.v,
                    r = v.r,
                    g = v.g,
                    b = v.b,
                    a = v.a
                };
            }

            _mesh.SetVertexBufferData(nativeVertices, 0, 0, vertices.Count, 0, NoMeshChecks);
            nativeVertices.Dispose();
        }

        private void UploadIndices(IReadOnlyList<uint> indices)
        {
            var nativeIndices = new NativeArray<uint>(indices.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < indices.Count; i++)
            {
                nativeIndices[i] = indices[i];
            }

            _mesh.SetIndexBufferData(nativeIndices, 0, 0, indices.Count, NoMeshChecks);
            nativeIndices.Dispose();
        }

        private static Matrix4x4 ToUnityMatrix(Float4x4 mat)
        {
            return new Matrix4x4(
                new Vector4((float)mat[0, 0], (float)mat[1, 0], (float)mat[2, 0], (float)mat[3, 0]),
                new Vector4((float)mat[0, 1], (float)mat[1, 1], (float)mat[2, 1], (float)mat[3, 1]),
                new Vector4((float)mat[0, 2], (float)mat[1, 2], (float)mat[2, 2], (float)mat[3, 2]),
                new Vector4((float)mat[0, 3], (float)mat[1, 3], (float)mat[2, 3], (float)mat[3, 3])
            );
        }

        private static Vector4 ToUnityColor(QuillColor32 color)
        {
            return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        public void Dispose()
        {
            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_material != null)
            {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }

            if (_defaultTexture != null && _defaultTexture.name == "Quill Default White")
            {
                UnityEngine.Object.DestroyImmediate(_defaultTexture);
                _defaultTexture = null;
            }
        }
    }
}
