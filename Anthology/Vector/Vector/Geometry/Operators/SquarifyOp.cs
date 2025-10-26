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
        private static Double4x4 ComputeLocalAxis(Double3 r0, Double3 r1, Double3 r2, Double3 r3)
        {
            Double3 Z = Double3.Normalize(
                Double3.Normalize(Double3.Cross(r0, r1)) +
                Double3.Normalize(Double3.Cross(r1, r2)) +
                Double3.Normalize(Double3.Cross(r2, r3)) +
                Double3.Normalize(Double3.Cross(r3, r0))
            );
            Double3 X = Double3.Normalize(r0);
            Double3 Y = Double3.Cross(Z, X);

            // Build transformation matrix
            return new Double4x4(
                new Double4(X.X, X.Y, X.Z, 0),
                new Double4(Y.X, Y.Y, Y.Z, 0),
                new Double4(Z.X, Z.Y, Z.Z, 0),
                new Double4(0, 0, 0, 1)
            );
        }

        /// <summary>
        /// Calculate average radius length of all quads in the mesh.
        /// </summary>
        private static double AverageRadiusLength(GeometryData mesh)
        {
            double lengthSum = 0;
            double weightSum = 0;

            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Double3 c = f.Center();
                Double3 r0 = verts[0].Point - c;
                Double3 r1 = verts[1].Point - c;
                Double3 r2 = verts[2].Point - c;
                Double3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Double4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Double3 l0 = Double4x4.TransformPoint(r0, globalToLocal);
                Double3 l1 = Double4x4.TransformPoint(r1, globalToLocal);
                Double3 l2 = Double4x4.TransformPoint(r2, globalToLocal);
                Double3 l3 = Double4x4.TransformPoint(r3, globalToLocal);

                // Rotate vectors to align
                Double3 rl0 = l0;
                Double3 rl1 = new Double3(l1.Y, -l1.X, l1.Z);
                Double3 rl2 = new Double3(-l2.X, -l2.Y, l2.Z);
                Double3 rl3 = new Double3(-l3.Y, l3.X, l3.Z);

                Double3 average = (rl0 + rl1 + rl2 + rl3) * 0.25;

                lengthSum += Double3.Length(average);
                weightSum += 1;
            }

            return weightSum > 0 ? lengthSum / weightSum : 1.0;
        }

        internal static void SquarifyQuads(GeometryData mesh, double rate = 1.0, bool uniformLength = false)
        {
            double avg = uniformLength ? AverageRadiusLength(mesh) : 0;

            var pointUpdates = new Double3[mesh.Vertices.Count];
            var weights = new double[mesh.Vertices.Count];

            // Initialize with rest positions if available
            int i = 0;
            foreach (var v in mesh.Vertices)
            {
                if (mesh.HasVertexAttribute("restpos"))
                {
                    weights[i] = mesh.HasVertexAttribute("weight")
                        ? (v.Attributes["weight"] as GeometryData.FloatAttributeValue)?.Data[0] ?? 1.0
                        : 1.0;

                    var restpos = (v.Attributes["restpos"] as GeometryData.FloatAttributeValue)?.AsVector3() ?? v.Point;
                    pointUpdates[i] = (restpos - v.Point) * weights[i];
                }
                else
                {
                    pointUpdates[i] = Double3.Zero;
                    weights[i] = 0;
                }
                v.Id = i++;
            }

            // Accumulate squarification updates
            foreach (var f in mesh.Faces)
            {
                var verts = f.NeighborVertices();
                if (verts.Count != 4) continue;

                Double3 c = f.Center();
                Double3 r0 = verts[0].Point - c;
                Double3 r1 = verts[1].Point - c;
                Double3 r2 = verts[2].Point - c;
                Double3 r3 = verts[3].Point - c;

                var localToGlobal = ComputeLocalAxis(r0, r1, r2, r3);
                var globalToLocal = Double4x4.Transpose(localToGlobal);

                // Transform to local coordinates
                Double3 l0 = Double4x4.TransformPoint(r0, globalToLocal);
                Double3 l1 = Double4x4.TransformPoint(r1, globalToLocal);
                Double3 l2 = Double4x4.TransformPoint(r2, globalToLocal);
                Double3 l3 = Double4x4.TransformPoint(r3, globalToLocal);

                // Ensure proper winding order
                bool switch03 = false;
                if (Double3.Normalize(l1).Y < Double3.Normalize(l3).Y)
                {
                    switch03 = true;
                    var tmp = l3;
                    l3 = l1;
                    l1 = tmp;
                }

                // Rotate vectors to align
                Double3 rl0 = l0;
                Double3 rl1 = new Double3(l1.Y, -l1.X, l1.Z);
                Double3 rl2 = new Double3(-l2.X, -l2.Y, l2.Z);
                Double3 rl3 = new Double3(-l3.Y, l3.X, l3.Z);

                Double3 average = (rl0 + rl1 + rl2 + rl3) * 0.25;
                if (uniformLength)
                {
                    average = Double3.Normalize(average) * avg;
                }

                // Rotate back to get target positions
                Double3 lt0 = average;
                Double3 lt1 = new Double3(-average.Y, average.X, average.Z);
                Double3 lt2 = new Double3(-average.X, -average.Y, average.Z);
                Double3 lt3 = new Double3(average.Y, -average.X, average.Z);

                if (switch03)
                {
                    var tmp = lt3;
                    lt3 = lt1;
                    lt1 = tmp;
                }

                // Transform back to global coordinates
                Double3 t0 = Double4x4.TransformPoint(lt0, localToGlobal);
                Double3 t1 = Double4x4.TransformPoint(lt1, localToGlobal);
                Double3 t2 = Double4x4.TransformPoint(lt2, localToGlobal);
                Double3 t3 = Double4x4.TransformPoint(lt3, localToGlobal);

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
