// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Vector.Geometry.Operators
{
    internal static class SquarifyOp
    {
        /// <summary>
        /// Compute a local coordinate system for a quad face.
        /// </summary>
        private static Float4x4 ComputeLocalAxis(Float3 r0, Float3 r1, Float3 r2, Float3 r3)
        {
            Float3 Z = Float3.Normalize(
                Float3.Normalize(Float3.Cross(r0, r1)) +
                Float3.Normalize(Float3.Cross(r1, r2)) +
                Float3.Normalize(Float3.Cross(r2, r3)) +
                Float3.Normalize(Float3.Cross(r3, r0))
            );
            Float3 X = Float3.Normalize(r0);
            Float3 Y = Float3.Cross(Z, X);

            // Build transformation matrix
            return new Float4x4(
                new Float4(X.X, X.Y, X.Z, 0),
                new Float4(Y.X, Y.Y, Y.Z, 0),
                new Float4(Z.X, Z.Y, Z.Z, 0),
                new Float4(0, 0, 0, 1)
            );
        }

        /// <summary>
        /// Calculate average radius length of all quads in the mesh.
        /// </summary>
        private static float AverageRadiusLength(GeometryData mesh)
        {
            float lengthSum = 0;
            float weightSum = 0;

            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Float3 c = f.Center();
                Float3 r0 = verts[0].Point - c;
                Float3 r1 = verts[1].Point - c;
                Float3 r2 = verts[2].Point - c;
                Float3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Float4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Float3 l0 = Float4x4.TransformPoint(r0, globalToLocal);
                Float3 l1 = Float4x4.TransformPoint(r1, globalToLocal);
                Float3 l2 = Float4x4.TransformPoint(r2, globalToLocal);
                Float3 l3 = Float4x4.TransformPoint(r3, globalToLocal);

                // Rotate vectors to align
                Float3 rl0 = l0;
                Float3 rl1 = new Float3(l1.Y, -l1.X, l1.Z);
                Float3 rl2 = new Float3(-l2.X, -l2.Y, l2.Z);
                Float3 rl3 = new Float3(-l3.Y, l3.X, l3.Z);

                Float3 average = (rl0 + rl1 + rl2 + rl3) * 0.25f;

                lengthSum += Float3.Length(average);
                weightSum += 1;
            }

            return weightSum > 0f ? lengthSum / weightSum : 1.0f;
        }

        internal static void SquarifyQuads(GeometryData mesh, float rate = 1.0f, bool uniformLength = false)
        {
            float avg = uniformLength ? AverageRadiusLength(mesh) : 0;

            var pointUpdates = new Float3[mesh.Vertices.Count];
            var weights = new float[mesh.Vertices.Count];

            // Initialize with rest positions if available
            int i = 0;
            foreach (var v in mesh.Vertices)
            {
                if (mesh.HasVertexAttribute("restpos"))
                {
                    weights[i] = mesh.HasVertexAttribute("weight")
                        ? (v.Attributes["weight"] as GeometryData.FloatAttributeValue)?.Data[0] ?? 1.0f
                        : 1.0f;

                    var restpos = (v.Attributes["restpos"] as GeometryData.FloatAttributeValue)?.AsVector3() ?? v.Point;
                    pointUpdates[i] = (restpos - v.Point) * weights[i];
                }
                else
                {
                    pointUpdates[i] = Float3.Zero;
                    weights[i] = 0;
                }
                v.Id = i++;
            }

            // Accumulate squarification updates
            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Float3 c = f.Center();
                Float3 r0 = verts[0].Point - c;
                Float3 r1 = verts[1].Point - c;
                Float3 r2 = verts[2].Point - c;
                Float3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Float4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Float3 l0 = Float4x4.TransformPoint(r0, globalToLocal);
                Float3 l1 = Float4x4.TransformPoint(r1, globalToLocal);
                Float3 l2 = Float4x4.TransformPoint(r2, globalToLocal);
                Float3 l3 = Float4x4.TransformPoint(r3, globalToLocal);

                // Ensure proper winding order
                bool switch03 = false;
                if (Float3.Normalize(l1).Y < Float3.Normalize(l3).Y)
                {
                    switch03 = true;
                    var tmp = l3;
                    l3 = l1;
                    l1 = tmp;
                }

                // Rotate vectors to align
                Float3 rl0 = l0;
                Float3 rl1 = new Float3(l1.Y, -l1.X, l1.Z);
                Float3 rl2 = new Float3(-l2.X, -l2.Y, l2.Z);
                Float3 rl3 = new Float3(-l3.Y, l3.X, l3.Z);

                Float3 average = (rl0 + rl1 + rl2 + rl3) * 0.25f;
                if (uniformLength)
                {
                    average = Float3.Normalize(average) * avg;
                }

                // Rotate back to get target positions
                Float3 lt0 = average;
                Float3 lt1 = new Float3(-average.Y, average.X, average.Z);
                Float3 lt2 = new Float3(-average.X, -average.Y, average.Z);
                Float3 lt3 = new Float3(average.Y, -average.X, average.Z);

                if (switch03)
                {
                    var tmp = lt3;
                    lt3 = lt1;
                    lt1 = tmp;
                }

                // Transform back to global coordinates
                Float3 t0 = Float4x4.TransformPoint(lt0, localToGlobal);
                Float3 t1 = Float4x4.TransformPoint(lt1, localToGlobal);
                Float3 t2 = Float4x4.TransformPoint(lt2, localToGlobal);
                Float3 t3 = Float4x4.TransformPoint(lt3, localToGlobal);

                // Accumulate updates
                pointUpdates[verts[0].Id] += t0 - r0;
                pointUpdates[verts[1].Id] += t1 - r1;
                pointUpdates[verts[2].Id] += t2 - r2;
                pointUpdates[verts[3].Id] += t3 - r3;
                weights[verts[0].Id] += 1;
                weights[verts[1].Id] += 1;
                weights[verts[2].Id] += 1;
                weights[verts[3].Id] += 1;
            }

            // Apply accumulated updates
            i = 0;
            foreach (var v in mesh.Vertices)
            {
                if (weights[i] > 0)
                {
                    v.Point += pointUpdates[i] * (rate / weights[i]);
                }
                ++i;
            }
        }
    }
}
