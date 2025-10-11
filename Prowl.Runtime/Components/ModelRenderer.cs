// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime;

public class ModelRenderer : MonoBehaviour
{
    public Model Model;
    public Float4 mainColor = Colors.White;

    // Animation properties
    public AnimationClip CurrentAnimation;
    public bool PlayAutomatically = true;
    public bool Loop = true;
    public double AnimationSpeed = 0.1;

    private double _animationTime = 0.0;
    private bool _isPlaying = false;
    private Dictionary<string, ModelNodeTransform> _nodeTransforms = new();
    private Dictionary<string, int> _boneNameToIndex = new();

    private class ModelNodeTransform
    {
        public Double3 Position;
        public Quaternion Rotation;
        public Double3 Scale;
        public Double4x4 LocalMatrix;
        public Double4x4 WorldMatrix;
    }

    public override void OnEnable()
    {
        if (Model != null)
        {
            // Build node transform cache
            BuildNodeTransformCache(Model.RootNode, Double4x4.Identity);

            // Auto-play first animation if requested
            if (PlayAutomatically && Model.Animations.Count > 0)
            {
                CurrentAnimation = Model.Animations[0];
                Play();
            }
        }
    }

    public override void Update()
    {
        // Update animation
        if (_isPlaying && CurrentAnimation != null)
        {
            _animationTime += Time.deltaTimeF * AnimationSpeed;

            if (_animationTime >= CurrentAnimation.Duration)
            {
                if (Loop)
                {
                    _animationTime = _animationTime % CurrentAnimation.Duration;
                }
                else
                {
                    _animationTime = CurrentAnimation.Duration;
                    _isPlaying = false;
                }
            }

            // Evaluate animation and update node transforms
            EvaluateAnimation(CurrentAnimation, _animationTime);
        }

        // Render the model
        if (Model != null)
        {
            RenderModelNode(Model.RootNode, Transform.localToWorldMatrix);
        }
    }

    public void Play(AnimationClip animation = null)
    {
        if (animation != null)
            CurrentAnimation = animation;

        if (CurrentAnimation != null)
        {
            _animationTime = 0.0;
            _isPlaying = true;
        }
    }

    public void Stop()
    {
        _isPlaying = false;
        _animationTime = 0.0;
    }

    public void Pause()
    {
        _isPlaying = false;
    }

    public void Resume()
    {
        if (CurrentAnimation != null)
            _isPlaying = true;
    }

    private void BuildNodeTransformCache(ModelNode node, Double4x4 parentWorldMatrix)
    {
        // Calculate this node's matrices
        var localMatrix = Double4x4.CreateTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
        var worldMatrix = Maths.Mul(parentWorldMatrix, localMatrix);

        _nodeTransforms[node.Name] = new ModelNodeTransform
        {
            Position = node.LocalPosition,
            Rotation = node.LocalRotation,
            Scale = node.LocalScale,
            LocalMatrix = localMatrix,
            WorldMatrix = worldMatrix
        };

        // Recursively process children
        foreach (var child in node.Children)
        {
            BuildNodeTransformCache(child, worldMatrix);
        }
    }

    private void EvaluateAnimation(AnimationClip clip, double time)
    {
        // Reset all transforms to bind pose first
        BuildNodeTransformCache(Model.RootNode, Double4x4.Identity);

        // Apply animation to each bone
        foreach (var bone in clip.Bones)
        {
            if (_nodeTransforms.TryGetValue(bone.BoneName, out var nodeTransform))
            {
                // Evaluate animation curves at current time
                var position = bone.EvaluatePositionAt(time);
                var rotation = bone.EvaluateRotationAt(time);
                var scale = bone.EvaluateScaleAt(time);

                // Update the node transform
                nodeTransform.Position = position;
                nodeTransform.Rotation = rotation;
                nodeTransform.Scale = scale;
                nodeTransform.LocalMatrix = Double4x4.CreateTRS(position, rotation, scale);
            }
        }

        // Recalculate world matrices after animation update
        UpdateWorldMatrices(Model.RootNode, Double4x4.Identity);
    }

    private void UpdateWorldMatrices(ModelNode node, Double4x4 parentWorldMatrix)
    {
        if (_nodeTransforms.TryGetValue(node.Name, out var nodeTransform))
        {
            nodeTransform.WorldMatrix = Maths.Mul(parentWorldMatrix, nodeTransform.LocalMatrix);

            // Recursively update children
            foreach (var child in node.Children)
            {
                UpdateWorldMatrices(child, nodeTransform.WorldMatrix);
            }
        }
    }

    private Float4x4[] CalculateBoneMatrices(ModelMesh modelMesh, Double4x4 meshWorldMatrix)
    {
        if (!modelMesh.HasBones || modelMesh.Mesh.bindPoses == null || modelMesh.Mesh.boneNames == null)
            return null;

        int boneCount = modelMesh.Mesh.bindPoses.Length;
        Float4x4[] boneMatrices = new Float4x4[boneCount];

        // Invert mesh world matrix to get mesh local space
        var meshLocalMatrix = (Float4x4)meshWorldMatrix.Invert();

        // Calculate bone transformation matrices
        for (int i = 0; i < boneCount; i++)
        {
            var boneName = modelMesh.Mesh.boneNames[i];
            var bindPose = modelMesh.Mesh.bindPoses[i];

            // Try to find the bone transform from our cache
            if (_nodeTransforms.TryGetValue(boneName, out var boneTransform))
            {
                // The final bone matrix formula for GPU skinning is:
                // boneMatrix = meshLocalMatrix * boneWorldMatrix * bindPose
                // This transforms from bind pose -> bone space -> world space -> mesh local space
                var boneWorldMatrix = (Float4x4)boneTransform.WorldMatrix;
                boneMatrices[i] = Maths.Mul(Maths.Mul(meshLocalMatrix, boneWorldMatrix), bindPose);
            }
            else
            {
                // If bone not found, use bind pose (no animation)
                boneMatrices[i] = bindPose;
            }
        }

        return boneMatrices;
    }

    private void RenderModelNode(ModelNode node, Double4x4 parentMatrix)
    {
        // Get the node's world matrix (from animation or bind pose)
        Double4x4 nodeWorldMatrix;
        if (_nodeTransforms.TryGetValue(node.Name, out var nodeTransform))
        {
            // Use the animated/cached transform
            nodeWorldMatrix = Maths.Mul(parentMatrix, nodeTransform.LocalMatrix);
        }
        else
        {
            // Fallback to node's original transform
            var nodeLocalMatrix = Double4x4.CreateTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
            nodeWorldMatrix = Maths.Mul(parentMatrix, nodeLocalMatrix);
        }

        // Render all meshes on this node
        foreach (var meshIndex in node.MeshIndices)
        {
            var modelMesh = Model.Meshes[meshIndex];

            if (modelMesh.Material != null)
            {
                PropertyState properties = new PropertyState();
                properties.SetInt("_ObjectID", InstanceID);

                // Add bone matrices for skinned meshes
                if (modelMesh.HasBones)
                {
                    var boneMatrices = CalculateBoneMatrices(modelMesh, nodeWorldMatrix);
                    if (boneMatrices != null && boneMatrices.Length > 0)
                    {
                        // Convert to Double4x4 array for PropertyState
                        var boneMatricesDouble = boneMatrices.Select(m => (Double4x4)m).ToArray();
                        properties.SetMatrices("boneTransforms", boneMatricesDouble);
                    }
                }

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

        // Get the node's world matrix
        Double4x4 nodeWorldMatrix;
        if (_nodeTransforms.TryGetValue(node.Name, out var nodeTransform))
        {
            nodeWorldMatrix = Maths.Mul(parentMatrix, nodeTransform.LocalMatrix);
        }
        else
        {
            var nodeLocalMatrix = Double4x4.CreateTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
            nodeWorldMatrix = Maths.Mul(parentMatrix, nodeLocalMatrix);
        }

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
