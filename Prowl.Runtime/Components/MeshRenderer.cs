// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Rendering/Mesh Renderer")]
public class MeshRenderer : MonoBehaviour, IRenderable
{
    public Mesh Mesh;
    public Material Material;
    public Color MainColor = Color.White;

    private PropertyState _properties = new();

    public override void OnRenderCollect()
    {
        if (Mesh.IsValid() && Material.IsValid())
        {
            _properties.Clear();
            _properties.SetInt("_ObjectID", InstanceID);
            _properties.SetColor("_MainColor", MainColor);
            GameObject.Scene.PushRenderable(this);
        }
    }

    public Material GetMaterial() => Material;
    public int GetLayer() => GameObject.LayerIndex;
    public Float3 GetPosition() => Transform.Position;

    public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh drawData, out Float4x4 model, out InstanceData[]? instanceData)
    {
        drawData = Mesh;
        properties = _properties;
        model = Transform.LocalToWorldMatrix;
        instanceData = null; // Single instance rendering
    }

    public void GetCullingData(out bool isRenderable, out AABB bounds)
    {
        isRenderable = true;
        //bounds = Bounds.CreateFromMinMax(new Vector3(999999), new Vector3(999999));
        bounds = Mesh.bounds.TransformBy(Transform.LocalToWorldMatrix);
    }

    /// <summary>
    /// Raycast against this renderer's mesh in world space.
    /// </summary>
    public bool Raycast(Ray worldRay, out float distance)
    {
        distance = float.MaxValue;
        if (!Mesh.IsValid()) return false;

        // Transform ray to local space
        Float4x4 worldToLocal = Transform.WorldToLocalMatrix;
        Float3 localOrigin = Float4x4.TransformPoint(worldRay.Origin, worldToLocal);
        Float3 localDir = Float3.Normalize(Float4x4.TransformNormal(worldRay.Direction, worldToLocal));
        var localRay = new Ray(localOrigin, localDir);

        // First check AABB
        if (!localRay.Intersects(Mesh.bounds, out _, out _))
            return false;

        return Mesh.Raycast(localRay, out distance);
    }
}
