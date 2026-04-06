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

[AddComponentMenu("Rendering/Model Renderer")]
public class ModelRenderer : MonoBehaviour
{
    public AssetRef<Model> Model;
    public Color MainColor = Color.White;

    // Animation properties
    public AssetRef<AnimationClip> CurrentAnimation;
    public bool PlayAutomatically = true;
    public bool Loop = true;
    public float AnimationSpeed = 10.0f;

    private float _animationTime = 0.0f;
    private bool _isPlaying = false;
    private Pose _currentPose;

    public override void OnEnable()
    {
        if (Model.Res?.IsValid() ?? false)
        {
            // Initialize bind pose if skeleton exists
            if (Model.Res.Skeleton.IsValid())
            {
                _currentPose = Pose.CreateBindPose(Model.Res.Skeleton);
            }

            // Auto-play first animation if requested
            if (PlayAutomatically && Model.Res.Animations.Count > 0)
            {
                CurrentAnimation = Model.Res.Animations[0];
                Play();
            }
        }
    }

    public override void Update()
    {
        // Update animation
        if (_isPlaying && (CurrentAnimation.Res?.IsValid() ?? false))
        {
            _animationTime += Time.DeltaTime * AnimationSpeed;

            if (_animationTime >= CurrentAnimation.Res.Duration)
            {
                if (Loop)
                {
                    _animationTime %= CurrentAnimation.Res.Duration;
                }
                else
                {
                    _animationTime = CurrentAnimation.Res.Duration;
                    _isPlaying = false;
                }
            }

            // Get the current pose from the animation
            _currentPose = CurrentAnimation.Res.GetPose(_animationTime);
        }
    }

    public override void OnRenderCollect()
    {
        if ((!Model.Res?.IsValid()) ?? false) return;

        // Check if skeleton actually has mesh assignments
        bool skeletonHasMeshes = Model.Res.Skeleton.IsValid()
            && Model.Res.Skeleton.Bones.Any(b => b.MeshIndices.Count > 0);

        if (skeletonHasMeshes)
        {
            // Render via skeleton (handles bones + skinning)
            RenderSkeleton(Transform.LocalToWorldMatrix);
        }
        else
        {
            // No skeleton or no mesh assignments — render meshes directly
            RenderMeshesDirect(Transform.LocalToWorldMatrix);
        }
    }

    private void RenderMeshesDirect(Float4x4 worldTransform)
    {
        for (int i = 0; i < Model.Res!.Meshes.Count; i++)
        {
            var modelMesh = Model.Res!.Meshes[i];
            if (modelMesh.Mesh.Res == null || modelMesh.Material.Res == null) continue;

            PropertyState properties = new();
            properties.SetInt("_ObjectID", InstanceID);
            properties.SetColor("_MainColor", MainColor);

            GameObject.Scene.PushRenderable(new MeshRenderable(
                modelMesh.Mesh.Res!,
                modelMesh.Material.Res!,
                worldTransform,
                GameObject.LayerIndex,
                properties));
        }
    }

    public void Play(AnimationClip animation = null)
    {
        if (animation.IsValid())
            CurrentAnimation = animation;

        if (CurrentAnimation.Res != null)
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
        if (CurrentAnimation.Res?.IsValid() ?? false)
            _isPlaying = true;
    }

    private Float4x4[] CalculateBoneMatrices(ModelMesh modelMesh, Float4x4[] boneWorldTransforms)
    {
        if (Model.Res?.Skeleton.IsNotValid() ?? false)
            return null;

        // Calculate skinning matrices using mesh-specific offset matrices
        var mesh = modelMesh.Mesh.Res;
        if (mesh != null && mesh.boneNames != null && mesh.bindPoses != null)
        {
            return Model.Res.Skeleton.CalculateSkinningMatrices(boneWorldTransforms, mesh.boneNames, mesh.bindPoses);
        }

        return null;
    }

    private void RenderSkeleton(Float4x4 worldTransform)
    {
        Skeleton skeleton = Model.Res.Skeleton;

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
                ModelMesh modelMesh = Model.Res.Meshes[meshIndex];

                if (modelMesh.Mesh.Res == null || modelMesh.Material.Res == null) continue;

                {
                    PropertyState properties = new();
                    properties.SetInt("_ObjectID", InstanceID);
                    properties.SetColor("_MainColor", MainColor);

                    Float4x4 meshTransform;

                    if (modelMesh.HasBones)
                    {
                        Float4x4[] boneMatrices = CalculateBoneMatrices(modelMesh, boneWorldTransforms);
                        if (boneMatrices != null && boneMatrices.Length > 0)
                        {
                            properties.SetMatrices("boneTransforms", boneMatrices);
                        }

                        meshTransform = Transform.LocalToWorldMatrix;
                    }
                    else
                    {
                        meshTransform = boneWorldMatrix;
                    }

                    GameObject.Scene.PushRenderable(new MeshRenderable(
                        modelMesh.Mesh.Res!,
                        modelMesh.Material.Res!,
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

        if ((Model.Res?.IsNotValid() ?? true) || Model.Res.Skeleton.IsNotValid())
            return false;

        // Calculate bone world transforms
        Float4x4[] boneWorldTransforms;
        if (_currentPose != null)
        {
            Model.Res.Skeleton.CalculateWorldTransforms(
                _currentPose.LocalPositions,
                _currentPose.LocalRotations,
                _currentPose.LocalScales,
                out boneWorldTransforms);
        }
        else
        {
            // Use bind pose
            Float3[] bindPositions = new Float3[Model.Res.Skeleton.Bones.Count];
            Quaternion[] bindRotations = new Quaternion[Model.Res.Skeleton.Bones.Count];
            Float3[] bindScales = new Float3[Model.Res.Skeleton.Bones.Count];

            for (int i = 0; i < Model.Res.Skeleton.Bones.Count; i++)
            {
                bindPositions[i] = Model.Res.Skeleton.Bones[i].BindPosition;
                bindRotations[i] = Model.Res.Skeleton.Bones[i].BindRotation;
                bindScales[i] = Model.Res.Skeleton.Bones[i].BindScale;
            }

            Model.Res.Skeleton.CalculateWorldTransforms(bindPositions, bindRotations, bindScales, out boneWorldTransforms);
        }

        return RaycastSkeleton(Transform.LocalToWorldMatrix, boneWorldTransforms, ray, ref distance);
    }

    private bool RaycastSkeleton(Float4x4 worldTransform, Float4x4[] boneWorldTransforms, Ray ray, ref float closestDistance)
    {
        bool hit = false;

        // Test all bones
        for (int i = 0; i < Model.Res.Skeleton.Bones.Count; i++)
        {
            Skeleton.Bone bone = Model.Res.Skeleton.Bones[i];

            // Skip bones with no meshes
            if (bone.MeshIndices.Count == 0)
                continue;

            // Calculate bone's world matrix
            Float4x4 boneWorldMatrix = worldTransform * boneWorldTransforms[i];

            // Test all meshes on this bone
            foreach (int meshIndex in bone.MeshIndices)
            {
                ModelMesh modelMesh = Model.Res.Meshes[meshIndex];

                var mesh = modelMesh.Mesh.Res;
                if (mesh == null) continue;

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
        if ((Model.Res?.IsNotValid() ?? true) || Model.Res!.Skeleton.IsNotValid() || _currentPose == null)
            return;

        DrawSkeleton();
    }

    private void DrawSkeleton()
    {
        Float4x4 worldTransform = Transform.LocalToWorldMatrix;
        Skeleton skeleton = Model.Res.Skeleton;

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
