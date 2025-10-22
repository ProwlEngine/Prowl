// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Defines how primitives are assembled from vertex data.
    /// </summary>
    public enum MeshTopology
    {
        /// <summary>
        /// Each pair of vertices defines a line segment.
        /// </summary>
        LineList,

        /// <summary>
        /// Vertices form a continuous line. Each vertex after the first creates a line segment with the previous vertex.
        /// </summary>
        LineStrip,

        /// <summary>
        /// Each group of three vertices defines a triangle.
        /// </summary>
        TriangleList,

        /// <summary>
        /// Vertices form a triangle strip. After the first two vertices, each additional vertex forms a triangle with the previous two.
        /// </summary>
        TriangleStrip
    }

    /// <summary>
    /// Specifies the rendering mode for geometry visualization.
    /// </summary>
    public enum MeshMode
    {
        /// <summary>
        /// Render as wireframe/outline using lines.
        /// </summary>
        Wireframe,

        /// <summary>
        /// Render as solid filled geometry using triangles.
        /// </summary>
        Solid
    }

    /// <summary>
    /// Contains mesh data for rendering a shape, including vertices and topology information.
    /// </summary>
    public readonly struct MeshData
    {
        /// <summary>
        /// The vertex positions.
        /// </summary>
        public readonly Double3[] Vertices;

        /// <summary>
        /// Optional indices for indexed rendering. If null, vertices are rendered in order.
        /// </summary>
        public readonly uint[]? Indices;

        /// <summary>
        /// Defines how primitives are assembled from the vertex data.
        /// </summary>
        public readonly MeshTopology Topology;

        /// <summary>
        /// Creates mesh data with vertices and topology (non-indexed).
        /// </summary>
        public MeshData(Double3[] vertices, MeshTopology topology)
        {
            Vertices = vertices;
            Indices = null;
            Topology = topology;
        }

        /// <summary>
        /// Creates mesh data with vertices, indices, and topology (indexed rendering).
        /// </summary>
        public MeshData(Double3[] vertices, uint[] indices, MeshTopology topology)
        {
            Vertices = vertices;
            Indices = indices;
            Topology = topology;
        }

        /// <summary>
        /// Returns true if this mesh uses indexed rendering.
        /// </summary>
        public readonly bool IsIndexed => Indices != null;

        /// <summary>
        /// Gets the number of primitives based on topology and vertex/index count.
        /// </summary>
        public readonly int PrimitiveCount
        {
            get
            {
                int count = IsIndexed ? Indices!.Length : Vertices.Length;
                return Topology switch
                {
                    MeshTopology.LineList => count / 2,
                    MeshTopology.LineStrip => count > 1 ? count - 1 : 0,
                    MeshTopology.TriangleList => count / 3,
                    MeshTopology.TriangleStrip => count > 2 ? count - 2 : 0,
                    _ => 0
                };
            }
        }
    }
}
