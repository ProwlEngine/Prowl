// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.AssetImporting;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>Builds the default models (Cube, Sphere, ...), generated rather than loaded from files.</summary>
public static class DefaultModels
{
    /// <summary>Generates a default model's mesh, with lightmap UVs so it can be lightmapped as it is.</summary>
    public static Mesh Generate(DefaultModel model)
    {
        Mesh mesh = model switch
        {
            DefaultModel.Cube => Mesh.CreateCube(Float3.One),
            DefaultModel.Sphere => Mesh.CreateSphere(0.5f, 32, 64),
            DefaultModel.Cylinder => Mesh.CreateCylinder(0.5f, 2f, 48),
            DefaultModel.Plane => Mesh.CreatePlane(10f, 10f),
            DefaultModel.Capsule => Mesh.CreateCapsule(0.5f, 2f, 48, 12),
            DefaultModel.Cone => Mesh.CreateCone(0.5f, 2f, 48),
            _ => throw new ArgumentException($"Unknown default model: {model}")
        };
        mesh.Name = model.ToString();
        LightmapUVGenerator.Generate(mesh);
        return mesh;
    }

    /// <summary>A default model as a prefab: one object drawing its built-in mesh, the same as an imported model.</summary>
    public static PrefabAsset Load(DefaultModel model)
    {
        var root = new GameObject(model.ToString());
        // Referenced without loading, as loading never waits on another asset.
        root.AddComponent<MeshRenderer>().Mesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(model));

        return new PrefabAsset
        {
            Name = model.ToString(),
            InstanceType = PrefabInstanceType.Model,
            GameObjectData = Serializer.Serialize(typeof(object), root),
        };
    }
}
