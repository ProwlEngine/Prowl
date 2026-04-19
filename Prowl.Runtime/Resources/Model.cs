// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A Model asset stores a serialized GameObject hierarchy produced by importing a 3D model.
/// Like PrefabAsset — call Instantiate() to create a live GO tree.
/// Sub-assets (meshes, materials, animations) are tracked by the asset database, not by Model.
/// </summary>
public class Model : EngineObject, ISerializable
{
    /// <summary>Serialized GO hierarchy.</summary>
    public EchoObject? GameObjectData { get; set; }

    public Model() { }
    public Model(string name) { Name = name; }

    /// <summary>
    /// Instantiate the model as a live GameObject hierarchy.
    /// </summary>
    public GameObject? Instantiate()
    {
        if (GameObjectData == null) return null;
        return Serializer.Deserialize<GameObject>(GameObjectData);
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        if (GameObjectData != null)
            value.Add("GameObjectData", GameObjectData.Clone());
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name")?.StringValue ?? "Model";
        GameObjectData = value.Get("GameObjectData")?.Clone();
    }

    /// <summary>Loads a default embedded model (Cube, Sphere, etc.)</summary>
    public static Model LoadDefault(DefaultModel model)
    {
        string fileName = model switch
        {
            DefaultModel.Cube => "Cube.obj",
            DefaultModel.Sphere => "Sphere.obj",
            DefaultModel.Cylinder => "Cylinder.obj",
            DefaultModel.Plane => "Plane.obj",
            DefaultModel.SkyDome => "SkyDome.obj",
            DefaultModel.UnitCube => "1mcube.obj",
            _ => throw new ArgumentException($"Unknown default model: {model}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        var result = new Model(model.ToString());

        // Import via the OBJ importer. Embedded defaults have no companion .mtl, so the
        // resulting GO just has a MeshRenderer with an empty Materials list — callers that
        // use these meshes (e.g. BuiltInAssets, primitive creators) assign their own material.
        var importResult = new AssetImporting.Obj.ObjImporter().Import(stream, fileName);
        if (importResult.RootGO != null)
            result.GameObjectData = Serializer.Serialize(typeof(object), importResult.RootGO);

        result.AssetPath = $"$Default:{model}";
        result.AssetID = BuiltInAssets.GuidFor(model);
        return result;
    }
}
