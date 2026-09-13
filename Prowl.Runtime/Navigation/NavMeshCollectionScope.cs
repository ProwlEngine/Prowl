// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Optional narrowing for <see cref="NavMeshGeometryCollector.Collect(Resources.Scene, LayerMask, int, NavMeshCollectionScope)"/> -
/// which objects contribute geometry, and which of an object's own geometry sources count. The default
/// value (every field left unset) reproduces the collector's original, unscoped behavior exactly: every
/// matching object in the scene, both render meshes and physics colliders - so passing no scope at all
/// (the two/three-argument overloads) never changes for existing callers.
/// </summary>
public struct NavMeshCollectionScope
{
    /// <summary>Which objects are eligible. <see cref="NavMeshCollectObjects.All"/> (the default, 0)
    /// ignores every other field below.</summary>
    public NavMeshCollectObjects CollectObjects;

    /// <summary>Which of an object's own geometry sources count. Null (the default) collects both render
    /// meshes and physics colliders, unconditionally - the collector's original behavior, kept exactly
    /// for any caller not opting into the render-meshes-or-colliders choice.</summary>
    public NavMeshGeometrySource? UseGeometry;

    /// <summary>For <see cref="NavMeshCollectObjects.Children"/>: only this GameObject and its
    /// descendants are eligible.</summary>
    public GameObject? Root;

    /// <summary>For <see cref="NavMeshCollectObjects.Volume"/>: the transform <see cref="VolumeCenter"/>/
    /// <see cref="VolumeSize"/> are local to.</summary>
    public Transform? VolumeTransform;

    /// <summary>For <see cref="NavMeshCollectObjects.Volume"/>: the declared bake volume's center, local
    /// to <see cref="VolumeTransform"/>.</summary>
    public Float3 VolumeCenter;

    /// <summary>For <see cref="NavMeshCollectObjects.Volume"/>: the declared bake volume's size, local to
    /// <see cref="VolumeTransform"/> (rotation and scale honoured).</summary>
    public Float3 VolumeSize;
}
