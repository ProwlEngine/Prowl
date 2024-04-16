using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities.Memory;
using Prowl.Icons;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Mesh Collider")]
    public class MeshCollider : Collider
    {
        public AssetRef<Mesh> mesh;
    
        public bool convex = false;
        public bool isClosed = true;
    
        public enum Approximation
        {
            Level1 = 6,
            Level2 = 7,
            Level3 = 8,
            Level4 = 9,
            Level5 = 10,
            Level6 = 11,
            Level7 = 12,
            Level8 = 13,
            Level9 = 15,
            Level10 = 20,
            Level15 = 25,
            Level20 = 30
        }
    
        public Approximation convexApprox = Approximation.Level5;
    
        public override void CreateShape()
        {
            if (mesh.IsAvailable == false)
            {
                var meshRenderer = GetComponentInChildren<MeshRenderer>();
                if (meshRenderer != null)
                    mesh = meshRenderer.Mesh;
            }
            if (mesh.IsAvailable == false) return;

            if (!convex)
            {
                Physics.Pool.Take<Triangle>(mesh.Res!.IndexCount / 3, out var triangles);
                for (int i = 0; i < mesh.Res!.IndexCount / 3; ++i)
                {
                    var a = mesh.Res!.Indices[i * 3 + 1];
                    var b = mesh.Res!.Indices[i * 3 + 0];
                    var c = mesh.Res!.Indices[i * 3 + 2];
                    triangles[i] = new Triangle(mesh.Res!.Vertices[a], mesh.Res!.Vertices[b], mesh.Res!.Vertices[c]);
                }

                var meshCollider = CreateGiantMeshFastWithoutBounds(triangles, this.Transform.lossyScale, Physics.Pool);
                shape = meshCollider;
                if(isClosed)
                    bodyInertia = meshCollider.ComputeClosedInertia(mass);
                else
                    bodyInertia = meshCollider.ComputeOpenInertia(mass);
                shapeIndex = Physics.Sim.Shapes.Add(meshCollider);
            }
            else
            {
                var copy = new List<System.Numerics.Vector3>(mesh.Res!.Vertices.Length);
                var s = this.GameObject.Transform.lossyScale.ToFloat();
                foreach (var v in mesh.Res!.Vertices)
                    copy.Add(new System.Numerics.Vector3(v.X * s.X, v.Y * s.Y, v.Z * s.Z));
                var convexShape = new ConvexHull(copy.ToArray(), Physics.Pool, out _);
                shape = convexShape;
                bodyInertia = convexShape.ComputeInertia(mass);
                shapeIndex = Physics.Sim.Shapes.Add(convexShape);
            }
        }

        public unsafe static BepuPhysics.Collidables.Mesh CreateGiantMeshFastWithoutBounds(Buffer<Triangle> triangles, Vector3 scaling, BufferPool pool)
        {
            if (triangles.Length < 128)
            {
                //The special logic isn't necessary for tiny meshes, and we also don't handle the corner case of leaf counts <= 2. Just use the regular constructor.
                return new BepuPhysics.Collidables.Mesh(triangles, scaling, pool);
            }
            var mesh = BepuPhysics.Collidables.Mesh.CreateWithoutTreeBuild(triangles, scaling, pool);
            int leafCounter = 0;
            CreateDummyNodes(ref mesh.Tree, 0, triangles.Length, ref leafCounter);
            for (int i = 0; i < triangles.Length; ++i)
            {
                ref var t = ref triangles[i];
                mesh.Tree.GetBoundsPointers(i, out var min, out var max);
                *min = Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                *max = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
            }
            return mesh;
        }

        static void CreateDummyNodes(ref Tree tree, int nodeIndex, int nodeLeafCount, ref int leafCounter)
        {
            ref var node = ref tree.Nodes[nodeIndex];
            node.A.LeafCount = nodeLeafCount / 2;
            if (node.A.LeafCount > 1)
            {
                node.A.Index = nodeIndex + 1;
                tree.Metanodes[node.A.Index] = new Metanode { IndexInParent = 0, Parent = nodeIndex };
                CreateDummyNodes(ref tree, node.A.Index, node.A.LeafCount, ref leafCounter);
            }
            else
            {
                tree.Leaves[leafCounter] = new Leaf(nodeIndex, 0);
                node.A.Index = Tree.Encode(leafCounter++);
            }
            node.B.LeafCount = nodeLeafCount - node.A.LeafCount;
            if (node.B.LeafCount > 1)
            {
                node.B.Index = nodeIndex + node.A.LeafCount;
                tree.Metanodes[node.B.Index] = new Metanode { IndexInParent = 1, Parent = nodeIndex };
                CreateDummyNodes(ref tree, node.B.Index, node.B.LeafCount, ref leafCounter);
            }
            else
            {
                tree.Leaves[leafCounter] = new Leaf(nodeIndex, 1);
                node.B.Index = Tree.Encode(leafCounter++);
            }
        }

        private List<System.Numerics.Vector3> BuildConvexCloud(List<System.Numerics.Vector3> pointCloud)
        {
            List<System.Numerics.Vector3> allIndices = new();
    
            int steps = (int)convexApprox;
    
            for (int thetaIndex = 0; thetaIndex < steps; thetaIndex++)
            {
                // [0,PI]
                float theta = MathF.PI / (steps - 1) * thetaIndex;
                float sinTheta = (float)Math.Sin(theta);
                float cosTheta = (float)Math.Cos(theta);
    
                for (int phiIndex = 0; phiIndex < steps; phiIndex++)
                {
                    // [-PI,PI]
                    float phi = (2.0f * MathF.PI) / (steps - 0) * phiIndex - MathF.PI;
                    float sinPhi = (float)Math.Sin(phi);
                    float cosPhi = (float)Math.Cos(phi);

                    System.Numerics.Vector3 dir = new(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
    
                    int index = FindExtremePoint(pointCloud, ref dir);
                    allIndices.Add(pointCloud[index]);
                }
            }
    
            return allIndices.Distinct().ToList();
        }
    
        private static int FindExtremePoint(List<System.Numerics.Vector3> points, ref System.Numerics.Vector3 dir)
        {
            int index = 0;
            float current = float.MinValue;

            System.Numerics.Vector3 point; float value;
    
            for (int i = 1; i < points.Count; i++)
            {
                point = points[i];
                value = System.Numerics.Vector3.Dot(point, dir);
                if (value > current) { current = value; index = i; }
            }
    
            return index;
        }
    
    }

}