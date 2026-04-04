// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime;

public class ModelRenderer : MonoBehaviour
{
    public Model Model;
    public Color MainColor = Color.White;

    // Animation properties
    public AnimationClip CurrentAnimation;
    public bool PlayAutomatically = true;
    public bool Loop = true;
    public float AnimationSpeed = 10.0f;

    private float _animationTime = 0.0f;
    private bool _isPlaying = false;
    private Pose _currentPose;

    public override void OnEnable()
    {
        if (Model.IsValid())
        {
            // Initialize bind pose if skeleton exists
            if (Model.Skeleton.IsValid())
            {
                _currentPose = Pose.CreateBindPose(Model.Skeleton);
            }

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
        if (_isPlaying && CurrentAnimation.IsValid())
        {
            _animationTime += Time.DeltaTime * AnimationSpeed;

            if (_animationTime >= CurrentAnimation.Duration)
            {
                if (Loop)
                {
                    _animationTime %= CurrentAnimation.Duration;
                }
                else
                {
                    _animationTime = CurrentAnimation.Duration;
                    _isPlaying = false;
                }
            }

            // Get the current pose from the animation
            _currentPose = CurrentAnimation.GetPose(_animationTime);
        }
    }

    public override void OnRenderCollect()
    {
        // Render the model via skeleton
        if (Model.IsValid() && Model.Skeleton.IsValid())
        {
            RenderSkeleton(Transform.LocalToWorldMatrix);
        }
    }

    public void Play(AnimationClip animation = null)
    {
        if (animation.IsValid())
            CurrentAnimation = animation;

        if (CurrentAnimation.IsValid())
        {
            _animationTime = 0.0f;
            _isPlaying = true;
        }
    }

    public void Stop()
    {
        _isPlaying = false;
        _animationTime = 0.0f;
    }

    public void Pause()
    {
        _isPlaying = false;
    }

    public void Resume()
    {
        if (CurrentAnimation.IsValid())
            _isPlaying = true;
    }

    private Float4x4[] CalculateBoneMatrices(ModelMesh modelMesh, Float4x4[] boneWorldTransforms)
    {
        if (Model.Skeleton.IsNotValid())
            return null;

        // Calculate skinning matrices using mesh-specific offset matrices
        if (modelMesh.Mesh.boneNames != null && modelMesh.Mesh.bindPoses != null)
        {
            return Model.Skeleton.CalculateSkinningMatrices(boneWorldTransforms, modelMesh.Mesh.boneNames, modelMesh.Mesh.bindPoses);
        }

        return null;
    }

    private void RenderSkeleton(Float4x4 worldTransform)
    {
        Skeleton skeleton = Model.Skeleton;

        // Calculate world transforms for all bones from the current pose (or bind pose if no animation)
        Float4x4[] boneWorldTransforms;
        if (_currentPose != null)
        {
            skeleton.CalculateWorldTransforms(
                _currentPose.LocalPositions,
                _currentPose.LocalRotations,
                _currentPose.LocalScales,
                out boneWorldTransforms);
        }
        else
        {
            // Use bind pose
            Float3[] bindPositions = new Float3[skeleton.Bones.Count];
            Quaternion[] bindRotations = new Quaternion[skeleton.Bones.Count];
            Float3[] bindScales = new Float3[skeleton.Bones.Count];

            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                bindPositions[i] = skeleton.Bones[i].BindPosition;
                bindRotations[i] = skeleton.Bones[i].BindRotation;
                bindScales[i] = skeleton.Bones[i].BindScale;
            }

            skeleton.CalculateWorldTransforms(bindPositions, bindRotations, bindScales, out boneWorldTransforms);
        }

        // Render each bone's meshes
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            Skeleton.Bone bone = skeleton.Bones[i];

            // Skip bones with no meshes
            if (bone.MeshIndices.Count == 0)
                continue;

            // Calculate bone's world matrix
            Float4x4 boneWorldMatrix = worldTransform * boneWorldTransforms[i];

            // Render all meshes on this bone
            foreach (int meshIndex in bone.MeshIndices)
            {
                ModelMesh modelMesh = Model.Meshes[meshIndex];

                if (modelMesh.Material.IsValid())
                {
                    PropertyState properties = new();
                    properties.SetInt("_ObjectID", InstanceID);
                    properties.SetColor("_MainColor", MainColor);

                    // Determine which transform to use
                    Float4x4 meshTransform;

                    // Add bone matrices for skinned meshes
                    if (modelMesh.HasBones)
                    {
                        Float4x4[] boneMatrices = CalculateBoneMatrices(modelMesh, boneWorldTransforms);
                        if (boneMatrices != null && boneMatrices.Length > 0)
                        {
                            properties.SetMatrices("boneTransforms", boneMatrices);
                        }

                        // Skinned meshes use GameObject's transform since bones handle positioning
                        meshTransform = Transform.LocalToWorldMatrix;
                    }
                    else
                    {
                        // Non-skinned meshes use the bone's world transform
                        meshTransform = boneWorldMatrix;
                    }

                    GameObject.Scene.PushRenderable(new MeshRenderable(
                        modelMesh.Mesh,
                        modelMesh.Material,
                        meshTransform,
                        GameObject.LayerIndex,
                        properties));
                }
            }
        }
    }

    public bool Raycast(Ray ray, out float distance)
    {
        distance = float.MaxValue;

        if (Model.IsNotValid() || Model.Skeleton.IsNotValid())
            return false;

        // Calculate bone world transforms
        Float4x4[] boneWorldTransforms;
        if (_currentPose != null)
        {
            Model.Skeleton.CalculateWorldTransforms(
                _currentPose.LocalPositions,
                _currentPose.LocalRotations,
                _currentPose.LocalScales,
                out boneWorldTransforms);
        }
        else
        {
            // Use bind pose
            Float3[] bindPositions = new Float3[Model.Skeleton.Bones.Count];
            Quaternion[] bindRotations = new Quaternion[Model.Skeleton.Bones.Count];
            Float3[] bindScales = new Float3[Model.Skeleton.Bones.Count];

            for (int i = 0; i < Model.Skeleton.Bones.Count; i++)
            {
                bindPositions[i] = Model.Skeleton.Bones[i].BindPosition;
                bindRotations[i] = Model.Skeleton.Bones[i].BindRotation;
                bindScales[i] = Model.Skeleton.Bones[i].BindScale;
            }

            Model.Skeleton.CalculateWorldTransforms(bindPositions, bindRotations, bindScales, out boneWorldTransforms);
        }

        return RaycastSkeleton(Transform.LocalToWorldMatrix, boneWorldTransforms, ray, ref distance);
    }

    private bool RaycastSkeleton(Float4x4 worldTransform, Float4x4[] boneWorldTransforms, Ray ray, ref float closestDistance)
    {
        bool hit = false;

        // Test all bones
        for (int i = 0; i < Model.Skeleton.Bones.Count; i++)
        {
            Skeleton.Bone bone = Model.Skeleton.Bones[i];

            // Skip bones with no meshes
            if (bone.MeshIndices.Count == 0)
                continue;

            // Calculate bone's world matrix
            Float4x4 boneWorldMatrix = worldTransform * boneWorldTransforms[i];

            // Test all meshes on this bone
            foreach (int meshIndex in bone.MeshIndices)
            {
                ModelMesh modelMesh = Model.Meshes[meshIndex];

                if (modelMesh.Mesh.IsNotValid())
                    continue;

                Mesh mesh = modelMesh.Mesh;

                // Transform ray to this mesh's local space
                Float4x4 worldToLocalMatrix = boneWorldMatrix.Invert();

                Float3 localOrigin = Float4x4.TransformPoint(ray.Origin, worldToLocalMatrix);
                Float3 localDirection = Float4x4.TransformNormal(ray.Direction, worldToLocalMatrix);
                Ray localRay = new(localOrigin, localDirection);

                if (mesh.Raycast(localRay, out float localDistance))
                {
                    // Calculate world space distance
                    Float3 localHitPoint = localOrigin + localDirection * localDistance;
                    Float3 worldHitPoint = Float4x4.TransformPoint(localHitPoint, boneWorldMatrix);
                    float worldDistance = Float3.Distance(ray.Origin, worldHitPoint);

                    if (worldDistance < closestDistance)
                    {
                        closestDistance = worldDistance;
                        hit = true;
                    }
                }
            }
        }

        return hit;
    }



    #region Debug Drawing

    public override void DrawGizmos()
    {
        if (Model.IsNotValid() || Model.Skeleton.IsNotValid() || _currentPose == null)
            return;

        DrawSkeleton();
    }

    private void DrawSkeleton()
    {
        Float4x4 worldTransform = Transform.LocalToWorldMatrix;
        Skeleton skeleton = Model.Skeleton;

        // Calculate world transforms for all bones from the current pose
        skeleton.CalculateWorldTransforms(
            _currentPose.LocalPositions,
            _currentPose.LocalRotations,
            _currentPose.LocalScales,
            out Float4x4[] boneWorldTransforms);

        // Draw each bone
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            Skeleton.Bone bone = skeleton.Bones[i];

            // Transform bone to world space
            Float4x4 boneWorldMatrix = worldTransform * boneWorldTransforms[i];
            Float4 bonePosF4 = boneWorldMatrix.Translation;
            Float3 bonePos = new Float3(bonePosF4.X, bonePosF4.Y, bonePosF4.Z);

            if (bone.ParentID >= 0)
            {
                // Get parent transform in world space
                Float4x4 parentWorldMatrix = worldTransform * boneWorldTransforms[bone.ParentID];
                Float4 parentPosF4 = parentWorldMatrix.Translation;
                Float3 parentPos = new Float3(parentPosF4.X, parentPosF4.Y, parentPosF4.Z);

                // Draw line from parent to this bone
                Debug.DrawLine(parentPos, bonePos, Color.Cyan);

                // Draw bone as a pyramid from parent to this position
                DrawBone(parentPos, bonePos, Color.Yellow);
            }
            else
            {
                // Root bone - just draw a sphere
                Debug.DrawWireSphere(bonePos, 0.03f, Color.Red);
            }

            // Draw a small sphere at each bone position
            Debug.DrawWireSphere(bonePos, 0.02f, Color.Yellow);
        }
    }

    private void DrawBone(Float3 parentPos, Float3 childPos, Color color)
    {
        // Calculate bone direction and length
        Float3 direction = childPos - parentPos;
        float length = Float3.Length(direction);

        if (length < 0.001f)
            return; // Skip zero-length bones

        Float3 normalizedDir = direction / length;

        // Draw a pyramid from parent to child
        // Base at parent, tip at child
        float baseRadius = length * 0.1f; // 10% of bone length

        // Create a perpendicular vector for the base
        Float3 perpendicular = Maths.Abs(normalizedDir.Y) < 0.9f
            ? Float3.Cross(normalizedDir, Float3.UnitY)
            : Float3.Cross(normalizedDir, Float3.UnitX);
        perpendicular = Float3.Normalize(perpendicular);

        Float3 perpendicular2 = Float3.Cross(normalizedDir, perpendicular);

        // Create 4 points around the base
        Float3[] basePoints = new Float3[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f) * Maths.PI / 180f;
            Float3 offset = (perpendicular * Maths.Cos(angle) + perpendicular2 * Maths.Sin(angle)) * baseRadius;
            basePoints[i] = parentPos + offset;
        }

        // Draw pyramid edges from base to tip
        for (int i = 0; i < 4; i++)
        {
            Debug.DrawLine(basePoints[i], childPos, color);
        }

        // Draw base square
        for (int i = 0; i < 4; i++)
        {
            Debug.DrawLine(basePoints[i], basePoints[(i + 1) % 4], color);
        }
    }

    #endregion
}
