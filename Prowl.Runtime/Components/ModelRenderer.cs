// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

public class ModelRenderer : MonoBehaviour
{
    public AssetRef<Model> Model;
    public Color mainColor = Color.white;

    public override void Update()
    {
        if (Model.IsAvailable)
        {
            RenderModelNode(Model.Res.RootNode, Transform.localToWorldMatrix);
        }
    }

    private void RenderModelNode(ModelNode node, Matrix4x4 parentMatrix)
    {
        // Calculate this node's world matrix
        var nodeLocalMatrix = Matrix4x4.CreateScale(node.LocalScale) *
                              Matrix4x4.CreateFromQuaternion(node.LocalRotation) *
                              Matrix4x4.CreateTranslation(node.LocalPosition);

        var nodeWorldMatrix = nodeLocalMatrix * parentMatrix;

        // Render all meshes on this node
        foreach (var meshIndex in node.MeshIndices)
        {
            var modelMesh = Model.Res.Meshes[meshIndex];

            if (modelMesh.Material.IsAvailable)
            {
                PropertyState properties = new PropertyState();
                properties.SetInt("_ObjectID", InstanceID);

                RenderPipeline.AddRenderable(new MeshRenderable(
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

    public bool Raycast(Ray ray, out double distance)
    {
        distance = double.MaxValue;

        if (!Model.IsAvailable)
            return false;

        return RaycastModelNode(Model.Res.RootNode, Transform.localToWorldMatrix, ray, ref distance);
    }

    private bool RaycastModelNode(ModelNode node, Matrix4x4 parentMatrix, Ray ray, ref double closestDistance)
    {
        bool hit = false;

        // Calculate this node's world matrix
        var nodeLocalMatrix = Matrix4x4.CreateScale(node.LocalScale) *
                              Matrix4x4.CreateFromQuaternion(node.LocalRotation) *
                              Matrix4x4.CreateTranslation(node.LocalPosition);

        var nodeWorldMatrix = nodeLocalMatrix * parentMatrix;

        // Test all meshes on this node
        foreach (var meshIndex in node.MeshIndices)
        {
            var modelMesh = Model.Res.Meshes[meshIndex];

            if (!modelMesh.Mesh.IsAvailable)
                continue;

            var mesh = modelMesh.Mesh.Res;

            // Transform ray to this mesh's local space
            Matrix4x4.Invert(nodeWorldMatrix, out var worldToLocalMatrix);

            Vector3 localOrigin = Vector3.Transform(ray.origin, worldToLocalMatrix);
            Vector3 localDirection = Vector3.TransformNormal(ray.direction, worldToLocalMatrix);
            Ray localRay = new Ray(localOrigin, localDirection);

            if (mesh.Raycast(localRay, out double localDistance))
            {
                // Calculate world space distance
                Vector3 localHitPoint = localOrigin + localDirection * localDistance;
                Vector3 worldHitPoint = Vector3.Transform(localHitPoint, nodeWorldMatrix);
                double worldDistance = Vector3.Distance(ray.origin, worldHitPoint);

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
