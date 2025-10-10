// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime;

public class ModelRenderer : MonoBehaviour
{
    public Model Model;
    public Color mainColor = Color.white;

    public override void Update()
    {
        if (Model != null)
        {
            RenderModelNode(Model.RootNode, Transform.localToWorldMatrix);
        }
    }

    private void RenderModelNode(ModelNode node, Double4x4 parentMatrix)
    {
        // Calculate this node's world matrix
        //var nodeLocalMatrix = Double4x4.CreateScale(node.LocalScale) *
        //                      Double4x4.CreateFromQuaternion(node.LocalRotation) *
        //                      Double4x4.CreateTranslation(node.LocalPosition);
        var nodeLocalMatrix = Double4x4.CreateTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);

        var nodeWorldMatrix = Maths.Mul(parentMatrix, nodeLocalMatrix);

        // Render all meshes on this node
        foreach (var meshIndex in node.MeshIndices)
        {
            var modelMesh = Model.Meshes[meshIndex];

            if (modelMesh.Material != null)
            {
                PropertyState properties = new PropertyState();
                properties.SetInt("_ObjectID", InstanceID);

                GameObject.Scene.PushRenderable(new MeshRenderable(
                    modelMesh.Mesh,
                    modelMesh.Material,
                    nodeWorldMatrix,
                    this.GameObject.layerIndex,
                    properties));
            }
        }

        // Render child nodes
        foreach (var child in node.Children)
        {
            RenderModelNode(child, nodeWorldMatrix);
        }
    }

    public bool Raycast(RayD ray, out double distance)
    {
        distance = double.MaxValue;

        if (Model == null)
            return false;

        return RaycastModelNode(Model.RootNode, Transform.localToWorldMatrix, ray, ref distance);
    }

    private bool RaycastModelNode(ModelNode node, Double4x4 parentMatrix, RayD ray, ref double closestDistance)
    {
        bool hit = false;

        // Calculate this node's world matrix
        //var nodeLocalMatrix = Double4x4.CreateScale(node.LocalScale) *
        //                      Double4x4.CreateFromQuaternion(node.LocalRotation) *
        //                      Double4x4.CreateTranslation(node.LocalPosition);
        var nodeLocalMatrix = Double4x4.CreateTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);

        var nodeWorldMatrix = Maths.Mul(parentMatrix, nodeLocalMatrix);

        // Test all meshes on this node
        foreach (var meshIndex in node.MeshIndices)
        {
            var modelMesh = Model.Meshes[meshIndex];

            if (modelMesh.Mesh == null)
                continue;

            var mesh = modelMesh.Mesh;

            // Transform ray to this mesh's local space
            var worldToLocalMatrix = nodeWorldMatrix.Invert();

            Double3 localOrigin = Maths.TransformPoint(ray.Origin, worldToLocalMatrix);
            Double3 localDirection = Maths.TransformNormal(ray.Direction, worldToLocalMatrix);
            RayD localRay = new RayD(localOrigin, localDirection);

            if (mesh.Raycast(localRay, out double localDistance))
            {
                // Calculate world space distance
                Double3 localHitPoint = localOrigin + localDirection * localDistance;
                Double3 worldHitPoint = Maths.TransformPoint(localHitPoint, nodeWorldMatrix);
                double worldDistance = Maths.Distance(ray.Origin, worldHitPoint);

                if (worldDistance < closestDistance)
                {
                    closestDistance = worldDistance;
                    hit = true;
                }
            }
        }

        // Test child nodes
        foreach (var child in node.Children)
        {
            if (RaycastModelNode(child, nodeWorldMatrix, ray, ref closestDistance))
                hit = true;
        }

        return hit;
    }
}
