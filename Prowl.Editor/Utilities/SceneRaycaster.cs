// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;
using Prowl.Runtime.SceneManagement;

using Veldrid;

using Shader = Prowl.Runtime.Shader;

namespace Prowl.Editor;

public static class SceneRaycaster
{
    public record MeshHitInfo(GameObject? gameObject, Vector3 worldPosition);


    public static MeshHitInfo? Raycast(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return new MeshHitInfo(EngineObject.FindObjectByID<MeshRenderer>(id)?.GameObject, pos);

        return null;
    }


    public static GameObject? GetObject(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return EngineObject.FindObjectByID<MeshRenderer>(id)?.GameObject;

        return null;
    }


    public static Vector3? GetPosition(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return pos;

        return null;
    }


    public static void RenderTest(Camera camera, Vector2 scale)
    {
        RenderRaycast(camera, new Vector2(scale.x / 2, scale.y / 2), scale, out _, out _);
    }


    private static bool RenderRaycast(Camera camera, Vector2 rayUV, Vector2 screenScale, out Vector3 position, out int objectID)
    {
        camera.UpdateRenderData();
        Ray ray = camera.ScreenPointToRay(rayUV, screenScale);
        List<(double, MeshRenderer)> hits = new();

        MeshRenderer?[] meshRenderers = EngineObject.FindObjectsOfType<MeshRenderer>();
        foreach (MeshRenderer? obj in meshRenderers)
        {
            if (obj is MonoBehaviour mb)
                if (!mb.EnabledInHierarchy) continue;

            obj.GetCullingData(out bool isRenderable, out Bounds bounds);
            if (isRenderable)
            {
                // Ignore bounds the camera is inside of
                //if (bounds.Contains(camera.Transform.position) != ContainmentType.Disjoint)
                //    continue;

                var dist = bounds.Intersects(ray);
                if (dist.HasValue)
                {
                    hits.Add((dist.Value, obj));
                }
            }
        }

        if (hits.Count == 0)
        {
            position = Vector3.zero;
            objectID = -1;
            return false;
        }

        // Find the closest hit
        hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        // Track closest intersection across all meshes
        double closestDistance = double.MaxValue;
        Vector3 closestPosition = Vector3.zero;
        int closestObjectId = -1;
        bool foundAny = false;

        foreach ((double, MeshRenderer) hit in hits)
        {
            // If we've found an intersection and this bound is farther than our closest hit, we can skip
            if (foundAny && hit.Item1 > closestDistance)
                break;

            var mesh = hit.Item2.Mesh.Res;
            if (mesh == null) continue;

            var vertices = mesh.Vertices;

            if (vertices == null || vertices.Length == 0)
                continue;

            int[] indices = mesh.IndexFormat == IndexFormat.UInt16 ?
                mesh.Indices16?.Select(x => (int)x).ToArray() :
                mesh.Indices32?.Select(x => (int)x).ToArray();

            if (indices == null || indices.Length < 3)
                continue;

            Matrix4x4 matrix = hit.Item2.Transform.localToWorldMatrix;

            // Cache transformed vertices to avoid repeated calculations
            var transformedVerts = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                transformedVerts[i] = matrix.MultiplyPoint(vertices[i]);
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 v0 = transformedVerts[indices[i]];
                Vector3 v1 = transformedVerts[indices[i + 1]];
                Vector3 v2 = transformedVerts[indices[i + 2]];

                var distance = ray.Intersects(v0, v1, v2);
                if (distance.HasValue)
                {
                    // Found a closer intersection
                    closestDistance = distance.Value;
                    closestPosition = ray.Position(distance.Value);
                    closestObjectId = hit.Item2.InstanceID;
                    foundAny = true;
                }
            }
        }

        if (foundAny)
        {
            position = closestPosition;
            objectID = closestObjectId;
            return true;
        }

        position = Vector3.zero;
        objectID = -1;
        return false;
    }


    static float GetDepth(Texture2D texture, uint x, uint y, double near, double far)
    {
        float depth;

        switch (texture.Format)
        {
            case PixelFormat.D16_UNorm:
                depth = texture.GetPixel<ushort>(x, y) / 65535.0f; // Untested
                break;

            case PixelFormat.D16_UNorm_S8_UInt:
                depth = texture.GetPixel<(ushort, byte)>(x, y).Item1 / 65535.0f; // Untested
                break;

            case PixelFormat.D24_UNorm_S8_UInt:
                (ushort depth1, byte depth2, _) = texture.GetPixel<(ushort, byte, byte)>(x, y);

                // This does not seem correct - depth2 should not be after depth1. However, it seems to work fine.
                depth = ((uint)depth2 << 16 | depth1) / 16777215.0f;
                break;

            case PixelFormat.D32_Float:
            case PixelFormat.D32_Float_S8_UInt:
                depth = texture.GetPixel<float>(x, y);
                break;

            default:
                throw new Exception($"Unsupported depth format: {texture.Format}");
        }

        return (float)(far * near / (far - (far - near) * depth));
    }
}
