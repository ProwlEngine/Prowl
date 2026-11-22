// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports a <c>.pmesh</c> model document as a prefab: one object per model object, each drawing a mesh sub asset built
/// from its faces. Everything is keyed on the objects' persistent ids, so renaming or reordering objects keeps every
/// reference to them.
/// </summary>
[ImporterFor(PMeshDocument.Extension)]
public class PMeshImporter : AssetImporter
{
    private const int BaseVersion = 1;

    public override int Version => BaseVersion + MeshFeatureRegistry.AggregateVersion;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            PMeshDocument document = PMeshDocument.Read(File.ReadAllBytes(ctx.AbsolutePath));
            EchoObject? settings = ctx.Settings;
            var collider = (PMeshCollider)(settings != null && settings.TryGet(Keys.Collider, out EchoObject? c) ? c!.IntValue : 0);
            bool lightmapUVs = settings != null && settings.TryGet(Keys.LightmapUVs, out EchoObject? l) && l!.BoolValue;

            var root = new GameObject(ctx.FileName);
            root.SetIdentifier(BuiltInAssets.DeterministicGuid("$PMesh/$Root"));

            var objects = new Dictionary<Guid, GameObject>();
            var meshes = new List<Mesh>();
            var meshIdentities = new List<string>();
            foreach (PMeshObject item in document.Objects)
            {
                var go = new GameObject(item.Name);
                go.SetIdentifier(BuiltInAssets.DeterministicGuid($"$PMesh/{item.Id}"));
                objects[item.Id] = go;

                Mesh mesh = BuildMesh(item);
                if (lightmapUVs) LightmapUVGenerator.Generate(mesh);
                meshIdentities.Add(ctx.AddSubAsset(item.Name, mesh, SubAssetIdentity.Key(item.Id.ToString())));
                meshes.Add(mesh);

                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.Identifier = BuiltInAssets.DeterministicGuid($"$PMesh/{item.Id}#Renderer");
                renderer.Mesh = mesh;
                renderer.Materials = Materials(item, ctx);

                if (collider != PMeshCollider.None)
                {
                    MeshCollider shape = go.AddComponent<MeshCollider>();
                    shape.Identifier = BuiltInAssets.DeterministicGuid($"$PMesh/{item.Id}#Collider");
                    shape.Mesh = mesh;
                    shape.Convex = collider == PMeshCollider.Convex;
                }
            }

            // Parents once every object exists, so a child may be written before its parent
            foreach (PMeshObject item in document.Objects)
            {
                GameObject go = objects[item.Id];
                GameObject parent = objects.TryGetValue(item.Parent, out GameObject? p) ? p : root;
                go.SetParent(parent, false);
                go.Transform.LocalPosition = item.Position;
                go.Transform.LocalRotation = item.Rotation;
                go.Transform.LocalScale = item.Scale;
            }

            MeshFeatureImporter.GenerateAll(meshes, ctx.Settings, ctx, [.. meshIdentities]);

            var prefab = new PrefabAsset { Name = ctx.FileName, InstanceType = PrefabInstanceType.Model };
            SerializationContext serialization = ImportHelper.CreateTrackingContext(out HashSet<Guid>? dependencies);
            prefab.GameObjectData = Serializer.Serialize(typeof(object), root, serialization);
            foreach (Guid dependency in dependencies) ctx.Dependencies.Add(dependency);

            ctx.SetMainAsset(prefab);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model document: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// A copy of the object's mesh made drawable: run through its modifiers, corner normals from its edge flags when it
    /// has none or a modifier ran, softened by its smoothing angle when no edge is flagged at all. The mesh cuts its faces
    /// into triangles, ear clipping the concave ones. While dragging, modifiers too slow for every frame are left out.
    /// </summary>
    internal static Mesh BuildMesh(PMeshObject item, bool dragging = false) => BuildMesh(item, dragging, out _);

    /// <summary>
    /// Builds the mesh as <see cref="BuildMesh(PMeshObject, bool)"/> does, giving the index of the object's point each
    /// mesh vertex sits on, or null when a modifier ran and the mesh is no longer the object's points.
    /// </summary>
    internal static Mesh BuildMesh(PMeshObject item, bool dragging, out int[]? sources)
    {
        GeometryData geometry = ModelModifier.Run(item.Mesh, item.Modifiers, dragging);
        bool modified = !ReferenceEquals(geometry, item.Mesh);
        if (!modified)
        {
            geometry = new GeometryData();
            geometry.Restore(item.Mesh.Capture());
        }

        if (modified || !geometry.HasLoopAttribute(MeshGeometry.Normal))
        {
            bool flagged = false;
            foreach (GeometryData.Edge edge in geometry.Edges)
                if (edge.IsSoft || edge.IsSharp) { flagged = true; break; }
            if (!flagged) GeometryOperators.SoftenByAngle(geometry, geometry.Edges, item.SmoothingAngle);
            Operators.CornerNormals(geometry);
        }

        var emitted = new List<GeometryData.Vertex>();
        Mesh mesh = MeshGeometry.ToMesh(geometry, null, modified ? null : emitted);
        mesh.Name = item.Name;
        sources = modified ? null : [.. emitted.Select(v => v.Index)];
        return mesh;
    }

    /// <summary>The object's material slots as material assets, the standard material where a slot has none.</summary>
    private static List<Material> Materials(PMeshObject item, ImportContext ctx)
    {
        var materials = new List<Material>();
        foreach (Guid id in item.Materials)
        {
            if (id != Guid.Empty && AssetDatabase.Get<Material>(id) is { } material)
            {
                ctx.Dependencies.Add(id);
                materials.Add(material);
            }
            else materials.Add(BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard))!);
        }
        if (materials.Count == 0) materials.Add(BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard))!);
        return materials;
    }

    /// <summary>The keys of the import settings in the meta file.</summary>
    public static class Keys
    {
        public const string Collider = "collider";
        public const string LightmapUVs = "generateLightmapUVs";
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s[Keys.Collider] = new EchoObject((int)PMeshCollider.None);
        s[Keys.LightmapUVs] = new EchoObject(false);
        MeshFeatureRegistry.PopulateDefaultSettings(s);
        return s;
    }
}

/// <summary>Which collider each object of an imported model gets.</summary>
public enum PMeshCollider
{
    None,
    Mesh,
    Convex,
}
