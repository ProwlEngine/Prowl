using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Provides static methods for generating procedural geometry primitives.
    /// All generators return GeometryData which can be further manipulated with GeometryOperators.
    /// </summary>
    public static class GeometryGenerator
    {
        #region Box

        /// <summary>
        /// Creates a box (cube or rectangular prism) geometry.
        /// </summary>
        /// <param name="size">Dimensions of the box (width, height, depth)</param>
        /// <param name="center">Center position of the box</param>
        /// <param name="segments">Number of subdivisions per face (minimum 1)</param>
        /// <returns>GeometryData representing the box with quad faces</returns>
        public static GeometryData Box(Double3 size, Double3 center = default, Int3 segments = default)
        {
            if (segments == default) segments = new Int3(1, 1, 1);
            segments = new Int3(
                Maths.Max(1, segments.X),
                Maths.Max(1, segments.Y),
                Maths.Max(1, segments.Z)
            );

            var geometryData = new GeometryData();
            Double3 halfSize = size * 0.5;

            // If no subdivisions, use a simple 8-vertex cube
            if (segments.X == 1 && segments.Y == 1 && segments.Z == 1)
            {
                // Create 8 corner vertices
                var v0 = geometryData.AddVertex(center + new Double3(-halfSize.X, -halfSize.Y, -halfSize.Z));
                var v1 = geometryData.AddVertex(center + new Double3(halfSize.X, -halfSize.Y, -halfSize.Z));
                var v2 = geometryData.AddVertex(center + new Double3(halfSize.X, halfSize.Y, -halfSize.Z));
                var v3 = geometryData.AddVertex(center + new Double3(-halfSize.X, halfSize.Y, -halfSize.Z));
                var v4 = geometryData.AddVertex(center + new Double3(-halfSize.X, -halfSize.Y, halfSize.Z));
                var v5 = geometryData.AddVertex(center + new Double3(halfSize.X, -halfSize.Y, halfSize.Z));
                var v6 = geometryData.AddVertex(center + new Double3(halfSize.X, halfSize.Y, halfSize.Z));
                var v7 = geometryData.AddVertex(center + new Double3(-halfSize.X, halfSize.Y, halfSize.Z));

                // Create 6 faces
                geometryData.AddFace(v1, v0, v3, v2); // Front (-Z)
                geometryData.AddFace(v4, v5, v6, v7); // Back (+Z)
                geometryData.AddFace(v5, v1, v2, v6); // Right (+X)
                geometryData.AddFace(v0, v4, v7, v3); // Left (-X)
                geometryData.AddFace(v2, v3, v7, v6); // Top (+Y)
                geometryData.AddFace(v5, v4, v0, v1); // Bottom (-Y)
            }
            else
            {
                // Use subdivided faces (original behavior)
                // Front face (+Z)
                AddBoxFace(geometryData, center, halfSize, segments.X, segments.Y,
                    new Double3(-1, -1, 1), new Double3(1, 0, 0), new Double3(0, 1, 0));

                // Back face (-Z)
                AddBoxFace(geometryData, center, halfSize, segments.X, segments.Y,
                    new Double3(1, -1, -1), new Double3(-1, 0, 0), new Double3(0, 1, 0));

                // Right face (+X)
                AddBoxFace(geometryData, center, halfSize, segments.Z, segments.Y,
                    new Double3(1, -1, 1), new Double3(0, 0, -1), new Double3(0, 1, 0));

                // Left face (-X)
                AddBoxFace(geometryData, center, halfSize, segments.Z, segments.Y,
                    new Double3(-1, -1, -1), new Double3(0, 0, 1), new Double3(0, 1, 0));

                // Top face (+Y)
                AddBoxFace(geometryData, center, halfSize, segments.X, segments.Z,
                    new Double3(-1, 1, 1), new Double3(1, 0, 0), new Double3(0, 0, -1));

                // Bottom face (-Y)
                AddBoxFace(geometryData, center, halfSize, segments.X, segments.Z,
                    new Double3(-1, -1, -1), new Double3(1, 0, 0), new Double3(0, 0, 1));

                // Weld vertices along edges to avoid duplicates
                GeometryOperators.WeldVertices(geometryData, 0.0001);
            }

            return geometryData;
        }

        /// <summary>
        /// Creates a simple box with given size centered at origin.
        /// </summary>
        public static GeometryData Box(double width, double height, double depth)
        {
            return Box(new Double3(width, height, depth));
        }

        private static void AddBoxFace(GeometryData mesh, Double3 center, Double3 halfSize,
            int segmentsU, int segmentsV, Double3 corner, Double3 uDir, Double3 vDir)
        {
            var vertices = new GeometryData.Vertex[segmentsV + 1, segmentsU + 1];

            for (int v = 0; v <= segmentsV; v++)
            {
                for (int u = 0; u <= segmentsU; u++)
                {
                    double uf = (double)u / segmentsU;
                    double vf = (double)v / segmentsV;

                    Double3 pos = center + new Double3(
                        (corner.X + uDir.X * 2 * uf + vDir.X * 2 * vf) * halfSize.X,
                        (corner.Y + uDir.Y * 2 * uf + vDir.Y * 2 * vf) * halfSize.Y,
                        (corner.Z + uDir.Z * 2 * uf + vDir.Z * 2 * vf) * halfSize.Z
                    );

                    vertices[v, u] = mesh.AddVertex(pos);
                }
            }

            for (int v = 0; v < segmentsV; v++)
            {
                for (int u = 0; u < segmentsU; u++)
                {
                    mesh.AddFace(
                        vertices[v, u],
                        vertices[v, u + 1],
                        vertices[v + 1, u + 1],
                        vertices[v + 1, u]
                    );
                }
            }
        }

        #endregion

        #region Plane

        /// <summary>
        /// Creates a planar grid geometry.
        /// </summary>
        /// <param name="size">Width and depth of the plane</param>
        /// <param name="center">Center position of the plane</param>
        /// <param name="segments">Number of subdivisions (X and Z)</param>
        /// <param name="normal">Normal direction of the plane (default is up/+Y)</param>
        /// <returns>GeometryData representing the plane with quad faces</returns>
        public static GeometryData Plane(Double2 size, Double3 center = default, Int2 segments = default, Double3 normal = default)
        {
            if (segments == default) segments = new Int2(1, 1);
            if (normal == default) normal = Double3.UnitY;

            segments = new Int2(Maths.Max(1, segments.X), Maths.Max(1, segments.Y));

            var geometryData = new GeometryData();
            Double2 halfSize = size * 0.5;

            // Create a coordinate system aligned with the normal
            Double3 tangent, bitangent;
            if (Maths.Abs(normal.Y) < 0.999)
            {
                tangent = Double3.Normalize(Double3.Cross(normal, Double3.UnitY));
            }
            else
            {
                tangent = Double3.Normalize(Double3.Cross(normal, Double3.UnitZ));
            }
            bitangent = Double3.Normalize(Double3.Cross(normal, tangent));

            var vertices = new GeometryData.Vertex[segments.Y + 1, segments.X + 1];

            for (int z = 0; z <= segments.Y; z++)
            {
                for (int x = 0; x <= segments.X; x++)
                {
                    double u = (double)x / segments.X - 0.5;
                    double v = (double)z / segments.Y - 0.5;

                    Double3 pos = center + tangent * (u * size.X) + bitangent * (v * size.Y);
                    vertices[z, x] = geometryData.AddVertex(pos);
                }
            }

            for (int z = 0; z < segments.Y; z++)
            {
                for (int x = 0; x < segments.X; x++)
                {
                    geometryData.AddFace(
                        vertices[z, x],
                        vertices[z, x + 1],
                        vertices[z + 1, x + 1],
                        vertices[z + 1, x]
                    );
                }
            }

            return geometryData;
        }

        /// <summary>
        /// Creates a square plane with given size.
        /// </summary>
        public static GeometryData Plane(double size, int segments = 1)
        {
            return Plane(new Double2(size, size), segments: new Int2(segments, segments));
        }

        #endregion

        #region Sphere

        /// <summary>
        /// Creates a UV sphere geometry with latitude/longitude topology.
        /// </summary>
        /// <param name="radius">Radius of the sphere</param>
        /// <param name="center">Center position of the sphere</param>
        /// <param name="segments">Number of horizontal segments (longitude)</param>
        /// <param name="rings">Number of vertical rings (latitude)</param>
        /// <returns>GeometryData representing the sphere with quad faces</returns>
        public static GeometryData Sphere(double radius, Double3 center = default, int segments = 32, int rings = 16)
        {
            segments = Maths.Max(3, segments);
            rings = Maths.Max(2, rings);

            var geometryData = new GeometryData();
            var vertices = new GeometryData.Vertex[rings + 1, segments];

            for (int lat = 0; lat <= rings; lat++)
            {
                double theta = lat * Maths.PI / rings;
                double sinTheta = Maths.Sin(theta);
                double cosTheta = Maths.Cos(theta);

                for (int lon = 0; lon < segments; lon++)
                {
                    double phi = lon * 2.0 * Maths.PI / segments;
                    double sinPhi = Maths.Sin(phi);
                    double cosPhi = Maths.Cos(phi);

                    Double3 position = center + new Double3(
                        radius * sinTheta * cosPhi,
                        radius * cosTheta,
                        radius * sinTheta * sinPhi
                    );

                    vertices[lat, lon] = geometryData.AddVertex(position);
                }
            }

            for (int lat = 0; lat < rings; lat++)
            {
                for (int lon = 0; lon < segments; lon++)
                {
                    int nextLon = (lon + 1) % segments;

                    var v0 = vertices[lat, lon];
                    var v1 = vertices[lat, nextLon];
                    var v2 = vertices[lat + 1, nextLon];
                    var v3 = vertices[lat + 1, lon];

                    geometryData.AddFace(v0, v1, v2, v3);
                }
            }

            return geometryData;
        }

        #endregion

        #region Icosphere

        /// <summary>
        /// Creates an icosphere (geodesic sphere) with triangular topology.
        /// </summary>
        /// <param name="radius">Radius of the sphere</param>
        /// <param name="center">Center position of the sphere</param>
        /// <param name="subdivisions">Number of subdivisions (0 = icosahedron, higher = smoother)</param>
        /// <returns>GeometryData representing the icosphere with triangular faces</returns>
        public static GeometryData Icosphere(double radius, Double3 center = default, int subdivisions = 2)
        {
            subdivisions = Maths.Max(0, subdivisions);

            var geometryData = new GeometryData();

            // Create icosahedron vertices
            double t = (1.0 + Maths.Sqrt(5.0)) / 2.0; // Golden ratio

            List<Double3> positions = new List<Double3>
            {
                Double3.Normalize(new Double3(-1,  t,  0)) * radius + center,
                Double3.Normalize(new Double3( 1,  t,  0)) * radius + center,
                Double3.Normalize(new Double3(-1, -t,  0)) * radius + center,
                Double3.Normalize(new Double3( 1, -t,  0)) * radius + center,
                Double3.Normalize(new Double3( 0, -1,  t)) * radius + center,
                Double3.Normalize(new Double3( 0,  1,  t)) * radius + center,
                Double3.Normalize(new Double3( 0, -1, -t)) * radius + center,
                Double3.Normalize(new Double3( 0,  1, -t)) * radius + center,
                Double3.Normalize(new Double3( t,  0, -1)) * radius + center,
                Double3.Normalize(new Double3( t,  0,  1)) * radius + center,
                Double3.Normalize(new Double3(-t,  0, -1)) * radius + center,
                Double3.Normalize(new Double3(-t,  0,  1)) * radius + center
            };

            // Create icosahedron faces (20 triangles)
            List<(int, int, int)> indices = new List<(int, int, int)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
                (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
                (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)
            };

            // Subdivide
            for (int i = 0; i < subdivisions; i++)
            {
                var newIndices = new List<(int, int, int)>();
                var midPointCache = new Dictionary<(int, int), int>();

                int GetMidPoint(int i1, int i2)
                {
                    var key = i1 < i2 ? (i1, i2) : (i2, i1);
                    if (midPointCache.TryGetValue(key, out int cached))
                        return cached;

                    Double3 mid = Double3.Normalize((positions[i1] - center + positions[i2] - center) * 0.5) * radius + center;
                    int index = positions.Count;
                    positions.Add(mid);
                    midPointCache[key] = index;
                    return index;
                }

                foreach (var (i0, i1, i2) in indices)
                {
                    int m0 = GetMidPoint(i0, i1);
                    int m1 = GetMidPoint(i1, i2);
                    int m2 = GetMidPoint(i2, i0);

                    newIndices.Add((i0, m0, m2));
                    newIndices.Add((i1, m1, m0));
                    newIndices.Add((i2, m2, m1));
                    newIndices.Add((m0, m1, m2));
                }

                indices = newIndices;
            }

            // Build GeometryData
            var vertices = new List<GeometryData.Vertex>();
            foreach (var pos in positions)
            {
                vertices.Add(geometryData.AddVertex(pos));
            }

            foreach (var (i0, i1, i2) in indices)
            {
                geometryData.AddFace(vertices[i0], vertices[i1], vertices[i2]);
            }

            return geometryData;
        }

        #endregion

        #region Cylinder

        /// <summary>
        /// Creates a cylinder geometry.
        /// </summary>
        /// <param name="radius">Radius of the cylinder</param>
        /// <param name="height">Height of the cylinder</param>
        /// <param name="center">Center position of the cylinder</param>
        /// <param name="segments">Number of radial segments</param>
        /// <param name="rings">Number of height segments</param>
        /// <param name="capTop">Include top cap</param>
        /// <param name="capBottom">Include bottom cap</param>
        /// <returns>GeometryData representing the cylinder</returns>
        public static GeometryData Cylinder(double radius, double height, Double3 center = default,
            int segments = 32, int rings = 1, bool capTop = true, bool capBottom = true)
        {
            segments = Maths.Max(3, segments);
            rings = Maths.Max(1, rings);

            var geometryData = new GeometryData();
            double halfHeight = height * 0.5;

            // Side vertices (without wrapping duplicate at seg=segments)
            var sideVertices = new GeometryData.Vertex[rings + 1, segments];

            for (int ring = 0; ring <= rings; ring++)
            {
                double y = -halfHeight + (height * ring / rings);

                for (int seg = 0; seg < segments; seg++)
                {
                    double angle = seg * 2.0 * Maths.PI / segments;
                    Double3 pos = center + new Double3(
                        Maths.Cos(angle) * radius,
                        y,
                        Maths.Sin(angle) * radius
                    );

                    sideVertices[ring, seg] = geometryData.AddVertex(pos);
                }
            }

            // Side faces
            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int nextSeg = (seg + 1) % segments;
                    geometryData.AddFace(
                        sideVertices[ring, nextSeg],
                        sideVertices[ring, seg],
                        sideVertices[ring + 1, seg],
                        sideVertices[ring + 1, nextSeg]
                    );
                }
            }

            // Top cap (reuse top ring vertices from side)
            if (capTop)
            {
                var topCenter = geometryData.AddVertex(center + new Double3(0, halfHeight, 0));

                for (int seg = 0; seg < segments; seg++)
                {
                    int next = (seg + 1) % segments;
                    geometryData.AddFace(topCenter, sideVertices[rings, next], sideVertices[rings, seg]);
                }
            }

            // Bottom cap (reuse bottom ring vertices from side)
            if (capBottom)
            {
                var bottomCenter = geometryData.AddVertex(center + new Double3(0, -halfHeight, 0));

                for (int seg = 0; seg < segments; seg++)
                {
                    int next = (seg + 1) % segments;
                    geometryData.AddFace(bottomCenter, sideVertices[0, seg], sideVertices[0, next]);
                }
            }

            return geometryData;
        }

        #endregion

        #region Cone

        /// <summary>
        /// Creates a cone geometry.
        /// </summary>
        /// <param name="radius">Radius of the base</param>
        /// <param name="height">Height of the cone</param>
        /// <param name="center">Center position of the base</param>
        /// <param name="segments">Number of radial segments</param>
        /// <param name="capBottom">Include bottom cap</param>
        /// <returns>GeometryData representing the cone</returns>
        public static GeometryData Cone(double radius, double height, Double3 center = default,
            int segments = 32, bool capBottom = true)
        {
            segments = Maths.Max(3, segments);

            var geometryData = new GeometryData();

            // Apex
            var apex = geometryData.AddVertex(center + new Double3(0, height, 0));

            // Base ring
            var baseRing = new GeometryData.Vertex[segments];
            for (int seg = 0; seg < segments; seg++)
            {
                double angle = seg * 2.0 * Maths.PI / segments;
                Double3 pos = center + new Double3(
                    Maths.Cos(angle) * radius,
                    0,
                    Maths.Sin(angle) * radius
                );
                baseRing[seg] = geometryData.AddVertex(pos);
            }

            // Side faces
            for (int seg = 0; seg < segments; seg++)
            {
                int next = (seg + 1) % segments;
                geometryData.AddFace(apex, baseRing[next], baseRing[seg]);
            }

            // Bottom cap (reuses baseRing vertices)
            if (capBottom)
            {
                geometryData.AddFace(baseRing);
            }

            return geometryData;
        }

        #endregion

        #region Torus

        /// <summary>
        /// Creates a torus (donut) geometry.
        /// </summary>
        /// <param name="majorRadius">Distance from center to tube center</param>
        /// <param name="minorRadius">Radius of the tube</param>
        /// <param name="center">Center position of the torus</param>
        /// <param name="majorSegments">Number of segments around the major circle</param>
        /// <param name="minorSegments">Number of segments around the tube</param>
        /// <returns>GeometryData representing the torus with quad faces</returns>
        public static GeometryData Torus(double majorRadius, double minorRadius, Double3 center = default,
            int majorSegments = 48, int minorSegments = 24)
        {
            majorSegments = Maths.Max(3, majorSegments);
            minorSegments = Maths.Max(3, minorSegments);

            var geometryData = new GeometryData();
            var vertices = new GeometryData.Vertex[majorSegments, minorSegments];

            for (int i = 0; i < majorSegments; i++)
            {
                double u = (double)i / majorSegments * 2.0 * Maths.PI;
                double cosU = Maths.Cos(u);
                double sinU = Maths.Sin(u);

                for (int j = 0; j < minorSegments; j++)
                {
                    double v = (double)j / minorSegments * 2.0 * Maths.PI;
                    double cosV = Maths.Cos(v);
                    double sinV = Maths.Sin(v);

                    Double3 pos = center + new Double3(
                        (majorRadius + minorRadius * cosV) * cosU,
                        minorRadius * sinV,
                        (majorRadius + minorRadius * cosV) * sinU
                    );

                    vertices[i, j] = geometryData.AddVertex(pos);
                }
            }

            for (int i = 0; i < majorSegments; i++)
            {
                int iNext = (i + 1) % majorSegments;

                for (int j = 0; j < minorSegments; j++)
                {
                    int jNext = (j + 1) % minorSegments;

                    geometryData.AddFace(
                        vertices[iNext, j],
                        vertices[i, j],
                        vertices[i, jNext],
                        vertices[iNext, jNext]
                    );
                }
            }

            return geometryData;
        }

        #endregion

        #region Platonic Solids

        /// <summary>
        /// Creates a tetrahedron (4-sided regular polyhedron).
        /// </summary>
        /// <param name="size">Size of the tetrahedron</param>
        /// <param name="center">Center position</param>
        /// <returns>GeometryData representing the tetrahedron</returns>
        public static GeometryData Tetrahedron(double size, Double3 center = default)
        {
            var geometryData = new GeometryData();

            double a = size / Maths.Sqrt(3.0);

            var v0 = geometryData.AddVertex(center + new Double3(a, a, a));
            var v1 = geometryData.AddVertex(center + new Double3(-a, -a, a));
            var v2 = geometryData.AddVertex(center + new Double3(-a, a, -a));
            var v3 = geometryData.AddVertex(center + new Double3(a, -a, -a));

            geometryData.AddFace(v0, v2, v1);
            geometryData.AddFace(v0, v1, v3);
            geometryData.AddFace(v0, v3, v2);
            geometryData.AddFace(v1, v2, v3);

            return geometryData;
        }

        /// <summary>
        /// Creates an octahedron (8-sided regular polyhedron).
        /// </summary>
        /// <param name="size">Size of the octahedron</param>
        /// <param name="center">Center position</param>
        /// <returns>GeometryData representing the octahedron</returns>
        public static GeometryData Octahedron(double size, Double3 center = default)
        {
            var geometryData = new GeometryData();

            double s = size / Maths.Sqrt(2.0);

            var v0 = geometryData.AddVertex(center + new Double3(s, 0, 0));
            var v1 = geometryData.AddVertex(center + new Double3(-s, 0, 0));
            var v2 = geometryData.AddVertex(center + new Double3(0, s, 0));
            var v3 = geometryData.AddVertex(center + new Double3(0, -s, 0));
            var v4 = geometryData.AddVertex(center + new Double3(0, 0, s));
            var v5 = geometryData.AddVertex(center + new Double3(0, 0, -s));

            // Upper pyramid
            geometryData.AddFace(v2, v4, v0);
            geometryData.AddFace(v2, v0, v5);
            geometryData.AddFace(v2, v5, v1);
            geometryData.AddFace(v2, v1, v4);

            // Lower pyramid
            geometryData.AddFace(v3, v0, v4);
            geometryData.AddFace(v3, v5, v0);
            geometryData.AddFace(v3, v1, v5);
            geometryData.AddFace(v3, v4, v1);

            return geometryData;
        }

        /// <summary>
        /// Creates a dodecahedron (12-sided regular polyhedron).
        /// </summary>
        /// <param name="size">Size of the dodecahedron</param>
        /// <param name="center">Center position</param>
        /// <returns>GeometryData representing the dodecahedron with pentagonal faces</returns>
        public static GeometryData Dodecahedron(double size, Double3 center = default)
        {
            var geometryData = new GeometryData();

            double phi = (1.0 + Maths.Sqrt(5.0)) / 2.0; // Golden ratio
            double a = size / Maths.Sqrt(3.0);
            double b = a / phi;
            double c = a * phi;

            // Create 20 vertices
            var vertices = new List<GeometryData.Vertex>
            {
                // (±1, ±1, ±1)
                geometryData.AddVertex(center + new Double3(a, a, a)),
                geometryData.AddVertex(center + new Double3(a, a, -a)),
                geometryData.AddVertex(center + new Double3(a, -a, a)),
                geometryData.AddVertex(center + new Double3(a, -a, -a)),
                geometryData.AddVertex(center + new Double3(-a, a, a)),
                geometryData.AddVertex(center + new Double3(-a, a, -a)),
                geometryData.AddVertex(center + new Double3(-a, -a, a)),
                geometryData.AddVertex(center + new Double3(-a, -a, -a)),

                // (0, ±1/φ, ±φ)
                geometryData.AddVertex(center + new Double3(0, b, c)),
                geometryData.AddVertex(center + new Double3(0, b, -c)),
                geometryData.AddVertex(center + new Double3(0, -b, c)),
                geometryData.AddVertex(center + new Double3(0, -b, -c)),

                // (±1/φ, ±φ, 0)
                geometryData.AddVertex(center + new Double3(b, c, 0)),
                geometryData.AddVertex(center + new Double3(b, -c, 0)),
                geometryData.AddVertex(center + new Double3(-b, c, 0)),
                geometryData.AddVertex(center + new Double3(-b, -c, 0)),

                // (±φ, 0, ±1/φ)
                geometryData.AddVertex(center + new Double3(c, 0, b)),
                geometryData.AddVertex(center + new Double3(c, 0, -b)),
                geometryData.AddVertex(center + new Double3(-c, 0, b)),
                geometryData.AddVertex(center + new Double3(-c, 0, -b))
            };

            // Create 12 pentagonal faces
            int[][] faces = new int[][]
            {
                new int[] { 0, 16, 2, 10, 8 },
                new int[] { 0, 8, 4, 14, 12 },
                new int[] { 0, 12, 1, 17, 16 },
                new int[] { 1, 9, 11, 3, 17 },
                new int[] { 1, 12, 14, 5, 9 },
                new int[] { 2, 13, 15, 6, 10 },
                new int[] { 2, 16, 17, 3, 13 },
                new int[] { 3, 11, 7, 15, 13 },
                new int[] { 4, 8, 10, 6, 18 },
                new int[] { 4, 18, 19, 5, 14 },
                new int[] { 5, 19, 7, 11, 9 },
                new int[] { 6, 15, 7, 19, 18 }
            };

            foreach (var face in faces)
            {
                geometryData.AddFace(
                    vertices[face[0]],
                    vertices[face[1]],
                    vertices[face[2]],
                    vertices[face[3]],
                    vertices[face[4]]
                );
            }

            return geometryData;
        }

        #endregion

        #region IndexedMesh

        /// <summary>
        /// Creates geometry from indexed vertex and face data.
        /// </summary>
        /// <param name="vertices">Array of vertex positions</param>
        /// <param name="indices">Array of face indices (flat array where each face can have variable vertex count)</param>
        /// <param name="faceSizes">Array specifying number of vertices per face (if null, assumes all triangles)</param>
        /// <returns>GeometryData created from the indexed data</returns>
        public static GeometryData IndexedMesh(Double3[] vertices, int[] indices, int[]? faceSizes = null)
        {
            var geometryData = new GeometryData();

            // Add all vertices
            var verts = new GeometryData.Vertex[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                verts[i] = geometryData.AddVertex(vertices[i]);
            }

            // Add faces
            if (faceSizes == null)
            {
                // Assume triangles
                for (int i = 0; i < indices.Length; i += 3)
                {
                    geometryData.AddFace(
                        verts[indices[i]],
                        verts[indices[i + 1]],
                        verts[indices[i + 2]]
                    );
                }
            }
            else
            {
                // Variable face sizes
                int indexOffset = 0;
                foreach (int faceSize in faceSizes)
                {
                    var faceVerts = new GeometryData.Vertex[faceSize];
                    for (int i = 0; i < faceSize; i++)
                    {
                        faceVerts[i] = verts[indices[indexOffset + i]];
                    }
                    geometryData.AddFace(faceVerts);
                    indexOffset += faceSize;
                }
            }

            return geometryData;
        }

        /// <summary>
        /// Creates geometry from indexed vertex and triangle data.
        /// </summary>
        /// <param name="vertices">Array of vertex positions</param>
        /// <param name="triangles">Array of triangle indices (3 indices per triangle)</param>
        /// <returns>GeometryData created from the indexed data</returns>
        public static GeometryData TriangleMesh(Double3[] vertices, int[] triangles)
        {
            return IndexedMesh(vertices, triangles, null);
        }

        #endregion
    }
}
